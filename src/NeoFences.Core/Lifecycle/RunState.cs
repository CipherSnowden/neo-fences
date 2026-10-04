namespace NeoFences.Core.Lifecycle;

/// <summary>
/// The run-time modes of NeoFences and what they mean together (M5 quick-hide, M6a Pause and game mode, ADR-021).
/// None of them is saved: after a restart only Takeover (a setting) applies again.
/// </summary>
/// <param name="IconsHiddenByUser">Takeover off and the icons were already hidden in Explorer when quick-hide began: quick-hide
/// neither hides nor shows them (M8a review I1).</param>
public sealed record RunState(bool Takeover, bool QuickHidden, bool Paused, bool GameMode, bool IconsHiddenByUser = false)
{
    public bool FencesVisible => !Paused && !QuickHidden;

    /// <summary>Pause gives the desktop back to Windows: its icons show even with Takeover on (spec §6).</summary>
    public bool IconsHidden => !Paused && (Takeover || (QuickHidden && !IconsHiddenByUser));

    /// <summary>The only global hook goes away while paused or gaming (hard rule 3, spec §4.7).</summary>
    public bool MouseHookWanted => !Paused && !GameMode;

    /// <summary>Ctrl+Alt+Space (or the chosen Peek hotkey) is released to Windows and the game while paused or gaming.</summary>
    public bool PeekHotkeyWanted => !Paused && !GameMode;

    /// <summary>Desktop and Portal changes wait until the game is left (spec §4.7).</summary>
    public bool ShellWorkDeferred => GameMode;
}
