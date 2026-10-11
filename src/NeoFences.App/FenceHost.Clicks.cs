using System.Windows;
using NeoFences.Core.Model;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// M41a (spec 2026-10-11-m41a-feel-design §4–5, ADR-064): one click opens (Settings and each fence's own choice), and the
/// fence menu's shape.
/// </summary>
public sealed partial class FenceHost
{
    private void SetOpenItemsWith(OpenWith openWith)
    {
        _config = _config with { Settings = _config.Settings with { OpenItemsWith = openWith } };
        Log.Information("open items with: {OpenWith}", openWith);
        RestyleAll(); // each window takes its fence's choice (ApplyStyle)
        ScheduleSave();
        RefreshSettings();
        RefreshAllFenceSettings(); // "Like all fences (…)" names the new default
    }

    private void SetFenceMenu(FenceMenuStyle style)
    {
        _config = _config with { Settings = _config.Settings with { FenceMenu = style } };
        Log.Information("fence menu: {Style}", style);
        RestyleAll();
        ScheduleSave();
        RefreshSettings();
    }

    /// <summary>
    /// M41a (spec §1): a setup from before rc.3 keeps its look, so once — not in a game — the top-left fence says the themes
    /// are there, with a button to Fence settings. A new install starts with Aurora and never sees it.
    /// </summary>
    private void IntroduceThemes()
    {
        if (_config.Settings.ThemesIntroduced || _gameMode || SafeMode || !Current.FencesVisible) return;
        if (_windows.Values.Where(window => window.IsVisible).OrderBy(window => window.Left + window.Top).FirstOrDefault() is not { } first) return;
        first.ShowTip("New: fence themes — Aurora glass, Game HUD, Neon, Light Mica", "Try them", () => OpenFenceSettings(first.FenceId));
        _config = _config with { Settings = _config.Settings with { ThemesIntroduced = true } };
        Log.Information("themes introduced");
        ScheduleSave();
    }

    /// <summary>What a window needs besides its look: how its items open, its menu's shape, the names' style and the motion.</summary>
    private void ApplyFeel(FenceWindow window, Fence shown)
    {
        window.OneClickOpens = Clicks.OneClickOpens(shown, _config.Settings);
        window.MenuStyle = _config.Settings.FenceMenu;
        window.NameStyle = Appearance.NameStyle;
        window.Motion = EffectiveMotion;
    }

    /// <summary>M41a (spec §3): Settings' motion, off in a game, in safe mode and with Windows' animation effects off.</summary>
    private Motion EffectiveMotion =>
        MotionPolicy.Effective(Appearance.Motion, gameMode: _gameMode, windowsAnimations: SystemParameters.ClientAreaAnimation, safeMode: SafeMode);
}
