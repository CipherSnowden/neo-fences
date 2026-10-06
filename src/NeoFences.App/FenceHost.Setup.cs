using System.IO;
using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// The whole setup (M36, spec 2026-10-06-fence-settings-and-presets-design §3, ADR-057): Export setup… / Import setup… as
/// one <c>.neofences</c> file, Reset settings to defaults…, and replacing the setup in place (an import, a whole snapshot).
/// </summary>
public sealed partial class FenceHost
{
    private const string SetupFilter = "NeoFences setup (*.neofences)|*.neofences";

    private static string AppVersion => typeof(FenceHost).Assembly.GetName().Version?.ToString(3) ?? "";

    /// <summary>Settings → Snapshots → Export setup…: writes the file; changes nothing.</summary>
    private void ExportSetup()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export setup", Filter = SetupFilter, DefaultExt = SetupFile.Extension, AddExtension = true, OverwritePrompt = true,
            FileName = $"NeoFences setup {DateTime.Now:yyyy-MM-dd}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if ((_settingsWindow is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) != true) return;
        try
        {
            // ponytail: written on the UI thread (a few MB of covers at most); a background write if big setups ever stutter.
            var missing = SetupFile.Write(dialog.FileName, _config, _items, AppPaths.DataDirectory, AppVersion, DateTimeOffset.Now);
            if (missing.Count > 0) Log.Warning("setup export: {Count} picture(s) were gone and are not in the file", missing.Count);
            Log.Information("setup exported to {Path}", dialog.FileName);
            _settingsWindow?.ShowSnapshotNotice($"Exported to \"{Path.GetFileName(dialog.FileName)}\".", failed: false);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Warning(failure, "setup could not be exported to {Path}", dialog.FileName);
            _settingsWindow?.ShowSnapshotNotice("Not exported: NeoFences could not write the file (see the log).", failed: true);
        }
    }

    /// <summary>
    /// Settings → Snapshots → Import setup…: the file is checked, the owner asked, the current setup saved as a whole snapshot,
    /// then the file's setup and pictures go in and the fences reload where they are. Restoring "Before import" undoes it.
    /// </summary>
    private void ImportSetup()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import setup", Filter = SetupFilter, CheckFileExists = true };
        if ((_settingsWindow is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) != true) return;
        SetupRead read;
        try
        {
            read = SetupFile.Read(dialog.FileName);
        }
        catch (SetupFileException problem)
        {
            Log.Warning(problem.InnerException, "setup {Path} not imported: {Reason}", dialog.FileName, problem.Message);
            MessageDialog.Tell(_settingsWindow, "Setup not imported", problem.Message);
            return;
        }
        if (!MessageDialog.Ask(_settingsWindow, "Replace your fences, items and settings with this file's?",
                "Your current setup is saved as a snapshot first.", primary: "Replace", secondary: "Cancel")) return;
        if (!SaveWholeSnapshot("Before import", failureTitle: "Setup not imported")) return;
        foreach (var (picture, failure) in SetupFile.PlacePictures(read, AppPaths.DataDirectory))
            Log.Warning(failure, "setup import: picture {Folder}/{File} could not be written; the item shows its usual icon", picture.Folder, picture.FileName);
        Log.Information("importing the setup from {Path} (NeoFences {Version}, {Fences} fence(s))", dialog.FileName, read.Manifest.AppVersion, read.Config.Fences.Count);
        ReplaceSetup(read.Config, read.Items, whole: true);
        _settingsWindow?.ShowSnapshotNotice($"Imported \"{Path.GetFileName(dialog.FileName)}\". To undo, restore the snapshot \"Before import\".", failed: false);
    }

    /// <summary>Settings → About → Reset settings to defaults…: asked, a whole snapshot first; fences, items and presets stay.</summary>
    private void ResetSettings()
    {
        if (!MessageDialog.Ask(_settingsWindow, "Reset every setting to its default?",
                "Your fences, items, own presets, game folders, hidden games and chosen covers stay. A snapshot is taken first, so restoring it undoes this.",
                primary: "Reset", secondary: "Cancel")) return;
        if (!SaveWholeSnapshot("Before reset", failureTitle: "Settings not reset")) return;
        var reset = SettingsReset.Apply(_config);
        _config = _config with { Library = reset.Library };
        Log.Information("settings reset to their defaults");
        ApplySettings(reset.Settings);
        UpdateLibrary();
        ScanLibrary(); // every launcher is read again
    }

    /// <summary>A snapshot of the whole setup (Settings and presets too); false, with the failure shown, when it could not be saved.</summary>
    private bool SaveWholeSnapshot(string name, string failureTitle)
    {
        var now = DateTimeOffset.Now;
        if (_snapshots.Save(Snapshots.TakeWhole(_config, _items, name: $"{name} ({now:d MMM HH:mm})", now: now)) is not null) return true;
        Log.Warning(_snapshots.LastFailure, "{Name}: the snapshot could not be saved; nothing changed", name);
        SnapshotFailure(failureTitle, "NeoFences could not save a snapshot first (see the log). Nothing changed.");
        return false;
    }

    /// <summary>
    /// A new setup in place (a snapshot restore; M36: an import, a whole snapshot): the fences reload where they are, their
    /// items and looks follow, and with <paramref name="whole"/> the Settings apply one by one (each with its effect) and the
    /// games are scanned again.
    /// </summary>
    private void ReplaceSetup(NeoFencesConfig config, ItemsDocument items, bool whole)
    {
        _undo = null; // M33: everything was replaced; nothing older to undo
        var settings = config.Settings;
        _config = config with { Settings = _config.Settings };
        _items = items;
        MigrateGames(_library.Items.Count > 0 ? _library : LibraryWriter.ReadIndex(AppPaths.LibraryDirectory)); // M22: a setup from before games became items
        MigrateFolderViews(); // M26: a setup from before folder views became panels
        SaveNow();
        SyncBoxes();
        // Windows that kept their fence still show its old title, icon size and labels (M10 final review I1).
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
            window.Refresh(shown);
            window.SetTitle(shown.Title);
        }
        RefreshWindows();
        RestyleAll(); // M36: their own looks
        UpdateLibrary(); // M22: the fences may hold game items, or none
        ForgetGoneTargets(); // records of items that went (M20)
        CheckAllTargets(); // the items' targets may have changed since
        if (whole)
        {
            if (settings != _config.Settings) ApplySettings(settings);
            ScanLibrary(); // other game folders or launchers
        }
        RefreshAllFenceSettings(); // fences that are gone close their settings
        RefreshSettings();
    }

    /// <summary>
    /// Settings replaced as a whole (M36: an import, a reset, a whole snapshot): each setting through its own path, so its effect
    /// follows (the startup entry, the desktop icons, the hotkey, the mouse hook, the look). Saved at once.
    /// </summary>
    private void ApplySettings(Settings target)
    {
        var current = _config.Settings;
        if (target.StartWithWindows != current.StartWithWindows) SetStartWithWindows(target.StartWithWindows);
        if (target.HideDesktopIcons != current.HideDesktopIcons) SetHideDesktopIcons(target.HideDesktopIcons);
        if (target.QuickHideGesture != current.QuickHideGesture || target.DrawGesture != current.DrawGesture)
            SetGestures(quickHide: target.QuickHideGesture, draw: target.DrawGesture);
        if (target.RollupExpand != current.RollupExpand) SetRollupExpand(target.RollupExpand);
        if (target.GameMode != current.GameMode) SetGameModeEnabled(target.GameMode);
        if (target.AutoUpdate != current.AutoUpdate) SetAutoUpdate(target.AutoUpdate);
        if (target.Appearance != current.Appearance) SetAppearance(target.Appearance);
        if (target.ShowShortcutArrows != current.ShowShortcutArrows)
        {
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetShortcutArrows(target.ShowShortcutArrows);
        }
        if (target.PeekHotkey != current.PeekHotkey && SetPeekHotkey(target.PeekHotkey) is (false, var problem))
            Log.Warning("peek hotkey {Hotkey} not taken: {Problem}; the old one stays", target.PeekHotkey, problem);
        _config = _config with { Settings = _config.Settings with { ShowShortcutArrows = target.ShowShortcutArrows, DefaultLabels = target.DefaultLabels } };
        SaveNow();
        RefreshSettings();
    }
}
