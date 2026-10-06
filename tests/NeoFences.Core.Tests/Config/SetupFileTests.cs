using System.IO.Compression;
using NeoFences.Core.Config;
using NeoFences.Core.Items;
using NeoFences.Core.Model;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Config;

/// <summary>M36 (spec §3): Export setup… / Import setup… — one .neofences file (a zip) and its checks.</summary>
public class SetupFileTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A setup with a picture icon, a chosen cover and an own preset; the pictures exist in <paramref name="data"/>.</summary>
    private static (NeoFencesConfig Config, ItemsDocument Items) Setup(TempDirectory data)
    {
        var fence = Fence.Create("Apps") with { Look = new OwnLook { Strength = 40 } };
        var config = new NeoFencesConfig
        {
            Fences = [fence],
            Settings = new Settings { PeekHotkey = "Ctrl+Q" },
            Presets = [new LookPreset { Name = "Mine", Look = new OwnLook { Spacing = Spacing.Roomy } }],
            Library = new LibrarySettings { Folders = ["D:\\Games"], CoverChoices = new Dictionary<string, string> { ["steam:1"] = "choice-steam-1.jpg" } },
        };
        var items = new ItemsDocument().With(fence.Id,
        [
            VirtualItem.Create("C:\\tools\\a.exe") with { Icon = new ItemIcon { Image = "pic.png" } },
            VirtualItem.Create("C:\\tools\\b.exe") with { Icon = new ItemIcon { File = "C:\\icons\\b.ico" } }, // a user's file: never exported
        ]);
        Directory.CreateDirectory(data.File("icons"));
        Directory.CreateDirectory(data.File("covers"));
        File.WriteAllBytes(data.File("icons\\pic.png"), [1, 2, 3]);
        File.WriteAllBytes(data.File("icons\\unused.png"), [9]);
        File.WriteAllBytes(data.File("covers\\choice-steam-1.jpg"), [4, 5]);
        File.WriteAllText(data.File("covers\\index.json"), "{}");
        return (config, items);
    }

    [Fact]
    public void Export_HoldsTheSetupItsPicturesAndAManifest_AndNothingElse()
    {
        using var data = new TempDirectory();
        var (config, items) = Setup(data);
        var path = data.File("my.neofences");

        var missing = SetupFile.Write(path, config, items, data.Path, appVersion: "0.23.0", now: Now);

        Assert.Empty(missing);
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(["config.json", "covers/choice-steam-1.jpg", "icons/pic.png", "items.json", "manifest.json"],
            zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Import_ReadsBackTheSameSetupAndPictures_AndPlacesThePictures()
    {
        using var data = new TempDirectory();
        var (config, items) = Setup(data);
        var path = data.File("my.neofences");
        SetupFile.Write(path, config, items, data.Path, appVersion: "0.23.0", now: Now);

        var read = SetupFile.Read(path);

        Assert.Equal(("0.23.0", Now), (read.Manifest.AppVersion, read.Manifest.ExportedAt));
        Assert.Equal("Ctrl+Q", read.Config.Settings.PeekHotkey);
        Assert.Equal(config.Fences[0].Look, read.Config.Fences[0].Look);
        Assert.Equal(["Mine"], read.Config.Presets.Select(preset => preset.Name));
        Assert.Equal(["D:\\Games"], read.Config.Library.Folders);
        Assert.Equal(2, read.Items.Of(config.Fences[0].Id).Count);
        Assert.Equal([new SetupPicture("covers", "choice-steam-1.jpg"), new SetupPicture("icons", "pic.png")],
            read.Pictures.Select(picture => picture.Picture).OrderBy(picture => picture.Folder, StringComparer.Ordinal));

        using var elsewhere = new TempDirectory();
        Assert.Empty(SetupFile.PlacePictures(read, elsewhere.Path));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(elsewhere.File("icons\\pic.png")));
        Assert.Equal([4, 5], File.ReadAllBytes(elsewhere.File("covers\\choice-steam-1.jpg")));
    }

    [Fact]
    public void Export_LeavesOutAPictureThatIsGone_AndSaysWhich()
    {
        using var data = new TempDirectory();
        var (config, items) = Setup(data);
        File.Delete(data.File("icons\\pic.png"));
        var missing = SetupFile.Write(data.File("my.neofences"), config, items, data.Path, appVersion: "0.23.0", now: Now);
        Assert.Equal([new SetupPicture("icons", "pic.png")], missing);
        Assert.DoesNotContain(SetupFile.Read(data.File("my.neofences")).Pictures, picture => picture.Picture.Folder == "icons");
    }

    [Fact]
    public void Read_RefusesAFileThatIsNotAZip()
    {
        using var data = new TempDirectory();
        File.WriteAllText(data.File("notes.neofences"), "hello");
        Assert.Equal(SetupFile.NotASetup, Assert.Throws<SetupFileException>(() => SetupFile.Read(data.File("notes.neofences"))).Message);
    }

    [Fact]
    public void Read_RefusesAZipThatIsNotAnExport()
    {
        using var data = new TempDirectory();
        Zip(data.File("other.neofences"), ("readme.txt", "hi"));
        Assert.Equal(SetupFile.NotASetup, Assert.Throws<SetupFileException>(() => SetupFile.Read(data.File("other.neofences"))).Message);
        Zip(data.File("wrong.neofences"), ("manifest.json", """{ "format": "Something else", "formatVersion": 1 }"""));
        Assert.Equal(SetupFile.NotASetup, Assert.Throws<SetupFileException>(() => SetupFile.Read(data.File("wrong.neofences"))).Message);
    }

    [Theory]
    [InlineData("""{ "format": "NeoFences setup", "formatVersion": 2, "appVersion": "2.0.0", "configSchema": 5, "itemsSchema": 1 }""")]
    [InlineData("""{ "format": "NeoFences setup", "formatVersion": 1, "appVersion": "2.0.0", "configSchema": 6, "itemsSchema": 1 }""")]
    [InlineData("""{ "format": "NeoFences setup", "formatVersion": 1, "appVersion": "2.0.0", "configSchema": 5, "itemsSchema": 2 }""")]
    public void Read_RefusesAnExportOfANewerNeoFences(string manifest)
    {
        using var data = new TempDirectory();
        Zip(data.File("new.neofences"), ("manifest.json", manifest), ("config.json", "{}"), ("items.json", "{}"));
        Assert.Equal("This file comes from a newer NeoFences (2.0.0). Update NeoFences first.",
            Assert.Throws<SetupFileException>(() => SetupFile.Read(data.File("new.neofences"))).Message);
    }

    [Theory]
    [InlineData(null, "{}")]                 // no config.json
    [InlineData("{ not json", "{}")]          // a broken config.json
    [InlineData("""{ "schemaVersion": 5 }""", null)] // no items.json
    [InlineData("""{ "schemaVersion": 5 }""", "[1, 2")]
    [InlineData("""{ "schemaVersion": 4 }""", "{}")] // from before the virtual items: not something this export writes
    public void Read_CallsAnExportWithMissingOrBrokenPartsDamaged(string? configJson, string? itemsJson)
    {
        using var data = new TempDirectory();
        var parts = new List<(string, string)> { ("manifest.json", """{ "format": "NeoFences setup", "formatVersion": 1, "appVersion": "0.23.0", "configSchema": 5, "itemsSchema": 1 }""") };
        if (configJson is not null) parts.Add(("config.json", configJson));
        if (itemsJson is not null) parts.Add(("items.json", itemsJson));
        Zip(data.File("broken.neofences"), [.. parts]);
        Assert.Equal(SetupFile.Damaged, Assert.Throws<SetupFileException>(() => SetupFile.Read(data.File("broken.neofences"))).Message);
    }

    [Fact]
    public void Read_TakesOnlyPicturesTheSetupUses_AndNeverANameThatLeavesItsFolder()
    {
        using var data = new TempDirectory();
        var (config, items) = Setup(data);
        var path = data.File("my.neofences");
        SetupFile.Write(path, config, items, data.Path, appVersion: "0.23.0", now: Now);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            foreach (var name in new[] { "icons/../../evil.png", "icons/sub/pic.png", "..\\icons\\pic.png", "icons/", "logs/neofences.log", "icons/extra.png" })
                using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("x");
        }
        // A hand-edited items.json pointing a picture out of its folder is read as the file name alone.
        var read = SetupFile.Read(path);
        Assert.Equal(["choice-steam-1.jpg", "pic.png"], read.Pictures.Select(picture => picture.Picture.FileName).Order(StringComparer.Ordinal));

        using var elsewhere = new TempDirectory();
        SetupFile.PlacePictures(read, elsewhere.File("data"));
        Assert.False(File.Exists(elsewhere.File("evil.png")));
        Assert.Equal(["choice-steam-1.jpg", "pic.png"],
            Directory.GetFiles(elsewhere.File("data"), "*", SearchOption.AllDirectories).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PicturesOf_UsesOnlyFileNames()
    {
        var fence = Fence.Create("Apps");
        var items = new ItemsDocument().With(fence.Id, [VirtualItem.Create("C:\\a.exe") with { Icon = new ItemIcon { Image = "..\\..\\Windows\\evil.png" } }]);
        Assert.Equal([new SetupPicture("icons", "evil.png")], SetupFile.PicturesOf(new NeoFencesConfig { Fences = [fence] }, items));
    }

    private static void Zip(string path, params (string Name, string Text)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
            using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(text);
    }
}
