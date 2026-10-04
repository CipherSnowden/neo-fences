using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>Fence tabs (M9, spec 2026-10-03-fence-tabs-design): fences combined into one box, each tab a full fence.</summary>
public class FenceTabsTests
{
    private const string Setup = "1mon:test";
    private static readonly FenceRect Box = new("mon", 100, 100, 320, 220);

    private static (NeoFencesConfig Config, Fence Inbox, Fence Games, Fence Tools, Fence Docs) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true };
        var games = Fence.Create("Games") with { Items = [@"C:\Desktop\a.lnk"] };
        var tools = Fence.Create("Tools");
        var docs = Fence.Create("Docs");
        var layout = new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["mon"] = new(1920, 1040) },
            Fences = new Dictionary<string, FenceRect>
            {
                [inbox.Id] = Box with { X = 1000 }, [games.Id] = Box, [tools.Id] = Box with { X = 500 }, [docs.Id] = Box with { X = 800 },
            },
        };
        var config = new NeoFencesConfig
        {
            Fences = [inbox, games, tools, docs],
            Layouts = new Dictionary<string, Layout> { [Setup] = layout },
            LastLayoutFingerprint = Setup,
        };
        return (config, inbox, games, tools, docs);
    }

    private static Fence Get(NeoFencesConfig config, Fence fence) => config.Fences.Single(candidate => candidate.Id == fence.Id);

    private static IReadOnlyDictionary<string, FenceRect> Rects(NeoFencesConfig config) => config.Layouts[Setup].Fences;

    [Fact]
    public void Merge_TwoFences_MakesTheTargetAHostWithBothTabs_TheMovedOneActive()
    {
        var (config, _, games, tools, _) = Sample();

        var merged = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);

        Assert.Equal([games.Id, tools.Id], Get(merged, games).Tabs);
        Assert.Equal(tools.Id, Get(merged, games).ActiveTab);
        Assert.False(Rects(merged).ContainsKey(tools.Id)); // members have no placement of their own
        Assert.True(Rects(merged).ContainsKey(games.Id));
        Assert.Equal(games.Id, FenceTabs.HostOf(merged, tools.Id).Id);
        Assert.True(FenceTabs.IsMember(merged, tools.Id));
        Assert.Equal(Get(config, tools).Items, Get(merged, tools).Items); // items never move
    }

    [Fact]
    public void Merge_IntoABox_InsertsAtThePosition_AndABoxIntoABoxBringsAllItsTabs()
    {
        var (config, inbox, games, tools, docs) = Sample();
        config = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);

        var inserted = FenceTabs.Merge(config, movingFenceId: docs.Id, targetFenceId: tools.Id, insertAt: 1);
        Assert.Equal([games.Id, docs.Id, tools.Id], Get(inserted, games).Tabs);

        var boxIntoBox = FenceTabs.Merge(config, movingFenceId: games.Id, targetFenceId: inbox.Id);
        Assert.Equal([inbox.Id, games.Id, tools.Id], Get(boxIntoBox, inbox).Tabs);
        Assert.Empty(Get(boxIntoBox, games).Tabs);
        Assert.Equal(games.Id, Get(boxIntoBox, inbox).ActiveTab);
    }

    [Fact]
    public void Merge_ATabOfOneBoxIntoAnother_LeavesTheRestOfItsBox()
    {
        var (config, inbox, games, tools, docs) = Sample();
        config = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);
        config = FenceTabs.Merge(config, movingFenceId: docs.Id, targetFenceId: games.Id);

        var moved = FenceTabs.Merge(config, movingFenceId: docs.Id, targetFenceId: inbox.Id);

        Assert.Equal([games.Id, tools.Id], Get(moved, games).Tabs);
        Assert.Equal([inbox.Id, docs.Id], Get(moved, inbox).Tabs);
        Assert.NotEqual(docs.Id, Get(moved, games).ActiveTab);
    }

    [Fact]
    public void Merge_OneTab_TheHostsHeader_MovesOnlyThatTab()
    {
        // Dragging the first (host) tab's header into another box moves that tab, not the whole box (final review C1).
        var (config, _, games, tools, docs) = Sample();
        config = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);

        var moved = FenceTabs.Merge(config, movingFenceId: games.Id, targetFenceId: docs.Id, wholeBox: false);

        Assert.Equal([docs.Id, games.Id], Get(moved, docs).Tabs);
        Assert.Empty(Get(moved, tools).Tabs); // Tools alone again: an ordinary fence with the box's place
        Assert.False(FenceTabs.IsMember(moved, tools.Id));
        Assert.Equal(Box, Rects(moved)[tools.Id]);
    }

    [Fact]
    public void Merge_ClearsTheMovedFencesRollUpAndLock()
    {
        // The box owns roll-up and lock: a tab detached later must not come back rolled up from before the merge.
        var (config, _, games, tools, _) = Sample();
        config = config.WithFence(Get(config, tools) with { RolledUp = true, Locked = true });

        var merged = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);
        var detached = FenceTabs.Detach(merged, tools.Id, fingerprint: Setup, rect: Box with { X = 1400 });

        Assert.False(Get(detached, tools).RolledUp);
        Assert.False(Get(detached, tools).Locked);
    }

    [Fact]
    public void Normalizer_RepairsNullTabs()
    {
        // A hand-edited "tabs": null must not stop NeoFences from starting (final review I2).
        var json = """{ "schemaVersion": 1, "fences": [ { "id": "a", "title": "A", "isInbox": true, "tabs": null } ] }""";

        var normalized = ConfigNormalizer.Normalize(ConfigJson.Deserialize(json));

        Assert.Empty(normalized.Fences.Single().Tabs);
    }

    [Fact]
    public void Merge_IntoItsOwnBox_ChangesNothing()
    {
        var (config, _, games, tools, _) = Sample();
        config = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);

        Assert.Same(config, FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id));
        Assert.Same(config, FenceTabs.Merge(config, movingFenceId: games.Id, targetFenceId: games.Id));
    }

    [Fact]
    public void Detach_AMember_GetsThePlacement_AndALastTabBecomesAnOrdinaryFence()
    {
        var (config, _, games, tools, _) = Sample();
        config = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);
        var dropped = Box with { X = 1400, Y = 600 };

        var detached = FenceTabs.Detach(config, tools.Id, fingerprint: Setup, rect: dropped);

        Assert.Empty(Get(detached, games).Tabs); // one tab left: an ordinary fence again
        Assert.Null(Get(detached, games).ActiveTab);
        Assert.Equal(dropped, Rects(detached)[tools.Id]);
        Assert.False(FenceTabs.IsMember(detached, tools.Id));
    }

    [Fact]
    public void Detach_TheHost_HandsTheBoxToTheNextTab()
    {
        var (config, _, games, tools, docs) = Sample();
        config = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);
        config = FenceTabs.Merge(config, movingFenceId: docs.Id, targetFenceId: games.Id);
        config = config.WithFence(Get(config, games) with { RolledUp = true, Locked = true });
        var dropped = Box with { X = 1400 };

        var detached = FenceTabs.Detach(config, games.Id, fingerprint: Setup, rect: dropped);

        var newHost = Get(detached, tools);
        Assert.Equal([tools.Id, docs.Id], newHost.Tabs);
        Assert.Equal(docs.Id, newHost.ActiveTab); // the active tab stays active
        Assert.True(newHost.RolledUp);
        Assert.True(newHost.Locked);
        Assert.Equal(Box, Rects(detached)[tools.Id]); // the box keeps its place
        Assert.Equal(dropped, Rects(detached)[games.Id]);
        Assert.Empty(Get(detached, games).Tabs);
        Assert.False(Get(detached, games).RolledUp); // the detached fence starts unrolled and unlocked
    }

    [Fact]
    public void Reorder_SetActive_SetColor()
    {
        var (config, _, games, tools, docs) = Sample();
        config = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);
        config = FenceTabs.Merge(config, movingFenceId: docs.Id, targetFenceId: games.Id);

        var reordered = FenceTabs.Reorder(config, fenceId: docs.Id, newIndex: 0);
        Assert.Equal([docs.Id, games.Id, tools.Id], Get(reordered, games).Tabs);

        var active = FenceTabs.SetActive(config, tools.Id);
        Assert.Equal(tools.Id, Get(active, games).ActiveTab);
        Assert.Equal(tools.Id, FenceTabs.ActiveOf(active, games.Id).Id);

        var coloured = FenceTabs.SetColor(config, tools.Id, TabColor.Teal);
        Assert.Equal(TabColor.Teal, Get(coloured, tools).TabColor);
        Assert.Null(Get(FenceTabs.SetColor(coloured, tools.Id, color: null), tools).TabColor);
    }

    [Fact]
    public void TabsOf_ABoxInOrder_AndAStandaloneFenceIsItsOwnOnlyTab()
    {
        var (config, inbox, games, tools, _) = Sample();
        config = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);

        Assert.Equal([games.Id, tools.Id], FenceTabs.TabsOf(config, games.Id).Select(fence => fence.Id));
        Assert.Equal([inbox.Id], FenceTabs.TabsOf(config, inbox.Id).Select(fence => fence.Id));
        Assert.Equal([inbox.Id, games.Id], FenceTabs.Boxes(config).Select(fence => fence.Id).Where(id => id == inbox.Id || id == games.Id));
        Assert.DoesNotContain(tools.Id, FenceTabs.Boxes(config).Select(fence => fence.Id));
    }

    [Fact]
    public void Deleting_ATab_OrAHost_KeepsTheBoxConsistent()
    {
        var (config, inbox, games, tools, docs) = Sample();
        config = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);
        config = FenceTabs.Merge(config, movingFenceId: docs.Id, targetFenceId: games.Id);

        var memberGone = FenceMembership.DeleteFence(config, tools.Id);
        Assert.Equal([games.Id, docs.Id], Get(memberGone, games).Tabs);

        var hostGone = FenceMembership.DeleteFence(config, games.Id);
        Assert.Equal([tools.Id, docs.Id], Get(hostGone, tools).Tabs);
        Assert.Equal(Box, Rects(hostGone)[tools.Id]);
        Assert.Contains(@"C:\Desktop\a.lnk", Get(hostGone, inbox).Items); // items go to the Inbox, as always
    }

    [Fact]
    public void Layout_PlacesBoxes_NotMembers()
    {
        var (config, _, games, tools, _) = Sample();
        config = FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id);

        var (_, layout) = LayoutEngine.Resolve(config, [new DisplayMonitor("mon", 1920, 1080, 100, 1920, 1040, IsPrimary: true)]);

        Assert.True(layout.Fences.ContainsKey(games.Id));
        Assert.False(layout.Fences.ContainsKey(tools.Id));
    }

    [Fact]
    public void Normalizer_RepairsBrokenTabs()
    {
        var (config, inbox, games, tools, docs) = Sample();
        var broken = config with
        {
            Fences =
            [
                inbox,
                games with { Tabs = [tools.Id, "missing", tools.Id], ActiveTab = "missing", TabColor = (TabColor)42 }, // host missing from its own list
                tools,
                docs with { Tabs = [docs.Id, tools.Id] }, // tools already in Games' box
            ],
        };

        var normalized = ConfigNormalizer.Normalize(broken);

        Assert.Equal([games.Id, tools.Id], Get(normalized, games).Tabs);
        Assert.Equal(games.Id, FenceTabs.ActiveOf(normalized, games.Id).Id); // invalid active → the first tab
        Assert.Null(Get(normalized, games).TabColor);
        Assert.Empty(Get(normalized, docs).Tabs); // only itself left: an ordinary fence
    }

    [Fact]
    public void Normalizer_FlattensABoxListedInsideAnotherBox()
    {
        var (config, inbox, games, tools, docs) = Sample();
        var nested = config with
        {
            Fences = [inbox, games with { Tabs = [games.Id, tools.Id] }, tools with { Tabs = [tools.Id, docs.Id] }, docs],
        };

        var normalized = ConfigNormalizer.Normalize(nested);

        Assert.Equal([games.Id, tools.Id, docs.Id], Get(normalized, games).Tabs);
        Assert.Empty(Get(normalized, tools).Tabs);
    }

    [Fact]
    public void Tabs_RoundTripThroughJson_AndAnOldConfigHasNone()
    {
        var (config, _, games, tools, _) = Sample();
        config = FenceTabs.SetColor(FenceTabs.Merge(config, movingFenceId: tools.Id, targetFenceId: games.Id), tools.Id, TabColor.Purple);

        var json = ConfigJson.Serialize(config);
        var loaded = ConfigJson.Deserialize(json);

        Assert.Equal([games.Id, tools.Id], Get(loaded, games).Tabs);
        Assert.Equal(TabColor.Purple, Get(loaded, tools).TabColor);
        Assert.Contains("\"tabColor\": \"purple\"", json);
        Assert.DoesNotContain("tabColor", ConfigJson.Serialize(Sample().Config)); // nothing written for fences without tabs/colours
        Assert.All(ConfigJson.Deserialize(ConfigJson.Serialize(Sample().Config)).Fences, fence => Assert.Empty(fence.Tabs));
    }
}
