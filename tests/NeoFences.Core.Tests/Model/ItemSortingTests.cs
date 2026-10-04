using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

public class ItemSortingTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static ItemInfo File(string name, string type, int daysAgo) => new(@"D:\Downloads\" + name, name, IsFolder: false, type, Day.AddDays(-daysAgo));
    private static ItemInfo Folder(string name, int daysAgo) => new(@"D:\Downloads\" + name, name, IsFolder: true, "File folder", Day.AddDays(-daysAgo));

    private static readonly ItemInfo[] Sample =
    [
        File("setup10.exe", "Application", 5),
        File("notes.txt", "Text Document", 1),
        Folder("Mods", 9),
        File("setup2.exe", "Application", 0),
        Folder("archive", 2),
    ];

    private static IReadOnlyList<string> Names(IReadOnlyList<string> refs) => refs.Select(Path.GetFileName).ToList()!;

    [Fact]
    public void ByName_FoldersFirst_NumbersInNaturalOrder() =>
        Assert.Equal(["archive", "Mods", "notes.txt", "setup2.exe", "setup10.exe"], Names(ItemSorting.Order(Sample, FenceSort.Name)));

    [Fact]
    public void ByType_FoldersFirst_ThenTypeName_ThenName() =>
        Assert.Equal(["archive", "Mods", "setup2.exe", "setup10.exe", "notes.txt"], Names(ItemSorting.Order(Sample, FenceSort.Type)));

    [Fact]
    public void ByDate_NewestFirst_FoldersNotGrouped()
    {
        // User choice 2026-10-03: "the latest file is always at the top" (Downloads, Screenshots).
        Assert.Equal(["setup2.exe", "notes.txt", "archive", "setup10.exe", "Mods"], Names(ItemSorting.Order(Sample, FenceSort.Date)));
    }

    [Fact]
    public void Manual_KeepsTheGivenOrder() =>
        Assert.Equal(Sample.Select(item => item.ItemRef), ItemSorting.Order(Sample, FenceSort.Manual));
}
