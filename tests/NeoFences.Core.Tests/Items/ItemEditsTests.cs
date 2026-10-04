using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>Adding, moving, duplicating, removing and following items (M18 spec §2, §4). Nothing here touches a file.</summary>
public class ItemEditsTests
{
    private const string Games = "games";
    private const string Tools = "tools";
    private const string Desktop = @"C:\Users\cipher\Desktop\";

    private static VirtualItem Item(string id, string target, string? name = null) => new() { Id = id, Target = target, Name = name };

    private static ItemsDocument Sample() => new ItemsDocument()
        .With(Games, [Item("a", @"D:\Games\a.exe"), Item("b", @"D:\Games\b.exe"), Item("c", @"D:\Games\c.exe")])
        .With(Tools, [Item("t", @"C:\Tools\t.exe")]);

    private static string[] Ids(ItemsDocument document, string fenceId) => [.. document.Of(fenceId).Select(item => item.Id)];

    // ---------- Add (drops and Add item…) ----------

    [Fact]
    public void Add_InsertsAtThePosition_AndSkipsATargetTheFenceAlreadyHas_IgnoringCase()
    {
        var added = ItemEdits.Add(Sample(), Games, [VirtualItem.Create(@"d:\games\B.EXE"), Item("new", @"D:\Games\new.exe")], insertAt: 1);

        Assert.Equal(["a", "new", "b", "c"], Ids(added.Document, Games));
        Assert.Equal(["new"], added.AddedIds);
        Assert.Equal(["b"], added.AlreadyThereIds); // flashes
    }

    [Fact]
    public void Add_TheSameTargetMayBeInAnotherFence()
    {
        var added = ItemEdits.Add(Sample(), Tools, [Item("copy", @"D:\Games\a.exe")]);

        Assert.Equal(["t", "copy"], Ids(added.Document, Tools));
        Assert.Empty(added.AlreadyThereIds);
    }

    [Fact]
    public void Add_ToAFenceWithoutAList_MakesOne_AtTheEndByDefault_AndIgnoresBlankAndRepeatedTargets()
    {
        var added = ItemEdits.Add(new ItemsDocument(), "new-fence",
            [Item("one", Desktop + "x.txt"), Item("two", Desktop + "X.TXT"), Item("blank", "  ")], insertAt: 99);

        Assert.Equal(["one"], Ids(added.Document, "new-fence"));
    }

    [Fact]
    public void Add_NothingNew_ReturnsTheSameDocument()
    {
        var document = Sample();
        Assert.Same(document, ItemEdits.Add(document, Games, [VirtualItem.Create(@"D:\Games\a.exe")]).Document);
    }

    // ---------- Move and Duplicate (drag between and inside fences) ----------

    [Fact]
    public void Move_ToAnotherFence_KeepsEveryField()
    {
        var document = ItemEdits.Replace(Sample(), Item("b", @"D:\Games\b.exe", name: "Bee"));

        var moved = ItemEdits.Move(document, ["b"], Tools, insertAt: 0);

        Assert.Equal(["a", "c"], Ids(moved, Games));
        Assert.Equal(["b", "t"], Ids(moved, Tools));
        Assert.Equal("Bee", moved.Find("b")!.Name);
    }

    [Fact]
    public void Move_InsideTheFence_CountsTheDraggedItemsInTheShownPosition()
    {
        // a b c shown; a dragged to after c (insert index 3, as displayed with a still there).
        Assert.Equal(["b", "c", "a"], Ids(ItemEdits.Move(Sample(), ["a"], Games, insertAt: 3), Games));
        // c dragged before a (index 0).
        Assert.Equal(["c", "a", "b"], Ids(ItemEdits.Move(Sample(), ["c"], Games, insertAt: 0), Games));
        // a and c dragged between b and c (index 2): they keep their own order.
        Assert.Equal(["b", "a", "c"], Ids(ItemEdits.Move(Sample(), ["c", "a"], Games, insertAt: 2), Games));
    }

    [Fact]
    public void Move_UnknownIds_ChangeNothing()
    {
        var document = Sample();
        Assert.Same(document, ItemEdits.Move(document, ["nope"], Tools, insertAt: 0));
    }

    [Fact]
    public void Duplicate_MakesCopiesWithNewIds_AlsoInTheSameFence()
    {
        var (duplicated, newIds) = ItemEdits.Duplicate(Sample(), ["a"], Games, insertAt: 1);

        var copyId = Assert.Single(newIds);
        Assert.NotEqual("a", copyId);
        Assert.Equal(["a", copyId, "b", "c"], Ids(duplicated, Games));
        Assert.Equal(@"D:\Games\a.exe", duplicated.Find(copyId)!.Target);
    }

    // ---------- Remove, Replace, RemoveFence, Prune, Reorder ----------

    [Fact]
    public void Remove_TakesTheItemsOutOfEveryFence()
    {
        var removed = ItemEdits.Remove(Sample(), ["a", "t"]);

        Assert.Equal(["b", "c"], Ids(removed, Games));
        Assert.Empty(removed.Of(Tools));
        var document = Sample();
        Assert.Same(document, ItemEdits.Remove(document, ["nope"]));
    }

    [Fact]
    public void Replace_ChangesOnlyThatItem()
    {
        var replaced = ItemEdits.Replace(Sample(), Item("b", @"E:\Moved\b.exe", name: "B") with { Arguments = "-w", RunAsAdmin = true, Note = "n" });

        Assert.Equal(["a", "b", "c"], Ids(replaced, Games));
        var item = replaced.Find("b")!;
        Assert.Equal((@"E:\Moved\b.exe", "B", "-w", true, "n"), (item.Target, item.Name, item.Arguments, item.RunAsAdmin, item.Note));
    }

    [Fact]
    public void RemoveFence_AndPrune_DropLists()
    {
        Assert.Empty(ItemEdits.RemoveFence(Sample(), Tools).Of(Tools));
        var document = Sample();
        Assert.Same(document, ItemEdits.Prune(document, [Games, Tools, "other"]));
        Assert.Equal([Games], ItemEdits.Prune(document, [Games]).Fences.Keys);
    }

    [Fact]
    public void Reorder_NeedsExactlyTheFencesItems()
    {
        Assert.Equal(["c", "a", "b"], Ids(ItemEdits.Reorder(Sample(), Games, ["c", "a", "b"]), Games));
        Assert.Throws<ArgumentException>(() => ItemEdits.Reorder(Sample(), Games, ["c", "a"]));
        Assert.Throws<ArgumentException>(() => ItemEdits.Reorder(Sample(), Games, ["c", "a", "a"]));
    }

    // ---------- Retarget (a watched target renamed in place) ----------

    [Fact]
    public void Retarget_FollowsARename_InEveryFence_AndInsideARenamedFolder()
    {
        var document = new ItemsDocument()
            .With(Games, [Item("g1", @"D:\Games\Hades"), Item("g2", @"D:\Games\Hades\Hades.exe", name: "Hades"), Item("g3", @"D:\Games\Hades 2\x.exe")])
            .With(Tools, [Item("t1", @"d:\games\hades"), Item("web", "https://hades.example")]);

        var followed = ItemEdits.Retarget(document, @"D:\Games\Hades", @"D:\Games\Hades II");

        Assert.Equal(@"D:\Games\Hades II", followed.Find("g1")!.Target);
        Assert.Equal(@"D:\Games\Hades II\Hades.exe", followed.Find("g2")!.Target);
        Assert.Equal("Hades", followed.Find("g2")!.Name); // its own name stays
        Assert.Equal(@"D:\Games\Hades 2\x.exe", followed.Find("g3")!.Target); // a sibling with a longer name is not inside it
        Assert.Equal(@"D:\Games\Hades II", followed.Find("t1")!.Target);
        Assert.Equal("https://hades.example", followed.Find("web")!.Target);
        Assert.Same(document, ItemEdits.Retarget(document, @"D:\Other", @"D:\Else"));
    }

    // ---------- watching inputs, pictures, repair ----------

    [Fact]
    public void PathTargets_AreFilesAndFolders_Once_InFenceOrder()
    {
        var document = Sample().With("web", [Item("w", "https://x.y"), Item("s", "::{645FF040-5081-101B-9F08-00AA002F954E}"), Item("dup", @"d:\games\A.exe")]);

        Assert.Equal([@"D:\Games\a.exe", @"D:\Games\b.exe", @"D:\Games\c.exe", @"C:\Tools\t.exe"], ItemEdits.PathTargets(document));
    }

    [Fact]
    public void ImagesInUse_ListsTheCopiedPictures()
    {
        var document = new ItemsDocument().With(Games,
        [
            Item("a", @"D:\a.exe") with { Icon = new ItemIcon { Image = "a.png" } },
            Item("b", @"D:\b.exe") with { Icon = new ItemIcon { File = @"C:\Windows\System32\shell32.dll", Index = 4 } },
        ]);

        Assert.Equal(["a.png"], ItemEdits.ImagesInUse(document));
    }

    [Fact]
    public void Repair_DropsBrokenEntries_AndGivesCopiedIdsNewOnes()
    {
        var document = new ItemsDocument
        {
            Fences = new Dictionary<string, IReadOnlyList<VirtualItem>>
            {
                [Games] = [Item("same", @"D:\a.exe"), null!, Item("same", @"D:\b.exe"), Item("", " "), Item("x", @" D:\c.exe ") with { Icon = new ItemIcon() }],
                [""] = [Item("lost", @"D:\d.exe")],
                [Tools] = null!,
            },
        };

        var repaired = ItemEdits.Repair(document);

        var games = repaired.Of(Games);
        Assert.Equal([@"D:\a.exe", @"D:\b.exe", @"D:\c.exe"], games.Select(item => item.Target));
        Assert.Equal("same", games[0].Id);
        Assert.NotEqual("same", games[1].Id);
        Assert.Null(games[2].Icon); // an icon with neither a file nor a picture is none
        Assert.Empty(repaired.Of(Tools));
        Assert.False(repaired.Fences.ContainsKey(""));
    }
}
