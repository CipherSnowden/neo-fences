namespace NeoFences.Core.Model;

public enum RollupExpand { Hover, Click }

public sealed record Settings
{
    /// <summary>Hide native desktop icons and show them in fences. Off by default until the M2 sign-out test passes (ADR-011).</summary>
    public bool Takeover { get; init; }
    /// <summary>The user answered the one-time "hide desktop icons?" banner in the Inbox (M2b first run).</summary>
    public bool TakeoverPromptAnswered { get; init; }
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
