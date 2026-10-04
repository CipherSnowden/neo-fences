using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>M19 final review fixes: shared root probes, app answers, opening shell targets.</summary>
public class ProbeAndLaunchTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public void ProbeWait_OnlyWhatIsLeftOfTheFirstCallersTimeout()
    {
        // A dead share: 20 items asked one after another wait 2 s together, not 2 s each (review I1).
        Assert.Equal(Timeout, TargetChecks.ProbeWait(Start, Start, Timeout));
        Assert.Equal(TimeSpan.FromMilliseconds(500), TargetChecks.ProbeWait(Start, Start.AddMilliseconds(1500), Timeout));
        Assert.Equal(TimeSpan.Zero, TargetChecks.ProbeWait(Start, Start.AddSeconds(2), Timeout));
        Assert.Equal(TimeSpan.Zero, TargetChecks.ProbeWait(Start, Start.AddSeconds(30), Timeout)); // still pending: no wait at all
        Assert.Equal(Timeout, TargetChecks.ProbeWait(Start, Start.AddSeconds(-1), Timeout)); // a clock step back never waits longer
    }

    [Theory]
    [InlineData(unchecked((int)0x80070002), true)]  // ERROR_FILE_NOT_FOUND: Windows has no such app (seen for a made-up id)
    [InlineData(unchecked((int)0x80070003), true)]  // ERROR_PATH_NOT_FOUND
    [InlineData(unchecked((int)0x80004005), false)] // E_FAIL: the app resolver not ready (sign-in), a Store app mid-update
    [InlineData(unchecked((int)0x8001010E), false)] // RPC_E_WRONG_THREAD and other transient answers
    [InlineData(0, false)]
    public void AnAppIsUninstalled_OnlyWhenWindowsSaysNotFound(int hresult, bool uninstalled) =>
        Assert.Equal(uninstalled, TargetChecks.AppIsGone(hresult));

    [Theory]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", @"""shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App""")]
    [InlineData(@"shell:AppsFolder\{6D809377-6AF0-444B-8957-A3773F02200E}\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe",
        @"""shell:AppsFolder\{6D809377-6AF0-444B-8957-A3773F02200E}\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe""")]
    [InlineData("::{645FF040-5081-101B-9F08-00AA002F954E}", @"""shell:::{645FF040-5081-101B-9F08-00AA002F954E}""")]
    [InlineData(@"shell:Downloads", @"""shell:Downloads""")]
    public void ExplorerArgument_IsQuoted_SoSpacesAndCommasStayInOneTarget(string target, string argument) =>
        Assert.Equal(argument, ItemKinds.ExplorerArgument(target));
}
