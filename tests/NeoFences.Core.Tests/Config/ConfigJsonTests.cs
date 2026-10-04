using System.Text.Json;
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Config;

public class ConfigJsonTests
{
    private static NeoFencesConfig SampleConfig()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [@"C:\Users\cipher\Desktop\notes.txt"] };
        var games = Fence.Create("Games") with
        {
            Items = [@"C:\Users\cipher\Desktop\Crysis 2.lnk", "::{645FF040-5081-101B-9F08-00AA002F954E}"],
            IconSize = 64,
            RolledUp = true,
        };
        var screenshots = Fence.Create("Screenshots", FenceSource.Portal(@"D:\Pictures\Screenshots")) with { Sort = FenceSort.Date };
        var layout = new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1392) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("DELL", 40, 60, 420, 260) },
        };
        return new NeoFencesConfig
        {
            Fences = [inbox, games, screenshots],
            Layouts = new Dictionary<string, Layout> { ["1mon:DELL-3840x2160@150%"] = layout },
            LastLayoutFingerprint = "1mon:DELL-3840x2160@150%",
        };
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var original = SampleConfig();

        var restored = ConfigJson.Deserialize(ConfigJson.Serialize(original));

        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.Settings, restored.Settings);
        Assert.Equal(original.LastLayoutFingerprint, restored.LastLayoutFingerprint);
        Assert.Equal(original.Fences.Count, restored.Fences.Count);
        for (var fenceIdx = 0; fenceIdx < original.Fences.Count; fenceIdx++)
        {
            var expected = original.Fences[fenceIdx];
            var actual = restored.Fences[fenceIdx];
            Assert.Equal(expected with { Items = [], Tabs = [] }, actual with { Items = [], Tabs = [] }); // lists compare by reference
            Assert.Equal(expected.Items, actual.Items);
        }
        var restoredLayout = restored.Layouts["1mon:DELL-3840x2160@150%"];
        Assert.Equal(new MonitorArea(2560, 1392), restoredLayout.Monitors["DELL"]);
        Assert.Equal(new FenceRect("DELL", 40, 60, 420, 260), restoredLayout.Fences[original.Fences[1].Id]);
    }

    [Fact]
    public void Json_UsesCamelCaseNamesAndEnumValues_AndOmitsComputedInbox()
    {
        var json = ConfigJson.Serialize(SampleConfig());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(NeoFencesConfig.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.GetProperty("settings").GetProperty("takeover").GetBoolean());
        Assert.False(root.TryGetProperty("inbox", out _));
        var screenshots = root.GetProperty("fences")[2];
        Assert.Equal("portal", screenshots.GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal(@"D:\Pictures\Screenshots", screenshots.GetProperty("source").GetProperty("path").GetString());
        Assert.Equal("date", screenshots.GetProperty("sort").GetString());
        var rect = root.GetProperty("layouts").GetProperty("1mon:DELL-3840x2160@150%").GetProperty("fences").EnumerateObject().Single().Value;
        Assert.Equal("DELL", rect.GetProperty("monitor").GetString());
        Assert.Equal(420, rect.GetProperty("w").GetDouble());
    }

    [Fact]
    public void Deserialize_AcceptsMinimalDocument()
    {
        var config = ConfigJson.Deserialize("""{ "schemaVersion": 1 }""");

        Assert.Empty(config.Fences);
        Assert.Equal(new Settings(), config.Settings);
    }

    [Fact]
    public void Deserialize_MissingPropertiesKeepTheirDefaults()
    {
        var config = ConfigJson.Deserialize("""
            { "schemaVersion": 1, "settings": { "takeover": true },
              "fences": [ { "id": "f1", "title": "Games" } ] }
            """);

        Assert.True(config.Settings.Takeover);
        Assert.Equal("Ctrl+Alt+Space", config.Settings.PeekHotkey);
        var fence = Assert.Single(config.Fences);
        Assert.Equal(48, fence.IconSize);
        Assert.Equal(FenceSource.Desktop, fence.Source);
        Assert.Empty(fence.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{ "fences": [ { "title": "no id" } ] }""")]
    public void Deserialize_RejectsInvalidDocuments(string json)
    {
        Assert.ThrowsAny<JsonException>(() => ConfigJson.Deserialize(json));
    }

    [Fact]
    public void Json_IsReadableForHandEditing_NoEscapedPlusOrAmpersand()
    {
        var config = NeoFencesConfig.CreateDefault() with
        {
            LastLayoutFingerprint = @"1mon:\\?\DISPLAY#GSM5B71#5&66efef6&1&UID4353-1920x1080@100%",
        };

        var json = ConfigJson.Serialize(config);

        Assert.Contains("\"peekHotkey\": \"Ctrl+Alt+Space\"", json);
        Assert.Contains("5&66efef6&1&UID4353", json);
    }
}
