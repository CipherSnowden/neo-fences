using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

public class FileNamesTests
{
    [Theory]
    [InlineData("report.docx")]
    [InlineData("Crysis 2 (2011)")]
    [InlineData("été notes.txt")]
    public void OrdinaryNames_AreValid(string name) => Assert.True(FileNames.IsValidNewName(name));

    [Theory]
    [InlineData(@"sub\inside.txt")]   // M3a review I2: IFileOperation would move the file into "sub"
    [InlineData(@"..\moved.txt")]     // … or out of the Desktop
    [InlineData("a/b")]
    [InlineData("what?")]
    [InlineData("a:b")]
    [InlineData("tab\there")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("   ")]
    [InlineData("")]
    public void PathsAndForbiddenCharacters_AreRejected(string name) => Assert.False(FileNames.IsValidNewName(name));
}
