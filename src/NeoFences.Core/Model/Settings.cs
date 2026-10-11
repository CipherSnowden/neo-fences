namespace NeoFences.Core.Model;

public enum RollupExpand { Hover, Click }

public sealed record Settings
{
    /// <summary>
    /// "Hide desktop icons while NeoFences runs" (M18, off by default): Windows' own setting, shown again on exit, crash and
    /// Task Manager kill (the watchdog, hard rule 2).
    /// </summary>
    public bool HideDesktopIcons { get; init; }
    public string PeekHotkey { get; init; } = "Ctrl+Alt+Space";
    public bool StartWithWindows { get; init; } = true;
    public bool GameMode { get; init; } = true;
    public RollupExpand RollupExpand { get; init; } = RollupExpand.Hover;
    /// <summary>Labels for new fences (M8b); no UI since rc.4 — read once by <see cref="AllFences.Introduce"/>.</summary>
    public LabelMode DefaultLabels { get; init; } = LabelMode.Always;
    /// <summary>The small arrow Windows draws on shortcut icons (M8b, user choice: a setting, off by default).</summary>
    public bool ShowShortcutArrows { get; init; }

    /// <summary>Settings → General: "Double-click the desktop to quick-hide" (M33, on by default).</summary>
    public bool QuickHideGesture { get; init; } = true;

    /// <summary>Settings → General: "Right-drag on the desktop to draw a fence" (M33, on by default).</summary>
    public bool DrawGesture { get; init; } = true;

    /// <summary>Settings → General: "Open items with" (M41a, ADR-064): one click by default; a fence may choose its own.</summary>
    public OpenWith OpenItemsWith { get; init; }

    /// <summary>Settings → Appearance: "Fence menu" (M41a): Guided (sections) by default, or Compact (an icon row).</summary>
    public FenceMenuStyle FenceMenu { get; init; }

    /// <summary>Settings → General: "Click wallpaper to reveal desktop" (M41a, ADR-066, off by default; macOS-style).</summary>
    public bool RevealGesture { get; init; }
    /// <summary>Settings → Appearance (M14): background strength, colour style, wallpaper accent, title font.</summary>
    public AppearanceSettings Appearance { get; init; } = new();
    /// <summary>Download updates from GitHub by themselves (M17); off: NeoFences makes no network calls at all.</summary>
    public bool AutoUpdate { get; init; } = true;

    /// <summary>M41a: the one-time "New: fence themes" note was shown (a new install never needs it).</summary>
    public bool ThemesIntroduced { get; init; }

    /// <summary>M41b: icon sizes and labels moved to All fences once (<see cref="AllFences.Introduce"/>); a new install never needs it.</summary>
    public bool AllFencesIntroduced { get; init; }

    /// <summary>
    /// M41a: the settings NeoFences comes with — a new install, and Settings → Reset settings (Aurora glass and Clean titles;
    /// a setup from before rc.3 keeps the record's defaults, the look it had).
    /// </summary>
    public static Settings NewInstall { get; } = new() { Appearance = AppearanceSettings.NewInstall, ThemesIntroduced = true, AllFencesIntroduced = true };
}
