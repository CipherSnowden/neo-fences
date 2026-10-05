namespace NeoFences.Core.Lifecycle;

/// <summary>
/// The run-time modes of NeoFences and what they mean together (M5 quick-hide, M6a Pause and game mode, ADR-021).
/// None of them is saved: after a restart only "Hide desktop icons" (a setting) applies again.
/// </summary>
/// <param name="HideIcons">Settings → "Hide desktop icons while NeoFences runs" (M18).</param>
/// <param name="IconsHiddenByUser">Hide icons off and the icons were already hidden in Explorer when quick-hide began:
/// quick-hide neither hides nor shows them (M8a review I1).</param>
public sealed record RunState(bool HideIcons, bool QuickHidden, bool Paused, bool GameMode, bool IconsHiddenByUser = false)
{
    /// <summary>Safe mode after a crash loop (M33, ADR-053): fences only; icons never hidden, no hook, no extras.</summary>
    public bool SafeMode { get; init; }

    /// <summary>Settings → "Double-click the desktop to quick-hide" (M33, on by default).</summary>
    public bool QuickHideGesture { get; init; } = true;

    /// <summary>Settings → "Right-drag on the desktop to draw a fence" (M33, on by default).</summary>
    public bool DrawGesture { get; init; } = true;

    public bool FencesVisible => !Paused && !QuickHidden;

    /// <summary>Pause gives the desktop back to Windows: its icons show even with "Hide desktop icons" on (spec §6).</summary>
    public bool IconsHidden => !Paused && !SafeMode && (HideIcons || (QuickHidden && !IconsHiddenByUser));

    /// <summary>
    /// The only global hook goes away while paused or gaming (hard rule 3, spec §4.7), in safe mode, and when both desktop
    /// gestures are switched off (M33).
    /// </summary>
    public bool MouseHookWanted => !Paused && !GameMode && !SafeMode && (QuickHideGesture || DrawGesture);

    /// <summary>Widgets, folder panels, auto-collect, the library scan and wallpaper watching: all off in safe mode (M33).</summary>
    public bool ExtrasWanted => !SafeMode;

    /// <summary>Ctrl+Alt+Space (or the chosen Peek hotkey) is released to Windows and the game while paused or gaming.</summary>
    public bool PeekHotkeyWanted => !Paused && !GameMode;

    /// <summary>Target re-checks, watch events and library scans wait until the game is left (spec §4.7).</summary>
    public bool ShellWorkDeferred => GameMode;
}
