namespace NeoFences.Core.Tests;

/// <summary>
/// M39 (ADR-061): Core reads the Windows paths it stores (items, folders, game shortcuts, picture names) by Windows' rules on
/// every OS, so its tests pass on Linux too. For the paths Core stores the answers match System.IO.Path on Windows; the
/// known differences reach no caller (an extension of "file." is ".", "/" is kept, "\\?\" prefixes are not fully qualified).
/// </summary>
public class WindowsPathTests
{
    [Theory]
    [InlineData(@"D:\Downloads\notes.txt", "notes.txt")]
    [InlineData(@"..\..\Windows\evil.png", "evil.png")]
    [InlineData("../../etc/evil.png", "evil.png")]
    [InlineData("plain.jpg", "plain.jpg")]
    [InlineData(@"D:\Music\", "")]
    [InlineData("", "")]
    [InlineData("C:evil.png", "evil.png")] // M39 review C1: a drive-relative name never keeps its drive
    [InlineData("D:", "")]
    public void FileName_IsWhatFollowsTheLastSeparator(string path, string expected) =>
        Assert.Equal(expected, WindowsPath.FileName(path));

    [Theory]
    [InlineData(@"D:\Games\Hades.url", "Hades", ".url")]
    [InlineData(@"C:\Tools\setup.v2.EXE", "setup.v2", ".EXE")]
    [InlineData(@"D:\Folder.d\README", "README", "")]
    [InlineData(@"D:\.hidden", "", ".hidden")]
    public void Stem_AndExtension(string path, string stem, string extension)
    {
        Assert.Equal(stem, WindowsPath.FileNameWithoutExtension(path));
        Assert.Equal(extension, WindowsPath.Extension(path));
    }

    [Theory]
    [InlineData(@"D:\GameLibrary\Cyberpunk", @"D:\GameLibrary")]
    [InlineData(@"D:\Music", @"D:\")]
    [InlineData(@"D:\", null)]
    [InlineData(@"\\nas\share\pics", @"\\nas\share")]
    [InlineData(@"\\nas\share", null)]
    [InlineData("name.txt", null)]
    public void DirectoryName_IsTheParent_NoneAboveARoot(string path, string? expected) =>
        Assert.Equal(expected, WindowsPath.DirectoryName(path));

    [Theory]
    [InlineData(@"C:\Data\NeoFences\library", "Hades.url", @"C:\Data\NeoFences\library\Hades.url")]
    [InlineData(@"D:\", "Music", @"D:\Music")]
    [InlineData(@"D:\Root\", "a.txt", @"D:\Root\a.txt")]
    public void Combine_PutsOneBackslashBetween(string folder, string name, string expected) =>
        Assert.Equal(expected, WindowsPath.Combine(folder, name));

    [Theory]
    [InlineData(@"D:\GameLibrary\", @"D:\GameLibrary")]
    [InlineData(@"\\nas\share\", @"\\nas\share")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary")]
    public void TrimEndingSeparator_KeepsADrivesRoot(string path, string expected) =>
        Assert.Equal(expected, WindowsPath.TrimEndingSeparator(path));

    [Theory]
    [InlineData(@"D:\GameLibrary", true)]
    [InlineData("D:/GameLibrary", true)]
    [InlineData(@"\\nas\share\pics", true)]
    [InlineData(@"D:GameLibrary", false)]
    [InlineData(@"\GameLibrary", false)]
    [InlineData(@"GameLibrary\x", false)]
    [InlineData("/home/user", false)]
    public void IsFullyQualified_IsADriveWithItsSeparator_OrAShare(string path, bool expected) =>
        Assert.Equal(expected, WindowsPath.IsFullyQualified(path));

    [Theory]
    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary\Cyberpunk\bin", @"Cyberpunk\bin")]
    [InlineData(@"D:\GameLibrary", @"d:\gamelibrary\Saves", "Saves")]
    [InlineData(@"D:\", @"D:\Music", "Music")]
    [InlineData(@"D:\GameLibrary", @"D:\GameLibrary", ".")]
    [InlineData(@"D:\GameLibrary", @"E:\Other", @"E:\Other")]
    public void RelativePath_IsWhatFollowsTheFolder(string folder, string path, string expected) =>
        Assert.Equal(expected, WindowsPath.RelativePath(folder, path));

    [Theory]
    [InlineData("scene.json", true)]
    [InlineData("a:b", false)]
    [InlineData("what?", false)]
    [InlineData(@"..\x", false)]
    [InlineData("..", false)]
    [InlineData("", false)]
    public void IsPlainFileName_RefusesSeparatorsAndWindowsReservedCharacters(string name, bool expected) =>
        Assert.Equal(expected, WindowsPath.IsPlainFileName(name));
}
