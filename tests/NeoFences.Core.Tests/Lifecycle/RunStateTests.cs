using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

public class RunStateTests
{
    [Fact]
    public void Normal_WithTakeover_ShowsFences_HidesIcons_WantsTheHook()
    {
        var state = new RunState(HideIcons: true, QuickHidden: false, Paused: false, GameMode: false);
        Assert.True(state.FencesVisible);
        Assert.True(state.IconsHidden);
        Assert.True(state.MouseHookWanted);
        Assert.True(state.PeekHotkeyWanted);
        Assert.False(state.ShellWorkDeferred);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PausedOrGaming_ReleasesThePeekHotkey(bool paused, bool gameMode) =>
        // Ctrl+Alt+Space goes back to Windows and the game (M6a review M5).
        Assert.False(new RunState(HideIcons: true, QuickHidden: false, Paused: paused, GameMode: gameMode).PeekHotkeyWanted);

    [Fact]
    public void QuickHidden_WithoutTakeover_HidesFencesAndIcons()
    {
        var state = new RunState(HideIcons: false, QuickHidden: true, Paused: false, GameMode: false);
        Assert.False(state.FencesVisible);
        Assert.True(state.IconsHidden);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Paused_ShowsIcons_HidesFences_DropsTheHook(bool takeover, bool quickHidden)
    {
        // Pause gives the desktop back to Windows, whatever else is on (hard rule 2 never depends on Pause).
        var state = new RunState(HideIcons: takeover, QuickHidden: quickHidden, Paused: true, GameMode: false);
        Assert.False(state.FencesVisible);
        Assert.False(state.IconsHidden);
        Assert.False(state.MouseHookWanted);
    }

    [Fact]
    public void GameMode_KeepsFencesAndIcons_DropsTheHook_DefersShellWork()
    {
        var state = new RunState(HideIcons: true, QuickHidden: false, Paused: false, GameMode: true);
        Assert.True(state.FencesVisible); // user choice: go idle, fences stay
        Assert.True(state.IconsHidden);
        Assert.False(state.MouseHookWanted);
        Assert.True(state.ShellWorkDeferred);
    }

    // Quick-hide with Takeover off when the user had already hidden the icons in Explorer (M8a review I1).
    [Fact]
    public void QuickHidden_WithIconsTheUserHid_NeverOwnsThem()
    {
        var state = new RunState(HideIcons: false, QuickHidden: true, Paused: false, GameMode: false, IconsHiddenByUser: true);
        Assert.False(state.IconsHidden); // neither hidden by quick-hide, nor shown at its end, exit or an Explorer restart
        Assert.False(state.FencesVisible);
    }

    [Fact]
    public void QuickHidden_WithIconsNeoFencesHid_StillOwnsThem()
    {
        // Icons left hidden by a failed show (the marker is set): not the user's, so the next quick-hide off retries the show.
        Assert.True(new RunState(HideIcons: false, QuickHidden: true, Paused: false, GameMode: false, IconsHiddenByUser: false).IconsHidden);
    }

    [Fact]
    public void Takeover_HidesIcons_WhateverTheUserDidInExplorer() =>
        Assert.True(new RunState(HideIcons: true, QuickHidden: true, Paused: false, GameMode: false, IconsHiddenByUser: true).IconsHidden);
}
