# M15 — Search Palette Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One hotkey (Ctrl+Alt+F) to find and open anything in the fences — also hidden tabs, rolled-up fences, Portals and the Game Library — plus NeoFences' actions; Enter opens, Shift+Enter shows the item in its fence. Released as v1.8.0.

**Architecture:**
- **Core (pure, test-first):**
  - `SearchPalette.Entries` builds the entries from the config, each Portal's last listing and the library index;
  - `SearchPalette.Rank`: prefix, then word starts, then contains, then letters in order, with case and accents ignored;
  - `SearchActions.For` gives the tray's commands for the current state;
  - `Settings.SearchHotkey`, schema 4;
  - `RollUpExpansion.Open`.
- **App:**
  - `SearchWindow` (the centred bar);
  - `FenceHost.Search`: the hotkey, entries, picks, actions, the per-Portal listing cache;
  - `FenceWindow.Reveal`;
  - a Search hotkey recorder in Settings, sharing the Peek recorder.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335, Serilog, xUnit. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-04-search-palette-design.md` (approved 2026-10-04). **Decision:** ADR-037 (new, Task 3).

**Pre-verified (2026-10-04):** every code block below was compiled together (0 warnings, 0 errors), and **444/444 tests pass** (22 new; one Appearance test asserts the current schema instead of 3).
- **Live check** (the user's go; config and the installed 1.7.0 restored; Firefox PiP untouched):
  - the palette opens by the real hotkey;
  - searches find Recycle Bin, Forza Horizon 6 ("fh6") and Take snapshot;
  - Shift+Enter reveals and selects an item in its fence;
  - Enter on "Settings" opens Settings;
  - the hotkey again and Esc both close it;
  - 0 warnings.
- **Bug found by the first check and fixed:** a crash closing the palette twice (`docs/research/m15-search-palette.md`).

## Global Constraints

- **Hard rule 1:** the palette opens and selects; it never renames, moves or deletes.
- **Hard rule 4/5:** Win32 (hotkey, foreground, monitor, glass) through `NeoFences.Shell`. **Hard rule 6:** no new NuGet. **Hard rule 7:** an unregistered hotkey is reported in Settings; a broken entry build closes the palette and is logged; icons fall back to the generic icon.
- **Hotkeys:**
  - the search hotkey is released while a game is in front and while Settings records a hotkey;
  - it works while paused;
  - it never equals Peek's.
- **Commits:** single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- **Smokes:**
  - ask the user first;
  - print the boxed TEST RUNNING / TEST COMPLETE banners;
  - press the real hotkey only after focusing empty desktop (never Terminal);
  - cleanup stops every non-installed NeoFences, restores `config.json`, removes a test-made `pre-schema-4-config.json` and verifies the installed copy runs;
  - leave the Firefox PiP alone.

## Review Focus

1. **Closing paths.** Esc, Enter, a click elsewhere, the hotkey again, Settings opening, NeoFences exiting, a second hotkey press during opening. Expected: one close, never an exception (the first check found one).
2. **Stale entries.** A fence deleted, a Portal browsed into a subfolder or its drive removed, the library rescanned, all while the palette is open. Expected: picking a gone item does nothing harmful (an open failure is reported as in a fence); Shift+Enter on a gone fence does nothing.
3. **Focus and the keyboard.** The palette must get the keyboard from the real hotkey, and Shift+Enter must give it to the fence. Over a full-screen app or when another app holds the foreground lock: no keys lost to the wrong window.
4. **Ranking edge cases.** Names with emoji, combining accents, all-digit names, a one-letter query, 2,000+ items. Expected: sensible order, no exception, no stutter.
5. **Monitors and DPI.** The palette on a secondary monitor at a different scale, or a monitor unplugged while it is open. Expected: placed on the monitor under the mouse at the right size; never off-screen.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Search/SearchPalette.cs`, `Search/SearchActions.cs` (new), `Model/Settings.cs`, `Model/NeoFencesConfig.cs`, `Config/ConfigNormalizer.cs`, `Layouts/RollUpExpansion.cs` + `tests/…/Search/SearchPaletteTests.cs` (new), `tests/…/Appearance/AppearanceTests.cs` | entries, ranking, actions, hotkey setting, schema 4, roll-up open | 1 |
| `src/NeoFences.App/SearchWindow.xaml`, `SearchWindow.xaml.cs`, `FenceHost.Search.cs` (new), `FenceHost.cs`, `FenceWindow.xaml.cs`, `SettingsWindow.xaml`, `SettingsWindow.xaml.cs` | palette window, hotkey, picks, reveal, Settings recorder | 2 |
| `docs/…` (ADR-037, checklist AB, research, ROADMAP, FEATURES, ARCHITECTURE, SESSION-LOG), hub | docs | 3 |

---

### Task 1: Core — entries, ranking, actions, hotkey setting

**Files:**
- Modify: `docs/ROADMAP.md` (M15 section and claim).
- Create: `tests/NeoFences.Core.Tests/Search/SearchPaletteTests.cs`, `src/NeoFences.Core/Search/SearchPalette.cs`, `src/NeoFences.Core/Search/SearchActions.cs`.
- Replace: `src/NeoFences.Core/Model/Settings.cs`, `Model/NeoFencesConfig.cs`, `Config/ConfigNormalizer.cs`, `Layouts/RollUpExpansion.cs`, `tests/NeoFences.Core.Tests/Appearance/AppearanceTests.cs`.

**Interfaces:**
- Produces:
  - `SearchEntry` / `ItemEntry(Name, ItemRef, FenceId, BoxId, Chip, Source)` / `ActionEntry(Name, ActionId)`, `SearchResult(Entry, Positions, Rank)`, `SearchSource`;
  - `SearchPalette.Entries(config, portalListings, library, libraryDirectory, virtualName)`, `SearchPalette.Rank(query, entries, limit = 50)`, `SearchPalette.DisplayName(itemRef, virtualName)`;
  - `SearchActions.For(snapshots, canUndoRestore, hasLibrary, paused)` and its id constants;
  - `Settings.SearchHotkey`;
  - `NeoFencesConfig.CurrentSchemaVersion = 4`;
  - `RollUpExpansion.Open()`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m15-search-palette
```
At the end of `docs/ROADMAP.md` add:
```markdown

## M15 — Search palette (v1.8, spec 2026-10-04-search-palette-design)
- [~] M15 — Search palette (v1.8.0) — claimed by session 2026-10-04 m15
```
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M15"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Search/SearchPaletteTests.cs`:
```csharp
using NeoFences.Core.Config;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using NeoFences.Core.Search;

namespace NeoFences.Core.Tests.Search;

/// <summary>M15 (v1.8, spec 2026-10-04-search-palette-design): what the search palette finds and how it ranks.</summary>
public class SearchPaletteTests
{
    private static ItemEntry Item(string name, string fence = "Inbox") =>
        new(name, @"C:\Users\me\Desktop\" + name + ".lnk", "f", "f", fence, SearchSource.Desktop);

    private static IReadOnlyList<string> Names(string query, params SearchEntry[] entries) =>
        SearchPalette.Rank(query, entries).Select(result => result.Entry.Name).ToList();

    // ---------- §2 ranking ----------

    [Fact]
    public void Rank_PrefixThenWordStartsThenContainsThenInOrder()
    {
        SearchEntry[] entries = [Item("Platform notes"), Item("Forza Horizon 6"), Item("Form 16 - 2026"), Item("Office Reports"), Item("Flash Overlay Router")];
        // prefix: Form, Forza (shorter first); word starts: F-lash O-verlay R-outer; contains: Platform; in order: Of-fice R-eports
        Assert.Equal(["Form 16 - 2026", "Forza Horizon 6", "Flash Overlay Router", "Platform notes", "Office Reports"], Names("for", entries));
    }

    [Theory]
    [InlineData("fh6")]
    [InlineData("h6")]
    [InlineData("forhor")]
    [InlineData("f h 6")] // spaces in the query are ignored for word starts
    public void Rank_WordStartsFindGamesByTheirInitials(string query) =>
        Assert.Equal(["Forza Horizon 6"], Names(query, Item("Forza Horizon 6"), Item("Halo Infinite"), Item("Notes")));

    [Fact]
    public void Rank_CamelCaseAndDigitsStartWords_AndPositionsMarkTheMatch()
    {
        var result = SearchPalette.Rank("gc", [Item("GameCenter"), Item("Logic")]).ToList();
        Assert.Equal("GameCenter", result[0].Entry.Name);
        Assert.Equal([0, 4], result[0].Positions);
        Assert.Equal(["Win11Tool"], Names("11", Item("Win11Tool"))); // a digit after a letter starts a word
        Assert.Equal([0, 6, 14], SearchPalette.Rank("fh6", [Item("Forza Horizon 6")]).Single().Positions);
    }

    [Fact]
    public void Rank_IgnoresCaseAndAccents()
    {
        Assert.Equal(["Pokémon Legends"], Names("pokemon", Item("Pokémon Legends")));
        Assert.Equal([0, 1, 2, 3], SearchPalette.Rank("POKE", [Item("Pokémon")]).Single().Positions);
        Assert.Equal(["Café"], Names("cafe", Item("Café")));
    }

    [Fact]
    public void Rank_ItemsBeforeActions_ShorterFirst_ThenAToZ_AndAtMostTheLimit()
    {
        SearchEntry[] entries = [new ActionEntry("Take snapshot", "take-snapshot"), Item("Snapshots folder"), Item("Snap"), Item("Snapper")];
        Assert.Equal(["Snap", "Snapper", "Snapshots folder", "Take snapshot"], Names("snap", entries));
        var many = Enumerable.Range(0, 80).Select(number => (SearchEntry)Item($"Note {number:00}")).ToArray();
        Assert.Equal(50, SearchPalette.Rank("note", many).Count);
        Assert.Equal(3, SearchPalette.Rank("note", many, limit: 3).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzz")]
    public void Rank_BlankOrUnmatchedQueriesGiveNothing(string query) => Assert.Empty(Names(query, Item("Forza Horizon 6")));

    // ---------- §2 what is searched ----------

    [Fact]
    public void Entries_AllFences_HiddenTabs_PortalListings_AndTheLibrary()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [@"C:\Users\me\Desktop\Notes.txt", "::{645FF040-5081-101B-9F08-00AA002F954E}"] };
        var work = Fence.Create("Work") with { Items = [@"C:\Users\me\Desktop\Budget.xlsx"] };
        var taxes = Fence.Create("Taxes", FenceSource.Portal(@"D:\Taxes"));
        var games = Fence.Create("Games", FenceSource.Library);
        var config = FenceTabs.Merge(new NeoFencesConfig { Fences = [inbox, work, taxes, games] }, taxes.Id, work.Id, wholeBox: false);
        var listings = new Dictionary<string, IReadOnlyList<ItemInfo>>
        {
            [taxes.Id] = [new ItemInfo(@"D:\Taxes\Form 16 - 2026.pdf", "Form 16 - 2026.pdf", false, "PDF", DateTimeOffset.Now)],
        };
        var library = new LibraryState
        {
            Items = [new LibraryItem(new GameEntry("steam:1", "Forza Horizon 6", GameSource.Steam, "steam", new GameLaunch("steam://rungameid/1"), null), "Forza Horizon 6.url", "sig")],
        };

        var entries = SearchPalette.Entries(config, listings, library, @"C:\Users\me\AppData\Local\NeoFences\library",
            virtualName: itemRef => itemRef.StartsWith("::{645FF040", StringComparison.Ordinal) ? "Recycle Bin" : null).OfType<ItemEntry>().ToList();

        Assert.Equal(["Notes.txt", "Recycle Bin", "Budget.xlsx", "Form 16 - 2026.pdf", "Forza Horizon 6"], entries.Select(entry => entry.Name));
        Assert.Equal(["Inbox", "Inbox", "Work › Work", "Work › Taxes", "Games · Library"], entries.Select(entry => entry.Chip));
        var form = entries.Single(entry => entry.Name.StartsWith("Form", StringComparison.Ordinal));
        Assert.Equal((taxes.Id, work.Id, SearchSource.Portal), (form.FenceId, form.BoxId, form.Source)); // a hidden tab of Work's box
        Assert.Equal(@"C:\Users\me\AppData\Local\NeoFences\library\Forza Horizon 6.url", entries[^1].ItemRef);
    }

    [Theory]
    [InlineData(@"C:\Users\me\Desktop\Steam.lnk", "Steam")]
    [InlineData(@"C:\Users\me\Desktop\Wiki.url", "Wiki")]
    [InlineData(@"C:\Users\me\Desktop\App.appref-ms", "App")]
    [InlineData(@"C:\Users\me\Desktop\Form.pdf", "Form.pdf")]
    [InlineData(@"C:\Users\me\Desktop\Projects", "Projects")]
    public void DisplayName_DropsShortcutExtensionsOnly(string itemRef, string name) =>
        Assert.Equal(name, SearchPalette.DisplayName(itemRef, virtualName: _ => null));

    [Fact]
    public void DisplayName_AVirtualItemWithoutAKnownNameIsLeftOut()
    {
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true, Items = ["::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"] }] };
        Assert.Empty(SearchPalette.Entries(config, new Dictionary<string, IReadOnlyList<ItemInfo>>(), new LibraryState(), @"C:\lib", virtualName: _ => null));
    }

    // ---------- §2 actions ----------

    [Fact]
    public void Actions_FollowWhatCanBeDoneNow_AndNeverExit()
    {
        var idle = SearchActions.For(snapshots: [("C:\\s\\a.json", "Before the stream")], canUndoRestore: false, hasLibrary: false, paused: false);
        Assert.Contains(idle, action => action.Name == "Restore snapshot \"Before the stream\"" && action.ActionId == "restore:C:\\s\\a.json");
        Assert.Contains(idle, action => action.Name == "New Game Library fence");
        Assert.DoesNotContain(idle, action => action.Name.Contains("Refresh library", StringComparison.Ordinal) || action.Name.Contains("Undo", StringComparison.Ordinal));
        Assert.Contains(idle, action => action.Name == "Pause NeoFences");
        Assert.DoesNotContain(idle, action => action.Name.Contains("Exit", StringComparison.OrdinalIgnoreCase));

        var busy = SearchActions.For(snapshots: [], canUndoRestore: true, hasLibrary: true, paused: true);
        Assert.Contains(busy, action => action.Name == "Undo the last restore");
        Assert.Contains(busy, action => action.Name == "Refresh library");
        Assert.Contains(busy, action => action.Name == "Resume NeoFences");
        Assert.DoesNotContain(busy, action => action.Name == "New Game Library fence");
        Assert.Equal(busy.Count, busy.Select(action => action.ActionId).Distinct().Count());
    }

    // ---------- §3 show in fence: a rolled-up box opens in either mode ----------

    [Theory]
    [InlineData(RollupExpand.Hover)]
    [InlineData(RollupExpand.Click)]
    public void Reveal_OpensARolledUpBox_InEitherMode(RollupExpand mode)
    {
        var expansion = new NeoFences.Core.Layouts.RollUpExpansion(mode);
        Assert.True(expansion.Open());
        Assert.True(expansion.Expanded);
        Assert.False(expansion.Open()); // already open: nothing to animate
    }

    // ---------- §4 config ----------

    [Fact]
    public void Config_SchemaFour_SearchHotkeyDefaultsAndRepairs()
    {
        Assert.Equal(4, NeoFencesConfig.CurrentSchemaVersion);
        Assert.Equal("Ctrl+Alt+F", new Settings().SearchHotkey);
        var config = ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "schemaVersion": 3, "settings": { "searchHotkey": "  " }, "fences": [] }"""));
        Assert.Equal(4, config.SchemaVersion);
        Assert.Equal("Ctrl+Alt+F", config.Settings.SearchHotkey);
        var kept = ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "settings": { "searchHotkey": "Ctrl+Shift+K" }, "fences": [] }"""));
        Assert.Equal("Ctrl+Shift+K", kept.Settings.SearchHotkey);
    }
}
```

`tests/NeoFences.Core.Tests/Appearance/AppearanceTests.cs` (asserts the current schema, not 3):
```csharp
using NeoFences.Core.Appearance;
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Appearance;

/// <summary>M14 (v1.7, spec 2026-10-04-appearance-design): the look resolver, the wallpaper accent and the config.</summary>
public class AppearanceTests
{
    private static readonly AppearanceSettings Defaults = new();
    private static readonly Fence Plain = Fence.Create("Games");
    private static readonly Argb Red = Argb.FromHex("#E84855")!.Value;

    // ---------- §3 the defaults are v1.6's look ----------

    [Fact]
    public void Defaults_GiveTheV16LookInBothTones()
    {
        var dark = FenceLook.Resolve(Defaults, Plain, light: false, wallpaperAccent: null);
        Assert.Equal(0, dark.Veil.A);                                   // Clear: no veil
        Assert.Equal(new Argb(0x40, 0xFF, 0xFF, 0xFF), dark.Border);
        Assert.Equal(new Argb(0xFF, 0xFF, 0xFF, 0xFF), dark.TitleText);
        Assert.Null(dark.TitleStrip);
        Assert.Null(dark.Bar);

        var light = FenceLook.Resolve(Defaults, Plain, light: true, wallpaperAccent: null);
        Assert.Equal(new Argb(0xB8, 0xF2, 0xF2, 0xF2), light.Veil);     // today's light veil
        Assert.Equal(new Argb(0x33, 0x00, 0x00, 0x00), light.Border);
        Assert.Equal(new Argb(0xE6, 0x00, 0x00, 0x00), light.TitleText);

        Assert.Equal(("Segoe UI", 14, TitleWeight.SemiBold, 30.0), (dark.Font.Family, dark.Font.Size, dark.Font.Weight, dark.TitleHeight));
    }

    [Theory]
    [InlineData(false, 40, 0x66)]
    [InlineData(true, 0, 0x00)]
    [InlineData(true, 85, 0xD9)]
    public void Strength_SetsTheVeilOfTheCurrentTone(bool light, int strength, byte alpha)
    {
        var appearance = light ? Defaults with { StrengthLight = strength } : Defaults with { StrengthDark = strength };
        Assert.Equal(alpha, FenceLook.Resolve(appearance, Plain, light, wallpaperAccent: null).Veil.A);
    }

    // ---------- §3.1 which colour ----------

    [Fact]
    public void Colour_CustomBeatsSwatchBeatsAccent_AndTheAccentNeedsItsSwitch()
    {
        var accent = new Argb(0xFF, 0x20, 0x90, 0x40);
        var swatch = Plain with { TabColor = TabColor.Blue };
        var custom = swatch with { CustomColor = "#E84855" };

        Assert.Equal(Red, FenceLook.Resolve(Defaults, custom, light: false, accent).Colour);
        Assert.Equal(FenceLook.Swatches[TabColor.Blue], FenceLook.Resolve(Defaults, swatch, light: false, accent).Colour);
        Assert.Null(FenceLook.Resolve(Defaults, Plain, light: false, accent).Colour);                                   // switch off
        Assert.Equal(accent, FenceLook.Resolve(Defaults with { WallpaperAccent = true }, Plain, light: false, accent).Colour);
    }

    // ---------- §3.2 the three styles ----------

    [Fact]
    public void AccentEdge_ColoursBorderBarAndTitle_KeepsTheVeil()
    {
        var look = FenceLook.Resolve(Defaults, Plain with { CustomColor = "#E84855" }, light: false, wallpaperAccent: null);
        Assert.Equal(0, look.Veil.A);
        Assert.Equal(Red, look.Border);
        Assert.Equal(Red, look.Bar);
        Assert.True(look.TitleText.R > Red.R || look.TitleText.G > Red.G); // lighter than the colour: readable on dark
        Assert.Null(look.TitleStrip);
    }

    [Fact]
    public void TintedGlass_ShowsAtClear_AndBorderTakesTheColour()
    {
        var look = FenceLook.Resolve(Defaults with { ColourStyle = ColourStyle.TintedGlass }, Plain with { CustomColor = "#E84855" }, light: false, wallpaperAccent: null);
        Assert.True(look.Veil.A >= 56);             // at least 22 %
        Assert.True(look.Veil.R > look.Veil.G);     // reddish
        Assert.Equal(Red with { A = 0xB3 }, look.Border);
        Assert.Null(look.Bar);
    }

    [Theory]
    [InlineData("#0078D4", 0xFF)] // dark blue strip: white title
    [InlineData("#FFB900", 0x00)] // yellow strip: dark title
    [InlineData("#16C60C", 0x00)] // green strip (luminance 0.41): dark reads better than white (final review I4)
    [InlineData("#00B7C3", 0x00)] // teal, same
    [InlineData("#E81123", 0xFF)] // deep red: white
    public void TitleStrip_ColoursOnlyTheTitleBar_WithReadableText(string colour, byte titleRed)
    {
        var look = FenceLook.Resolve(Defaults with { ColourStyle = ColourStyle.TitleStrip }, Plain with { CustomColor = colour }, light: false, wallpaperAccent: null);
        Assert.Equal(Argb.FromHex(colour)!.Value with { A = 0xBF }, look.TitleStrip);
        Assert.Equal(titleRed, look.TitleText.R);
        Assert.Equal(new Argb(0x40, 0xFF, 0xFF, 0xFF), look.Border);
    }

    // ---------- §3.4 fonts ----------

    [Fact]
    public void Font_AFenceOverridesOnlyWhatItSets_AndTheTitleBarGrows()
    {
        var appearance = Defaults with { TitleFont = new TitleFont("Bahnschrift", 17, TitleWeight.Bold) };
        var look = FenceLook.Resolve(appearance, Plain with { TitleFont = new TitleFont(null, 20, null) }, light: false, wallpaperAccent: null);
        Assert.Equal(("Bahnschrift", 20, TitleWeight.Bold, 38.0), (look.Font.Family, look.Font.Size, look.Font.Weight, look.TitleHeight));
        Assert.Equal(26.0, FenceLook.TitleHeightFor(12));
    }

    // ---------- §4 the wallpaper's colour ----------

    private static byte[] Image(params (byte R, byte G, byte B, int Count)[] runs) =>
        [.. runs.SelectMany(run => Enumerable.Repeat(new[] { run.B, run.G, run.R, (byte)255 }, run.Count).SelectMany(pixel => pixel))];

    [Fact]
    public void Accent_TheStrongestVividHueWins_DarkAndGreyPixelsAreIgnored()
    {
        // mostly near-black and grey, some vivid red, a little blue
        var accent = AccentColor.FromPixels(Image((5, 5, 8, 600), (128, 128, 128, 300), (220, 40, 50, 80), (30, 60, 220, 20)));
        Assert.NotNull(accent);
        Assert.True(accent.Value.R > 150 && accent.Value.G < 110 && accent.Value.B < 120);
        Assert.Equal(0xFF, accent.Value.A);
    }

    [Fact]
    public void Accent_AGreyImageHasNone_AndColoursAreClampedToReadableLightness()
    {
        Assert.Null(AccentColor.FromPixels(Image((10, 10, 10, 500), (200, 200, 200, 500))));
        var deep = AccentColor.FromPixels(Image((60, 0, 0, 1000)))!.Value;   // very dark red
        var lightness = (Math.Max(deep.R, Math.Max(deep.G, deep.B)) + Math.Min(deep.R, Math.Min(deep.G, deep.B))) / 2.0 / 255;
        Assert.InRange(lightness, 0.39, 0.66);

        // a muted slate (the user's WE wallpaper gave #566376): the same hue, vivid enough to read as an accent
        var muted = AccentColor.FromPixels(Image((86, 99, 118, 1000)))!.Value;
        double max = Math.Max(muted.R, Math.Max(muted.G, muted.B)) / 255.0, min = Math.Min(muted.R, Math.Min(muted.G, muted.B)) / 255.0;
        Assert.True((max - min) / (1 - Math.Abs(max + min - 1)) >= 0.44, $"{muted.ToHex()} is too grey");
        Assert.True(muted.B > muted.G && muted.G > muted.R); // still blue
    }

    // ---------- §4 Wallpaper Engine's files ----------

    [Fact]
    public void WallpaperEngine_SelectedWallpapersAndPreviewAreRead()
    {
        const string config = """
            { "cipher": { "general": { "wallpaperconfig": { "selectedwallpapers": {
                "Monitor0": { "file": "C:/Steam/steamapps/workshop/content/431960/1405736695/scene.pkg" },
                "Monitor1": { "file": "D:/WE/projects/myproject/index.html" } } } } },
              "?installdirectory": "C:/Steam" }
            """;
        var selected = WallpaperEngineFiles.SelectedWallpapers(config);
        Assert.Equal(@"C:\Steam\steamapps\workshop\content\431960\1405736695\scene.pkg", selected[0]);
        Assert.Equal(@"D:\WE\projects\myproject\index.html", selected[1]);
        Assert.Equal("preview.gif", WallpaperEngineFiles.PreviewName("""{ "title": "Kara", "preview": "preview.gif", "type": "scene" }"""));
        Assert.Equal("Kara", WallpaperEngineFiles.TitleOf("""{ "title": " Kara ", "preview": "preview.gif" }"""));
        Assert.Empty(WallpaperEngineFiles.SelectedWallpapers("{ not json"));
        Assert.Empty(WallpaperEngineFiles.SelectedWallpapers("""{ "cipher": { "general": {} } }"""));
        Assert.Null(WallpaperEngineFiles.PreviewName("""{ "preview": "../../escape.jpg" }""")); // stays inside the wallpaper's folder
    }

    // ---------- §2 config ----------

    [Fact]
    public void Config_SchemaThree_AndBrokenAppearanceIsRepaired()
    {
        const string json = """
            { "schemaVersion": 2,
              "settings": { "appearance": { "strengthDark": 300, "strengthLight": -5, "colourStyle": "neon", "titleFont": { "family": " ", "size": 13, "weight": "heavy" } } },
              "fences": [ { "id": "a", "title": "A", "isInbox": true, "customColor": "red", "titleFont": { "size": 99, "family": "Bahnschrift" } },
                          { "id": "b", "title": "B", "customColor": "#e84855" } ] }
            """;
        var config = ConfigNormalizer.Normalize(ConfigJson.Deserialize(json));

        Assert.Equal(NeoFencesConfig.CurrentSchemaVersion, config.SchemaVersion); // 3 in v1.7, 4 since v1.8 (M15)
        var appearance = config.Settings.Appearance;
        Assert.Equal((85, 0, ColourStyle.AccentEdge), (appearance.StrengthDark, appearance.StrengthLight, appearance.ColourStyle));
        Assert.Equal(new TitleFont("Segoe UI", 14, TitleWeight.SemiBold), appearance.TitleFont);
        Assert.Null(config.Fences[0].CustomColor);
        Assert.Equal(new TitleFont("Bahnschrift", null, null), config.Fences[0].TitleFont);
        Assert.Equal("#E84855", config.Fences[1].CustomColor);
    }

    [Fact]
    public void Config_AnOldFileGetsTheDefaults_AndAppearanceRoundTrips()
    {
        var old = ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "schemaVersion": 2, "fences": [ { "id": "a", "title": "A", "isInbox": true } ] }"""));
        Assert.Equal(new AppearanceSettings(), old.Settings.Appearance);

        var styled = old with { Settings = old.Settings with { Appearance = new AppearanceSettings { ColourStyle = ColourStyle.TitleStrip, WallpaperAccent = true, StrengthDark = 40 } } };
        var again = ConfigNormalizer.Normalize(ConfigJson.Deserialize(ConfigJson.Serialize(styled)));
        Assert.Equal(styled.Settings.Appearance, again.Settings.Appearance);
        Assert.Contains("\"colourStyle\": \"titleStrip\"", ConfigJson.Serialize(styled));
    }

    // ---------- fence menu edits ----------

    [Fact]
    public void Edits_ASwatchClearsTheCustomColour_AndFontsAreSetPerFence()
    {
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, Plain] };

        var custom = FenceEdits.SetCustomColor(config, Plain.Id, "#e84855");
        Assert.Equal("#E84855", custom.Fences[1].CustomColor);
        var swatch = FenceTabs.SetColor(custom, Plain.Id, TabColor.Teal);
        Assert.Null(swatch.Fences[1].CustomColor);
        Assert.Equal(TabColor.Teal, swatch.Fences[1].TabColor);
        Assert.Same(swatch, FenceTabs.SetColor(swatch, Plain.Id, TabColor.Teal));

        var font = FenceEdits.SetTitleFont(swatch, Plain.Id, new TitleFont("Bahnschrift", null, null));
        Assert.Equal("Bahnschrift", font.Fences[1].TitleFont!.Family);
        Assert.Null(FenceEdits.SetTitleFont(font, Plain.Id, null).Fences[1].TitleFont); // "Use the default"
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build errors: the namespace `NeoFences.Core.Search` does not exist; `ItemEntry` and `SearchEntry` not found.

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Search/SearchPalette.cs`:
```csharp
using System.Globalization;
using System.Text;
using NeoFences.Core.Library;
using NeoFences.Core.Model;

namespace NeoFences.Core.Search;

public enum SearchSource { Desktop, Portal, Library }

/// <summary>Something the search palette can show (M15): an item in a fence, or a NeoFences action.</summary>
public abstract record SearchEntry(string Name);

/// <param name="Chip">Where it lives: "Inbox", "Work › Taxes" (a tab of Work's box), "Games · Library".</param>
public sealed record ItemEntry(string Name, string ItemRef, string FenceId, string BoxId, string Chip, SearchSource Source) : SearchEntry(Name);

public sealed record ActionEntry(string Name, string ActionId) : SearchEntry(Name);

/// <param name="Positions">Indexes into <see cref="SearchEntry.Name"/> of the matched letters (for highlighting).</param>
/// <param name="Rank">1 prefix, 2 word starts, 3 contains, 4 letters in order.</param>
public sealed record SearchResult(SearchEntry Entry, IReadOnlyList<int> Positions, int Rank);

/// <summary>
/// The search palette (M15, spec 2026-10-04-search-palette-design §2): what it searches and how results rank. Pure: the App
/// supplies Portal listings, the library index and virtual items' shell names.
/// </summary>
public static class SearchPalette
{
    /// <summary>
    /// Every item of every fence, also tabs not shown and rolled-up fences: desktop items from the config, each Portal's
    /// last listing (the level it shows) and the library's games. Virtual items without a known name are left out.
    /// </summary>
    public static IReadOnlyList<SearchEntry> Entries(NeoFencesConfig config, IReadOnlyDictionary<string, IReadOnlyList<ItemInfo>> portalListings,
        LibraryState library, string libraryDirectory, Func<string, string?> virtualName)
    {
        var entries = new List<SearchEntry>();
        foreach (var fence in config.Fences)
        {
            var box = FenceTabs.HostOf(config, fence.Id);
            var chip = FenceTabs.TabsOf(config, box.Id).Count > 1 ? $"{box.Title} › {fence.Title}" : fence.Title;
            switch (fence.Source.Kind)
            {
                case FenceSourceKind.Desktop:
                    foreach (var itemRef in fence.Items)
                    {
                        if (DisplayName(itemRef, virtualName) is { Length: > 0 } name)
                            entries.Add(new ItemEntry(name, itemRef, fence.Id, box.Id, chip, SearchSource.Desktop));
                    }
                    break;
                case FenceSourceKind.Portal when portalListings.TryGetValue(fence.Id, out var listing):
                    entries.AddRange(listing.Select(item => new ItemEntry(item.Name, item.ItemRef, fence.Id, box.Id, chip, SearchSource.Portal)));
                    break;
                case FenceSourceKind.Library:
                    entries.AddRange(library.Items.Select(item => new ItemEntry(item.Game.Name, Path.Combine(libraryDirectory, item.FileName),
                        fence.Id, box.Id, chip + " · Library", SearchSource.Library)));
                    break;
            }
        }
        return entries;
    }

    /// <summary>The name a desktop item shows: shortcuts without their extension, other files as they are, virtual items by their shell name.</summary>
    public static string? DisplayName(string itemRef, Func<string, string?> virtualName)
    {
        if (itemRef.StartsWith("::", StringComparison.Ordinal)) return virtualName(itemRef);
        var name = Path.GetFileName(itemRef.TrimEnd('\\'));
        return Path.GetExtension(name).ToLowerInvariant() is ".lnk" or ".url" or ".appref-ms" ? Path.GetFileNameWithoutExtension(name) : name;
    }

    /// <summary>
    /// The best matches: prefix, then word starts ("fh6" → Forza Horizon 6), then contains, then the letters in order.
    /// Case and accents are ignored; spaces in the query too for word starts and letters in order. Within a rank: items
    /// before actions, shorter names, then A–Z. A blank query gives none.
    /// </summary>
    public static IReadOnlyList<SearchResult> Rank(string query, IReadOnlyList<SearchEntry> entries, int limit = 50)
    {
        var folded = Fold(query.Trim());
        if (folded.Text.Length == 0) return [];
        var compact = folded.Text.Replace(" ", "", StringComparison.Ordinal);
        var results = new List<SearchResult>();
        foreach (var entry in entries)
        {
            if (Match(folded.Text, compact, entry.Name) is { } match) results.Add(new SearchResult(entry, match.Positions, match.Rank));
        }
        return results
            .OrderBy(result => result.Rank)
            .ThenBy(result => result.Entry is ActionEntry)
            .ThenBy(result => result.Entry.Name.Length)
            .ThenBy(result => result.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static (int Rank, IReadOnlyList<int> Positions)? Match(string query, string compact, string name)
    {
        var folded = Fold(name);
        var text = folded.Text;
        IReadOnlyList<int> Original(IEnumerable<int> foldedPositions) => foldedPositions.Select(position => folded.Origin[position]).Distinct().ToList();

        if (text.StartsWith(query, StringComparison.Ordinal)) return (1, Original(Enumerable.Range(0, query.Length)));
        if (compact.Length > 0 && WordStarts(compact, text, folded.WordStart) is { } words) return (2, Original(words));
        var at = text.IndexOf(query, StringComparison.Ordinal);
        if (at >= 0) return (3, Original(Enumerable.Range(at, query.Length)));
        if (compact.Length > 0 && InOrder(compact, text) is { } letters) return (4, Original(letters));
        return null;
    }

    /// <summary>The query as runs of letters that each begin a word, in order ("forhor": "for" + "hor"). Longest runs first.</summary>
    private static List<int>? WordStarts(string query, string text, bool[] wordStart)
    {
        List<int>? Search(int queryIndex, int from)
        {
            if (queryIndex == query.Length) return [];
            for (var start = from; start < text.Length; start++)
            {
                if (!wordStart[start] || text[start] != query[queryIndex]) continue;
                var run = 1;
                while (queryIndex + run < query.Length && start + run < text.Length && text[start + run] == query[queryIndex + run]) run++;
                for (var length = run; length >= 1; length--)
                {
                    if (Search(queryIndex + length, start + length) is { } rest) return [.. Enumerable.Range(start, length), .. rest];
                }
            }
            return null;
        }
        return Search(0, 0);
    }

    private static List<int>? InOrder(string query, string text)
    {
        var positions = new List<int>(query.Length);
        var at = 0;
        foreach (var letter in query)
        {
            at = text.IndexOf(letter, at);
            if (at < 0) return null;
            positions.Add(at++);
        }
        return positions;
    }

    /// <summary>
    /// Lower case without accents ("Pokémon" → "pokemon"), with each folded letter's index in the original and whether a
    /// word starts there: the first letter, after a space or punctuation, a capital after a small letter, a digit after a letter.
    /// </summary>
    private static (string Text, int[] Origin, bool[] WordStart) Fold(string value)
    {
        var text = new StringBuilder(value.Length);
        var origin = new List<int>(value.Length);
        var starts = new List<bool>(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var previous = index > 0 ? value[index - 1] : '\0';
            var startsWord = index == 0 || !char.IsLetterOrDigit(previous)
                             || (char.IsUpper(character) && char.IsLower(previous))
                             || (char.IsDigit(character) && char.IsLetter(previous));
            var first = true;
            foreach (var part in character.ToString().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(part) == UnicodeCategory.NonSpacingMark) continue;
                text.Append(char.ToLowerInvariant(part));
                origin.Add(index);
                starts.Add(startsWord && first);
                first = false;
            }
        }
        return (text.ToString(), [.. origin], [.. starts]);
    }
}
```

`src/NeoFences.Core/Search/SearchActions.cs`:
```csharp
namespace NeoFences.Core.Search;

/// <summary>
/// NeoFences' commands in the search palette (M15, spec §2): the tray menu's, each one searchable. "Exit" is never one:
/// a stray Enter must not quit NeoFences.
/// </summary>
public static class SearchActions
{
    public const string NewFence = "new-fence", NewPortal = "new-portal", NewLibrary = "new-library", TakeSnapshot = "take-snapshot",
        RestorePrefix = "restore:", UndoRestore = "undo-restore", ApplyRules = "apply-rules", RefreshLibrary = "refresh-library",
        Peek = "peek", PauseOrResume = "pause", Settings = "settings";

    /// <param name="snapshots">Restorable snapshots (path, name), newest first.</param>
    public static IReadOnlyList<ActionEntry> For(IReadOnlyList<(string Path, string Name)> snapshots, bool canUndoRestore, bool hasLibrary, bool paused)
    {
        List<ActionEntry> actions =
        [
            new("New fence", NewFence),
            new("New Portal fence…", NewPortal),
        ];
        if (!hasLibrary) actions.Add(new ActionEntry("New Game Library fence", NewLibrary));
        actions.Add(new ActionEntry("Take snapshot", TakeSnapshot));
        actions.AddRange(snapshots.Select(snapshot => new ActionEntry($"Restore snapshot \"{snapshot.Name}\"", RestorePrefix + snapshot.Path)));
        if (canUndoRestore) actions.Add(new ActionEntry("Undo the last restore", UndoRestore));
        actions.Add(new ActionEntry("Apply rules now", ApplyRules));
        if (hasLibrary) actions.Add(new ActionEntry("Refresh library", RefreshLibrary));
        actions.Add(new ActionEntry("Peek at the fences", Peek));
        actions.Add(new ActionEntry(paused ? "Resume NeoFences" : "Pause NeoFences", PauseOrResume));
        actions.Add(new ActionEntry("Settings", Settings));
        return actions;
    }
}
```

`src/NeoFences.Core/Model/Settings.cs`:
```csharp
namespace NeoFences.Core.Model;

public enum RollupExpand { Hover, Click }

public sealed record Settings
{
    /// <summary>Hide native desktop icons and show them in fences. Off by default until the M2 sign-out test passes (ADR-011).</summary>
    public bool Takeover { get; init; }
    /// <summary>The user answered the one-time "hide desktop icons?" banner in the Inbox (M2b first run).</summary>
    public bool TakeoverPromptAnswered { get; init; }
    public string PeekHotkey { get; init; } = "Ctrl+Alt+Space";
    /// <summary>Opens the search palette (M15, user choice: Ctrl+Alt+F, "F" for find).</summary>
    public string SearchHotkey { get; init; } = "Ctrl+Alt+F";
    public bool StartWithWindows { get; init; } = true;
    public bool GameMode { get; init; } = true;
    public RollupExpand RollupExpand { get; init; } = RollupExpand.Hover;
    /// <summary>Labels for new fences (M8b; Settings → Fences).</summary>
    public LabelMode DefaultLabels { get; init; } = LabelMode.Always;
    /// <summary>The small arrow Windows draws on shortcut icons (M8b, user choice: a setting, off by default).</summary>
    public bool ShowShortcutArrows { get; init; }
    /// <summary>Settings → Appearance (M14): background strength, colour style, wallpaper accent, title font.</summary>
    public AppearanceSettings Appearance { get; init; } = new();
}
```

`src/NeoFences.Core/Model/NeoFencesConfig.cs`:
```csharp
using System.Text.Json.Serialization;

namespace NeoFences.Core.Model;

/// <summary>Everything NeoFences persists, stored as <c>config.json</c> (ADR-006).</summary>
public sealed record NeoFencesConfig
{
    /// <summary>
    /// 4 since v1.8 (M15): the search palette's hotkey. 3 since v1.7 (M14): appearance settings and per-fence colours and fonts. 2 since v1.6 (M13a): tabs, rules and the
    /// library. An older NeoFences reads a newer number as read-only and never saves over it (it would drop the fields).
    /// </summary>
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Settings Settings { get; init; } = new();
    public IReadOnlyList<Fence> Fences { get; init; } = [];
    public IReadOnlyDictionary<string, Layout> Layouts { get; init; } = new Dictionary<string, Layout>();

    /// <summary>Rules auto-sort (M11), in order: the first matching enabled rule decides a new item's fence.</summary>
    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary>Game Library settings (M12): game folders, sources, hidden games.</summary>
    public LibrarySettings Library { get; init; } = new();

    /// <summary>Fingerprint of the display configuration seen last; new configurations are derived from it.</summary>
    public string? LastLayoutFingerprint { get; init; }

    /// <summary>The single Inbox fence. Guaranteed to exist after <c>ConfigNormalizer.Normalize</c>.</summary>
    [JsonIgnore]
    public Fence Inbox => Fences.First(fence => fence.IsInbox);

    public static NeoFencesConfig CreateDefault() =>
        new() { Fences = [Fence.Create("Inbox") with { IsInbox = true }] };

    public NeoFencesConfig WithFence(Fence updated) =>
        this with { Fences = Fences.Select(fence => fence.Id == updated.Id ? updated : fence).ToList() };
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
            SearchHotkey = string.IsNullOrWhiteSpace(settings.SearchHotkey) ? defaults.SearchHotkey : settings.SearchHotkey, // M15
            RollupExpand = Enum.IsDefined(settings.RollupExpand) ? settings.RollupExpand : defaults.RollupExpand,
            DefaultLabels = Enum.IsDefined(settings.DefaultLabels) ? settings.DefaultLabels : defaults.DefaultLabels, // M8b review
            Appearance = NormalizeAppearance(settings.Appearance),
        };

        var seenFenceIds = new HashSet<string>(StringComparer.Ordinal);
        var seenItems = new HashSet<string>(ItemRef.Comparer);
        var inboxFound = false;
        var libraryFound = false; // M12: at most one Game Library fence
        var fences = new List<Fence>();

        foreach (var loadedFence in config.Fences ?? [])
        {
            if (loadedFence is null) continue;
            var isInbox = loadedFence.IsInbox && !inboxFound;
            var source = loadedFence.Source is { } loadedSource && Enum.IsDefined(loadedSource.Kind) ? loadedSource : FenceSource.Desktop;
            if (source.Kind == FenceSourceKind.Library && (libraryFound || loadedFence.IsInbox)) source = FenceSource.Desktop;
            libraryFound |= source.Kind == FenceSourceKind.Library;
            var fence = loadedFence with
            {
                Id = string.IsNullOrWhiteSpace(loadedFence.Id) || !seenFenceIds.Add(loadedFence.Id) ? Fence.NewId() : loadedFence.Id,
                Title = loadedFence.Title ?? "",
                Source = isInbox ? FenceSource.Desktop : source,
                IsInbox = isInbox,
                Sort = Enum.IsDefined(loadedFence.Sort) ? loadedFence.Sort : FenceSort.Manual,
                Labels = Enum.IsDefined(loadedFence.Labels) ? loadedFence.Labels : LabelMode.Always, // a hand-edited number (M8b review)
                Tabs = loadedFence.Tabs ?? [], // a hand-edited "tabs": null (M9 final review)
                IconSize = IconSizes.Contains(loadedFence.IconSize) ? loadedFence.IconSize : 48,
                CustomColor = Appearance.Argb.FromHex(loadedFence.CustomColor)?.ToHex(), // M14: a broken colour is none
                TitleFont = NormalizeFontOverride(loadedFence.TitleFont),
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
            SchemaVersion = NeoFencesConfig.CurrentSchemaVersion, // an older file is saved in today's format (M13a)
            Settings = settings,
            Fences = FenceTabs.Repair(fences), // M9: one consistent box per tab
            Rules = UniqueIds((config.Rules ?? []).Where(rule => rule is not null).Select(Rules.Repair)), // M11: a broken rule is disabled
            Layouts = NormalizeLayouts(config.Layouts),
            Library = NormalizeLibrary(config.Library),
        };
    }

    /// <summary>M14: strengths in range, a known style, a whole global title font (spec §2).</summary>
    private static AppearanceSettings NormalizeAppearance(AppearanceSettings? appearance)
    {
        var defaults = new AppearanceSettings();
        if (appearance is null) return defaults;
        var font = appearance.TitleFont;
        return appearance with
        {
            StrengthDark = Math.Clamp(appearance.StrengthDark, 0, AppearanceSettings.MaxStrength),
            StrengthLight = Math.Clamp(appearance.StrengthLight, 0, AppearanceSettings.MaxStrength),
            ColourStyle = Enum.IsDefined(appearance.ColourStyle) ? appearance.ColourStyle : defaults.ColourStyle,
            TitleFont = new TitleFont(
                string.IsNullOrWhiteSpace(font?.Family) ? defaults.TitleFont.Family : font.Family.Trim(),
                font?.Size is { } size && TitleFont.Sizes.Contains(size) ? size : defaults.TitleFont.Size,
                font?.Weight is { } weight && Enum.IsDefined(weight) ? weight : defaults.TitleFont.Weight),
        };
    }

    /// <summary>A fence's font override keeps only valid parts; nothing left = no override (M14).</summary>
    private static TitleFont? NormalizeFontOverride(TitleFont? font)
    {
        if (font is null) return null;
        var repaired = new TitleFont(
            string.IsNullOrWhiteSpace(font.Family) ? null : font.Family.Trim(),
            font.Size is { } size && TitleFont.Sizes.Contains(size) ? size : null,
            font.Weight is { } weight && Enum.IsDefined(weight) ? weight : null);
        return repaired is { Family: null, Size: null, Weight: null } ? null : repaired;
    }

    /// <summary>A rule block copied in a hand edit keeps its id: the copies get new ones, or one action would hit all (M13a).</summary>
    private static List<Rule> UniqueIds(IEnumerable<Rule> rules)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return rules.Select(rule => seen.Add(rule.Id) ? rule : rule with { Id = Guid.NewGuid().ToString("N") }).ToList();
    }

    private static LibrarySettings NormalizeLibrary(LibrarySettings? library) => new()
    {
        Folders = (library?.Folders ?? []).Where(folder => !string.IsNullOrWhiteSpace(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        Sources = library?.Sources ?? new LibrarySources(),
        Hidden = (library?.Hidden ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
    };

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

`src/NeoFences.Core/Layouts/RollUpExpansion.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Layouts;

/// <summary>
/// Whether a rolled-up fence is open right now (M5 hover, M6b click; user choice 2026-10-03: both offered, hover
/// default). Driven by a 100 ms pointer poll and by single clicks on the title. Either way it closes about half a
/// second after the pointer leaves, so a brief slip does not close it.
/// </summary>
public sealed class RollUpExpansion(RollupExpand mode)
{
    public const int OpenTicks = 3;  // ~300 ms resting on the fence (hover mode)
    public const int CloseTicks = 5; // ~500 ms away

    private int _ticks;

    public RollupExpand Mode { get; set; } = mode;

    public bool Expanded { get; private set; }

    /// <summary>One poll with the pointer inside or outside the fence.</summary>
    /// <returns>True when <see cref="Expanded"/> changed.</returns>
    public bool Tick(bool pointerInside)
    {
        if (!Expanded && Mode == RollupExpand.Click)
        {
            _ticks = 0; // click mode never opens by resting
            return false;
        }
        _ticks = pointerInside == Expanded ? 0 : _ticks + 1;
        if (_ticks < (Expanded ? CloseTicks : OpenTicks)) return false;
        _ticks = 0;
        Expanded = !Expanded;
        return true;
    }

    /// <summary>A single click on the rolled-up title.</summary>
    /// <returns>True when it opened the fence (click mode only; hover mode opens by resting).</returns>
    public bool Click()
    {
        if (Expanded || Mode != RollupExpand.Click) return false;
        Expanded = true;
        _ticks = 0;
        return true;
    }

    /// <summary>The search palette shows an item in this box (M15): it opens in either mode.</summary>
    /// <returns>True when it opened (false when it was open already).</returns>
    public bool Open()
    {
        if (Expanded) return false;
        Expanded = true;
        _ticks = 0;
        return true;
    }

    /// <summary>Rolled up or unrolled by a double-click: start closed.</summary>
    public void Reset()
    {
        Expanded = false;
        _ticks = 0;
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed: 444`, 0 failed.

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests
git commit -m "feat: added search palette entries, ranking and actions, and the search hotkey setting"
```

---

### Task 2: App — palette window, hotkey, picks, reveal, Settings

**Files:**
- Create: `src/NeoFences.App/SearchWindow.xaml`, `SearchWindow.xaml.cs`, `FenceHost.Search.cs`.
- Replace: `src/NeoFences.App/FenceHost.cs`, `FenceWindow.xaml.cs`, `SettingsWindow.xaml`, `SettingsWindow.xaml.cs`.

**Interfaces:**
- Consumes: Task 1's `SearchPalette`, `SearchActions`, `SearchEntry` types, `Settings.SearchHotkey`, `RollUpExpansion.Open`.
- Produces:
  - `SearchWindow(entries, iconLoader, lightTheme, fencesHidden)` with `Picked(SearchResult, bool show)` and `PlaceOn(MonitorPlacement)`;
  - `FenceWindow.Reveal(itemRef)`;
  - `SettingsWindow.SearchHotkeyChosen` / `ShowSearchHotkeyResult`;
  - `SettingsView.SearchHotkey` / `SearchHotkeyActive`.

- [ ] **Step 1: Files**

`src/NeoFences.App/SearchWindow.xaml`:
```xml
<Window x:Class="NeoFences.App.SearchWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NeoFences search" Width="640" SizeToContent="Height" WindowStyle="None" AllowsTransparency="True"
        Background="Transparent" ResizeMode="NoResize" ShowInTaskbar="False" Topmost="True" ShowActivated="True"
        UseLayoutRounding="True">
    <!-- The search palette (M15, spec 2026-10-04-search-palette-design §3): a centred bar; colours set by ApplyTheme. -->
    <Border x:Name="Frame" CornerRadius="10" BorderThickness="1" Padding="0,0,0,4">
        <StackPanel>
            <DockPanel Margin="14,10,14,10">
                <TextBlock x:Name="Glass" Text="&#xE721;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="16"
                           VerticalAlignment="Center" Margin="0,0,10,0" />
                <TextBlock x:Name="KeysHint" DockPanel.Dock="Right" VerticalAlignment="Center" FontSize="11" Margin="10,0,0,0"
                           Text="Enter open · Shift+Enter show" />
                <TextBox x:Name="QueryBox" FontSize="16" BorderThickness="0" Background="Transparent" VerticalAlignment="Center"
                         AutomationProperties.Name="Search fences and actions" />
            </DockPanel>
            <Border x:Name="Divider" Height="1" />
            <ListBox x:Name="Results" BorderThickness="0" Background="Transparent" MaxHeight="306" Margin="0,4,0,0"
                     ScrollViewer.HorizontalScrollBarVisibility="Disabled" AutomationProperties.Name="Results"
                     KeyboardNavigation.TabNavigation="None" Focusable="False" />
            <TextBlock x:Name="Footer" FontSize="11" Margin="14,6,14,4" />
        </StackPanel>
    </Border>
</Window>
```

`src/NeoFences.App/SearchWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NeoFences.Core.Layouts;
using NeoFences.Core.Search;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// The search palette (M15, spec 2026-10-04-search-palette-design §3): type to find items in every fence and NeoFences'
/// actions; Enter opens, Shift+Enter shows the item in its fence, Esc or a click elsewhere closes. It never changes a file.
/// </summary>
public partial class SearchWindow : Window
{
    private const double RowIconDips = 22;
    private readonly IReadOnlyList<SearchEntry> _entries;
    private readonly IconLoader _iconLoader;
    private readonly bool _fencesHidden;
    private readonly Dictionary<string, FenceItemView> _views = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<SearchResult> _results = [];
    private bool _closing;

    /// <summary>A result was chosen: open it (false) or show it in its fence (true, Shift+Enter).</summary>
    public event Action<SearchResult, bool>? Picked;

    /// <param name="entries">Everything searchable, built by the host when the palette opens.</param>
    /// <param name="fencesHidden">Paused or quick-hidden: Shift+Enter only says so.</param>
    public SearchWindow(IReadOnlyList<SearchEntry> entries, IconLoader iconLoader, bool lightTheme, bool fencesHidden)
    {
        _entries = entries;
        _iconLoader = iconLoader;
        _fencesHidden = fencesHidden;
        InitializeComponent();
        ApplyTheme(lightTheme);
        QueryBox.TextChanged += (_, _) => ShowResults();
        PreviewKeyDown += OnKeys;
        Deactivated += (_, _) => CloseOnce(); // a click elsewhere
        // Every close counts, also the host's (the hotkey again): Close() deactivates, and a second Close() while closing throws.
        Closing += (_, _) => _closing = true;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            FenceWindowChrome.ApplyAccentBlur(handle);   // the fences' glass (spec §3)
            FenceWindowChrome.UseRoundedCorners(handle);
        };
        Loaded += (_, _) => QueryBox.Focus();
        ShowResults();
    }

    /// <summary>Top-centre at 20 % of the monitor's work area, in its pixels (the window may sit on another DPI than the primary).</summary>
    public void PlaceOn(MonitorPlacement monitor)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var widthPx = (int)Math.Round(Width * monitor.Scale);
        var heightPx = (int)Math.Round(Math.Max(ActualHeight, 120) * monitor.Scale);
        var x = monitor.WorkLeftPx + (monitor.WorkWidthPx - widthPx) / 2;
        var y = monitor.WorkTopPx + monitor.WorkHeightPx / 5;
        FenceWindowChrome.SetPixelRect(handle, new PixelRect(x, y, widthPx, heightPx));
    }

    private void ApplyTheme(bool light)
    {
        Frame.Background = Brush(light ? Color.FromArgb(0xE8, 0xF4, 0xF4, 0xF4) : Color.FromArgb(0xD9, 0x20, 0x20, 0x24));
        Frame.BorderBrush = Brush(light ? Color.FromArgb(0x33, 0, 0, 0) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        var text = Brush(light ? Color.FromArgb(0xE6, 0, 0, 0) : Colors.White);
        var subtle = Brush(light ? Color.FromArgb(0x99, 0, 0, 0) : Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        QueryBox.Foreground = text;
        QueryBox.CaretBrush = text;
        Glass.Foreground = subtle;
        KeysHint.Foreground = subtle;
        Footer.Foreground = subtle;
        Divider.Background = Brush(light ? Color.FromArgb(0x1A, 0, 0, 0) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        Resources["RowText"] = text;
        Resources["RowSubtle"] = subtle;
        Resources["RowChip"] = Brush(light ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        Resources["RowHit"] = Brush(light ? Color.FromRgb(0x00, 0x5F, 0xB8) : Color.FromRgb(0x60, 0xCD, 0xFF));
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void ShowResults()
    {
        _results = SearchPalette.Rank(QueryBox.Text, _entries);
        Results.Items.Clear();
        foreach (var result in _results) Results.Items.Add(BuildRow(result));
        if (_results.Count > 0) Results.SelectedIndex = 0;
        var items = _results.Count(result => result.Entry is ItemEntry);
        Footer.Text = QueryBox.Text.Trim().Length == 0 ? "Type to search every fence and NeoFences' actions · Esc to close"
            : _results.Count == 0 ? "Nothing found"
            : $"{items} item{(items == 1 ? "" : "s")} · {_results.Count - items} action{(_results.Count - items == 1 ? "" : "s")} — ↑↓ to move, Esc to close";
    }

    /// <summary>Icon, name with the matched letters in the accent colour, and where it lives (or "Action").</summary>
    private ListBoxItem BuildRow(SearchResult result)
    {
        var icon = new Image { Width = RowIconDips, Height = RowIconDips, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        if (result.Entry is ItemEntry item)
        {
            if (!_views.TryGetValue(item.ItemRef, out var view))
            {
                view = new FenceItemView(item.ItemRef);
                _views[item.ItemRef] = view;
                _iconLoader.Request(view, (int)Math.Round(RowIconDips * VisualTreeHelper.GetDpi(this).DpiScaleX)); // off the UI thread
            }
            icon.SetBinding(Image.SourceProperty, new Binding(nameof(FenceItemView.Icon)) { Source = view });
        }
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 };
        name.SetResourceReference(TextBlock.ForegroundProperty, "RowText");
        var hits = result.Positions.ToHashSet();
        for (var index = 0; index < result.Entry.Name.Length;)
        {
            var hit = hits.Contains(index);
            var end = index;
            while (end < result.Entry.Name.Length && hits.Contains(end) == hit) end++;
            var run = new Run(result.Entry.Name[index..end]);
            if (hit)
            {
                run.FontWeight = FontWeights.SemiBold;
                run.SetResourceReference(TextElement.ForegroundProperty, "RowHit");
            }
            name.Inlines.Add(run);
            index = end;
        }
        var chipText = new TextBlock
        {
            Text = result.Entry is ItemEntry chipItem ? chipItem.Chip : "Action", FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220,
        };
        chipText.SetResourceReference(TextBlock.ForegroundProperty, "RowSubtle");
        var chip = new Border { Child = chipText, CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(10, 0, 0, 0) };
        chip.SetResourceReference(Border.BackgroundProperty, "RowChip");
        var row = new DockPanel { Margin = new Thickness(6, 3, 6, 3) };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(chip, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(chip);
        row.Children.Add(name);
        var container = new ListBoxItem { Content = row, Tag = result, Padding = new Thickness(8, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetName(container,
            result.Entry is ItemEntry spoken ? $"{result.Entry.Name}, in {spoken.Chip}" : $"{result.Entry.Name}, action");
        container.MouseLeftButtonUp += (_, _) => Pick(result, show: false);
        return container;
    }

    private void OnKeys(object sender, KeyEventArgs pressed)
    {
        switch (pressed.Key)
        {
            case Key.Escape:
                CloseOnce();
                break;
            case Key.Down or Key.Up or Key.PageDown or Key.PageUp when _results.Count > 0:
                var step = pressed.Key switch { Key.Down => 1, Key.Up => -1, Key.PageDown => 8, _ => -8 };
                Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + step, 0, _results.Count - 1);
                Results.ScrollIntoView(Results.SelectedItem);
                break;
            case Key.Enter when Results.SelectedItem is ListBoxItem { Tag: SearchResult chosen }:
                var show = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                if (show && chosen.Entry is not ItemEntry) show = false; // an action just runs
                if (show && _fencesHidden)
                {
                    Footer.Text = "Fences are hidden (paused or quick-hidden): Enter still opens the item.";
                    break;
                }
                Pick(chosen, show);
                break;
            default:
                return;
        }
        pressed.Handled = true;
    }

    private void Pick(SearchResult result, bool show)
    {
        CloseOnce();
        Picked?.Invoke(result, show);
    }

    /// <summary>Esc, Enter, a click elsewhere: closes once (Deactivated also fires while closing).</summary>
    private void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }
}
```

`src/NeoFences.App/FenceHost.Search.cs`:
```csharp
using NeoFences.Core.Input;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;
using NeoFences.Core.Search;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// The search palette (M15, spec 2026-10-04-search-palette-design): its hotkey, what it searches (the config, each
/// Portal's last listing, the library index) and what a pick does. It opens and selects; it never changes a file.
/// </summary>
public sealed partial class FenceHost
{
    private const int SearchHotkeyId = 3;
    private GlobalHotkey? _searchHotkey;
    private string? _searchHotkeyProblem; // why it is not registered (Settings shows it), null when it is
    private SearchWindow? _searchWindow;

    /// <summary>Each Portal's last listing at the level it shows, also for hidden Portal tabs (spec §2).</summary>
    private readonly Dictionary<string, IReadOnlyList<ItemInfo>> _portalListings = new(StringComparer.Ordinal);

    /// <summary>Registered unless a game is in front (released to it) or Settings records a hotkey; it also works while paused.</summary>
    private void UpdateSearchHotkey()
    {
        var wanted = !_gameMode && !_recordingHotkey;
        if (wanted == _searchHotkey is not null) return;
        if (_searchHotkey is not null)
        {
            _searchHotkey.Dispose();
            _searchHotkey = null;
            return;
        }
        _searchHotkey = TryRegisterHotkey(_config.Settings.SearchHotkey, SearchHotkeyId, "Search", out var problem);
        _searchHotkeyProblem = _searchHotkey is null ? problem : null;
        if (_searchHotkey is null) Log.Warning("search hotkey is off: {Problem}", problem);
    }

    /// <summary>Settings: a new search hotkey, kept only if Windows accepts it and it is not Peek's.</summary>
    private (bool Saved, string Message) SetSearchHotkey(string text)
    {
        if (!Hotkey.TryParse(text, out var parsed)) return (false, $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.");
        if (!parsed.IsSafeToRecord) return (false, $"{parsed.DisplayText} would stop working everywhere else (typing, Tab, closing windows). Use Alt or Win with a key, or an F-key.");
        var normalized = parsed.ToString();
        if (Hotkey.TryParse(_config.Settings.PeekHotkey, out var peek) && peek.ToString() == normalized)
            return (false, $"{parsed.DisplayText} is the Peek hotkey. Pick another combination.");
        if (normalized == _config.Settings.SearchHotkey && _searchHotkeyProblem is null) return (true, $"Search: {parsed.DisplayTextWith(LayoutKeyCap)}");
        _searchHotkey?.Dispose();
        _searchHotkey = null;
        var registration = TryRegisterHotkey(normalized, SearchHotkeyId, "Search", out var problem);
        if (registration is null)
        {
            UpdateSearchHotkey(); // the old one again
            return (false, problem);
        }
        registration.Dispose(); // proven free; UpdateSearchHotkey registers it whenever it is wanted
        _searchHotkeyProblem = null;
        _config = _config with { Settings = _config.Settings with { SearchHotkey = normalized } };
        UpdateSearchHotkey();
        SaveNow();
        return (true, $"Saved. Search: {parsed.DisplayTextWith(LayoutKeyCap)}");
    }

    private string SearchHotkeyDisplay =>
        Hotkey.TryParse(_config.Settings.SearchHotkey, out var hotkey) ? hotkey.DisplayTextWith(LayoutKeyCap) : _config.Settings.SearchHotkey;

    /// <summary>The hotkey: opens the palette on the monitor under the mouse; pressed again, closes it.</summary>
    private void ToggleSearch()
    {
        if (_searchWindow is { } open)
        {
            open.Close();
            return;
        }
        IReadOnlyList<SearchEntry> entries;
        try
        {
            entries = [.. SearchPalette.Entries(_config, _portalListings, _library, AppPaths.LibraryDirectory, VirtualItemName), .. ActionsNow()];
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "search palette could not list the fences"); // hard rule 7: no palette this time
            return;
        }
        var window = new SearchWindow(entries, _iconLoader, _lightTheme, fencesHidden: _paused || _quickHidden);
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        if (_monitors.Count > 0)
        {
            var monitor = FencePlacement.ContainingMonitor(new PixelRect(cursorX, cursorY, 1, 1), _monitors);
            window.SourceInitialized += (_, _) => window.PlaceOn(monitor); // placed before it shows: no flash elsewhere
        }
        window.Picked += OnSearchPicked;
        window.Closed += (_, _) => { if (_searchWindow == window) _searchWindow = null; };
        _searchWindow = window;
        window.Show();
        window.Activate(); // the hotkey gave NeoFences the foreground
    }

    /// <summary>A virtual item's name (Recycle Bin, This PC) as the shell gives it; null leaves it out.</summary>
    private static string? VirtualItemName(string itemRef)
    {
        try
        {
            return ShellItems.TryGetDisplayName(itemRef);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
    }

    private IReadOnlyList<ActionEntry> ActionsNow()
    {
        var snapshots = ListSnapshots();
        return SearchActions.For(
            snapshots: [.. snapshots.Where(entry => !entry.IsBeforeRestore).Take(10).Select(entry => (entry.Path, entry.Name))],
            canUndoRestore: snapshots.Any(entry => entry.IsBeforeRestore),
            hasLibrary: _config.Fences.Any(fence => fence.Source.Kind == FenceSourceKind.Library),
            paused: _paused);
    }

    private void OnSearchPicked(SearchResult result, bool show)
    {
        switch (result.Entry)
        {
            case ActionEntry action:
                RunSearchAction(action.ActionId);
                break;
            case ItemEntry item when show:
                ShowInFence(item);
                break;
            case ItemEntry item:
                OpenItem(item.ItemRef, ownerHandle: 0); // as a double-click in its fence (folders open in Explorer: no fence to browse in)
                SetPeek(false);
                break;
        }
    }

    /// <summary>Shift+Enter: the item's tab shows, a rolled-up box opens, the fences rise as in Peek, the item is selected and flashes.</summary>
    private void ShowInFence(ItemEntry item)
    {
        if (_paused || _config.Fences.All(fence => fence.Id != item.FenceId)) return;
        var box = FenceTabs.HostOf(_config, item.FenceId);
        if (!_windows.TryGetValue(box.Id, out var window)) return;
        if (window.FenceId != item.FenceId) SwitchTab(window, item.FenceId);
        SetPeek(true); // Esc or a click elsewhere ends it, as in Peek
        window.Reveal(item.ItemRef);
    }

    private void RunSearchAction(string actionId)
    {
        Log.Information("search palette action {Action}", actionId);
        switch (actionId)
        {
            case SearchActions.NewFence:
                SetQuickHidden(false);
                CreateFence();
                break;
            case SearchActions.NewPortal when _windows.Values.FirstOrDefault() is { } owner:
                CreatePortal(owner);
                break;
            case SearchActions.NewLibrary: CreateLibraryFence(); break;
            case SearchActions.TakeSnapshot: TakeSnapshot(); break;
            case SearchActions.UndoRestore when ListSnapshots().FirstOrDefault(entry => entry.IsBeforeRestore) is { } before:
                RestoreSnapshot(before.Path);
                break;
            case SearchActions.ApplyRules: ApplyRulesNow(); break;
            case SearchActions.RefreshLibrary: ScanLibrary(full: true); break;
            case SearchActions.Peek when !_paused: SetPeek(true); break;
            case SearchActions.PauseOrResume: SetPaused(!_paused); break;
            case SearchActions.Settings: OpenSettings(); break;
            case var restore when restore.StartsWith(SearchActions.RestorePrefix, StringComparison.Ordinal):
                RestoreSnapshot(restore[SearchActions.RestorePrefix.Length..]);
                break;
        }
    }
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
public sealed partial class FenceHost
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
    private readonly SnapshotStore _snapshots = new(Path.Combine(AppPaths.DataDirectory, "snapshots")); // M10
    private readonly HashSet<string> _loggedSnapshotProblems = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _specialIconsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // Rules wait until arrivals are quiet: a shortcut or file still being written reads wrong (M11 smoke X4, review M3).
    private readonly DispatcherTimer _filingTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly List<string> _pendingArrivals = [];
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
    // Snapshots (M10): "Restore snapshot" lists the newest few by id TrayRestoreFirst + index.
    private const int TrayTakeSnapshot = 7, TrayRestoreMenu = 8, TrayRestoreBefore = 9, TrayRestoreFirst = 100, TrayRestoreCount = 10;
    private const int TrayNewLibrary = 11; // M12
    private const int TraySnapshotsSettings = 12; // M13c: "More in Settings…" opens the Snapshots card
    private SettingsWindow? _settingsWindow; // M6b: one at a time

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
        _messages.WallpaperChanged += () => { if (Appearance.WallpaperAccent) UpdateAccents(); }; // M14
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
            if (ReleaseLibraryForRemoval(handle)) Log.Information("a drive the game library watches is being removed: released it");
        };
        _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
        _filingTimer.Tick += (_, _) => FilePendingArrivals();
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
        if (Appearance.WallpaperAccent) UpdateAccents(); // M14: the accent is read once at start, then on wallpaper changes
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
        _searchHotkey?.Dispose(); // M15
        _searchWindow?.Close();
        WatchWallpaperEngine(watch: false); // M14
        _libraryStopped = true; // also on session end: a library scan finishing now writes and re-arms nothing (M13a review)
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
        StopLibraryWatchers();
        _libraryTimer?.Stop();
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
        window.TabDragMoved += (screenX, screenY) =>
        {
            foreach (var other in _windows.Values) other.SetMergeHighlight(other != window && other.TitleRowContains(screenX, screenY));
        };
        window.TabColorRequested += color =>
        {
            _config = FenceTabs.SetColor(_config, window.FenceId, color);
            RefreshTabs(window);
            ScheduleSave();
        };
        window.CustomColorRequested += () => PickCustomColour(window); // M14
        window.TitleFontRequested += font => SetTitleFont(window, font);
        window.DetachTabRequested += () => DetachTab(window, window.FenceId, dropPoint: null);
        window.TabCycleRequested += step => CycleTab(window, step);
        window.SetTabs(FenceTabs.TabsOf(_config, box.Id), shown.Id);
        ApplyStyle(window); // M14
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
        window.NewLibraryRequested += CreateLibraryFence;
        window.RefreshLibraryRequested += () => ScanLibrary(full: true);
        window.StartupToggled += SetStartWithWindows;
        window.SettingsRequested += OpenSettings;
        window.RulesRequested += () => OpenRulesFor(window.FenceId);
        window.LabelModeRequested += labels => SetFenceLabels(window, labels);
        window.SetShortcutArrows(_config.Settings.ShowShortcutArrows);
        window.SetStartupChecked(_config.Settings.StartWithWindows);
        window.SortRequested += sort => SortFence(window, sort);
        window.OpenFolderRequested += () => { if (_portals.TryGetValue(window.FenceId, out var portal)) OpenItem(portal.Current, ownerHandle: window.Handle); };
        window.DragRequested += itemRefs =>
            ShellDragDrop.TryDrag(window.Handle, itemRefs, logFailure: failure => Log.Warning(failure, "could not start dragging {ItemRefs}", itemRefs),
                copyOnly: window.IsLibrary); // a game dragged out of the library is copied, never moved (M12)
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
        // A Portal tab that gets a window of its own (detached, or its host deleted) lists its folder now (final review I1).
        if (_portals.TryGetValue(shown.Id, out var shownPortal)) shownPortal.Refresh();
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
        FileNewItems(report.AddedToInbox);
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
            ReconcileDesktop(); // game mode returned early above: no deferral needed here (M13c, dead branch removed)
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
        var arrival = ArrivalOf(change); // before Apply: was the item fenced already?
        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
        RefreshWindows();
        ScheduleSave();
        if (arrival is not null) FileNewItems([arrival]);
        OnDesktopShortcutChange(change);
    }

    /// <summary>
    /// The item a change brings that rules may file (M11): a created item no fence holds yet (an attribute change on an
    /// existing item also arrives as "created", final review I1), or a download that just got its final name (I2).
    /// </summary>
    private string? ArrivalOf(DesktopChange change) => change switch
    {
        DesktopChange.Created created when !_config.Fences.Any(fence => fence.Items.Contains(created.ItemRef, ItemRef.Comparer)) => created.ItemRef,
        DesktopChange.Renamed renamed when Rules.IsDownloadRename(renamed.OldRef) => renamed.NewRef,
        _ => null,
    };

    /// <summary>
    /// Rules auto-sort (M11, spec §3): new items that landed in the Inbox go to the fence of the first rule they match.
    /// Their facts are read on the shell worker (a shortcut to an offline share can be slow); an item moved or deleted
    /// meanwhile stays put. Membership only: no file is touched.
    /// </summary>
    private void FileNewItems(IReadOnlyList<string> itemRefs)
    {
        if (itemRefs.Count == 0 || !_config.Rules.Any(rule => rule.Enabled)) return;
        _pendingArrivals.AddRange(itemRefs);
        _filingTimer.Stop(); // a burst (an unpack, an installer) is one read, 1.5 s after the last arrival
        _filingTimer.Start();
    }

    private void FilePendingArrivals()
    {
        _filingTimer.Stop();
        var inbox = _config.Inbox.Items.ToHashSet(ItemRef.Comparer);
        var arrivals = _pendingArrivals.Distinct(ItemRef.Comparer).Where(inbox.Contains).ToList(); // a safe-save memory or a drop placed it already
        _pendingArrivals.Clear();
        if (arrivals.Count == 0) return;
        ReadFactsThen(arrivals, facts =>
        {
            var stillInInbox = _config.Inbox.Items.ToHashSet(ItemRef.Comparer);
            var filed = Rules.File(_config, [.. facts.Where(fact => stillInInbox.Contains(fact.ItemRef))], DateTimeOffset.Now);
            if (ReferenceEquals(filed, _config)) return;
            var moved = _config.Inbox.Items.Except(filed.Inbox.Items, ItemRef.Comparer).ToList();
            Log.Information("rules filed {Count} new item(s): {ItemRefs}", moved.Count, moved);
            _config = filed;
            RefreshWindows();
            ScheduleSave();
        });
    }

    /// <summary>Reads item facts on the shell worker, then continues on the UI thread.</summary>
    private void ReadFactsThen(IReadOnlyList<string> itemRefs, Action<IReadOnlyList<ItemFacts>> then)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var facts = ItemFactsReader.Read(itemRefs, logFailure: (itemRef, failure) => Log.Warning(failure, "rules: {ItemRef} could not be read", itemRef));
            dispatcher.BeginInvoke(() => then(facts));
        });
    }

    /// <summary>
    /// Settings → "Apply rules now" (M11, spec §3): every desktop item, hand-placed ones too. "Before restore" is written
    /// first, so tray → "Undo the last restore" puts everything back; nothing moves without it.
    /// </summary>
    private void ApplyRulesNow()
    {
        var itemRefs = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items).ToList();
        ReadFactsThen(itemRefs, facts =>
        {
            var now = DateTimeOffset.Now;
            var moves = Rules.CountMoves(_config, facts, now);
            if (moves == 0)
            {
                _settingsWindow?.ShowRulesResult("Nothing to move: every item is where the rules want it.");
                return;
            }
            if (_snapshots.Save(Snapshots.Take(_config, name: $"Before applying rules ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
            {
                Log.Warning(_snapshots.LastFailure, "rules not applied: 'Before restore' could not be saved");
                _settingsWindow?.ShowRulesResult("Nothing moved: NeoFences could not save 'Before restore' first (see the log).");
                return;
            }
            _config = Rules.File(_config, facts, now);
            Log.Information("rules applied: {Count} item(s) moved", moves);
            SaveNow();
            RefreshWindows();
            RefreshSettings();
            var message = $"{moves} item{(moves == 1 ? "" : "s")} moved. Tray → Restore snapshot → Undo the last restore puts them back.";
            _settingsWindow?.ShowRulesResult(message);
            _trayIcon?.ShowBalloon("Rules applied", message);
        });
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
                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId),
                AcceptsDrops: () => !window.IsLibrary), // the library shows NeoFences' own shortcuts only (M12)
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
        if (window.IsLibrary)
        {
            ShowLibraryItemMenu(window, itemRefs, screenX, screenY, extended);
            return;
        }
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
        if (window.IsLibrary)
        {
            HideGames(itemRefs); // Delete in the library hides the game; its shortcut is NeoFences' own (M12)
            return;
        }
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
        if (Appearance.WallpaperAccent) ApplyStyle(window); // M14: moved to another monitor, another wallpaper
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
        RestyleAll(); // M14: the tone's strength and ink
        RefreshSettings();
    }

    private void OnDisplayChanged()
    {
        RefreshMonitors();
        ApplyLayout();
        if (Appearance.WallpaperAccent) UpdateAccents(); // M14: monitors and their wallpapers may have changed
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
        RestyleAll(); // M14: after placing, so each fence takes its own monitor's accent
        ScheduleSave();
    }

    /// <summary>A watcher per Portal fence, shown or not (a hidden Portal tab keeps watching, M9); gone ones end.</summary>
    private void EnsurePortals()
    {
        var portalFences = _config.Fences.Where(fence => fence.Source is { Kind: FenceSourceKind.Portal, Path: not null } or { Kind: FenceSourceKind.Library }).ToList();
        foreach (var goneId in _portals.Keys.Where(fenceId => portalFences.All(fence => fence.Id != fenceId)).ToList())
        {
            _portals[goneId].Dispose(); // the folder itself is never touched
            _portals.Remove(goneId);
        }
        foreach (var fence in portalFences.Where(fence => !_portals.ContainsKey(fence.Id)))
        {
            var fenceId = fence.Id;
            var root = fence.Source.Kind == FenceSourceKind.Library ? AppPaths.LibraryDirectory : fence.Source.Path!;
            if (fence.Source.Kind == FenceSourceKind.Library) TryCreateFolder(root);
            _portals[fenceId] = new PortalState(root, noticeOwner: _messages.Handle, show: items => ShowPortalTab(fenceId, items),
                logFailure: failure => Log.Warning(failure, "cannot watch Portal folder of {FenceId}", fenceId));
            if (_gameMode) _portals[fenceId].SetPaused(true);
        }
        UpdateLibrary(); // M12: the library scans while its fence exists
    }

    /// <summary>A Portal's listing goes to the window showing it; a hidden Portal tab re-lists when shown.</summary>
    private void ShowPortalTab(string fenceId, IReadOnlyList<ItemInfo>? items)
    {
        if (items is not null) _portalListings[fenceId] = items; // the search palette finds hidden Portal tabs too (M15)
        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } window) ShowPortal(window, items);
    }

    private void RefreshTabs(FenceWindow window)
    {
        window.SetTabs(FenceTabs.TabsOf(_config, window.BoxId), window.FenceId);
        ApplyStyle(window); // the shown tab's colour and font (M14)
    }

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
        var own = FenceWindowChrome.GetPixelRect(window.Handle);
        if (target is null && screenX >= own.X && screenX < own.X + own.Width && screenY >= own.Y && screenY < own.Y + own.Height)
        {
            return; // released over its own box: nothing happens (not a detach on top of itself; final review)
        }
        if (target == window)
        {
            var slot = window.TabIndexAt(screenX);
            var current = FenceTabs.TabsOf(_config, window.BoxId).ToList().FindIndex(tab => tab.Id == fenceId);
            _config = FenceTabs.Reorder(_config, fenceId, slot > current ? slot - 1 : slot);
        }
        else if (target is not null)
        {
            Log.Information("tab {FenceId} moved into the box of {TargetId}", fenceId, target.BoxId);
            // One tab moves, also when it is its box's host (final review C1).
            _config = FenceTabs.Merge(_config, movingFenceId: fenceId, targetFenceId: target.BoxId, insertAt: target.TabIndexAt(screenX), wholeBox: false);
        }
        else
        {
            DetachTab(window, fenceId, dropPoint: (screenX, screenY));
            return;
        }
        SyncBoxes();
    }

    /// <summary>A tab leaves its box: at the drop point (dragged out) or offset 40 DIP down-right of the box (menu), with the box's size.</summary>
    private void DetachTab(FenceWindow window, string fenceId, (int X, int Y)? dropPoint)
    {
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint
            || !_config.Layouts.TryGetValue(fingerprint, out var layout) || !layout.Fences.TryGetValue(window.BoxId, out var boxRect))
        {
            Log.Warning("tab {FenceId} not detached: its box has no place in the current layout", fenceId);
            return;
        }
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
        if (fence.Source.Kind == FenceSourceKind.Library)
        {
            window.ShowPortalMessage(null);
            window.SetLibraryArt(LibraryArt());
            window.SetItems(items is null ? [] : LibraryOrder(items)); // A–Z by game, only NeoFences' own shortcuts (M12)
            return;
        }
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
        UpdateSearchHotkey(); // M15: the same moments (game mode, recording in Settings)
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

    private GlobalHotkey? TryRegisterPeekHotkey(string text, out string problem) => TryRegisterHotkey(text, PeekHotkeyId, "Peek", out problem);

    /// <summary>A global hotkey (Peek, or the search palette since M15), or null with the reason a person reads.</summary>
    private GlobalHotkey? TryRegisterHotkey(string text, int hotkeyId, string what, out string problem)
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
        var registration = new GlobalHotkey(_messages.Handle, hotkeyId);
        if (registration.TryRegister(hotkey, (uint)virtualKey))
        {
            problem = "";
            Log.Information("{What} hotkey {Hotkey} registered", what, hotkey);
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
        if (Hotkey.TryParse(_config.Settings.SearchHotkey, out var search) && search.ToString() == normalized)
            return (false, $"{parsed.DisplayText} is the search hotkey. Pick another combination."); // M15
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
        window.SearchHotkeyChosen += text => // M15
        {
            var (saved, message) = SetSearchHotkey(text);
            RefreshSettings();
            window.ShowSearchHotkeyResult(saved, message);
        };
        window.RollupExpandChanged += SetRollupExpand;
        window.TakeSnapshotRequested += () => TakeSnapshot();
        window.RestoreSnapshotRequested += RestoreSnapshot;
        window.RenameSnapshotRequested += (path, name) =>
        {
            if (!_snapshots.Rename(path, name))
            {
                Log.Warning(_snapshots.LastFailure, "snapshot {Path} could not be renamed", path);
                SnapshotFailure("Snapshot not renamed", "The snapshot file could not be changed (see the log)."); // final review M1
            }
            RefreshSettings();
        };
        window.DeleteSnapshotRequested += DeleteSnapshot;
        window.RulesChanged += rules =>
        {
            _config = _config with { Rules = rules };
            Log.Information("rules changed: {Count} rule(s)", rules.Count);
            SaveNow();
            RefreshSettings();
        };
        window.ApplyRulesRequested += ApplyRulesNow;
        WireLibrarySettings(window);
        window.OpenSnapshotsRequested += () =>
        {
            try
            {
                Directory.CreateDirectory(_snapshots.Directory);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Log.Warning(failure, "snapshots folder {Folder} could not be created", _snapshots.Directory); // hard rule 7 (final review I3)
                return;
            }
            OpenItem(_snapshots.Directory, ownerHandle: 0);
        };
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
        window.AppearanceChanged += SetAppearance; // M14
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
        SearchHotkey: SearchHotkeyDisplay,
        SearchHotkeyActive: _searchHotkeyProblem is null,
        RollupExpand: _config.Settings.RollupExpand,
        GameModeEnabled: _config.Settings.GameMode,
        GameModeActive: _gameMode,
        Version: typeof(FenceHost).Assembly.GetName().Version?.ToString(3) ?? "",
        DataFolder: AppPaths.DataDirectory,
        DefaultLabels: _config.Settings.DefaultLabels,
        ShowShortcutArrows: _config.Settings.ShowShortcutArrows,
        Snapshots: ListSnapshots(),
        Rules: _config.Rules,
        RuleLines: [.. _config.Rules.Select(rule => Rules.Describe(rule, _config))],
        RuleFences: [.. _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).Select(fence => new RuleFence(fence.Id, fence.Title))],
        Library: LibrarySettingsView(),
        Appearance: AppearanceView()));

    /// <summary>Fence menu → "Rules for this fence…" (M11): Settings at Rules, a new rule for that fence.</summary>
    private void OpenRulesFor(string fenceId)
    {
        OpenSettings();
        _settingsWindow?.BeginNewRule(fenceId);
    }

    /// <summary>The Peek hotkey as a person reads it ("Ctrl+Shift+=", not "Ctrl+Shift+OemPlus").</summary>
    private string PeekHotkeyDisplay =>
        Hotkey.TryParse(_config.Settings.PeekHotkey, out var hotkey) ? hotkey.DisplayTextWith(LayoutKeyCap) : _config.Settings.PeekHotkey;

    /// <summary>The character the user's keyboard layout prints on an OEM key ("+" for OemPlus on a German keyboard, where the US name is "="), or null (M13c).</summary>
    private static string? LayoutKeyCap(string key) =>
        key.StartsWith("Oem", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<System.Windows.Input.Key>(key, ignoreCase: true, out var wpfKey)
            ? KeyboardLayout.CharacterOf(System.Windows.Input.KeyInterop.VirtualKeyFromKey(wpfKey))
            : null;

    /// <summary>Saves the arrangement now, named by date and time (M10); renamed in Settings if wanted.</summary>
    private void TakeSnapshot()
    {
        var now = DateTimeOffset.Now;
        var snapshot = Snapshots.Take(_config, name: $"Snapshot {now:d MMM HH:mm}", now: now);
        if (_snapshots.Save(snapshot) is { } path)
        {
            Log.Information("snapshot saved: {Path}", path);
            _trayIcon?.ShowBalloon("Snapshot saved", snapshot.Name);
            _settingsWindow?.ShowSnapshotNotice($"Saved \"{snapshot.Name}\".", failed: false);
        }
        else
        {
            Log.Warning(_snapshots.LastFailure, "snapshot could not be saved");
            SnapshotFailure("Snapshot not saved", "NeoFences could not write the snapshot file (see the log).");
        }
        RefreshSettings();
    }

    /// <summary>
    /// Puts a snapshot's arrangement back (M10, spec §3): only with a complete desktop listing, and only after "Before
    /// restore" was written, so the restore itself can be undone. One config change, saved at once.
    /// </summary>
    private void RestoreSnapshot(string path)
    {
        if (_snapshots.Load(path) is not { } snapshot)
        {
            Log.Warning(_snapshots.LastFailure, "snapshot {Path} could not be read", path);
            SnapshotFailure("Snapshot not restored", "The snapshot file could not be read.");
            return;
        }
        var listing = DesktopItems.Enumerate();
        if (listing.UnavailableFolders.Count > 0)
        {
            Log.Warning("snapshot not restored: desktop folders not readable {Folders}", listing.UnavailableFolders);
            SnapshotFailure("Snapshot not restored", "A Desktop folder cannot be read right now. Try again in a moment.");
            return;
        }
        var now = DateTimeOffset.Now;
        if (_snapshots.Save(Snapshots.Take(_config, name: $"Before restore ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
        {
            Log.Warning(_snapshots.LastFailure, "snapshot not restored: 'Before restore' could not be saved");
            SnapshotFailure("Snapshot not restored", "NeoFences could not save 'Before restore' first (see the log).");
            return;
        }
        Log.Information("restoring snapshot {Name} from {Path}", snapshot.Name, path);
        _config = Snapshots.Restore(_config, snapshot, listing.ItemRefs);
        SaveNow();
        SyncBoxes();
        // Windows that kept their fence still show its old title, icon size and labels (final review I1).
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
            window.Refresh(shown);
            RefreshPortal(window); // the Portal breadcrumb replaces the plain title again
        }
        _settingsWindow?.ShowSnapshotNotice($"Restored \"{snapshot.Name}\".", failed: false); // replaces an earlier failure line (final review M1)
        RefreshSettings();
    }

    /// <summary>The snapshot list; a damaged file or an unreadable folder is logged once, not on every tray open (final review I2).</summary>
    private IReadOnlyList<SnapshotEntry> ListSnapshots()
    {
        var snapshots = _snapshots.List();
        foreach (var (path, failure) in _snapshots.Problems.Where(problem => _loggedSnapshotProblems.Add(problem.Path)))
            Log.Warning(failure, "snapshot {Path} skipped: it cannot be read", path);
        return snapshots;
    }

    /// <summary>
    /// A snapshot failure, said where the user looks (M13c): Windows' warning notice, and the Settings card when it is open
    /// (a notice alone is easy to miss, and Do Not Disturb hides it).
    /// </summary>
    private void SnapshotFailure(string title, string reason)
    {
        _trayIcon?.ShowBalloon(title, reason, warning: true);
        _settingsWindow?.ShowSnapshotNotice($"{title}: {reason}", failed: true);
    }

    /// <summary>A snapshot file goes to the Recycle Bin, never deleted for good (hard rule 1).</summary>
    private void DeleteSnapshot(string path)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        // Windows' delete confirmation (when turned on) belongs to Settings, not to no window behind it (M13c).
        var owner = _settingsWindow is { } settings ? new WindowInteropHelper(settings).Handle : 0;
        _shellWorker.Run(() =>
        {
            var (started, refused, missing) = ShellFileOps.TryRecycle(LiveOwner(owner), [path]);
            if (!started || refused.Count > 0)
            {
                Log.Warning("snapshot {Path} could not be moved to the Recycle Bin", path);
                dispatcher.BeginInvoke(() => SnapshotFailure("Snapshot not deleted", "It was not moved to the Recycle Bin (see the log).")); // final review M1
            }
            if (missing.Count > 0) Log.Information("snapshot {Path} was already gone", path);
            dispatcher.BeginInvoke(RefreshSettings);
        });
    }

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
        if (_libraryDeferred)
        {
            _libraryDeferred = false;
            ScanLibrary(); // a game installed while playing shows up now (M12)
        }
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
        var arrivals = new List<string>();
        foreach (var change in _deferredDesktopChanges)
        {
            if (ArrivalOf(change) is { } arrival) arrivals.Add(arrival);
            (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
            OnDesktopShortcutChange(change);
        }
        _deferredDesktopChanges.Clear();
        RefreshWindows();
        ScheduleSave();
        FileNewItems(arrivals);
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
        var snapshots = ListSnapshots();
        var restorable = snapshots.Where(entry => !entry.IsBeforeRestore).Take(TrayRestoreCount).ToList();
        var beforeRestore = snapshots.FirstOrDefault(entry => entry.IsBeforeRestore);
        // One safe line per snapshot (M13c): no blank, multi-line, tabbed or 300-character entries.
        List<TrayMenuItem> restoreItems = [.. restorable.Select((entry, index) => new TrayMenuItem(TrayRestoreFirst + index, Snapshots.MenuLabel(entry, TimeZoneInfo.Local)))];
        if (beforeRestore is not null)
        {
            if (restoreItems.Count > 0) restoreItems.Add(TrayMenuItem.Separator); // never a separator first (M13c)
            restoreItems.Add(new TrayMenuItem(TrayRestoreBefore, "Undo the last restore"));
        }
        if (restoreItems.Count > 0) restoreItems.Add(TrayMenuItem.Separator);
        restoreItems.Add(new TrayMenuItem(TraySnapshotsSettings, "More in Settings…"));
        var chosen = TrayMenu.Show(_messages.Handle,
        [
            new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
            new TrayMenuItem(TrayNewLibrary, "New Game Library fence", Enabled: !_paused),
            new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
            new TrayMenuItem(TrayPeek, $"Peek\t{PeekHotkeyDisplay}", Checked: _peeking, Enabled: !_paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayTakeSnapshot, "Take snapshot"),
            new TrayMenuItem(TrayRestoreMenu, "Restore snapshot", Enabled: snapshots.Count > 0) { Children = restoreItems },
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
            case TrayNewLibrary: CreateLibraryFence(); break;
            case TrayQuickHide: SetQuickHidden(!_quickHidden); break;
            case TrayPeek: SetPeek(!_peeking); break;
            case TrayPause: SetPaused(!_paused); break;
            case TraySettings: OpenSettings(); break;
            case TraySnapshotsSettings:
                OpenSettings();
                _settingsWindow?.ShowSnapshotsCard();
                break;
            case TrayExit: ExitRequested?.Invoke(); break;
            case TrayTakeSnapshot: TakeSnapshot(); break;
            case TrayRestoreBefore when beforeRestore is not null: RestoreSnapshot(beforeRestore.Path); break;
            case >= TrayRestoreFirst and < TrayRestoreFirst + TrayRestoreCount when chosen - TrayRestoreFirst < restorable.Count:
                RestoreSnapshot(restorable[chosen - TrayRestoreFirst].Path);
                break;
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
        if (hotkeyId == SearchHotkeyId) // M15: also while paused (items still open); a game keeps the keys
        {
            if (!_gameMode) ToggleSearch();
            return;
        }
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

    private static void TryCreateFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Warning(failure, "cannot create {Folder}", folder); // the fence then shows "not available" (hard rule 7)
        }
    }
}
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
using NeoFences.Core.Appearance;
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
    private const int WmNcRightButtonUp = 0x00A5;
    private const int HitTestCaption = 2;
    private const double CornerRadiusDips = 8;
    private double _captionHeightDips = 30; // follows the title font (M14: 26 / 30 / 34 / 38)
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
    private bool _isLibrary; // the shown tab is the Game Library (M12): tiles, its own menu items
    private IReadOnlyDictionary<string, (string Path, bool IsPoster)> _libraryArt = new Dictionary<string, (string, bool)>();
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
    /// <summary>A dragged tab header is over this screen point (physical pixels); int.MinValue when the drag ended.</summary>
    public event Action<int, int>? TabDragMoved;
    public event Action<TabColor?>? TabColorRequested;
    /// <summary>Fence menu → Colour → Custom… (M14): the host opens Windows' colour picker.</summary>
    public event Action? CustomColorRequested;
    /// <summary>Fence menu → Title font (M14): the fence's new override, or null for "Use the default".</summary>
    public event Action<TitleFont?>? TitleFontRequested;
    public event Action? DetachTabRequested;
    /// <summary>Ctrl+Tab (+1) / Ctrl+Shift+Tab (-1).</summary>
    public event Action<int>? TabCycleRequested;

    /// <summary>The accent colours (M9), as Windows' own accent palette roughly offers them; defined in Core since M14.</summary>
    public static readonly IReadOnlyDictionary<TabColor, Color> TabColors =
        FenceLook.Swatches.ToDictionary(swatch => swatch.Key, swatch => ToColor(swatch.Value));

    private static Color ToColor(Argb colour) => Color.FromArgb(colour.A, colour.R, colour.G, colour.B);

    private static SolidColorBrush Frozen(Argb colour)
    {
        var brush = new SolidColorBrush(ToColor(colour));
        brush.Freeze();
        return brush;
    }

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
    /// <summary>Fence menu → "New Game Library fence" / "Refresh library" (M12).</summary>
    public event Action? NewLibraryRequested;
    public event Action? RefreshLibraryRequested;
    /// <summary>Fence menu → "Rules for this fence…" (M11): Settings opens at Rules with a new rule for this fence.</summary>
    public event Action? RulesRequested;
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
        BuildCustomColourAndFontMenus();
        DetachTabItem.Click += (_, _) => DetachTabRequested?.Invoke();
        TitleBar.SizeChanged += (_, _) => UpdateTabStripWidth();
        PreviewKeyDown += OnTabKeys;
        NewPortalItem.Click += (_, _) => NewPortalRequested?.Invoke();
        OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
        NewLibraryItem.Click += (_, _) => NewLibraryRequested?.Invoke();
        RefreshLibraryItem.Click += (_, _) => RefreshLibraryRequested?.Invoke();
        RulesItem.Click += (_, _) => RulesRequested?.Invoke();
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
        // A locked fence has no caption area (no WM_NCRBUTTONUP): its title is client area outside Body (final review I2).
        TitleBar.MouseRightButtonUp += (_, click) => { click.Handled = true; OpenFenceMenu(); };
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
            ReloadArt();
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
        _isLibrary = fence.Source.Kind == FenceSourceKind.Library;
        OpenFolderItem.Visibility = _isPortal ? Visibility.Visible : Visibility.Collapsed;
        RulesItem.Visibility = fence.Source.Kind == FenceSourceKind.Desktop ? Visibility.Visible : Visibility.Collapsed; // rules fill desktop fences only (M11)
        RefreshLibraryItem.Visibility = _isLibrary ? Visibility.Visible : Visibility.Collapsed;
        SortItem.Visibility = _isLibrary ? Visibility.Collapsed : Visibility.Visible; // the library is always A–Z
        DeleteItem.Header = _isLibrary ? "Delete fence (your games are not touched)"
            : _isPortal ? "Delete fence (the folder is not touched)" : "Delete fence (items go to the Inbox)";
        DeleteItem.Visibility = fence.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        // Desktop fences sort once (dragging keeps working); Portals keep the chosen order live, so it is checked.
        foreach (var sortItem in SortItem.Items.OfType<MenuItem>())
        {
            sortItem.IsCheckable = _isPortal;
            sortItem.IsChecked = _isPortal && (FenceSort)sortItem.Tag == fence.Sort;
        }
        _labelMode = fence.Labels;
        _fontOverride = fence.TitleFont;
        UpdateFontChecks();
        SetIconSize(fence.IconSize);
        SetLabelMode(fence.Labels);
    }

    /// <summary>The shown tab is the Game Library (M12).</summary>
    public bool IsLibrary => _isLibrary;

    /// <summary>The library's tile art by item ref: a 2:3 poster, or a logo shown centred (M12).</summary>
    public void SetLibraryArt(IReadOnlyDictionary<string, (string Path, bool IsPoster)> art)
    {
        _libraryArt = art;
        foreach (var view in _items) ApplyArt(view);
    }

    private void ApplyArt(FenceItemView view)
    {
        view.IsTile = _isLibrary;
        if (!_isLibrary || !_libraryArt.TryGetValue(view.ItemRef, out var art))
        {
            view.ArtPath = null;
            view.Art = null;
            view.ArtKind = "None";
            return;
        }
        if (view.ArtPath == art.Path) return;
        view.ArtPath = art.Path;
        var kind = art.IsPoster ? "Poster" : "Logo";
        // Decoded off the UI thread (a cover is a few hundred KB); only the newest request is shown.
        // Decoded at the tile's pixel width, not more: 300 covers would otherwise hold ~160 MB (M13b).
        var decodeWidth = Math.Max(48, (int)Math.Round(Math.Round(_iconSizeDips * 1.5) * VisualTreeHelper.GetDpi(this).DpiScaleX));
        Task.Run(() => LoadArt(art.Path, decodeWidth)).ContinueWith(loaded =>
        {
            if (view.ArtPath != art.Path || loaded.Result is not { } image) return;
            view.Art = image;
            view.ArtKind = kind;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static System.Windows.Media.ImageSource? LoadArt(string path, int decodeWidth)
    {
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // the file is not kept open
            image.DecodePixelWidth = decodeWidth;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Serilog.Log.Warning(failure, "could not load game art {Path}", path); // the icon stays
            return null;
        }
    }

    /// <summary>The shown fence changed in place (a snapshot restore, M10 final review I1): title, icon size, labels, menus.</summary>
    public void Refresh(Fence fence) => ApplyFence(fence);

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
        // The bar under a single fence's title comes from its look (ApplyStyle, M14); the menu shows the fence's choice.
        foreach (var colorItem in TabColorItem.Items.OfType<MenuItem>())
        {
            colorItem.IsChecked = active.CustomColor is not null ? Equals(colorItem.Tag, CustomColourTag) : Equals(colorItem.Tag, active.TabColor);
        }
        _fontOverride = active.TitleFont; // a font pick changes the shown tab's override: the next pick builds on it (final review I1)
        UpdateFontChecks();
        if (_style is { } style) ApplyStyle(style); // headers were rebuilt: their font and the bar follow the look again
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
        title.SetResourceReference(TextBlock.ForegroundProperty, "FenceTitleText");
        if (_style is { } style)
        {
            title.FontFamily = new FontFamily(style.Font.Family);
            title.FontSize = style.Font.Size;
            if (isActive) title.FontWeight = ToWeight(style.Font.Weight);
        }
        var bar = new Rectangle
        {
            Height = 3, RadiusX = 1.5, RadiusY = 1.5, Margin = new Thickness(6, 0, 6, 1), VerticalAlignment = VerticalAlignment.Bottom,
            // A custom colour marks its header too (final review M3, spec §3)
            Fill = (Argb.FromHex(tab.CustomColor) ?? (tab.TabColor is { } color ? FenceLook.Swatches[color] : null)) is { } mark ? Frozen(mark) : Brushes.Transparent,
            Opacity = isActive ? 1 : 0.55,
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
        // Esc cancels even when another app has the keyboard (fences rarely do, ADR-015; final review I3).
        if (FenceWindowChrome.IsKeyDown(0x1B))
        {
            EndTabGesture();
            return;
        }
        var (screenX, screenY) = FenceWindowChrome.GetCursorPosition();
        _tabGhost.Follow(screenX, screenY, VisualTreeHelper.GetDpi(this).DpiScaleX);
        TabDragMoved?.Invoke(screenX, screenY); // the title row it would join lights up
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
            ClickToOpen(); // a rolled-up box in click mode opens too, as a click on its title does (M13c)
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
        OpenFenceMenu();
    }

    /// <summary>The fence menu at the pointer (a header or the title right-clicked).</summary>
    private void OpenFenceMenu()
    {
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
        CloseTabGhost();
        Mouse.Capture(null);
    }

    private void CancelTabGesture()
    {
        if (_tabPressId is null) return;
        _tabPressId = null;
        CloseTabGhost();
    }

    private void CloseTabGhost()
    {
        if (_tabGhost is null) return;
        _tabGhost.Close();
        _tabGhost = null;
        TabDragMoved?.Invoke(int.MinValue, int.MinValue); // no more highlight
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
            ApplyArt(view);
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
        ReloadArt();
    }

    /// <summary>Covers are decoded at the tile's pixel width: a new icon size or monitor DPI decodes them again (final review I3).</summary>
    private void ReloadArt()
    {
        if (!_isLibrary) return;
        foreach (var view in _items)
        {
            view.ArtPath = null;
            ApplyArt(view);
        }
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

    private void ApplyCellWidth()
    {
        // Game Library tiles are 2:3, 1.5 × the icon size wide (M12).
        Resources["TileWidth"] = Math.Round(_iconSizeDips * 1.5);
        Resources["TileHeight"] = Math.Round(_iconSizeDips * 2.25);
        // With labels: room for two short words under small icons. Icons only: a tight grid.
        Resources["ItemWidth"] = _isLibrary ? Math.Round(_iconSizeDips * 1.5) + 12.0
            : _labelMode == LabelMode.Always ? LabelledItemWidth : _iconSizeDips + 12.0;
    }

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
            CaptionHeight = _locked ? 0 : _captionHeightDips,
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
    private int RolledUpHeightPx => (int)Math.Round((_captionHeightDips + 2) * VisualTreeHelper.GetDpi(this).DpiScaleY);

    // ponytail: polls the cursor every 100 ms while rolled up (no mouse-leave on a no-activate layered window when
    // the pointer leaves fast); in hover mode it also opens during a file drag, which is wanted.
    private void OnHoverTick()
    {
        if (Handle == 0 || !_rolledUp) return;
        if (_revealHold) // shown by the search palette (M15): open while the fences are raised
        {
            if (Peeking) return;
            _revealHold = false;
        }
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
        // The look's defaults until the host applies the fence's own (M14): v1.6's outline and title ink.
        Resources["FenceOutline"] = Resources["FenceBorder"];
        Resources["FenceTitleText"] = Ink(light ? (byte)0xE6 : (byte)0xFF);
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

    private FenceStyle? _style;
    private TitleFont? _fontOverride;
    private const string CustomColourTag = "custom";

    /// <summary>
    /// The fence's look (M14, spec §3): veil, outline, title ink, title strip, colour bar, title font and the title row's
    /// height (with Windows' caption area). Item labels keep the tone's ink (<see cref="ApplyTheme"/>).
    /// </summary>
    public void ApplyStyle(FenceStyle style)
    {
        _style = style;
        Resources["FenceVeil"] = Frozen(style.Veil);
        Resources["FenceOutline"] = Frozen(style.Border);
        Resources["FenceTitleText"] = Frozen(style.TitleText);
        TitleStripFill.Background = style.TitleStrip is { } strip ? Frozen(strip) : Brushes.Transparent;
        var single = _tabs.Count <= 1;
        TitleColorBar.Visibility = single && style.Bar is not null ? Visibility.Visible : Visibility.Collapsed;
        if (style.Bar is { } bar) TitleColorBar.Fill = Frozen(bar);
        TitleText.FontFamily = new FontFamily(style.Font.Family); // a font that is gone falls back to Segoe UI; the name stays
        TitleText.FontSize = style.Font.Size;
        TitleText.FontWeight = ToWeight(style.Font.Weight);
        foreach (var header in TabStrip.Children.OfType<Border>())
        {
            if (header.Child is not Grid { Children: [TextBlock title, ..] }) continue;
            title.FontFamily = TitleText.FontFamily;
            title.FontSize = style.Font.Size;
            if (Equals(header.Tag, FenceId)) title.FontWeight = TitleText.FontWeight;
        }
        if (style.TitleHeight != _captionHeightDips)
        {
            _captionHeightDips = style.TitleHeight;
            TitleRow.Height = new GridLength(style.TitleHeight);
            ApplyChrome();
            if (_rolledUp && !_expansion.Expanded && Handle != 0)
                FenceWindowChrome.SetPixelRect(Handle, FenceWindowChrome.GetPixelRect(Handle) with { Height = RolledUpHeightPx });
        }
    }

    private static FontWeight ToWeight(TitleWeight weight) => weight switch
    {
        TitleWeight.Regular => FontWeights.Normal,
        TitleWeight.Bold => FontWeights.Bold,
        _ => FontWeights.SemiBold,
    };

    /// <summary>Colour → Custom… and the Title font menu (M14). The font list is built the first time it opens.</summary>
    private void BuildCustomColourAndFontMenus()
    {
        TabColorItem.Items.Add(new Separator());
        var custom = new MenuItem { Header = "Custom…", Tag = CustomColourTag, IsCheckable = true };
        custom.Click += (_, _) => CustomColorRequested?.Invoke();
        TabColorItem.Items.Add(custom);

        var useDefault = new MenuItem { Header = "Use the default", IsCheckable = true, Tag = "default" };
        useDefault.Click += (_, _) => TitleFontRequested?.Invoke(null);
        var fonts = new MenuItem { Header = "Font" };
        fonts.Items.Add(new MenuItem { Header = "…" }); // a placeholder: WPF shows the arrow only for a menu with items
        fonts.SubmenuOpened += (_, _) =>
        {
            if (fonts.Items.Count > 1 || fonts.Items[0] is MenuItem { Tag: string }) { UpdateFontChecks(); return; }
            fonts.Items.Clear();
            foreach (var family in Fonts.SystemFontFamilies.Select(family => family.Source).Distinct().Order(StringComparer.CurrentCultureIgnoreCase))
            {
                var fontItem = new MenuItem
                {
                    Header = new TextBlock { Text = family, FontFamily = new FontFamily(family) }, Tag = family, IsCheckable = true,
                };
                fontItem.Click += (_, _) => TitleFontRequested?.Invoke((_fontOverride ?? new TitleFont(null, null, null)) with { Family = family });
                fonts.Items.Add(fontItem);
            }
            UpdateFontChecks();
        };
        var sizes = new MenuItem { Header = "Size" };
        foreach (var (size, name) in TitleFont.Sizes.Zip(["Small", "Normal", "Large", "Huge"]))
        {
            var sizeItem = new MenuItem { Header = name, Tag = size, IsCheckable = true };
            sizeItem.Click += (_, _) => TitleFontRequested?.Invoke((_fontOverride ?? new TitleFont(null, null, null)) with { Size = size });
            sizes.Items.Add(sizeItem);
        }
        var weights = new MenuItem { Header = "Weight" };
        foreach (var (weight, name) in new[] { (TitleWeight.Regular, "Regular"), (TitleWeight.SemiBold, "SemiBold"), (TitleWeight.Bold, "Bold") })
        {
            var weightItem = new MenuItem { Header = name, Tag = weight, IsCheckable = true };
            weightItem.Click += (_, _) => TitleFontRequested?.Invoke((_fontOverride ?? new TitleFont(null, null, null)) with { Weight = weight });
            weights.Items.Add(weightItem);
        }
        TitleFontItem.Items.Add(useDefault);
        TitleFontItem.Items.Add(new Separator());
        TitleFontItem.Items.Add(fonts);
        TitleFontItem.Items.Add(sizes);
        TitleFontItem.Items.Add(weights);
    }

    /// <summary>Ticks what this fence overrides; "Use the default" when it overrides nothing.</summary>
    private void UpdateFontChecks()
    {
        var items = TitleFontItem.Items.OfType<MenuItem>().ToList();
        if (items.Count < 4) return;
        items[0].IsChecked = _fontOverride is null;
        foreach (var fontItem in items[1].Items.OfType<MenuItem>()) fontItem.IsChecked = fontItem.Tag is string family && family == _fontOverride?.Family;
        foreach (var sizeItem in items[2].Items.OfType<MenuItem>()) sizeItem.IsChecked = Equals(sizeItem.Tag, _fontOverride?.Size);
        foreach (var weightItem in items[3].Items.OfType<MenuItem>()) weightItem.IsChecked = Equals(weightItem.Tag, _fontOverride?.Weight);
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
            case Key.F2 when selected.Count == 1 && !_isLibrary: // library shortcuts are named by their game (M12)
                BeginItemRename(selected[0].ItemRef);
                break;
            default:
                return;
        }
        args.Handled = true;
    }

    private bool _revealHold;

    /// <summary>
    /// The search palette's Shift+Enter (M15, spec §3): a rolled-up box opens (in either mode, while the fences are raised),
    /// the item is scrolled into view, selected and flashed twice, and the fence gets the keyboard. A Portal tab that is
    /// still listing gets one more try.
    /// </summary>
    public void Reveal(string itemRef)
    {
        if (_rolledUp && _expansion.Open()) AnimateHeight(_fullHeightPx);
        _revealHold = _rolledUp;
        Activate(); // the palette just had the foreground: the fence may take it
        void Select(bool retry)
        {
            var view = _items.FirstOrDefault(candidate => ItemRef.Comparer.Equals(candidate.ItemRef, itemRef));
            if (view is null)
            {
                if (retry) Dispatcher.BeginInvoke(async () => { await Task.Delay(500); Select(retry: false); });
                return;
            }
            ItemList.SelectedItems.Clear();
            ItemList.SelectedItem = view;
            ItemList.ScrollIntoView(view);
            ItemList.UpdateLayout();
            if (ItemList.ItemContainerGenerator.ContainerFromItem(view) is not ListBoxItem container) return;
            container.Focus();
            if (!AnimationsAllowed()) return;
            container.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(180))
            {
                AutoReverse = true, RepeatBehavior = new RepeatBehavior(2), FillBehavior = FillBehavior.Stop,
            });
        }
        Dispatcher.BeginInvoke(() => Select(retry: true), System.Windows.Threading.DispatcherPriority.Loaded);
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
            // The title is caption area: Windows would show its Move / Size / Close menu there. The fence menu instead (M13c).
            case WmNcRightButtonUp when wParam == HitTestCaption:
                OpenFenceMenu();
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

`src/NeoFences.App/SettingsWindow.xaml`:
```xml
<Window x:Class="NeoFences.App.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NeoFences settings" Width="600" Height="680" MinWidth="460" MinHeight="400"
        WindowStartupLocation="CenterScreen" ThemeMode="System">
    <!-- Fluent (WPF's built-in theme, ThemeMode="System"): follows Windows light/dark and the accent colour (spec §6).
         Set only on this window: the fences keep their own look. Changes apply at once; there is no OK button. -->
    <Window.Resources>
        <Style x:Key="SectionHeader" TargetType="TextBlock">
            <Setter Property="FontSize" Value="14" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Margin" Value="2,20,0,8" />
        </Style>
        <Style x:Key="Card" TargetType="Border">
            <Setter Property="Background" Value="{DynamicResource CardBackgroundFillColorDefaultBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource CardStrokeColorDefaultBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="6" />
            <Setter Property="Padding" Value="16,12" />
            <Setter Property="Margin" Value="0,0,0,4" />
        </Style>
        <Style x:Key="Description" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
            <Setter Property="FontSize" Value="12" />
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="Margin" Value="0,2,0,0" />
        </Style>
    </Window.Resources>
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="28,16,28,28">
            <TextBlock Text="Settings" FontSize="28" FontWeight="SemiBold" />

            <TextBlock Text="General" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="StartupBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Start with Windows" />
                    <StackPanel>
                        <TextBlock Text="Start with Windows" TextWrapping="Wrap" />
                        <TextBlock x:Name="StartupDescription" Style="{StaticResource Description}" Text="Your fences come back by themselves after a restart or a power cut." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="TakeoverBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Hide desktop icons" />
                    <StackPanel>
                        <TextBlock Text="Hide desktop icons" TextWrapping="Wrap" />
                        <TextBlock x:Name="TakeoverDescription" Style="{StaticResource Description}" Text="Show your desktop items only in fences. They are back on the desktop whenever NeoFences is paused or closed." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <TextBox x:Name="HotkeyBox" DockPanel.Dock="Right" Width="190" IsReadOnly="True" IsReadOnlyCaretVisible="False"
                                 VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Peek hotkey" />
                        <StackPanel>
                            <TextBlock Text="Peek hotkey" TextWrapping="Wrap" />
                            <TextBlock x:Name="HotkeyDescription" Style="{StaticResource Description}" Text="Shows your fences above all windows. Click the box, then press the new combination (with Alt or Win, or an F-key)." />
                        </StackPanel>
                    </DockPanel>
                    <!-- Assertive live region: screen readers announce a refused combination at once (M6b review carry-over). -->
                    <TextBlock x:Name="HotkeyStatus" Style="{StaticResource Description}" Margin="0,8,0,0" Visibility="Collapsed"
                               AutomationProperties.LiveSetting="Assertive" />
                </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <TextBox x:Name="SearchHotkeyBox" DockPanel.Dock="Right" Width="190" IsReadOnly="True" IsReadOnlyCaretVisible="False"
                                 VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Search hotkey" />
                        <StackPanel>
                            <TextBlock Text="Search hotkey" TextWrapping="Wrap" />
                            <TextBlock x:Name="SearchHotkeyDescription" Style="{StaticResource Description}" Text="Finds anything in your fences, and NeoFences' actions. Click the box, then press the new combination." />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock x:Name="SearchHotkeyStatus" Style="{StaticResource Description}" Margin="0,8,0,0" Visibility="Collapsed"
                               AutomationProperties.LiveSetting="Assertive" />
                </StackPanel>
            </Border>

            <TextBlock Text="Fences" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <ComboBox x:Name="LabelsBox" DockPanel.Dock="Right" Width="230" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Labels for new fences">
                            <ComboBoxItem Content="Always" Tag="Always" />
                            <ComboBoxItem Content="On hover (icons only)" Tag="OnHover" />
                        </ComboBox>
                        <StackPanel>
                            <TextBlock Text="Labels for new fences" TextWrapping="Wrap" />
                            <TextBlock x:Name="LabelsDescription" Style="{StaticResource Description}" Text="Icons only shows an item's name when you point at it or select it. Each fence can also choose in its own menu." />
                        </StackPanel>
                    </DockPanel>
                    <Button x:Name="LabelsApplyAllButton" Content="Apply to all fences" HorizontalAlignment="Left" Margin="0,10,0,0" />
                </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="ArrowsBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Show shortcut arrows" />
                    <StackPanel>
                        <TextBlock Text="Show shortcut arrows" TextWrapping="Wrap" />
                        <TextBlock x:Name="ArrowsDescription" Style="{StaticResource Description}" Text="The small arrow Windows draws on shortcut icons." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <ComboBox x:Name="RollupBox" DockPanel.Dock="Right" Width="230" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Rolled-up fences open">
                        <ComboBoxItem Content="When the mouse rests on them" Tag="Hover" />
                        <ComboBoxItem Content="When you click their title" Tag="Click" />
                    </ComboBox>
                    <StackPanel>
                        <TextBlock Text="Rolled-up fences open" TextWrapping="Wrap" />
                        <TextBlock x:Name="RollupDescription" Style="{StaticResource Description}" Text="Double-click a fence's title to roll it up to its title bar." />
                    </StackPanel>
                </DockPanel>
            </Border>

            <TextBlock Text="Appearance" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock Text="Colour style" />
                    <TextBlock Style="{StaticResource Description}" Text="How a fence shows its colour (fence menu → Colour)." />
                    <UniformGrid Rows="1" Margin="0,10,0,0" AutomationProperties.Name="Colour style">
                        <RadioButton x:Name="AccentEdgeStyle" GroupName="ColourStyle" Margin="0,0,8,0" AutomationProperties.Name="Accent edge">
                            <StackPanel>
                                <Border Width="64" Height="36" CornerRadius="4" BorderThickness="1" BorderBrush="#E84855" Background="#30101018" HorizontalAlignment="Left">
                                    <Rectangle Width="18" Height="3" Fill="#E84855" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="6,10,0,0" />
                                </Border>
                                <TextBlock Text="Accent edge" Margin="0,4,0,0" />
                            </StackPanel>
                        </RadioButton>
                        <RadioButton x:Name="TintedGlassStyle" GroupName="ColourStyle" Margin="0,0,8,0" AutomationProperties.Name="Tinted glass">
                            <StackPanel>
                                <Border Width="64" Height="36" CornerRadius="4" BorderThickness="1" BorderBrush="#B3E84855" Background="#59E84855" HorizontalAlignment="Left" />
                                <TextBlock Text="Tinted glass" Margin="0,4,0,0" />
                            </StackPanel>
                        </RadioButton>
                        <RadioButton x:Name="TitleStripStyle" GroupName="ColourStyle" AutomationProperties.Name="Title strip">
                            <StackPanel>
                                <Border Width="64" Height="36" CornerRadius="4" BorderThickness="1" BorderBrush="#40808080" Background="#30101018" HorizontalAlignment="Left">
                                    <Border Height="11" CornerRadius="3,3,0,0" Background="#BFE84855" VerticalAlignment="Top" />
                                </Border>
                                <TextBlock Text="Title strip" Margin="0,4,0,0" />
                            </StackPanel>
                        </RadioButton>
                    </UniformGrid>
                </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock Text="Background strength" />
                    <TextBlock x:Name="StrengthDescription" Style="{StaticResource Description}" />
                    <DockPanel Margin="0,8,0,0">
                        <TextBlock Text="Clear" DockPanel.Dock="Left" VerticalAlignment="Center" Margin="0,0,10,0" Style="{StaticResource Description}" />
                        <TextBlock Text="Solid" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="10,0,0,0" Style="{StaticResource Description}" />
                        <Slider x:Name="StrengthSlider" Minimum="0" Maximum="85" TickFrequency="5" IsSnapToTickEnabled="True" SmallChange="5" LargeChange="10"
                                VerticalAlignment="Center" AutomationProperties.Name="Background strength" />
                    </DockPanel>
                </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="AccentBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Colour fences from the wallpaper" />
                    <StackPanel>
                        <TextBlock Text="Colour fences from the wallpaper" TextWrapping="Wrap" />
                        <TextBlock Style="{StaticResource Description}" Text="Fences without a colour of their own take the wallpaper's colour (also Wallpaper Engine's)." />
                        <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                            <Border x:Name="AccentSwatch" Width="16" Height="16" CornerRadius="3" Margin="0,0,6,0" Visibility="Collapsed" VerticalAlignment="Center" />
                            <TextBlock x:Name="AccentSource" Style="{StaticResource Description}" VerticalAlignment="Center" Margin="0" Visibility="Collapsed" />
                        </StackPanel>
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock Text="Title font" />
                    <TextBlock Style="{StaticResource Description}" Text="Each fence can use its own (fence menu → Title font)." />
                    <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                        <ComboBox x:Name="FontFamilyBox" Width="200" IsEditable="False" AutomationProperties.Name="Title font" VirtualizingPanel.IsVirtualizing="True">
                            <ComboBox.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <VirtualizingStackPanel />
                                </ItemsPanelTemplate>
                            </ComboBox.ItemsPanel>
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding}" FontFamily="{Binding}" />
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                        <ComboBox x:Name="FontSizeBox" Width="110" Margin="8,0,0,0" AutomationProperties.Name="Title size">
                            <ComboBoxItem Content="Small" Tag="12" />
                            <ComboBoxItem Content="Normal" Tag="14" />
                            <ComboBoxItem Content="Large" Tag="17" />
                            <ComboBoxItem Content="Huge" Tag="20" />
                        </ComboBox>
                        <ComboBox x:Name="FontWeightBox" Width="110" Margin="8,0,0,0" AutomationProperties.Name="Title weight">
                            <ComboBoxItem Content="Regular" Tag="Regular" />
                            <ComboBoxItem Content="SemiBold" Tag="SemiBold" />
                            <ComboBoxItem Content="Bold" Tag="Bold" />
                        </ComboBox>
                    </StackPanel>
                </StackPanel>
            </Border>

            <TextBlock Text="Game mode" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <CheckBox x:Name="GameModeBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Go idle while a full-screen game runs" />
                        <StackPanel>
                            <TextBlock Text="Go idle while a full-screen game runs" TextWrapping="Wrap" />
                            <TextBlock x:Name="GameModeDescription" Style="{StaticResource Description}" Text="NeoFences removes its mouse hook and waits with background work, so games get every bit of input. Fences stay where they are." />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock x:Name="GameModeStatus" Style="{StaticResource Description}" Margin="0,8,0,0" />
                </StackPanel>
            </Border>

            <TextBlock Text="Snapshots" Style="{StaticResource SectionHeader}" />
            <Border x:Name="SnapshotsCard" Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="SnapshotsDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
                               Text="Save how your fences are arranged and put it back later. Restoring first saves 'Before restore', so it can be undone. Your files and settings are never changed." />
                    <ListBox x:Name="SnapshotList" MaxHeight="220" AutomationProperties.Name="Snapshots">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <DockPanel>
                                    <TextBlock DockPanel.Dock="Right" Text="{Binding When}" Opacity="0.7" Margin="16,0,0,0" />
                                    <TextBlock Text="{Binding Name}" TextTrimming="CharacterEllipsis" />
                                </DockPanel>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                        <ListBox.ItemContainerStyle>
                            <Style TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}">
                                <Setter Property="AutomationProperties.Name" Value="{Binding Spoken}" />
                            </Style>
                        </ListBox.ItemContainerStyle>
                    </ListBox>
                    <TextBox x:Name="SnapshotNameBox" Visibility="Collapsed" Margin="0,8,0,0" AutomationProperties.Name="New name for the snapshot" />
                    <TextBlock x:Name="SnapshotsStatus" Style="{StaticResource Description}" Margin="0,8,0,0" Visibility="Collapsed" />
                    <WrapPanel Margin="0,10,0,0">
                        <Button x:Name="TakeSnapshotButton" Content="Take snapshot" Margin="0,0,8,6" />
                        <Button x:Name="RestoreSnapshotButton" Content="Restore" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="RenameSnapshotButton" Content="Rename" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="DeleteSnapshotButton" Content="Delete" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="OpenSnapshotsButton" Content="Open snapshots folder" Margin="0,0,0,6" />
                    </WrapPanel>
                </StackPanel>
            </Border>

            <TextBlock Text="Rules" Style="{StaticResource SectionHeader}" />
            <Border x:Name="RulesCard" Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="RulesDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
                               Text="New Desktop items go to the fence of the first rule they match; the rest go to the Inbox. Rules only choose fences: files are never moved or changed." />
                    <ListBox x:Name="RuleList" MaxHeight="220" AutomationProperties.Name="Rules">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <DockPanel>
                                    <CheckBox IsChecked="{Binding Enabled, Mode=OneWay}" Tag="{Binding Id}" Checked="OnRuleToggled" Unchecked="OnRuleToggled"
                                              VerticalAlignment="Center" Margin="0,0,8,0" AutomationProperties.Name="{Binding ToggleName}" />
                                    <TextBlock Text="{Binding Text}" Opacity="{Binding Opacity}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center" />
                                </DockPanel>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                        <ListBox.ItemContainerStyle>
                            <Style TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}">
                                <Setter Property="AutomationProperties.Name" Value="{Binding Spoken}" />
                            </Style>
                        </ListBox.ItemContainerStyle>
                    </ListBox>
                    <StackPanel x:Name="RuleEditor" Visibility="Collapsed" Margin="0,10,0,0">
                        <WrapPanel>
                            <TextBlock Text="When" VerticalAlignment="Center" Margin="0,0,8,6" />
                            <ComboBox x:Name="RuleKindBox" Width="170" Margin="0,0,8,6" AutomationProperties.Name="Rule condition">
                                <ComboBoxItem Content="Type of file" Tag="Type" />
                                <ComboBoxItem Content="Game shortcut" Tag="Game" />
                                <ComboBoxItem Content="Name" Tag="Name" />
                                <ComboBoxItem Content="Date modified" Tag="Age" />
                                <ComboBoxItem Content="Size" Tag="Size" />
                            </ComboBox>
                            <ComboBox x:Name="RuleChoiceBox" Width="190" Margin="0,0,8,6" />
                            <TextBox x:Name="RuleTextBox" Width="150" Margin="0,0,8,6" />
                            <Button x:Name="RuleBrowseButton" Content="Choose…" Margin="0,0,8,6" Visibility="Collapsed" AutomationProperties.Name="Choose the game folder" />
                            <TextBlock x:Name="RuleUnitText" VerticalAlignment="Center" Margin="0,0,8,6" />
                        </WrapPanel>
                        <WrapPanel>
                            <TextBlock Text="Put in" VerticalAlignment="Center" Margin="0,0,8,6" />
                            <ComboBox x:Name="RuleFenceBox" Width="220" Margin="0,0,8,6" DisplayMemberPath="Title" AutomationProperties.Name="Put in fence" />
                            <Button x:Name="SaveRuleButton" Content="Save rule" Margin="0,0,8,6" IsDefault="False" />
                            <Button x:Name="CancelRuleButton" Content="Cancel" Margin="0,0,8,6" />
                        </WrapPanel>
                        <TextBlock x:Name="RuleHint" Style="{StaticResource Description}" />
                    </StackPanel>
                    <WrapPanel Margin="0,10,0,0">
                        <Button x:Name="AddRuleButton" Content="Add" Margin="0,0,8,6" />
                        <Button x:Name="EditRuleButton" Content="Edit" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="DeleteRuleButton" Content="Delete" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="MoveRuleUpButton" Content="Move up" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="MoveRuleDownButton" Content="Move down" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="ApplyRulesButton" Content="Apply rules now" Margin="0,0,0,6" />
                    </WrapPanel>
                    <TextBlock x:Name="RulesStatus" Style="{StaticResource Description}" Visibility="Collapsed" />
                </StackPanel>
            </Border>

            <TextBlock Text="Game Library" Style="{StaticResource SectionHeader}" />
            <Border x:Name="LibraryCard" Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="LibraryDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
                               Text="The Game Library fence lists every installed game it finds. Launcher games start through their launcher. Nothing is downloaded, and your games and files are never changed." />
                    <TextBlock Text="Game folders (each sub-folder is a game)" Margin="0,4,0,4" />
                    <ListBox x:Name="GameFolderList" MaxHeight="120" AutomationProperties.Name="Game folders" />
                    <WrapPanel Margin="0,8,0,0">
                        <Button x:Name="AddGameFolderButton" Content="Add folder…" Margin="0,0,8,6" />
                        <Button x:Name="RemoveGameFolderButton" Content="Remove" Margin="0,0,8,6" IsEnabled="False" />
                    </WrapPanel>
                    <TextBlock Text="Look for games in" Margin="0,8,0,4" />
                    <WrapPanel>
                        <CheckBox x:Name="SteamSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="EpicSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="GogSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="UbisoftSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="EaSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="BattleNetSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="XboxSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="FoldersSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="ShortcutsSourceBox" Margin="0,0,16,6" />
                    </WrapPanel>
                    <TextBlock Text="Hidden games" Margin="0,8,0,4" />
                    <TextBlock x:Name="HiddenGamesEmpty" Style="{StaticResource Description}" Text="None. Right-click a game → Hide from library." />
                    <ListBox x:Name="HiddenGameList" MaxHeight="120" AutomationProperties.Name="Hidden games" />
                    <WrapPanel Margin="0,8,0,0">
                        <Button x:Name="ShowGameAgainButton" Content="Show again" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="RefreshLibraryButton" Content="Refresh library now" Margin="0,0,8,6" />
                    </WrapPanel>
                    <TextBlock x:Name="LibraryStatus" Style="{StaticResource Description}" />
                </StackPanel>
            </Border>

            <TextBlock Text="About and logs" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="VersionText" />
                    <TextBlock x:Name="DataFolderText" Style="{StaticResource Description}" />
                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                        <Button x:Name="OpenLogsButton" Content="Open logs folder" Margin="0,0,8,0" />
                        <Button x:Name="OpenDataButton" Content="Open data folder" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </StackPanel>
    </ScrollViewer>
</Window>
```

`src/NeoFences.App/SettingsWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>What the settings window shows; the host builds it from the config and the run state.</summary>
/// <param name="PeekHotkey">As a person reads it (key caps).</param>
/// <param name="PeekHotkeyActive">False when Windows refused it (another app owns it): the window says so.</param>
public sealed record SettingsView(
    bool StartWithWindows, bool Takeover, string PeekHotkey, bool PeekHotkeyActive, string SearchHotkey, bool SearchHotkeyActive, RollupExpand RollupExpand,
    bool GameModeEnabled, bool GameModeActive, string Version, string DataFolder, LabelMode DefaultLabels, bool ShowShortcutArrows,
    IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> Snapshots, IReadOnlyList<Rule> Rules, IReadOnlyList<string> RuleLines,
    IReadOnlyList<RuleFence> RuleFences, LibraryView Library, AppearanceView Appearance);

/// <summary>One row of the Snapshots list (M10).</summary>
public sealed record SnapshotRow(string Path, string Name, string When)
{
    public string Spoken => $"{Name}, {When}";
}

/// <summary>
/// The settings window (spec §6, M6b): General, Fences, Game mode, About and logs. Every change is reported to the host
/// at once; the host applies, saves and calls <see cref="Show(SettingsView)"/> back with the result.
/// </summary>
public partial class SettingsWindow : Window
{
    private bool _updating; // filling the controls from the host must not report changes back

    public event Action<bool>? StartWithWindowsChanged;
    public event Action<bool>? TakeoverChanged;
    /// <summary>A new Peek hotkey was pressed in the box (text like "Ctrl+Alt+P"); answer with <see cref="ShowHotkeyResult"/>.</summary>
    public event Action<string>? PeekHotkeyChosen;
    /// <summary>A new search palette hotkey was pressed in its box (M15); answer with <see cref="ShowSearchHotkeyResult"/>.</summary>
    public event Action<string>? SearchHotkeyChosen;
    public event Action<RollupExpand>? RollupExpandChanged;
    public event Action<bool>? GameModeChanged;
    public event Action? OpenLogsRequested;
    public event Action? OpenDataRequested;
    public event Action? TakeSnapshotRequested;
    public event Action<string>? RestoreSnapshotRequested;
    public event Action<string, string>? RenameSnapshotRequested;
    public event Action<string>? DeleteSnapshotRequested;
    public event Action? OpenSnapshotsRequested;
    /// <summary>The hotkey box got (true) or lost (false) the keyboard: the host releases the Peek hotkey meanwhile (M6b review).</summary>
    public event Action<bool>? HotkeyRecording;
    public event Action<LabelMode>? DefaultLabelsChanged;
    public event Action<LabelMode>? LabelsAppliedToAll;
    public event Action<bool>? ShortcutArrowsChanged;

    public SettingsWindow()
    {
        InitializeComponent();
        // Checked/Unchecked, not Click: UI Automation (Narrator, Toggle) changes the box without a click (M6b smoke).
        OnToggled(StartupBox, isChecked => StartWithWindowsChanged?.Invoke(isChecked));
        OnToggled(TakeoverBox, isChecked => TakeoverChanged?.Invoke(isChecked));
        OnToggled(GameModeBox, isChecked => GameModeChanged?.Invoke(isChecked));
        RollupBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && RollupBox.SelectedItem is ComboBoxItem { Tag: string mode }) RollupExpandChanged?.Invoke(Enum.Parse<RollupExpand>(mode));
        };
        foreach (var (box, status) in new[] { (HotkeyBox, HotkeyStatus), (SearchHotkeyBox, SearchHotkeyStatus) }) // M15: the same recorder twice
        {
            box.PreviewKeyDown += OnHotkeyKeyDown;
            box.GotKeyboardFocus += (_, _) =>
            {
                HotkeyRecording?.Invoke(true); // so pressing the current combination is recorded, not Peek or the palette
                ShowHotkeyHint("Press the new combination… (Esc keeps the current one)", status);
            };
            box.LostKeyboardFocus += (_, _) =>
            {
                HotkeyRecording?.Invoke(false);
                if (status.Tag is null) status.Visibility = Visibility.Collapsed;
            };
        }
        OnToggled(ArrowsBox, isChecked => ShortcutArrowsChanged?.Invoke(isChecked));
        LabelsBox.SelectionChanged += (_, _) => { if (!_updating) DefaultLabelsChanged?.Invoke(SelectedLabels); };
        LabelsApplyAllButton.Click += (_, _) => LabelsAppliedToAll?.Invoke(SelectedLabels);
        // Screen readers read each setting's description with it (M6b review carry-over).
        foreach (var (control, description) in new (UIElement, TextBlock)[]
                 { (StartupBox, StartupDescription), (TakeoverBox, TakeoverDescription), (HotkeyBox, HotkeyDescription), (SearchHotkeyBox, SearchHotkeyDescription),
                   (LabelsBox, LabelsDescription), (ArrowsBox, ArrowsDescription), (RollupBox, RollupDescription), (GameModeBox, GameModeDescription) })
        {
            System.Windows.Automation.AutomationProperties.SetHelpText(control, description.Text);
        }
        OpenLogsButton.Click += (_, _) => OpenLogsRequested?.Invoke();
        OpenDataButton.Click += (_, _) => OpenDataRequested?.Invoke();
        TakeSnapshotButton.Click += (_, _) => TakeSnapshotRequested?.Invoke();
        OpenSnapshotsButton.Click += (_, _) => OpenSnapshotsRequested?.Invoke();
        RestoreSnapshotButton.Click += (_, _) => { if (SelectedSnapshot is { } row) RestoreSnapshotRequested?.Invoke(row.Path); };
        DeleteSnapshotButton.Click += (_, _) => { if (SelectedSnapshot is { } row) DeleteSnapshotRequested?.Invoke(row.Path); };
        RenameSnapshotButton.Click += (_, _) => BeginSnapshotRename();
        SnapshotList.SelectionChanged += (_, _) => UpdateSnapshotButtons();
        // Only a double-click on a row restores; the scrollbar or the empty space below the rows do nothing (M13c).
        SnapshotList.MouseDoubleClick += (_, click) =>
        {
            if (click.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(SnapshotList, source) is ListBoxItem
                && SelectedSnapshot is { } row) RestoreSnapshotRequested?.Invoke(row.Path);
        };
        SnapshotNameBox.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) EndSnapshotRename(commit: true, backToRow: true);
            else if (key.Key == Key.Escape) EndSnapshotRename(commit: false, backToRow: true);
            else return;
            key.Handled = true;
        };
        SnapshotNameBox.LostKeyboardFocus += (_, _) => EndSnapshotRename(commit: true, backToRow: false); // the user went elsewhere: focus stays there
        System.Windows.Automation.AutomationProperties.SetHelpText(SnapshotList, SnapshotsDescription.Text);
        InitializeRules();
        InitializeLibrary();
        InitializeAppearance();
    }

    private void OnToggled(CheckBox box, Action<bool> report)
    {
        box.Checked += (_, _) => { if (!_updating) report(true); };
        box.Unchecked += (_, _) => { if (!_updating) report(false); };
    }

    private LabelMode SelectedLabels => LabelsBox.SelectedIndex == 1 ? LabelMode.OnHover : LabelMode.Always;

    private SnapshotRow? SelectedSnapshot => SnapshotList.SelectedItem as SnapshotRow;
    private string? _renamingPath;

    private void UpdateSnapshotButtons()
    {
        var selected = SelectedSnapshot is not null;
        RestoreSnapshotButton.IsEnabled = selected;
        RenameSnapshotButton.IsEnabled = selected;
        DeleteSnapshotButton.IsEnabled = selected;
    }

    private void BeginSnapshotRename()
    {
        if (SelectedSnapshot is not { } row) return;
        _renamingPath = row.Path;
        SnapshotNameBox.Text = row.Name;
        SnapshotNameBox.Visibility = Visibility.Visible;
        SnapshotNameBox.Focus();
        SnapshotNameBox.SelectAll();
    }

    /// <param name="backToRow">Enter or Esc: the keyboard goes back to the row, not nowhere (M13c). Never after a click elsewhere.</param>
    private void EndSnapshotRename(bool commit, bool backToRow)
    {
        if (_renamingPath is not { } path) return;
        _renamingPath = null;
        SnapshotNameBox.Visibility = Visibility.Collapsed;
        if (backToRow) _focusSnapshotPath = path;
        if (commit && SnapshotNameBox.Text.Trim() is { Length: > 0 } name) RenameSnapshotRequested?.Invoke(path, name);
        else FocusSnapshotRow();
    }

    private string? _focusSnapshotPath;

    /// <summary>Puts the keyboard on the row of <see cref="_focusSnapshotPath"/> once the list is laid out.</summary>
    private void FocusSnapshotRow()
    {
        if (_focusSnapshotPath is not { } path) return;
        _focusSnapshotPath = null;
        Dispatcher.BeginInvoke(() =>
        {
            if (SnapshotList.ItemsSource is IEnumerable<SnapshotRow> rows && rows.FirstOrDefault(row => row.Path == path) is { } row
                && SnapshotList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item) item.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>A snapshot action's result, in the card (M13c): Settings-started actions say what happened where the user looks.</summary>
    public void ShowSnapshotNotice(string message, bool failed)
    {
        SnapshotsStatus.Text = message;
        SnapshotsStatus.Foreground = failed ? System.Windows.Media.Brushes.IndianRed : SecondaryText;
        SnapshotsStatus.Visibility = Visibility.Visible;
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(SnapshotsStatus)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    /// <summary>Tray → Restore snapshot → "More in Settings…": the Snapshots card in view (M13c).</summary>
    public void ShowSnapshotsCard() =>
        Dispatcher.BeginInvoke(() => SnapshotsCard.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);

    /// <summary>The snapshot list, keeping the selection where the same file is still listed.</summary>
    private void ShowSnapshots(IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> snapshots)
    {
        var selectedPath = SelectedSnapshot?.Path;
        var rows = snapshots.Select(entry => new SnapshotRow(entry.Path, entry.Name, entry.TakenAt.ToLocalTime().ToString("d MMM yyyy, HH:mm"))).ToList();
        if (SnapshotList.ItemsSource is IEnumerable<SnapshotRow> shown && shown.SequenceEqual(rows))
        {
            FocusSnapshotRow(); // a refresh with nothing new keeps the list; a pending focus is used now, never left for later (final review I1)
            return;
        }
        var hadFocus = SnapshotList.IsKeyboardFocusWithin;
        SnapshotList.ItemsSource = rows;
        SnapshotList.SelectedItem = rows.FirstOrDefault(row => row.Path == selectedPath);
        UpdateSnapshotButtons();
        if (hadFocus && _focusSnapshotPath is null && SelectedSnapshot is { } selected) _focusSnapshotPath = selected.Path;
        FocusSnapshotRow();
    }

    public void Show(SettingsView view)
    {
        ShowSnapshots(view.Snapshots);
        ShowRules(view);
        _updating = true;
        LabelsBox.SelectedIndex = view.DefaultLabels == LabelMode.OnHover ? 1 : 0;
        ArrowsBox.IsChecked = view.ShowShortcutArrows;
        if (!view.PeekHotkeyActive && !HotkeyBox.IsKeyboardFocused)
        {
            ShowHotkeyResult(saved: false, message: $"{view.PeekHotkey} is not active: Windows or another app owns it. Record another combination.");
        }
        StartupBox.IsChecked = view.StartWithWindows;
        TakeoverBox.IsChecked = view.Takeover;
        HotkeyBox.Text = view.PeekHotkey;
        SearchHotkeyBox.Text = view.SearchHotkey;
        if (!view.SearchHotkeyActive && !SearchHotkeyBox.IsKeyboardFocused)
        {
            ShowSearchHotkeyResult(saved: false, message: $"{view.SearchHotkey} is not active: Windows or another app owns it. Record another combination.");
        }
        RollupBox.SelectedIndex = view.RollupExpand == RollupExpand.Click ? 1 : 0;
        GameModeBox.IsChecked = view.GameModeEnabled;
        ShowLibrary(view.Library);
        ShowAppearance(view.Appearance);
        GameModeStatus.Text = !view.GameModeEnabled ? "Off: NeoFences stays fully active during games."
            : view.GameModeActive ? "Right now: idle, a full-screen app is in front." : "Right now: active (no full-screen app in front).";
        VersionText.Text = $"NeoFences {view.Version}";
        DataFolderText.Text = $"Settings, backups and logs: {view.DataFolder}";
        _updating = false;
    }

    /// <summary>The host's answer to <see cref="PeekHotkeyChosen"/>: saved, or why not (invalid, or taken by another app).</summary>
    public void ShowHotkeyResult(bool saved, string message) => ShowHotkeyStatus(saved, message, HotkeyStatus);

    /// <summary>The host's answer to <see cref="SearchHotkeyChosen"/> (M15).</summary>
    public void ShowSearchHotkeyResult(bool saved, string message) => ShowHotkeyStatus(saved, message, SearchHotkeyStatus);

    private void ShowHotkeyStatus(bool saved, string message, TextBlock status)
    {
        var changed = status.Text != message || status.Visibility != Visibility.Visible;
        status.Tag = saved ? null : "error"; // an error stays visible after the box loses focus
        status.Text = message;
        status.Foreground = saved ? SecondaryText : System.Windows.Media.Brushes.IndianRed;
        status.Visibility = Visibility.Visible;
        if (changed) AnnounceHotkeyStatus(status); // Settings refreshes often (game mode): say a warning once
    }

    private static void AnnounceHotkeyStatus(TextBlock status) =>
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(status)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);

    /// <summary>Fluent's secondary text colour (the grey a missing theme resource falls back to).</summary>
    private System.Windows.Media.Brush SecondaryText => TryFindResource("TextFillColorSecondaryBrush") as System.Windows.Media.Brush ?? SystemColors.GrayTextBrush;

    private void ShowHotkeyHint(string hint, TextBlock status)
    {
        status.Tag = null;
        status.Text = hint;
        status.Foreground = SecondaryText;
        status.Visibility = Visibility.Visible;
    }

    private TextBlock StatusOf(object box) => ReferenceEquals(box, SearchHotkeyBox) ? SearchHotkeyStatus : HotkeyStatus;

    /// <summary>Records a combination: modifiers alone only preview; Esc leaves the box; the first other key decides.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs pressed)
    {
        var key = pressed.Key == Key.System ? pressed.SystemKey : pressed.Key; // Alt combinations arrive as Key.System
        // Tab / Shift+Tab move on and Alt+F4 closes, as everywhere: never trap them in the box (M6b review I1).
        if (key == Key.Tab && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None) return;
        if (key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt) return;
        pressed.Handled = true;
        key = key switch { Key.ImeProcessed => pressed.ImeProcessedKey, Key.DeadCharProcessed => pressed.DeadCharProcessedKey, _ => key };
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        var modifiers = Keyboard.Modifiers;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            ShowHotkeyHint(string.Join("+", parts.Append("…")), StatusOf(sender));
            return;
        }
        parts.Add(key.ToString());
        if (ReferenceEquals(sender, SearchHotkeyBox)) SearchHotkeyChosen?.Invoke(string.Join("+", parts));
        else PeekHotkeyChosen?.Invoke(string.Join("+", parts));
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 444`.

- [ ] **Step 3: Commit** (the live check runs once, after the final review, with the user's go)

```powershell
git add src/NeoFences.App
git commit -m "feat: added the search palette window, its hotkey and show-in-fence"
```

---

### Task 3: Verification and docs

- [ ] **Step 1: Add section AB to the end of `docs/TEST-CHECKLIST.md`**

```markdown

## AB — Search palette (M15, ADR-037)
| ID | Steps | Expected |
|---|---|---|
| AB1 | Ctrl+Alt+F anywhere (also with an app in front) | the palette opens top-centre on the monitor under the mouse, with the keyboard in its box |
| AB2 | Type part of a desktop item's name; Enter | the item opens as from its fence; the palette closes |
| AB3 | Type a game's initials ("fh6"); Enter | the game starts through its launcher |
| AB4 | Search an item of a tab that is not shown, and one in a rolled-up fence; Shift+Enter | the tab shows / the fence opens, rises above the windows, the item is selected and flashes; Esc ends it |
| AB5 | Search a file in a Portal (also a hidden Portal tab) | it is found with "Box › Tab" in its chip |
| AB6 | "snap", "settings", "pause" | the actions; Enter runs them; there is never an "Exit" action |
| AB7 | "pokemon" for "Pokémon …"; a camel-case name ("GameCenter" by "gc") | found, matched letters highlighted |
| AB8 | Ctrl+Alt+F again while open; Esc; a click elsewhere | each closes it |
| AB9 | Pause NeoFences; Ctrl+Alt+F; Shift+Enter | the palette works; Shift+Enter says the fences are hidden; Enter still opens |
| AB10 | A full-screen game | Ctrl+Alt+F goes to the game (the palette does not open) |
| AB11 | Settings → Search hotkey: record a new one; record Peek's combination | the new one works; Peek's is refused with a message |
| AB12 | Another app owns Ctrl+Alt+F before NeoFences starts | Settings shows "not active"; everything else works |
| AB13 | Screen reader on the palette | the box is "Search fences and actions"; rows read "name, in fence" or "name, action" |
```

- [ ] **Step 2: Append ADR-037 to `docs/DECISIONS.md`**

```markdown

## ADR-037 — Search palette: in-memory entries, Core ranking, a second global hotkey (M15, v1.8.0)
**Date:** 2026-10-04 · **Status:** Accepted · **Spec:** `docs/superpowers/specs/2026-10-04-search-palette-design.md`

**Context.** Items hide in scrolled fences, hidden tabs, rolled-up fences, Portals and the Game Library. The user wants
one hotkey to find and open anything there, plus NeoFences' commands.

**Decision.**
- **Search what NeoFences already holds** (approach 1): desktop fence items from the config, each Portal's last listing
  (the host now keeps it per Portal fence, also for hidden tabs), the library index's games. No Windows Search, no folder
  walks, no deeper Portal levels.
- **Ranking in Core** (`SearchPalette.Rank`): prefix, then word starts (spaces, punctuation, camel case, digits after
  letters; runs like "forhor"), then contains, then letters in order; case and accents ignored; items before actions,
  shorter names, then A–Z; at most 50.
- **Actions** (`SearchActions.For`): the tray's commands by state; never "Exit".
- **Palette window**: a centred bar on the monitor under the mouse (user choice A); Enter opens like a double-click
  (folders open in Explorer: the palette has no fence to browse in), Shift+Enter shows the item in its fence (tab, roll-up
  open via `RollUpExpansion.Open`, Peek raise, select, flash); it closes on Esc, Enter, the hotkey again or losing focus.
- **Hotkey** `Settings.SearchHotkey` (Ctrl+Alt+F), registered like Peek with its own id; released to a game (game mode)
  and while Settings records; it works while paused. Peek's and the palette's combinations must differ. Schema 4.

**Consequences.** One more global hotkey is held while NeoFences runs (outside games). The palette never changes a file.
```

- [ ] **Step 3: Write `docs/research/m15-search-palette.md`**

```markdown
# M15 — Search palette: results

**Date:** 2026-10-04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.7.0 installed.
**Build:** M15 prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section AB · **Decision:** ADR-037.

## Live check on the prototype (the user's go; config and installed copy restored; Firefox PiP untouched)

| Check | Result |
|---|---|
| Hotkey registered | **Pass**: "Search hotkey Ctrl+Alt+F registered". |
| AB1 Ctrl+Alt+F (real keys, keyboard on empty desktop) | **Pass**: palette top-centre (640,206-1280,285 on 1920×1080), foreground. |
| Queries | **Pass**: "recycle" → Recycle Bin (Inbox); "fh6" → Forza Horizon 6 (Inbox); "snap" → Take snapshot (action); "zzqq" → "Nothing found". |
| AB4 Shift+Enter on Recycle Bin | **Pass**: palette closed; the Inbox rose above the windows with Recycle Bin selected (screenshot). |
| AB6 Enter on "Settings" | **Pass**: Settings opened; its Search hotkey box is there. |
| AB8 the hotkey again / Esc | **Pass**: each closes the palette. |
| Log | 0 warnings or errors. |

**Found on the first run and fixed:** closing the palette from the host (the hotkey again) deactivated it while closing, and
its own close handler called `Close()` a second time → an unhandled `InvalidOperationException`. The window now marks
itself closing from its `Closing` event. The watchdog then restarted the *test* build, the cleanup's start of the
installed copy exited ("already running") and the test build re-saved config.json as schema 4; the config was restored
from the backup and the test-made `pre-schema-4-config.json` removed; the smoke cleanup now stops every non-installed
NeoFences and verifies the installed one. Also learned: a posted `WM_HOTKEY` gives no foreground — the check presses the
real hotkey after focusing empty desktop.

## Core (test-first)

22 new tests (444 in all): ranking (prefix, word starts incl. camel case, digits and runs, contains, in order, accents,
spaces, order, limit, blank and unmatched queries, positions), entries (all fences, hidden tabs, Portal listings, the
library, chips, virtual names), display names, actions by state, schema 4, `RollUpExpansion.Open`.
```

- [ ] **Step 4: Update the other docs**
  - `ROADMAP.md`: the claim line becomes `- [x] M15 — done <date> (ADR-037, research/m15-search-palette.md)`, and add `- [ ] v1.8.0 release`.
  - `FEATURES.md`: "Search palette across all fences" → v1.8 (M15), done.
  - `ARCHITECTURE.md`: add an "M15 complete" line.
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 5: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 6: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-037, search palette checks and results, and recorded M15"
```
