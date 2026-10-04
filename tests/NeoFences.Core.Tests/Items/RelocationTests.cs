using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>Fixing many missing items after one Locate… (M19, spec 2026-10-05 §2).</summary>
public class RelocationTests
{
    // ---------- Relocation.Find ----------

    [Theory]
    [InlineData(@"D:\Games\Crysis 2\Crysis2.exe", @"E:\Games\Crysis 2\Crysis2.exe", @"D:\", @"E:\")] // a drive letter changed
    [InlineData(@"D:\Games\X\x.exe", @"D:\MyGames\X\x.exe", @"D:\Games", @"D:\MyGames")] // a folder renamed
    [InlineData(@"D:\Games\X\x.exe", @"D:\Archive\Games\X\x.exe", @"D:\", @"D:\Archive")] // moved one level down
    [InlineData(@"D:\Archive\Games\X\x.exe", @"D:\Games\X\x.exe", @"D:\Archive", @"D:\")] // and back up
    [InlineData(@"\\nas\old\Movies\a.mkv", @"\\nas\new\Movies\a.mkv", @"\\nas\old", @"\\nas\new")] // another share
    [InlineData(@"D:\games\x\X.EXE", @"E:\Games\X\x.exe", @"D:\", @"E:\")] // case does not matter
    [InlineData(@"D:\Games\Hades\", @"E:\Games\Hades", @"D:\", @"E:\")] // a folder target, with and without a trailing "\"
    public void Find_TheDifferingFront_FromTheEnd(string oldTarget, string newTarget, string oldBase, string newBase)
    {
        var found = Relocation.Find(oldTarget, newTarget);
        Assert.NotNull(found);
        Assert.Equal(oldBase, found.OldBase, ignoreCase: true);
        Assert.Equal(newBase, found.NewBase, ignoreCase: true);
    }

    [Theory]
    [InlineData(@"D:\Games\a.exe", @"D:\Games\b.exe")] // the file itself was renamed: nothing to share
    [InlineData(@"D:\Games\a.exe", @"D:\Games\a.exe")] // nothing moved
    [InlineData(@"D:\a.exe", @"relative\a.exe")] // no root
    [InlineData("https://a.com/x", @"D:\x")]
    public void Find_Nothing_WhenTheNameChanged_OrNothingMoved_OrNoRoot(string oldTarget, string newTarget) =>
        Assert.Null(Relocation.Find(oldTarget, newTarget));

    [Fact]
    public void Find_AFileRightOnADrive_MovesTheDrive()
    {
        var found = Relocation.Find(@"D:\a.exe", @"E:\a.exe");
        Assert.Equal(new Relocation.Bases(@"D:\", @"E:\"), found);
    }

    // ---------- Relocation.Candidates ----------

    private static ItemsDocument Sample() => new ItemsDocument()
        .With("games", [
            new VirtualItem { Id = "located", Target = @"D:\Games\Crysis 2\Crysis2.exe" },
            new VirtualItem { Id = "c3", Target = @"D:\Games\Crysis 3\Crysis3.exe", Name = "Crysis 3" },
            new VirtualItem { Id = "ok", Target = @"D:\Games\Hades\Hades.exe" },
            new VirtualItem { Id = "web", Target = "https://github.com" },
        ])
        .With("other", [
            new VirtualItem { Id = "mirage", Target = @"D:\Games\AC Mirage" },
            new VirtualItem { Id = "near", Target = @"D:\GamesOld\x.exe" }, // "D:\Games" must not match "D:\GamesOld"
            new VirtualItem { Id = "unplugged", Target = @"D:\Games\Doom\doom.exe" },
            new VirtualItem { Id = "app", Target = @"shell:AppsFolder\Spotify!App" },
        ]);

    private static TargetState StateOf(string target) => target switch
    {
        @"D:\Games\Hades\Hades.exe" => TargetState.Ok,
        @"D:\Games\Doom\doom.exe" => TargetState.Unavailable,
        "https://github.com" or @"shell:AppsFolder\Spotify!App" => TargetState.Ok,
        _ => TargetState.Missing,
    };

    [Fact]
    public void Candidates_AreTheOtherMissingOrUnavailablePathItems_UnderTheOldBase_InEveryFence()
    {
        var moves = Relocation.Candidates(Sample(), StateOf, new Relocation.Bases(@"D:\Games", @"E:\Games"), exceptItemId: "located");
        Assert.Equal(
            [
                new Relocation.Move("c3", @"D:\Games\Crysis 3\Crysis3.exe", @"E:\Games\Crysis 3\Crysis3.exe"),
                new Relocation.Move("mirage", @"D:\Games\AC Mirage", @"E:\Games\AC Mirage"),
                new Relocation.Move("unplugged", @"D:\Games\Doom\doom.exe", @"E:\Games\Doom\doom.exe"),
            ],
            moves);
    }

    [Fact]
    public void Candidates_FromADriveRoot_KeepTheRestOfThePath()
    {
        var moves = Relocation.Candidates(Sample(), StateOf, new Relocation.Bases(@"D:\", @"E:\"), exceptItemId: "located");
        Assert.Contains(new Relocation.Move("near", @"D:\GamesOld\x.exe", @"E:\GamesOld\x.exe"), moves);
        Assert.DoesNotContain(moves, move => move.ItemId is "ok" or "web" or "app" or "located");
    }

    [Fact]
    public void Candidates_TheSameTargetInTwoItems_BothMove()
    {
        var document = new ItemsDocument()
            .With("a", [new VirtualItem { Id = "1", Target = @"D:\G\x.exe" }])
            .With("b", [new VirtualItem { Id = "2", Target = @"D:\G\x.exe" }]);
        var moves = Relocation.Candidates(document, _ => TargetState.Missing, new Relocation.Bases(@"D:\G", @"E:\G"), exceptItemId: "none");
        Assert.Equal(["1", "2"], moves.Select(move => move.ItemId));
    }

    // ---------- ItemEdits.Relocate ----------

    [Fact]
    public void Relocate_ChangesOnlyTheseTargets_EverythingElseStays()
    {
        var document = new ItemsDocument().With("f", [
            new VirtualItem { Id = "1", Target = @"D:\G\a.exe", Name = "A", Arguments = "-x", Note = "n", Icon = new ItemIcon { Image = "p.png" } },
            new VirtualItem { Id = "2", Target = @"D:\G\b.exe" },
            new VirtualItem { Id = "3", Target = @"D:\G\c.exe" },
        ]);
        var moved = ItemEdits.Relocate(document, new Dictionary<string, string> { ["1"] = @"E:\G\a.exe", ["3"] = @"E:\G\c.exe", ["unknown"] = @"E:\x" });
        Assert.Equal([@"E:\G\a.exe", @"D:\G\b.exe", @"E:\G\c.exe"], moved.Of("f").Select(item => item.Target));
        var first = moved.Of("f")[0];
        Assert.Equal(("A", "-x", "n", "p.png"), (first.Name, first.Arguments, first.Note, first.Icon?.Image));
        Assert.Same(document, ItemEdits.Relocate(document, new Dictionary<string, string> { ["unknown"] = @"E:\x" }));
    }
}
