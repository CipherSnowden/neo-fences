using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

/// <summary>Start with Windows (ADR-019, ADR-023): after a power loss NeoFences must come back by itself to restore the desktop.</summary>
public class StartupPolicyTests
{
    private const string Temp = @"C:\Users\cipher\AppData\Local\Temp\";
    private const string DevBuild = @"F:\projects\neo_fences\src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe";
    private const string InstallRoot = @"C:\Users\cipher\AppData\Local\NeoFences.App";
    private const string Installed = InstallRoot + @"\current\NeoFences.exe";

    /// <summary>A file system with the Velopack install in place (Update.exe and the installed exe exist).</summary>
    private static bool WithInstall(string path) =>
        path.Equals(InstallRoot + @"\Update.exe", StringComparison.OrdinalIgnoreCase) || path.Equals(Installed, StringComparison.OrdinalIgnoreCase)
        || path.Equals(DevBuild, StringComparison.OrdinalIgnoreCase);

    private static bool WithoutInstall(string path) => path.Equals(DevBuild, StringComparison.OrdinalIgnoreCase);

    private static string? Target(bool startWithWindows, string exePath, string? entry, Func<string, bool> fileExists) =>
        StartupPolicy.SignInTarget(startWithWindows: startWithWindows, exePath: exePath, tempFolder: Temp, currentRunCommand: entry,
            defaultInstalledExe: Installed, fileExists: fileExists);

    [Fact]
    public void Enabled_DevBuildWithNoEntry_NothingInstalled_RegistersItself() =>
        Assert.Equal(DevBuild, Target(true, DevBuild, entry: null, WithoutInstall));

    [Fact]
    public void Disabled_RegistersNothing() =>
        Assert.Null(Target(false, Installed, entry: null, WithInstall));

    [Fact]
    public void ABuildRunningFromTheTempFolder_NeverRegistersItself()
    {
        // A test build there is deleted later; sign-in would then start nothing and the icons would stay hidden.
        Assert.Null(Target(true, Temp + @"scratchm4NeoFences.exe", entry: null, WithoutInstall));
        Assert.Null(Target(true, Temp.ToUpperInvariant() + @"xNeoFences.exe", entry: null, WithoutInstall));
    }

    [Fact]
    public void TheInstalledCopy_AlwaysTakesTheEntry() =>
        Assert.Equal(Installed, Target(true, Installed, entry: StartupPolicy.RunCommand(DevBuild), WithInstall));

    [Fact]
    public void ADevBuild_KeepsTheEntryOnAnInstalledCopy() =>
        // Smoke runs start the repo build; sign-in must keep starting the installed NeoFences (M7).
        Assert.Equal(Installed, Target(true, DevBuild, entry: StartupPolicy.RunCommand(Installed), WithInstall));

    [Fact]
    public void ADevBuild_WithNoEntry_PointsItAtTheInstalledCopy() =>
        // The setting toggled off and on in a dev build, or the value removed by someone else (M7 review I3).
        Assert.Equal(Installed, Target(true, DevBuild, entry: null, WithInstall));

    [Fact]
    public void ATempBuild_WithAnInstalledCopy_PointsTheEntryAtIt() =>
        Assert.Equal(Installed, Target(true, Temp + @"xNeoFences.exe", entry: null, WithInstall));

    [Fact]
    public void ADevBuild_TakesTheEntryBackWhenTheInstallIsGone() =>
        // Uninstalled without its hook (files deleted by hand): the stale entry would start nothing after a power cut.
        Assert.Equal(DevBuild, Target(true, DevBuild, entry: StartupPolicy.RunCommand(Installed), WithoutInstall));

    [Fact]
    public void IsInstalledExe_NeedsCurrentFolderAndUpdateExe()
    {
        Assert.True(StartupPolicy.IsInstalledExe(Installed, WithInstall));
        Assert.False(StartupPolicy.IsInstalledExe(Installed, WithoutInstall));
        Assert.False(StartupPolicy.IsInstalledExe(DevBuild, WithInstall));
    }

    [Theory]
    [InlineData("\"C:\\A B\\NeoFences.exe\"", "C:\\A B\\NeoFences.exe")]
    [InlineData("\"C:\\A B\\NeoFences.exe\" --flag", "C:\\A B\\NeoFences.exe")]
    [InlineData("C:\\X\\NeoFences.exe", "C:\\X\\NeoFences.exe")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExePathOf_ReadsQuotedAndPlainCommands(string? command, string? expected) =>
        Assert.Equal(expected, StartupPolicy.ExePathOf(command));

    [Fact]
    public void RunCommand_QuotesThePath() =>
        Assert.Equal("\"C:\\Program Files\\NeoFences\\NeoFences.exe\"", StartupPolicy.RunCommand(@"C:\Program Files\NeoFences\NeoFences.exe"));
}
