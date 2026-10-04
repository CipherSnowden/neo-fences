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
    /// <summary>Labels for new fences (M8b; Settings → Fences).</summary>
    public LabelMode DefaultLabels { get; init; } = LabelMode.Always;
    /// <summary>The small arrow Windows draws on shortcut icons (M8b, user choice: a setting, off by default).</summary>
    public bool ShowShortcutArrows { get; init; }
    /// <summary>Settings → Appearance (M14): background strength, colour style, wallpaper accent, title font.</summary>
    public AppearanceSettings Appearance { get; init; } = new();
    /// <summary>Download updates from GitHub by themselves (M17); off: NeoFences makes no network calls at all.</summary>
    public bool AutoUpdate { get; init; } = true;
}
