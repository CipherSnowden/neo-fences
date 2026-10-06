using System.Windows;
using NeoFences.Core.Model;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Fence settings… (M36, spec 2026-10-06-fence-settings-and-presets-design §1–2): one window per fence (a tab has its own).
/// Every change applies to the fence at once and is saved; the window shows the result back.
/// </summary>
public sealed partial class FenceHost
{
    private readonly Dictionary<string, FenceSettingsWindow> _fenceSettings = new(StringComparer.Ordinal);

    private void OpenFenceSettings(string fenceId)
    {
        if (_fenceSettings.TryGetValue(fenceId, out var open))
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        if (_config.Fences.All(fence => fence.Id != fenceId)) return;
        var window = new FenceSettingsWindow(fenceId);
        window.LookChanged += look => ChangeFence(fenceId, config => FenceEdits.SetLook(config, fenceId, look));
        window.PresetChosen += preset => ChangeFence(fenceId, config => LookPresets.Apply(config, fenceId, preset));
        window.LikeAllChosen += () => ChangeFence(fenceId, config => LookPresets.LikeAllFences(config, fenceId));
        window.IconSizeChanged += iconSize => ChangeFence(fenceId, config => FenceEdits.SetIconSize(config, fenceId, iconSize));
        window.LabelsChanged += labels => ChangeFence(fenceId, config => FenceEdits.SetLabels(config, fenceId, labels));
        window.LayoutChanged += layout =>
        {
            // The shown tab takes the menu's path (a Free layout pins the cells where the items are now).
            if (WindowShowing(fenceId) is { } shown) SetFenceLayout(shown, layout);
            else ChangeFence(fenceId, config => FenceEdits.SetLayout(config, fenceId, layout));
            RefreshFenceSettings(fenceId);
        };
        window.PresetSaved += name =>
        {
            _config = LookPresets.Save(_config, fenceId, name);
            Log.Information("look preset saved: {Name}", name);
            ScheduleSave();
            RefreshAllFenceSettings(); // every open window lists the presets
        };
        window.PresetDeleted += name =>
        {
            _config = LookPresets.Delete(_config, name);
            Log.Information("look preset deleted: {Name}", name);
            ScheduleSave();
            RefreshAllFenceSettings();
        };
        window.Activated += (_, _) => RefreshFenceSettings(fenceId); // the fence menu may have changed the fence meanwhile
        window.Closed += (_, _) => _fenceSettings.Remove(fenceId);
        _fenceSettings[fenceId] = window;
        RefreshFenceSettings(fenceId);
        window.Show();
        window.Activate();
    }

    /// <summary>One change to a fence from its settings: applied to the fence window showing it (if any), saved, shown back.</summary>
    private void ChangeFence(string fenceId, Func<NeoFencesConfig, NeoFencesConfig> change)
    {
        if (_config.Fences.All(fence => fence.Id != fenceId)) return; // deleted meanwhile
        _config = change(_config);
        if (WindowShowing(fenceId) is { } window)
        {
            window.Refresh(_config.Fences.First(fence => fence.Id == fenceId));
            ApplyStyle(window);
        }
        ScheduleSave();
        RefreshFenceSettings(fenceId);
    }

    /// <summary>The fence window whose shown tab is this fence, or null (another tab of its box is in front).</summary>
    private FenceWindow? WindowShowing(string fenceId) => _windows.Values.FirstOrDefault(window => window.FenceId == fenceId);

    /// <summary>Shows the fence as it is now; a fence that is gone (deleted, a snapshot restored) closes its window.</summary>
    private void RefreshFenceSettings(string fenceId)
    {
        if (!_fenceSettings.TryGetValue(fenceId, out var window)) return;
        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { } shown)
        {
            window.Close();
            return;
        }
        window.Show(new FenceSettingsView(shown, Appearance, _lightTheme, LookPresets.All(_config), LookPresets.Matching(_config, shown)));
    }

    private void RefreshAllFenceSettings()
    {
        foreach (var fenceId in _fenceSettings.Keys.ToList()) RefreshFenceSettings(fenceId);
    }
}
