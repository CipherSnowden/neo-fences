using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>
/// Renames seen in a watched folder are settled before items follow them (final review C1): editors save by renaming the
/// original away and a temp file onto its name; following the first rename would point the item at a deleted temp file.
/// </summary>
public class RenamesTests
{
    private static IReadOnlyList<(string OldPath, string NewPath)> Settle(string[] existing, params (string, string)[] seen) =>
        Renames.Settle(seen, path => existing.Contains(path, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void APlainRename_IsFollowed()
    {
        Assert.Equal([(@"D:\a.txt", @"D:\b.txt")], Settle([@"D:\b.txt"], (@"D:\a.txt", @"D:\b.txt")));
    }

    [Fact]
    public void AnEditorSave_IsNotFollowed()
    {
        // Word: report.docx -> ~WRL0001.tmp, ~WRD0000.tmp -> report.docx, then ~WRL0001.tmp is deleted.
        var settled = Settle([@"D:\report.docx"],
            (@"D:\report.docx", @"D:\~WRL0001.tmp"), (@"D:\~WRD0000.tmp", @"D:\report.docx"));

        Assert.DoesNotContain(settled, rename => rename.OldPath.Equals(@"D:\report.docx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AChain_IsJoined_IgnoringCase()
    {
        Assert.Equal([(@"D:\a.txt", @"D:\c.txt")], Settle([@"D:\c.txt"], (@"D:\a.txt", @"D:\b.txt"), (@"d:\B.TXT", @"D:\c.txt")));
    }

    [Fact]
    public void ARenameWhoseNewNameIsGoneAgain_IsNotFollowed()
    {
        // Renamed and then deleted (or moved on) within the burst: the item shows Missing instead of pointing at nothing.
        Assert.Empty(Settle([], (@"D:\a.txt", @"D:\b.txt")));
    }

    [Fact]
    public void ARenameBackAndForth_IsNothing()
    {
        Assert.Empty(Settle([@"D:\a.txt"], (@"D:\a.txt", @"D:\b.txt"), (@"D:\b.txt", @"D:\a.txt")));
    }
}
