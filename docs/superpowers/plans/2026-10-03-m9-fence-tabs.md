# M9 — Fence Tabs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fences-6-style tabs: combine fences into one box by dragging a title onto another, switch, rename, colour, reorder, drop items onto a tab, and drag a tab out again. Released as v1.2.0.

**Architecture:**
- **Core:** every tab stays an ordinary `Fence`. A box is a host fence whose `Tabs` lists its tabs (itself included), plus `ActiveTab`; any fence can have a `TabColor`.
  - All edits live in `FenceTabs`: Merge, Detach, Leave, Reorder, SetActive, SetColor, Repair.
  - The normalizer repairs tab state on load; the layout engine places boxes only; deleting a tab leaves its box first.
- **Shell:** the drop target re-reads the shown fence while an item drag hovers headers.
- **App:** one `FenceWindow` per box. `FenceId` is the shown tab and `BoxId` the host.
  - The window gets a tab strip, header gestures (a `TabGhost` follows a dragged header), and Colour / Detach menu items.
  - `FenceHost.SyncBoxes` keeps windows in line with boxes. Merge-on-move uses a title-row highlight.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335, Serilog, xUnit. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-03-fence-tabs-design.md` (approved 2026-10-03). **Decision:** ADR-029 (new, Task 4).

**Pre-verified (2026-10-03):** every code block below was compiled together (0 warnings, 0 errors), and **296/296 tests pass** (13 new). On the user's desktop, with consent (installed 1.1.2 restored):
- merged "New fence" into the Inbox by a title drag (2 windows → 1);
- switched tabs (31 / 0 items);
- set the colour teal (visible bar);
- after a restart, the tabs and the active tab were kept;
- roll-up 275 → 32 → 275 px;
- an item dropped onto a header landed in that tab;
- dragging the header out detached it (2 windows again);
- quick-hide worked;
- 0 log warnings.

## Global Constraints

- **Hard rules 1, 2, 4, 5, 7:** tabs never touch files. There is no new Win32 (WPF capture and a click-through overlay window via existing `FenceWindowChrome` calls). Membership, reconcile, Portals, Takeover and the watchdog are unchanged.
- **Configs:** old configs load unchanged. No new fields are written for fences without tabs or colours (`DefaultIgnoreCondition = WhenWritingNull`; empty `tabs` lists are written like `items`).
- **One save per action:** one Core edit and one save, after the drop; nothing changes mid-drag.
- **Records compare lists by reference:** `FenceTabs.Repair` returns untouched fences unchanged (ARCHITECTURE "Core contracts").
- **Commits:** single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- **Smokes:** NeoFences 1.1.2 is installed; smokes restore the installed copy. Ask before mouse runs, print TEST RUNNING / TEST COMPLETE, and never type into Windows Terminal.

## Review Focus

1. **A structural change while something is in flight.** A Portal listing for a tab that was just detached or deleted; a tab deleted while its header is being dragged; a rename open on a tab when another is clicked. Expected: no exception; the listing goes to whichever window shows that fence, if any; a cancelled rename.
2. **Merge and detach on a rolled-up, locked or peeking box**, and the host being the shown tab while it is detached. Expected: the box keeps roll-up and lock; the detached fence is unrolled and unlocked; Peek's topmost state holds for every box window.
3. **Mixed DPI and multi-monitor geometry.** `TitleRowContains`, `TabIndexAt` and the ghost size on a 150 % monitor next to a 100 % one; a detach onto the other monitor. Expected: hit tests and drop slots match what the user sees; the detached fence lands on the monitor under the pointer.
4. **Gestures crossing each other.** Header drag versus desktop gestures (double-click quick-hide, right-drag draw), Esc / right-click during a header drag, a header drag ending over the same header. Expected: a click or a cancel changes nothing; no stuck capture or leftover ghost.
5. **Explorer restart, display change, power cut** with boxes. Expected: boxes re-attach and re-place as single fences do; after a power cut the config holds a consistent box (one save per action).

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Model/FenceTabs.cs` (new), `Model/Fence.cs`, `Config/ConfigNormalizer.cs`, `Layouts/LayoutEngine.cs`, `Membership/FenceMembership.cs` + `tests/…/Model/FenceTabsTests.cs` (new), `tests/…/Config/ConfigJsonTests.cs` | tab model and edits | 1 |
| `src/NeoFences.Shell/ShellDragDrop.cs` | drop target follows the shown tab | 2 |
| `src/NeoFences.App/TabGhost.cs` (new), `FenceWindow.xaml(.cs)`, `FenceHost.cs` | tab strip, gestures, boxes | 3 |
| `docs/…` (ADR-029, checklist V, research, ROADMAP, FEATURES, ARCHITECTURE, SESSION-LOG), hub | docs | 4 |

---

### Task 1: Core — the tab model and its edits

**Files:**
- Modify: `docs/ROADMAP.md` (claim).
- Create: `src/NeoFences.Core/Model/FenceTabs.cs`, `tests/NeoFences.Core.Tests/Model/FenceTabsTests.cs`.
- Replace: `src/NeoFences.Core/Model/Fence.cs`, `Config/ConfigNormalizer.cs`, `Layouts/LayoutEngine.cs`, `Membership/FenceMembership.cs`, `tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs`.

**Interfaces:**
- Produces:
  - `enum TabColor { Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink }`; `Fence.Tabs`, `Fence.ActiveTab`, `Fence.TabColor`;
  - `FenceTabs.IsMember(config, fenceId)`, `HostOf(config, fenceId) → Fence`, `Boxes(config)`, `TabsOf(config, hostId)`, `ActiveOf(config, hostId)`;
  - `FenceTabs.Merge(config, movingFenceId, targetFenceId, insertAt = null)`, `Detach(config, fenceId, fingerprint, rect)`, `Leave(config, fenceId)`, `Reorder(config, fenceId, newIndex)`, `SetActive(config, fenceId)`, `SetColor(config, fenceId, color?)`, `Repair(fences)`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m9-fence-tabs
```
In `docs/ROADMAP.md`, before the line `Tasks for M2–M8 are broken down when the previous milestone closes.`, add:
```markdown
## M9 — Fence tabs (v1.2, spec 2026-10-03-fence-tabs-design)
User choices 2026-10-03: combine fences into one box (Fences 6), drag a tab out to split, a per-tab accent colour.
- [x] M9 spec and implementation plan
- [~] M9 — claimed by session 2026-10-03 m9
```
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M9"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Model/FenceTabsTests.cs`:
```csharp
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
```
`tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs` (the round trip also neutralises the new `Tabs` list, which records compare by reference):
```csharp
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

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
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
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/NeoFences.Core.Tests`
Expected: build FAILS: `CS0103 FenceTabs`, `CS0103 TabColor`, `CS0117`/`CS1061` (`Tabs`, `ActiveTab`, `TabColor` on `Fence`).

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Model/FenceTabs.cs`:
```csharp
using NeoFences.Core.Layouts;

namespace NeoFences.Core.Model;

/// <summary>A tab's accent colour (M9): a bar under its header.</summary>
public enum TabColor { Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink }

/// <summary>
/// Fence tabs (M9, spec 2026-10-03-fence-tabs-design): fences combined into one box. Every tab stays a full fence (items,
/// source, sort, icon size, labels). The host — the fence whose <see cref="Fence.Tabs"/> lists two or more ids, itself
/// included — owns the box: its window, its placement in every layout, roll-up and lock. Members have no placement.
/// Every edit returns a new config; nothing changes items or files.
/// </summary>
public static class FenceTabs
{
    /// <summary>True when the fence is a tab of another fence's box.</summary>
    public static bool IsMember(NeoFencesConfig config, string fenceId) => HostListing(config, fenceId) is not null;

    /// <summary>The fence owning the box this fence shows in: its host, or itself when it is not a member.</summary>
    public static Fence HostOf(NeoFencesConfig config, string fenceId) =>
        HostListing(config, fenceId) ?? Find(config, fenceId);

    /// <summary>The fences that get a window: hosts and standalone fences, in config order.</summary>
    public static IReadOnlyList<Fence> Boxes(NeoFencesConfig config) =>
        config.Fences.Where(fence => !IsMember(config, fence.Id)).ToList();

    /// <summary>A box's tabs in order; a standalone fence is its own only tab.</summary>
    public static IReadOnlyList<Fence> TabsOf(NeoFencesConfig config, string hostId)
    {
        var host = Find(config, hostId);
        return host.Tabs.Count > 1 ? host.Tabs.Select(id => Find(config, id)).ToList() : [host];
    }

    /// <summary>The tab a box shows (the host's <see cref="Fence.ActiveTab"/>, else its first tab).</summary>
    public static Fence ActiveOf(NeoFencesConfig config, string hostId)
    {
        var tabs = TabsOf(config, hostId);
        var activeId = Find(config, hostId).ActiveTab;
        return tabs.FirstOrDefault(tab => tab.Id == activeId) ?? tabs[0];
    }

    /// <summary>
    /// Drops a fence onto another fence's box: it becomes a tab there (a box dropped as a whole brings all its tabs, in
    /// order), at <paramref name="insertAt"/> or the end, and is shown. Its own placements go (a member has none). Into
    /// its own box, or onto itself: the same config.
    /// </summary>
    public static NeoFencesConfig Merge(NeoFencesConfig config, string movingFenceId, string targetFenceId, int? insertAt = null)
    {
        var targetHost = HostOf(config, targetFenceId);
        if (HostOf(config, movingFenceId).Id == targetHost.Id) return config;

        List<string> moving;
        if (IsMember(config, movingFenceId))
        {
            config = Leave(config, movingFenceId); // one tab dragged out of another box
            moving = [movingFenceId];
        }
        else
        {
            var movingFence = Find(config, movingFenceId);
            moving = movingFence.Tabs.Count > 1 ? [.. movingFence.Tabs] : [movingFenceId];
            config = config.WithFence(movingFence with { Tabs = [], ActiveTab = null }); // a whole box: its tabs come along
        }

        targetHost = Find(config, targetHost.Id);
        var tabs = targetHost.Tabs.Count > 1 ? targetHost.Tabs.ToList() : [targetHost.Id];
        tabs.InsertRange(Math.Clamp(insertAt ?? tabs.Count, 0, tabs.Count), moving);
        config = config.WithFence(targetHost with { Tabs = tabs, ActiveTab = movingFenceId });
        return WithoutPlacements(config, moving);
    }

    /// <summary>
    /// A tab leaves its box and becomes an ordinary fence at <paramref name="rect"/> in the layout
    /// <paramref name="fingerprint"/> (its placements in other setups go: the layout engine places it there anew).
    /// Detaching the host hands the box (tabs, active tab, roll-up, lock, placements) to the next tab. A fence that is
    /// not in a box: the same config.
    /// </summary>
    public static NeoFencesConfig Detach(NeoFencesConfig config, string fenceId, string fingerprint, FenceRect rect)
    {
        if (TabsOf(config, HostOf(config, fenceId).Id).Count < 2) return config;
        config = WithoutPlacements(Leave(config, fenceId), [fenceId]);
        return LayoutEngine.WithFenceRect(config, fingerprint, fenceId, rect);
    }

    /// <summary>
    /// Takes a tab out of its box without placing it (detach, delete, a move to another box). The host leaving hands the
    /// box to the next tab; a box left with one tab becomes an ordinary fence.
    /// </summary>
    public static NeoFencesConfig Leave(NeoFencesConfig config, string fenceId)
    {
        var host = HostOf(config, fenceId);
        if (host.Tabs.Count < 2) return config;
        var remaining = host.Tabs.Where(id => id != fenceId).ToList();
        var active = host.ActiveTab == fenceId ? null : host.ActiveTab;
        if (host.Id != fenceId)
        {
            return config.WithFence(remaining.Count > 1 ? host with { Tabs = remaining, ActiveTab = active } : host with { Tabs = [], ActiveTab = null });
        }

        var heir = Find(config, remaining[0]);
        heir = remaining.Count > 1
            ? heir with { Tabs = remaining, ActiveTab = active ?? heir.Id, RolledUp = host.RolledUp, Locked = host.Locked }
            : heir with { Tabs = [], ActiveTab = null, RolledUp = host.RolledUp, Locked = host.Locked };
        config = config.WithFence(heir).WithFence(host with { Tabs = [], ActiveTab = null, RolledUp = false, Locked = false });
        // The heir takes the box's place in every setup.
        var layouts = config.Layouts.ToDictionary(entry => entry.Key, entry =>
        {
            if (!entry.Value.Fences.TryGetValue(host.Id, out var boxRect)) return entry.Value;
            var rects = new Dictionary<string, FenceRect>(entry.Value.Fences) { [heir.Id] = boxRect };
            return entry.Value with { Fences = rects };
        });
        return config with { Layouts = layouts };
    }

    /// <summary>Moves a tab to another position in its box (dragging its header along the strip).</summary>
    public static NeoFencesConfig Reorder(NeoFencesConfig config, string fenceId, int newIndex)
    {
        var host = HostOf(config, fenceId);
        if (host.Tabs.Count < 2) return config;
        var tabs = host.Tabs.Where(id => id != fenceId).ToList();
        tabs.Insert(Math.Clamp(newIndex, 0, tabs.Count), fenceId);
        return config.WithFence(host with { Tabs = tabs });
    }

    /// <summary>Shows this tab in its box (remembered across restarts).</summary>
    public static NeoFencesConfig SetActive(NeoFencesConfig config, string fenceId)
    {
        var host = HostOf(config, fenceId);
        return host.Tabs.Count < 2 || host.ActiveTab == fenceId ? config : config.WithFence(host with { ActiveTab = fenceId });
    }

    /// <summary>A tab's accent colour, or none.</summary>
    public static NeoFencesConfig SetColor(NeoFencesConfig config, string fenceId, TabColor? color) =>
        config.WithFence(Find(config, fenceId) with { TabColor = color });

    /// <summary>
    /// Load-time repair (spec §2): unknown and repeated ids go; a fence listed by two boxes stays with the first; a box
    /// listed inside another is flattened into it; a list without its host gets the host first; a box left with one tab
    /// is cleared; an unknown active tab falls back to the first; an unknown colour becomes none.
    /// </summary>
    public static IReadOnlyList<Fence> Repair(IReadOnlyList<Fence> fences)
    {
        var known = fences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        var lists = fences.Where(fence => fence.Tabs.Count > 0).ToDictionary(fence => fence.Id, fence =>
        {
            var ids = fence.Tabs.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (!ids.Contains(fence.Id)) ids.Insert(0, fence.Id);
            return ids;
        });
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        var final = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var fence in fences)
        {
            if (!lists.TryGetValue(fence.Id, out var ids) || owner.ContainsKey(fence.Id)) continue; // absorbed by an earlier box
            var tabs = new List<string>();
            foreach (var id in ids)
            {
                if (owner.ContainsKey(id)) continue;
                AddTab(id);
                // A box listed inside this one: its tabs come along, in order (no nesting).
                if (id != fence.Id && lists.TryGetValue(id, out var nested))
                {
                    foreach (var nestedId in nested.Where(nestedId => !owner.ContainsKey(nestedId))) AddTab(nestedId);
                }
            }
            final[fence.Id] = tabs;

            void AddTab(string id)
            {
                owner[id] = fence.Id;
                tabs.Add(id);
            }
        }

        return fences.Select(fence =>
        {
            // Untouched when there is nothing to repair: records compare lists by reference (Core contract, ARCHITECTURE).
            if (fence.Tabs.Count == 0 && fence.ActiveTab is null && (fence.TabColor is null || Enum.IsDefined(fence.TabColor.Value))) return fence;
            var tabs = final.TryGetValue(fence.Id, out var list) && list.Count > 1 ? list : [];
            return fence with
            {
                Tabs = tabs,
                ActiveTab = tabs.Contains(fence.ActiveTab ?? "") ? fence.ActiveTab : null,
                TabColor = fence.TabColor is { } color && Enum.IsDefined(color) ? color : null,
            };
        }).ToList();
    }

    private static Fence? HostListing(NeoFencesConfig config, string fenceId) =>
        config.Fences.FirstOrDefault(host => host.Id != fenceId && host.Tabs.Count > 1 && host.Tabs.Contains(fenceId));

    private static Fence Find(NeoFencesConfig config, string fenceId) =>
        config.Fences.FirstOrDefault(fence => fence.Id == fenceId) ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));

    private static NeoFencesConfig WithoutPlacements(NeoFencesConfig config, IReadOnlyCollection<string> fenceIds) =>
        config with
        {
            Layouts = config.Layouts.ToDictionary(entry => entry.Key, entry => entry.Value with
            {
                Fences = entry.Value.Fences.Where(rect => !fenceIds.Contains(rect.Key)).ToDictionary(rect => rect.Key, rect => rect.Value),
            }),
        };
}
```
`src/NeoFences.Core/Model/Fence.cs`:
```csharp
namespace NeoFences.Core.Model;

public enum FenceSourceKind { Desktop, Portal }

/// <summary>Where a fence's items come from: the desktop (Takeover) or a folder (Portal).</summary>
public sealed record FenceSource(FenceSourceKind Kind, string? Path = null)
{
    public static FenceSource Desktop { get; } = new(FenceSourceKind.Desktop);

    public static FenceSource Portal(string folderPath) => new(FenceSourceKind.Portal, folderPath);
}

public enum FenceSort { Manual, Name, Type, Date }

/// <summary>Item names under the icons: always, or only for the hovered or selected item (icon-only fences, M8b).</summary>
public enum LabelMode { Always, OnHover }

/// <summary>
/// One fence. Desktop fences keep an ordered list of item refs (shell parsing names: file paths or
/// "::{GUID}" for virtual items). Portal fences keep no items; they mirror <see cref="FenceSource.Path"/>.
/// </summary>
public sealed record Fence
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public FenceSource Source { get; init; } = FenceSource.Desktop;
    public IReadOnlyList<string> Items { get; init; } = [];
    public bool IsInbox { get; init; }
    public FenceSort Sort { get; init; } = FenceSort.Manual;
    public int IconSize { get; init; } = 48;
    public bool RolledUp { get; init; }
    public bool Locked { get; init; }
    public LabelMode Labels { get; init; } = LabelMode.Always;

    /// <summary>Only on a box's host (M9): every tab of the box in order, itself included. Empty = not a box.</summary>
    public IReadOnlyList<string> Tabs { get; init; } = [];

    /// <summary>On a box's host (M9): the tab shown; null or unknown = the first.</summary>
    public string? ActiveTab { get; init; }

    /// <summary>This fence's tab accent (M9), or none.</summary>
    public TabColor? TabColor { get; init; }

    public static Fence Create(string title, FenceSource? source = null) =>
        new() { Id = NewId(), Title = title, Source = source ?? FenceSource.Desktop };

    public static string NewId() => Guid.NewGuid().ToString("N");
}
```
`src/NeoFences.Core/Config/ConfigNormalizer.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>
/// Repairs a loaded (possibly hand-edited) config without losing items: exactly one Inbox, unique fence
/// ids, no nulls, supported icon sizes and enum values, each desktop item in at most one fence, no items on
/// portal fences, the Inbox always a desktop fence, and layouts free of null or non-finite entries.
/// </summary>
public static class ConfigNormalizer
{
    public static IReadOnlyList<int> IconSizes { get; } = [32, 48, 64, 96];

    public static NeoFencesConfig Normalize(NeoFencesConfig config)
    {
        var defaults = new Settings();
        var settings = config.Settings ?? defaults;
        settings = settings with
        {
            PeekHotkey = string.IsNullOrWhiteSpace(settings.PeekHotkey) ? defaults.PeekHotkey : settings.PeekHotkey,
            RollupExpand = Enum.IsDefined(settings.RollupExpand) ? settings.RollupExpand : defaults.RollupExpand,
            DefaultLabels = Enum.IsDefined(settings.DefaultLabels) ? settings.DefaultLabels : defaults.DefaultLabels, // M8b review
        };

        var seenFenceIds = new HashSet<string>(StringComparer.Ordinal);
        var seenItems = new HashSet<string>(ItemRef.Comparer);
        var inboxFound = false;
        var fences = new List<Fence>();

        foreach (var loadedFence in config.Fences ?? [])
        {
            if (loadedFence is null) continue;
            var isInbox = loadedFence.IsInbox && !inboxFound;
            var source = loadedFence.Source is { } loadedSource && Enum.IsDefined(loadedSource.Kind) ? loadedSource : FenceSource.Desktop;
            var fence = loadedFence with
            {
                Id = string.IsNullOrWhiteSpace(loadedFence.Id) || !seenFenceIds.Add(loadedFence.Id) ? Fence.NewId() : loadedFence.Id,
                Title = loadedFence.Title ?? "",
                Source = isInbox ? FenceSource.Desktop : source,
                IsInbox = isInbox,
                Sort = Enum.IsDefined(loadedFence.Sort) ? loadedFence.Sort : FenceSort.Manual,
                Labels = Enum.IsDefined(loadedFence.Labels) ? loadedFence.Labels : LabelMode.Always, // a hand-edited number (M8b review)
                IconSize = IconSizes.Contains(loadedFence.IconSize) ? loadedFence.IconSize : 48,
            };
            seenFenceIds.Add(fence.Id);
            inboxFound |= fence.IsInbox;

            var items = fence.Source.Kind == FenceSourceKind.Desktop
                ? (loadedFence.Items ?? []).Where(itemRef => !string.IsNullOrWhiteSpace(itemRef) && seenItems.Add(itemRef)).ToList()
                : [];
            fences.Add(fence with { Items = items });
        }

        if (!inboxFound)
        {
            fences.Insert(0, Fence.Create("Inbox") with { IsInbox = true });
        }

        return config with
        {
            Settings = settings,
            Fences = FenceTabs.Repair(fences), // M9: one consistent box per tab
            Layouts = NormalizeLayouts(config.Layouts),
        };
    }

    private static Dictionary<string, Layout> NormalizeLayouts(IReadOnlyDictionary<string, Layout>? loadedLayouts)
    {
        var layouts = new Dictionary<string, Layout>();
        foreach (var (fingerprint, layout) in loadedLayouts ?? new Dictionary<string, Layout>())
        {
            if (layout is null) continue;
            layouts[fingerprint] = new Layout
            {
                Monitors = (layout.Monitors ?? new Dictionary<string, MonitorArea>())
                    .Where(entry => entry.Value is { IsUsable: true })
                    .ToDictionary(entry => entry.Key, entry => entry.Value),
                Fences = (layout.Fences ?? new Dictionary<string, FenceRect>())
                    .Where(entry => entry.Value is { } rect && !string.IsNullOrWhiteSpace(rect.Monitor)
                                    && double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.W) && double.IsFinite(rect.H))
                    .ToDictionary(entry => entry.Key, entry => entry.Value),
            };
        }
        return layouts;
    }
}
```
`src/NeoFences.Core/Layouts/LayoutEngine.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Layouts;

/// <summary>
/// Picks fence rectangles for the current display configuration (spec §5): a known fingerprint is
/// restored exactly; an unknown one is derived from the last layout, scaled per monitor, clamped
/// on-screen and saved under the new fingerprint.
/// </summary>
public static class LayoutEngine
{
    public const double DefaultWidth = 320;
    public const double DefaultHeight = 220;
    public const double MinWidth = 120;
    public const double MinHeight = 60;
    public const double NewFenceMargin = 24;
    public const double CascadeStep = 32;
    /// <summary>Space kept between a new fence and the others (the snapping gap, spec §6 smart placement).</summary>
    public const double PlacementGap = 8;

    public static (NeoFencesConfig Config, Layout Layout) Resolve(NeoFencesConfig config, IReadOnlyList<DisplayMonitor> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        // A monitor query during an Explorer restart or driver reset can return garbage: never let it reach a saved layout.
        if (monitors.FirstOrDefault(monitor => !new MonitorArea(monitor.WorkWidth, monitor.WorkHeight).IsUsable) is { } unusable)
        {
            throw new ArgumentException($"Monitor {unusable.DeviceId} reported an unusable work area {unusable.WorkWidth}x{unusable.WorkHeight}.", nameof(monitors));
        }

        var fingerprint = DisplayFingerprint.Of(monitors);
        var areas = monitors.ToDictionary(monitor => monitor.DeviceId, monitor => new MonitorArea(monitor.WorkWidth, monitor.WorkHeight));
        var primary = monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? monitors[0];

        var rects = config.Layouts.TryGetValue(fingerprint, out var known)
            ? new Dictionary<string, FenceRect>(known.Fences)
            : config.LastLayoutFingerprint is { } lastFingerprint && config.Layouts.TryGetValue(lastFingerprint, out var previous)
                ? Adapt(previous: previous, areas: areas, primaryId: primary.DeviceId)
                : new Dictionary<string, FenceRect>();

        // A known setup that misses fences made on another setup since (laptop vs. dock): those keep their place from
        // the last layout, scaled, instead of being placed anew (M8a).
        if (known is not null && config.LastLayoutFingerprint is { } lastSeen && lastSeen != fingerprint
            && config.Layouts.TryGetValue(lastSeen, out var lastLayout))
        {
            foreach (var (fenceId, rect) in Adapt(previous: lastLayout, areas: areas, primaryId: primary.DeviceId))
            {
                rects.TryAdd(fenceId, rect);
            }
        }

        var fenceIds = config.Fences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var removedFenceId in rects.Keys.Where(fenceId => !fenceIds.Contains(fenceId)).ToList())
        {
            rects.Remove(removedFenceId);
        }

        // Stored rects stay as the user left them; only the returned (display) layout is clamped, so a
        // temporarily smaller work area (taskbar moved, resolution blip) never shifts positions for good.
        var displayRects = new Dictionary<string, FenceRect>();
        var cascadeIndex = 0;
        foreach (var fence in FenceTabs.Boxes(config)) // tabs of a box share the host's placement (M9)
        {
            if (!rects.TryGetValue(fence.Id, out var rect) || !areas.ContainsKey(rect.Monitor))
            {
                var occupied = rects.Values.Where(placed => placed.Monitor == primary.DeviceId).ToList();
                var offset = NewFenceMargin + CascadeStep * cascadeIndex++;
                rect = FreeSpot(primary.DeviceId, areas[primary.DeviceId], occupied)
                       ?? new FenceRect(primary.DeviceId, offset, offset, DefaultWidth, DefaultHeight); // full screen: cascade, clamped below
                rects[fence.Id] = rect;
            }
            displayRects[fence.Id] = Clamp(rect, areas[rect.Monitor]);
        }

        var storedLayout = new Layout { Monitors = areas, Fences = rects };
        var layouts = new Dictionary<string, Layout>(config.Layouts) { [fingerprint] = storedLayout };
        return (config with { Layouts = layouts, LastLayoutFingerprint = fingerprint }, new Layout { Monitors = areas, Fences = displayRects });
    }

    /// <summary>
    /// Smart placement (M5): the first spot, reading top-left to bottom-right, where a default-size fence fits on the
    /// monitor without touching any other (keeping <see cref="PlacementGap"/>). Candidates are the margin and the spots
    /// just right of / below existing fences, so new fences line up with them. Null when the monitor is full.
    /// </summary>
    public static FenceRect? FreeSpot(string monitor, MonitorArea area, IReadOnlyList<FenceRect> occupied)
    {
        var columns = occupied.Select(other => other.X + other.W + PlacementGap).Prepend(NewFenceMargin).Distinct().Order().ToList();
        var rows = occupied.Select(other => other.Y + other.H + PlacementGap).Prepend(NewFenceMargin).Distinct().Order().ToList();
        foreach (var y in rows)
        {
            foreach (var x in columns)
            {
                if (x + DefaultWidth > area.WorkWidth || y + DefaultHeight > area.WorkHeight) continue;
                var touches = occupied.Any(other =>
                    x < other.X + other.W + PlacementGap && x + DefaultWidth + PlacementGap > other.X
                    && y < other.Y + other.H + PlacementGap && y + DefaultHeight + PlacementGap > other.Y);
                if (!touches) return new FenceRect(monitor, x, y, DefaultWidth, DefaultHeight);
            }
        }
        return null;
    }

    /// <summary>Stores a fence's rect (e.g. after the user moved it) under <paramref name="fingerprint"/>.</summary>
    public static NeoFencesConfig WithFenceRect(NeoFencesConfig config, string fingerprint, string fenceId, FenceRect rect)
    {
        var layout = config.Layouts.TryGetValue(fingerprint, out var existing) ? existing : new Layout();
        var fences = new Dictionary<string, FenceRect>(layout.Fences) { [fenceId] = rect };
        var layouts = new Dictionary<string, Layout>(config.Layouts) { [fingerprint] = layout with { Fences = fences } };
        return config with { Layouts = layouts };
    }

    /// <summary>Keeps each fence on its monitor if that monitor still exists (else the primary), scaled to the new work area.</summary>
    private static Dictionary<string, FenceRect> Adapt(Layout previous, IReadOnlyDictionary<string, MonitorArea> areas, string primaryId)
    {
        var adapted = new Dictionary<string, FenceRect>();
        foreach (var (fenceId, rect) in previous.Fences)
        {
            var targetId = areas.ContainsKey(rect.Monitor) ? rect.Monitor : primaryId;
            var target = areas[targetId];
            var source = previous.Monitors.TryGetValue(rect.Monitor, out var savedArea) && savedArea.IsUsable ? savedArea : target;
            var scaleX = target.WorkWidth / source.WorkWidth;
            var scaleY = target.WorkHeight / source.WorkHeight;
            adapted[fenceId] = new FenceRect(targetId, rect.X * scaleX, rect.Y * scaleY, rect.W * scaleX, rect.H * scaleY);
        }
        return adapted;
    }

    /// <summary>Fits the rect inside the work area: never larger than it, never past its edges.</summary>
    public static FenceRect Clamp(FenceRect rect, MonitorArea area)
    {
        var width = Math.Min(Math.Max(rect.W, MinWidth), area.WorkWidth);
        var height = Math.Min(Math.Max(rect.H, MinHeight), area.WorkHeight);
        var x = Math.Clamp(rect.X, 0, area.WorkWidth - width);
        var y = Math.Clamp(rect.Y, 0, area.WorkHeight - height);
        return rect with { X = x, Y = y, W = width, H = height };
    }
}
```
`src/NeoFences.Core/Membership/FenceMembership.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Membership;

/// <param name="Suspicious">True when part of the desktop could not be listed (an unreadable folder, or an empty listing): the affected memberships were kept, not pruned.</param>
public sealed record ReconcileReport(IReadOnlyList<string> Removed, IReadOnlyList<string> AddedToInbox, bool Suspicious = false)
{
    /// <summary>Remembered placements this reconcile applied: the caller drops them, so they are used once (M8a review).</summary>
    public IReadOnlyList<RememberedPlacement> UsedMemories { get; init; } = [];
}

/// <summary>
/// Which desktop item lives in which fence (spec §5). Pure functions: each returns a new config.
/// Item refs compare case-insensitively (<see cref="ItemRef.Comparer"/>). Portal fences hold no items.
/// </summary>
public static class FenceMembership
{
    /// <summary>
    /// Startup reconcile: drops refs that no longer exist, adopts the shell's current spelling, keeps each
    /// item in its first fence only, and appends unknown items to the Inbox in the order given.
    /// Refs under <paramref name="unavailableFolders"/> (Desktop folders the shell layer could not list: offline
    /// redirect, unmounted OneDrive) are kept, and so is everything if the listing is empty; the report is then
    /// flagged. Everything else missing from a readable folder is dropped (M2b review: no ghosts), except:
    /// <list type="bullet">
    /// <item>a missing item whose file name now exists exactly once elsewhere, unfenced, takes that new path in place: a
    /// moved Desktop folder (OneDrive Known Folder Move) keeps every arrangement (M8a);</item>
    /// <item>an unknown item with an unexpired <paramref name="remembered"/> placement returns there instead of the Inbox:
    /// a safe-save whose watcher events were lost (M8a).</item>
    /// </list>
    /// </summary>
    public static (NeoFencesConfig Config, ReconcileReport Report) Reconcile(
        NeoFencesConfig config, IEnumerable<string> desktopItems, IReadOnlyCollection<string>? unavailableFolders = null,
        IReadOnlyList<RememberedPlacement>? remembered = null, DateTimeOffset? now = null)
    {
        var presentSpelling = new Dictionary<string, string>(ItemRef.Comparer);
        var presentOrder = new List<string>();
        foreach (var desktopItem in desktopItems)
        {
            if (presentSpelling.TryAdd(desktopItem, desktopItem)) presentOrder.Add(desktopItem);
        }

        var unlistedPrefixes = (unavailableFolders ?? []).Select(folder => folder.TrimEnd('\\') + "\\").ToList();
        bool MayStillExist(string itemRef) =>
            presentOrder.Count == 0 || unlistedPrefixes.Any(prefix => itemRef.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        var suspicious = false;

        // Present but not yet fenced, by file name: candidates for an item whose folder moved.
        var fencedPresent = config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop)
            .SelectMany(fence => fence.Items).Where(presentSpelling.ContainsKey).ToHashSet(ItemRef.Comparer);
        var unfencedByName = presentOrder
            .Where(itemRef => !fencedPresent.Contains(itemRef) && !itemRef.StartsWith("::", StringComparison.Ordinal))
            .GroupBy(itemRef => Path.GetFileName(itemRef), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var placed = new HashSet<string>(ItemRef.Comparer);
        var removed = new List<string>();
        var fences = config.Fences.Select(fence =>
        {
            if (fence.Source.Kind != FenceSourceKind.Desktop) return fence;
            var kept = new List<string>();
            foreach (var itemRef in fence.Items)
            {
                if (presentSpelling.TryGetValue(itemRef, out var currentSpelling))
                {
                    if (placed.Add(currentSpelling)) kept.Add(currentSpelling);
                }
                else if (MayStillExist(itemRef))
                {
                    suspicious = true;
                    if (placed.Add(itemRef)) kept.Add(itemRef);
                }
                else if (!itemRef.StartsWith("::", StringComparison.Ordinal)
                         && unfencedByName.TryGetValue(Path.GetFileName(itemRef), out var candidates)
                         && candidates.Count == 1 && placed.Add(candidates[0]))
                {
                    kept.Add(candidates[0]); // same name, new folder: the same item moved (an ambiguous name is never guessed)
                }
                else
                {
                    removed.Add(itemRef);
                }
            }
            return fence with { Items = kept };
        }).ToList();

        var reconciled = config with { Fences = fences };
        var live = (remembered ?? []).Where(placement => now is null || placement.ExpiresAt >= now).ToList();
        var added = new List<string>();
        var returning = new List<RememberedPlacement>();
        foreach (var itemRef in presentOrder.Where(itemRef => !placed.Contains(itemRef)))
        {
            var memory = live.LastOrDefault(placement => ItemRef.Comparer.Equals(placement.ItemRef, itemRef));
            if (memory is not null && reconciled.Fences.Any(fence => fence.Id == memory.FenceId && fence.Source.Kind == FenceSourceKind.Desktop))
            {
                returning.Add(memory with { ItemRef = itemRef }); // the shell's current spelling
            }
            else
            {
                added.Add(itemRef);
            }
        }
        // By index, not listing order: several arrivals for one fence land where they were dropped (M8a review).
        foreach (var memory in returning.OrderBy(memory => memory.Index))
        {
            var home = reconciled.Fences.First(fence => fence.Id == memory.FenceId);
            var items = home.Items.ToList();
            items.Insert(Math.Clamp(memory.Index, 0, items.Count), memory.ItemRef);
            reconciled = reconciled.WithFence(home with { Items = items });
        }
        var usedMemories = live.Where(memory => returning.Any(used => ItemRef.Comparer.Equals(used.ItemRef, memory.ItemRef))).ToList();
        if (added.Count > 0)
        {
            reconciled = reconciled.WithFence(reconciled.Inbox with { Items = [.. reconciled.Inbox.Items, .. added] });
        }
        return (reconciled, new ReconcileReport(removed, added, suspicious) { UsedMemories = usedMemories });
    }

    /// <summary>Applies one watcher event: created → Inbox, deleted → removed, renamed → same fence and position.</summary>
    public static NeoFencesConfig Apply(NeoFencesConfig config, DesktopChange change) => change switch
    {
        DesktopChange.Created created => AddItem(config, created.ItemRef),
        DesktopChange.Deleted deleted => RemoveItem(config, deleted.ItemRef),
        DesktopChange.Renamed renamed => RenameItem(config, renamed.OldRef, renamed.NewRef),
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown desktop change."),
    };

    /// <summary>How long a removed item's fence and position are remembered, so a replace-save puts it back (M3a).</summary>
    public static readonly TimeSpan SafeSaveWindow = TimeSpan.FromSeconds(5);

    /// <summary>How long files dropped from Explorer are expected: Windows creates each when it starts copying it (M3b review I1).</summary>
    public static readonly TimeSpan ArrivalWindow = TimeSpan.FromMinutes(3);

    /// <summary>
    /// <see cref="Apply(NeoFencesConfig, DesktopChange)"/> that also survives "safe saves": editors replace a file by
    /// deleting (or renaming away) the original and creating (or renaming a temp file onto) the same name. A fenced item
    /// that disappears is remembered for <see cref="SafeSaveWindow"/>; if the same ref comes back in that time it returns
    /// to its fence and position instead of the Inbox. Keep the returned list and pass it to the next call.
    /// </summary>
    public static (NeoFencesConfig Config, IReadOnlyList<RememberedPlacement> Recent) Apply(
        NeoFencesConfig config, DesktopChange change, IReadOnlyList<RememberedPlacement> recent, DateTimeOffset now)
    {
        var remembered = recent.Where(placement => placement.ExpiresAt >= now).ToList();
        switch (change)
        {
            case DesktopChange.Deleted deleted when FindOwner(config, deleted.ItemRef) is { } deletedOwner:
                var index = deletedOwner.Items.ToList().FindIndex(existing => ItemRef.Comparer.Equals(existing, deleted.ItemRef));
                remembered.Add(new RememberedPlacement(deleted.ItemRef, deletedOwner.Id, index, now + SafeSaveWindow));
                return (RemoveItem(config, deleted.ItemRef), remembered);
            // A temp file renamed onto a name that just disappeared (Excel, LibreOffice, atomic writers): the temp may
            // already sit in the Inbox from its Created event; drop it and put the returning name back (M3a review I1).
            case DesktopChange.Renamed renamed
                when FindOwner(config, renamed.NewRef) is null
                     && remembered.Any(removal => ItemRef.Comparer.Equals(removal.ItemRef, renamed.NewRef)):
                return (Return(RemoveItem(config, renamed.OldRef), renamed.NewRef, remembered), remembered);
            case DesktopChange.Renamed renamed when FindOwner(config, renamed.OldRef) is { } owner:
                // Word renames the original away before renaming its temp file onto the old name: remember the old name too.
                var oldIndex = owner.Items.ToList().FindIndex(existing => ItemRef.Comparer.Equals(existing, renamed.OldRef));
                remembered.Add(new RememberedPlacement(renamed.OldRef, owner.Id, oldIndex, now + SafeSaveWindow));
                return (RenameItem(config, renamed.OldRef, renamed.NewRef), remembered);
            case DesktopChange.Created created when FindOwner(config, created.ItemRef) is null:
                return (Return(config, created.ItemRef, remembered), remembered);
            case DesktopChange.Renamed renamed when FindOwner(config, renamed.OldRef) is null && FindOwner(config, renamed.NewRef) is null:
                return (Return(config, renamed.NewRef, remembered), remembered);
            default:
                return (Apply(config, change), remembered);
        }
    }

    /// <summary>Puts an appearing item back where it was removed from moments ago, or in the Inbox. Consumes the memory.</summary>
    private static NeoFencesConfig Return(NeoFencesConfig config, string itemRef, List<RememberedPlacement> remembered)
    {
        var removal = remembered.FindLast(candidate => ItemRef.Comparer.Equals(candidate.ItemRef, itemRef));
        if (removal is null) return AddItem(config, itemRef);
        remembered.Remove(removal);
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == removal.FenceId);
        return fence is { Source.Kind: FenceSourceKind.Desktop }
            ? MoveItem(config, itemRef, fence.Id, removal.Index)
            : AddItem(config, itemRef);
    }

    /// <summary>A new desktop item appeared: goes to <paramref name="targetFenceId"/> (a drop target) or the Inbox. No-op if already fenced.</summary>
    public static NeoFencesConfig AddItem(NeoFencesConfig config, string itemRef, string? targetFenceId = null)
    {
        if (FindOwner(config, itemRef) is not null) return config;
        var target = targetFenceId is null ? config.Inbox : RequireDesktopFence(config, targetFenceId);
        return config.WithFence(target with { Items = [.. target.Items, itemRef] });
    }

    /// <summary>A desktop item was deleted.</summary>
    public static NeoFencesConfig RemoveItem(NeoFencesConfig config, string itemRef)
    {
        var owner = FindOwner(config, itemRef);
        return owner is null
            ? config
            : config.WithFence(owner with { Items = owner.Items.Where(existing => !ItemRef.Comparer.Equals(existing, itemRef)).ToList() });
    }

    /// <summary>A desktop item was renamed (including case-only renames). Unknown old ref: treated as a new item.</summary>
    public static NeoFencesConfig RenameItem(NeoFencesConfig config, string oldRef, string newRef)
    {
        var owner = FindOwner(config, oldRef);
        if (owner is null) return AddItem(config, newRef);
        // New name already fenced (an event storm can deliver its create before the rename): keep that entry only.
        if (!ItemRef.Comparer.Equals(oldRef, newRef) && FindOwner(config, newRef) is not null) return RemoveItem(config, oldRef);
        return config.WithFence(owner with
        {
            Items = owner.Items.Select(existing => ItemRef.Comparer.Equals(existing, oldRef) ? newRef : existing).ToList(),
        });
    }

    /// <summary>
    /// Drag-drop of fence items (M3b). Moves them, in the order they had in their fences (not the selection order), to
    /// <paramref name="toFenceId"/>, before the item shown at <paramref name="insertAt"/> in the target as displayed
    /// during the drag (still including the dragged items). Past the end appends; unknown refs are ignored.
    /// </summary>
    /// <exception cref="ArgumentException">The target is unknown or a Portal fence.</exception>
    public static NeoFencesConfig MoveItems(NeoFencesConfig config, IReadOnlyList<string> itemRefs, string toFenceId, int insertAt)
    {
        var target = RequireDesktopFence(config, toFenceId);
        var moving = new HashSet<string>(itemRefs, ItemRef.Comparer);
        var ordered = config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop)
            .SelectMany(fence => fence.Items).Where(moving.Contains).ToList();
        if (ordered.Count == 0) return config;

        var displayedIndex = Math.Clamp(insertAt, 0, target.Items.Count);
        var movingBeforeDrop = target.Items.Take(displayedIndex).Count(moving.Contains);
        var withoutMoving = config with
        {
            Fences = config.Fences.Select(fence => fence.Source.Kind == FenceSourceKind.Desktop
                ? fence with { Items = fence.Items.Where(itemRef => !moving.Contains(itemRef)).ToList() }
                : fence).ToList(),
        };
        var remainingTarget = withoutMoving.Fences.First(fence => fence.Id == toFenceId);
        var items = remainingTarget.Items.ToList();
        items.InsertRange(displayedIndex - movingBeforeDrop, ordered);
        return withoutMoving.WithFence(remainingTarget with { Items = items });
    }

    /// <summary>
    /// Files were dropped onto a fence from outside (Explorer) and Windows is copying or moving them to the Desktop:
    /// when each appears within <see cref="ArrivalWindow"/> it goes to that fence at the drop position, in order.
    /// </summary>
    public static IReadOnlyList<RememberedPlacement> ExpectArrivals(
        IReadOnlyList<RememberedPlacement> recent, IReadOnlyList<string> itemRefs, string fenceId, int insertAt, DateTimeOffset now) =>
        [.. recent.Where(placement => placement.ExpiresAt >= now),
         .. itemRefs.Select((itemRef, offset) => new RememberedPlacement(itemRef, fenceId, insertAt + offset, now + ArrivalWindow))];

    /// <summary>Moves an item to another desktop fence (or reorders within one). Index is clamped; null appends.</summary>
    public static NeoFencesConfig MoveItem(NeoFencesConfig config, string itemRef, string toFenceId, int? index = null)
    {
        var target = RequireDesktopFence(config, toFenceId);
        var withoutItem = RemoveItem(config, itemRef);
        target = withoutItem.Fences.First(fence => fence.Id == toFenceId);
        var items = target.Items.ToList();
        items.Insert(Math.Clamp(index ?? items.Count, 0, items.Count), itemRef);
        return withoutItem.WithFence(target with { Items = items });
    }

    public static (NeoFencesConfig Config, Fence Fence) CreateFence(NeoFencesConfig config, string title, FenceSource? source = null)
    {
        var fence = Fence.Create(title, source) with { Labels = config.Settings.DefaultLabels };
        return (config with { Fences = [.. config.Fences, fence] }, fence);
    }

    /// <summary>A Portal: a fence that shows a folder live (M4). New Portals sort newest first (user choice 2026-10-03).</summary>
    /// <exception cref="ArgumentException">No folder given.</exception>
    public static (NeoFencesConfig Config, Fence Fence) CreatePortal(NeoFencesConfig config, string title, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("A Portal needs a folder.", nameof(folderPath));
        var portal = Fence.Create(title, FenceSource.Portal(folderPath)) with { Sort = FenceSort.Date, Labels = config.Settings.DefaultLabels };
        return (config with { Fences = [.. config.Fences, portal] }, portal);
    }

    /// <summary>Deletes a fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
    /// <exception cref="InvalidOperationException">The fence is the Inbox.</exception>
    public static NeoFencesConfig DeleteFence(NeoFencesConfig config, string fenceId)
    {
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == fenceId)
                    ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
        if (fence.IsInbox) throw new InvalidOperationException("The Inbox cannot be deleted.");

        config = FenceTabs.Leave(config, fenceId); // a tab leaves its box first; a host hands the box to the next tab (M9)
        var remaining = config with { Fences = config.Fences.Where(candidate => candidate.Id != fenceId).ToList() };
        return remaining.WithFence(remaining.Inbox with { Items = [.. remaining.Inbox.Items, .. fence.Items] });
    }

    private static Fence? FindOwner(NeoFencesConfig config, string itemRef) =>
        config.Fences.FirstOrDefault(fence => fence.Items.Contains(itemRef, ItemRef.Comparer));

    private static Fence RequireDesktopFence(NeoFencesConfig config, string fenceId)
    {
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == fenceId)
                    ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
        return fence.Source.Kind == FenceSourceKind.Desktop
            ? fence
            : throw new ArgumentException("Portal fences hold no items; moving into them is a file operation.", nameof(fenceId));
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 296`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "feat: added fence tabs to the core model with merge, detach, reorder, colours and load-time repair"
```

---

### Task 2: Shell — the drop target follows the shown tab

**Files:** replace `src/NeoFences.Shell/ShellDragDrop.cs`.

**Interfaces:** none new (the existing `portalFolder` callback is now re-read on every drag step).

- [ ] **Step 1: File**

`src/NeoFences.Shell/ShellDragDrop.cs`:
```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace NeoFences.Shell;

/// <summary>Where a drop on a fence lands, asked of the fence for a screen point (physical pixels).</summary>
/// <param name="ItemRef">The item whose "drop into" zone is under the point (Core DropZones), if any.</param>
/// <param name="InsertAt">Index in the fence's shown list to insert before (see FenceMembership.MoveItems).</param>
public readonly record struct FenceDropPoint(string? ItemRef, int InsertAt);

/// <summary>What a fence does with drops; NeoFences.App supplies these (all called on the UI thread).</summary>
/// <param name="HitTest">Item and insert position under a screen point.</param>
/// <param name="MoveItems">Desktop items dropped on the fence (from a fence or from Explorer's Desktop): membership only, no file operation.</param>
/// <param name="ExpectArrivals">Desktop refs Windows is about to copy or move onto the Desktop for this drop, and where they go.</param>
/// <param name="Recycle">Files dropped on the Recycle Bin item: always recycled by NeoFences, never deleted (hard rule 1).</param>
/// <param name="ShowFeedback">Where the drop would land (null hides it): an insertion caret, or a highlighted container when into is true.</param>
/// <param name="LogFailure">A drop that could not be handed to Windows.</param>
public sealed record FenceDropHandlers(
    Func<int, int, FenceDropPoint> HitTest,
    Action<IReadOnlyList<string>, int> MoveItems,
    Action<IReadOnlyList<string>, int> ExpectArrivals,
    Action<IReadOnlyList<string>> Recycle,
    Action<FenceDropPoint?, bool> ShowFeedback,
    Action<Exception> LogFailure);

/// <summary>
/// Drag-drop with Windows' own engine (spec §6, M3b). Dragging out uses the shell's data object for the items, so
/// apps and Explorer get real files (copy/move/link as they decide) and Windows draws the drag image. Drops on a fence:
/// <list type="bullet">
/// <item>desktop items (from any fence, or Explorer showing the Desktop folder) only change membership: no file operation;</item>
/// <item>on an item that takes drops (folder, Recycle Bin, program) they go to that item, as on the desktop;</item>
/// <item>anything else goes to the Desktop folder's own drop target (Windows copies/moves, with progress and Undo),
/// and the arriving files are placed in this fence at the drop position.</item>
/// </list>
/// </summary>
public static class ShellDragDrop
{
    /// <summary>Items being dragged out of a fence right now (one drag at a time; drops on fences are membership moves).</summary>
    internal static IReadOnlyList<string>? CurrentDrag { get; private set; }

    /// <summary>Starts a drag of these items; returns when it ends. The drag image, cursor and effects are Windows'.</summary>
    /// <returns>False when the drag could not start (an item vanished, a broken shell extension).</returns>
    public static unsafe bool TryDrag(nint ownerHandle, IReadOnlyList<string> itemRefs, Action<Exception> logFailure)
    {
        ComDataObject? dataObject = null;
        try
        {
            dataObject = (ComDataObject)DesktopNamespace.GetUIObject((HWND)ownerHandle, itemRefs, typeof(ComDataObject).GUID);
            CurrentDrag = itemRefs;
            PInvoke.SHDoDragDrop((HWND)ownerHandle, dataObject, null,
                DROPEFFECT.DROPEFFECT_COPY | DROPEFFECT.DROPEFFECT_MOVE | DROPEFFECT.DROPEFFECT_LINK, out _);
            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            logFailure(failure);
            return false;
        }
        finally
        {
            CurrentDrag = null;
            if (dataObject is not null) Marshal.ReleaseComObject(dataObject);
        }
    }

    /// <summary>Makes the fence window a drop target. Dispose (before the window is destroyed) to unregister.</summary>
    /// <param name="portalFolder">For a Portal fence: the folder it shows right now (drops become real moves/copies into it, M4).</param>
    public static IDisposable RegisterFence(nint fenceHandle, FenceDropHandlers handlers, Func<string?>? portalFolder = null)
    {
        var target = new FenceDropTarget((HWND)fenceHandle, handlers, portalFolder);
        // WPF registers its own drop target on every window; NeoFences does not use WPF drag-drop, so replace it.
        PInvoke.RevokeDragDrop((HWND)fenceHandle);
        PInvoke.RegisterDragDrop((HWND)fenceHandle, target).ThrowOnFailure();
        return target;
    }

    private static readonly ushort FileGroupDescriptorFormat = (ushort)PInvoke.RegisterClipboardFormat("FileGroupDescriptorW");

    /// <summary>True when the source describes virtual files (zip contents, phones, mail attachments).</summary>
    internal static unsafe bool OffersVirtualFiles(IDataObject dataObject)
    {
        var format = DescriptorFormat();
        try { return dataObject.QueryGetData(&format).Value == 0; } // S_OK; S_FALSE and errors mean no
        catch (Exception failure) when (failure is not OutOfMemoryException) { return false; }
    }

    /// <summary>The names of virtual files being dropped (no extraction); empty when there are none.</summary>
    internal static unsafe List<string> VirtualFileNames(IDataObject dataObject)
    {
        var names = new List<string>();
        var format = DescriptorFormat();
        STGMEDIUM medium = default;
        try
        {
            dataObject.GetData(&format, out medium);
            // The source is another program: only a memory block, and only as many descriptors as it really holds (a bad
            // count would read past it, which .NET cannot catch; M8c review I5).
            if (medium.tymed != TYMED.TYMED_HGLOBAL) return names;
            var size = (long)(nuint)PInvoke.GlobalSize(medium.u.hGlobal);
            var group = (FILEGROUPDESCRIPTORW*)PInvoke.GlobalLock(medium.u.hGlobal);
            if (group is null) return names;
            try
            {
                if (size < sizeof(uint) || group->cItems > (size - sizeof(uint)) / sizeof(FILEDESCRIPTORW)) return names;
                var descriptors = &group->fgd.e0;
                for (var index = 0; index < group->cItems; index++)
                {
                    var name = descriptors[index].cFileName.ToString();
                    if (!name.Contains('\\')) names.Add(name); // files in subfolders arrive inside their folder
                }
            }
            finally
            {
                PInvoke.GlobalUnlock(medium.u.hGlobal);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // No descriptors: nothing to place; Windows still handles the drop.
        }
        finally
        {
            if (medium.u.hGlobal.Value != null) PInvoke.ReleaseStgMedium(ref medium);
        }
        return names;
    }

    private static FORMATETC DescriptorFormat() => new()
    {
        cfFormat = FileGroupDescriptorFormat,
        dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
        lindex = -1,
        tymed = (uint)TYMED.TYMED_HGLOBAL,
    };

    /// <summary>Paths in the drop's file list (CF_HDROP); empty for virtual items (zip contents, phones).</summary>
    internal static unsafe List<string> DroppedFiles(IDataObject dataObject)
    {
        var files = new List<string>();
        var format = new FORMATETC
        {
            cfFormat = (ushort)CLIPBOARD_FORMAT.CF_HDROP,
            dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = (uint)TYMED.TYMED_HGLOBAL,
        };
        STGMEDIUM medium = default;
        try
        {
            dataObject.GetData(&format, out medium);
            var drop = (HDROP)(nint)medium.u.hGlobal.Value;
            var count = PInvoke.DragQueryFile(drop, uint.MaxValue, default, 0);
            var buffer = new char[32768];
            for (uint index = 0; index < count; index++)
            {
                var length = PInvoke.DragQueryFile(drop, index, buffer);
                files.Add(new string(buffer, 0, (int)length));
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // No file list: nothing to place; Windows still handles the drop.
        }
        finally
        {
            if (medium.u.hGlobal.Value != null) PInvoke.ReleaseStgMedium(ref medium);
        }
        return files;
    }

    /// <summary>
    /// IDropTarget for one fence. Forwards to the shell (the hovered item's drop target, or the Desktop folder's) for
    /// real file drops, and keeps Windows' drag image visible over the fence (IDropTargetHelper).
    /// </summary>
    private sealed unsafe class FenceDropTarget(HWND fence, FenceDropHandlers handlers, Func<string?>? portalFolder) : IDropTarget, IDisposable
    {
        private IDataObject? _dataObject;
        private bool _desktopItemsOnly;      // desktop fence: items already on the Desktop (membership only)
        private string? _portalFolder;       // Portal fence: the folder it shows (drops are real file operations)
        private bool _sameFolderOnly;        // Portal fence: items already in that folder (nothing to do)
        private string? _hoveredItem;        // item whose own drop target is active
        private readonly Dictionary<string, bool> _containers = new(StringComparer.OrdinalIgnoreCase); // per drag: DragOver runs per mouse move
        private bool _overRecycleBin;         // NeoFences recycles itself here (never the Recycle Bin's own drop)
        private IReadOnlyList<string> _dragged = []; // what this drag carries (no disk access after DragEnter)
        private bool _virtualSource;         // zip contents, phones: never asked for CF_HDROP (that extracts every file)
        private IDropTarget? _shellTarget;   // forwarded-to target while it is entered
        private IDropTargetHelper? _imageHelper;

        public void DragEnter(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            LeaveShellTarget(); // a previous drag that failed half-way must not leak into this one
            Reset();
            _dataObject = pDataObj;
            // A virtual source (zip contents, a phone) would extract every file to answer CF_HDROP: not here, not on drop (M3b/M8c review).
            _virtualSource = CurrentDrag is null && OffersVirtualFiles(pDataObj);
            _dragged = CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj));
            UseFolder(portalFolder?.Invoke());
            _imageHelper = TryCreateImageHelper();
            var allowed = *pdwEffect;
            Update(grfKeyState, pt, pdwEffect, allowed);
            var effect = *pdwEffect;
            WithImageHelper(helper =>
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                helper.DragEnter(fence, pDataObj, &point, effect);
            });
        }

        public void DragOver(MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            Update(grfKeyState, pt, pdwEffect, *pdwEffect);
            var effect = *pdwEffect;
            WithImageHelper(helper =>
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                helper.DragOver(&point, effect);
            });
        }

        public void DragLeave()
        {
            LeaveShellTarget();
            WithImageHelper(helper => helper.DragLeave());
            handlers.ShowFeedback(null, false);
            Reset();
        }

        public void Drop(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            var allowed = *pdwEffect;
            try
            {
                Update(grfKeyState, pt, pdwEffect, allowed);
                var shownEffect = *pdwEffect;
                WithImageHelper(helper =>
                {
                    var point = new System.Drawing.Point(pt.x, pt.y);
                    helper.Drop(pDataObj, &point, shownEffect);
                });
                var drop = handlers.HitTest(pt.x, pt.y);
                if (_overRecycleBin)
                {
                    handlers.Recycle(CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj))); // always the Recycle Bin, never a delete
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
                    return;
                }
                if (_shellTarget is null && _sameFolderOnly)
                {
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE; // a Portal item dropped back into its own folder
                    return;
                }
                if (_shellTarget is null)
                {
                    // Membership only. Report "none" so the source never deletes anything after a "move".
                    handlers.MoveItems(CurrentDrag ?? DroppedFiles(pDataObj), drop.InsertAt);
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
                    return;
                }
                if (_hoveredItem is null && _portalFolder is null)
                {
                    // Desktop items in a mixed drag join this fence; the rest arrive through Windows (M3b review).
                    var files = CurrentDrag ?? (_virtualSource ? [] : DroppedFiles(pDataObj));
                    var onDesktop = files.Where(DesktopNamespace.IsDesktopItem).ToList();
                    if (onDesktop.Count > 0) handlers.MoveItems(onDesktop, drop.InsertAt);
                    var arriving = files.Count > 0 ? files.Except(onDesktop).Select(Path.GetFileName).OfType<string>().ToList() : VirtualFileNames(pDataObj);
                    // Files and their possible shortcuts are separate lists at the same place: each file keeps its own slot
                    // (one interleaved list spread the files over every other position; M8c review I2).
                    handlers.ExpectArrivals(DesktopRefsFor(arriving), drop.InsertAt + onDesktop.Count);
                    handlers.ExpectArrivals(DesktopRefsFor(arriving.Select(ShortcutName)), drop.InsertAt + onDesktop.Count);
                }
                var target = _shellTarget;
                _shellTarget = null; // Drop replaces DragLeave for it
                try
                {
                    *pdwEffect = allowed; // the source's choices, so a right-button drop menu offers them all
                    target.Drop(pDataObj, grfKeyState, pt, pdwEffect);
                }
                finally
                {
                    Marshal.ReleaseComObject(target);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                handlers.LogFailure(failure);
                *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
            }
            finally
            {
                LeaveShellTarget();
                handlers.ShowFeedback(null, false);
                Reset();
            }
        }

        public void Dispose()
        {
            PInvoke.RevokeDragDrop(fence);
            Reset();
        }

        /// <summary>Picks where the drag would land now and asks it for the effect (or computes ours).</summary>
        private void Update(MODIFIERKEYS_FLAGS keys, POINTL pt, DROPEFFECT* effect, DROPEFFECT allowed)
        {
            try
            {
                var drop = handlers.HitTest(pt.x, pt.y);
                // Hovering a tab header shows that tab (M9): a Portal tab and a desktop tab take drops differently.
                if (portalFolder?.Invoke() is var shown && !string.Equals(shown, _portalFolder, StringComparison.OrdinalIgnoreCase))
                {
                    LeaveShellTarget();
                    UseFolder(shown);
                }
                var hovered = drop.ItemRef;
                var dropsOnItem = hovered is not null && !(CurrentDrag?.Contains(hovered, StringComparer.OrdinalIgnoreCase) ?? false)
                                  && IsDropContainer(hovered);
                var wantedItem = dropsOnItem ? hovered : null;
                var wantsShell = dropsOnItem || !(_desktopItemsOnly || _sameFolderOnly);

                handlers.ShowFeedback(drop, wantedItem is not null);
                // The Recycle Bin's own drop target deletes permanently with Shift held, or refuses: NeoFences recycles
                // itself and only shows "move" here, whatever keys are held (M3b review C1).
                _overRecycleBin = string.Equals(wantedItem, DesktopItems.RecycleBinRef, StringComparison.OrdinalIgnoreCase);
                if (_overRecycleBin)
                {
                    LeaveShellTarget();
                    *effect = allowed & DROPEFFECT.DROPEFFECT_MOVE;
                    return;
                }
                if (_shellTarget is not null && (!wantsShell || wantedItem != _hoveredItem)) LeaveShellTarget();
                if (!wantsShell)
                {
                    // Desktop fence: a membership move, nothing on disk changes. Portal: already in this folder.
                    *effect = _sameFolderOnly ? DROPEFFECT.DROPEFFECT_NONE : allowed & DROPEFFECT.DROPEFFECT_MOVE;
                    return;
                }
                if (_shellTarget is null)
                {
                    _shellTarget = (IDropTarget)(wantedItem is null
                        ? (_portalFolder is null ? DesktopNamespace.DesktopDropTarget(fence) : DesktopNamespace.FolderDropTarget(fence, _portalFolder))
                        : DesktopNamespace.GetUIObject(fence, [wantedItem], typeof(IDropTarget).GUID));
                    _hoveredItem = wantedItem;
                    *effect = allowed;
                    _shellTarget.DragEnter(_dataObject!, keys, pt, effect);
                    return;
                }
                *effect = allowed;
                _shellTarget.DragOver(keys, pt, effect);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                handlers.LogFailure(failure);
                *effect = DROPEFFECT.DROPEFFECT_NONE;
            }
        }

        /// <summary>The drag image is cosmetic: a failing helper must not break the drop or leave state behind.</summary>
        private void WithImageHelper(Action<IDropTargetHelper> call)
        {
            if (_imageHelper is null) return;
            try { call(_imageHelper); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
        }

        private void LeaveShellTarget()
        {
            if (_shellTarget is null) return;
            try { _shellTarget.DragLeave(); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
            Marshal.ReleaseComObject(_shellTarget);
            _shellTarget = null;
            _hoveredItem = null;
        }

        /// <summary>What the fence shown now is: a Portal of this folder, or a desktop fence (null).</summary>
        private void UseFolder(string? folder)
        {
            _portalFolder = folder;
            _desktopItemsOnly = _portalFolder is null && _dragged.Count > 0 && _dragged.All(DesktopNamespace.IsDesktopItem);
            _sameFolderOnly = _portalFolder is not null && _dragged.Count > 0
                && _dragged.All(itemRef => string.Equals(Path.GetDirectoryName(itemRef), _portalFolder, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsDropContainer(string itemRef)
        {
            if (!_containers.TryGetValue(itemRef, out var container)) _containers[itemRef] = container = DesktopNamespace.IsDropContainer(fence, itemRef);
            return container;
        }

        private void Reset()
        {
            _containers.Clear();
            _dataObject = null;
            _desktopItemsOnly = false;
            _portalFolder = null;
            _sameFolderOnly = false;
            _overRecycleBin = false;
            _virtualSource = false;
            _dragged = [];
            if (_imageHelper is not null) Marshal.ReleaseComObject(_imageHelper);
            _imageHelper = null;
        }

        private static IDropTargetHelper? TryCreateImageHelper()
        {
            try
            {
                return (IDropTargetHelper)Activator.CreateInstance(Type.GetTypeFromCLSID(PInvoke.CLSID_DragDropHelper)!)!;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                return null; // no drag image over fences; the drop still works
            }
        }

        /// <summary>Where Windows will put dropped files: the user's Desktop, same names. Renamed copies ("name (2)") go to the Inbox.</summary>
        private static List<string> DesktopRefsFor(IEnumerable<string> fileNames) =>
            fileNames.Select(name => Path.Combine(DesktopItems.UserDesktop, name)).ToList();

        /// <summary>The name Windows gives a shortcut it makes on a drop (Alt, or a link-only source).</summary>
        // ponytail: the English " - Shortcut" suffix; other display languages name links differently (their links go to the Inbox).
        private static string ShortcutName(string fileName) => fileName + " - Shortcut.lnk";
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 296`.

- [ ] **Step 3: Commit**

```powershell
git add src/NeoFences.Shell/ShellDragDrop.cs
git commit -m "feat: let drops follow the tab shown while dragging over tab headers"
```

---

### Task 3: App — tab strip, gestures, one window per box

**Files:**
- Create: `src/NeoFences.App/TabGhost.cs`.
- Replace: `src/NeoFences.App/FenceWindow.xaml`, `FenceWindow.xaml.cs`, `FenceHost.cs`.

**Interfaces:**
- Consumes: Task 1 (`FenceTabs.*`, `TabColor`), Task 2.
- Produces:
  - `FenceWindow.BoxId`, `FenceId` (now the shown tab);
  - `ShowTab(Fence)`, `SetTabs(IReadOnlyList<Fence>, string activeId)`, `TabIndexAt(int screenX)`, `TitleRowContains(int, int)`, `SetMergeHighlight(bool)`;
  - events `TabSelected`, `TabDropped`, `TabColorRequested`, `DetachTabRequested`, `TabCycleRequested`;
  - `TabGhost(string title, bool lightTheme).Follow(x, y, scale)`.

- [ ] **Step 1: Files**

`src/NeoFences.App/TabGhost.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NeoFences.Core.Layouts;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// The tab header that follows the pointer while a tab is dragged (M9): topmost, never activated, and the mouse passes
/// through it, so the fence that started the drag keeps the mouse.
/// </summary>
public sealed class TabGhost : Window
{
    private const int WidthDips = 140;
    private const int HeightDips = 28;
    private readonly nint _handle;

    public TabGhost(string title, bool lightTheme)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        var ink = lightTheme ? Colors.Black : Colors.White;
        var paper = lightTheme ? Colors.White : Color.FromRgb(0x20, 0x20, 0x20);
        Content = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, ink.R, ink.G, ink.B)),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, paper.R, paper.G, paper.B)),
            Padding = new Thickness(10, 0, 10, 0),
            Child = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(ink),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        _handle = new WindowInteropHelper(this).EnsureHandle();
        FenceWindowChrome.MakeOverlay(_handle);
    }

    /// <summary>Just below and right of the pointer (physical pixels), sized for the monitor's scale.</summary>
    public void Follow(int screenX, int screenY, double scale)
    {
        FenceWindowChrome.SetPixelRect(_handle, new PixelRect(screenX + (int)(12 * scale), screenY + (int)(12 * scale),
            (int)Math.Round(WidthDips * scale), (int)Math.Round(HeightDips * scale)));
        if (!IsVisible) Show();
    }
}
```
`src/NeoFences.App/FenceWindow.xaml`:
```xml
<Window x:Class="NeoFences.App.FenceWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NeoFences fence" Width="320" Height="220"
        WindowStyle="None" ResizeMode="CanResize" AllowsTransparency="True"
        Background="#01000000" ShowInTaskbar="False" ShowActivated="False">
    <!-- Layered window (AllowsTransparency) + accent blur, owned by Progman: ADR-011.
         The 1/255-alpha background keeps the empty area hit-testable (fully transparent pixels click through).
         Colours are DynamicResources set by ApplyTheme (light/dark follows Windows, M2c). -->
    <WindowChrome.WindowChrome>
        <!-- Replaced in code (ApplyChrome) with the 8-DIP corner radius: ADR-024. -->
        <WindowChrome GlassFrameThickness="0" CaptionHeight="30" ResizeBorderThickness="6" UseAeroCaptionButtons="False" />
    </WindowChrome.WindowChrome>
    <Window.Resources>
        <sys:Double x:Key="IconSize" xmlns:sys="clr-namespace:System;assembly=System.Runtime">48</sys:Double>
        <sys:Double x:Key="ItemWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">76</sys:Double>
        <sys:Double x:Key="EditItemWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">76</sys:Double>
        <!-- Icon-only fences (M8b): labels collapse, the name pops under the hovered or selected icon instead. -->
        <Visibility x:Key="LabelVisibility">Visible</Visibility>
        <sys:Boolean x:Key="ItemToolTips" xmlns:sys="clr-namespace:System;assembly=System.Runtime">True</sys:Boolean>
        <Visibility x:Key="ShortcutArrowVisibility">Collapsed</Visibility>
        <Style x:Key="BannerButton" TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource FenceText}" />
            <Setter Property="Padding" Value="10,3" />
            <Setter Property="Margin" Value="0,0,6,0" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="Chrome" Background="{DynamicResource FenceHover}" CornerRadius="4" Padding="{TemplateBinding Padding}">
                            <ContentPresenter />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <!-- Thin scrollbar (spec §6): no arrows, a rounded thumb that brightens under the mouse. -->
        <Style TargetType="ScrollBar">
            <Setter Property="Width" Value="6" />
            <Setter Property="MinWidth" Value="6" />
            <Setter Property="Margin" Value="0,4,2,4" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ScrollBar">
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Border x:Name="ThumbChrome" CornerRadius="3" Background="{DynamicResource FenceScrollThumb}" />
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="ThumbChrome" Property="Background" Value="{DynamicResource FenceSubtleText}" />
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <Border CornerRadius="8" BorderBrush="{DynamicResource FenceBorder}" BorderThickness="1" Background="{DynamicResource FenceVeil}">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="30" />
                <RowDefinition Height="Auto" />
                <RowDefinition />
            </Grid.RowDefinitions>
            <DockPanel x:Name="TitleBar" Background="Transparent">
                <!-- Portal browsing (M4): back to the parent folder. Hit-testable inside the caption area. -->
                <Button x:Name="BackButton" DockPanel.Dock="Left" Visibility="Collapsed" Content="‹" ToolTip="Back (Backspace)"
                        Margin="6,3,0,3" Padding="8,0" FontSize="16" Style="{StaticResource BannerButton}"
                        WindowChrome.IsHitTestVisibleInChrome="True" />
                <!-- Fence tabs (M9): one header per tab, built in code; the space right of them stays caption (move, roll-up). -->
                <UniformGrid x:Name="TabStrip" DockPanel.Dock="Left" Rows="1" Visibility="Collapsed" HorizontalAlignment="Left" Margin="6,3,0,0" />
                <TextBlock x:Name="TitleText" Foreground="{DynamicResource FenceText}" FontWeight="SemiBold" Margin="12,0"
                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
            </DockPanel>
            <!-- A single fence's tab colour (M9): a bar under the title. -->
            <Rectangle x:Name="TitleColorBar" Height="3" Width="48" Margin="12,0,0,1" RadiusX="1.5" RadiusY="1.5"
                       VerticalAlignment="Bottom" HorizontalAlignment="Left" Visibility="Collapsed" IsHitTestVisible="False" />
            <!-- Rename: replaces the title while editing. Hit-testable inside the caption area. -->
            <TextBox x:Name="TitleBox" Visibility="Collapsed" Margin="8,4" Padding="3,1" FontWeight="SemiBold"
                     VerticalContentAlignment="Center" WindowChrome.IsHitTestVisibleInChrome="True"
                     Foreground="{DynamicResource FenceText}" Background="{DynamicResource FenceHover}"
                     BorderBrush="{DynamicResource FenceBorder}" CaretBrush="{DynamicResource FenceText}" />
            <!-- First run (Inbox only): ask once whether NeoFences should take over the desktop icons. -->
            <Border x:Name="TakeoverPrompt" Grid.Row="1" Visibility="Collapsed" Background="{DynamicResource FenceHover}"
                    Margin="8,0,8,6" Padding="10,8" CornerRadius="6">
                <StackPanel>
                    <TextBlock Foreground="{DynamicResource FenceText}" TextWrapping="Wrap"
                               Text="Hide the desktop icons and keep them only in fences?" />
                    <TextBlock Foreground="{DynamicResource FenceSubtleText}" FontSize="11" TextWrapping="Wrap" Margin="0,2,0,6"
                               Text="Your files stay where they are. Turn it off any time from the right-click menu." />
                    <StackPanel Orientation="Horizontal">
                        <Button x:Name="PromptHideButton" Content="Hide them" Style="{StaticResource BannerButton}" />
                        <Button x:Name="PromptLaterButton" Content="Not now" Style="{StaticResource BannerButton}" />
                    </StackPanel>
                </StackPanel>
            </Border>
            <Border x:Name="Body" Grid.Row="2" BorderBrush="{DynamicResource FenceDivider}" BorderThickness="0,1,0,0" Background="#01000000">
                <Border.ContextMenu>
                    <ContextMenu x:Name="BodyContextMenu">
                        <MenuItem x:Name="NewFenceItem" Header="New fence" />
                        <MenuItem x:Name="NewPortalItem" Header="New Portal fence…" />
                        <MenuItem x:Name="RenameItem" Header="Rename fence" />
                        <MenuItem x:Name="IconSizeItem" Header="Icon size" />
                        <MenuItem x:Name="LabelsItem" Header="Labels">
                            <MenuItem x:Name="LabelsAlwaysItem" Header="Always" IsCheckable="True" />
                            <MenuItem x:Name="LabelsOnHoverItem" Header="On hover (icons only)" IsCheckable="True" />
                        </MenuItem>
                        <MenuItem x:Name="SortItem" Header="Sort by" />
                        <MenuItem x:Name="TabColorItem" Header="Colour" />
                        <MenuItem x:Name="DetachTabItem" Header="Detach tab" Visibility="Collapsed" />
                        <MenuItem x:Name="OpenFolderItem" Header="Open folder in Explorer" Visibility="Collapsed" />
                        <MenuItem x:Name="LockItem" Header="Lock position" IsCheckable="True" />
                        <MenuItem x:Name="DeleteItem" Header="Delete fence (items go to the Inbox)" />
                        <Separator />
                        <MenuItem x:Name="TakeoverItem" Header="Hide desktop icons" IsCheckable="True" />
                        <MenuItem x:Name="StartupItem" Header="Start with Windows" IsCheckable="True" />
                        <MenuItem x:Name="SettingsItem" Header="Settings…" />
                        <Separator />
                        <MenuItem x:Name="ExitItem" Header="Exit NeoFences" />
                    </ContextMenu>
                </Border.ContextMenu>
                <Grid>
                    <ListBox x:Name="ItemList" Background="Transparent" BorderThickness="0" Padding="4"
                             SelectionMode="Extended" ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                             ScrollViewer.VerticalScrollBarVisibility="Auto">
                        <ListBox.ItemsPanel>
                            <ItemsPanelTemplate>
                                <WrapPanel />
                            </ItemsPanelTemplate>
                        </ListBox.ItemsPanel>
                        <ListBox.ItemContainerStyle>
                            <Style TargetType="ListBoxItem">
                                <Setter Property="ToolTip" Value="{Binding Label}" />
                                <Setter Property="ToolTipService.IsEnabled" Value="{DynamicResource ItemToolTips}" />
                                <EventSetter Event="MouseEnter" Handler="OnItemMouseEnter" />
                                <EventSetter Event="MouseLeave" Handler="OnItemMouseLeave" />
                                <Setter Property="AutomationProperties.Name" Value="{Binding Label}" />
                                <Setter Property="FocusVisualStyle" Value="{x:Null}" />
                                <Setter Property="Template">
                                    <Setter.Value>
                                        <ControlTemplate TargetType="ListBoxItem">
                                            <!-- The outer border fills the gaps between cells, so the pointer is always over some cell and the
                                                 pop-under name does not blink between neighbours (final review I2). -->
                                            <Border Background="Transparent" Padding="2">
                                                <Border x:Name="Chrome" Background="Transparent" CornerRadius="4" Padding="2,4">
                                                    <ContentPresenter />
                                                </Border>
                                            </Border>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
                                                </Trigger>
                                                <Trigger Property="IsSelected" Value="True">
                                                    <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Setter.Value>
                                </Setter>
                            </Style>
                        </ListBox.ItemContainerStyle>
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <StackPanel x:Name="Cell" Width="{DynamicResource ItemWidth}">
                                    <Grid HorizontalAlignment="Center">
                                        <Image Source="{Binding Icon}" Width="{DynamicResource IconSize}" Height="{DynamicResource IconSize}" />
                                        <!-- Shortcut arrow (M8b, Settings switch): a white tile with a blue curved arrow, like Windows draws. -->
                                        <Viewbox x:Name="ShortcutArrow" Visibility="Collapsed" HorizontalAlignment="Left" VerticalAlignment="Bottom"
                                                 Width="{DynamicResource ArrowSize}" Height="{DynamicResource ArrowSize}" IsHitTestVisible="False">
                                            <Canvas Width="16" Height="16">
                                                <Rectangle Width="16" Height="16" RadiusX="2" RadiusY="2" Fill="White" Stroke="#FF8A8A8A" StrokeThickness="0.75" />
                                                <Path Data="M4,12 C4,7.5 6.5,5.5 10,5.5 L10,3.5 L13,6.75 L10,10 L10,8 C7.5,8 5.5,9 4,12 Z" Fill="#FF2A6FD6" />
                                            </Canvas>
                                        </Viewbox>
                                    </Grid>
                                    <Grid Margin="0,4,0,0">
                                        <TextBlock x:Name="Label" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
                                                   Visibility="{DynamicResource LabelVisibility}"
                                                   TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"
                                                   MaxHeight="32" Effect="{DynamicResource LabelShadow}" />
                                        <!-- In-place rename (M3a): Enter renames through Windows, Esc cancels. -->
                                        <TextBox x:Name="LabelBox" Visibility="Collapsed" FontSize="12" TextAlignment="Center" TextWrapping="Wrap"
                                                 MaxHeight="48" Text="{Binding EditName, UpdateSourceTrigger=PropertyChanged}"
                                                 KeyDown="OnLabelBoxKeyDown" LostKeyboardFocus="OnLabelBoxLostFocus"
                                                 IsVisibleChanged="OnLabelBoxVisibleChanged" />
                                    </Grid>
                                </StackPanel>
                                <DataTemplate.Triggers>
                                    <DataTrigger Binding="{Binding IsShortcut}" Value="True">
                                        <Setter TargetName="ShortcutArrow" Property="Visibility" Value="{DynamicResource ShortcutArrowVisibility}" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding IsEditing}" Value="True">
                                        <Setter TargetName="LabelBox" Property="Visibility" Value="Visible" />
                                        <Setter TargetName="Label" Property="Visibility" Value="Hidden" />
                                        <!-- Icons-only cells are narrow: the rename box gets a labelled cell's width (M8b review). -->
                                        <Setter TargetName="Cell" Property="Width" Value="{DynamicResource EditItemWidth}" />
                                    </DataTrigger>
                                </DataTemplate.Triggers>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                    <!-- Portal whose folder cannot be read (missing, offline, denied). -->
                    <TextBlock x:Name="PortalMessage" Visibility="Collapsed" Margin="12" TextWrapping="Wrap"
                               Foreground="{DynamicResource FenceSubtleText}" IsHitTestVisible="False" />
                    <!-- Rubber-band selection (M3b): drawn while dragging on empty space. -->
                    <Canvas IsHitTestVisible="False" ClipToBounds="True">
                        <Rectangle x:Name="SelectionBand" Visibility="Collapsed" Fill="{DynamicResource FenceHover}"
                                   Stroke="{DynamicResource FenceSubtleText}" StrokeThickness="1" RadiusX="2" RadiusY="2" />
                        <!-- Drag-drop feedback (M3b review I2): where a drop lands, or which folder takes it. -->
                        <Rectangle x:Name="InsertCaret" Visibility="Collapsed" Width="2" Fill="{DynamicResource FenceText}" RadiusX="1" RadiusY="1" />
                        <!-- Icon-only fences (M8b): the hovered or selected item's name, under its icon, over the neighbours. -->
                        <Border x:Name="HoverLabel" Visibility="Collapsed" CornerRadius="4" Padding="6,2"
                                Background="{DynamicResource HoverLabelBackground}" BorderBrush="{DynamicResource FenceBorder}" BorderThickness="1">
                            <TextBlock x:Name="HoverLabelText" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
                                       TextAlignment="Center" TextWrapping="Wrap" MaxWidth="180" />
                        </Border>
                        <Rectangle x:Name="DropHighlight" Visibility="Collapsed" Fill="{DynamicResource FenceSelected}"
                                   Stroke="{DynamicResource FenceText}" StrokeThickness="1" RadiusX="4" RadiusY="4" />
                    </Canvas>
                </Grid>
            </Border>
        </Grid>
    </Border>
</Window>
```
`src/NeoFences.App/FenceWindow.xaml.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Shell;
using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>One fence on the desktop. Placement and persistence are the host's job; this window reports what the user did.</summary>
public partial class FenceWindow : Window
{
    private const int WmWindowPosChanging = 0x0046;
    private const int WmSizing = 0x0214;
    private const int WmMoving = 0x0216;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmNcLeftButtonDoubleClick = 0x00A3;
    private const int HitTestCaption = 2;
    private const double CornerRadiusDips = 8;
    private const double CaptionHeightDips = 30;
    private const double ResizeBorderDips = 6;

    // The menu lists ConfigNormalizer.IconSizes (one list, M2c review carry-over); these are only their names.
    private static readonly Dictionary<int, string> IconSizeNames = new() { [32] = "Small", [48] = "Medium", [64] = "Large", [96] = "Extra large" };

    private readonly ObservableCollection<FenceItemView> _items = [];
    private readonly IconLoader _iconLoader;
    private ListBoxItem? _hoverLabelContainer; // the cell the pop-under name belongs to
    private int _iconSizeDips;
    private bool _renaming;
    private string _title = "";
    private bool _isPortal; // the shown tab is a Portal (changes with the tab, M9)
    private DragTracker? _drag;
    private bool _locked;
    private bool _rolledUp;
    private readonly RollUpExpansion _expansion; // rolled up, but open right now (hover or click, M5/M6b)
    private int _fullHeightPx;              // height when not rolled up (physical pixels)
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer;
    private readonly System.Windows.Threading.DispatcherTimer _heightAnimation;
    private System.Diagnostics.Stopwatch _heightClock = new();
    private int _heightFrom;
    private int _heightTo;
    private int _fadeGeneration;
    private int? _moveHeightPx; // a move that interrupted a roll-up animation keeps this height (M6b review M2)
    private static readonly TimeSpan RollUpDuration = TimeSpan.FromMilliseconds(200); // spec §6
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(150)); // spec §6

    /// <summary>The fence shown now: the box's active tab (M9). Items, icon size, labels, sort and rename act on it.</summary>
    public string FenceId { get; private set; }

    /// <summary>The fence owning this box (its host, M9): placement, move, roll-up and lock act on it.</summary>
    public string BoxId { get; set; }

    /// <summary>A tab header clicked (or hovered during a drop): show that tab.</summary>
    public event Action<string>? TabSelected;
    /// <summary>A tab header dragged and released at this screen point (physical pixels): reorder, merge or detach.</summary>
    public event Action<string, int, int>? TabDropped;
    public event Action<TabColor?>? TabColorRequested;
    public event Action? DetachTabRequested;
    /// <summary>Ctrl+Tab (+1) / Ctrl+Shift+Tab (-1).</summary>
    public event Action<int>? TabCycleRequested;

    /// <summary>The accent colours (M9), as Windows' own accent palette roughly offers them.</summary>
    public static readonly IReadOnlyDictionary<TabColor, Color> TabColors = new Dictionary<TabColor, Color>
    {
        [TabColor.Red] = Color.FromRgb(0xE8, 0x48, 0x55), [TabColor.Orange] = Color.FromRgb(0xF7, 0x63, 0x0C),
        [TabColor.Yellow] = Color.FromRgb(0xFF, 0xB9, 0x00), [TabColor.Green] = Color.FromRgb(0x16, 0xC6, 0x0C),
        [TabColor.Teal] = Color.FromRgb(0x00, 0xB7, 0xC3), [TabColor.Blue] = Color.FromRgb(0x00, 0x78, 0xD4),
        [TabColor.Purple] = Color.FromRgb(0x88, 0x64, 0xD8), [TabColor.Pink] = Color.FromRgb(0xE3, 0x00, 0x8C),
    };

    private const double TabHeaderMaxWidth = 140;
    private List<Fence> _tabs = [];
    private string? _tabPressId;      // a header pressed: a click or the start of a tab drag
    private Point _tabPressPoint;
    private TabGhost? _tabGhost;      // set once the press became a drag
    private bool _lightTheme;

    public nint Handle { get; private set; }

    /// <summary>Windows refused the rounded-corner preference (Windows 10); the host logs it once.</summary>
    public bool CornersUnavailable { get; private set; }

    /// <summary>Peek (M5): while set the fence may rise above apps instead of staying at the bottom.</summary>
    public bool Peeking { get; set; }

    /// <summary>
    /// Asked before each roll-up or fade (spec §6): off when Windows' "Animation effects" are off, and in game mode
    /// (spec §4.7). Set by the host.
    /// </summary>
    public Func<bool> AnimationsAllowed { get; set; } = () => false;

    /// <summary>Asked while the user drags an edge or the title: returns where the window should go (snapping).</summary>
    public Func<PixelRect, SnapEdges, PixelRect>? SnapRect { get; set; }

    /// <summary>Raised after the user finishes moving or resizing, with the new physical-pixel rect.</summary>
    public event Action<FenceWindow, PixelRect>? MovedByUser;

    public event Action? NewFenceRequested;
    public event Action<bool>? TakeoverToggled;
    public event Action? ExitRequested;
    public event Action<string>? OpenRequested;
    /// <summary>Enter with several items selected: open each (a Portal does not browse into one of several folders, M8d).</summary>
    public event Action<IReadOnlyList<string>>? OpenManyRequested;
    /// <summary>The first-run question was answered: true = hide the desktop icons.</summary>
    public event Action<bool>? TakeoverPromptAnswered;
    public event Action<string>? RenameRequested;
    public event Action<int>? IconSizeRequested;
    public event Action<bool>? LockToggled;
    public event Action? DeleteRequested;
    /// <summary>Right-click (or the menu key) on items: show Windows' item menu for these refs at this screen point (px).</summary>
    /// <summary>Right-click or menu key on items: refs (the clicked item first), screen point, opened from the keyboard.</summary>
    public event Action<IReadOnlyList<string>, int, int, bool>? ItemMenuRequested;
    /// <summary>Del: send these items to the Recycle Bin.</summary>
    public event Action<IReadOnlyList<string>>? RecycleRequested;
    /// <summary>An in-place rename was confirmed: item ref, new name as typed.</summary>
    public event Action<string, string>? ItemRenameRequested;
    /// <summary>The user started dragging these items out of the fence (M3b).</summary>
    public event Action<IReadOnlyList<string>>? DragRequested;
    /// <summary>Portal (M4): back to the parent folder (Back button, Backspace).</summary>
    public event Action? BackRequested;
    public event Action? NewPortalRequested;
    public event Action<FenceSort>? SortRequested;
    public event Action? OpenFolderRequested;
    /// <summary>The "Start with Windows" toggle changed (ADR-019).</summary>
    public event Action<bool>? StartupToggled;
    /// <summary>"Settings…" in the fence menu (M6b).</summary>
    public event Action? SettingsRequested;
    /// <summary>Double-click on the title: roll up to the title bar, or back down (M5).</summary>
    public event Action? RollUpToggled;
    /// <summary>Fence menu → Labels (M8b): always, or only on hover / selection.</summary>
    public event Action<LabelMode>? LabelModeRequested;

    private Point? _pressPoint;                 // left button pressed on an item: a drag may start
    private ListBoxItem? _deferredSelect;       // pressed on an already selected item: select it alone only on release
    private Point? _bandStart;                  // left button pressed on empty space: rubber band
    private ListBoxItem? _deferredToggle;       // Ctrl+press on a selected item: unselect it on release unless it was dragged
    private LabelMode _labelMode = LabelMode.Always;
    private ListBoxItem? _hoveredContainer;
    private (uint At, Point Where)? _lastCaptionPress; // a title double-click recognised by NeoFences itself (M8b); message time
    private uint? _recognisedDoubleClickAt;

    public FenceWindow(Fence fence, bool takeoverActive, bool lightTheme, IconLoader iconLoader, RollupExpand rollupExpand)
    {
        _expansion = new RollUpExpansion(rollupExpand);
        FenceId = fence.Id;
        BoxId = fence.Id;
        _iconLoader = iconLoader;
        InitializeComponent();
        foreach (var (sort, name) in new[] { (FenceSort.Name, "Name"), (FenceSort.Type, "Type"), (FenceSort.Date, "Date (newest first)") })
        {
            var sortItem = new MenuItem { Header = name, Tag = sort };
            sortItem.Click += (_, _) => SortRequested?.Invoke(sort);
            SortItem.Items.Add(sortItem);
        }
        var noColor = new MenuItem { Header = "None", IsCheckable = true };
        noColor.Click += (_, _) => TabColorRequested?.Invoke(null);
        TabColorItem.Items.Add(noColor);
        foreach (var (color, value) in TabColors)
        {
            var colorItem = new MenuItem
            {
                Header = color.ToString(), Tag = color, IsCheckable = true,
                Icon = new Rectangle { Width = 12, Height = 12, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(value) },
            };
            colorItem.Click += (_, _) => TabColorRequested?.Invoke(color);
            TabColorItem.Items.Add(colorItem);
        }
        DetachTabItem.Click += (_, _) => DetachTabRequested?.Invoke();
        TitleBar.SizeChanged += (_, _) => UpdateTabStripWidth();
        PreviewKeyDown += OnTabKeys;
        NewPortalItem.Click += (_, _) => NewPortalRequested?.Invoke();
        OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
        BackButton.Click += (_, _) => BackRequested?.Invoke();
        TakeoverItem.IsChecked = takeoverActive;
        foreach (var size in ConfigNormalizer.IconSizes)
        {
            var sizeItem = new MenuItem { Header = IconSizeNames.GetValueOrDefault(size, $"{size} px"), Tag = size, IsCheckable = true };
            sizeItem.Click += (_, _) => IconSizeRequested?.Invoke(size);
            IconSizeItem.Items.Add(sizeItem);
        }
        NewFenceItem.Click += (_, _) => NewFenceRequested?.Invoke();
        RenameItem.Click += (_, _) => BeginRename();
        LockItem.Click += (_, _) => LockToggled?.Invoke(LockItem.IsChecked);
        DeleteItem.Click += (_, _) => DeleteRequested?.Invoke();
        TakeoverItem.Click += (_, _) => TakeoverToggled?.Invoke(TakeoverItem.IsChecked);
        ExitItem.Click += (_, _) => ExitRequested?.Invoke();
        StartupItem.Click += (_, _) => StartupToggled?.Invoke(StartupItem.IsChecked);
        SettingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        LabelsAlwaysItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.Always);
        LabelsOnHoverItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.OnHover);
        TitleBox.MaxLength = FenceEdits.MaxTitleLength; // the cut in FenceEdits.Rename never surprises the user (M2c review)
        ItemList.SelectionChanged += (_, _) => UpdateHoverLabel();
        ItemList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateHoverLabel()));
        HoverLabel.SizeChanged += (_, _) => PlaceHoverLabel(); // the bound name arrived or changed (final review I3)
        Loaded += (_, _) => ReloadIcons(); // placed on its monitor: one load at the right size (mixed DPI, M2b review)
        ItemList.ItemsSource = _items;
        ItemList.MouseDoubleClick += OnItemDoubleClick;
        ItemList.PreviewMouseLeftButtonDown += OnListPress;
        ItemList.PreviewMouseMove += OnListMove;
        ItemList.PreviewMouseLeftButtonUp += OnListRelease;
        ItemList.LostMouseCapture += (_, _) => EndBand();
        ItemList.KeyDown += OnItemListKeyDown;
        Body.ContextMenuOpening += OnBodyContextMenuOpening;
        TitleBox.KeyDown += OnTitleBoxKeyDown;
        TitleBox.LostKeyboardFocus += (_, _) => EndRename(commit: true);
        PromptHideButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(true);
        PromptLaterButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(false);
#if DEBUG
        // Checklist B11: a hung fence UI thread must not freeze the desktop or taskbar (owner input-queue attachment, ADR-011).
        var freezeItem = new MenuItem { Header = "Debug: freeze this UI thread for 10 s (B11)" };
        freezeItem.Click += (_, _) => Thread.Sleep(TimeSpan.FromSeconds(10));
        BodyContextMenu.Items.Add(freezeItem);
#endif
        ApplyTheme(lightTheme);
        ApplyFence(fence);
        _rolledUp = fence.RolledUp;
        // Rolled up, the fence opens on hover or click (setting) and closes again shortly after the pointer leaves.
        _hoverTimer = new System.Windows.Threading.DispatcherTimer { Interval = HoverTick };
        _hoverTimer.Tick += (_, _) => OnHoverTick();
        if (_rolledUp) _hoverTimer.Start();
        _heightAnimation = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _heightAnimation.Tick += (_, _) => StepHeight();
        Closed += (_, _) =>
        {
            _hoverTimer.Stop(); // a deleted fence must not keep ticking on its dead handle (M5 review M1)
            _heightAnimation.Stop();
        };
        SetLocked(fence.Locked);
        // Unlocked, the title is caption (WM_NCLBUTTONDBLCLK); locked, it is client area.
        TitleBar.MouseLeftButtonDown += (_, click) =>
        {
            if (click.ClickCount == 2) RollUpToggled?.Invoke();
            else if (click.ClickCount == 1) ClickToOpen();
        };
        SourceInitialized += OnSourceInitialized;
        // Icons are rendered for one DPI. DpiChanged is routed: every new item raises it too, so react only to the window's own.
        DpiChanged += (_, dpiChange) =>
        {
            if (dpiChange.OriginalSource != this || dpiChange.OldDpi.PixelsPerDip == dpiChange.NewDpi.PixelsPerDip) return;
            // A rolled-up fence keeps its full height in physical pixels: rescale it with the monitor (M5 review carry-over).
            _fullHeightPx = (int)Math.Round(_fullHeightPx * dpiChange.NewDpi.DpiScaleY / dpiChange.OldDpi.DpiScaleY);
            ReloadIcons();
        };
    }

    public void SetTakeoverChecked(bool active) => TakeoverItem.IsChecked = active;

    public void SetStartupChecked(bool startWithWindows) => StartupItem.IsChecked = startWithWindows;

    /// <summary>Everything the window shows of one fence: title, Portal bits, menu state, icon size, labels.</summary>
    private void ApplyFence(Fence fence)
    {
        _title = fence.Title;
        TitleText.Text = fence.Title;
        _isPortal = fence.Source.Kind == FenceSourceKind.Portal;
        OpenFolderItem.Visibility = _isPortal ? Visibility.Visible : Visibility.Collapsed;
        DeleteItem.Header = _isPortal ? "Delete fence (the folder is not touched)" : "Delete fence (items go to the Inbox)";
        DeleteItem.Visibility = fence.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        // Desktop fences sort once (dragging keeps working); Portals keep the chosen order live, so it is checked.
        foreach (var sortItem in SortItem.Items.OfType<MenuItem>())
        {
            sortItem.IsCheckable = _isPortal;
            sortItem.IsChecked = _isPortal && (FenceSort)sortItem.Tag == fence.Sort;
        }
        _labelMode = fence.Labels;
        SetIconSize(fence.IconSize);
        SetLabelMode(fence.Labels);
    }

    /// <summary>
    /// Another tab of this box is shown (M9): its look and menus; its items follow from the host (or its Portal). An open
    /// rename of the previous tab is cancelled.
    /// </summary>
    public void ShowTab(Fence fence)
    {
        if (fence.Id == FenceId) return;
        EndRename(commit: false);
        FenceId = fence.Id;
        ApplyFence(fence);
        _items.Clear();
        ShowPortalMessage(null);
        BackButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>The box's tabs in order and the shown one (M9). One tab: the plain title (with its colour bar, if any).</summary>
    public void SetTabs(IReadOnlyList<Fence> tabs, string activeId)
    {
        _tabs = [.. tabs];
        var many = _tabs.Count > 1;
        var active = _tabs.FirstOrDefault(tab => tab.Id == activeId) ?? _tabs[0];
        TabStrip.Children.Clear();
        if (many) foreach (var tab in _tabs) TabStrip.Children.Add(BuildTabHeader(tab, isActive: tab.Id == active.Id));
        ShowTitleOrTabs();
        DetachTabItem.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
        RenameItem.Header = many ? "Rename tab" : "Rename fence";
        TitleColorBar.Visibility = !many && active.TabColor is not null ? Visibility.Visible : Visibility.Collapsed;
        if (active.TabColor is { } titleColor) TitleColorBar.Fill = new SolidColorBrush(TabColors[titleColor]);
        foreach (var colorItem in TabColorItem.Items.OfType<MenuItem>()) colorItem.IsChecked = Equals(colorItem.Tag, active.TabColor);
        UpdateTabStripWidth();
    }

    private void ShowTitleOrTabs()
    {
        var many = _tabs.Count > 1;
        TabStrip.Visibility = _renaming ? Visibility.Hidden : many ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Visibility = _renaming ? Visibility.Hidden : many ? Visibility.Collapsed : Visibility.Visible;
    }

    private Border BuildTabHeader(Fence tab, bool isActive)
    {
        var title = new TextBlock
        {
            Text = tab.Title, FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(8, 0, 8, 2),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "FenceText");
        var bar = new Rectangle
        {
            Height = 3, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(6, 0, 6, 1), VerticalAlignment = VerticalAlignment.Bottom,
            Fill = tab.TabColor is { } color ? new SolidColorBrush(TabColors[color]) : Brushes.Transparent, Opacity = isActive ? 1 : 0.55,
            IsHitTestVisible = false,
        };
        var cell = new Grid();
        cell.Children.Add(title);
        cell.Children.Add(bar);
        var header = new Border { Child = cell, CornerRadius = new CornerRadius(5, 5, 0, 0), Tag = tab.Id, Background = Brushes.Transparent };
        if (isActive) header.SetResourceReference(Border.BackgroundProperty, "FenceHover");
        WindowChrome.SetIsHitTestVisibleInChrome(header, true); // the title row is caption: headers take the mouse themselves
        System.Windows.Automation.AutomationProperties.SetName(header, tab.Title);
        header.MouseLeftButtonDown += OnTabPress;
        header.MouseMove += OnTabMove;
        header.MouseLeftButtonUp += OnTabRelease;
        header.MouseRightButtonDown += OnTabRightPress;
        header.MouseRightButtonUp += OnTabRightClick;
        header.LostMouseCapture += (_, _) => CancelTabGesture();
        return header;
    }

    private void UpdateTabStripWidth()
    {
        if (_tabs.Count < 2) return;
        // Headers share the strip equally and shrink with "…"; the rest of the row stays free to move the box.
        TabStrip.Width = Math.Max(0, Math.Min(_tabs.Count * TabHeaderMaxWidth, TitleBar.ActualWidth - 40));
    }

    private void OnTabPress(object sender, MouseButtonEventArgs press)
    {
        if (sender is not Border { Tag: string tabId } header) return;
        press.Handled = true;
        if (press.ClickCount == 2)
        {
            if (tabId == FenceId) BeginRename(); // the first click already showed it
            return;
        }
        _tabPressId = tabId;
        _tabPressPoint = press.GetPosition(this);
        header.CaptureMouse();
    }

    private void OnTabMove(object sender, MouseEventArgs move)
    {
        if (_tabPressId is not { } tabId || move.LeftButton != MouseButtonState.Pressed) return;
        if (_tabGhost is null)
        {
            var moved = move.GetPosition(this) - _tabPressPoint;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance * 2
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
            _tabGhost = new TabGhost(_tabs.FirstOrDefault(tab => tab.Id == tabId)?.Title ?? "", _lightTheme);
        }
        var (screenX, screenY) = FenceWindowChrome.GetCursorPosition();
        _tabGhost.Follow(screenX, screenY, VisualTreeHelper.GetDpi(this).DpiScaleX);
    }

    private void OnTabRelease(object sender, MouseButtonEventArgs release)
    {
        if (_tabPressId is not { } tabId) return;
        release.Handled = true;
        var dragged = _tabGhost is not null;
        EndTabGesture();
        if (!dragged)
        {
            TabSelected?.Invoke(tabId);
            return;
        }
        var (screenX, screenY) = FenceWindowChrome.GetCursorPosition();
        TabDropped?.Invoke(tabId, screenX, screenY);
    }

    /// <summary>A right-click during a tab drag cancels it; nothing changes.</summary>
    private void OnTabRightPress(object sender, MouseButtonEventArgs press)
    {
        if (_tabPressId is null) return;
        press.Handled = true;
        EndTabGesture();
    }

    /// <summary>Right-click on a header: that tab is shown, and the fence menu acts on it.</summary>
    private void OnTabRightClick(object sender, MouseButtonEventArgs click)
    {
        if (sender is not Border { Tag: string tabId }) return;
        click.Handled = true;
        if (tabId != FenceId) TabSelected?.Invoke(tabId);
        BodyContextMenu.PlacementTarget = Body;
        BodyContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        BodyContextMenu.IsOpen = true;
    }

    private void OnTabKeys(object sender, KeyEventArgs key)
    {
        if (key.Key == Key.Escape && _tabPressId is not null)
        {
            EndTabGesture();
            key.Handled = true;
        }
        else if (key.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _tabs.Count > 1)
        {
            TabCycleRequested?.Invoke(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            key.Handled = true;
        }
    }

    private void EndTabGesture()
    {
        _tabPressId = null; // first: releasing the capture below raises LostMouseCapture
        _tabGhost?.Close();
        _tabGhost = null;
        Mouse.Capture(null);
    }

    private void CancelTabGesture()
    {
        if (_tabPressId is null) return;
        _tabPressId = null;
        _tabGhost?.Close();
        _tabGhost = null;
    }

    /// <summary>The insertion slot (0..tab count) under a screen x (physical pixels), for a reorder or a merge.</summary>
    public int TabIndexAt(int screenX)
    {
        if (_tabs.Count < 2 || TabStrip.ActualWidth <= 0) return _tabs.Count;
        var headerWidth = TabStrip.ActualWidth / _tabs.Count;
        var x = TabStrip.PointFromScreen(new Point(screenX, 0)).X;
        return Math.Clamp((int)Math.Round(x / headerWidth), 0, _tabs.Count);
    }

    /// <summary>True when the screen point (physical pixels) is on this box's title row or tab strip.</summary>
    public bool TitleRowContains(int screenX, int screenY)
    {
        if (!IsVisible) return false;
        var rect = FenceWindowChrome.GetPixelRect(Handle);
        var titleHeightPx = TitleBar.ActualHeight * VisualTreeHelper.GetDpi(this).DpiScaleY;
        return screenX >= rect.X && screenX < rect.X + rect.Width && screenY >= rect.Y && screenY < rect.Y + titleHeightPx;
    }

    /// <summary>A fence or tab dragged over this title row would merge here: show it.</summary>
    public void SetMergeHighlight(bool highlighted)
    {
        if (highlighted) TitleBar.SetResourceReference(Panel.BackgroundProperty, "FenceSelected");
        else TitleBar.Background = Brushes.Transparent;
    }

    /// <summary>The tab whose header is under a screen point, if any (an item drop over a header shows that tab).</summary>
    private string? TabHeaderAt(int screenX, int screenY)
    {
        if (_tabs.Count < 2 || TabStrip.Visibility != Visibility.Visible) return null;
        var point = TabStrip.PointFromScreen(new Point(screenX, screenY));
        if (point.Y < 0 || point.Y > TabStrip.ActualHeight || point.X < 0 || point.X >= TabStrip.ActualWidth) return null;
        return _tabs[Math.Clamp((int)(point.X / (TabStrip.ActualWidth / _tabs.Count)), 0, _tabs.Count - 1)].Id;
    }

    public void ShowTakeoverPrompt(bool visible) => TakeoverPrompt.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetTitle(string title)
    {
        _title = title;
        TitleText.Text = title;
    }

    /// <summary>Portal (M4): what the title shows while browsing ("Downloads › Mods"), and whether Back is offered.</summary>
    public void SetPortalLocation(string breadcrumb, bool canGoBack)
    {
        TitleText.Text = breadcrumb;
        BackButton.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Portal (M4): a message instead of items (folder missing or unreadable); null hides it.</summary>
    public void ShowPortalMessage(string? message)
    {
        PortalMessage.Text = message ?? "";
        PortalMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Portal (M4): the sort shown as checked.</summary>
    public void SetSortChecked(FenceSort sort)
    {
        foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = _isPortal && (FenceSort)sortItem.Tag == sort;
    }

    /// <summary>
    /// Shows exactly these items in this order, by moving, adding and removing views in place: items already shown keep
    /// their name, icon, selection and the scroll position (M2b review carry-over; duplicate refs are tolerated).
    /// </summary>
    public void SetItems(IReadOnlyList<string> itemRefs)
    {
        if (_items.Select(view => view.ItemRef).SequenceEqual(itemRefs, StringComparer.Ordinal)) return;
        var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var itemRef in itemRefs) wanted[itemRef] = wanted.GetValueOrDefault(itemRef) + 1;
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            var itemRef = _items[index].ItemRef;
            if (wanted.GetValueOrDefault(itemRef) > 0) wanted[itemRef]--;
            else RemoveItemAt(index);
        }
        // ponytail: O(n²) moves in the worst case (a full reorder of hundreds of items); a keyed diff when that shows up.
        var iconSizePx = IconSizePx;
        for (var index = 0; index < itemRefs.Count; index++)
        {
            if (index < _items.Count && _items[index].ItemRef == itemRefs[index]) continue;
            var found = -1;
            for (var later = index + 1; later < _items.Count && found < 0; later++)
            {
                if (_items[later].ItemRef == itemRefs[index]) found = later;
            }
            if (found >= 0)
            {
                CancelRename(_items[found]);
                _items.Move(found, index);
                continue;
            }
            var view = new FenceItemView(itemRefs[index]);
            if (IsLoaded) _iconLoader.Request(view, iconSizePx); // before that, Loaded requests them at the right DPI (M2b review)
            _items.Insert(index, view);
        }
        // Cells may have shifted under a shown name without a scroll or selection event (final review I3).
        Dispatcher.BeginInvoke(UpdateHoverLabel, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// A rename whose own item is moved or removed would lose its box (and commit half-typed text): cancel only that one,
    /// right before. An item that merely shifts keeps its box (M3a review I3; final review M1).
    /// </summary>
    private static void CancelRename(FenceItemView view)
    {
        if (view.IsEditing) view.IsEditing = false;
    }

    private void RemoveItemAt(int index)
    {
        CancelRename(_items[index]);
        _items.RemoveAt(index);
    }

    private int IconSizePx => (int)Math.Round(_iconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);

    /// <summary>One of <see cref="ConfigNormalizer.IconSizes"/> (DIPs). Icons are reloaded at the new size.</summary>
    public void SetIconSize(int iconSizeDips)
    {
        if (iconSizeDips == _iconSizeDips) return; // nothing to reload (M2c review carry-over)
        _iconSizeDips = iconSizeDips;
        Resources["IconSize"] = (double)iconSizeDips;
        Resources["ArrowSize"] = Math.Max(12.0, Math.Round(iconSizeDips * 0.36));
        ApplyItemWidth();
        foreach (var sizeItem in IconSizeItem.Items.OfType<MenuItem>()) sizeItem.IsChecked = (int)sizeItem.Tag == iconSizeDips;
        ReloadIcons();
    }

    /// <summary>Labels always, or icons only with the name popping under the hovered or selected icon (M8b, user choice).</summary>
    public void SetLabelMode(LabelMode labelMode)
    {
        _labelMode = labelMode;
        Resources["LabelVisibility"] = labelMode == LabelMode.Always ? Visibility.Visible : Visibility.Collapsed;
        Resources["ItemToolTips"] = labelMode == LabelMode.Always; // the pop-under name replaces the tooltip
        LabelsAlwaysItem.IsChecked = labelMode == LabelMode.Always;
        LabelsOnHoverItem.IsChecked = labelMode == LabelMode.OnHover;
        ApplyItemWidth();
        UpdateHoverLabel();
    }

    /// <summary>Settings → "Show shortcut arrows" (M8b, off by default).</summary>
    public void SetShortcutArrows(bool show) => Resources["ShortcutArrowVisibility"] = show ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyItemWidth()
    {
        Resources["EditItemWidth"] = LabelledItemWidth;
        ApplyCellWidth();
    }

    private void ApplyCellWidth() =>
        // With labels: room for two short words under small icons. Icons only: a tight grid.
        Resources["ItemWidth"] = _labelMode == LabelMode.Always ? LabelledItemWidth : _iconSizeDips + 12.0;

    private double LabelledItemWidth => Math.Max(76.0, _iconSizeDips + 28.0);

    private void OnItemMouseEnter(object sender, MouseEventArgs args)
    {
        _hoveredContainer = sender as ListBoxItem;
        UpdateHoverLabel();
    }

    private void OnItemMouseLeave(object sender, MouseEventArgs args)
    {
        if (ReferenceEquals(_hoveredContainer, sender)) _hoveredContainer = null;
        UpdateHoverLabel();
    }

    /// <summary>
    /// Icon-only fences: the name of the hovered item (else the one selected item) pops up under its icon, over the
    /// neighbours, kept inside the fence (above the icon when there is no room below). Drawn in the fence window itself,
    /// so it stays at the fence's place in the window stack (no popup window above other apps).
    /// </summary>
    private void UpdateHoverLabel()
    {
        var container = _labelMode != LabelMode.OnHover ? null
            : _hoveredContainer ?? (ItemList.SelectedItems.Count == 1 ? ItemList.ItemContainerGenerator.ContainerFromItem(ItemList.SelectedItem) as ListBoxItem : null);
        if (container is not { DataContext: FenceItemView { IsEditing: false } view, IsVisible: true })
        {
            HoverLabel.Visibility = Visibility.Collapsed;
            return;
        }
        _hoverLabelContainer = container;
        HoverLabel.DataContext = view; // bound: a name that loads later shows up (final review I3)
        // Never wider than the fence (narrow icon-only fences, final review I1); 18 = padding + border + margins.
        HoverLabelText.MaxWidth = Math.Clamp(ItemList.ActualWidth - 18, 24, 180);
        HoverLabel.Visibility = Visibility.Visible;
        PlaceHoverLabel();
    }

    /// <summary>Under the cell, clamped to the fence's sides; above it when there is no room; hidden when it is scrolled away.</summary>
    private void PlaceHoverLabel()
    {
        if (HoverLabel.Visibility != Visibility.Visible || _hoverLabelContainer is not { IsVisible: true } container) return;
        HoverLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = HoverLabel.DesiredSize;
        var cell = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
        if (cell.Bottom <= 0 || cell.Top >= ItemList.ActualHeight)
        {
            HoverLabel.Visibility = Visibility.Collapsed; // a selected item scrolled out of view (final review I1)
            return;
        }
        var left = Math.Clamp(cell.Left + cell.Width / 2 - size.Width / 2, 2, Math.Max(2, ItemList.ActualWidth - size.Width - 2));
        var top = cell.Bottom - 2;
        if (top + size.Height > ItemList.ActualHeight - 2) top = Math.Max(2, cell.Top - size.Height + 2);
        Canvas.SetLeft(HoverLabel, left);
        Canvas.SetTop(HoverLabel, top);
    }

    /// <summary>Locked: the title no longer drags and the edges no longer resize.</summary>
    public void SetLocked(bool locked)
    {
        LockItem.IsChecked = locked;
        _locked = locked;
        ApplyChrome();
    }

    /// <summary>Locked: no drag, no resize. Rolled up: drag, no resize (its height is the stored full height).</summary>
    private void ApplyChrome()
    {
        // A fresh WindowChrome each time: editing the attached one in place is not re-applied after an unlock.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(0),
            CaptionHeight = _locked ? 0 : CaptionHeightDips,
            ResizeBorderThickness = new Thickness(_locked || _rolledUp ? 0 : ResizeBorderDips),
            // WindowChrome owns the window region and re-applies it on every resize: a radius of 0 kept resetting it to
            // a square, so the blur showed outside the rounded border (user screenshot 2026-10-03). Same radius as the border.
            CornerRadius = new CornerRadius(CornerRadiusDips),
            UseAeroCaptionButtons = false,
        });
    }

    /// <summary>Puts the fence at its full rect (physical pixels); rolled up, only the title bar of it shows.</summary>
    public void Place(PixelRect fullRect)
    {
        _heightAnimation.Stop();
        _fullHeightPx = fullRect.Height;
        FenceWindowChrome.SetPixelRect(Handle, _rolledUp && !_expansion.Expanded ? fullRect with { Height = RolledUpHeightPx } : fullRect);
    }

    public void SetRolledUp(bool rolledUp)
    {
        if (rolledUp == _rolledUp) return;
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (rolledUp && !_expansion.Expanded) _fullHeightPx = _heightAnimation.IsEnabled ? _heightTo : current.Height;
        _rolledUp = rolledUp;
        _expansion.Reset();
        ApplyChrome();
        AnimateHeight(rolledUp ? RolledUpHeightPx : _fullHeightPx);
        if (rolledUp) _hoverTimer.Start();
        else _hoverTimer.Stop();
    }

    /// <summary>Roll-up expand mode from settings (M6b): hover or click.</summary>
    public void SetRollupExpand(RollupExpand mode) => _expansion.Mode = mode;

    /// <summary>Shows at once (Pause, layout): a quick-hide fade still running must not hide the fence afterwards (M8a).</summary>
    public void ShowNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Show();
    }

    /// <summary>Hides at once (Pause).</summary>
    public void HideNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    /// <summary>Quick-hide (spec §6): a 150 ms fade, then hidden. Without animations it hides at once.</summary>
    public void HideFaded()
    {
        var generation = ++_fadeGeneration;
        if (!IsVisible || !AnimationsAllowed())
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
            return;
        }
        var fade = new DoubleAnimation(0, FadeDuration);
        fade.Completed += (_, _) =>
        {
            if (generation != _fadeGeneration) return; // shown again meanwhile
            Hide();
            BeginAnimation(OpacityProperty, null);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Shows the fence (fading in when animations are on). The host sends it to the bottom afterwards.</summary>
    public void ShowFaded()
    {
        ++_fadeGeneration;
        var fadeIn = AnimationsAllowed();
        BeginAnimation(OpacityProperty, null);
        Show();
        if (fadeIn) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeDuration));
    }

    /// <summary>Click mode: a single click on the rolled-up title opens it.</summary>
    private bool ClickToOpen()
    {
        if (!_rolledUp || !_expansion.Click()) return false;
        AnimateHeight(_fullHeightPx);
        return true;
    }

    /// <summary>Roll-up and roll-down move the bottom edge over 200 ms (ease-out); at once without animations.</summary>
    private void AnimateHeight(int targetPx)
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (!AnimationsAllowed() || _drag is not null || current.Height == targetPx)
        {
            _heightAnimation.Stop();
            FenceWindowChrome.SetPixelRect(Handle, current with { Height = targetPx });
            return;
        }
        _heightFrom = current.Height;
        _heightTo = targetPx;
        _heightClock = System.Diagnostics.Stopwatch.StartNew();
        _heightAnimation.Start();
    }

    private void StepHeight()
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        var progress = Math.Min(1.0, _heightClock.Elapsed / RollUpDuration);
        if (_drag is not null) progress = 1; // never fight a move: jump to the end
        var eased = 1 - Math.Pow(1 - progress, 3);
        FenceWindowChrome.SetPixelRect(Handle, current with { Height = (int)Math.Round(_heightFrom + (_heightTo - _heightFrom) * eased) });
        if (progress >= 1) _heightAnimation.Stop();
    }

    private static readonly TimeSpan HoverTick = TimeSpan.FromMilliseconds(100);

    /// <summary>Title row plus the 1-DIP border above and below it.</summary>
    private int RolledUpHeightPx => (int)Math.Round((CaptionHeightDips + 2) * VisualTreeHelper.GetDpi(this).DpiScaleY);

    // ponytail: polls the cursor every 100 ms while rolled up (no mouse-leave on a no-activate layered window when
    // the pointer leaves fast); in hover mode it also opens during a file drag, which is wanted.
    private void OnHoverTick()
    {
        if (Handle == 0 || !_rolledUp) return;
        if (_drag is not null || BodyContextMenu.IsOpen || _renaming || _items.Any(view => view.IsEditing)) return; // never close under the user
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        var rect = FenceWindowChrome.GetPixelRect(Handle);
        // While the height animates, judge "inside" against where the fence is going, so it does not flicker shut.
        var height = _heightAnimation.IsEnabled ? Math.Max(rect.Height, _heightTo) : rect.Height;
        var inside = cursorX >= rect.X && cursorX < rect.X + rect.Width && cursorY >= rect.Y && cursorY < rect.Y + height;
        if (!_expansion.Tick(inside)) return;
        AnimateHeight(_expansion.Expanded ? _fullHeightPx : RolledUpHeightPx);
    }

    /// <summary>Colours for Windows' light or dark app mode (M2c: fences follow Windows).</summary>
    public void ApplyTheme(bool light)
    {
        _lightTheme = light;
        var ink = light ? Colors.Black : Colors.White;
        SolidColorBrush Ink(byte alpha)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B));
            brush.Freeze();
            return brush;
        }
        // The accent blur ignores its tint colour (ACCENT_ENABLE_BLURBEHIND), so the veil is drawn here: none in dark mode
        // (the look the user approved in M2a), a dense light veil in light mode so dark text reads on any wallpaper.
        var veil = new SolidColorBrush(light ? Color.FromArgb(0xB8, 0xF2, 0xF2, 0xF2) : Colors.Transparent);
        veil.Freeze();
        Resources["FenceVeil"] = veil;
        Resources["FenceText"] = Ink(light ? (byte)0xE6 : (byte)0xFF);
        Resources["FenceSubtleText"] = Ink(0xA0);
        Resources["FenceBorder"] = Ink(light ? (byte)0x33 : (byte)0x40);
        Resources["FenceDivider"] = Ink(light ? (byte)0x1F : (byte)0x26);
        Resources["FenceHover"] = Ink(light ? (byte)0x14 : (byte)0x22);
        Resources["FenceSelected"] = Ink(light ? (byte)0x2A : (byte)0x44);
        Resources["FenceScrollThumb"] = Ink(light ? (byte)0x44 : (byte)0x55);
        var hoverLabel = new SolidColorBrush(light ? Color.FromArgb(0xF0, 0xF4, 0xF4, 0xF4) : Color.FromArgb(0xE6, 0x20, 0x22, 0x28));
        hoverLabel.Freeze();
        Resources["HoverLabelBackground"] = hoverLabel;
        // Soft halo behind labels, like desktop icon labels: readable on busy or bright wallpapers (user choice).
        var shadow = new DropShadowEffect
        {
            Color = light ? Colors.White : Colors.Black,
            ShadowDepth = light ? 0 : 1,
            BlurRadius = 4,
            Opacity = 0.9,
        };
        shadow.Freeze();
        Resources["LabelShadow"] = shadow;
    }

    /// <summary>The Recycle Bin turned full or empty, or another special icon changed (M8c).</summary>
    public void ReloadSpecialIcons()
    {
        var iconSizePx = IconSizePx;
        foreach (var view in _items.Where(view => view.ItemRef.StartsWith("::", StringComparison.Ordinal))) _iconLoader.Request(view, iconSizePx);
    }

    /// <summary>New size or DPI: every icon is requested again in place; names, selection and renames stay (M2c review carry-over).</summary>
    private void ReloadIcons()
    {
        var iconSizePx = IconSizePx;
        foreach (var view in _items) _iconLoader.Request(view, iconSizePx);
    }

    /// <summary>Starts renaming the fence title (menu, or a freshly drawn fence).</summary>
    public void BeginRename()
    {
        _renaming = true;
        TitleBox.Text = _title;
        ShowTitleOrTabs();
        TitleBox.Visibility = Visibility.Visible;
        Activate(); // keyboard input needs the fence active; it stays at the bottom (owned by Progman, ADR-011)
        // With another app in front the fence may not get focus (ADR-015); then typing would go elsewhere and the box
        // could never close, so give up instead (M2c review).
        if (!TitleBox.Focus() || !IsActive)
        {
            EndRename(commit: false);
            return;
        }
        TitleBox.SelectAll();
    }

    private void EndRename(bool commit)
    {
        if (!_renaming) return;
        _renaming = false;
        TitleBox.Visibility = Visibility.Collapsed;
        ShowTitleOrTabs();
        if (commit && TitleBox.Text != _title) RenameRequested?.Invoke(TitleBox.Text);
    }

    private void OnTitleBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Escape)) return;
        EndRename(commit: args.Key == Key.Enter);
        ItemList.Focus();
        args.Handled = true;
    }

    private void OnItemListKeyDown(object sender, KeyEventArgs args)
    {
        if (args.OriginalSource is TextBox) return; // keys typed into the rename box
        var selected = ItemList.SelectedItems.OfType<FenceItemView>().ToList();
        switch (args.Key)
        {
            case Key.Enter when selected.Count == 1:
                OpenRequested?.Invoke(selected[0].ItemRef);
                break;
            case Key.Enter when selected.Count > 1:
                OpenManyRequested?.Invoke(selected.Select(view => view.ItemRef).ToList());
                break;
            case Key.Delete when selected.Count > 0:
                RecycleRequested?.Invoke(selected.Select(view => view.ItemRef).ToList()); // Shift+Del too: always the Recycle Bin
                break;
            case Key.Back when _isPortal:
                BackRequested?.Invoke();
                break;
            case Key.F2 when selected.Count == 1:
                BeginItemRename(selected[0].ItemRef);
                break;
            default:
                return;
        }
        args.Handled = true;
    }

    /// <summary>Starts renaming one item in place (F2, or Rename in Windows' item menu). Special items cannot be renamed.</summary>
    public void BeginItemRename(string itemRef)
    {
        var view = _items.FirstOrDefault(candidate => candidate.ItemRef == itemRef);
        if (view is null || itemRef.StartsWith("::", StringComparison.Ordinal)) return;
        ItemList.ScrollIntoView(view);
        view.EditName = view.Label;
        view.IsEditing = true; // the box shows; OnLabelBoxVisibleChanged focuses it
    }

    private void OnLabelBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not TextBox { IsVisible: true, DataContext: FenceItemView view } box) return;
        Activate();
        // Without focus typing would go to another app and the box could never close (ADR-015): give up instead.
        if (!box.Focus() || !IsActive)
        {
            view.IsEditing = false;
            return;
        }
        // Like Explorer: select the name, not the extension.
        var extensionStart = box.Text.LastIndexOf('.');
        box.Select(0, extensionStart > 0 ? extensionStart : box.Text.Length);
    }

    private void OnLabelBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (sender is not TextBox { DataContext: FenceItemView view } || args.Key is not (Key.Enter or Key.Escape)) return;
        EndItemRename(view, commit: args.Key == Key.Enter);
        ItemList.Focus();
        args.Handled = true;
    }

    private void OnLabelBoxLostFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (sender is TextBox { DataContext: FenceItemView view }) EndItemRename(view, commit: true);
    }

    private void EndItemRename(FenceItemView view, bool commit)
    {
        if (!view.IsEditing) return;
        view.IsEditing = false;
        var newName = view.EditName.Trim();
        if (commit && newName.Length > 0 && newName != view.Label) ItemRenameRequested?.Invoke(view.ItemRef, newName);
    }

    /// <summary>Right-click on an item opens Windows' item menu instead of the fence menu.</summary>
    private void OnBodyContextMenuOpening(object sender, ContextMenuEventArgs args)
    {
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is not ListBoxItem { DataContext: FenceItemView clicked } container) return;
        args.Handled = true;
        if (!container.IsSelected)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
        }
        // From the mouse, or (menu key: CursorLeft < 0) from the item's corner. PointToScreen gives physical pixels.
        var anchor = args.CursorLeft >= 0 ? PointToScreen(Mouse.GetPosition(this)) : container.PointToScreen(new Point(container.ActualWidth / 2, container.ActualHeight / 2));
        // The clicked item first: menu → Rename renames it, whatever else is selected (user choice 2026-10-03).
        List<string> refs = [clicked.ItemRef, .. ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).Where(itemRef => itemRef != clicked.ItemRef)];
        ItemMenuRequested?.Invoke(refs, (int)anchor.X, (int)anchor.Y, args.CursorLeft < 0);
    }

    /// <summary>The item under a screen point (physical pixels) and the index to insert before when dropping there.</summary>
    public FenceDropPoint HitTest(int screenX, int screenY)
    {
        // Over a tab header: show that tab now, so the drop lands in it (M9).
        if (TabHeaderAt(screenX, screenY) is { } hoveredTab && hoveredTab != FenceId) TabSelected?.Invoke(hoveredTab);
        var point = ItemList.PointFromScreen(new Point(screenX, screenY));
        string? hovered = null;
        var cells = new List<(double Left, double Top, double Width, double Height)>();
        var cellIndexes = new List<int>();
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            // Only the middle of an item means "into it" (folders, Recycle Bin); its edges reorder (M3b review I2).
            if (DropZones.IsInto(bounds.Left, bounds.Top, bounds.Width, bounds.Height, point.X, point.Y)) hovered = _items[index].ItemRef;
            cells.Add((bounds.Left, bounds.Top, bounds.Width, bounds.Height));
            cellIndexes.Add(index);
        }
        // Rows reach down to their tallest item (mixed label heights, M3b review).
        var cellAt = DropZones.InsertIndex(cells, point.X, point.Y);
        return new FenceDropPoint(hovered, cellAt < cellIndexes.Count ? cellIndexes[cellAt] : _items.Count);
    }

    /// <summary>Shows where a drag would land: a caret before the insert position, or a highlight on the container taking it.</summary>
    public void ShowDropFeedback(FenceDropPoint? drop, bool into)
    {
        InsertCaret.Visibility = Visibility.Collapsed;
        DropHighlight.Visibility = Visibility.Collapsed;
        if (drop is not { } point) return;
        if (into && _items.FirstOrDefault(view => view.ItemRef == point.ItemRef) is { } target
            && ItemList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem targetContainer)
        {
            var cell = targetContainer.TransformToAncestor(ItemList).TransformBounds(new Rect(targetContainer.RenderSize));
            Canvas.SetLeft(DropHighlight, cell.Left);
            Canvas.SetTop(DropHighlight, cell.Top);
            DropHighlight.Width = cell.Width;
            DropHighlight.Height = cell.Height;
            DropHighlight.Visibility = Visibility.Visible;
            return;
        }
        // Caret at the left edge of the item it goes before, or after the last item.
        var before = point.InsertAt < _items.Count ? ItemList.ItemContainerGenerator.ContainerFromIndex(point.InsertAt) as ListBoxItem : null;
        var anchor = before ?? (_items.Count > 0 ? ItemList.ItemContainerGenerator.ContainerFromIndex(_items.Count - 1) as ListBoxItem : null);
        if (anchor is null) return;
        var bounds = anchor.TransformToAncestor(ItemList).TransformBounds(new Rect(anchor.RenderSize));
        Canvas.SetLeft(InsertCaret, (before is null ? bounds.Right : bounds.Left) - 1);
        Canvas.SetTop(InsertCaret, bounds.Top + 4);
        InsertCaret.Height = Math.Max(0, bounds.Height - 8);
        InsertCaret.Visibility = Visibility.Visible;
    }

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject source) where TAncestor : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TAncestor match) return match;
        }
        return null;
    }

    private void OnListPress(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null) return;
        // Text selection in the rename box must never start a drag of the file (M3b review I3).
        if (args.OriginalSource is DependencyObject pressed && FindAncestor<TextBox>(pressed) is not null) return;
        var container = args.OriginalSource is DependencyObject element ? FindAncestor<ListBoxItem>(element) : null;
        if (container is null)
        {
            // Empty space: rubber band. Without Ctrl it starts a new selection. The list takes focus, so a pending rename
            // commits and the keys after the band go to the list (M3b review carry-over).
            ItemList.Focus();
            _bandStart = args.GetPosition(ItemList);
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ItemList.SelectedItems.Clear();
            ItemList.CaptureMouse();
            args.Handled = true;
            return;
        }
        _pressPoint = args.GetPosition(ItemList);
        // Ctrl+press on a selected item: unselect it on release, not now, so Ctrl+drag can still copy the selection (M3b review).
        if (container.IsSelected && Keyboard.Modifiers == ModifierKeys.Control && args.ClickCount == 1)
        {
            _deferredToggle = container;
            container.Focus();
            args.Handled = true;
            return;
        }
        // Pressing one of several selected items must keep the selection, so they can be dragged together.
        if (container.IsSelected && ItemList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None && args.ClickCount == 1)
        {
            _deferredSelect = container;
            container.Focus();
            args.Handled = true;
        }
    }

    private void OnListMove(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed)
        {
            _pressPoint = null;
            return;
        }
        var position = args.GetPosition(ItemList);
        if (_bandStart is { } bandStart)
        {
            UpdateBand(bandStart, position);
            return;
        }
        if (_pressPoint is not { } pressPoint) return;
        if (Math.Abs(position.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressPoint = null;
        _deferredSelect = null;
        _deferredToggle = null; // dragged: the item stays selected
        var dragged = ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).ToList();
        if (dragged.Count > 0) DragRequested?.Invoke(dragged); // returns when the drag ends (Windows' modal loop)
    }

    private void OnListRelease(object sender, MouseButtonEventArgs args)
    {
        _pressPoint = null;
        if (_deferredToggle is { } toggled)
        {
            toggled.IsSelected = false;
            _deferredToggle = null;
        }
        if (_deferredSelect is { } container)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
            _deferredSelect = null;
        }
        if (_bandStart is not null)
        {
            ItemList.ReleaseMouseCapture(); // ends the band via LostMouseCapture
            args.Handled = true;
        }
    }

    /// <summary>Selects every item the band touches (added to the selection when Ctrl was held at the start).</summary>
    private void UpdateBand(Point start, Point current)
    {
        var band = new Rect(start, current);
        Canvas.SetLeft(SelectionBand, band.Left);
        Canvas.SetTop(SelectionBand, band.Top);
        SelectionBand.Width = band.Width;
        SelectionBand.Height = band.Height;
        SelectionBand.Visibility = Visibility.Visible;
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            if (bounds.IntersectsWith(band)) container.IsSelected = true;
            else if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) container.IsSelected = false;
        }
    }

    private void EndBand()
    {
        _bandStart = null;
        SelectionBand.Visibility = Visibility.Collapsed;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        // A double-click in the rename box selects a word; it must not open the file (M3a review carry-over).
        if (args.OriginalSource is DependencyObject source && FindAncestor<TextBox>(source) is not null) return;
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is ListBoxItem { DataContext: FenceItemView view })
            OpenRequested?.Invoke(view.ItemRef);
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        Handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(Handle).AddHook(OnMessage);
        FenceWindowChrome.ApplyToolWindowStyles(Handle);
        FenceWindowChrome.ApplyAccentBlur(Handle);
        if (!FenceWindowChrome.UseRoundedCorners(Handle)) CornersUnavailable = true; // Windows 10: square blur corners (ADR-024)
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Windows' item menu draws "Send to", "Open with" and shell-extension entries through its owner window.
        if (ShellItemMenu.HandleMenuMessage(message, wParam, lParam, out var menuResult))
        {
            handled = true;
            return menuResult;
        }
        switch (message)
        {
            case WmWindowPosChanging:
                if (!Peeking) FenceWindowChrome.KeepAtBottom(lParam); // fences never rise above apps, except during Peek
                break;
            // Click mode: the first press on a rolled-up title opens it instead of starting a move (M6b).
            case WmNcLeftButtonDown when wParam == HitTestCaption && IsSecondCaptionClick(lParam):
                RollUpToggled?.Invoke();
                handled = true;
                return 0;
            case WmNcLeftButtonDown when wParam == HitTestCaption && ClickToOpen():
                handled = true;
                return 0;
            case WmNcLeftButtonDoubleClick when wParam == HitTestCaption:
                _lastCaptionPress = null;
                // A third quick click after a recognised double-click arrives as DBLCLK: not a second toggle (M8b review).
                if (!IsRightAfterRecognisedDoubleClick()) RollUpToggled?.Invoke();
                _recognisedDoubleClickAt = null; // used once: a later genuine double-click toggles (M8c review M12)
                handled = true;
                return 0;
            case WmEnterSizeMove:
                // A move that starts during a roll-up animation finishes it. Windows' move loop captured the partial rect
                // and proposes it on every step, so WM_MOVING keeps the finished height too (M6b review M2).
                _moveHeightPx = null;
                if (_heightAnimation.IsEnabled)
                {
                    _heightAnimation.Stop();
                    _moveHeightPx = _heightTo;
                    FenceWindowChrome.SetPixelRect(Handle, FenceWindowChrome.GetPixelRect(Handle) with { Height = _heightTo });
                }
                _drag = new DragTracker(FenceWindowChrome.GetPixelRect(Handle));
                break;
            case WmMoving when SnapRect is not null && _drag is not null:
                var proposed = FenceWindowChrome.ReadRect(lParam);
                if (_moveHeightPx is { } heightPx) proposed = proposed with { Height = heightPx };
                FenceWindowChrome.WriteRect(lParam, _drag.Step(proposed, snap: rect => SnapRect(rect, SnapEdges.Move)));
                handled = true;
                return 1;
            case WmSizing when SnapRect is not null && _drag is not null:
                var edges = SizingEdges((int)wParam);
                FenceWindowChrome.WriteRect(lParam, _drag.Step(FenceWindowChrome.ReadRect(lParam), snap: rect => SnapRect(rect, edges)));
                handled = true;
                return 1;
            case WmExitSizeMove:
                _drag = null;
                _moveHeightPx = null;
                // Rolled up, the window is shorter than the fence: the stored rect keeps the full height.
                var moved = FenceWindowChrome.GetPixelRect(Handle);
                MovedByUser?.Invoke(this, _rolledUp ? moved with { Height = _fullHeightPx } : moved);
                if (_rolledUp && !_expansion.Expanded && moved.Height != RolledUpHeightPx) AnimateHeight(RolledUpHeightPx); // a move during an animation
                break;
        }
        return 0;
    }

    /// <summary>
    /// Every caption press is remembered; a second one within the double-click time and size counts as a double-click.
    /// Right after another window hands the fence the foreground, Windows sends the second press as a plain
    /// WM_NCLBUTTONDOWN instead of WM_NCLBUTTONDBLCLK, so the first double-click did nothing (M6a finding 4, M8b).
    /// </summary>
    /// <param name="pointParam">The press's screen point (WM_NCLBUTTONDOWN's lParam): where it happened, not where the
    /// cursor is when a busy UI thread gets to it; its time is the message's own (M8b review).</param>
    private bool IsSecondCaptionClick(nint pointParam)
    {
        var (milliseconds, widthPx, heightPx) = FenceWindowChrome.DoubleClickSettings();
        var at = FenceWindowChrome.MessageTime();
        var (x, y) = ((short)(pointParam & 0xFFFF), (short)((pointParam >> 16) & 0xFFFF));
        var isSecond = _lastCaptionPress is { } last && unchecked(at - last.At) <= milliseconds
                       && Math.Abs(x - last.Where.X) <= widthPx / 2.0 && Math.Abs(y - last.Where.Y) <= heightPx / 2.0;
        _lastCaptionPress = isSecond ? null : (at, new Point(x, y));
        if (isSecond) _recognisedDoubleClickAt = at;
        return isSecond;
    }

    private bool IsRightAfterRecognisedDoubleClick() =>
        _recognisedDoubleClickAt is { } recognised && unchecked(FenceWindowChrome.MessageTime() - recognised) <= FenceWindowChrome.DoubleClickSettings().Milliseconds;

    /// <summary>WM_SIZING's WMSZ_* value as the edges being dragged.</summary>
    private static SnapEdges SizingEdges(int sizingEdge) => sizingEdge switch
    {
        1 => SnapEdges.Left,
        2 => SnapEdges.Right,
        3 => SnapEdges.Top,
        4 => SnapEdges.Top | SnapEdges.Left,
        5 => SnapEdges.Top | SnapEdges.Right,
        6 => SnapEdges.Bottom,
        7 => SnapEdges.Bottom | SnapEdges.Left,
        8 => SnapEdges.Bottom | SnapEdges.Right,
        _ => SnapEdges.None,
    };
}
```
`src/NeoFences.App/FenceHost.cs`:
```csharp
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Config;
using NeoFences.Core.Input;
using NeoFences.Core.Layouts;
using NeoFences.Core.Lifecycle;
using NeoFences.Core.Membership;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Owns the fences on the desktop: loads the config, places one <see cref="FenceWindow"/> per fence on the current
/// monitors, keeps fence contents in step with the Desktop folders (reconcile at start, then watcher events),
/// saves changes (debounced 500 ms, ADR-006), and keeps everything attached through display changes,
/// Explorer restarts and sign-out (ADR-011, ADR-013). Every Win32/COM call goes through NeoFences.Shell.
/// </summary>
public sealed class FenceHost
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReattachInterval = TimeSpan.FromMilliseconds(500);
    private const double SnapGapDips = 8;        // spec §6: 8 px spacing from other fences and screen edges
    private const double SnapThresholdDips = 12; // how close an edge must come before it snaps
    private const int ReattachAttempts = 10;
    private const int PeekHotkeyId = 1;
    private const int PeekEscapeHotkeyId = 2;
    private static readonly TimeSpan DrawFrame = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan TrayRetryInterval = TimeSpan.FromSeconds(2);
    private const int TrayRetryAttempts = 15;

    private readonly ConfigStore _store = new(AppPaths.DataDirectory);
    private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
    private readonly SystemMessageWindow _messages = new();
    private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
    private DesktopWatcher? _desktopWatcher;
    private readonly ShellWorker _shellWorker = new(); // open, recycle, rename: off the UI thread, on STA (M3a review)
    private SpecialIconNotifications? _specialIcons;
    private bool _specialIconsDeferred;
    private readonly DispatcherTimer _specialIconsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // Watcher trouble arrives in bursts: one re-arm and one reconcile per burst; a watcher that fails again at once waits longer (M8a review).
    private readonly DispatcherTimer _watcherRecoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _rearmWatcher;
    private DateTime _lastWatcherRearm = DateTime.MinValue;
    private TimeSpan _watcherRearmDelay = WatcherBackoff.First;
    // Safe-save memory and expected drop arrivals (FenceMembership.SafeSaveWindow).
    private IReadOnlyList<RememberedPlacement> _rememberedPlacements = [];
    private readonly Dictionary<string, IDisposable> _dropRegistrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PortalState> _portals = new(StringComparer.Ordinal); // M4: Portal fences by id
    private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
    private IReadOnlyList<MonitorPlacement> _monitors = [];
    private bool _takeoverActive;
    private bool _lightTheme = SystemTheme.AppsUseLightTheme();
    private bool _sessionEnding;
    // M5 desktop gestures
    private DesktopMouseHook? _mouseHook;
    private bool _quickHidden;              // double-click on the desktop: fences (and icons) hidden until the next one
    private bool _iconsHiddenByUser;        // RunState.IconsHiddenByUser: set when quick-hide begins, cleared when it ends
    private DrawFenceOverlay? _drawOverlay; // right-drag on the desktop: the fence being drawn
    private DispatcherTimer? _drawTimer;
    private (int X, int Y) _drawStart;
    private GlobalHotkey? _peekHotkey;
    private GlobalHotkey? _peekEscapeHotkey; // Esc ends Peek; registered only while peeking
    private bool _peeking;
    private bool _recordingHotkey;          // Settings' hotkey box has the keyboard: the Peek hotkey is released
    private string? _peekHotkeyProblem;     // why the Peek hotkey is not registered (Settings shows it), null when it is
    private bool _cornersLogged;
    private FenceWindow? _movingWindow; // the box being moved by its title (not resized): it may merge where it is dropped (M9)
    // M6a
    private bool _paused;                    // tray: Pause NeoFences (not saved)
    private bool _gameMode;                  // a full-screen app is in front: idle (spec §4.7, ADR-021)
    private ForegroundWatcher? _foregroundWatcher;
    private TrayIcon? _trayIcon;
    private readonly List<DesktopChange> _deferredDesktopChanges = [];
    private bool _reconcileDeferred;
    private const int MaxDeferredDesktopChanges = 500;
    private const int TrayNewFence = 1, TrayQuickHide = 2, TrayPeek = 3, TrayPause = 4, TrayExit = 5, TraySettings = 6;
    private SettingsWindow? _settingsWindow; // M6b: one at a time

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
        _messages.HotkeyPressed += OnHotkey;
        _messages.TrayMenuRequested += ShowTrayMenu;
        _messages.SessionUnlocked += OnSessionUnlocked;
        _messages.SpecialIconsChanged += ScheduleSpecialIconRefresh;
        _messages.DeviceRemovalRequested += handle =>
        {
            foreach (var (fenceId, portal) in _portals)
            {
                if (portal.ReleaseForRemoval(handle)) Log.Information("drive of Portal {FenceId} is being removed: released it", fenceId);
            }
        };
        _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
        _watcherRecoveryTimer.Tick += (_, _) => RecoverDesktopWatcher();
    }

    public void Start()
    {
        var loaded = _store.Load();
        Log.Information("config loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath})",
            loaded.Source, loaded.IsReadOnly, loaded.CorruptCopyPath);
        _config = loaded.Config;
        _watchdog.LaunchDetached(Environment.ProcessId);
        ApplyStartup(); // after a power loss NeoFences must come back by itself (ADR-019)

        RefreshMonitors();
        foreach (var box in FenceTabs.Boxes(_config)) OpenWindow(box); // one window per box (M9)
        EnsurePortals();
        StartDesktopWatcher(); // first: an item created while the startup reconcile lists the desktop is not missed (M8a)
        ReconcileDesktop();
        StartSpecialIconNotifications();
        ApplyLayout();
        if (_config.Settings.Takeover) SetTakeover(true);
        else if (_watchdog.IsTakeoverActiveMarked)
        {
            // Icons may still be hidden from a run whose Takeover-off never got saved (both processes killed): show them.
            Log.Warning("takeover-active marker found while Takeover is off; showing desktop icons");
            SetIconsHidden(false);
        }
        StartGestures();
        if (!_messages.SessionNotificationsActive) Log.Warning("unlock notices unavailable: the mouse hook is re-installed only after an Explorer restart");
        try
        {
            _trayIcon = new TrayIcon(_messages.Handle, TrayTooltip(), log: message => Log.Warning("{Message}", message));
            ShowTrayIcon();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "tray icon unavailable; fences and gestures keep working"); // hard rule 7: only the tray is lost
        }
        StartGameMode();
        ScheduleSave();
    }

    /// <summary>
    /// The clock for safe-save and drop memory: wall time at start plus a monotonic stopwatch, so a clock change (time
    /// sync, daylight saving, the user) cannot expire or extend a memory early (M8a).
    /// </summary>
    private static readonly DateTimeOffset ClockStart = DateTimeOffset.Now;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static DateTimeOffset Now => ClockStart + Clock.Elapsed;

    /// <summary>All of NeoFences' run-time modes together (Core rules: which fences, icons and hooks they imply).</summary>
    private RunState Current => new(Takeover: _takeoverActive, QuickHidden: _quickHidden, Paused: _paused, GameMode: _gameMode,
        IconsHiddenByUser: _iconsHiddenByUser);

    /// <summary>
    /// Windows asked to end the session (WPF's SessionEnding, inside WM_QUERYENDSESSION). WPF then shuts the app
    /// down, which may be the last thing that ever runs, so the icons come back now and the watchdog is told it is
    /// a session end, not a user exit: if the user cancels the shutdown, the watchdog restarts NeoFences (ADR-013).
    /// </summary>
    public void OnSessionEnding()
    {
        _sessionEnding = true;
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        _trayIcon?.Dispose(); // also on session end: a cancelled shutdown restarts us, and the old icon would linger (M8a)
        _trayIcon = null;
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        // The marker also catches a show that failed earlier (quick-hide ending while Explorer was busy; M5 review M6).
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
        _foregroundWatcher?.Dispose();
        _mouseHook?.Dispose();
        _peekHotkey?.Dispose();
        _peekEscapeHotkey?.Dispose();
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        _specialIcons?.Dispose();
        _shellWorker.Dispose();
        _iconLoader.Dispose();
        _messages.Dispose();
    }

    /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
    public void EmergencyRestoreIcons()
    {
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) DesktopIcons.TrySetHidden(false);
    }

    /// <summary>One window per box (M9): it shows the box's active tab; roll-up and lock are the box's.</summary>
    private void OpenWindow(Fence box)
    {
        var shown = FenceTabs.ActiveOf(_config, box.Id);
        var window = new FenceWindow(shown with { RolledUp = box.RolledUp, Locked = box.Locked }, takeoverActive: _takeoverActive,
            lightTheme: _lightTheme, iconLoader: _iconLoader, rollupExpand: _config.Settings.RollupExpand)
        {
            // Spec §6: no animations when Windows' "Animation effects" are off; spec §4.7: none while gaming.
            AnimationsAllowed = () => !_gameMode && SystemParameters.ClientAreaAnimation,
            BoxId = box.Id,
        };
        window.TabSelected += fenceId => SwitchTab(window, fenceId);
        window.TabDropped += (fenceId, screenX, screenY) => OnTabDropped(window, fenceId, screenX, screenY);
        window.TabColorRequested += color =>
        {
            _config = FenceTabs.SetColor(_config, window.FenceId, color);
            RefreshTabs(window);
            ScheduleSave();
        };
        window.DetachTabRequested += () => DetachTab(window, window.FenceId, dropPoint: null);
        window.TabCycleRequested += step => CycleTab(window, step);
        window.SetTabs(FenceTabs.TabsOf(_config, box.Id), shown.Id);
        window.SnapRect = (rect, edges) => SnapFence(window, rect, edges);
        window.MovedByUser += OnFenceMoved;
        window.RenameRequested += title => RenameFence(window, title);
        window.IconSizeRequested += iconSize => SetFenceIconSize(window, iconSize);
        window.LockToggled += locked => SetFenceLocked(window, locked);
        window.DeleteRequested += () => DeleteFence(window);
        window.NewFenceRequested += CreateFence;
        window.TakeoverToggled += SetTakeover;
        window.ExitRequested += () => ExitRequested?.Invoke();
        window.OpenRequested += itemRef => OpenOrBrowse(window, itemRef);
        window.OpenManyRequested += itemRefs =>
        {
            foreach (var itemRef in itemRefs) OpenItem(itemRef, ownerHandle: window.Handle); // folders open in Explorer
            SetPeek(false);
        };
        window.ItemMenuRequested += (itemRefs, screenX, screenY, fromKeyboard) => ShowItemMenu(window, itemRefs, screenX, screenY, fromKeyboard);
        window.RecycleRequested += itemRefs => RecycleItems(window, itemRefs);
        window.ItemRenameRequested += (itemRef, newName) => RenameItem(window, itemRef, newName);
        window.BackRequested += () => BrowsePortal(window, back: true);
        window.NewPortalRequested += () => CreatePortal(window);
        window.StartupToggled += SetStartWithWindows;
        window.SettingsRequested += OpenSettings;
        window.LabelModeRequested += labels => SetFenceLabels(window, labels);
        window.SetShortcutArrows(_config.Settings.ShowShortcutArrows);
        window.SetStartupChecked(_config.Settings.StartWithWindows);
        window.SortRequested += sort => SortFence(window, sort);
        window.OpenFolderRequested += () => { if (_portals.TryGetValue(window.FenceId, out var portal)) OpenItem(portal.Current, ownerHandle: window.Handle); };
        window.DragRequested += itemRefs =>
            ShellDragDrop.TryDrag(window.Handle, itemRefs, logFailure: failure => Log.Warning(failure, "could not start dragging {ItemRefs}", itemRefs));
        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
        window.RollUpToggled += () => ToggleRollUp(window);
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        if (window.CornersUnavailable && !_cornersLogged)
        {
            _cornersLogged = true;
            Log.Information("Windows refused rounded corners (Windows 10?): fences keep square blur corners"); // M8a review
        }
        RegisterDrops(window); // needs the HWND
        if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", box.Id);
        _windows[box.Id] = window;
    }

    /// <summary>Full pass over the Desktop folders: at start and whenever watcher events were lost.</summary>
    private void ReconcileDesktop()
    {
        var listing = DesktopItems.Enumerate();
        var (reconciled, report) = FenceMembership.Reconcile(_config, listing.ItemRefs, listing.UnavailableFolders,
            remembered: _rememberedPlacements, now: Now);
        _config = reconciled;
        _rememberedPlacements = [.. _rememberedPlacements.Except(report.UsedMemories)]; // each memory places one item once (M8a review)
        if (listing.UnavailableFolders.Count > 0) Log.Warning("desktop folders not readable: {Folders}", listing.UnavailableFolders);
        if (report.Suspicious) Log.Warning("kept fenced items from an unreadable or empty desktop listing until a later reconcile");
        Log.Information("desktop reconciled: {AddedCount} added to the Inbox, {RemovedCount} removed", report.AddedToInbox.Count, report.Removed.Count);
        RefreshWindows();
        ScheduleSave();
    }

    private void StartDesktopWatcher()
    {
        // A folder that cannot be watched degrades to "its changes show after a restart" (hard rule 7).
        _desktopWatcher = new DesktopWatcher((folder, failure) => Log.Error(failure, "cannot watch {Folder}; its changes show after a restart", folder));
        var dispatcher = Dispatcher.CurrentDispatcher;
        _desktopWatcher.Changed += change => dispatcher.BeginInvoke(() => OnDesktopChanged(change));
        _desktopWatcher.Overflowed += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: true));
        _desktopWatcher.ReconcileNeeded += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: false));
    }

    /// <summary>
    /// Events were lost or the watcher stopped (re-arm: .NET disables it after a non-overflow error), or one event could
    /// not be read (reconcile only). Bursts are gathered into one recovery (M8a review).
    /// </summary>
    private void OnDesktopWatcherTrouble(bool rearm)
    {
        if (_desktopWatcher is null) return; // shut down meanwhile
        if (rearm && !_rearmWatcher)
        {
            _rearmWatcher = true;
            // Measured when the failure arrives: the wait itself is not quiet time (Core WatcherBackoff, M8c review I3).
            _watcherRearmDelay = WatcherBackoff.Next(_watcherRearmDelay, lastRearm: _lastWatcherRearm, failureAt: DateTime.UtcNow);
            _watcherRecoveryTimer.Stop(); // a reconcile-only recovery already waiting now waits for the re-arm delay
            _watcherRecoveryTimer.Interval = _watcherRearmDelay;
            _watcherRecoveryTimer.Start();
            return;
        }
        if (_watcherRecoveryTimer.IsEnabled) return;
        _watcherRecoveryTimer.Interval = WatcherBackoff.First;
        _watcherRecoveryTimer.Start();
    }

    private void RecoverDesktopWatcher()
    {
        _watcherRecoveryTimer.Stop();
        if (_desktopWatcher is null) return;
        if (_rearmWatcher)
        {
            _rearmWatcher = false;
            _lastWatcherRearm = DateTime.UtcNow;
            Log.Warning("desktop watcher lost events or stopped; re-armed after {Delay} and reconciling", _watcherRearmDelay);
            _desktopWatcher.Dispose();
            StartDesktopWatcher();
        }
        else
        {
            Log.Information("a desktop change could not be read; reconciling");
        }
        if (Current.ShellWorkDeferred) _reconcileDeferred = true; // after the game
        else ReconcileDesktop();
    }

    /// <summary>Also after an Explorer restart: Explorer brokers the shell's change notices and forgets them (M8c review I6).</summary>
    private void StartSpecialIconNotifications()
    {
        _specialIcons?.Dispose();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _specialIcons = new SpecialIconNotifications(_messages.Handle,
            settingsChanged: () => dispatcher.BeginInvoke(ScheduleSpecialIconRefresh),
            log: (what, failure) => Log.Warning(failure, "{What} unavailable: special icons change after a restart", what));
    }

    private void ScheduleSpecialIconRefresh()
    {
        _specialIconsTimer.Stop(); // a burst (several icons, a Recycle Bin emptying) is one refresh
        _specialIconsTimer.Start();
    }

    /// <summary>"Desktop icon settings" changed (reconcile), or the Recycle Bin turned full or empty (new icon) (M8c).</summary>
    private void RefreshSpecialIcons()
    {
        _specialIconsTimer.Stop();
        if (Current.ShellWorkDeferred)
        {
            _specialIconsDeferred = true; // games get every bit of the machine: after the game (M8c review)
            return;
        }
        var shown = DesktopItems.SpecialIconRefs().ToHashSet(ItemRef.Comparer);
        var fenced = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items)
            .Where(itemRef => itemRef.StartsWith("::", StringComparison.Ordinal)).ToHashSet(ItemRef.Comparer);
        if (!shown.SetEquals(fenced))
        {
            Log.Information("desktop icon settings changed; reconciling");
            if (Current.ShellWorkDeferred) _reconcileDeferred = true;
            else ReconcileDesktop();
        }
        foreach (var window in _windows.Values) window.ReloadSpecialIcons();
        Log.Information("special icons refreshed");
    }

    private void OnDesktopChanged(DesktopChange change)
    {
        if (Current.ShellWorkDeferred)
        {
            // Applied in order when the game is left; a flood (a big download unpacking) becomes one reconcile instead (M8a).
            if (_deferredDesktopChanges.Count < MaxDeferredDesktopChanges) _deferredDesktopChanges.Add(change);
            else _reconcileDeferred = true;
            return;
        }
        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
        RefreshWindows();
        ScheduleSave();
    }

    private void RefreshWindows()
    {
        var showPrompt = !_config.Settings.TakeoverPromptAnswered && !_takeoverActive;
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
            if (!_portals.ContainsKey(shown.Id)) window.SetItems(shown.Items); // Portals refresh on their own (M4 review I1)
            window.ShowTakeoverPrompt(showPrompt && shown.IsInbox);
        }
    }

    private void OpenItem(string itemRef, nint ownerHandle)
    {
        // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6);
        // on an STA thread, as shell handlers and Windows' error dialog expect (M3a review).
        // Each open on its own thread: one waiting on an offline share (and its error dialog) holds up nothing else (M8c review I4).
        ShellWorker.RunAlone(() =>
        {
            if (!ShellItems.TryOpen(itemRef, ownerHandle)) Log.Warning("could not open {ItemRef}", itemRef);
        }, name: "NeoFences open");
    }

    /// <summary>Makes the fence accept drops (M3b): fence items and Desktop files move membership; other files go to Windows.</summary>
    private void RegisterDrops(FenceWindow window)
    {
        try
        {
            _dropRegistrations[window.BoxId] = ShellDragDrop.RegisterFence(window.Handle, new FenceDropHandlers(
                HitTest: window.HitTest,
                MoveItems: (itemRefs, insertAt) =>
                {
                    _config = FenceMembership.MoveItems(_config, itemRefs, window.FenceId, insertAt);
                    RefreshWindows();
                    ScheduleSave();
                },
                ExpectArrivals: (itemRefs, insertAt) =>
                    _rememberedPlacements = FenceMembership.ExpectArrivals(_rememberedPlacements, itemRefs, window.FenceId, insertAt, Now),
                Recycle: itemRefs => RecycleItems(window, itemRefs),
                ShowFeedback: window.ShowDropFeedback,
                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId)),
                portalFolder: () => _portals.TryGetValue(window.FenceId, out var portal) ? portal.Current : null);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "fence {FenceId} cannot accept drops", window.FenceId); // degrade: everything else still works
        }
    }

    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool fromKeyboard)
    {
        // Shift+right-click is the extended menu; Shift+F10 is just the keyboard's normal menu (M3a review).
        var extended = !fromKeyboard && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs));
        switch (choice)
        {
            case ItemMenuChoice.Rename:
                window.BeginItemRename(itemRefs[0]); // the right-clicked item (it comes first)
                break;
            case ItemMenuChoice.Delete:
                RecycleItems(window, itemRefs); // always the Recycle Bin, even with Shift held (hard rule 1)
                break;
        }
    }

    /// <summary>
    /// Windows moves them to the Recycle Bin (with its own dialogs) on the shell worker, so a long recycle never freezes
    /// the fences (M3a review); the watcher then removes them from the fence.
    /// </summary>
    private void RecycleItems(FenceWindow window, IReadOnlyList<string> itemRefs)
    {
        Log.Information("recycling {Count} item(s)", itemRefs.Count);
        var ownerHandle = window.Handle;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var (started, refused, missing) = ShellFileOps.TryRecycle(LiveOwner(ownerHandle), itemRefs);
            if (!started) Log.Warning("recycle did not run or was cancelled: {ItemRefs}", itemRefs);
            if (missing.Count > 0) Log.Information("skipped {Count} item(s) that no longer exist: {ItemRefs}", missing.Count, missing);
            if (refused.Count > 0) dispatcher.BeginInvoke(() => ExplainRefusedRecycle(window, refused));
        });
    }

    /// <summary>No owner when the fence was deleted while the operation waited: Windows' dialogs then stand alone (M8c review).</summary>
    private static nint LiveOwner(nint ownerHandle) => FenceWindowChrome.IsLiveWindow(ownerHandle) ? ownerHandle : 0;

    private static void ExplainRefusedRecycle(FenceWindow window, IReadOnlyList<string> refused)
    {
        Log.Information("not deleting {Count} item(s) on drives without a Recycle Bin", refused.Count);
        System.Windows.MessageBox.Show(window,
            (refused.Count == 1 ? $"\"{Path.GetFileName(refused[0])}\" is" : $"{refused.Count} items are") +
            " on a drive without a Recycle Bin (a USB stick or network drive), so NeoFences won't delete it." +
            " Deleting there would be permanent; if you really mean it, delete it in Explorer.",
            "NeoFences", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>Windows renames the file (on the shell worker: its conflict dialogs); the watcher keeps it in its fence and position.</summary>
    private void RenameItem(FenceWindow window, string itemRef, string newName)
    {
        var ownerHandle = window.Handle;
        _shellWorker.Run(() =>
        {
            if (!ShellFileOps.TryRename(LiveOwner(ownerHandle), itemRef, newName)) Log.Warning("rename did not run or was cancelled: {ItemRef}", itemRef);
        });
    }

    private void AnswerTakeoverPrompt(bool hideIcons)
    {
        Log.Information("first-run question answered: hide desktop icons {HideIcons}", hideIcons);
        if (hideIcons) SetTakeover(true);
        else
        {
            _config = _config with { Settings = _config.Settings with { TakeoverPromptAnswered = true } };
            RefreshWindows();
            SaveNow();
        }
    }

    private void ApplyLayout()
    {
        if (_monitors.Count == 0)
        {
            Log.Warning("no monitors reported; keeping the current layout");
            return;
        }
        try
        {
            var (resolved, layout) = LayoutEngine.Resolve(_config, _monitors.Select(monitor => monitor.ToDisplayMonitor()).ToList());
            _config = resolved;
            foreach (var (fenceId, rect) in layout.Fences)
            {
                if (!_windows.TryGetValue(fenceId, out var window)) continue;
                var monitor = _monitors.First(candidate => candidate.DeviceId == rect.Monitor);
                window.Place(FencePlacement.ToPixels(rect, monitor));
                if (!window.IsVisible && Current.FencesVisible)
                {
                    window.ShowNow();
                    if (_peeking)
                    {
                        // A fence made during Peek (its menus no longer end it, M5 review I1) joins the others on top.
                        window.Peeking = true;
                        FenceWindowChrome.SetTopmost(window.Handle, topmost: true);
                    }
                    else FenceWindowChrome.SendToBack(window.Handle); // Show puts it above every app; fences live just above the desktop
                }
            }
        }
        catch (ArgumentException unusableDisplay)
        {
            // Garbage from a monitor query mid-change (M1 review): skip this resolve, the next display event retries.
            Log.Warning(unusableDisplay, "display query unusable; keeping the current layout");
        }
    }

    private void RefreshMonitors()
    {
        _monitors = Monitors.Enumerate();
        Log.Information("monitors: {Monitors}", string.Join("; ", _monitors.Select(monitor =>
            $"{monitor.DeviceId} {monitor.PixelWidth}x{monitor.PixelHeight}@{monitor.ScalePercent}% " +
            $"work {monitor.WorkLeftPx},{monitor.WorkTopPx} {monitor.WorkWidthPx}x{monitor.WorkHeightPx}{(monitor.IsPrimary ? " primary" : "")}")));
    }

    private void OnFenceMoved(FenceWindow window, PixelRect pixels)
    {
        var wasMove = ReferenceEquals(_movingWindow, window);
        _movingWindow = null;
        foreach (var other in _windows.Values) other.SetMergeHighlight(false);
        // Dropped by its title onto another fence's title row: the whole box joins that box as tabs (M9).
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        if (wasMove && _windows.Values.FirstOrDefault(other => other != window && other.TitleRowContains(cursorX, cursorY)) is { } target)
        {
            Log.Information("fence {FenceId} merged into the box of {TargetId}", window.BoxId, target.BoxId);
            _config = FenceTabs.Merge(_config, movingFenceId: window.BoxId, targetFenceId: target.BoxId);
            SyncBoxes();
            return;
        }
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: window.BoxId, rect: FencePlacement.FromPixels(pixels, monitor));
        ScheduleSave();
    }

    private PixelRect SnapFence(FenceWindow window, PixelRect rect, SnapEdges edges)
    {
        // While a box moves, the title row it would merge into lights up; a resize never merges (M9).
        _movingWindow = edges == SnapEdges.Move ? window : null;
        if (_movingWindow is not null)
        {
            var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
            foreach (var other in _windows.Values) other.SetMergeHighlight(other != window && other.TitleRowContains(cursorX, cursorY));
        }
        if (_monitors.Count == 0) return rect;
        var monitor = FencePlacement.ContainingMonitor(rect, _monitors);
        var others = _windows.Values.Where(other => other != window && other.IsVisible).Select(other => FenceWindowChrome.GetPixelRect(other.Handle)).ToList();
        return Snapping.Snap(rect, edges,
            workArea: new PixelRect(monitor.WorkLeftPx, monitor.WorkTopPx, monitor.WorkWidthPx, monitor.WorkHeightPx),
            others: others,
            gapPx: (int)Math.Round(SnapGapDips * monitor.Scale),
            thresholdPx: (int)Math.Round(SnapThresholdDips * monitor.Scale),
            // A resize snap never makes the fence smaller than its minimum (M2c review carry-over).
            minWidthPx: (int)Math.Round(LayoutEngine.MinWidth * monitor.Scale),
            minHeightPx: (int)Math.Round(LayoutEngine.MinHeight * monitor.Scale));
    }

    private void RenameFence(FenceWindow window, string title)
    {
        _config = FenceEdits.Rename(_config, window.FenceId, title);
        window.SetTitle(_config.Fences.First(fence => fence.Id == window.FenceId).Title);
        RefreshTabs(window);
        RefreshPortal(window); // a Portal browsing a subfolder shows its breadcrumb again
        ScheduleSave();
    }

    private void SetFenceIconSize(FenceWindow window, int iconSize)
    {
        _config = FenceEdits.SetIconSize(_config, window.FenceId, iconSize);
        window.SetIconSize(iconSize);
        ScheduleSave();
    }

    private void SetFenceLocked(FenceWindow window, bool locked)
    {
        _config = FenceEdits.SetLocked(_config, window.BoxId, locked); // the box's (M9)
        window.SetLocked(locked);
        ScheduleSave();
    }

    /// <summary>Removes the fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
    private void DeleteFence(FenceWindow window)
    {
        _config = FenceMembership.DeleteFence(_config, window.FenceId); // the shown tab; the rest of its box stays (M9)
        SyncBoxes(); // closes the window when its box is gone; a deleted Portal's watcher ends (the folder is never touched)
    }

    private void OnThemeChanged()
    {
        var light = SystemTheme.AppsUseLightTheme();
        if (light == _lightTheme) return;
        _lightTheme = light;
        Log.Information("Windows app mode changed; light: {Light}", light);
        foreach (var window in _windows.Values) window.ApplyTheme(light);
    }

    private void OnDisplayChanged()
    {
        RefreshMonitors();
        ApplyLayout();
        ScheduleSave();
    }

    /// <summary>"New Portal fence…": Windows' folder dialog, then a fence that mirrors the folder (M4).</summary>
    private void CreatePortal(FenceWindow owner)
    {
        var folder = FolderPicker.TryPick(owner.Handle, "Choose the folder for the new Portal fence",
            logFailure: failure => Log.Warning(failure, "folder dialog failed"));
        if (folder is null) return;
        var title = Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } name ? name : folder;
        (_config, _) = FenceMembership.CreatePortal(_config, title: title, folderPath: folder);
        Log.Information("Portal fence created for {Folder}", folder);
        SyncBoxes();
    }

    /// <summary>
    /// Brings the windows in line with the boxes after any change to them (M9): a window per box, showing the box's
    /// active tab, with its tab strip, lock and roll-up. A box that changed hands (its host left) keeps its window.
    /// </summary>
    private void SyncBoxes()
    {
        var boxes = FenceTabs.Boxes(_config);
        var boxIds = boxes.Select(box => box.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (boxId, window) in _windows.ToList())
        {
            if (boxIds.Contains(boxId)) continue;
            _windows.Remove(boxId);
            if (_config.Fences.Any(fence => fence.Id == window.FenceId) && FenceTabs.HostOf(_config, window.FenceId) is { } heir
                && boxIds.Contains(heir.Id) && !_windows.ContainsKey(heir.Id))
            {
                window.BoxId = heir.Id;
                _windows[heir.Id] = window;
                if (_dropRegistrations.Remove(boxId, out var moved)) _dropRegistrations[heir.Id] = moved;
                continue;
            }
            if (_dropRegistrations.Remove(boxId, out var registration)) registration.Dispose();
            window.Close();
        }
        foreach (var box in boxes.Where(box => !_windows.ContainsKey(box.Id))) OpenWindow(box);
        EnsurePortals();
        foreach (var box in boxes)
        {
            var window = _windows[box.Id];
            var active = FenceTabs.ActiveOf(_config, box.Id);
            if (window.FenceId != active.Id)
            {
                window.ShowTab(active);
                if (_portals.TryGetValue(active.Id, out var portal)) portal.Refresh();
            }
            window.SetTabs(FenceTabs.TabsOf(_config, box.Id), active.Id);
            window.SetLocked(box.Locked);
            window.SetRolledUp(box.RolledUp);
        }
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
    }

    /// <summary>A watcher per Portal fence, shown or not (a hidden Portal tab keeps watching, M9); gone ones end.</summary>
    private void EnsurePortals()
    {
        var portalFences = _config.Fences.Where(fence => fence.Source is { Kind: FenceSourceKind.Portal, Path: not null }).ToList();
        foreach (var goneId in _portals.Keys.Where(fenceId => portalFences.All(fence => fence.Id != fenceId)).ToList())
        {
            _portals[goneId].Dispose(); // the folder itself is never touched
            _portals.Remove(goneId);
        }
        foreach (var fence in portalFences.Where(fence => !_portals.ContainsKey(fence.Id)))
        {
            var fenceId = fence.Id;
            _portals[fenceId] = new PortalState(fence.Source.Path!, noticeOwner: _messages.Handle, show: items => ShowPortalTab(fenceId, items),
                logFailure: failure => Log.Warning(failure, "cannot watch Portal folder of {FenceId}", fenceId));
            if (_gameMode) _portals[fenceId].SetPaused(true);
        }
    }

    /// <summary>A Portal's listing goes to the window showing it; a hidden Portal tab re-lists when shown.</summary>
    private void ShowPortalTab(string fenceId, IReadOnlyList<ItemInfo>? items)
    {
        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } window) ShowPortal(window, items);
    }

    private void RefreshTabs(FenceWindow window) => window.SetTabs(FenceTabs.TabsOf(_config, window.BoxId), window.FenceId);

    /// <summary>A header clicked (or hovered during a drop): that tab shows, now and after a restart.</summary>
    private void SwitchTab(FenceWindow window, string fenceId)
    {
        if (window.FenceId == fenceId || _config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { } tab) return;
        _config = FenceTabs.SetActive(_config, fenceId);
        window.ShowTab(tab);
        RefreshTabs(window);
        if (_portals.TryGetValue(fenceId, out var portal)) portal.Refresh();
        RefreshWindows();
        ScheduleSave();
    }

    private void CycleTab(FenceWindow window, int step)
    {
        var tabs = FenceTabs.TabsOf(_config, window.BoxId);
        var index = tabs.ToList().FindIndex(tab => tab.Id == window.FenceId);
        SwitchTab(window, tabs[((index + step) % tabs.Count + tabs.Count) % tabs.Count].Id);
    }

    /// <summary>A tab header released after a drag: on its own strip = reorder; on another title row = merge; elsewhere = detach.</summary>
    private void OnTabDropped(FenceWindow window, string fenceId, int screenX, int screenY)
    {
        var target = _windows.Values.FirstOrDefault(candidate => candidate.TitleRowContains(screenX, screenY));
        if (target == window)
        {
            var slot = window.TabIndexAt(screenX);
            var current = FenceTabs.TabsOf(_config, window.BoxId).ToList().FindIndex(tab => tab.Id == fenceId);
            _config = FenceTabs.Reorder(_config, fenceId, slot > current ? slot - 1 : slot);
        }
        else if (target is not null)
        {
            Log.Information("tab {FenceId} moved into the box of {TargetId}", fenceId, target.BoxId);
            _config = FenceTabs.Merge(_config, movingFenceId: fenceId, targetFenceId: target.BoxId, insertAt: target.TabIndexAt(screenX));
        }
        else
        {
            DetachTab(window, fenceId, dropPoint: (screenX, screenY));
            return;
        }
        SyncBoxes();
    }

    /// <summary>A tab leaves its box: at the drop point (dragged out) or just below the box (menu), with the box's size.</summary>
    private void DetachTab(FenceWindow window, string fenceId, (int X, int Y)? dropPoint)
    {
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint
            || !_config.Layouts.TryGetValue(fingerprint, out var layout) || !layout.Fences.TryGetValue(window.BoxId, out var boxRect)) return;
        var boxMonitor = _monitors.FirstOrDefault(monitor => monitor.DeviceId == boxRect.Monitor) ?? _monitors[0];
        var box = FencePlacement.ToPixels(boxRect, boxMonitor);
        var offset = (int)Math.Round(40 * boxMonitor.Scale);
        var placed = dropPoint is { } point
            ? box with { X = point.X - offset, Y = point.Y - offset / 3 } // the pointer near its title
            : box with { X = box.X + offset, Y = box.Y + offset };
        var monitor = FencePlacement.ContainingMonitor(placed, _monitors);
        Log.Information("tab {FenceId} detached from the box of {BoxId}", fenceId, window.BoxId);
        _config = FenceTabs.Detach(_config, fenceId, fingerprint, FencePlacement.FromPixels(placed, monitor));
        SyncBoxes();
    }

    /// <summary>Asks a Portal to re-list its folder (in the background; ShowPortal follows).</summary>
    private void RefreshPortal(FenceWindow window)
    {
        if (_portals.TryGetValue(window.FenceId, out var portal)) portal.Refresh();
    }

    /// <summary>Shows a Portal's listing (null: the folder cannot be read) in its sort order (M4).</summary>
    private void ShowPortal(FenceWindow window, IReadOnlyList<ItemInfo>? items)
    {
        if (!_portals.TryGetValue(window.FenceId, out var portal)) return;
        if (_config.Fences.FirstOrDefault(candidate => candidate.Id == window.FenceId) is not { } fence) return;
        window.SetPortalLocation(portal.Breadcrumb(fence.Title), portal.CanGoBack);
        window.SetSortChecked(fence.Sort);
        window.ShowPortalMessage(items is null ? $"This folder is not available right now:\n{portal.Current}" : null);
        window.SetItems(items is null ? [] : ItemSorting.Order(items, fence.Sort));
    }

    /// <summary>Double-click / Enter: inside a Portal a folder is browsed in place (Ctrl opens it in Explorer, user choice 2026-10-03).</summary>
    private void OpenOrBrowse(FenceWindow window, string itemRef)
    {
        var inExplorer = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        if (_portals.TryGetValue(window.FenceId, out var portal) && !inExplorer && portal.IsListedFolder(itemRef))
        {
            portal.Browse(itemRef); // re-lists in the background
            return;
        }
        OpenItem(itemRef, ownerHandle: window.Handle);
        SetPeek(false); // like Fences: Peek ends once something is opened from it
    }

    private void BrowsePortal(FenceWindow window, bool back)
    {
        if (!back || !_portals.TryGetValue(window.FenceId, out var portal) || !portal.CanGoBack) return;
        portal.Back(); // re-lists in the background
    }

    /// <summary>"Sort by": a Portal keeps the order live; a desktop fence is sorted once (M4).</summary>
    private void SortFence(FenceWindow window, FenceSort sort)
    {
        var fence = _config.Fences.First(candidate => candidate.Id == window.FenceId);
        if (_portals.ContainsKey(fence.Id))
        {
            _config = FenceEdits.SetSort(_config, fence.Id, sort);
            RefreshPortal(window);
        }
        else
        {
            try
            {
                _config = FenceEdits.SetItemOrder(_config, fence.Id, ItemSorting.Order(FolderItems.Describe(fence.Items), sort));
            }
            catch (ArgumentException mismatch)
            {
                Log.Warning(mismatch, "sort of fence {FenceId} refused: the sorted list did not match its items", fence.Id); // never crash (M4 review I3)
                return;
            }
            RefreshWindows();
        }
        ScheduleSave();
    }

    private void ApplyStartup() =>
        StartupRegistration.Apply(_config.Settings.StartWithWindows, Environment.ProcessPath ?? "", log: message => Log.Information("{Message}", message));

    private void SetStartWithWindows(bool startWithWindows)
    {
        _config = _config with { Settings = _config.Settings with { StartWithWindows = startWithWindows } };
        ApplyStartup();
        foreach (var window in _windows.Values) window.SetStartupChecked(startWithWindows);
        SaveNow();
        RefreshSettings();
    }

    private void CreateFence()
    {
        (_config, _) = FenceMembership.CreateFence(_config, "New fence");
        SyncBoxes();
    }

    private void SetTakeover(bool active)
    {
        SetQuickHidden(false); // an explicit icons choice ends quick-hide first, so the two never disagree
        _takeoverActive = active;
        // Any explicit choice (banner or menu) answers the first-run question.
        _config = _config with { Settings = _config.Settings with { Takeover = active, TakeoverPromptAnswered = true } };
        SetIconsHidden(Current.IconsHidden);
        foreach (var window in _windows.Values) window.SetTakeoverChecked(active);
        RefreshSettings();
        RefreshWindows();
        SaveNow(); // not debounced: the saved setting must match the takeover-active marker if we are killed next
    }

    /// <returns>True when Windows confirmed the new state.</returns>
    private bool SetIconsHidden(bool hidden)
    {
        // The watchdog must know whenever icons may be hidden: mark before hiding, unmark only after a confirmed show.
        if (hidden) TryMarker(() => _watchdog.SetTakeoverActive(true), what: "takeover-active marker");
        var applied = DesktopIcons.TrySetHidden(hidden);
        if (applied && !hidden) TryMarker(() => _watchdog.SetTakeoverActive(false), what: "takeover-active marker");
        if (applied) Log.Information("desktop icons hidden: {Hidden}", hidden);
        else Log.Warning("could not set desktop icons hidden: {Hidden}", hidden);
        return applied;
    }

    /// <summary>Icons as they should be: hidden while Takeover is on, shown (and unmarked) otherwise.</summary>
    private bool EnsureIconState() =>
        Current.IconsHidden ? SetIconsHidden(true)
        : !_watchdog.IsTakeoverActiveMarked || SetIconsHidden(false);

    private static void TryMarker(Action writeMarker, string what)
    {
        try
        {
            writeMarker();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "could not update the {What}", what);
        }
    }

    private void OnExplorerRestarted()
    {
        Log.Information("Explorer restarted; re-attaching fences");
        SetPeek(false); // re-attaching sends fences to the bottom; Peek (and its global Esc) must end with it (M5 review M2)
        ShowTrayIcon(); // Explorer forgot every tray icon
        ReinstallMouseHook(); // a hook Windows dropped silently comes back here at the latest (M5 review)
        StartSpecialIconNotifications();
        ScheduleSpecialIconRefresh(); // the Recycle Bin may have changed meanwhile
        var attempts = 0;
        var retryTimer = new DispatcherTimer { Interval = ReattachInterval };
        retryTimer.Tick += (_, _) =>
        {
            attempts++;
            var unattachedCount = _windows.Values.Count(window => !DesktopHost.AttachToDesktop(window.Handle));
            foreach (var window in _windows.Values) FenceWindowChrome.SendToBack(window.Handle);
            var iconsOk = EnsureIconState();
            if ((unattachedCount == 0 && iconsOk) || attempts >= ReattachAttempts)
            {
                retryTimer.Stop();
                Log.Information("re-attach after Explorer restart: {Attempts} attempt(s), unattached {UnattachedCount}, icons ok {IconsOk}",
                    attempts, unattachedCount, iconsOk);
            }
        };
        retryTimer.Start();
    }

    /// <summary>The WH_MOUSE_LL desktop gestures and the Peek hotkey (M5). Either failing only turns that feature off.</summary>
    private void StartGestures()
    {
        UpdateMouseHook();
        UpdatePeekHotkey();
    }

    /// <summary>The Peek hotkey is registered exactly while wanted: released to Windows and the game while paused or gaming.</summary>
    private void UpdatePeekHotkey()
    {
        // Released while Settings records a new one, so pressing the current combination is recorded, not Peek (M6b review).
        if ((Current.PeekHotkeyWanted && !_recordingHotkey) == _peekHotkey is not null) return;
        if (_peekHotkey is not null)
        {
            _peekHotkey.Dispose();
            _peekHotkey = null;
            return;
        }
        _peekHotkey = TryRegisterPeekHotkey(_config.Settings.PeekHotkey, out var problem);
        _peekHotkeyProblem = _peekHotkey is null ? problem : null;
        if (_peekHotkey is null) Log.Warning("Peek is off: {Problem}", problem);
    }

    private GlobalHotkey? TryRegisterPeekHotkey(string text, out string problem)
    {
        if (!Hotkey.TryParse(text, out var hotkey) || !Enum.TryParse<System.Windows.Input.Key>(hotkey.Key, ignoreCase: true, out var key))
        {
            problem = $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.";
            return null;
        }
        var virtualKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            problem = $"{hotkey.DisplayText} has no key Windows can watch for. Pick another key."; // would say "Saved" and never fire (M6b review M4)
            return null;
        }
        var registration = new GlobalHotkey(_messages.Handle, PeekHotkeyId);
        if (registration.TryRegister(hotkey, (uint)virtualKey))
        {
            problem = "";
            Log.Information("Peek hotkey {Hotkey} registered", hotkey);
            return registration;
        }
        registration.Dispose();
        problem = $"{hotkey.DisplayText} is already taken by Windows or another app. Pick another combination.";
        return null;
    }

    /// <summary>Settings: a new Peek hotkey. It is kept only if Windows accepts it; otherwise the old one stays.</summary>
    private (bool Saved, string Message) SetPeekHotkey(string text)
    {
        if (!Hotkey.TryParse(text, out var parsed)) return (false, $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.");
        // A global hotkey swallows its keys in every app (M6b review I1): only safe combinations are recorded.
        if (!parsed.IsSafeToRecord) return (false, $"{parsed.DisplayText} would stop working everywhere else (typing, Tab, closing windows). Use Alt or Win with a key, or an F-key.");
        var normalized = parsed.ToString();
        // The same combination again is a retry when it is not registered (Settings showed it as not active, M6b review).
        if (normalized == _config.Settings.PeekHotkey && _peekHotkeyProblem is null) return (true, $"Peek: {parsed.DisplayText}");
        _peekHotkey?.Dispose();
        _peekHotkey = null;
        var registration = TryRegisterPeekHotkey(normalized, out var problem);
        if (registration is null)
        {
            UpdatePeekHotkey(); // the old one again
            return (false, problem);
        }
        registration.Dispose(); // proven free; UpdatePeekHotkey registers it whenever it is wanted
        _peekHotkeyProblem = null;
        _config = _config with { Settings = _config.Settings with { PeekHotkey = normalized } };
        UpdatePeekHotkey();
        SaveNow();
        return (true, $"Saved. Peek: {parsed.DisplayText}");
    }

    /// <summary>Tray or fence menu → Settings… (M6b). One window; a second request brings it to the front.</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var window = new SettingsWindow();
        window.StartWithWindowsChanged += SetStartWithWindows;
        window.TakeoverChanged += SetTakeover;
        window.PeekHotkeyChosen += text =>
        {
            var (saved, message) = SetPeekHotkey(text);
            RefreshSettings();
            window.ShowHotkeyResult(saved, message);
        };
        window.RollupExpandChanged += SetRollupExpand;
        window.HotkeyRecording += recording =>
        {
            _recordingHotkey = recording;
            UpdatePeekHotkey();
            // Leaving the box re-registers: Settings must show if that failed (final review I5).
            if (!recording) RefreshSettings();
        };
        window.DefaultLabelsChanged += labels =>
        {
            _config = _config with { Settings = _config.Settings with { DefaultLabels = labels } };
            SaveNow();
        };
        window.LabelsAppliedToAll += labels =>
        {
            _config = FenceEdits.SetLabelsEverywhere(_config, labels);
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetLabelMode(labels);
            Log.Information("labels for every fence: {Labels}", labels);
            SaveNow();
            RefreshSettings();
        };
        window.ShortcutArrowsChanged += show =>
        {
            _config = _config with { Settings = _config.Settings with { ShowShortcutArrows = show } };
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetShortcutArrows(show);
            SaveNow();
        };
        window.GameModeChanged += SetGameModeEnabled;
        window.OpenLogsRequested += () => OpenItem(AppPaths.LogsDirectory, ownerHandle: 0);
        window.OpenDataRequested += () => OpenItem(AppPaths.DataDirectory, ownerHandle: 0);
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _recordingHotkey = false;
            UpdatePeekHotkey();
        };
        _settingsWindow = window;
        RefreshSettings();
        window.Show();
        window.Activate();
    }

    private void RefreshSettings() => _settingsWindow?.Show(new SettingsView(
        StartWithWindows: _config.Settings.StartWithWindows,
        Takeover: _takeoverActive,
        PeekHotkey: PeekHotkeyDisplay,
        PeekHotkeyActive: _peekHotkeyProblem is null,
        RollupExpand: _config.Settings.RollupExpand,
        GameModeEnabled: _config.Settings.GameMode,
        GameModeActive: _gameMode,
        Version: typeof(FenceHost).Assembly.GetName().Version?.ToString(3) ?? "",
        DataFolder: AppPaths.DataDirectory,
        DefaultLabels: _config.Settings.DefaultLabels,
        ShowShortcutArrows: _config.Settings.ShowShortcutArrows));

    /// <summary>The Peek hotkey as a person reads it ("Ctrl+Shift+=", not "Ctrl+Shift+OemPlus").</summary>
    private string PeekHotkeyDisplay =>
        Hotkey.TryParse(_config.Settings.PeekHotkey, out var hotkey) ? hotkey.DisplayText : _config.Settings.PeekHotkey;

    /// <summary>Fence menu → Labels (M8b).</summary>
    private void SetFenceLabels(FenceWindow window, LabelMode labels)
    {
        _config = FenceEdits.SetLabels(_config, window.FenceId, labels);
        window.SetLabelMode(labels);
        ScheduleSave();
    }

    private void SetRollupExpand(RollupExpand mode)
    {
        _config = _config with { Settings = _config.Settings with { RollupExpand = mode } };
        foreach (var window in _windows.Values) window.SetRollupExpand(mode);
        Log.Information("roll-up expands on: {Mode}", mode);
        SaveNow();
    }

    private void SetGameModeEnabled(bool enabled)
    {
        _config = _config with { Settings = _config.Settings with { GameMode = enabled } };
        Log.Information("game mode enabled: {Enabled}", enabled);
        SaveNow();
        CheckGameMode(); // turning it off in a game ends idle at once
        RefreshSettings();
    }

    /// <summary>The hook exists exactly while it is wanted: not while paused or gaming (hard rule 3, spec §4.7).</summary>
    private void UpdateMouseHook()
    {
        if (Current.MouseHookWanted == _mouseHook is not null) return;
        if (_mouseHook is not null)
        {
            EndDrawOverlay();
            _mouseHook.Dispose();
            _mouseHook = null;
            return;
        }
        var dispatcher = Dispatcher.CurrentDispatcher;
        _mouseHook = new DesktopMouseHook(
            onGesture: (gesture, screenX, screenY) => dispatcher.BeginInvoke(() => OnDesktopGesture(gesture, screenX, screenY)),
            onPeekClickOutside: () => dispatcher.BeginInvoke(() => SetPeek(false)),
            log: message => Log.Information("{Message}", message));
    }

    /// <summary>Windows drops a low-level hook silently (LowLevelHooksTimeout): a fresh one after Explorer restarts and on unlock.</summary>
    private void ReinstallMouseHook()
    {
        if (_mouseHook is null) return; // not wanted now; it comes back when it is
        EndDrawOverlay();
        _mouseHook.Dispose();
        _mouseHook = null;
        UpdateMouseHook();
    }

    private void OnSessionUnlocked()
    {
        Log.Information("session unlocked; re-installing the mouse hook");
        ReinstallMouseHook();
        CheckGameMode();
    }

    /// <summary>Game mode (spec §4.7, ADR-021): checked on every foreground change, and again shortly after (the signal lags).</summary>
    private void StartGameMode()
    {
        // Backstop for a game that goes full screen after the last re-check (M6a review I1).
        var poll = new DispatcherTimer { Interval = GameModePolicy.PollInterval };
        poll.Tick += (_, _) => CheckGameMode();
        poll.Start();
        _foregroundWatcher = new ForegroundWatcher(onForegroundChanged: OnForegroundChanged,
            logFailure: failure => Log.Error(failure, "game mode check failed"));
        if (!_foregroundWatcher.IsWatching) Log.Warning("game mode: foreground changes cannot be watched; game mode is off");
        CheckGameMode();
    }

    private void OnForegroundChanged()
    {
        CheckGameMode();
        foreach (var delay in GameModePolicy.RecheckDelays)
        {
            // ponytail: one short-lived timer per foreground change and delay; a coalescing timer if switching storms ever show up in a profile.
            var recheck = new DispatcherTimer { Interval = delay };
            recheck.Tick += (_, _) =>
            {
                recheck.Stop();
                CheckGameMode();
            };
            recheck.Start();
        }
    }

    private void CheckGameMode()
    {
        var gameMode = GameModePolicy.IsGameActive(enabled: _config.Settings.GameMode, foreground: GameDetection.TakeSnapshot());
        if (gameMode == _gameMode) return;
        _gameMode = gameMode;
        Log.Information("game mode: {GameMode}", gameMode);
        if (gameMode) SetPeek(false);
        UpdateMouseHook();
        foreach (var portal in _portals.Values) portal.SetPaused(gameMode);
        if (!gameMode) ApplyDeferredShellWork();
        UpdatePeekHotkey();
        _trayIcon?.SetTooltip(TrayTooltip());
        RefreshSettings();
    }

    /// <summary>The game was left: Desktop changes made meanwhile apply in order (or one reconcile if events were lost).</summary>
    private void ApplyDeferredShellWork()
    {
        if (_specialIconsDeferred)
        {
            _specialIconsDeferred = false;
            ScheduleSpecialIconRefresh();
        }
        if (_reconcileDeferred)
        {
            _reconcileDeferred = false;
            _deferredDesktopChanges.Clear();
            ReconcileDesktop();
            return;
        }
        if (_deferredDesktopChanges.Count == 0) return;
        Log.Information("applying {Count} desktop change(s) from game mode", _deferredDesktopChanges.Count);
        foreach (var change in _deferredDesktopChanges)
            (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
        _deferredDesktopChanges.Clear();
        RefreshWindows();
        ScheduleSave();
    }

    /// <summary>Pause (tray): the desktop goes back to Windows — fences hidden, icons shown, the hook gone — until resumed.</summary>
    private void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        if (paused)
        {
            SetPeek(false);
            EndDrawOverlay();
        }
        _paused = paused;
        if (paused)
        {
            _quickHidden = false; // resuming shows everything
            _iconsHiddenByUser = false;
        }
        foreach (var window in _windows.Values)
        {
            if (!Current.FencesVisible) window.HideNow();
            else if (!window.IsVisible)
            {
                window.ShowNow();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        EnsureIconState(); // also shows icons left hidden by an earlier failed show: Pause "restores icons" (spec §6, M6a review M1)
        UpdateMouseHook();
        UpdatePeekHotkey();
        RefreshSettings(); // a hotkey that cannot be registered on resume shows in an open Settings (final review I5)
        _trayIcon?.SetTooltip(TrayTooltip());
        Log.Information("paused: {Paused}", paused);
    }

    private string TrayTooltip() =>
        _paused ? "NeoFences — paused" : _gameMode ? "NeoFences — idle while a game runs" : "NeoFences";

    /// <summary>Tray menu (spec §6; user choice 2026-10-03: left-click opens it too).</summary>
    private void ShowTrayMenu(int screenX, int screenY)
    {
        var chosen = TrayMenu.Show(_messages.Handle,
        [
            new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
            new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
            new TrayMenuItem(TrayPeek, $"Peek\t{PeekHotkeyDisplay}", Checked: _peeking, Enabled: !_paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TraySettings, "Settings…"),
            new TrayMenuItem(TrayPause, "Pause NeoFences", Checked: _paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayExit, "Exit NeoFences"),
        ], screenX, screenY);
        switch (chosen)
        {
            case TrayNewFence:
                SetQuickHidden(false); // a new fence must be visible (M6a review I3)
                CreateFence();
                break;
            case TrayQuickHide: SetQuickHidden(!_quickHidden); break;
            case TrayPeek: SetPeek(!_peeking); break;
            case TrayPause: SetPaused(!_paused); break;
            case TraySettings: OpenSettings(); break;
            case TrayExit: ExitRequested?.Invoke(); break;
        }
    }

    private void OnDesktopGesture(DesktopGesture gesture, int screenX, int screenY)
    {
        switch (gesture)
        {
            // A double-click on a visible native icon opens it; only empty desktop toggles quick-hide.
            case DesktopGesture.DoubleClick when Current.IconsHidden
                || !DesktopWindows.IsOverDesktopIcon(screenX, screenY, log: message => Log.Warning("{Message}", message)):
                SetQuickHidden(!_quickHidden);
                break;
            case DesktopGesture.RightDragStarted:
                BeginDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCompleted:
                EndDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCancelled:
                EndDrawOverlay(); // the drag's right-up was never seen (M5 review I2)
                break;
        }
    }

    /// <summary>Quick-hide (M5, user choice): fences and the native desktop icons go away together and come back together.</summary>
    private void SetQuickHidden(bool hidden)
    {
        if (hidden == _quickHidden) return;
        if (hidden) SetPeek(false);
        // Icons the user had hidden through Explorer stay theirs: quick-hide neither hides nor later shows them (M8a).
        // Hidden now without the takeover-active marker: the user hid them in Explorer, so they stay the user's. With the marker
        // set, NeoFences hid them (an earlier show failed) and the end of this quick-hide retries the show (M8a review I1).
        if (hidden && !_takeoverActive) _iconsHiddenByUser = DesktopIcons.TryIsHidden() == true && !_watchdog.IsTakeoverActiveMarked;
        var iconsWereHidden = Current.IconsHidden;
        _quickHidden = hidden;
        foreach (var window in _windows.Values)
        {
            if (hidden) window.HideFaded(); // 150 ms fade (spec §6)
            else
            {
                window.ShowFaded();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        if (Current.IconsHidden != iconsWereHidden) SetIconsHidden(Current.IconsHidden); // RunState decides, user-hidden icons included
        if (!hidden) _iconsHiddenByUser = false;
        Log.Information("quick-hide: {Hidden}", hidden);
    }

    private void BeginDrawFence(int startX, int startY)
    {
        if (_mouseHook is not { } mouseHook) return;
        EndDrawOverlay();
        _drawStart = (startX, startY);
        var overlay = new DrawFenceOverlay(_lightTheme);
        overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        overlay.Show();
        _drawOverlay = overlay;
        _drawTimer = new DispatcherTimer { Interval = DrawFrame };
        // A lost right-up (Win+L, UAC, an elevated window) is ended by the next click (RightDragCancelled). The button
        // state cannot tell: the swallowed right-press never reaches Windows' key state (M5 review I2, smoke finding).
        _drawTimer.Tick += (_, _) => overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        _drawTimer.Start();
    }

    /// <summary>The right button came up: a new fence where the rectangle was, its title ready to type.</summary>
    private void EndDrawFence(int endX, int endY)
    {
        if (!EndDrawOverlay()) return; // the start was never seen
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        SetQuickHidden(false);
        var pixels = DrawFenceOverlay.Between(_drawStart, (endX, endY));
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        (_config, var fence) = FenceMembership.CreateFence(_config, "New fence");
        // Too small a drag still makes a usable fence: the layout clamps it to the minimum size.
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: fence.Id, rect: FencePlacement.FromPixels(pixels, monitor));
        Log.Information("fence drawn on the desktop at {Pixels}", pixels);
        OpenWindow(fence);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
        if (_windows.TryGetValue(fence.Id, out var window)) window.BeginRename();
    }

    /// <returns>True when an overlay was showing.</returns>
    private bool EndDrawOverlay()
    {
        _drawTimer?.Stop();
        _drawTimer = null;
        if (_drawOverlay is null) return false;
        _drawOverlay.Close();
        _drawOverlay = null;
        return true;
    }

    private void OnHotkey(int hotkeyId)
    {
        CheckGameMode(); // fresh: a game may have gone full screen since the last check (M6a review I1)
        // Paused: the desktop belongs to Windows. Gaming: fences must not rise over the game, and the click-outside hook is off.
        if (_paused || _gameMode) return;
        if (hotkeyId == PeekHotkeyId) SetPeek(!_peeking);
        else if (hotkeyId == PeekEscapeHotkeyId) SetPeek(false);
    }

    /// <summary>Peek (M5): every fence above all windows until the hotkey again, Esc, a click outside, or an item opens.</summary>
    private void SetPeek(bool peeking)
    {
        if (peeking == _peeking) return;
        if (peeking) SetQuickHidden(false);
        _peeking = peeking;
        if (_mouseHook is not null) _mouseHook.PeekActive = peeking;
        // Every fence first: raising one restacks its siblings (all owned by Progman), and a sibling still keeping
        // itself at the bottom would drop back (M5 smoke: only one fence rose).
        foreach (var window in _windows.Values) window.Peeking = peeking;
        foreach (var window in _windows.Values) FenceWindowChrome.SetTopmost(window.Handle, peeking);
        _peekEscapeHotkey?.Dispose();
        _peekEscapeHotkey = null;
        if (peeking)
        {
            _peekEscapeHotkey = new GlobalHotkey(_messages.Handle, PeekEscapeHotkeyId);
            if (!_peekEscapeHotkey.TryRegister(new Hotkey(Ctrl: false, Alt: false, Shift: false, Win: false, Key: "Escape"), virtualKey: 0x1B))
                Log.Warning("Esc is taken by another app; Peek ends with its hotkey or a click outside");
        }
        Log.Information("peek: {Peeking}", peeking);
    }

    /// <summary>Title double-click (M5): rolled up to its title bar, or back. Stored, so it survives a restart.</summary>
    private void ToggleRollUp(FenceWindow window)
    {
        var rolledUp = !_config.Fences.First(fence => fence.Id == window.BoxId).RolledUp; // the box's (M9)
        _config = FenceEdits.SetRolledUp(_config, window.BoxId, rolledUp);
        window.SetRolledUp(rolledUp);
        ScheduleSave();
    }

    /// <summary>Adds the tray icon, retrying while Explorer is still busy (sign-in autostart, Explorer restart).</summary>
    private void ShowTrayIcon()
    {
        if (_trayIcon is not { } trayIcon || trayIcon.Show()) return;
        var attempts = 0;
        var retry = new DispatcherTimer { Interval = TrayRetryInterval };
        retry.Tick += (_, _) =>
        {
            if (trayIcon.Show() || ++attempts >= TrayRetryAttempts)
            {
                retry.Stop();
                Log.Information("tray icon retry finished after {Attempts} attempt(s)", attempts + 1);
            }
        };
        retry.Start();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            if (!_store.Save(_config)) Log.Warning("config not saved: config.json is read-only this session");
            else if (_store.LastBackupFailure is { } backupFailure) Log.Warning(backupFailure, "config saved, but the daily backups could not be written or pruned");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "config save failed");
        }
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 296`.

- [ ] **Step 3: Live checks (ask first; print TEST RUNNING / TEST COMPLETE; restore the installed copy)**

Run `m9-smoke.ps1 -Exe <branch exe>` with Windows PowerShell (session scratchpad; it dot-sources `m8b-helpers.ps1` and reads fence positions from the config), then `m8b-smoke.ps1 -Exe <branch exe>` for the regressions.
Expected: as in `docs/research/m9-fence-tabs.md`; the M8b lines as before.

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.App
git commit -m "feat: added fence tabs with a tab strip, merging by title drag, switching, colours, reordering and dragging tabs out"
```

---

### Task 4: Verification and docs

- [ ] **Step 1: Append section V to `docs/TEST-CHECKLIST.md`**

```markdown

## V — Fence tabs (M9, ADR-029)
| ID | Steps | Expected |
|---|---|---|
| V1 | Drag a fence by its title onto another fence's title | the target lights up while hovering; on release one box with two tabs; the moved fence's tab is shown |
| V2 | Click each header; Ctrl+Tab / Ctrl+Shift+Tab with the box focused | the tab's items, icon size, labels and sort show |
| V3 | Double-click a header | rename that tab in place |
| V4 | Right-click a header → Colour → a colour; then None | a coloured bar under that header; gone again |
| V5 | Drag a header along the strip | the tabs reorder |
| V6 | Drag a header onto another box's or fence's title | it moves into that box at that spot |
| V7 | Drag a header out onto the desktop; also right-click → Detach tab | a separate fence at the drop point (menu: just below the box) |
| V8 | Detach the first (host) tab | the box stays where it was with the other tabs, still rolled up / locked as before |
| V9 | Drag items from Explorer or another fence onto a header | that tab shows and the items land in it (a Portal tab copies/moves into its folder) |
| V10 | Restart NeoFences (and **[USER]** a power cut) | boxes, tab order, the shown tab and colours come back |
| V11 | Roll up, lock, Peek, quick-hide, game mode with a box | as for a single fence |
| V12 | Delete a tab (fence menu → Delete fence) | its items go to the Inbox; the box keeps the other tabs |
| V13 | A Portal tab and a desktop tab in one box; switch while files change in the Portal's folder | the Portal shows its current files when switched to |
| V14 | Resize a fence and release with the pointer over another fence's title | no merge |
```

- [ ] **Step 2: Append ADR-029 to `docs/DECISIONS.md`**

```markdown

## ADR-029 — Fence tabs as boxes over ordinary fences (M9, v1.2)
**Date:** 2026-10-03 · **Status:** Accepted · **Spec:** `superpowers/specs/2026-10-03-fence-tabs-design.md`

**Context.** The first v2 feature the user picked: Fences-6-style tabs to combine fences into one box (user choices:
combine existing fences by dragging a title onto another; drag a tab out to split it; a per-tab accent colour).

**Decision.**
- **Every tab stays an ordinary `Fence`** (items, source, sort, icon size, labels): membership, reconcile, Portals and
  their tests are untouched. A box is a host fence whose `Tabs` lists two or more ids (itself included); the host owns
  the window, the placements, roll-up and lock. `ActiveTab` and `TabColor` complete the model. All edits live in Core
  `FenceTabs` (Merge, Detach, Leave, Reorder, SetActive, SetColor, Repair), tested first; `FenceMembership.DeleteFence`
  leaves the box first, and the layout engine places boxes only.
- **One window per box.** `FenceWindow.FenceId` is the shown tab (items, icon size, labels, sort, rename, delete, drops
  act on it); `BoxId` is the host (placement, move, snap, roll-up, lock). `FenceHost.SyncBoxes` brings windows in line
  after any change and keeps a window when its box changes hands (the host left). Portal watchers exist per Portal fence,
  shown or not.
- **Gestures.** Header click switches; double-click renames; a header drag (NeoFences' own capture with a click-through
  ghost) reorders, merges into another title row or detaches at the drop point; a fence moved by its title (Windows'
  move loop) merges when released over another title row (the target lights up; a resize never merges). An item drag
  over a header shows that tab at once, so the drop lands there; the drop target re-reads the shown fence each step.
- **Old configs** load unchanged (no new fields written for fences without tabs or colours); the normalizer repairs
  unknown ids, double membership, nesting, a missing host, single-tab boxes, invalid active tabs and colours.

**Consequences.**
- Hovering a header during an item drag switches at once (the spec said "about 0.5 s"; an instant switch keeps the drop
  target simple and was smooth in the smoke).
- Tab headers are exposed to UI Automation by their text only (no tab/selected semantics yet).
- A Portal tab shows its folder title on its header; the browsing breadcrumb shows only for a single fence.
```

- [ ] **Step 3: Write `docs/research/m9-fence-tabs.md`**

```markdown
# M9 — Fence tabs: results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), 1920×1080 at 100 %, NeoFences 1.1.2 installed.
**Build:** M9 prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section V · **Decision:** ADR-029.

## Live smoke on the prototype (installed copy restored)

| Check | Result |
|---|---|
| V1 merge "New fence" into the Inbox by dragging its title | **Pass**: tabs [Inbox, New fence], New fence shown; 2 fence windows became 1. |
| V2 switch by clicking headers | **Pass**: Inbox (31 items) and New fence (0 items) shown in turn. |
| V4 colour via the header menu (keyboard: Colour → Teal) | **Pass**: `tabColor: teal`; teal bar under the header (screenshot). |
| V10 restart | **Pass**: one window, both tabs, New fence still shown. |
| V11 roll-up on a box (double-click empty strip) | **Pass**: 275 → 32 → 275 px. |
| V9 drop an Inbox item onto the New fence header | **Pass**: New fence 0 → 1 item. |
| V7 drag the header out onto the desktop | **Pass**: tabs cleared, 2 windows again, the fence at the drop point. |
| V11 quick-hide on/off | **Pass**: 0 → 2 fences shown. |
| Log | 0 warnings or errors. |
```

- [ ] **Step 4: Update the other docs**
  - `ROADMAP.md`: the claim line becomes `- [x] M9 — done <date> (ADR-029, research/m9-fence-tabs.md)`; add `- [ ] V10 (power cut), V13 — user checks` and `- [ ] v1.2.0 release`.
  - `FEATURES.md`: the row `| Fence tabs (+ drop onto tab header, tab color) | 6 | v2 | — | — | |` becomes `| Fence tabs (+ drop onto tab header, tab color) | 6 | v1.2 | M9 | done | combine by dragging a title onto another; drag a tab out to split (ADR-029) |`.
  - `ARCHITECTURE.md`: add an "M9 complete" line (one window per box; `FenceId` = shown tab, `BoxId` = host); add `FenceTabs` to the Core contracts paragraph.
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 5: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 6: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-029, M9 checks and results, and recorded fence tabs"
```
