using System.Text.Json;
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Config;

public class ConfigJsonTests
{
    private static NeoFencesConfig SampleConfig()
    {
        var tools = Fence.Create("Tools");
        var games = Fence.Create("Games") with { IconSize = 64, RolledUp = true };
        var library = Fence.Create("Library", isLibrary: true);
        var layout = new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1392) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("DELL", 40, 60, 420, 260) },
        };
        return new NeoFencesConfig
        {
            Fences = [tools, games, library],
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
            Assert.Equal(original.Fences[fenceIdx] with { Tabs = [] }, restored.Fences[fenceIdx] with { Tabs = [] }); // lists compare by reference
        }
        var restoredLayout = restored.Layouts["1mon:DELL-3840x2160@150%"];
        Assert.Equal(new MonitorArea(2560, 1392), restoredLayout.Monitors["DELL"]);
        Assert.Equal(new FenceRect("DELL", 40, 60, 420, 260), restoredLayout.Fences[original.Fences[1].Id]);
    }

    [Fact]
    public void Json_UsesCamelCaseNames_AndHoldsNoItems()
    {
        var json = ConfigJson.Serialize(SampleConfig());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(NeoFencesConfig.CurrentSchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.GetProperty("settings").GetProperty("hideDesktopIcons").GetBoolean());
        Assert.True(root.GetProperty("fences")[2].GetProperty("isLibrary").GetBoolean());
        Assert.False(root.GetProperty("fences")[0].TryGetProperty("items", out _)); // items are items.json's (ADR-041)
        var rect = root.GetProperty("layouts").GetProperty("1mon:DELL-3840x2160@150%").GetProperty("fences").EnumerateObject().Single().Value;
        Assert.Equal("DELL", rect.GetProperty("monitor").GetString());
        Assert.Equal(420, rect.GetProperty("w").GetDouble());
    }

    [Fact]
    public void Deserialize_AcceptsMinimalDocument()
    {
        var config = ConfigJson.Deserialize("""{ "schemaVersion": 5 }""");

        Assert.Empty(config.Fences);
        Assert.Equal(new Settings(), config.Settings);
    }

    [Fact]
    public void Deserialize_MissingPropertiesKeepTheirDefaults()
    {
        var config = ConfigJson.Deserialize("""
            { "schemaVersion": 5, "settings": { "hideDesktopIcons": true },
              "fences": [ { "id": "f1", "title": "Games" } ] }
            """);

        Assert.True(config.Settings.HideDesktopIcons);
        Assert.Equal("Ctrl+Alt+Space", config.Settings.PeekHotkey);
        var fence = Assert.Single(config.Fences);
        Assert.Equal(48, fence.IconSize);
        Assert.False(fence.IsLibrary);
    }

    [Fact]
    public void Deserialize_IgnoresWhatOlderVersionsWrote()
    {
        // A pre-pivot file (schema 4): Inbox, Portal sources, rules, Takeover. Read without failing; ConfigStore then starts fresh.
        var config = ConfigJson.Deserialize("""
            { "schemaVersion": 4, "settings": { "takeover": true, "takeoverPromptAnswered": true },
              "fences": [ { "id": "f1", "title": "Inbox", "isInbox": true, "items": [ "C:\\Users\\x\\Desktop\\a.txt" ] },
                          { "id": "f2", "title": "Shots", "source": { "kind": "portal", "path": "D:\\Shots" }, "sort": "date" } ],
              "rules": [ { "id": "r1", "fenceId": "f1", "condition": { "kind": "type" } } ] }
            """);

        Assert.Equal(["Inbox", "Shots"], config.Fences.Select(fence => fence.Title));
        Assert.False(config.Settings.HideDesktopIcons);
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
