using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

public class GameModePolicyTests
{
    private static ForegroundSnapshot Foreground(NotificationState notifications, string windowClass = "ScimitarEngineWindowClass", bool isOwnProcess = false) =>
        new(notifications, windowClass, isOwnProcess);

    [Theory]
    [InlineData(NotificationState.Busy)]                 // borderless full screen (the M0 game)
    [InlineData(NotificationState.RunningD3DFullScreen)] // exclusive full screen
    public void FullScreenApp_InFront_IsAGame(NotificationState notifications) =>
        Assert.True(GameModePolicy.IsGameActive(enabled: true, Foreground(notifications)));

    [Theory]
    [InlineData(NotificationState.AcceptsNotifications)] // a normal maximized app or browser
    [InlineData(NotificationState.NotPresent)]           // lock screen (M0 finding 5)
    [InlineData(NotificationState.PresentationMode)]
    [InlineData(NotificationState.QuietTime)]
    [InlineData(NotificationState.Unknown)]              // the query failed
    public void OtherStates_AreNotAGame(NotificationState notifications) =>
        Assert.False(GameModePolicy.IsGameActive(enabled: true, Foreground(notifications)));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("")] // no foreground window
    public void BusyButDesktopOrTaskbarInFront_IsNotAGame(string windowClass) =>
        Assert.False(GameModePolicy.IsGameActive(enabled: true, Foreground(NotificationState.Busy, windowClass)));

    [Fact]
    public void BusyButNeoFencesInFront_IsNotAGame() =>
        Assert.False(GameModePolicy.IsGameActive(enabled: true, Foreground(NotificationState.Busy, "HwndWrapper[NeoFences;;1]", isOwnProcess: true)));

    [Fact]
    public void GameModeOff_NeverActivates() =>
        Assert.False(GameModePolicy.IsGameActive(enabled: false, Foreground(NotificationState.RunningD3DFullScreen)));
}
