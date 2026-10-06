# M36 — Fence settings and presets (0.23.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every fence can have its own look and behaviour (Fence settings…), a look can be reused in one click (five built-in presets and the owner's own), and a whole setup can be exported to one `.neofences` file, imported on another PC, and Settings reset to defaults — nothing changes for an existing setup until the owner picks something.

**Architecture:** Core (test-first) gains `OwnLook` on `Fence` (each part null = "Like all fences"), resolved by `FenceLook.Resolve` over Settings → Appearance (plus title alignment, title bar on hover and spacing in `FenceStyle`, `FenceLook.CellInset`); `LookPresets` (five built-ins, apply / save / delete / like all / matching) with own presets in `NeoFencesConfig.Presets`; `SettingsReset`; whole snapshots (`Snapshots.TakeWhole`: Settings, Games settings and presets ride along, so "Before import" / "Before reset" undo everything); `SetupFile` (the zip: manifest, config, items, the pictures they use; checks for a damaged file, a non-export, a newer NeoFences, oversized parts and names that leave their folder). The App gets `FenceSettingsWindow` (Settings' look, live changes), the fence menu entry, the fence's alignment / title-on-hover / spacing, and Export / Import / Reset in Settings, with `ReplaceSetup` and `ApplySettings` in the host (a snapshot restore shares it).

**Tech Stack:** .NET 10, C#, WPF (Fluent `ThemeMode` on NeoFences' own windows), `System.IO.Compression` (in the BCL: no new NuGet), xUnit; PowerShell for the live check.

**Spec:** `docs/superpowers/specs/2026-10-06-fence-settings-and-presets-design.md` (approved 2026-10-06). Decision: ADR-057 — added by Task 5.

## How this plan is written

Built in a scratch worktree (`m36-proto`), probed twice on a copy of the owner's data (owner's OK both times; screenshots
sent): run 1 — the fence menu's new entry, the window, every built-in preset on the Apps fence, Minimal's title bar hidden
and shown on hover, saving an own preset (its ✕), Like all fences again; run 2 — the window again, Export setup… through
Windows' save dialog (3 KB: manifest, config, items — the owner has no chosen pictures), Import setup… of that file (the
question, then "Before import" in the list and the notice), About → Reset settings (the question, then defaults). Fixed
after the probe: the title's size, weight and alignment boxes sat three in a row and cut "Like all fences (…)" — now one row
each; the Snapshots card no longer says settings are never changed. Replay-verified on a fresh worktree of `main` at
`dc3530f`: patch 1a alone fails to build (`error CS`: `OwnLook`, `LookPresets`, … do not exist — the RED), 1b makes 793
tests pass; 2a alone fails to build (`SetupFile` does not exist), 2b makes 808 pass; patches 3 and 4 build with 0 warnings
and 808 pass; the tree is identical to the prototype. Tasks 1–4 are **patches to apply** (`git apply --whitespace=nowarn
<file>`; if one does not apply, stop).

**Calls made while prototyping (ledger them as rulings at the task named):**
- Task 1: per-fence title fonts come back, in Fence settings (not the menu) — ADR-038's "one font" is amended by ADR-057;
  Spacing is a look value (a preset sets it, "Like all fences" clears it) though the window lists it under Items, as the
  spec does; title alignment and spacing have no Settings value, so "Like all fences" is today's look (Left, Normal);
  built-in strengths are Glass 25, Minimal 4, Title strip 60, Solid 85 (the maximum); Minimal uses Accent edge (a coloured
  fence keeps a thin edge), Compact leaves style and background to all fences; a saved own preset keeps the fence's icon
  size and labels too; saving under an own preset's name replaces it, a built-in's name is refused with a message;
  `TitleOnHover = false` is stored as none (one way to say "always"); snapshots taken before an import or a reset are
  whole (Settings, the Games settings, own presets) — every other snapshot is as before (ADR-030); the config schema stays
  5 (the new fields are optional; an older NeoFences ignores them).
- Task 2: only pictures the setup uses are exported or read (item icon pictures, chosen covers — found covers are found
  again online); an entry is taken only by its exact name `icons/<file>` or `covers/<file>`; JSON parts and pictures over
  16 MB / 32 MB are refused as damaged or skipped; a config from before the virtual items (schema < 5) is "damaged".
- Task 3: the title bar on hover keeps its row (it fades out, so nothing moves under the pointer) and always shows while
  rolled up, renamed, dragged or with the menu open; the hover poll (100 ms, as for roll-up) runs only for such fences;
  spacing is the inset around each element (Compact 0, Normal 2, Roomy 8 DIP) so elements keep their size; the
  settings window is per fence ("Tab settings…" on a tab) and refreshes when activated (the fence menu may have changed the
  fence); Settings' card / switch styles moved to `SettingsStyles.xaml`, shared by both windows.
- Task 4: Reset also returns the Games page's switches to their defaults (sources, online covers asked again, no "new games
  go to" fence) — folders, hidden games and chosen covers stay; import replaces the places too (this PC's other monitor
  setups stay in "Before import"); imported pictures replace pictures of the same name; "Before restore" is whole when the
  snapshot being restored is whole; the export is written on the UI thread (a ponytail: a few MB).

## Global Constraints

- Hard rules stand: NeoFences never modifies, moves, renames or deletes a user file (an export never holds one; an import
  writes only NeoFences' own `icons\` / `covers\` pictures); native desktop icons always come back (a reset or import that
  turns "Hide desktop icons" off shows them through the usual path); no new NuGet dependency (`System.IO.Compression` is in
  the BCL); Win32 only in `NeoFences.Shell` (the file dialogs are WPF's `Microsoft.Win32` wrappers, as Choose cover… uses);
  Core pure and test-first; shell failures logged, never a crash.
- Copy, exactly: fence menu "Fence settings…" (next to Rename; "Tab settings…" on a tab); window "<title> — fence settings";
  sections "Preset", "Look", "Items"; chips "Like all fences", "Glass", "Minimal", "Title strip", "Solid", "Compact";
  "Save this look as a preset…"; "Colour style", "Background", "Title" (Font, Size, Weight, Alignment), "Show the title bar",
  "Icon size", "Labels", "Layout", "Spacing" (Compact, Normal, Roomy); "Like all fences (…)", "This fence's own",
  "Like all fences again", "Close"; Settings → Snapshots "Export setup…", "Import setup…"; Settings → About "Reset settings
  to defaults…"; questions "Replace your fences, items and settings with this file's?" / "Your current setup is saved as a
  snapshot first." (Replace / Cancel) and "Reset every setting to its default?" (Reset / Cancel).
- The file is `.neofences` (a zip: `manifest.json`, `config.json`, `items.json`, `icons/`, `covers/`); never logs, the
  game-library scan or a user's file.
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer**; secret scan
  (`gmail|claude\.ai/artifact`) before each commit.
- Version stays `0.22.0` until the release step; the release is 0.23.0.

## Review Focus

1. **The title bar on hover**: it shows whenever the pointer is over the fence and while the fence is rolled up, renamed
   (F2), dragged or its menu is open; a fence is never left without a way to move, roll up or rename it; a box follows its
   front tab's setting; nothing jumps under the pointer. (Checklist AV4.)
2. **Import from another PC or an older setup**: fences land on this PC's monitors (places derived), items whose paths are
   not here show Missing, game items follow their games after the scan, the pictures come along; restoring "Before import"
   brings back everything — fences, items, Settings, the Games settings and own presets. (AV7, AV8.)
3. **Settings applied by an import, a reset or a whole snapshot take effect live**: Hide desktop icons (icons come back or
   hide — hard rule 2), the Start with Windows entry, the Peek hotkey (taken, or the old one kept and logged), the gesture
   switches, the look of every fence. (AV9.)
4. **A Fence settings window while its fence changes elsewhere**: the fence deleted (the window closes), its box's tab
   switched, a snapshot restored, View ▸ used from the menu (shown when the window is activated), two windows on two fences
   (a saved or deleted own preset shows in both). (AV5.)
5. **Spacing with covers, panels, widgets and Free layouts**: nothing clipped at Compact, covers and panels as before at
   Normal, Free elements stay in their cells at every spacing. (AV3.)

---

### Task 0: Worktree and baseline

- [ ] `git worktree add -b m36-fence-settings ..\neo_fences-m36 main` (main at `dc3530f` or later docs-only commits).
- [ ] `dotnet build` → 0 warnings; `dotnet test` → 774 passed.

### Task 1: Core — a fence's own look, presets, reset, whole snapshots

**Files:**
- Create: `tests/NeoFences.Core.Tests/Model/FenceSettingsTests.cs`, `src/NeoFences.Core/Model/OwnLook.cs`, `src/NeoFences.Core/Model/LookPresets.cs`
- Modify: `src/NeoFences.Core/Model/Fence.cs` (`Look`), `src/NeoFences.Core/Model/NeoFencesConfig.cs` (`Presets`),
  `src/NeoFences.Core/Appearance/FenceLook.cs` (own values, `FenceStyle.Align` / `TitleOnHover` / `Spacing`, `CellInset`),
  `src/NeoFences.Core/Config/ConfigNormalizer.cs` (`NormalizeLook`, presets), `src/NeoFences.Core/Config/ConfigJson.cs`
  (lenient `TitleAlign`, `Spacing`, `LabelMode`), `src/NeoFences.Core/Model/FenceEdits.cs` (`SetLook`),
  `src/NeoFences.Core/Model/Snapshots.cs` (whole snapshots)

**Interfaces:**
- Produces: `enum TitleAlign { Left, Centre, Right }`, `enum Spacing { Normal, Compact, Roomy }`; `record OwnLook
  { ColourStyle? ColourStyle; int? Strength; TitleFont? TitleFont; TitleAlign? TitleAlign; bool? TitleOnHover; Spacing?
  Spacing }`; `Fence.Look : OwnLook?`; `FenceStyle(…, TitleAlign Align = Left, bool TitleOnHover = false, Spacing Spacing =
  Normal)`; `FenceLook.CellInset(Spacing) → double` (0 / 2 / 8); `ConfigNormalizer.NormalizeLook(OwnLook?) → OwnLook?`;
  `record LookPreset { string Name; OwnLook Look; int? IconSize; LabelMode? Labels }`; `NeoFencesConfig.Presets`;
  `LookPresets.BuiltIn`, `.All(config)`, `.Apply(config, fenceId, preset)`, `.LikeAllFences(config, fenceId)`,
  `.NameProblem(name) → string?`, `.Save(config, fenceId, name)`, `.Delete(config, name)`, `.Matching(config, fence) →
  string?`, `.MaxNameLength = 40`; `FenceEdits.SetLook(config, fenceId, OwnLook?)`; `SettingsReset.Apply(config)`;
  `Snapshot.Settings` / `.Library` / `.Presets` (null in ordinary snapshots), `Snapshots.TakeWhole(config, items, name, now)`.

- [ ] **Step 1: Write the failing tests.** Write this patch to `m36-1a-tests.patch` and apply it:

```diff
diff --git a/tests/NeoFences.Core.Tests/Model/FenceSettingsTests.cs b/tests/NeoFences.Core.Tests/Model/FenceSettingsTests.cs
new file mode 100644
index 0000000..3e25dff
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Model/FenceSettingsTests.cs
@@ -0,0 +1,242 @@
+using NeoFences.Core.Appearance;
+using NeoFences.Core.Config;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Tests.Model;
+
+/// <summary>M36 (spec 2026-10-06-fence-settings-and-presets-design): a fence's own look, presets, reset, whole snapshots.</summary>
+public class FenceSettingsTests
+{
+    private static readonly AppearanceSettings Appearance = new()
+    {
+        StrengthDark = 30, StrengthLight = 60, ColourStyle = ColourStyle.AccentEdge, TitleFont = new("Segoe UI", 14, TitleWeight.SemiBold),
+    };
+
+    private static readonly Fence Red = Fence.Create("Apps") with { TabColor = TabColor.Red };
+
+    private static NeoFencesConfig ConfigWith(params Fence[] fences) => new() { Fences = fences };
+
+    // ---------- §1 the look a fence shows: its own values over Settings' ----------
+
+    [Fact]
+    public void NoOwnLook_IsLikeAllFences()
+    {
+        var style = FenceLook.Resolve(Appearance, Red, light: false, wallpaperAccent: null);
+        Assert.Equal(FenceLook.Resolve(Appearance, Red with { Look = new OwnLook() }, light: false, wallpaperAccent: null), style);
+        Assert.Equal((TitleAlign.Left, false, Spacing.Normal), (style.Align, style.TitleOnHover, style.Spacing));
+    }
+
+    [Theory]
+    [InlineData(false)]
+    [InlineData(true)]
+    public void OwnStrength_IsOneValueForBothModes(bool light)
+    {
+        var fence = Fence.Create("Plain") with { Look = new OwnLook { Strength = 40 } };
+        Assert.Equal(0x66, FenceLook.Resolve(Appearance, fence, light, wallpaperAccent: null).Veil.A);
+    }
+
+    [Fact]
+    public void OwnColourStyle_WinsOverSettings()
+    {
+        var strip = FenceLook.Resolve(Appearance, Red with { Look = new OwnLook { ColourStyle = ColourStyle.TitleStrip } }, light: false, wallpaperAccent: null);
+        Assert.NotNull(strip.TitleStrip);
+        Assert.Null(strip.Bar);
+    }
+
+    [Fact]
+    public void OwnTitleFont_OverridesOnlyItsOwnParts()
+    {
+        var fence = Red with { Look = new OwnLook { TitleFont = new TitleFont(null, 20, null), TitleAlign = TitleAlign.Centre, TitleOnHover = true, Spacing = Spacing.Roomy } };
+        var style = FenceLook.Resolve(Appearance, fence, light: false, wallpaperAccent: null);
+        Assert.Equal(new ResolvedTitleFont("Segoe UI", 20, TitleWeight.SemiBold), style.Font);
+        Assert.Equal(38, style.TitleHeight);
+        Assert.Equal((TitleAlign.Centre, true, Spacing.Roomy), (style.Align, style.TitleOnHover, style.Spacing));
+    }
+
+    [Theory]
+    [InlineData(Spacing.Compact, 0)]
+    [InlineData(Spacing.Normal, 2)]
+    [InlineData(Spacing.Roomy, 8)]
+    public void Spacing_IsTheCellsInset(Spacing spacing, double inset) => Assert.Equal(inset, FenceLook.CellInset(spacing));
+
+    // ---------- the normalizer and the file ----------
+
+    [Fact]
+    public void Normalizer_RepairsOddOwnValues_AndAnEmptyLookIsNone()
+    {
+        Assert.Null(ConfigNormalizer.NormalizeLook(new OwnLook { TitleFont = new TitleFont(" ", 15, null), TitleOnHover = false })); // nothing usable: like all fences
+        var odd = new OwnLook
+        {
+            ColourStyle = (ColourStyle)7, Strength = 400, TitleFont = new TitleFont("  ", 13, (TitleWeight)9), TitleAlign = (TitleAlign)5,
+            TitleOnHover = false, Spacing = (Spacing)(-1),
+        };
+        var config = ConfigNormalizer.Normalize(ConfigWith(Red with { Look = odd }, Fence.Create("Clean") with { Look = new OwnLook { Strength = -5 } }));
+        Assert.Equal(new OwnLook { Strength = AppearanceSettings.MaxStrength }, config.Fences[0].Look); // only the strength was usable
+        Assert.Equal(new OwnLook { Strength = 0 }, config.Fences[1].Look);
+    }
+
+    [Fact]
+    public void OwnLookAndPresets_SurviveTheFile_AndTyposAreRepaired()
+    {
+        var look = new OwnLook { ColourStyle = ColourStyle.TintedGlass, Strength = 25, TitleFont = new TitleFont("Bahnschrift", null, TitleWeight.Bold), TitleAlign = TitleAlign.Right, TitleOnHover = true, Spacing = Spacing.Compact };
+        var config = ConfigWith(Red with { Look = look }) with { Presets = [new LookPreset { Name = "Dark", Look = look, IconSize = 64, Labels = LabelMode.OnHover }] };
+        var back = ConfigNormalizer.Normalize(ConfigJson.Deserialize(ConfigJson.Serialize(config)));
+        Assert.Equal(look, back.Fences[0].Look);
+        Assert.Equal(config.Presets[0], back.Presets[0]);
+
+        var typo = ConfigJson.Serialize(config).Replace("\"compact\"", "\"cosy\"").Replace("\"right\"", "\"middle\"").Replace("\"onHover\"", "\"sometimes\"");
+        var repaired = ConfigNormalizer.Normalize(ConfigJson.Deserialize(typo));
+        Assert.Null(repaired.Fences[0].Look!.Spacing);
+        Assert.Null(repaired.Fences[0].Look!.TitleAlign);
+        Assert.Null(repaired.Presets[0].Labels);
+    }
+
+    [Fact]
+    public void Normalizer_KeepsOwnPresetsWithUsableNamesOnly()
+    {
+        var config = ConfigNormalizer.Normalize(new NeoFencesConfig
+        {
+            Presets =
+            [
+                new LookPreset { Name = "  Mine  ", IconSize = 50, Labels = (LabelMode)4 },
+                new LookPreset { Name = "MINE" },          // the same name again
+                new LookPreset { Name = "glass" },         // a built-in's name
+                new LookPreset { Name = " \t " },          // blank
+                null!,
+                new LookPreset { Name = new string('x', 80), Look = null! },
+            ],
+        });
+        Assert.Equal(["Mine", new string('x', LookPresets.MaxNameLength)], config.Presets.Select(preset => preset.Name));
+        Assert.Equal(new LookPreset { Name = "Mine" }, config.Presets[0]); // odd icon size and labels dropped
+        Assert.Equal(new OwnLook(), config.Presets[1].Look);
+    }
+
+    // ---------- §2 presets ----------
+
+    [Fact]
+    public void BuiltIns_AreTheFiveFromTheSpec()
+    {
+        Assert.Equal(["Glass", "Minimal", "Title strip", "Solid", "Compact"], LookPresets.BuiltIn.Select(preset => preset.Name));
+        var minimal = LookPresets.BuiltIn[1];
+        Assert.True(minimal.Look.TitleOnHover);
+        Assert.Equal(LabelMode.OnHover, minimal.Labels);
+        var compact = LookPresets.BuiltIn[4];
+        Assert.Equal((32, LabelMode.OnHover, Spacing.Compact), (compact.IconSize, compact.Labels, compact.Look.Spacing));
+        // Every built-in survives the normalizer unchanged (none of them is "like all fences").
+        foreach (var preset in LookPresets.BuiltIn) Assert.Equal(preset.Look, ConfigNormalizer.NormalizeLook(preset.Look));
+    }
+
+    [Fact]
+    public void Apply_CopiesTheLook_KeepsTheColour_AndSetsIconSizeAndLabelsOnlyWhereThePresetHasThem()
+    {
+        var fence = Red with { IconSize = 64, Labels = LabelMode.Always, Look = new OwnLook { TitleAlign = TitleAlign.Right } };
+        var config = LookPresets.Apply(ConfigWith(fence), fence.Id, LookPresets.BuiltIn[4]); // Compact
+        var compact = config.Fences[0];
+        Assert.Equal((32, LabelMode.OnHover, TabColor.Red), (compact.IconSize, compact.Labels, compact.TabColor));
+        Assert.Equal(new OwnLook { Spacing = Spacing.Compact }, compact.Look); // the whole own look is the preset's
+
+        var glass = LookPresets.Apply(config, fence.Id, LookPresets.BuiltIn[0]).Fences[0];
+        Assert.Equal(32, glass.IconSize); // Glass has no icon size: the fence keeps its own
+        Assert.Equal(LabelMode.Always, glass.Labels);
+        Assert.Equal("Glass", LookPresets.Matching(config with { Fences = [glass] }, glass));
+    }
+
+    [Fact]
+    public void LikeAllFences_DropsTheOwnLook_ButKeepsIconSizeLabelsAndLayout()
+    {
+        var fence = Red with { IconSize = 96, Labels = LabelMode.OnHover, Layout = FenceLayout.Free, Look = new OwnLook { Strength = 50 } };
+        var cleared = LookPresets.LikeAllFences(ConfigWith(fence), fence.Id).Fences[0];
+        Assert.Null(cleared.Look);
+        Assert.Equal((96, LabelMode.OnHover, FenceLayout.Free), (cleared.IconSize, cleared.Labels, cleared.Layout));
+        Assert.Null(LookPresets.Matching(ConfigWith(cleared), cleared));
+    }
+
+    [Fact]
+    public void SetLook_StoresAnEmptyLookAsNone()
+    {
+        var config = FenceEdits.SetLook(ConfigWith(Red), Red.Id, new OwnLook { TitleOnHover = false });
+        Assert.Null(config.Fences[0].Look);
+        Assert.Equal(new OwnLook { Spacing = Spacing.Roomy }, FenceEdits.SetLook(config, Red.Id, new OwnLook { Spacing = Spacing.Roomy }).Fences[0].Look);
+    }
+
+    [Fact]
+    public void Save_KeepsTheFencesLook_ReplacesAnOwnPresetOfTheSameName_AndRefusesBadNames()
+    {
+        var fence = Red with { IconSize = 64, Labels = LabelMode.OnHover, Look = new OwnLook { Strength = 70 } };
+        var config = LookPresets.Save(ConfigWith(fence), fence.Id, "  My dark look ");
+        Assert.Equal(new LookPreset { Name = "My dark look", Look = new OwnLook { Strength = 70 }, IconSize = 64, Labels = LabelMode.OnHover }, config.Presets.Single());
+        Assert.Equal("My dark look", LookPresets.Matching(config, fence));
+
+        var changed = config.WithFence(fence with { Look = new OwnLook { Strength = 10 } });
+        config = LookPresets.Save(changed, fence.Id, "my DARK look");
+        Assert.Equal(10, config.Presets.Single().Look.Strength); // replaced, not added
+        Assert.Equal("my DARK look", config.Presets.Single().Name);
+
+        Assert.Equal("Type a name.", LookPresets.NameProblem("   "));
+        Assert.Equal("“Solid” is a built-in preset: choose another name.", LookPresets.NameProblem(" solid"));
+        Assert.Null(LookPresets.NameProblem("Solid 2"));
+        Assert.Same(config, LookPresets.Save(config, fence.Id, "Glass"));
+        Assert.Same(config, LookPresets.Save(config, fence.Id, ""));
+    }
+
+    [Fact]
+    public void Delete_RemovesOnlyAnOwnPreset_AndTheFencesKeepTheirLook()
+    {
+        var fence = Red with { Look = new OwnLook { Strength = 70 } };
+        var config = LookPresets.Save(ConfigWith(fence), fence.Id, "Mine");
+        config = LookPresets.Delete(config, "MINE");
+        Assert.Empty(config.Presets);
+        Assert.Equal(new OwnLook { Strength = 70 }, config.Fences[0].Look); // presets are copies, not links
+        Assert.Same(config, LookPresets.Delete(config, "Glass"));
+        Assert.Equal(LookPresets.BuiltIn, LookPresets.All(config));
+    }
+
+    // ---------- §3 reset and the whole snapshot ----------
+
+    [Fact]
+    public void ResetSettings_GivesEveryPageItsDefaults_AndKeepsFencesItemsPresetsAndTheGamesData()
+    {
+        var fence = Red with { Look = new OwnLook { Strength = 70 } };
+        var config = ConfigWith(fence) with
+        {
+            Settings = new Settings { HideDesktopIcons = true, PeekHotkey = "Ctrl+Q", AutoUpdate = false, Appearance = Appearance with { WallpaperAccent = true } },
+            Presets = [new LookPreset { Name = "Mine" }],
+            Library = new LibrarySettings
+            {
+                Folders = ["D:\\Games"], Hidden = ["steam:1"], CoverChoices = new Dictionary<string, string> { ["steam:2"] = "choice.jpg" },
+                Sources = new LibrarySources { Steam = false }, OnlineArt = true, NewGamesFence = fence.Id,
+            },
+        };
+        var reset = SettingsReset.Apply(config);
+        Assert.Equal(new Settings(), reset.Settings);
+        Assert.Equal(config.Fences, reset.Fences);
+        Assert.Equal(config.Presets, reset.Presets);
+        Assert.Equal((config.Library.Folders, config.Library.Hidden, config.Library.CoverChoices), (reset.Library.Folders, reset.Library.Hidden, reset.Library.CoverChoices));
+        Assert.Equal((new LibrarySources(), (bool?)null, (string?)null), (reset.Library.Sources, reset.Library.OnlineArt, reset.Library.NewGamesFence));
+    }
+
+    [Fact]
+    public void AWholeSnapshot_PutsSettingsGamesAndPresetsBack_AnOrdinaryOneLeavesThem()
+    {
+        var fence = Red;
+        var before = ConfigWith(fence) with
+        {
+            Settings = new Settings { PeekHotkey = "Ctrl+Q" }, Presets = [new LookPreset { Name = "Mine" }], Library = new LibrarySettings { Folders = ["D:\\Games"] },
+        };
+        var items = new ItemsDocument().With(fence.Id, [VirtualItem.Create("C:\\tools\\a.exe")]);
+        var whole = Snapshots.TakeWhole(before, items, "Before import", DateTimeOffset.UnixEpoch);
+        var ordinary = Snapshots.Take(before, items, "Plain", DateTimeOffset.UnixEpoch);
+        Assert.Null(ordinary.Settings);
+
+        var imported = new NeoFencesConfig { Fences = [Fence.Create("Other")], Settings = new Settings { PeekHotkey = "Ctrl+W" } };
+        var (restored, restoredItems) = Snapshots.Restore(imported, ConfigJson.DeserializeSnapshot(ConfigJson.SerializeSnapshot(whole)));
+        Assert.Equal("Ctrl+Q", restored.Settings.PeekHotkey);
+        Assert.Equal(["Mine"], restored.Presets.Select(preset => preset.Name));
+        Assert.Equal(["D:\\Games"], restored.Library.Folders);
+        Assert.Equal([fence.Id], restored.Fences.Select(restoredFence => restoredFence.Id));
+        Assert.Single(restoredItems.Of(fence.Id));
+
+        Assert.Equal("Ctrl+W", Snapshots.Restore(imported, ordinary).Config.Settings.PeekHotkey);
+    }
+}
```

- [ ] **Step 2: Run them.** `dotnet test tests/NeoFences.Core.Tests` → Expected: build fails with `error CS` lines (`OwnLook`, `LookPresets`, `TitleAlign`, … do not exist).
- [ ] **Step 3: Implement.** Write this patch to `m36-1b-core.patch` and apply it:

```diff
diff --git a/src/NeoFences.Core/Appearance/FenceLook.cs b/src/NeoFences.Core/Appearance/FenceLook.cs
index 38b2ad0..0d991de 100644
--- a/src/NeoFences.Core/Appearance/FenceLook.cs
+++ b/src/NeoFences.Core/Appearance/FenceLook.cs
@@ -9,8 +9,12 @@ public sealed record ResolvedTitleFont(string Family, int Size, TitleWeight Weig
 /// <param name="Colour">The fence's colour after the order of spec §3.1, or null (neutral).</param>
 /// <param name="TitleStrip">The title bar's background (Title strip style), or null.</param>
 /// <param name="Bar">The short bar under a single fence's title (Accent edge style), or null.</param>
+/// <param name="Align">Where the title sits (M36).</param>
+/// <param name="TitleOnHover">The title bar shows only while the pointer is over the fence (M36).</param>
+/// <param name="Spacing">Room between the cells (M36).</param>
 public sealed record FenceStyle(
-    Argb? Colour, Argb Veil, Argb Border, Argb TitleText, Argb? TitleStrip, Argb? Bar, ResolvedTitleFont Font, double TitleHeight);
+    Argb? Colour, Argb Veil, Argb Border, Argb TitleText, Argb? TitleStrip, Argb? Bar, ResolvedTitleFont Font, double TitleHeight,
+    TitleAlign Align = TitleAlign.Left, bool TitleOnHover = false, Spacing Spacing = Spacing.Normal);
 
 /// <summary>Decides how a fence looks (M14, spec 2026-10-04-appearance-design §3). Pure.</summary>
 public static class FenceLook
@@ -37,18 +41,19 @@ public static class FenceLook
     /// <param name="wallpaperAccent">The colour of the wallpaper behind this fence, when known.</param>
     public static FenceStyle Resolve(AppearanceSettings appearance, Fence fence, bool light, Argb? wallpaperAccent)
     {
+        var own = fence.Look ?? new OwnLook(); // M36: the fence's own values over Settings' (ADR-057)
         var colour = Argb.FromHex(fence.CustomColor)
                      ?? (fence.TabColor is { } swatch && Swatches.TryGetValue(swatch, out var swatchColour) ? swatchColour : (Argb?)null)
                      ?? (appearance.WallpaperAccent ? wallpaperAccent : null);
         var ink = light ? LightInk : DarkInk;
-        var veil = ink with { A = Alpha(light ? appearance.StrengthLight : appearance.StrengthDark) };
+        var veil = ink with { A = Alpha(own.Strength ?? (light ? appearance.StrengthLight : appearance.StrengthDark)) };
         var border = light ? new Argb(0x33, 0, 0, 0) : new Argb(0x40, 0xFF, 0xFF, 0xFF);
         var titleText = light ? DarkTitle : White;
         Argb? strip = null, bar = null;
 
         if (colour is { } fill)
         {
-            switch (appearance.ColourStyle)
+            switch (own.ColourStyle ?? appearance.ColourStyle)
             {
                 case ColourStyle.TintedGlass:
                     veil = fill.Mix(ink, 0.25) with { A = Math.Max(veil.A, MinimumGlassAlpha) };
@@ -71,10 +76,12 @@ public static class FenceLook
             }
         }
 
-        // One title font for every fence (v1.7.1, ADR-038: per-fence fonts were too much in practice).
+        // Settings' title font, part by part under the fence's own (M36, ADR-057: back in Fence settings…, not the menu).
         var global = appearance.TitleFont;
-        var font = new ResolvedTitleFont(global.Family ?? "Segoe UI", global.Size ?? 14, global.Weight ?? TitleWeight.SemiBold);
-        return new FenceStyle(colour, veil, border, titleText, strip, bar, font, TitleHeightFor(font.Size));
+        var mine = own.TitleFont;
+        var font = new ResolvedTitleFont(mine?.Family ?? global.Family ?? "Segoe UI", mine?.Size ?? global.Size ?? 14, mine?.Weight ?? global.Weight ?? TitleWeight.SemiBold);
+        return new FenceStyle(colour, veil, border, titleText, strip, bar, font, TitleHeightFor(font.Size),
+            own.TitleAlign ?? TitleAlign.Left, own.TitleOnHover == true, own.Spacing ?? Spacing.Normal);
     }
 
     /// <summary>
@@ -84,6 +91,12 @@ public static class FenceLook
     /// </summary>
     private static Argb ReadableOn(Argb background) => background.Luminance > 0.22 ? DarkTitle : White;
 
+    /// <summary>
+    /// The padding around each cell's element (M36): the room between elements; the elements keep their size. Normal is the
+    /// look before M36.
+    /// </summary>
+    public static double CellInset(Spacing spacing) => spacing switch { Spacing.Compact => 0, Spacing.Roomy => 8, _ => 2 };
+
     /// <summary>The title row grows with the font so bigger titles are never clipped: 26 / 30 / 34 / 38 DIP.</summary>
     public static double TitleHeightFor(int size) => size switch { <= 12 => 26, <= 14 => 30, <= 17 => 34, _ => 38 };
 
diff --git a/src/NeoFences.Core/Config/ConfigJson.cs b/src/NeoFences.Core/Config/ConfigJson.cs
index 30e15a7..675f4e2 100644
--- a/src/NeoFences.Core/Config/ConfigJson.cs
+++ b/src/NeoFences.Core/Config/ConfigJson.cs
@@ -27,6 +27,8 @@ public static class ConfigJson
             new LenientEnumConverter<ViewShow>(), new LenientEnumConverter<FenceSort>(),
             new LenientEnumConverter<Items.ItemShow>(), new LenientEnumConverter<Items.FenceLayout>(), // M24 // M22: a typo in items.json shows the usual look, never fails the file
             new LenientEnumConverter<Items.PanelLook>(), new LenientEnumConverter<Items.PanelSort>(), // M26
+            // M36: a fence's own look and the presets; a typo is "like all fences", never a lost file.
+            new LenientEnumConverter<TitleAlign>(), new LenientEnumConverter<Spacing>(), new LenientEnumConverter<LabelMode>(),
             new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
         },
     };
diff --git a/src/NeoFences.Core/Config/ConfigNormalizer.cs b/src/NeoFences.Core/Config/ConfigNormalizer.cs
index 007f9d9..e0b6547 100644
--- a/src/NeoFences.Core/Config/ConfigNormalizer.cs
+++ b/src/NeoFences.Core/Config/ConfigNormalizer.cs
@@ -41,6 +41,7 @@ public static class ConfigNormalizer
                 CustomColor = Appearance.Argb.FromHex(loadedFence.CustomColor)?.ToHex(), // M14: a broken colour is none
                 Layout = Enum.IsDefined(loadedFence.Layout) ? loadedFence.Layout : Items.FenceLayout.Flow, // M24: a typo is Flow
                 Collect = Items.CollectRules.Normalize(loadedFence.Collect), // M27
+                Look = NormalizeLook(loadedFence.Look), // M36
             };
             fence = fence with { View = fence.IsLibrary ? null : Items.FolderViews.Normalize(loadedFence.View) }; // M21: never on the Library
             seenFenceIds.Add(fence.Id);
@@ -54,6 +55,7 @@ public static class ConfigNormalizer
             Settings = settings,
             Fences = FenceTabs.Repair(fences), // M9: one consistent box per tab
             Layouts = NormalizeLayouts(config.Layouts),
+            Presets = NormalizePresets(config.Presets), // M36
             Library = NormalizeLibrary(config.Library) with
             {
                 // M22: only a fence that holds items (not a folder view, not gone)
@@ -80,6 +82,52 @@ public static class ConfigNormalizer
         };
     }
 
+    /// <summary>
+    /// M36: a fence's own look with odd parts dropped (they become "like all fences"); strength in range; a look with nothing
+    /// left is none (null).
+    /// </summary>
+    public static OwnLook? NormalizeLook(OwnLook? look)
+    {
+        if (look is null) return null;
+        var font = look.TitleFont is { } own
+            ? new TitleFont(
+                string.IsNullOrWhiteSpace(own.Family) ? null : own.Family.Trim(),
+                own.Size is { } size && TitleFont.Sizes.Contains(size) ? size : null,
+                own.Weight is { } weight && Enum.IsDefined(weight) ? weight : null)
+            : null;
+        var clean = new OwnLook
+        {
+            ColourStyle = look.ColourStyle is { } style && Enum.IsDefined(style) ? style : null,
+            Strength = look.Strength is { } strength ? Math.Clamp(strength, 0, AppearanceSettings.MaxStrength) : null,
+            TitleFont = font == new TitleFont(null, null, null) ? null : font,
+            TitleAlign = look.TitleAlign is { } align && Enum.IsDefined(align) ? align : null,
+            TitleOnHover = look.TitleOnHover == true ? true : null, // false is the same as like all fences: one way to say it
+            Spacing = look.Spacing is { } spacing && Enum.IsDefined(spacing) ? spacing : null,
+        };
+        return clean == new OwnLook() ? null : clean;
+    }
+
+    /// <summary>M36: own presets with a usable name (tidied, not blank, not a built-in's, the first of the same name); odd values dropped.</summary>
+    private static List<LookPreset> NormalizePresets(IReadOnlyList<LookPreset>? presets)
+    {
+        var names = new HashSet<string>(LookPresets.BuiltIn.Select(preset => preset.Name), StringComparer.OrdinalIgnoreCase);
+        var kept = new List<LookPreset>();
+        foreach (var preset in presets ?? [])
+        {
+            if (preset is null) continue;
+            var name = LookPresets.Clean(preset.Name);
+            if (name.Length == 0 || !names.Add(name)) continue;
+            kept.Add(new LookPreset
+            {
+                Name = name,
+                Look = NormalizeLook(preset.Look) ?? new OwnLook(),
+                IconSize = preset.IconSize is { } iconSize && IconSizes.Contains(iconSize) ? iconSize : null,
+                Labels = preset.Labels is { } labels && Enum.IsDefined(labels) ? labels : null,
+            });
+        }
+        return kept;
+    }
+
     private static LibrarySettings NormalizeLibrary(LibrarySettings? library) => new()
     {
         Folders = (library?.Folders ?? []).Where(folder => !string.IsNullOrWhiteSpace(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
diff --git a/src/NeoFences.Core/Model/Fence.cs b/src/NeoFences.Core/Model/Fence.cs
index dc4cf55..1b28be9 100644
--- a/src/NeoFences.Core/Model/Fence.cs
+++ b/src/NeoFences.Core/Model/Fence.cs
@@ -52,6 +52,9 @@ public sealed record Fence
     /// <summary>Any colour as "#RRGGBB" (M14, fence menu → Colour → Custom…); wins over <see cref="TabColor"/>.</summary>
     public string? CustomColor { get; init; }
 
+    /// <summary>Its own look (M36, Fence settings…); null: like all fences (Settings → Appearance).</summary>
+    public OwnLook? Look { get; init; }
+
     /// <summary>The first-run welcome (M30, ADR-051): only a fresh start's first fence; cleared by its first item.</summary>
     [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
     public bool Welcome { get; init; }
diff --git a/src/NeoFences.Core/Model/FenceEdits.cs b/src/NeoFences.Core/Model/FenceEdits.cs
index 38faaab..dae6012 100644
--- a/src/NeoFences.Core/Model/FenceEdits.cs
+++ b/src/NeoFences.Core/Model/FenceEdits.cs
@@ -54,6 +54,10 @@ public static class FenceEdits
     public static NeoFencesConfig SetLabels(NeoFencesConfig config, string fenceId, LabelMode labels) =>
         config.WithFence(Require(config, fenceId) with { Labels = labels });
 
+    /// <summary>Fence settings… (M36): the fence's own look, replaced as a whole; a look with nothing set is none (like all fences).</summary>
+    public static NeoFencesConfig SetLook(NeoFencesConfig config, string fenceId, OwnLook? look) =>
+        config.WithFence(Require(config, fenceId) with { Look = ConfigNormalizer.NormalizeLook(look) });
+
     /// <summary>Settings → "Apply to all fences": every fence, and the default for new ones.</summary>
     public static NeoFencesConfig SetLabelsEverywhere(NeoFencesConfig config, LabelMode labels) =>
         config with
diff --git a/src/NeoFences.Core/Model/LookPresets.cs b/src/NeoFences.Core/Model/LookPresets.cs
new file mode 100644
index 0000000..39eb0e0
--- /dev/null
+++ b/src/NeoFences.Core/Model/LookPresets.cs
@@ -0,0 +1,126 @@
+using NeoFences.Core.Config;
+
+namespace NeoFences.Core.Model;
+
+/// <summary>
+/// A look preset (M36): an own look and, where the preset needs them, an icon size and labels. Applied, it is copied onto
+/// the fence (a copy, not a link); the fence keeps its colour.
+/// </summary>
+public sealed record LookPreset
+{
+    public string Name { get; init; } = "";
+
+    public OwnLook Look { get; init; } = new();
+
+    public int? IconSize { get; init; }
+
+    public LabelMode? Labels { get; init; }
+}
+
+/// <summary>The built-in presets and the owner's own (M36, spec §2). Pure: each returns a new config.</summary>
+public static class LookPresets
+{
+    public const int MaxNameLength = 40;
+
+    /// <summary>Glass, Minimal, Title strip, Solid, Compact (the owner picked all five).</summary>
+    public static IReadOnlyList<LookPreset> BuiltIn { get; } =
+    [
+        new() { Name = "Glass", Look = new OwnLook { ColourStyle = ColourStyle.TintedGlass, Strength = 25 }, Labels = LabelMode.Always },
+        new() { Name = "Minimal", Look = new OwnLook { ColourStyle = ColourStyle.AccentEdge, Strength = 4, TitleOnHover = true }, Labels = LabelMode.OnHover },
+        new() { Name = "Title strip", Look = new OwnLook { ColourStyle = ColourStyle.TitleStrip, Strength = 60 } },
+        new() { Name = "Solid", Look = new OwnLook { ColourStyle = ColourStyle.AccentEdge, Strength = AppearanceSettings.MaxStrength, Spacing = Spacing.Roomy } },
+        new() { Name = "Compact", Look = new OwnLook { Spacing = Spacing.Compact }, IconSize = 32, Labels = LabelMode.OnHover },
+    ];
+
+    /// <summary>The built-ins, then the owner's own.</summary>
+    public static IReadOnlyList<LookPreset> All(NeoFencesConfig config) => [.. BuiltIn, .. config.Presets];
+
+    /// <summary>The preset's look copied onto the fence; icon size and labels only where the preset has them.</summary>
+    /// <exception cref="ArgumentException">No fence with that id.</exception>
+    public static NeoFencesConfig Apply(NeoFencesConfig config, string fenceId, LookPreset preset)
+    {
+        var fence = Require(config, fenceId);
+        return config.WithFence(fence with
+        {
+            Look = ConfigNormalizer.NormalizeLook(preset.Look),
+            IconSize = preset.IconSize ?? fence.IconSize,
+            Labels = preset.Labels ?? fence.Labels,
+        });
+    }
+
+    /// <summary>"Like all fences": the fence's own look goes; icon size, labels and layout stay.</summary>
+    public static NeoFencesConfig LikeAllFences(NeoFencesConfig config, string fenceId) =>
+        config.WithFence(Require(config, fenceId) with { Look = null });
+
+    /// <summary>Why a name cannot be a new preset's, or null when it can.</summary>
+    public static string? NameProblem(string name)
+    {
+        var clean = Clean(name);
+        if (clean.Length == 0) return "Type a name.";
+        return BuiltIn.FirstOrDefault(preset => string.Equals(preset.Name, clean, StringComparison.OrdinalIgnoreCase)) is { } builtIn
+            ? $"“{builtIn.Name}” is a built-in preset: choose another name."
+            : null;
+    }
+
+    /// <summary>
+    /// "Save this look as a preset…": the fence's own look, icon size and labels under <paramref name="name"/>. An own preset
+    /// of the same name (any case) is replaced in its place; a name with a <see cref="NameProblem"/> changes nothing.
+    /// </summary>
+    public static NeoFencesConfig Save(NeoFencesConfig config, string fenceId, string name)
+    {
+        if (NameProblem(name) is not null) return config;
+        var fence = Require(config, fenceId);
+        var preset = new LookPreset { Name = Clean(name), Look = fence.Look ?? new OwnLook(), IconSize = fence.IconSize, Labels = fence.Labels };
+        var presets = config.Presets.ToList();
+        var index = presets.FindIndex(own => string.Equals(own.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
+        if (index >= 0) presets[index] = preset;
+        else presets.Add(preset);
+        return config with { Presets = presets };
+    }
+
+    /// <summary>Deletes an own preset (any case); built-ins cannot be deleted. Fences that used it keep their look.</summary>
+    public static NeoFencesConfig Delete(NeoFencesConfig config, string name)
+    {
+        var kept = config.Presets.Where(own => !string.Equals(own.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
+        return kept.Count == config.Presets.Count ? config : config with { Presets = kept };
+    }
+
+    /// <summary>
+    /// The preset the fence looks like now (its chip is lit), or null: like all fences (no own look), or a look of its own
+    /// that no preset has.
+    /// </summary>
+    public static string? Matching(NeoFencesConfig config, Fence fence)
+    {
+        if (ConfigNormalizer.NormalizeLook(fence.Look) is not { } look) return null;
+        return All(config).FirstOrDefault(preset => ConfigNormalizer.NormalizeLook(preset.Look) == look
+                                                    && (preset.IconSize ?? fence.IconSize) == fence.IconSize
+                                                    && (preset.Labels ?? fence.Labels) == fence.Labels)?.Name;
+    }
+
+    /// <summary>A name as one tidy line: control characters are spaces, runs of spaces one, cut at <see cref="MaxNameLength"/>.</summary>
+    internal static string Clean(string? name)
+    {
+        var clean = string.Join(' ', new string((name ?? "").Select(character => char.IsControl(character) ? ' ' : character).ToArray())
+            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
+        if (clean.Length <= MaxNameLength) return clean;
+        // Never between the halves of a surrogate pair (an emoji): one character less instead.
+        return clean[..(char.IsHighSurrogate(clean[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength)].TrimEnd();
+    }
+
+    private static Fence Require(NeoFencesConfig config, string fenceId) =>
+        config.Fences.FirstOrDefault(fence => fence.Id == fenceId) ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
+}
+
+/// <summary>Settings → About → Reset settings to defaults… (M36, spec §3). Pure.</summary>
+public static class SettingsReset
+{
+    /// <summary>
+    /// Every Settings page back to its defaults (the Games page's switches too); the fences and their looks, the own presets
+    /// and the Games page's data (folders, hidden games, chosen covers) stay. Items are items.json's and are not touched.
+    /// </summary>
+    public static NeoFencesConfig Apply(NeoFencesConfig config) => config with
+    {
+        Settings = new Settings(),
+        Library = config.Library with { Sources = new LibrarySources(), OnlineArt = null, NewGamesFence = null },
+    };
+}
diff --git a/src/NeoFences.Core/Model/NeoFencesConfig.cs b/src/NeoFences.Core/Model/NeoFencesConfig.cs
index 898208d..fc3f269 100644
--- a/src/NeoFences.Core/Model/NeoFencesConfig.cs
+++ b/src/NeoFences.Core/Model/NeoFencesConfig.cs
@@ -18,6 +18,9 @@ public sealed record NeoFencesConfig
     /// <summary>Game Library settings (M12): game folders, sources, hidden games.</summary>
     public LibrarySettings Library { get; init; } = new();
 
+    /// <summary>The owner's own look presets (M36), after the built-in ones (<see cref="LookPresets.BuiltIn"/>).</summary>
+    public IReadOnlyList<LookPreset> Presets { get; init; } = [];
+
     /// <summary>Fingerprint of the display configuration seen last; new configurations are derived from it.</summary>
     public string? LastLayoutFingerprint { get; init; }
 
diff --git a/src/NeoFences.Core/Model/OwnLook.cs b/src/NeoFences.Core/Model/OwnLook.cs
new file mode 100644
index 0000000..79f525f
--- /dev/null
+++ b/src/NeoFences.Core/Model/OwnLook.cs
@@ -0,0 +1,29 @@
+namespace NeoFences.Core.Model;
+
+/// <summary>Where a fence's title sits in its title bar (M36).</summary>
+public enum TitleAlign { Left, Centre, Right }
+
+/// <summary>Room between a fence's cells (M36): the elements keep their size.</summary>
+public enum Spacing { Normal, Compact, Roomy }
+
+/// <summary>
+/// A fence's own look (M36, ADR-057): each part set here wins over Settings → Appearance; a null part is "Like all fences".
+/// The normalizer turns a look with nothing set into none (<see cref="Fence.Look"/> null).
+/// </summary>
+public sealed record OwnLook
+{
+    public ColourStyle? ColourStyle { get; init; }
+
+    /// <summary>Background strength in %, one value for light and dark mode (Settings keeps one per mode).</summary>
+    public int? Strength { get; init; }
+
+    /// <summary>Each part that is null is the Settings font's part.</summary>
+    public TitleFont? TitleFont { get; init; }
+
+    public TitleAlign? TitleAlign { get; init; }
+
+    /// <summary>True: the title bar shows only while the pointer is over the fence. Null: always, like all fences.</summary>
+    public bool? TitleOnHover { get; init; }
+
+    public Spacing? Spacing { get; init; }
+}
diff --git a/src/NeoFences.Core/Model/Snapshots.cs b/src/NeoFences.Core/Model/Snapshots.cs
index 81c3c34..e308424 100644
--- a/src/NeoFences.Core/Model/Snapshots.cs
+++ b/src/NeoFences.Core/Model/Snapshots.cs
@@ -18,6 +18,16 @@ public sealed record Snapshot
 
     /// <summary>Every fence's items (M18), as in items.json.</summary>
     public IReadOnlyDictionary<string, IReadOnlyList<VirtualItem>> Items { get; init; } = new Dictionary<string, IReadOnlyList<VirtualItem>>();
+
+    /// <summary>
+    /// The whole setup (M36): Settings, the Games page's settings and the own presets, only in the snapshot taken before an
+    /// import or a reset (<see cref="Snapshots.TakeWhole"/>), so restoring it undoes them. Null in every other snapshot.
+    /// </summary>
+    public Settings? Settings { get; init; }
+
+    public LibrarySettings? Library { get; init; }
+
+    public IReadOnlyList<LookPreset>? Presets { get; init; }
 }
 
 /// <summary>Taking and restoring snapshots (M10). Pure: the App saves the result.</summary>
@@ -51,9 +61,14 @@ public static class Snapshots
         Items = items.Fences,
     };
 
+    /// <summary>A snapshot that also holds Settings, the Games settings and the own presets (M36: before an import or a reset).</summary>
+    public static Snapshot TakeWhole(NeoFencesConfig config, ItemsDocument items, string name, DateTimeOffset now) =>
+        Take(config, items, name, now) with { Settings = config.Settings, Library = config.Library, Presets = config.Presets };
+
     /// <summary>
     /// The arrangement of <paramref name="snapshot"/> applied to <paramref name="current"/> (spec §3; M18: items as saved):
-    /// the snapshot's fences, places and items; Settings and setups the snapshot never saw stay as they are. Items of
+    /// the snapshot's fences, places and items; Settings and setups the snapshot never saw stay as they are (a whole snapshot,
+    /// M36, brings its Settings, Games settings and presets back too). Items of
     /// fences the snapshot does not have are dropped (a damaged or hand-edited file). Auto-collect rules start looking at
     /// <paramref name="restoredAt"/> (M27 final review I4): files from the snapshot's time on are not collected again.
     /// </summary>
@@ -63,7 +78,11 @@ public static class Snapshots
         var saved = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = snapshot.Fences, Layouts = snapshot.Layouts });
         var layouts = new Dictionary<string, Layout>(current.Layouts);
         foreach (var (fingerprint, layout) in saved.Layouts) layouts[fingerprint] = layout;
-        var config = ConfigNormalizer.Normalize(current with { Fences = saved.Fences, Layouts = layouts });
+        var config = ConfigNormalizer.Normalize(current with
+        {
+            Fences = saved.Fences, Layouts = layouts,
+            Settings = snapshot.Settings ?? current.Settings, Library = snapshot.Library ?? current.Library, Presets = snapshot.Presets ?? current.Presets,
+        });
         if (restoredAt is { } now)
             config = config with { Fences = [.. config.Fences.Select(fence => fence.Collect.Count == 0 ? fence : fence with { Collect = [.. fence.Collect.Select(rule => rule with { Watermark = now })] })] };
         var items = ItemEdits.Repair(new ItemsDocument { Fences = snapshot.Items ?? new Dictionary<string, IReadOnlyList<VirtualItem>>() });
```

- [ ] **Step 4: Run.** `dotnet test tests/NeoFences.Core.Tests` → Expected: `Passed: 793`.
- [ ] **Step 5: Commit.** `git add -A && git commit -m "feat: added per-fence looks, look presets, settings reset and whole snapshots in Core"` (ledger the Task 1 calls above as rulings).

### Task 2: Core — the setup file (export / import)

**Files:**
- Create: `tests/NeoFences.Core.Tests/Config/SetupFileTests.cs`, `src/NeoFences.Core/Config/SetupFile.cs`
- Modify: `src/NeoFences.Core/Config/ConfigJson.cs` (`SerializeManifest`, `DeserializeManifest`)

**Interfaces:**
- Consumes: `NeoFencesConfig.Presets`, `ConfigNormalizer.Normalize`, `ItemEdits.Repair` / `Prune`, `SnapshotStore.MaxFileBytes`.
- Produces: `SetupFile.Extension = ".neofences"`, `.PicturesOf(config, items) → IReadOnlyList<SetupPicture>`,
  `.Write(path, config, items, dataDirectory, appVersion, now) → IReadOnlyList<SetupPicture>` (the pictures that were gone),
  `.Read(path) → SetupRead(Manifest, Config, Items, Pictures)` (throws `SetupFileException` whose message is shown as is:
  `SetupFile.NotASetup`, `.Damaged`, `.Unreadable`, or "This file comes from a newer NeoFences (x). Update NeoFences
  first."), `.PlacePictures(read, dataDirectory) → IReadOnlyList<(SetupPicture, Exception)>`; `record SetupPicture(string
  Folder, string FileName)`, `record SetupPictureData(SetupPicture Picture, byte[] Bytes)`, `record SetupManifest`.

- [ ] **Step 1: Write the failing tests.** Write this patch to `m36-2a-tests.patch` and apply it:

```diff
diff --git a/tests/NeoFences.Core.Tests/Config/SetupFileTests.cs b/tests/NeoFences.Core.Tests/Config/SetupFileTests.cs
new file mode 100644
index 0000000..b18a9af
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Config/SetupFileTests.cs
@@ -0,0 +1,174 @@
+using System.IO.Compression;
+using NeoFences.Core.Config;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+using NeoFences.Core.Tests.TestSupport;
+
+namespace NeoFences.Core.Tests.Config;
+
+/// <summary>M36 (spec §3): Export setup… / Import setup… — one .neofences file (a zip) and its checks.</summary>
+public class SetupFileTests
+{
+    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
+
+    /// <summary>A setup with a picture icon, a chosen cover and an own preset; the pictures exist in <paramref name="data"/>.</summary>
+    private static (NeoFencesConfig Config, ItemsDocument Items) Setup(TempDirectory data)
+    {
+        var fence = Fence.Create("Apps") with { Look = new OwnLook { Strength = 40 } };
+        var config = new NeoFencesConfig
+        {
+            Fences = [fence],
+            Settings = new Settings { PeekHotkey = "Ctrl+Q" },
+            Presets = [new LookPreset { Name = "Mine", Look = new OwnLook { Spacing = Spacing.Roomy } }],
+            Library = new LibrarySettings { Folders = ["D:\\Games"], CoverChoices = new Dictionary<string, string> { ["steam:1"] = "choice-steam-1.jpg" } },
+        };
+        var items = new ItemsDocument().With(fence.Id,
+        [
+            VirtualItem.Create("C:\\tools\\a.exe") with { Icon = new ItemIcon { Image = "pic.png" } },
+            VirtualItem.Create("C:\\tools\\b.exe") with { Icon = new ItemIcon { File = "C:\\icons\\b.ico" } }, // a user's file: never exported
+        ]);
+        Directory.CreateDirectory(data.File("icons"));
+        Directory.CreateDirectory(data.File("covers"));
+        File.WriteAllBytes(data.File("icons\\pic.png"), [1, 2, 3]);
+        File.WriteAllBytes(data.File("icons\\unused.png"), [9]);
+        File.WriteAllBytes(data.File("covers\\choice-steam-1.jpg"), [4, 5]);
+        File.WriteAllText(data.File("covers\\index.json"), "{}");
+        return (config, items);
+    }
+
+    [Fact]
+    public void Export_HoldsTheSetupItsPicturesAndAManifest_AndNothingElse()
+    {
+        using var data = new TempDirectory();
+        var (config, items) = Setup(data);
+        var path = data.File("my.neofences");
+
+        var missing = SetupFile.Write(path, config, items, data.Path, appVersion: "0.23.0", now: Now);
+
+        Assert.Empty(missing);
+        using var zip = ZipFile.OpenRead(path);
+        Assert.Equal(["config.json", "covers/choice-steam-1.jpg", "icons/pic.png", "items.json", "manifest.json"],
+            zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
+        Assert.False(File.Exists(path + ".tmp"));
+    }
+
+    [Fact]
+    public void Import_ReadsBackTheSameSetupAndPictures_AndPlacesThePictures()
+    {
+        using var data = new TempDirectory();
+        var (config, items) = Setup(data);
+        var path = data.File("my.neofences");
+        SetupFile.Write(path, config, items, data.Path, appVersion: "0.23.0", now: Now);
+
+        var read = SetupFile.Read(path);
+
+        Assert.Equal(("0.23.0", Now), (read.Manifest.AppVersion, read.Manifest.ExportedAt));
+        Assert.Equal("Ctrl+Q", read.Config.Settings.PeekHotkey);
+        Assert.Equal(config.Fences[0].Look, read.Config.Fences[0].Look);
+        Assert.Equal(["Mine"], read.Config.Presets.Select(preset => preset.Name));
+        Assert.Equal(["D:\\Games"], read.Config.Library.Folders);
+        Assert.Equal(2, read.Items.Of(config.Fences[0].Id).Count);
+        Assert.Equal([new SetupPicture("covers", "choice-steam-1.jpg"), new SetupPicture("icons", "pic.png")],
+            read.Pictures.Select(picture => picture.Picture).OrderBy(picture => picture.Folder, StringComparer.Ordinal));
+
+        using var elsewhere = new TempDirectory();
+        Assert.Empty(SetupFile.PlacePictures(read, elsewhere.Path));
+        Assert.Equal([1, 2, 3], File.ReadAllBytes(elsewhere.File("icons\\pic.png")));
+        Assert.Equal([4, 5], File.ReadAllBytes(elsewhere.File("covers\\choice-steam-1.jpg")));
+    }
+
+    [Fact]
+    public void Export_LeavesOutAPictureThatIsGone_AndSaysWhich()
+    {
+        using var data = new TempDirectory();
+        var (config, items) = Setup(data);
+        File.Delete(data.File("icons\\pic.png"));
+        var missing = SetupFile.Write(data.File("my.neofences"), config, items, data.Path, appVersion: "0.23.0", now: Now);
+        Assert.Equal([new SetupPicture("icons", "pic.png")], missing);
+        Assert.DoesNotContain(SetupFile.Read(data.File("my.neofences")).Pictures, picture => picture.Picture.Folder == "icons");
+    }
+
+    [Fact]
+    public void Read_RefusesAFileThatIsNotAZip()
+    {
+        using var data = new TempDirectory();
+        File.WriteAllText(data.File("notes.neofences"), "hello");
+        Assert.Equal(SetupFile.NotASetup, Assert.Throws<SetupFileException>(() => SetupFile.Read(data.File("notes.neofences"))).Message);
+    }
+
+    [Fact]
+    public void Read_RefusesAZipThatIsNotAnExport()
+    {
+        using var data = new TempDirectory();
+        Zip(data.File("other.neofences"), ("readme.txt", "hi"));
+        Assert.Equal(SetupFile.NotASetup, Assert.Throws<SetupFileException>(() => SetupFile.Read(data.File("other.neofences"))).Message);
+        Zip(data.File("wrong.neofences"), ("manifest.json", """{ "format": "Something else", "formatVersion": 1 }"""));
+        Assert.Equal(SetupFile.NotASetup, Assert.Throws<SetupFileException>(() => SetupFile.Read(data.File("wrong.neofences"))).Message);
+    }
+
+    [Theory]
+    [InlineData("""{ "format": "NeoFences setup", "formatVersion": 2, "appVersion": "2.0.0", "configSchema": 5, "itemsSchema": 1 }""")]
+    [InlineData("""{ "format": "NeoFences setup", "formatVersion": 1, "appVersion": "2.0.0", "configSchema": 6, "itemsSchema": 1 }""")]
+    [InlineData("""{ "format": "NeoFences setup", "formatVersion": 1, "appVersion": "2.0.0", "configSchema": 5, "itemsSchema": 2 }""")]
+    public void Read_RefusesAnExportOfANewerNeoFences(string manifest)
+    {
+        using var data = new TempDirectory();
+        Zip(data.File("new.neofences"), ("manifest.json", manifest), ("config.json", "{}"), ("items.json", "{}"));
+        Assert.Equal("This file comes from a newer NeoFences (2.0.0). Update NeoFences first.",
+            Assert.Throws<SetupFileException>(() => SetupFile.Read(data.File("new.neofences"))).Message);
+    }
+
+    [Theory]
+    [InlineData(null, "{}")]                 // no config.json
+    [InlineData("{ not json", "{}")]          // a broken config.json
+    [InlineData("""{ "schemaVersion": 5 }""", null)] // no items.json
+    [InlineData("""{ "schemaVersion": 5 }""", "[1, 2")]
+    [InlineData("""{ "schemaVersion": 4 }""", "{}")] // from before the virtual items: not something this export writes
+    public void Read_CallsAnExportWithMissingOrBrokenPartsDamaged(string? configJson, string? itemsJson)
+    {
+        using var data = new TempDirectory();
+        var parts = new List<(string, string)> { ("manifest.json", """{ "format": "NeoFences setup", "formatVersion": 1, "appVersion": "0.23.0", "configSchema": 5, "itemsSchema": 1 }""") };
+        if (configJson is not null) parts.Add(("config.json", configJson));
+        if (itemsJson is not null) parts.Add(("items.json", itemsJson));
+        Zip(data.File("broken.neofences"), [.. parts]);
+        Assert.Equal(SetupFile.Damaged, Assert.Throws<SetupFileException>(() => SetupFile.Read(data.File("broken.neofences"))).Message);
+    }
+
+    [Fact]
+    public void Read_TakesOnlyPicturesTheSetupUses_AndNeverANameThatLeavesItsFolder()
+    {
+        using var data = new TempDirectory();
+        var (config, items) = Setup(data);
+        var path = data.File("my.neofences");
+        SetupFile.Write(path, config, items, data.Path, appVersion: "0.23.0", now: Now);
+        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
+        {
+            foreach (var name in new[] { "icons/../../evil.png", "icons/sub/pic.png", "..\\icons\\pic.png", "icons/", "logs/neofences.log", "icons/extra.png" })
+                using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write("x");
+        }
+        // A hand-edited items.json pointing a picture out of its folder is read as the file name alone.
+        var read = SetupFile.Read(path);
+        Assert.Equal(["choice-steam-1.jpg", "pic.png"], read.Pictures.Select(picture => picture.Picture.FileName).Order(StringComparer.Ordinal));
+
+        using var elsewhere = new TempDirectory();
+        SetupFile.PlacePictures(read, elsewhere.File("data"));
+        Assert.False(File.Exists(elsewhere.File("evil.png")));
+        Assert.Equal(["choice-steam-1.jpg", "pic.png"],
+            Directory.GetFiles(elsewhere.File("data"), "*", SearchOption.AllDirectories).Select(Path.GetFileName).Order(StringComparer.Ordinal));
+    }
+
+    [Fact]
+    public void PicturesOf_UsesOnlyFileNames()
+    {
+        var fence = Fence.Create("Apps");
+        var items = new ItemsDocument().With(fence.Id, [VirtualItem.Create("C:\\a.exe") with { Icon = new ItemIcon { Image = "..\\..\\Windows\\evil.png" } }]);
+        Assert.Equal([new SetupPicture("icons", "evil.png")], SetupFile.PicturesOf(new NeoFencesConfig { Fences = [fence] }, items));
+    }
+
+    private static void Zip(string path, params (string Name, string Text)[] entries)
+    {
+        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
+        foreach (var (name, text) in entries)
+            using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(text);
+    }
+}
```

- [ ] **Step 2: Run them.** `dotnet test tests/NeoFences.Core.Tests` → Expected: build fails with `error CS` lines (`SetupFile`, `SetupPicture`, `SetupFileException` do not exist).
- [ ] **Step 3: Implement.** Write this patch to `m36-2b-core.patch` and apply it:

```diff
diff --git a/src/NeoFences.Core/Config/ConfigJson.cs b/src/NeoFences.Core/Config/ConfigJson.cs
index 675f4e2..158be7c 100644
--- a/src/NeoFences.Core/Config/ConfigJson.cs
+++ b/src/NeoFences.Core/Config/ConfigJson.cs
@@ -49,6 +49,13 @@ public static class ConfigJson
     public static Items.ItemsDocument DeserializeItems(string json) =>
         JsonSerializer.Deserialize<Items.ItemsDocument>(json, Options) ?? throw new JsonException("items.json contains null");
 
+    /// <summary>An export's manifest (M36): the same names as config.json.</summary>
+    public static string SerializeManifest(SetupManifest manifest) => JsonSerializer.Serialize(manifest, Options);
+
+    /// <exception cref="JsonException">The text is not a manifest.</exception>
+    public static SetupManifest DeserializeManifest(string json) =>
+        JsonSerializer.Deserialize<SetupManifest>(json, Options) ?? throw new JsonException("the manifest contains null");
+
     /// <summary>The library folder's index (M12): the same names and enums as config.json.</summary>
     public static string SerializeLibrary(Library.LibraryState state) => JsonSerializer.Serialize(state, Options);
 
diff --git a/src/NeoFences.Core/Config/SetupFile.cs b/src/NeoFences.Core/Config/SetupFile.cs
new file mode 100644
index 0000000..07a9dcc
--- /dev/null
+++ b/src/NeoFences.Core/Config/SetupFile.cs
@@ -0,0 +1,231 @@
+using System.IO.Compression;
+using System.Text;
+using System.Text.Json;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Config;
+
+/// <summary>What an export says about itself (<c>manifest.json</c>).</summary>
+public sealed record SetupManifest
+{
+    public string Format { get; init; } = "";
+    public int FormatVersion { get; init; }
+    public string AppVersion { get; init; } = "";
+    public int ConfigSchema { get; init; }
+    public int ItemsSchema { get; init; }
+    public DateTimeOffset ExportedAt { get; init; }
+}
+
+/// <summary>A picture that travels with a setup: its folder in NeoFences' data (<c>icons</c> or <c>covers</c>) and file name.</summary>
+public sealed record SetupPicture(string Folder, string FileName);
+
+public sealed record SetupPictureData(SetupPicture Picture, byte[] Bytes);
+
+/// <summary>A setup read from a file: normalized, its items repaired, the pictures it uses.</summary>
+public sealed record SetupRead(SetupManifest Manifest, NeoFencesConfig Config, ItemsDocument Items, IReadOnlyList<SetupPictureData> Pictures);
+
+/// <summary>Why a file cannot be imported; the message is shown to the owner as it is.</summary>
+public sealed class SetupFileException(string message, Exception? inner = null) : Exception(message, inner);
+
+/// <summary>
+/// Export setup… / Import setup… (M36, spec §3, ADR-057): one <c>.neofences</c> file, a zip of <c>manifest.json</c>,
+/// <c>config.json</c> (fences, Settings, Games settings, own presets), <c>items.json</c> and the pictures they use (item icon
+/// pictures in <c>icons/</c>, chosen covers in <c>covers/</c>). Never the logs, the game-library scan or a user's file.
+/// </summary>
+public static class SetupFile
+{
+    public const string Extension = ".neofences";
+    public const string FormatName = "NeoFences setup";
+    public const int CurrentFormatVersion = 1;
+
+    public const string NotASetup = "This file is not a NeoFences setup.";
+    public const string Damaged = "This file is damaged: NeoFences cannot read its setup.";
+    public const string Unreadable = "NeoFences could not open this file.";
+
+    /// <summary>Bigger parts are not ours (a zip bomb, a stray file): the JSON files and each picture.</summary>
+    internal const long MaxJsonBytes = SnapshotStore.MaxFileBytes;
+    internal const long MaxPictureBytes = 32 * 1024 * 1024;
+
+    private const string IconsFolder = "icons", CoversFolder = "covers";
+
+    /// <summary>The pictures a setup uses, by file name only (a hand-edited name never points outside its folder).</summary>
+    public static IReadOnlyList<SetupPicture> PicturesOf(NeoFencesConfig config, ItemsDocument items) =>
+        items.Fences.Values.SelectMany(list => list).Select(item => item.Icon?.Image).Select(name => (Folder: IconsFolder, Name: name))
+            .Concat(config.Library.CoverChoices.Values.Select(name => (Folder: CoversFolder, Name: (string?)name)))
+            .Select(picture => (picture.Folder, Name: Path.GetFileName(picture.Name ?? "")))
+            .Where(picture => picture.Name.Length > 0)
+            .Select(picture => new SetupPicture(picture.Folder, picture.Name))
+            .Distinct()
+            .ToList();
+
+    /// <summary>
+    /// Writes the setup to <paramref name="path"/> (a temp file first, then moved: never half a file). Changes nothing else.
+    /// </summary>
+    /// <param name="dataDirectory">NeoFences' data folder, holding <c>icons\</c> and <c>covers\</c>.</param>
+    /// <returns>The pictures that were gone and are not in the file.</returns>
+    /// <exception cref="IOException">The file could not be written (also <see cref="UnauthorizedAccessException"/>).</exception>
+    public static IReadOnlyList<SetupPicture> Write(string path, NeoFencesConfig config, ItemsDocument items, string dataDirectory, string appVersion, DateTimeOffset now)
+    {
+        var missing = new List<SetupPicture>();
+        var temp = path + ".tmp";
+        try
+        {
+            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
+            {
+                var manifest = new SetupManifest
+                {
+                    Format = FormatName, FormatVersion = CurrentFormatVersion, AppVersion = appVersion,
+                    ConfigSchema = NeoFencesConfig.CurrentSchemaVersion, ItemsSchema = ItemsDocument.CurrentSchemaVersion, ExportedAt = now,
+                };
+                WriteText(zip, "manifest.json", ConfigJson.SerializeManifest(manifest));
+                WriteText(zip, "config.json", ConfigJson.Serialize(config));
+                WriteText(zip, "items.json", ConfigJson.SerializeItems(items));
+                foreach (var picture in PicturesOf(config, items))
+                {
+                    var source = Path.Combine(dataDirectory, picture.Folder, picture.FileName);
+                    if (File.Exists(source)) zip.CreateEntryFromFile(source, $"{picture.Folder}/{picture.FileName}", CompressionLevel.Fastest);
+                    else missing.Add(picture);
+                }
+            }
+            File.Move(temp, path, overwrite: true);
+            return missing;
+        }
+        catch
+        {
+            if (File.Exists(temp)) File.Delete(temp);
+            throw;
+        }
+    }
+
+    /// <summary>Reads and checks an export: a NeoFences export, readable, not from a newer NeoFences.</summary>
+    /// <exception cref="SetupFileException">It cannot be imported; the message says why.</exception>
+    public static SetupRead Read(string path)
+    {
+        try
+        {
+            using var zip = ZipFile.OpenRead(path);
+            var manifest = Text(zip, "manifest.json") is { } manifestText ? TryManifest(manifestText) : null;
+            if (manifest?.Format != FormatName) throw new SetupFileException(NotASetup);
+            if (manifest.FormatVersion > CurrentFormatVersion || manifest.ConfigSchema > NeoFencesConfig.CurrentSchemaVersion
+                || manifest.ItemsSchema > ItemsDocument.CurrentSchemaVersion)
+                throw Newer(manifest.AppVersion);
+
+            var config = ConfigJson.Deserialize(Text(zip, "config.json") ?? throw new SetupFileException(Damaged));
+            if (config.SchemaVersion > NeoFencesConfig.CurrentSchemaVersion) throw Newer(manifest.AppVersion);
+            if (config.SchemaVersion < ConfigStore.FirstVirtualItemsSchema) throw new SetupFileException(Damaged);
+            config = ConfigNormalizer.Normalize(config);
+            var items = ConfigJson.DeserializeItems(Text(zip, "items.json") ?? throw new SetupFileException(Damaged));
+            if (items.Schema > ItemsDocument.CurrentSchemaVersion) throw Newer(manifest.AppVersion);
+            items = ItemEdits.Prune(ItemEdits.Repair(items), config.Fences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal));
+
+            var pictures = new List<SetupPictureData>();
+            foreach (var picture in PicturesOf(config, items))
+            {
+                // Only the exact name "folder/file" is looked up: an entry such as "icons/../x" is never one of them.
+                if (zip.GetEntry($"{picture.Folder}/{picture.FileName}") is not { } entry || entry.Length > MaxPictureBytes) continue;
+                if (ReadCapped(entry, MaxPictureBytes) is { } bytes) pictures.Add(new SetupPictureData(picture, bytes));
+            }
+            return new SetupRead(manifest, config, items, pictures);
+        }
+        catch (SetupFileException)
+        {
+            throw;
+        }
+        catch (InvalidDataException failure) // not a zip, or a damaged one
+        {
+            throw new SetupFileException(File.Exists(path) && IsZip(path) ? Damaged : NotASetup, failure);
+        }
+        catch (JsonException failure)
+        {
+            throw new SetupFileException(Damaged, failure);
+        }
+        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+        {
+            throw new SetupFileException(Unreadable, failure);
+        }
+    }
+
+    /// <summary>
+    /// Puts a read setup's pictures into NeoFences' <c>icons\</c> and <c>covers\</c> (a picture of the same name is replaced).
+    /// Each is written on its own: one that fails is reported and the rest still go in (the item shows its usual icon).
+    /// </summary>
+    /// <returns>The pictures that could not be written, with why.</returns>
+    public static IReadOnlyList<(SetupPicture Picture, Exception Failure)> PlacePictures(SetupRead read, string dataDirectory)
+    {
+        var failures = new List<(SetupPicture, Exception)>();
+        foreach (var (picture, bytes) in read.Pictures)
+        {
+            try
+            {
+                var folder = Path.Combine(dataDirectory, picture.Folder);
+                Directory.CreateDirectory(folder);
+                var target = Path.Combine(folder, Path.GetFileName(picture.FileName));
+                File.WriteAllBytes(target + ".tmp", bytes);
+                File.Move(target + ".tmp", target, overwrite: true);
+            }
+            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+            {
+                failures.Add((picture, failure));
+            }
+        }
+        return failures;
+    }
+
+    private static SetupFileException Newer(string appVersion) =>
+        new($"This file comes from a newer NeoFences ({(string.IsNullOrWhiteSpace(appVersion) ? "unknown version" : appVersion)}). Update NeoFences first.");
+
+    private static SetupManifest? TryManifest(string text)
+    {
+        try
+        {
+            return ConfigJson.DeserializeManifest(text);
+        }
+        catch (JsonException)
+        {
+            return null;
+        }
+    }
+
+    private static string? Text(ZipArchive zip, string name)
+    {
+        if (zip.GetEntry(name) is not { } entry) return null;
+        if (entry.Length > MaxJsonBytes) throw new SetupFileException(Damaged);
+        return ReadCapped(entry, MaxJsonBytes) is { } bytes ? Encoding.UTF8.GetString(bytes) : throw new SetupFileException(Damaged);
+    }
+
+    /// <summary>The entry's bytes, or null when it holds more than it says (more than <paramref name="limit"/>).</summary>
+    private static byte[]? ReadCapped(ZipArchiveEntry entry, long limit)
+    {
+        using var stream = entry.Open();
+        using var buffer = new MemoryStream();
+        var chunk = new byte[81920];
+        int read;
+        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
+        {
+            if (buffer.Length + read > limit) return null;
+            buffer.Write(chunk, 0, read);
+        }
+        return buffer.ToArray();
+    }
+
+    private static bool IsZip(string path)
+    {
+        try
+        {
+            using var file = File.OpenRead(path);
+            Span<byte> head = stackalloc byte[2];
+            return file.Read(head) == 2 && head[0] == (byte)'P' && head[1] == (byte)'K';
+        }
+        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+        {
+            return false;
+        }
+    }
+
+    private static void WriteText(ZipArchive zip, string name, string text)
+    {
+        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
+        writer.Write(text);
+    }
+}
```

- [ ] **Step 4: Run.** `dotnet test tests/NeoFences.Core.Tests` → Expected: `Passed: 808`.
- [ ] **Step 5: Commit.** `git add -A && git commit -m "feat: added the .neofences setup file with its checks in Core"` (ledger the Task 2 calls).

### Task 3: App — Fence settings, presets, the fence's own look

**Files:**
- Create: `src/NeoFences.App/FenceSettingsWindow.xaml(.cs)`, `src/NeoFences.App/FenceHost.FenceSettings.cs`, `src/NeoFences.App/SettingsStyles.xaml`
- Modify: `src/NeoFences.App/FenceWindow.xaml(.cs)` (menu entry, `CellInset`, alignment, title bar on hover),
  `src/NeoFences.App/FenceItemView.cs` (`ApplySize` inset), `src/NeoFences.App/FenceHost.cs` (wiring, delete, theme),
  `src/NeoFences.App/FenceHost.Appearance.cs` (refresh on Settings → Appearance), `src/NeoFences.App/App.cs` (merge
  `SettingsStyles.xaml`), `src/NeoFences.App/SettingsWindow.xaml` (its styles moved out), `docs/GUIDE.md` (§1 Fence
  settings and presets, §4 Spacing, §12 Appearance — ADR-050: in the same commit)

**Interfaces:**
- Consumes: everything Task 1 produces.
- Produces: `FenceWindow.FenceSettingsRequested`; `FenceSettingsWindow(fenceId)` with `Show(FenceSettingsView)` and events
  `LookChanged(OwnLook?)`, `IconSizeChanged(int)`, `LabelsChanged(LabelMode)`, `LayoutChanged(FenceLayout)`,
  `PresetChosen(LookPreset)`, `LikeAllChosen()`, `PresetSaved(string)`, `PresetDeleted(string)`; host `OpenFenceSettings`,
  `ChangeFence`, `WindowShowing`, `RefreshFenceSettings`, `RefreshAllFenceSettings` (Task 4 calls the last one).

- [ ] **Step 1: Implement.** Write this patch to `m36-3-app.patch` and apply it:

```diff
diff --git a/docs/GUIDE.md b/docs/GUIDE.md
index 1d0c601..6fa77a9 100644
--- a/docs/GUIDE.md
+++ b/docs/GUIDE.md
@@ -75,6 +75,22 @@ for any colour (a colour field, a hue bar and a hex box). How the colour shows (
 title strip), how solid the background is and the title font are in **Settings → Appearance**. **Colour fences from the
 wallpaper** picks each fence's colour from the wallpaper behind it — Wallpaper Engine included.
 
+**Fence settings:** fence menu → **Fence settings…** (**Tab settings…** on a tab) gives one fence a look of its own. Each
+change shows on the fence at once; close the window when you are done.
+
+- **Preset**: one click for a whole look — **Glass** (tinted glass, light background, names below), **Minimal** (title bar
+  on hover, almost no background, names on hover), **Title strip** (a coloured title bar over a dark background),
+  **Solid** (a dense background with the accent edge, roomy spacing), **Compact** (small icons, compact spacing, names on
+  hover), and your own. **Save this look as a preset…** keeps the fence's look under a name; the small **✕** next to your
+  own preset deletes it. A preset is copied onto the fence: changing the fence later does not change the preset or other
+  fences. The fence keeps its colour.
+- **Look**: **Colour style**, **Background** (one strength for light and dark mode), **Title** (font, size, weight,
+  alignment) and **Show the title bar** — off, the title bar shows only while the pointer is over the fence, so you can
+  still move, roll up and rename it.
+- **Items**: **Icon size**, **Labels**, **Layout** and **Spacing** (**Compact**, **Normal**, **Roomy**).
+- Every look setting starts at **Like all fences (…)**: what Settings → Appearance says. A setting of the fence's own is
+  marked **This fence's own**; **Like all fences again** (or the **Like all fences** preset) drops them all.
+
 **Delete:** fence menu → **Delete fence**. Only the fence and its links go; your files are not touched. NeoFences
 takes a snapshot first; **Ctrl+Z** in a fence, or tray → **Undo delete**, brings it back for 2 minutes. (Keys reach a fence after
 a click on the desktop or the fence; from another app use the tray.)
@@ -151,6 +167,8 @@ Store**, the **game folders** you add, and game shortcuts on your desktop — an
 - **Icon size:** fence menu → **View** → **Icon size** (Small, Medium, Large, Extra large).
 - **Labels:** fence menu → **View** → **Labels** → **Always**, or **On hover (icons only)** for a tight grid that shows a name only
   when you point at it. A game cover then shows its name over itself.
+- **Spacing:** fence menu → **Fence settings…** → **Spacing**: **Compact**, **Normal** or **Roomy** room between the
+  things in a fence (they keep their size).
 
 ## 5. Widgets
 
@@ -248,7 +266,7 @@ Tray or fence menu → **Settings…**. A list of sections on the left opens one
 - **Fences** — **Labels for new fences** (and **Apply to all fences**), **Show shortcut arrows**, how **Rolled-up fences
   open**.
 - **Appearance** — **Colour style** (**Accent edge**, **Tinted glass**, **Title strip**), **Background strength**,
-  **Colour fences from the wallpaper**, **Title font**.
+  **Colour fences from the wallpaper**, **Title font**. A fence can have its own of each (fence menu → **Fence settings…**).
 - **Games**, **Game mode**, **Snapshots**, **Updates** — see their sections above.
 - **About** — the version, **Open logs folder**, **Open data folder**, **Help (online guide)**.
 
diff --git a/src/NeoFences.App/App.cs b/src/NeoFences.App/App.cs
index 482f033..030b7e4 100644
--- a/src/NeoFences.App/App.cs
+++ b/src/NeoFences.App/App.cs
@@ -86,6 +86,8 @@ public sealed class App : Application
 
         // M35 (ADR-056): one menu style for every menu, light or dark like the fences.
         Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/NeoFences;component/menus.xaml", UriKind.Relative) });
+        // M36: the Settings look, shared by Settings and Fence settings.
+        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/NeoFences;component/settingsstyles.xaml", UriKind.Relative) });
         MenuTheme.Apply(Resources, light: SystemTheme.AppsUseLightTheme());
         _host = new FenceHost { StartMode = _start };
         _host.ExitRequested += Shutdown;
diff --git a/src/NeoFences.App/FenceHost.Appearance.cs b/src/NeoFences.App/FenceHost.Appearance.cs
index c5010db..74113e0 100644
--- a/src/NeoFences.App/FenceHost.Appearance.cs
+++ b/src/NeoFences.App/FenceHost.Appearance.cs
@@ -58,6 +58,7 @@ public sealed partial class FenceHost
         _config = _config with { Settings = _config.Settings with { Appearance = appearance } };
         if (accentTurnedOn || accentTurnedOff) UpdateAccents(); // refreshes Settings itself (the accent's source line)
         RestyleAll();
+        RefreshAllFenceSettings(); // M36: "Like all fences (…)" names the new values
         ScheduleSave();
         // No RefreshSettings here: Settings already shows what the user changed, and a full refresh re-reads every snapshot
         // file on each slider step (final review I3).
diff --git a/src/NeoFences.App/FenceHost.FenceSettings.cs b/src/NeoFences.App/FenceHost.FenceSettings.cs
new file mode 100644
index 0000000..8b28805
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.FenceSettings.cs
@@ -0,0 +1,92 @@
+using System.Windows;
+using NeoFences.Core.Model;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Fence settings… (M36, spec 2026-10-06-fence-settings-and-presets-design §1–2): one window per fence (a tab has its own).
+/// Every change applies to the fence at once and is saved; the window shows the result back.
+/// </summary>
+public sealed partial class FenceHost
+{
+    private readonly Dictionary<string, FenceSettingsWindow> _fenceSettings = new(StringComparer.Ordinal);
+
+    private void OpenFenceSettings(string fenceId)
+    {
+        if (_fenceSettings.TryGetValue(fenceId, out var open))
+        {
+            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
+            open.Activate();
+            return;
+        }
+        if (_config.Fences.All(fence => fence.Id != fenceId)) return;
+        var window = new FenceSettingsWindow(fenceId);
+        window.LookChanged += look => ChangeFence(fenceId, config => FenceEdits.SetLook(config, fenceId, look));
+        window.PresetChosen += preset => ChangeFence(fenceId, config => LookPresets.Apply(config, fenceId, preset));
+        window.LikeAllChosen += () => ChangeFence(fenceId, config => LookPresets.LikeAllFences(config, fenceId));
+        window.IconSizeChanged += iconSize => ChangeFence(fenceId, config => FenceEdits.SetIconSize(config, fenceId, iconSize));
+        window.LabelsChanged += labels => ChangeFence(fenceId, config => FenceEdits.SetLabels(config, fenceId, labels));
+        window.LayoutChanged += layout =>
+        {
+            // The shown tab takes the menu's path (a Free layout pins the cells where the items are now).
+            if (WindowShowing(fenceId) is { } shown) SetFenceLayout(shown, layout);
+            else ChangeFence(fenceId, config => FenceEdits.SetLayout(config, fenceId, layout));
+            RefreshFenceSettings(fenceId);
+        };
+        window.PresetSaved += name =>
+        {
+            _config = LookPresets.Save(_config, fenceId, name);
+            Log.Information("look preset saved: {Name}", name);
+            ScheduleSave();
+            RefreshAllFenceSettings(); // every open window lists the presets
+        };
+        window.PresetDeleted += name =>
+        {
+            _config = LookPresets.Delete(_config, name);
+            Log.Information("look preset deleted: {Name}", name);
+            ScheduleSave();
+            RefreshAllFenceSettings();
+        };
+        window.Activated += (_, _) => RefreshFenceSettings(fenceId); // the fence menu may have changed the fence meanwhile
+        window.Closed += (_, _) => _fenceSettings.Remove(fenceId);
+        _fenceSettings[fenceId] = window;
+        RefreshFenceSettings(fenceId);
+        window.Show();
+        window.Activate();
+    }
+
+    /// <summary>One change to a fence from its settings: applied to the fence window showing it (if any), saved, shown back.</summary>
+    private void ChangeFence(string fenceId, Func<NeoFencesConfig, NeoFencesConfig> change)
+    {
+        if (_config.Fences.All(fence => fence.Id != fenceId)) return; // deleted meanwhile
+        _config = change(_config);
+        if (WindowShowing(fenceId) is { } window)
+        {
+            window.Refresh(_config.Fences.First(fence => fence.Id == fenceId));
+            ApplyStyle(window);
+        }
+        ScheduleSave();
+        RefreshFenceSettings(fenceId);
+    }
+
+    /// <summary>The fence window whose shown tab is this fence, or null (another tab of its box is in front).</summary>
+    private FenceWindow? WindowShowing(string fenceId) => _windows.Values.FirstOrDefault(window => window.FenceId == fenceId);
+
+    /// <summary>Shows the fence as it is now; a fence that is gone (deleted, a snapshot restored) closes its window.</summary>
+    private void RefreshFenceSettings(string fenceId)
+    {
+        if (!_fenceSettings.TryGetValue(fenceId, out var window)) return;
+        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { } shown)
+        {
+            window.Close();
+            return;
+        }
+        window.Show(new FenceSettingsView(shown, Appearance, _lightTheme, LookPresets.All(_config), LookPresets.Matching(_config, shown)));
+    }
+
+    private void RefreshAllFenceSettings()
+    {
+        foreach (var fenceId in _fenceSettings.Keys.ToList()) RefreshFenceSettings(fenceId);
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index 27fff7a..6c58326 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -305,6 +305,7 @@ public sealed partial class FenceHost
         window.MovedByUser += OnFenceMoved;
         window.RenameRequested += title => RenameFence(window, title);
         window.IconSizeRequested += iconSize => SetFenceIconSize(window, iconSize);
+        window.FenceSettingsRequested += () => OpenFenceSettings(window.FenceId); // M36
         window.LockToggled += locked => SetFenceLocked(window, locked);
         window.DeleteRequested += () => DeleteFence(window);
         window.NewFenceRequested += CreateFence;
@@ -567,6 +568,7 @@ public sealed partial class FenceHost
         _items = ItemEdits.RemoveFence(_items, fence.Id);
         SaveNow();
         SyncBoxes(); // closes the window when its box is gone
+        RefreshFenceSettings(fence.Id); // M36: its settings window closes too
         UpdateWatching();
         ForgetGoneTargets();
         UpdateLibrary(); // M23: no game fence left, no scan
@@ -590,6 +592,7 @@ public sealed partial class FenceHost
         MenuTheme.Apply(System.Windows.Application.Current.Resources, light); // M35: the menus follow too
         RestyleAll(); // M14: the tone's strength and ink
         RefreshSettings();
+        RefreshAllFenceSettings(); // M36: "Like all fences (…)" names the tone's strength
     }
 
     /// <summary>
@@ -1076,6 +1079,7 @@ public sealed partial class FenceHost
         CheckAllTargets(); // the restored items' targets may have changed since
         _settingsWindow?.ShowSnapshotNotice($"Restored \"{snapshot.Name}\".", failed: false); // replaces an earlier failure line (final review M1)
         RefreshSettings();
+        RefreshAllFenceSettings(); // M36: fences that are gone close their settings
     }
 
     /// <summary>The snapshot list; a damaged file or an unreadable folder is logged once, not on every tray open (final review I2).</summary>
diff --git a/src/NeoFences.App/FenceItemView.cs b/src/NeoFences.App/FenceItemView.cs
index 59b4e5d..871c714 100644
--- a/src/NeoFences.App/FenceItemView.cs
+++ b/src/NeoFences.App/FenceItemView.cs
@@ -192,9 +192,11 @@ public sealed class FenceItemView : INotifyPropertyChanged
     /// Sizes its content for its span on cells of this size (M24, spec §1). True when the icon size changed (the icon is
     /// loaded again at the new size).
     /// </summary>
-    public bool ApplySize(double cellWidth, double cellHeight, double iconDips, double labelHeight)
+    /// <param name="inset">The spacing's room on each side of the element (M36; 2 = Normal, the look before).</param>
+    public bool ApplySize(double cellWidth, double cellHeight, double iconDips, double labelHeight, double inset = 2)
     {
-        const double CellPaddingX = 8, CellPaddingY = 12, MaxIcon = 256;
+        const double MaxIcon = 256;
+        double CellPaddingX = 4 + 2 * inset, CellPaddingY = 8 + 2 * inset; // the item template: inset, a 1-px edge, 1,3 padding
         var width = Span.Columns * cellWidth - CellPaddingX;
         var height = Span.Rows * cellHeight - CellPaddingY - labelHeight;
         var icon = Span == GridSpan.One ? iconDips : Math.Clamp(Math.Floor(Math.Min(width - 8, height)), iconDips, MaxIcon);
diff --git a/src/NeoFences.App/FenceSettingsWindow.xaml b/src/NeoFences.App/FenceSettingsWindow.xaml
new file mode 100644
index 0000000..2b16667
--- /dev/null
+++ b/src/NeoFences.App/FenceSettingsWindow.xaml
@@ -0,0 +1,130 @@
+<Window x:Class="NeoFences.App.FenceSettingsWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="Fence settings" Width="480" Height="760" MinWidth="420" MinHeight="400"
+        WindowStartupLocation="CenterScreen" ThemeMode="System" ShowInTaskbar="True">
+    <!-- M36 (spec §1, owner's pick A): Settings' look in a small window. Every change shows on the fence at once; no OK. -->
+    <DockPanel>
+        <DockPanel DockPanel.Dock="Bottom" Margin="20,8,20,16">
+            <Button x:Name="CloseButton" Content="Close" DockPanel.Dock="Right" MinWidth="88" IsCancel="True" />
+            <Button x:Name="LikeAllAgainButton" Content="Like all fences again" HorizontalAlignment="Left"
+                    ToolTip="Drops every look setting of this fence's own (icon size, labels and layout stay)" />
+        </DockPanel>
+        <ScrollViewer VerticalScrollBarVisibility="Auto">
+            <StackPanel Margin="20,8,20,8">
+                <TextBlock Text="Preset" Style="{StaticResource SectionHeader}" Margin="2,8,0,8" />
+                <WrapPanel x:Name="PresetChips" AutomationProperties.Name="Presets" />
+                <Button x:Name="SavePresetButton" Content="Save this look as a preset…" HorizontalAlignment="Left" Margin="0,8,0,0" />
+                <Border x:Name="SavePanel" Style="{StaticResource Card}" Margin="0,8,0,0" Visibility="Collapsed">
+                    <StackPanel>
+                        <TextBlock Text="Name of the new preset" />
+                        <DockPanel Margin="0,8,0,0">
+                            <Button x:Name="SaveCancelButton" Content="Cancel" DockPanel.Dock="Right" Margin="8,0,0,0" />
+                            <Button x:Name="SaveConfirmButton" Content="Save" DockPanel.Dock="Right" Margin="8,0,0,0" />
+                            <TextBox x:Name="PresetNameBox" MaxLength="40" AutomationProperties.Name="Name of the new preset" />
+                        </DockPanel>
+                        <TextBlock x:Name="SaveProblem" Style="{StaticResource Description}" Visibility="Collapsed"
+                                   Foreground="{DynamicResource SystemFillColorCriticalBrush}" />
+                    </StackPanel>
+                </Border>
+
+                <TextBlock Text="Look" Style="{StaticResource SectionHeader}" />
+                <Border Style="{StaticResource Card}">
+                    <DockPanel>
+                        <ComboBox x:Name="StyleBox" DockPanel.Dock="Right" Width="210" VerticalAlignment="Center" AutomationProperties.Name="Colour style" />
+                        <StackPanel VerticalAlignment="Center">
+                            <TextBlock Text="Colour style" />
+                            <TextBlock x:Name="StyleOwn" Text="This fence's own" Style="{StaticResource Description}" Foreground="{DynamicResource AccentTextFillColorPrimaryBrush}" Visibility="Collapsed" />
+                        </StackPanel>
+                    </DockPanel>
+                </Border>
+                <Border Style="{StaticResource Card}">
+                    <StackPanel>
+                        <DockPanel>
+                            <Button x:Name="StrengthLikeAllButton" Content="Like all fences" DockPanel.Dock="Right" Visibility="Collapsed" />
+                            <StackPanel VerticalAlignment="Center">
+                                <TextBlock Text="Background" />
+                                <TextBlock x:Name="StrengthDescription" Style="{StaticResource Description}" />
+                            </StackPanel>
+                        </DockPanel>
+                        <DockPanel Margin="0,8,0,0">
+                            <TextBlock Text="Clear" DockPanel.Dock="Left" VerticalAlignment="Center" Margin="0,0,10,0" Style="{StaticResource Description}" />
+                            <TextBlock Text="Solid" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="10,0,0,0" Style="{StaticResource Description}" />
+                            <Slider x:Name="StrengthSlider" Minimum="0" Maximum="85" TickFrequency="5" IsSnapToTickEnabled="True" SmallChange="5" LargeChange="10"
+                                    VerticalAlignment="Center" AutomationProperties.Name="Background strength" />
+                        </DockPanel>
+                    </StackPanel>
+                </Border>
+                <Border Style="{StaticResource Card}">
+                    <StackPanel>
+                        <TextBlock Text="Title" />
+                        <TextBlock x:Name="TitleOwn" Text="This fence's own" Style="{StaticResource Description}" Foreground="{DynamicResource AccentTextFillColorPrimaryBrush}" Visibility="Collapsed" />
+                        <!-- One row per part: "Like all fences (…)" needs the room (probe: three in a row cut it). -->
+                        <DockPanel Margin="0,8,0,0">
+                            <ComboBox x:Name="FontFamilyBox" DockPanel.Dock="Right" Width="210" AutomationProperties.Name="Title font" VirtualizingPanel.IsVirtualizing="True">
+                                <ComboBox.ItemsPanel>
+                                    <ItemsPanelTemplate>
+                                        <VirtualizingStackPanel />
+                                    </ItemsPanelTemplate>
+                                </ComboBox.ItemsPanel>
+                            </ComboBox>
+                            <TextBlock Text="Font" VerticalAlignment="Center" />
+                        </DockPanel>
+                        <DockPanel Margin="0,8,0,0">
+                            <ComboBox x:Name="FontSizeBox" DockPanel.Dock="Right" Width="210" AutomationProperties.Name="Title size" />
+                            <TextBlock Text="Size" VerticalAlignment="Center" />
+                        </DockPanel>
+                        <DockPanel Margin="0,8,0,0">
+                            <ComboBox x:Name="FontWeightBox" DockPanel.Dock="Right" Width="210" AutomationProperties.Name="Title weight" />
+                            <TextBlock Text="Weight" VerticalAlignment="Center" />
+                        </DockPanel>
+                        <DockPanel Margin="0,8,0,0">
+                            <ComboBox x:Name="AlignBox" DockPanel.Dock="Right" Width="210" AutomationProperties.Name="Title alignment" />
+                            <TextBlock Text="Alignment" VerticalAlignment="Center" />
+                        </DockPanel>
+                    </StackPanel>
+                </Border>
+                <Border Style="{StaticResource Card}">
+                    <DockPanel>
+                        <CheckBox x:Name="TitleBarBox" DockPanel.Dock="Right" Style="{StaticResource Switch}" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Show the title bar" />
+                        <StackPanel>
+                            <TextBlock Text="Show the title bar" TextWrapping="Wrap" />
+                            <TextBlock Style="{StaticResource Description}" Text="Off: it shows while the pointer is over the fence, so you can still move, roll up and rename it." />
+                        </StackPanel>
+                    </DockPanel>
+                </Border>
+
+                <TextBlock Text="Items" Style="{StaticResource SectionHeader}" />
+                <Border Style="{StaticResource Card}">
+                    <StackPanel>
+                        <DockPanel>
+                            <ComboBox x:Name="IconSizeBox" DockPanel.Dock="Right" Width="210" AutomationProperties.Name="Icon size" />
+                            <TextBlock Text="Icon size" VerticalAlignment="Center" />
+                        </DockPanel>
+                        <DockPanel Margin="0,8,0,0">
+                            <ComboBox x:Name="LabelsBox" DockPanel.Dock="Right" Width="210" AutomationProperties.Name="Labels">
+                                <ComboBoxItem Content="Always" />
+                                <ComboBoxItem Content="On hover (icons only)" />
+                            </ComboBox>
+                            <TextBlock Text="Labels" VerticalAlignment="Center" />
+                        </DockPanel>
+                        <DockPanel x:Name="LayoutRow" Margin="0,8,0,0">
+                            <ComboBox x:Name="LayoutBox" DockPanel.Dock="Right" Width="210" AutomationProperties.Name="Layout">
+                                <ComboBoxItem Content="Flow (packed)" />
+                                <ComboBoxItem Content="Free (fixed positions)" />
+                            </ComboBox>
+                            <TextBlock Text="Layout" VerticalAlignment="Center" />
+                        </DockPanel>
+                        <DockPanel Margin="0,8,0,0">
+                            <ComboBox x:Name="SpacingBox" DockPanel.Dock="Right" Width="210" AutomationProperties.Name="Spacing" />
+                            <StackPanel VerticalAlignment="Center">
+                                <TextBlock Text="Spacing" />
+                                <TextBlock x:Name="SpacingOwn" Text="This fence's own" Style="{StaticResource Description}" Foreground="{DynamicResource AccentTextFillColorPrimaryBrush}" Visibility="Collapsed" />
+                            </StackPanel>
+                        </DockPanel>
+                    </StackPanel>
+                </Border>
+            </StackPanel>
+        </ScrollViewer>
+    </DockPanel>
+</Window>
diff --git a/src/NeoFences.App/FenceSettingsWindow.xaml.cs b/src/NeoFences.App/FenceSettingsWindow.xaml.cs
new file mode 100644
index 0000000..6fedf13
--- /dev/null
+++ b/src/NeoFences.App/FenceSettingsWindow.xaml.cs
@@ -0,0 +1,227 @@
+using System.Windows;
+using System.Windows.Controls;
+using System.Windows.Controls.Primitives;
+using NeoFences.Core.Config;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+
+namespace NeoFences.App;
+
+/// <summary>What Fence settings shows: the fence, Settings' look (for "Like all fences (…)"), the presets and the lit one.</summary>
+public sealed record FenceSettingsView(Fence Fence, AppearanceSettings Appearance, bool LightTheme, IReadOnlyList<LookPreset> Presets, string? Matching);
+
+/// <summary>
+/// Fence menu → Fence settings… (M36, spec §1): presets, then the look (each setting "Like all fences" or the fence's own),
+/// then the items. Every change goes to the host at once; the host applies, saves and shows it back.
+/// </summary>
+public partial class FenceSettingsWindow : Window
+{
+    public event Action<OwnLook?>? LookChanged;
+    public event Action<int>? IconSizeChanged;
+    public event Action<LabelMode>? LabelsChanged;
+    public event Action<FenceLayout>? LayoutChanged;
+    public event Action<LookPreset>? PresetChosen;
+    public event Action? LikeAllChosen;
+    public event Action<string>? PresetSaved;
+    public event Action<string>? PresetDeleted;
+
+    private const string LikeAllName = "Like all fences";
+    private static readonly ColourStyle[] Styles = [ColourStyle.AccentEdge, ColourStyle.TintedGlass, ColourStyle.TitleStrip];
+    private static readonly string[] StyleNames = ["Accent edge", "Tinted glass", "Title strip"];
+    private static readonly (int Size, string Name)[] TitleSizes = [(12, "Small"), (14, "Normal"), (17, "Large"), (20, "Huge")];
+    private static readonly (int Size, string Name)[] IconSizes = [(32, "Small"), (48, "Medium"), (64, "Large"), (96, "Extra large")];
+    private static readonly Spacing[] Spacings = [Spacing.Compact, Spacing.Normal, Spacing.Roomy];
+    private static readonly Lazy<List<string>> InstalledFonts =
+        new(() => [.. System.Windows.Media.Fonts.SystemFontFamilies.Select(family => family.Source).Distinct().Order(StringComparer.CurrentCultureIgnoreCase)]);
+
+    private FenceSettingsView? _view;
+    private int? _ownStrength;
+    private bool _updating;
+
+    public string FenceId { get; }
+
+    public FenceSettingsWindow(string fenceId)
+    {
+        FenceId = fenceId;
+        InitializeComponent();
+        StyleBox.SelectionChanged += (_, _) => ReportLook();
+        StrengthSlider.ValueChanged += (_, _) =>
+        {
+            if (_updating) return;
+            _ownStrength = (int)Math.Round(StrengthSlider.Value);
+            ReportLook();
+        };
+        StrengthLikeAllButton.Click += (_, _) =>
+        {
+            _ownStrength = null;
+            ReportLook();
+        };
+        FontFamilyBox.SelectionChanged += (_, _) => ReportLook();
+        FontSizeBox.SelectionChanged += (_, _) => ReportLook();
+        FontWeightBox.SelectionChanged += (_, _) => ReportLook();
+        AlignBox.SelectionChanged += (_, _) => ReportLook();
+        TitleBarBox.Checked += (_, _) => ReportLook();
+        TitleBarBox.Unchecked += (_, _) => ReportLook();
+        SpacingBox.SelectionChanged += (_, _) => ReportLook();
+        IconSizeBox.SelectionChanged += (_, _) =>
+        {
+            if (!_updating && IconSizeBox.SelectedIndex >= 0) IconSizeChanged?.Invoke(IconSizes[IconSizeBox.SelectedIndex].Size);
+        };
+        LabelsBox.SelectionChanged += (_, _) =>
+        {
+            if (!_updating && LabelsBox.SelectedIndex >= 0) LabelsChanged?.Invoke(LabelsBox.SelectedIndex == 0 ? LabelMode.Always : LabelMode.OnHover);
+        };
+        LayoutBox.SelectionChanged += (_, _) =>
+        {
+            if (!_updating && LayoutBox.SelectedIndex >= 0) LayoutChanged?.Invoke(LayoutBox.SelectedIndex == 0 ? FenceLayout.Flow : FenceLayout.Free);
+        };
+        LikeAllAgainButton.Click += (_, _) => LikeAllChosen?.Invoke();
+        CloseButton.Click += (_, _) => Close(); // IsCancel does not close a window shown modeless (M35)
+        SavePresetButton.Click += (_, _) =>
+        {
+            SavePanel.Visibility = Visibility.Visible;
+            SaveProblem.Visibility = Visibility.Collapsed;
+            PresetNameBox.Text = "";
+            PresetNameBox.Focus();
+        };
+        SaveCancelButton.Click += (_, _) => SavePanel.Visibility = Visibility.Collapsed;
+        SaveConfirmButton.Click += (_, _) => SavePreset();
+        PresetNameBox.KeyDown += (_, key) =>
+        {
+            if (key.Key != System.Windows.Input.Key.Enter) return;
+            key.Handled = true;
+            SavePreset();
+        };
+        IconSizeBox.ItemsSource = IconSizes.Select(size => size.Name).ToList();
+    }
+
+    /// <summary>Shows the fence's settings as they are now (after every change the host made).</summary>
+    public void Show(FenceSettingsView view)
+    {
+        // Rebuilt only when something changed: a refresh on activation must not disturb a list being opened.
+        if (_view is { } shown && shown.Fence == view.Fence && shown.Appearance == view.Appearance && shown.LightTheme == view.LightTheme
+            && shown.Matching == view.Matching && shown.Presets.SequenceEqual(view.Presets)) return;
+        _updating = true;
+        try
+        {
+            _view = view;
+            var fence = view.Fence;
+            var own = fence.Look ?? new OwnLook();
+            var settings = view.Appearance;
+            Title = $"{fence.Title} — fence settings";
+            ShowPresets(view);
+            LikeAllAgainButton.IsEnabled = fence.Look is not null;
+
+            StyleBox.ItemsSource = StyleNames.Prepend($"{LikeAllName} ({StyleNames[Array.IndexOf(Styles, settings.ColourStyle)]})").ToList();
+            StyleBox.SelectedIndex = own.ColourStyle is { } style ? Array.IndexOf(Styles, style) + 1 : 0;
+            StyleOwn.Visibility = Visible(own.ColourStyle is not null);
+
+            _ownStrength = own.Strength;
+            var allStrength = view.LightTheme ? settings.StrengthLight : settings.StrengthDark;
+            StrengthSlider.Value = own.Strength ?? allStrength;
+            StrengthDescription.Text = own.Strength is { } strength
+                ? $"This fence's own: {strength} % in light and dark mode."
+                : $"{LikeAllName} ({allStrength} % in {(view.LightTheme ? "light" : "dark")} mode).";
+            StrengthDescription.SetResourceReference(TextBlock.ForegroundProperty, own.Strength is null ? "TextFillColorSecondaryBrush" : "AccentTextFillColorPrimaryBrush");
+            StrengthLikeAllButton.Visibility = Visible(own.Strength is not null);
+
+            var font = own.TitleFont;
+            var fonts = InstalledFonts.Value;
+            if (font?.Family is { } family && !fonts.Contains(family)) fonts = [.. fonts.Append(family).Order(StringComparer.CurrentCultureIgnoreCase)]; // a font that is gone stays listed
+            FontFamilyBox.ItemsSource = fonts.Prepend($"{LikeAllName} ({settings.TitleFont.Family})").ToList();
+            FontFamilyBox.SelectedIndex = font?.Family is { } chosen ? fonts.IndexOf(chosen) + 1 : 0;
+            FontSizeBox.ItemsSource = TitleSizes.Select(size => size.Name).Prepend($"{LikeAllName} ({NameOf(TitleSizes, settings.TitleFont.Size)})").ToList();
+            FontSizeBox.SelectedIndex = font?.Size is { } size ? Array.FindIndex(TitleSizes, entry => entry.Size == size) + 1 : 0;
+            FontWeightBox.ItemsSource = Enum.GetNames<TitleWeight>().Prepend($"{LikeAllName} ({settings.TitleFont.Weight})").ToList();
+            FontWeightBox.SelectedIndex = font?.Weight is { } weight ? (int)weight + 1 : 0;
+            AlignBox.ItemsSource = new[] { $"{LikeAllName} (Left)", "Left", "Centre", "Right" };
+            AlignBox.SelectedIndex = own.TitleAlign is { } align ? (int)align + 1 : 0;
+            TitleOwn.Visibility = Visible(font is not null || own.TitleAlign is not null);
+            TitleBarBox.IsChecked = own.TitleOnHover != true;
+
+            IconSizeBox.SelectedIndex = Array.FindIndex(IconSizes, entry => entry.Size == fence.IconSize);
+            LabelsBox.SelectedIndex = fence.Labels == LabelMode.Always ? 0 : 1;
+            LayoutRow.Visibility = Visible(fence.Kind == FenceKind.Items); // as in View ▸: only a fence of items has a layout
+            LayoutBox.SelectedIndex = fence.Layout == FenceLayout.Flow ? 0 : 1;
+            SpacingBox.ItemsSource = new[] { $"{LikeAllName} (Normal)", "Compact", "Normal", "Roomy" };
+            SpacingBox.SelectedIndex = own.Spacing is { } spacing ? Array.IndexOf(Spacings, spacing) + 1 : 0;
+            SpacingOwn.Visibility = Visible(own.Spacing is not null);
+        }
+        finally
+        {
+            _updating = false;
+        }
+    }
+
+    private void ShowPresets(FenceSettingsView view)
+    {
+        PresetChips.Children.Clear();
+        PresetChips.Children.Add(Chip(LikeAllName, isOn: view.Fence.Look is null, () => LikeAllChosen?.Invoke()));
+        foreach (var preset in view.Presets)
+        {
+            var chip = Chip(preset.Name, isOn: preset.Name == view.Matching, () => PresetChosen?.Invoke(preset));
+            if (LookPresets.BuiltIn.Contains(preset))
+            {
+                PresetChips.Children.Add(chip);
+                continue;
+            }
+            // An own preset: its chip and a small ✕ that deletes it (built-ins cannot be deleted).
+            var delete = new Button
+            {
+                Content = "✕", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(-4, 0, 6, 6), VerticalAlignment = VerticalAlignment.Center,
+                ToolTip = $"Delete the preset “{preset.Name}” (fences keep their look)",
+            };
+            System.Windows.Automation.AutomationProperties.SetName(delete, $"Delete preset {preset.Name}");
+            delete.Click += (_, _) => PresetDeleted?.Invoke(preset.Name);
+            PresetChips.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { chip, delete } });
+        }
+    }
+
+    private static ToggleButton Chip(string name, bool isOn, Action chosen)
+    {
+        var chip = new ToggleButton { Content = name, IsChecked = isOn, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 6, 6) };
+        chip.Click += (_, _) =>
+        {
+            chip.IsChecked = isOn; // the host's answer decides which chip is lit
+            chosen();
+        };
+        return chip;
+    }
+
+    private void SavePreset()
+    {
+        if (LookPresets.NameProblem(PresetNameBox.Text) is { } problem)
+        {
+            SaveProblem.Text = problem;
+            SaveProblem.Visibility = Visibility.Visible;
+            return;
+        }
+        SavePanel.Visibility = Visibility.Collapsed;
+        PresetSaved?.Invoke(PresetNameBox.Text);
+    }
+
+    /// <summary>The look the controls show, as the fence's own (each "Like all fences" choice is null).</summary>
+    private void ReportLook()
+    {
+        if (_updating || _view is null) return;
+        var family = FontFamilyBox.SelectedIndex > 0 ? FontFamilyBox.SelectedItem as string : null;
+        int? size = FontSizeBox.SelectedIndex > 0 ? TitleSizes[FontSizeBox.SelectedIndex - 1].Size : null;
+        TitleWeight? weight = FontWeightBox.SelectedIndex > 0 ? (TitleWeight)(FontWeightBox.SelectedIndex - 1) : null;
+        var look = new OwnLook
+        {
+            ColourStyle = StyleBox.SelectedIndex > 0 ? Styles[StyleBox.SelectedIndex - 1] : null,
+            Strength = _ownStrength,
+            TitleFont = family is null && size is null && weight is null ? null : new TitleFont(family, size, weight),
+            TitleAlign = AlignBox.SelectedIndex > 0 ? (TitleAlign)(AlignBox.SelectedIndex - 1) : null,
+            TitleOnHover = TitleBarBox.IsChecked == true ? null : true,
+            Spacing = SpacingBox.SelectedIndex > 0 ? Spacings[SpacingBox.SelectedIndex - 1] : null,
+        };
+        if (ConfigNormalizer.NormalizeLook(look) == ConfigNormalizer.NormalizeLook(_view.Fence.Look)) return;
+        LookChanged?.Invoke(look);
+    }
+
+    private static string NameOf((int Size, string Name)[] sizes, int? size) =>
+        sizes.FirstOrDefault(entry => entry.Size == size).Name ?? $"{size}";
+
+    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
+}
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index 2d0bba4..d46c033 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -29,6 +29,8 @@
         <!-- M24: the grid's cells and layout, set by ApplyCellSizes / ApplyFence before the panel's first pass. -->
         <sys:Double x:Key="GridCellWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">84</sys:Double>
         <sys:Double x:Key="GridCellHeight" xmlns:sys="clr-namespace:System;assembly=System.Runtime">96</sys:Double>
+        <!-- M36: the room around each element, from the fence's spacing (Compact 0, Normal 2, Roomy 8). -->
+        <Thickness x:Key="CellInset">2</Thickness>
         <!-- Thin scrollbar (spec §6): no arrows, a rounded thumb that brightens under the mouse. -->
         <Style TargetType="ScrollBar">
             <Setter Property="Width" Value="6" />
@@ -246,6 +248,8 @@
                         <MenuItem x:Name="AutoCollectItem" Header="Auto-collect…" local:MenuGlyph.Glyph="&#xE895;" />
                         <Separator />
                         <MenuItem x:Name="RenameItem" Header="Rename" InputGestureText="F2" local:MenuGlyph.Glyph="&#xE8AC;" />
+                        <!-- M36: this fence's own look, presets and items (Tab settings… on a tab). -->
+                        <MenuItem x:Name="FenceSettingsItem" Header="Fence settings…" local:MenuGlyph.Glyph="&#xE771;" />
                         <!-- M35 (spec §3): swatches inside the menu, the accent, a custom colour (built in code). -->
                         <MenuItem x:Name="TabColorItem" Header="Colour" local:MenuGlyph.Glyph="&#xE790;" />
                         <MenuItem x:Name="LockItem" Header="Lock position" IsCheckable="True" local:MenuGlyph.Glyph="&#xE72E;" />
@@ -292,7 +296,7 @@
                                         <ControlTemplate TargetType="ListBoxItem">
                                             <!-- The outer border fills the gaps between cells, so the pointer is always over some cell and the
                                                  pop-under name does not blink between neighbours (final review I2). -->
-                                            <Border Background="Transparent" Padding="2">
+                                            <Border Background="Transparent" Padding="{DynamicResource CellInset}">
                                                 <!-- M34 (spec §4, "Clean"): no slab; a faint rounded hover, the accent for the selection. -->
                                                 <Border x:Name="Chrome" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="6" Padding="1,3">
                                                     <ContentPresenter HorizontalAlignment="Center" />
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index 1fe3ac8..3b53486 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -33,6 +33,8 @@ public partial class FenceWindow : Window
     private const int HitTestCaption = 2;
     private const double CornerRadiusDips = 8;
     private double _captionHeightDips = 30; // follows the title font (M14: 26 / 30 / 34 / 38)
+    private double _cellInset = FenceLook.CellInset(Spacing.Normal); // M36: the room around each element (Fence settings → Spacing)
+    private bool _titleOnHover; // M36: the title bar shows only while the pointer is over the fence
     private const double ResizeBorderDips = 6;
 
     // The menu lists ConfigNormalizer.IconSizes (one list, M2c review carry-over); these are only their names.
@@ -132,6 +134,8 @@ public partial class FenceWindow : Window
     public event Action<IReadOnlyList<string>>? OpenManyRequested;
     public event Action<string>? RenameRequested;
     public event Action<int>? IconSizeRequested;
+    /// <summary>Fence menu → Fence settings… (M36).</summary>
+    public event Action? FenceSettingsRequested;
     public event Action<bool>? LockToggled;
     public event Action? DeleteRequested;
     /// <summary>Ctrl+Z or the undo bar (M33, ADR-054): the last removal or deletion comes back.</summary>
@@ -232,6 +236,7 @@ public partial class FenceWindow : Window
         }
         NewFenceItem.Click += (_, _) => NewFenceRequested?.Invoke();
         RenameItem.Click += (_, _) => BeginRename();
+        FenceSettingsItem.Click += (_, _) => FenceSettingsRequested?.Invoke(); // M36
         LockItem.Click += (_, _) => LockToggled?.Invoke(LockItem.IsChecked);
         DeleteItem.Click += (_, _) => DeleteRequested?.Invoke();
         SettingsItem.Click += (_, _) => SettingsRequested?.Invoke();
@@ -416,6 +421,7 @@ public partial class FenceWindow : Window
         ShowTitleOrTabs();
         DetachTabItem.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
         RenameItem.Header = many ? "Rename tab" : "Rename";
+        FenceSettingsItem.Header = many ? "Tab settings…" : "Fence settings…"; // M36: a tab has its own
         // The bar under a single fence's title comes from its look (ApplyStyle, M14); the menu shows the fence's choice.
         ShowColourChoice(active); // M35: the ring on the current swatch, the tick on Custom colour…
         if (_style is { } style) ApplyStyle(style); // headers were rebuilt: their font and the bar follow the look again
@@ -865,14 +871,15 @@ public partial class FenceWindow : Window
     private void ApplyCellSizes(bool requestIcons = true)
     {
         var labelHeight = IsLibrary || _labelMode == LabelMode.Always ? 36.0 : 0.0;
-        var cellWidth = (double)Resources["ItemWidth"] + 8;
-        var cellHeight = _iconSizeDips + 12 + labelHeight;
+        // M36: the spacing's inset on each side (Normal 2: the look before M36, a cell 8 wider and 12 taller than its element).
+        var cellWidth = (double)Resources["ItemWidth"] + 4 + 2 * _cellInset;
+        var cellHeight = _iconSizeDips + 8 + 2 * _cellInset + labelHeight;
         // The panel takes them through resources, so it has them from its first layout pass (final review I3).
         (Resources["GridCellWidth"], Resources["GridCellHeight"]) = (cellWidth, cellHeight);
         foreach (var view in _items)
         {
             var tileWidth = view.TileWidth;
-            if (view.ApplySize(cellWidth, cellHeight, _iconSizeDips, labelHeight) && IsLoaded && requestIcons) RequestIcon(view);
+            if (view.ApplySize(cellWidth, cellHeight, _iconSizeDips, labelHeight, _cellInset) && IsLoaded && requestIcons) RequestIcon(view);
             if (view.IsTile && Math.Abs(view.TileWidth - tileWidth) > 0.5)
             {
                 view.ArtPath = null; // a cover decoded again at the new tile width (final review I2)
@@ -885,7 +892,7 @@ public partial class FenceWindow : Window
     private void SizeView(FenceItemView view)
     {
         var labelHeight = IsLibrary || _labelMode == LabelMode.Always ? 36.0 : 0.0;
-        view.ApplySize((double)Resources["ItemWidth"] + 8, _iconSizeDips + 12 + labelHeight, _iconSizeDips, labelHeight);
+        view.ApplySize((double)Resources["ItemWidth"] + 4 + 2 * _cellInset, _iconSizeDips + 8 + 2 * _cellInset + labelHeight, _iconSizeDips, labelHeight, _cellInset);
     }
 
     /// <summary>One of <see cref="ConfigNormalizer.IconSizes"/> (DIPs). Icons are reloaded at the new size.</summary>
@@ -957,7 +964,7 @@ public partial class FenceWindow : Window
     /// <summary>A fence's columns at this window's width (M28: a hidden tab's own, for its new elements in a Free fence).</summary>
     public int ColumnsFor(Fence fence, bool covers) =>
         !ItemList.IsLoaded || ItemList.ActualWidth <= 0 ? 0 // not laid out yet: no columns to pin with (final review M4)
-            : FenceGrid.ColumnsFor(ItemList.ActualWidth - 8, ItemWidthFor(fence.IconSize, fence.Labels, covers) + 8);
+            : FenceGrid.ColumnsFor(ItemList.ActualWidth - 8, ItemWidthFor(fence.IconSize, fence.Labels, covers) + 4 + 2 * FenceLook.CellInset(fence.Look?.Spacing ?? Spacing.Normal)); // M36: its own spacing
 
     private void OnItemMouseEnter(object sender, MouseEventArgs args)
     {
@@ -1053,8 +1060,8 @@ public partial class FenceWindow : Window
         _expansion.Reset();
         ApplyChrome();
         AnimateHeight(rolledUp ? RolledUpHeightPx : _fullHeightPx);
-        if (rolledUp) _hoverTimer.Start();
-        else _hoverTimer.Stop();
+        UpdateHoverTimer();
+        UpdateTitleBar(PointerInside()); // M36: a rolled-up fence always shows its title bar
     }
 
     /// <summary>Roll-up expand mode from settings (M6b): hover or click.</summary>
@@ -1150,7 +1157,9 @@ public partial class FenceWindow : Window
     // the pointer leaves fast); in hover mode it also opens during a file drag, which is wanted.
     private void OnHoverTick()
     {
-        if (Handle == 0 || !_rolledUp) return;
+        if (Handle == 0) return;
+        if (_titleOnHover) UpdateTitleBar(PointerInside()); // M36
+        if (!_rolledUp) return;
         if (_drag is not null || BodyContextMenu.IsOpen || _itemMenu?.IsOpen == true || _renaming) return; // never close under the user
         var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
         var rect = FenceWindowChrome.GetPixelRect(Handle);
@@ -1248,6 +1257,18 @@ public partial class FenceWindow : Window
         TitleText.FontFamily = new FontFamily(style.Font.Family); // a font that is gone falls back to Segoe UI; the name stays
         TitleText.FontSize = style.Font.Size;
         TitleText.FontWeight = ToWeight(style.Font.Weight);
+        // M36: the fence's title alignment, title bar on hover and spacing.
+        TitleText.TextAlignment = style.Align switch { TitleAlign.Centre => TextAlignment.Center, TitleAlign.Right => TextAlignment.Right, _ => TextAlignment.Left };
+        _titleOnHover = style.TitleOnHover;
+        UpdateHoverTimer();
+        UpdateTitleBar(PointerInside());
+        var inset = FenceLook.CellInset(style.Spacing);
+        if (inset != _cellInset)
+        {
+            _cellInset = inset;
+            Resources["CellInset"] = new Thickness(inset);
+            ApplyCellSizes();
+        }
         ApplyTabLook(style);
         if (style.TitleHeight != _captionHeightDips)
         {
@@ -1262,6 +1283,35 @@ public partial class FenceWindow : Window
         }
     }
 
+    /// <summary>The hover poll runs while it is needed: rolled up (M5), or a title bar shown on hover (M36).</summary>
+    private void UpdateHoverTimer()
+    {
+        if (_rolledUp || _titleOnHover) _hoverTimer.Start();
+        else _hoverTimer.Stop();
+    }
+
+    /// <summary>
+    /// M36: a title bar shown on hover is there while the pointer is over the fence, and whenever the fence is rolled up, renamed,
+    /// dragged or its menu is open; otherwise it fades out (its row keeps its place, so nothing moves under the pointer).
+    /// </summary>
+    private void UpdateTitleBar(bool pointerInside)
+    {
+        var shown = !_titleOnHover || pointerInside || _rolledUp || _renaming || _drag is not null || BodyContextMenu.IsOpen;
+        var opacity = shown ? 1.0 : 0.0;
+        if (TitleBar.Opacity == opacity) return;
+        TitleBar.Opacity = TitleStripFill.Opacity = TitleColorBar.Opacity = opacity;
+        if (shown) Body.SetResourceReference(Border.BorderBrushProperty, "FenceDivider");
+        else Body.BorderBrush = Brushes.Transparent;
+    }
+
+    private bool PointerInside()
+    {
+        if (Handle == 0) return false;
+        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
+        var rect = FenceWindowChrome.GetPixelRect(Handle);
+        return cursorX >= rect.X && cursorX < rect.X + rect.Width && cursorY >= rect.Y && cursorY < rect.Y + rect.Height;
+    }
+
     /// <summary>The colour bar of a single fence and the tab headers' font: they follow every header rebuild.</summary>
     private void ApplyTabLook(FenceStyle style)
     {
diff --git a/src/NeoFences.App/SettingsStyles.xaml b/src/NeoFences.App/SettingsStyles.xaml
new file mode 100644
index 0000000..0edc244
--- /dev/null
+++ b/src/NeoFences.App/SettingsStyles.xaml
@@ -0,0 +1,57 @@
+<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
+    <!-- M36: the Settings look (cards, descriptions, switches), shared by Settings and Fence settings. Colours are the
+         Fluent theme's, found in each window (ThemeMode="System"), so they follow light and dark per window. -->
+    <Style x:Key="SectionHeader" TargetType="TextBlock">
+        <Setter Property="FontSize" Value="14" />
+        <Setter Property="FontWeight" Value="SemiBold" />
+        <Setter Property="Margin" Value="2,20,0,8" />
+    </Style>
+    <Style x:Key="Card" TargetType="Border">
+        <Setter Property="Background" Value="{DynamicResource CardBackgroundFillColorDefaultBrush}" />
+        <Setter Property="BorderBrush" Value="{DynamicResource CardStrokeColorDefaultBrush}" />
+        <Setter Property="BorderThickness" Value="1" />
+        <Setter Property="CornerRadius" Value="6" />
+        <Setter Property="Padding" Value="16,12" />
+        <Setter Property="Margin" Value="0,0,0,4" />
+    </Style>
+    <Style x:Key="Description" TargetType="TextBlock">
+        <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
+        <Setter Property="FontSize" Value="12" />
+        <Setter Property="TextWrapping" Value="Wrap" />
+        <Setter Property="Margin" Value="0,2,0,0" />
+    </Style>
+    <Style x:Key="Switch" TargetType="CheckBox">
+        <Setter Property="Cursor" Value="Hand" />
+        <Setter Property="Template">
+            <Setter.Value>
+                <ControlTemplate TargetType="CheckBox">
+                    <StackPanel Orientation="Horizontal" Background="Transparent">
+                        <TextBlock x:Name="State" Text="Off" Width="28" VerticalAlignment="Center" Foreground="{DynamicResource TextFillColorPrimaryBrush}" />
+                        <Border x:Name="Track" Width="40" Height="20" CornerRadius="10" BorderThickness="1" Background="Transparent"
+                                BorderBrush="{DynamicResource TextFillColorSecondaryBrush}">
+                            <Ellipse x:Name="Knob" Width="12" Height="12" HorizontalAlignment="Left" Margin="4,0,0,0" Fill="{DynamicResource TextFillColorSecondaryBrush}" />
+                        </Border>
+                    </StackPanel>
+                    <ControlTemplate.Triggers>
+                        <Trigger Property="IsChecked" Value="True">
+                            <Setter TargetName="State" Property="Text" Value="On" />
+                            <Setter TargetName="Track" Property="Background" Value="{DynamicResource AccentFillColorDefaultBrush}" />
+                            <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource AccentFillColorDefaultBrush}" />
+                            <Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right" />
+                            <Setter TargetName="Knob" Property="Margin" Value="0,0,4,0" />
+                            <Setter TargetName="Knob" Property="Fill" Value="{DynamicResource TextOnAccentFillColorPrimaryBrush}" />
+                        </Trigger>
+                        <Trigger Property="IsMouseOver" Value="True">
+                            <Setter TargetName="Knob" Property="Width" Value="14" />
+                            <Setter TargetName="Knob" Property="Height" Value="14" />
+                        </Trigger>
+                        <Trigger Property="IsEnabled" Value="False">
+                            <Setter Property="Opacity" Value="0.45" />
+                        </Trigger>
+                    </ControlTemplate.Triggers>
+                </ControlTemplate>
+            </Setter.Value>
+        </Setter>
+    </Style>
+</ResourceDictionary>
diff --git a/src/NeoFences.App/SettingsWindow.xaml b/src/NeoFences.App/SettingsWindow.xaml
index 71f6c74..a65d00c 100644
--- a/src/NeoFences.App/SettingsWindow.xaml
+++ b/src/NeoFences.App/SettingsWindow.xaml
@@ -5,60 +5,6 @@
         WindowStartupLocation="CenterScreen" ThemeMode="System">
     <!-- Fluent (WPF's built-in theme, ThemeMode="System"): follows Windows light/dark and the accent colour (spec §6).
          Set only on this window: the fences keep their own look. Changes apply at once; there is no OK button. -->
-    <Window.Resources>
-        <Style x:Key="SectionHeader" TargetType="TextBlock">
-            <Setter Property="FontSize" Value="14" />
-            <Setter Property="FontWeight" Value="SemiBold" />
-            <Setter Property="Margin" Value="2,20,0,8" />
-        </Style>
-        <Style x:Key="Card" TargetType="Border">
-            <Setter Property="Background" Value="{DynamicResource CardBackgroundFillColorDefaultBrush}" />
-            <Setter Property="BorderBrush" Value="{DynamicResource CardStrokeColorDefaultBrush}" />
-            <Setter Property="BorderThickness" Value="1" />
-            <Setter Property="CornerRadius" Value="6" />
-            <Setter Property="Padding" Value="16,12" />
-            <Setter Property="Margin" Value="0,0,0,4" />
-        </Style>
-        <Style x:Key="Description" TargetType="TextBlock">
-            <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
-            <Setter Property="FontSize" Value="12" />
-            <Setter Property="TextWrapping" Value="Wrap" />
-            <Setter Property="Margin" Value="0,2,0,0" />
-        </Style>
-        <Style x:Key="Switch" TargetType="CheckBox">
-            <Setter Property="Cursor" Value="Hand" />
-            <Setter Property="Template">
-                <Setter.Value>
-                    <ControlTemplate TargetType="CheckBox">
-                        <StackPanel Orientation="Horizontal" Background="Transparent">
-                            <TextBlock x:Name="State" Text="Off" Width="28" VerticalAlignment="Center" Foreground="{DynamicResource TextFillColorPrimaryBrush}" />
-                            <Border x:Name="Track" Width="40" Height="20" CornerRadius="10" BorderThickness="1" Background="Transparent"
-                                    BorderBrush="{DynamicResource TextFillColorSecondaryBrush}">
-                                <Ellipse x:Name="Knob" Width="12" Height="12" HorizontalAlignment="Left" Margin="4,0,0,0" Fill="{DynamicResource TextFillColorSecondaryBrush}" />
-                            </Border>
-                        </StackPanel>
-                        <ControlTemplate.Triggers>
-                            <Trigger Property="IsChecked" Value="True">
-                                <Setter TargetName="State" Property="Text" Value="On" />
-                                <Setter TargetName="Track" Property="Background" Value="{DynamicResource AccentFillColorDefaultBrush}" />
-                                <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource AccentFillColorDefaultBrush}" />
-                                <Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right" />
-                                <Setter TargetName="Knob" Property="Margin" Value="0,0,4,0" />
-                                <Setter TargetName="Knob" Property="Fill" Value="{DynamicResource TextOnAccentFillColorPrimaryBrush}" />
-                            </Trigger>
-                            <Trigger Property="IsMouseOver" Value="True">
-                                <Setter TargetName="Knob" Property="Width" Value="14" />
-                                <Setter TargetName="Knob" Property="Height" Value="14" />
-                            </Trigger>
-                            <Trigger Property="IsEnabled" Value="False">
-                                <Setter Property="Opacity" Value="0.45" />
-                            </Trigger>
-                        </ControlTemplate.Triggers>
-                    </ControlTemplate>
-                </Setter.Value>
-            </Setter>
-        </Style>
-    </Window.Resources>
     <!-- M35 (spec §2): a section list on the left, the open section's page on the right. -->
     <Grid>
         <Grid.ColumnDefinitions>
```

- [ ] **Step 2: Build and test.** `dotnet build` → `0 Warning(s)`; `dotnet test` → `Passed: 808`.
- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: added Fence settings with presets, title alignment, the title bar on hover and spacing"` (ledger the Task 3 calls).

### Task 4: App — Export setup, Import setup, Reset settings

**Files:**
- Create: `src/NeoFences.App/FenceHost.Setup.cs`
- Modify: `src/NeoFences.App/SettingsWindow.xaml(.cs)` (the setup card on Snapshots, Reset on About, three events),
  `src/NeoFences.App/FenceHost.cs` (wiring; `RestoreSnapshot` uses `ReplaceSetup`, whole "Before restore"), `docs/GUIDE.md`
  (§9 the setup file, §12 Snapshots and About)

**Interfaces:**
- Consumes: Task 1's `SettingsReset.Apply`, `Snapshots.TakeWhole`; Task 2's `SetupFile`; Task 3's `RefreshAllFenceSettings`.
- Produces: `SettingsWindow.ExportSetupRequested` / `ImportSetupRequested` / `ResetSettingsRequested`; host `ExportSetup`,
  `ImportSetup`, `ResetSettings`, `SaveWholeSnapshot(name, failureTitle)`, `ReplaceSetup(config, items, whole)`,
  `ApplySettings(Settings)`.

- [ ] **Step 1: Implement.** Write this patch to `m36-4-app.patch` and apply it:

```diff
diff --git a/docs/GUIDE.md b/docs/GUIDE.md
index 6fa77a9..253e606 100644
--- a/docs/GUIDE.md
+++ b/docs/GUIDE.md
@@ -245,6 +245,15 @@ A snapshot saves your fences, their places and their items.
   folder**.
 - NeoFences also takes one by itself before big changes (for example before an update reshapes your fences).
 
+**Your whole setup in one file** (for another PC, or a fresh Windows):
+
+- **Settings → Snapshots → Export setup…** saves one `.neofences` file: your fences, their items, places and looks, your
+  settings, your own presets, and the pictures you chose as item icons and covers. Not the logs, not the game list (it is
+  found again on any PC), and never your files themselves.
+- **Settings → Snapshots → Import setup…** checks the file (a NeoFences export, readable, not from a newer NeoFences),
+  asks, saves your current setup as the snapshot "Before import", then puts the file's setup in place. To undo, restore
+  "Before import". Items whose files or apps are not on this PC show as missing, as usual.
+
 ## 10. Game mode
 
 While a full-screen game is in front, NeoFences goes idle: fences stay at the bottom, the desktop mouse gestures and Peek
@@ -267,8 +276,11 @@ Tray or fence menu → **Settings…**. A list of sections on the left opens one
   open**.
 - **Appearance** — **Colour style** (**Accent edge**, **Tinted glass**, **Title strip**), **Background strength**,
   **Colour fences from the wallpaper**, **Title font**. A fence can have its own of each (fence menu → **Fence settings…**).
-- **Games**, **Game mode**, **Snapshots**, **Updates** — see their sections above.
-- **About** — the version, **Open logs folder**, **Open data folder**, **Help (online guide)**.
+- **Games**, **Game mode**, **Snapshots**, **Updates** — see their sections above. **Snapshots** also has **Export setup…** and
+  **Import setup…**.
+- **About** — the version, **Open logs folder**, **Open data folder**, **Help (online guide)**, and **Reset settings
+  to defaults…**: every page of Settings back to how NeoFences comes (asked first, a snapshot "Before reset" first). Your
+  fences, items, own presets, game folders, hidden games and chosen covers stay.
 
 ![Settings](guide/settings.png)
 
diff --git a/src/NeoFences.App/FenceHost.Setup.cs b/src/NeoFences.App/FenceHost.Setup.cs
new file mode 100644
index 0000000..09d6ab1
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.Setup.cs
@@ -0,0 +1,160 @@
+using System.IO;
+using NeoFences.Core.Config;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// The whole setup (M36, spec 2026-10-06-fence-settings-and-presets-design §3, ADR-057): Export setup… / Import setup… as
+/// one <c>.neofences</c> file, Reset settings to defaults…, and replacing the setup in place (an import, a whole snapshot).
+/// </summary>
+public sealed partial class FenceHost
+{
+    private const string SetupFilter = "NeoFences setup (*.neofences)|*.neofences";
+
+    private static string AppVersion => typeof(FenceHost).Assembly.GetName().Version?.ToString(3) ?? "";
+
+    /// <summary>Settings → Snapshots → Export setup…: writes the file; changes nothing.</summary>
+    private void ExportSetup()
+    {
+        var dialog = new Microsoft.Win32.SaveFileDialog
+        {
+            Title = "Export setup", Filter = SetupFilter, DefaultExt = SetupFile.Extension, AddExtension = true, OverwritePrompt = true,
+            FileName = $"NeoFences setup {DateTime.Now:yyyy-MM-dd}",
+            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
+        };
+        if ((_settingsWindow is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) != true) return;
+        try
+        {
+            // ponytail: written on the UI thread (a few MB of covers at most); a background write if big setups ever stutter.
+            var missing = SetupFile.Write(dialog.FileName, _config, _items, AppPaths.DataDirectory, AppVersion, DateTimeOffset.Now);
+            if (missing.Count > 0) Log.Warning("setup export: {Count} picture(s) were gone and are not in the file", missing.Count);
+            Log.Information("setup exported to {Path}", dialog.FileName);
+            _settingsWindow?.ShowSnapshotNotice($"Exported to \"{Path.GetFileName(dialog.FileName)}\".", failed: false);
+        }
+        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
+        {
+            Log.Warning(failure, "setup could not be exported to {Path}", dialog.FileName);
+            _settingsWindow?.ShowSnapshotNotice("Not exported: NeoFences could not write the file (see the log).", failed: true);
+        }
+    }
+
+    /// <summary>
+    /// Settings → Snapshots → Import setup…: the file is checked, the owner asked, the current setup saved as a whole snapshot,
+    /// then the file's setup and pictures go in and the fences reload where they are. Restoring "Before import" undoes it.
+    /// </summary>
+    private void ImportSetup()
+    {
+        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import setup", Filter = SetupFilter, CheckFileExists = true };
+        if ((_settingsWindow is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) != true) return;
+        SetupRead read;
+        try
+        {
+            read = SetupFile.Read(dialog.FileName);
+        }
+        catch (SetupFileException problem)
+        {
+            Log.Warning(problem.InnerException, "setup {Path} not imported: {Reason}", dialog.FileName, problem.Message);
+            MessageDialog.Tell(_settingsWindow, "Setup not imported", problem.Message);
+            return;
+        }
+        if (!MessageDialog.Ask(_settingsWindow, "Replace your fences, items and settings with this file's?",
+                "Your current setup is saved as a snapshot first.", primary: "Replace", secondary: "Cancel")) return;
+        if (!SaveWholeSnapshot("Before import", failureTitle: "Setup not imported")) return;
+        foreach (var (picture, failure) in SetupFile.PlacePictures(read, AppPaths.DataDirectory))
+            Log.Warning(failure, "setup import: picture {Folder}/{File} could not be written; the item shows its usual icon", picture.Folder, picture.FileName);
+        Log.Information("importing the setup from {Path} (NeoFences {Version}, {Fences} fence(s))", dialog.FileName, read.Manifest.AppVersion, read.Config.Fences.Count);
+        ReplaceSetup(read.Config, read.Items, whole: true);
+        _settingsWindow?.ShowSnapshotNotice($"Imported \"{Path.GetFileName(dialog.FileName)}\". To undo, restore the snapshot \"Before import\".", failed: false);
+    }
+
+    /// <summary>Settings → About → Reset settings to defaults…: asked, a whole snapshot first; fences, items and presets stay.</summary>
+    private void ResetSettings()
+    {
+        if (!MessageDialog.Ask(_settingsWindow, "Reset every setting to its default?",
+                "Your fences, items, own presets, game folders, hidden games and chosen covers stay. A snapshot is taken first, so restoring it undoes this.",
+                primary: "Reset", secondary: "Cancel")) return;
+        if (!SaveWholeSnapshot("Before reset", failureTitle: "Settings not reset")) return;
+        var reset = SettingsReset.Apply(_config);
+        _config = _config with { Library = reset.Library };
+        Log.Information("settings reset to their defaults");
+        ApplySettings(reset.Settings);
+        UpdateLibrary();
+        ScanLibrary(); // every launcher is read again
+    }
+
+    /// <summary>A snapshot of the whole setup (Settings and presets too); false, with the failure shown, when it could not be saved.</summary>
+    private bool SaveWholeSnapshot(string name, string failureTitle)
+    {
+        var now = DateTimeOffset.Now;
+        if (_snapshots.Save(Snapshots.TakeWhole(_config, _items, name: $"{name} ({now:d MMM HH:mm})", now: now)) is not null) return true;
+        Log.Warning(_snapshots.LastFailure, "{Name}: the snapshot could not be saved; nothing changed", name);
+        SnapshotFailure(failureTitle, "NeoFences could not save a snapshot first (see the log). Nothing changed.");
+        return false;
+    }
+
+    /// <summary>
+    /// A new setup in place (a snapshot restore; M36: an import, a whole snapshot): the fences reload where they are, their
+    /// items and looks follow, and with <paramref name="whole"/> the Settings apply one by one (each with its effect) and the
+    /// games are scanned again.
+    /// </summary>
+    private void ReplaceSetup(NeoFencesConfig config, ItemsDocument items, bool whole)
+    {
+        _undo = null; // M33: everything was replaced; nothing older to undo
+        var settings = config.Settings;
+        _config = config with { Settings = _config.Settings };
+        _items = items;
+        MigrateGames(_library.Items.Count > 0 ? _library : LibraryWriter.ReadIndex(AppPaths.LibraryDirectory)); // M22: a setup from before games became items
+        MigrateFolderViews(); // M26: a setup from before folder views became panels
+        SaveNow();
+        SyncBoxes();
+        // Windows that kept their fence still show its old title, icon size and labels (M10 final review I1).
+        foreach (var window in _windows.Values)
+        {
+            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
+            window.Refresh(shown);
+            window.SetTitle(shown.Title);
+        }
+        RefreshWindows();
+        RestyleAll(); // M36: their own looks
+        UpdateLibrary(); // M22: the fences may hold game items, or none
+        ForgetGoneTargets(); // records of items that went (M20)
+        CheckAllTargets(); // the items' targets may have changed since
+        if (whole)
+        {
+            if (settings != _config.Settings) ApplySettings(settings);
+            ScanLibrary(); // other game folders or launchers
+        }
+        RefreshAllFenceSettings(); // fences that are gone close their settings
+        RefreshSettings();
+    }
+
+    /// <summary>
+    /// Settings replaced as a whole (M36: an import, a reset, a whole snapshot): each setting through its own path, so its effect
+    /// follows (the startup entry, the desktop icons, the hotkey, the mouse hook, the look). Saved at once.
+    /// </summary>
+    private void ApplySettings(Settings target)
+    {
+        var current = _config.Settings;
+        if (target.StartWithWindows != current.StartWithWindows) SetStartWithWindows(target.StartWithWindows);
+        if (target.HideDesktopIcons != current.HideDesktopIcons) SetHideDesktopIcons(target.HideDesktopIcons);
+        if (target.QuickHideGesture != current.QuickHideGesture || target.DrawGesture != current.DrawGesture)
+            SetGestures(quickHide: target.QuickHideGesture, draw: target.DrawGesture);
+        if (target.RollupExpand != current.RollupExpand) SetRollupExpand(target.RollupExpand);
+        if (target.GameMode != current.GameMode) SetGameModeEnabled(target.GameMode);
+        if (target.AutoUpdate != current.AutoUpdate) SetAutoUpdate(target.AutoUpdate);
+        if (target.Appearance != current.Appearance) SetAppearance(target.Appearance);
+        if (target.ShowShortcutArrows != current.ShowShortcutArrows)
+        {
+            foreach (var fenceWindow in _windows.Values) fenceWindow.SetShortcutArrows(target.ShowShortcutArrows);
+        }
+        if (target.PeekHotkey != current.PeekHotkey && SetPeekHotkey(target.PeekHotkey) is (false, var problem))
+            Log.Warning("peek hotkey {Hotkey} not taken: {Problem}; the old one stays", target.PeekHotkey, problem);
+        _config = _config with { Settings = _config.Settings with { ShowShortcutArrows = target.ShowShortcutArrows, DefaultLabels = target.DefaultLabels } };
+        SaveNow();
+        RefreshSettings();
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index 6c58326..08a1409 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -930,6 +930,9 @@ public sealed partial class FenceHost
             RefreshSettings();
         };
         window.DeleteSnapshotRequested += DeleteSnapshot;
+        window.ExportSetupRequested += ExportSetup; // M36
+        window.ImportSetupRequested += ImportSetup;
+        window.ResetSettingsRequested += ResetSettings;
         WireLibrarySettings(window);
         window.OpenSnapshotsRequested += () =>
         {
@@ -1054,32 +1057,19 @@ public sealed partial class FenceHost
             return;
         }
         var now = DateTimeOffset.Now;
-        if (_snapshots.Save(Snapshots.Take(_config, _items, name: $"Before restore ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
+        // M36: before a whole snapshot (one taken before an import or a reset), "Before restore" is whole too, so it undoes the Settings.
+        var before = snapshot.Settings is null ? Snapshots.Take(_config, _items, name: $"Before restore ({now:d MMM HH:mm})", now: now)
+            : Snapshots.TakeWhole(_config, _items, name: $"Before restore ({now:d MMM HH:mm})", now: now);
+        if (_snapshots.Save(before, SnapshotStore.BeforeRestoreFileName) is null)
         {
             Log.Warning(_snapshots.LastFailure, "snapshot not restored: 'Before restore' could not be saved");
             SnapshotFailure("Snapshot not restored", "NeoFences could not save 'Before restore' first (see the log).");
             return;
         }
         Log.Information("restoring snapshot {Name} from {Path}", snapshot.Name, path);
-        (_config, _items) = Snapshots.Restore(_config, snapshot, restoredAt: DateTimeOffset.Now); // M27: rules start looking now
-        MigrateGames(_library.Items.Count > 0 ? _library : LibraryWriter.ReadIndex(AppPaths.LibraryDirectory)); // M22: a snapshot from before games became items
-        MigrateFolderViews(); // M26: a snapshot from before folder views became panels
-        SaveNow();
-        SyncBoxes();
-        // Windows that kept their fence still show its old title, icon size and labels (final review I1).
-        foreach (var window in _windows.Values)
-        {
-            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
-            window.Refresh(shown);
-            window.SetTitle(shown.Title);
-        }
-        RefreshWindows();
-        UpdateLibrary(); // M22: the restored fences may hold game items, or none
-        ForgetGoneTargets(); // records of items the restore took away (M20)
-        CheckAllTargets(); // the restored items' targets may have changed since
+        var (config, items) = Snapshots.Restore(_config, snapshot, restoredAt: DateTimeOffset.Now); // M27: rules start looking now
+        ReplaceSetup(config, items, whole: snapshot.Settings is not null); // M36: shared with Import setup…
         _settingsWindow?.ShowSnapshotNotice($"Restored \"{snapshot.Name}\".", failed: false); // replaces an earlier failure line (final review M1)
-        RefreshSettings();
-        RefreshAllFenceSettings(); // M36: fences that are gone close their settings
     }
 
     /// <summary>The snapshot list; a damaged file or an unreadable folder is logged once, not on every tray open (final review I2).</summary>
diff --git a/src/NeoFences.App/SettingsWindow.xaml b/src/NeoFences.App/SettingsWindow.xaml
index a65d00c..931f885 100644
--- a/src/NeoFences.App/SettingsWindow.xaml
+++ b/src/NeoFences.App/SettingsWindow.xaml
@@ -254,7 +254,7 @@
             <Border x:Name="SnapshotsCard" Style="{StaticResource Card}">
                 <StackPanel>
                     <TextBlock x:Name="SnapshotsDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
-                               Text="Save how your fences are arranged and put it back later. Restoring first saves 'Before restore', so it can be undone. Your files and settings are never changed." />
+                               Text="Save how your fences are arranged and put it back later. Restoring first saves 'Before restore', so it can be undone. Your files are never changed; settings come back only with 'Before import' and 'Before reset'." />
                     <ListBox x:Name="SnapshotList" MaxHeight="220" AutomationProperties.Name="Snapshots">
                         <ListBox.ItemTemplate>
                             <DataTemplate>
@@ -281,6 +281,17 @@
                     </WrapPanel>
                 </StackPanel>
             </Border>
+            <!-- M36 (spec §3): the whole setup as one file, for another PC or a fresh start. -->
+            <Border Style="{StaticResource Card}">
+                <StackPanel>
+                    <TextBlock Text="Your whole setup in one file" />
+                    <TextBlock Style="{StaticResource Description}" Text="Export saves your fences, items, settings, own presets and the pictures you chose as icons and covers to one .neofences file. Import replaces your setup with a file's; your current one is saved as a snapshot first, so Restore undoes it. Your files are never in it and never changed." />
+                    <WrapPanel Margin="0,10,0,0">
+                        <Button x:Name="ExportSetupButton" Content="Export setup…" Margin="0,0,8,6" />
+                        <Button x:Name="ImportSetupButton" Content="Import setup…" Margin="0,0,0,6" />
+                    </WrapPanel>
+                </StackPanel>
+            </Border>
 
             </StackPanel>
             <StackPanel x:Name="GamesPage" Margin="0,16,0,0" Visibility="Collapsed">
@@ -362,6 +373,16 @@
                     </StackPanel>
                 </StackPanel>
             </Border>
+            <!-- M36 (spec §3): every Settings page back to its defaults; fences, items, presets and the games' data stay. -->
+            <Border Style="{StaticResource Card}">
+                <DockPanel>
+                    <Button x:Name="ResetSettingsButton" Content="Reset settings to defaults…" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" />
+                    <StackPanel>
+                        <TextBlock Text="Reset settings" TextWrapping="Wrap" />
+                        <TextBlock Style="{StaticResource Description}" Text="Every page of Settings back to how NeoFences comes. Your fences, items, own presets, game folders, hidden games and chosen covers stay. A snapshot is taken first." />
+                    </StackPanel>
+                </DockPanel>
+            </Border>
             </StackPanel>
         </StackPanel>
         </ScrollViewer>
diff --git a/src/NeoFences.App/SettingsWindow.xaml.cs b/src/NeoFences.App/SettingsWindow.xaml.cs
index e7784f0..1eab9df 100644
--- a/src/NeoFences.App/SettingsWindow.xaml.cs
+++ b/src/NeoFences.App/SettingsWindow.xaml.cs
@@ -53,6 +53,10 @@ public partial class SettingsWindow : Window
     public event Action<string, string>? RenameSnapshotRequested;
     public event Action<string>? DeleteSnapshotRequested;
     public event Action? OpenSnapshotsRequested;
+    /// <summary>M36: Settings → Snapshots → Export setup… / Import setup…; Settings → About → Reset settings to defaults….</summary>
+    public event Action? ExportSetupRequested;
+    public event Action? ImportSetupRequested;
+    public event Action? ResetSettingsRequested;
     /// <summary>The hotkey box got (true) or lost (false) the keyboard: the host releases the Peek hotkey meanwhile (M6b review).</summary>
     public event Action<bool>? HotkeyRecording;
     public event Action<LabelMode>? DefaultLabelsChanged;
@@ -100,6 +104,9 @@ public partial class SettingsWindow : Window
         HelpButton.Click += (_, _) => HelpRequested?.Invoke();
         TakeSnapshotButton.Click += (_, _) => TakeSnapshotRequested?.Invoke();
         OpenSnapshotsButton.Click += (_, _) => OpenSnapshotsRequested?.Invoke();
+        ExportSetupButton.Click += (_, _) => ExportSetupRequested?.Invoke(); // M36
+        ImportSetupButton.Click += (_, _) => ImportSetupRequested?.Invoke();
+        ResetSettingsButton.Click += (_, _) => ResetSettingsRequested?.Invoke();
         RestoreSnapshotButton.Click += (_, _) => { if (SelectedSnapshot is { } row) RestoreSnapshotRequested?.Invoke(row.Path); };
         DeleteSnapshotButton.Click += (_, _) => { if (SelectedSnapshot is { } row) DeleteSnapshotRequested?.Invoke(row.Path); };
         RenameSnapshotButton.Click += (_, _) => BeginSnapshotRename();
```

- [ ] **Step 2: Build and test.** `dotnet build` → `0 Warning(s)`; `dotnet test` → `Passed: 808`.
- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: added Export setup, Import setup and Reset settings to defaults"` (ledger the Task 4 calls).

### Task 5: Docs

**Files:**
- Modify: `docs/DECISIONS.md` (ADR-057; ADR-038's status line), `docs/ARCHITECTURE.md` (0.23.0 paragraph after 0.22.0),
  `docs/FEATURES.md` (the "Per-fence colors / custom title fonts" row; a NeoFences-extras row), `docs/TEST-CHECKLIST.md`
  (section AV)
- Create: `docs/research/m36-fence-settings-and-presets.md`

- [ ] **Step 1: ADR-057** appended to `docs/DECISIONS.md`:

```markdown
## ADR-057 — Per-fence looks that fall back to "all fences"; presets copy; the whole setup as one file
**Date:** 2026-10-06 · **Status:** Accepted · **Amends:** ADR-038 (per-fence title fonts, now in Fence settings…), ADR-030 (whole snapshots)

**Context.** The readiness review (`research/v1-readiness.md`) found no per-fence settings beyond icon size, labels, colour
and layout, no look presets and no export / import. The owner's "one kind of fence" idea asks for per-fence settings
instead of fence types. ADR-038 had removed per-fence fonts because the fence menu's Font ▸ / Size ▸ / Weight ▸ was too deep.

**Decision.**
- **Fence settings…** (fence menu, "Tab settings…" on a tab) opens a small window in the Settings style. A fence's own look
  (`Fence.Look`: colour style, one background strength for both modes, title font / size / weight / alignment, the title bar
  on hover, spacing) overrides Settings → Appearance part by part; each part left empty is "Like all fences", so a fence
  follows later Settings changes in everything it did not set. Existing setups have no own look.
- **Presets are copies**: five built-ins (Glass, Minimal, Title strip, Solid, Compact) and the owner's own, stored in
  `config.json`. Applying one replaces the fence's own look (and sets icon size and labels where the preset has them); the
  colour stays the fence's. Changing a fence later never changes a preset or other fences.
- **The title bar on hover** keeps its row and fades: nothing moves under the pointer; it always shows while the fence is
  rolled up, renamed, dragged or its menu is open. **Spacing** is the room around each element (the elements keep their size).
- **The whole setup in one file**: Export setup… writes a `.neofences` zip (manifest, config, items, the item icon
  pictures and chosen covers they use); Import setup… checks it (a NeoFences export, readable, not newer), asks, saves a
  **whole snapshot** first ("Before import": Settings, the Games settings and own presets ride along, so a restore undoes
  everything), then replaces the setup in place. Reset settings to defaults… works the same way ("Before reset") and keeps
  fences, items, presets and the games' data. Ordinary snapshots still hold no Settings (ADR-030).

**Why not** linked presets (a fence following a preset): one more thing to explain and to break when a preset is deleted;
copies match how the owner uses them. **Why not** per-fence settings in the fence menu: ADR-038's lesson — too deep.

**Consequences.** Per-fence title fonts are back, in a window rather than the menu. `System.IO.Compression` (BCL) only; no
new dependency. The config schema stays 5: an older NeoFences ignores the new fields (and drops them if it saves).
```

  And ADR-038's status line becomes `**Date:** 2026-10-04 · **Status:** Accepted; its "one title font" amended by ADR-057 (per-fence fonts in Fence settings…) · **Amends:** ADR-036 (per-fence title fonts)`.

- [ ] **Step 2: ARCHITECTURE** — after the 0.22.0 paragraph:

```markdown
**0.23.0 (M36, fence settings and presets)**: a fence's own look (ADR-057) — `Fence.Look` (`OwnLook`, each part null =
like all fences) resolved by `FenceLook.Resolve` over Settings → Appearance, with title alignment, the title bar on hover
(the row fades; the hover poll runs for such fences) and spacing (`FenceLook.CellInset`, the `CellInset` resource around
each element). `FenceSettingsWindow` (one per fence, live; `FenceHost.FenceSettings.cs`), presets in `LookPresets` (five
built-ins, own ones in `NeoFencesConfig.Presets`). The whole setup: `SetupFile` (a `.neofences` zip; checks in Core),
`FenceHost.Setup.cs` (Export / Import / Reset; `ReplaceSetup` shared with snapshot restores, `ApplySettings` applies each
setting through its own path), whole snapshots before an import or a reset. Settings' card and switch styles live in
`SettingsStyles.xaml`. `research/m36-fence-settings-and-presets.md`.
```

- [ ] **Step 3: FEATURES** — the row `| Per-fence colors / custom title fonts | … | one title font for all fences (ADR-038) |` gets the note
  `8 swatches + Custom…; 3 colour styles; a title font for all fences, and per fence in Fence settings… (ADR-057)`; add after
  the M35 extras row:
  `| Fence settings (a look of its own per fence: style, background, title, title bar on hover, spacing), five look presets and own ones, Export / Import setup as one file, Reset settings | 0.23 (M36) | done | ADR-057 |`

- [ ] **Step 4: Checklist AV** appended to `docs/TEST-CHECKLIST.md`:

```markdown
## AV — 0.23.0 fence settings and presets (M36)

| ID | Steps | Expected |
|---|---|---|
| AV1 | Fence menu → Fence settings… (a tab: Tab settings…) | "<title> — fence settings"; every look setting "Like all fences (…)"; Like all fences again disabled |
| AV2 | Click each preset: Glass, Minimal, Title strip, Solid, Compact; then Like all fences | the fence changes at once, keeps its colour; the chip lights; Like all fences: back to the Settings look, icon size and labels stay |
| AV3 | Spacing Compact / Normal / Roomy on a fence with icons, a cover, a widget and a panel; then in a Free fence | room between elements changes, nothing clipped, covers and panels keep their size; Free elements stay in their cells |
| AV4 | Show the title bar off | the title row fades when the pointer leaves, shows on hover; while rolled up, F2-renaming, dragging or with the menu open it shows; the fence can still be moved |
| AV5 | With the window open: change View ▸ Icon size from the menu, switch the box's tab, delete the fence | the window shows the change when clicked again; edits go to the right fence; the window closes with the fence |
| AV6 | Save this look as a preset… "Mine"; a second fence's window; ✕ on "Mine"; a name "glass" | "Mine" in both windows; deleted from both, fences keep their look; "glass" refused with the message |
| AV7 | Settings → Snapshots → Export setup… | a .neofences file; nothing else changes; a notice "Exported to …" |
| AV8 | Change a few things, then Import setup… that file → Replace | "Before import" snapshot; fences reload where they are with their looks; Settings and presets as exported; restore "Before import" undoes all of it |
| AV9 | Import a file with Hide desktop icons and a Peek hotkey different from now; then About → Reset settings to defaults… | icons hide / come back, the hotkey changes (or the old one stays if taken), Start with Windows entry follows; reset keeps fences, items, presets, game folders, hidden games, chosen covers; "Before reset" undoes it |
| AV10 | Import a text file renamed .neofences, a zip without manifest, an export from a newer version | "This file is not a NeoFences setup." / "… newer NeoFences … Update NeoFences first."; nothing changes |
```

- [ ] **Step 5: Research note** `docs/research/m36-fence-settings-and-presets.md`: the two probe runs and what they showed
  (see "How this plan is written"), the fixes after the probe, the calls made while prototyping (the list above), and the
  live-check results placeholder section "Live check" filled by Task 7.
- [ ] **Step 6: Check** the strings against the code; secret scan of `git diff main`.
- [ ] **Step 7: Commit.** `git add -A && git commit -m "docs: described fence settings, presets and the setup file in ADR-057, architecture, features, checklist AV and the M36 note"`

### Task 6: Final review and fix pass

- [ ] Whole-branch review on the most capable model (Review Focus above); findings re-graded by effect; Critical/Important
  fixed in one pass, each with a failing test first where Core-testable.

### Task 7: Live check (asked first)

- [ ] On a backed-up copy of the owner's data (`m36-switch.ps1`, branch Release build): AV1–AV10 as far as scriptable
  (screenshots sent as they are taken); restore the data and the Run value; start the installed copy; refocus Terminal;
  results into the research note; commit `docs: added the M36 live check results`.
