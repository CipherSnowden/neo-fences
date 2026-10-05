# M24 — Element sizes and the fence grid (0.13.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every element in a fence spans 1–4 columns × 1–4 rows of the fence's cells (Size ▸), and every fence is Flow (packed in order) or Free (fixed positions), with drops and sorting that respect the chosen layout.

**Architecture:** Core gains `GridSpan` / `GridCell` / `FenceLayout`, `VirtualItem.Size` / `Cell`, `Fence.Layout` and a pure `FenceGrid` (arrange, cell at a point, nearest free spot, dropped placement, default spans) with item edits and repairs. The App replaces the fence list's WrapPanel with `FenceGridPanel`, sizes each element's content for its span, and adds the Size ▸ picker, Layout ▸, Free drops, Sort in Free and storing new elements' cells.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335 (unchanged), xUnit, Serilog, Velopack (unchanged).

**Spec:** `docs/superpowers/specs/2026-10-05-element-sizes-design.md` (approved 2026-10-05). Decision: ADR-046 (added by Task 3). Build notes: `docs/research/m24-element-sizes.md` (added by Task 3).

## How this plan is written

Every code change was built in a scratch prototype first (2026-10-05, worktree `neo_fences-m24proto`, branch
`m24-proto`) and probed live on a copy of the user's data: Size ▸ 2 × 2 on Steam (a big icon, gaps filled), Games covers
on 1 × 2 cells, Apps → Free keeping all 19 elements in place, drags onto taken cells landing on the nearest free spot.
Each task's patch was then replayed on a fresh worktree of `main` at `6e9af10`: the Core tests failed to compile before
the Core patch (`CS0246: 'GridElement' could not be found`) and passed after it (540), the solution built with 0 warnings
after the App patch, and the replayed tree is identical to the prototype. So each code step is **a patch to apply**:

1. Write the patch block **exactly** as shown to a file in the scratchpad (use the Write tool, never a shell heredoc).
   The file ends with one newline.
2. `git apply --whitespace=nowarn <file>` from the worktree root. If it does not apply, stop: something else changed the
   files (rule on it, never hand-merge silently).

## Where the build departs from the spec (decided while prototyping; the final review weighs them)

- `Fence.Layout` is a plain enum: config.json writes `layout` for every fence (`flow` by default). items.json writes
  `size` / `cell` only when set.
- The panel arranges at every measure (no cache; one pass over a map of cells, ~µs for 500 elements — a `ponytail:` note).
- Arrow keys use WPF's directional navigation, which follows the panel's positions (no extra code).
- The Free drop marker shows a 1 × 1 cell, not the dragged element's span.
- New elements of Free fences store the spot they show at (`PinFreeCells`), so a later addition never moves them.
- The Size ▸ squares are Borders with automation names (screen readers see the caption and Default size).
- `FenceGrid.NearestFree` measures squared distance between top-left corners (ties: upper row, then left column).

## Global Constraints

- Hard rule 1 (ADR-040): **NeoFences never modifies, moves, renames or deletes any user file.** Sizes, layouts and cells
  are NeoFences' own records.
- Hard rules 2–7 unchanged: icons always come back; no injection; Win32/COM only in NeoFences.Shell; **no new NuGet
  dependency**; failures are logged and degrade one feature, never crash (a failed layout pass falls back to 1 × 1 in
  order).
- Spans: **1–4 columns × 1–4 rows**; default **1 × 1**, **1 × 2** for a game shown as a cover; icons grow to fit, capped
  at **256** DIPs. Cell = the item cell (`ItemWidth + 8` × `icon + 12 + 36` with labels, `+ 0` labels on hover).
- items.json stays **schema 1**; config stays **schema 5**.
- UI copy, exactly: item menu "Size" ▸ 4 × 4 squares, caption "c × r" / "Default size", entry "Default size"; fence menu
  "Layout" ▸ "Flow (packed)" / "Free (fixed positions)".
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer** (CLAUDE.md overrides the
  harness reminder).
- Version stays `0.12.1` in `NeoFences.App.csproj` until the release step (outside the tasks); the release is 0.13.0.

## Review Focus

The input classes the spec implies but no automated test exercises (checklist AJ covers most by hand):

1. **The WPF panel with real containers**: selection, rubber band, hover labels, the insert caret and keyboard focus with
   spans and empty Free cells; a fence narrower than one cell; scroll position after a re-layout.
2. **Drops in Free fences**: from another fence, from Explorer, from a folder view or the library, Ctrl-duplicates, and
   several elements at once — offsets, nearest free spots, `LastDropCell` never reused by a later Add item….
3. **Icon and cover sizes**: big icons requested at the right pixel size (DPI), covers decoded at the tile's width, icon
   size and label-mode changes, a monitor DPI change.
4. **Repairs and restores**: hand-edited sizes and cells, overlapping cells after Ctrl-duplicate or a snapshot restore,
   Free fences whose stored cells exceed the current width.
5. **Performance**: a 500-entry folder view and a 100-item fence re-laying out on every change; Settings, tabs and
   roll-up with the new panel.

---

### Task 0: Worktree and baseline

- [ ] `git worktree add -b m24-element-sizes ..\neo_fences-m24 main` (main at `6e9af10` or later docs-only commits).
- [ ] `dotnet build` → 0 warnings; `dotnet test` → 526 passed.

### Task 1: Core — spans, cells, layouts and the fence grid

**Files:**
- Create: `src/NeoFences.Core/Items/GridLayout.cs`
- Modify: `src/NeoFences.Core/Items/VirtualItem.cs` (+`Size`, `Cell`), `src/NeoFences.Core/Model/Fence.cs` (+`Layout`), `src/NeoFences.Core/Items/ItemsDocument.cs` (`SetSize`, `Place`, repairs), `src/NeoFences.Core/Model/FenceEdits.cs` (`SetLayout`), `src/NeoFences.Core/Config/ConfigJson.cs` (lenient `FenceLayout`), `src/NeoFences.Core/Config/ConfigNormalizer.cs`
- Test: `tests/NeoFences.Core.Tests/Items/FenceGridTests.cs`

**Interfaces:**
- Produces: `record GridSpan(int Columns, int Rows)` (`Max = 4`, `One`, `Clamp(int columns)`); `record GridCell(int Column, int Row)`;
  `enum FenceLayout { Flow, Free }`; `record GridElement(GridSpan Span, GridCell? Stored)`;
  `record GridArrangement(IReadOnlyList<GridCell> Cells, IReadOnlyList<GridSpan> Spans, int Rows)`;
  `FenceGrid.SpanOf(VirtualItem) → GridSpan`; `FenceGrid.Arrange(IReadOnlyList<GridElement>, int columns, FenceLayout) → GridArrangement`;
  `FenceGrid.CellAt(double x, double y, double cellWidth, double cellHeight) → GridCell`;
  `FenceGrid.NearestFree(IReadOnlyList<(GridCell Cell, GridSpan Span)>, GridSpan, GridCell target, int columns) → GridCell`;
  `FenceGrid.PlaceDropped(IReadOnlyList<(GridCell Cell, GridSpan Span)> others, IReadOnlyList<(GridSpan Span, GridCell? From)> dropped, GridCell dropCell, int columns) → IReadOnlyList<GridCell>`;
  `VirtualItem.Size` (GridSpan?), `VirtualItem.Cell` (GridCell?), `Fence.Layout` (FenceLayout);
  `ItemEdits.SetSize(ItemsDocument, IReadOnlyCollection<string>, GridSpan?)`, `ItemEdits.Place(ItemsDocument, IReadOnlyDictionary<string, GridCell>)`;
  `FenceEdits.SetLayout(NeoFencesConfig, string fenceId, FenceLayout)`.

- [ ] **Step 1: Write the failing tests.** Write this patch to `m24-1-core-tests.patch` and `git apply --whitespace=nowarn` it:

````diff
diff --git a/tests/NeoFences.Core.Tests/Items/FenceGridTests.cs b/tests/NeoFences.Core.Tests/Items/FenceGridTests.cs
new file mode 100644
index 0000000..5a37253
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Items/FenceGridTests.cs
@@ -0,0 +1,126 @@
+using NeoFences.Core.Config;
+using NeoFences.Core.Items;
+using NeoFences.Core.Library;
+using NeoFences.Core.Model;
+
+namespace NeoFences.Core.Tests.Items;
+
+/// <summary>M24 (0.13.0): element sizes and the fence grid (spec 2026-10-05-element-sizes-design).</summary>
+public class FenceGridTests
+{
+    private static GridElement E(int columns = 1, int rows = 1, int? atColumn = null, int? atRow = null) =>
+        new(new GridSpan(columns, rows), atColumn is { } column ? new GridCell(column, atRow!.Value) : null);
+
+    private static IReadOnlyList<(int, int)> Cells(GridArrangement arrangement) => [.. arrangement.Cells.Select(cell => (cell.Column, cell.Row))];
+
+    [Fact]
+    public void Flow_PacksInOrder_AndFillsTheGapsBesideBigElements()
+    {
+        // 4 columns: a, then a 2x2, then four 1x1s — two fill the gap under "a" row beside the 2x2, the rest go on.
+        var arrangement = FenceGrid.Arrange([E(), E(2, 2), E(), E(), E(), E()], columns: 4, FenceLayout.Flow);
+        Assert.Equal([(0, 0), (1, 0), (3, 0), (0, 1), (3, 1), (0, 2)], Cells(arrangement));
+        Assert.Equal(3, arrangement.Rows);
+    }
+
+    [Fact]
+    public void Flow_ClampsSpansToTheFenceWidth()
+    {
+        var arrangement = FenceGrid.Arrange([E(4, 1), E()], columns: 2, FenceLayout.Flow);
+        Assert.Equal([new GridSpan(2, 1), new GridSpan(1, 1)], arrangement.Spans);
+        Assert.Equal([(0, 0), (0, 1)], Cells(arrangement));
+    }
+
+    [Fact]
+    public void Free_KeepsStoredCells_AndPutsCollisionsAndOutOfWidthElementsInFreeSpots()
+    {
+        var arrangement = FenceGrid.Arrange(
+            [E(1, 1, 2, 1), E(2, 2, 0, 0), E(1, 1, 2, 1), E(1, 1, 7, 0), E()], columns: 3, FenceLayout.Free);
+        // the 2x2 at (0,0), the first 1x1 at its (2,1); the second one at (2,1) collides, (7,0) is out of width, the last
+        // has no cell: all three go to free spots in order — (2,0), then row 2.
+        Assert.Equal([(2, 1), (0, 0), (2, 0), (0, 2), (1, 2)], Cells(arrangement));
+    }
+
+    [Fact]
+    public void Arrange_OfNothing_IsEmpty_AndColumnsAreAtLeastOne()
+    {
+        Assert.Equal(0, FenceGrid.Arrange([], columns: 5, FenceLayout.Flow).Rows);
+        Assert.Equal([(0, 0), (0, 1)], Cells(FenceGrid.Arrange([E(), E()], columns: 0, FenceLayout.Flow)));
+    }
+
+    [Theory]
+    [InlineData(0, 0, 0, 0)]
+    [InlineData(159.9, 95, 1, 0)]
+    [InlineData(160, 96, 2, 1)]
+    [InlineData(-30, -5, 0, 0)]
+    public void CellAt_FindsTheCellUnderAPoint(double x, double y, int column, int row) =>
+        Assert.Equal(new GridCell(column, row), FenceGrid.CellAt(x, y, cellWidth: 80, cellHeight: 96));
+
+    [Fact]
+    public void NearestFree_IsTheTargetWhenFree_ElseTheClosestSpotThatFits()
+    {
+        IReadOnlyList<(GridCell, GridSpan)> placed = [(new GridCell(0, 0), new GridSpan(2, 2)), (new GridCell(3, 0), new GridSpan(1, 1))];
+        Assert.Equal(new GridCell(2, 0), FenceGrid.NearestFree(placed, new GridSpan(1, 1), new GridCell(2, 0), columns: 4));
+        Assert.Equal(new GridCell(2, 0), FenceGrid.NearestFree(placed, new GridSpan(1, 1), new GridCell(1, 0), columns: 4));
+        Assert.Equal(new GridCell(0, 2), FenceGrid.NearestFree(placed, new GridSpan(2, 1), new GridCell(0, 0), columns: 4)); // under the 2x2, closer than (2,1)
+        Assert.Equal(new GridCell(0, 2), FenceGrid.NearestFree(placed, new GridSpan(4, 1), new GridCell(0, 0), columns: 4));
+    }
+
+    [Fact]
+    public void PlaceDropped_KeepsRelativeOffsets_AndMovesCollidingOnesToFreeSpots()
+    {
+        IReadOnlyList<(GridCell, GridSpan)> others = [(new GridCell(2, 0), new GridSpan(1, 1))];
+        var cells = FenceGrid.PlaceDropped(others, [(new GridSpan(1, 1), new GridCell(0, 0)), (new GridSpan(1, 1), new GridCell(1, 0))],
+            dropCell: new GridCell(1, 0), columns: 4);
+        Assert.Equal([new GridCell(1, 0), new GridCell(3, 0)], cells); // the second would land on (2,0): taken → its nearest free spot
+    }
+
+    [Fact]
+    public void SpanOf_IsTheItemsSize_OrTheDefault_CoversTall()
+    {
+        var game = GameItems.Create(new LibraryItem(new GameEntry("steam:1", "Blur", GameSource.Steam, "steam", new GameLaunch("steam://x")), "Blur.url", "s"), @"C:\lib");
+        Assert.Equal(new GridSpan(1, 2), FenceGrid.SpanOf(game));
+        Assert.Equal(new GridSpan(1, 1), FenceGrid.SpanOf(game with { ShowAs = ItemShow.Icon }));
+        Assert.Equal(new GridSpan(1, 1), FenceGrid.SpanOf(VirtualItem.Create(@"C:\a.txt")));
+        Assert.Equal(new GridSpan(3, 2), FenceGrid.SpanOf(VirtualItem.Create(@"C:\a.txt") with { Size = new GridSpan(3, 2) }));
+    }
+
+    [Fact]
+    public void SetSize_AndPlace_ChangeOnlyTheGivenItems()
+    {
+        var fenceId = Fence.NewId();
+        var a = VirtualItem.Create(@"C:\a");
+        var b = VirtualItem.Create(@"C:\b");
+        var items = new ItemsDocument().With(fenceId, [a, b]);
+        var sized = ItemEdits.SetSize(items, [a.Id], new GridSpan(2, 2));
+        Assert.Equal([new GridSpan(2, 2), null], sized.Of(fenceId).Select(item => item.Size));
+        Assert.Null(ItemEdits.SetSize(sized, [a.Id], null).Of(fenceId)[0].Size);
+        var placed = ItemEdits.Place(sized, new Dictionary<string, GridCell> { [b.Id] = new GridCell(3, 1) });
+        Assert.Equal([null, new GridCell(3, 1)], placed.Of(fenceId).Select(item => item.Cell));
+    }
+
+    [Fact]
+    public void Repair_ClampsSizesAndDropsNegativeCells_AndTheLayoutTypoIsFlow()
+    {
+        var fenceId = Fence.NewId();
+        var odd = VirtualItem.Create(@"C:\a") with { Size = new GridSpan(9, 0), Cell = new GridCell(-1, 2) };
+        var repaired = ItemEdits.Repair(new ItemsDocument().With(fenceId, [odd])).Of(fenceId)[0];
+        Assert.Equal(new GridSpan(4, 1), repaired.Size);
+        Assert.Null(repaired.Cell);
+
+        var fence = Fence.Create("x") with { Layout = FenceLayout.Free };
+        var json = ConfigJson.Serialize(NeoFencesConfig.CreateDefault() with { Fences = [fence] }).Replace("\"free\"", "\"diagonal\"");
+        Assert.Equal(FenceLayout.Flow, ConfigNormalizer.Normalize(ConfigJson.Deserialize(json)).Fences.Single().Layout);
+    }
+
+    [Fact]
+    public void ItemsJson_RoundTripsSizesAndCells_WritingThemOnlyWhenSet()
+    {
+        var fenceId = Fence.NewId();
+        var sized = VirtualItem.Create(@"C:\a") with { Size = new GridSpan(2, 3), Cell = new GridCell(1, 4) };
+        var plain = VirtualItem.Create(@"C:\b");
+        var json = ConfigJson.SerializeItems(new ItemsDocument().With(fenceId, [sized, plain]));
+        Assert.Equal(1, json.Split("\"size\"").Length - 1);
+        var back = ConfigJson.DeserializeItems(json).Of(fenceId);
+        Assert.Equal((new GridSpan(2, 3), new GridCell(1, 4)), (back[0].Size, back[0].Cell));
+    }
+}
````

- [ ] **Step 2: Run them to see them fail.** `dotnet test tests/NeoFences.Core.Tests`
  Expected: build fails with `CS0246: The type or namespace name 'GridElement' could not be found` (and `GridArrangement`).

- [ ] **Step 3: Implement.** Write this patch to `m24-1-core.patch` and apply it:

````diff
diff --git a/src/NeoFences.Core/Config/ConfigJson.cs b/src/NeoFences.Core/Config/ConfigJson.cs
index cfd926c..57da39c 100644
--- a/src/NeoFences.Core/Config/ConfigJson.cs
+++ b/src/NeoFences.Core/Config/ConfigJson.cs
@@ -25,7 +25,7 @@ public static class ConfigJson
             new LenientEnumConverter<ColourStyle>(), new LenientEnumConverter<TitleWeight>(),
             // Folder views (M21): a typo in a view's show or sort is repaired too, not the whole file lost (final review).
             new LenientEnumConverter<ViewShow>(), new LenientEnumConverter<FenceSort>(),
-            new LenientEnumConverter<Items.ItemShow>(), // M22: a typo in items.json shows the usual look, never fails the file
+            new LenientEnumConverter<Items.ItemShow>(), new LenientEnumConverter<Items.FenceLayout>(), // M24 // M22: a typo in items.json shows the usual look, never fails the file
             new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
         },
     };
diff --git a/src/NeoFences.Core/Config/ConfigNormalizer.cs b/src/NeoFences.Core/Config/ConfigNormalizer.cs
index 546b362..8e8b0f0 100644
--- a/src/NeoFences.Core/Config/ConfigNormalizer.cs
+++ b/src/NeoFences.Core/Config/ConfigNormalizer.cs
@@ -39,6 +39,7 @@ public static class ConfigNormalizer
                 Tabs = loadedFence.Tabs ?? [], // a hand-edited "tabs": null (M9 final review)
                 IconSize = IconSizes.Contains(loadedFence.IconSize) ? loadedFence.IconSize : 48,
                 CustomColor = Appearance.Argb.FromHex(loadedFence.CustomColor)?.ToHex(), // M14: a broken colour is none
+                Layout = Enum.IsDefined(loadedFence.Layout) ? loadedFence.Layout : Items.FenceLayout.Flow, // M24: a typo is Flow
             };
             fence = fence with { View = fence.IsLibrary ? null : Items.FolderViews.Normalize(loadedFence.View) }; // M21: never on the Library
             seenFenceIds.Add(fence.Id);
diff --git a/src/NeoFences.Core/Items/GridLayout.cs b/src/NeoFences.Core/Items/GridLayout.cs
new file mode 100644
index 0000000..0b5aa57
--- /dev/null
+++ b/src/NeoFences.Core/Items/GridLayout.cs
@@ -0,0 +1,160 @@
+using NeoFences.Core.Library;
+
+namespace NeoFences.Core.Items;
+
+/// <summary>An element's size in the fence's cells (M24): 1–4 columns × 1–4 rows.</summary>
+public sealed record GridSpan(int Columns, int Rows)
+{
+    public const int Max = 4;
+
+    public static GridSpan One { get; } = new(1, 1);
+
+    /// <summary>Within 1–4 each way, and no wider than the fence.</summary>
+    public GridSpan Clamp(int columns) => new(Math.Clamp(Columns, 1, Math.Clamp(columns, 1, Max)), Math.Clamp(Rows, 1, Max));
+}
+
+/// <summary>A cell of the fence's grid (M24): column and row from the top-left, both ≥ 0.</summary>
+public sealed record GridCell(int Column, int Row);
+
+/// <summary>How a fence's elements sit (M24): packed in order (today's behaviour), or at fixed positions.</summary>
+public enum FenceLayout { Flow, Free }
+
+/// <summary>One element for <see cref="FenceGrid.Arrange"/>: its span, and its stored cell (Free fences only).</summary>
+public sealed record GridElement(GridSpan Span, GridCell? Stored);
+
+/// <param name="Cells">Each element's top-left cell, in input order.</param>
+/// <param name="Spans">Each element's span as shown (clamped to the fence's width), in input order.</param>
+/// <param name="Rows">Rows used.</param>
+public sealed record GridArrangement(IReadOnlyList<GridCell> Cells, IReadOnlyList<GridSpan> Spans, int Rows);
+
+/// <summary>
+/// The fence grid (M24, spec 2026-10-05-element-sizes-design §1): where every element sits for a column count. Flow packs
+/// in order and fills gaps; Free keeps stored cells and puts the rest in free spots. Pure; one pass over a map of cells.
+/// </summary>
+public static class FenceGrid
+{
+    /// <summary>An item's span: its own size, else 1×2 for a game shown as a cover (a 2:3 poster fits), else 1×1.</summary>
+    public static GridSpan SpanOf(VirtualItem item) => item.Size ?? (GameItems.ShowsCover(item) ? new GridSpan(1, 2) : GridSpan.One);
+
+    public static GridArrangement Arrange(IReadOnlyList<GridElement> elements, int columns, FenceLayout layout)
+    {
+        columns = Math.Max(1, columns);
+        var map = new Occupancy(columns);
+        var spans = elements.Select(element => element.Span.Clamp(columns)).ToList();
+        var cells = new GridCell?[elements.Count];
+        if (layout == FenceLayout.Free)
+        {
+            for (var index = 0; index < elements.Count; index++)
+            {
+                if (elements[index].Stored is not { } stored || stored.Column < 0 || stored.Row < 0) continue;
+                if (stored.Column + spans[index].Columns > columns || !map.IsFree(stored, spans[index])) continue;
+                map.Take(stored, spans[index]);
+                cells[index] = stored;
+            }
+        }
+        for (var index = 0; index < elements.Count; index++)
+        {
+            if (cells[index] is not null) continue;
+            var cell = map.FirstFree(spans[index]);
+            map.Take(cell, spans[index]);
+            cells[index] = cell;
+        }
+        return new GridArrangement([.. cells.Select(cell => cell!)], spans, map.Rows);
+    }
+
+    /// <summary>The cell under a point of the grid's area (negative coordinates: the first row or column).</summary>
+    public static GridCell CellAt(double x, double y, double cellWidth, double cellHeight) =>
+        new(Math.Max(0, (int)Math.Floor(x / cellWidth)), Math.Max(0, (int)Math.Floor(y / cellHeight)));
+
+    /// <summary>
+    /// The free spot for <paramref name="span"/> closest to <paramref name="target"/> (top-left corners; ties: the upper,
+    /// then the left one), rows added below as needed.
+    /// </summary>
+    public static GridCell NearestFree(IReadOnlyList<(GridCell Cell, GridSpan Span)> placed, GridSpan span, GridCell target, int columns)
+    {
+        columns = Math.Max(1, columns);
+        span = span.Clamp(columns);
+        var map = new Occupancy(columns);
+        foreach (var (cell, size) in placed) map.Take(cell, size.Clamp(columns));
+        var lastRow = Math.Max(map.Rows, target.Row) + span.Rows;
+        GridCell? best = null;
+        var bestDistance = long.MaxValue;
+        for (var row = 0; row <= lastRow; row++)
+        {
+            for (var column = 0; column + span.Columns <= columns; column++)
+            {
+                var candidate = new GridCell(column, row);
+                if (!map.IsFree(candidate, span)) continue;
+                long distance = (long)(column - target.Column) * (column - target.Column) + (long)(row - target.Row) * (row - target.Row);
+                if (distance < bestDistance) (best, bestDistance) = (candidate, distance);
+            }
+        }
+        return best ?? new GridCell(0, lastRow + 1);
+    }
+
+    /// <summary>
+    /// Free fences (spec §2): dropped elements land with their top-left on <paramref name="dropCell"/>, the others of the
+    /// drag keeping their offsets from the first one's <c>From</c> cell (unknown: the drop cell); a taken spot → the nearest
+    /// free one. Returns a cell per dropped element, in order.
+    /// </summary>
+    public static IReadOnlyList<GridCell> PlaceDropped(IReadOnlyList<(GridCell Cell, GridSpan Span)> others,
+        IReadOnlyList<(GridSpan Span, GridCell? From)> dropped, GridCell dropCell, int columns)
+    {
+        var placed = others.ToList();
+        var anchor = dropped.FirstOrDefault().From;
+        var cells = new List<GridCell>();
+        foreach (var (span, from) in dropped)
+        {
+            var target = anchor is not null && from is not null
+                ? new GridCell(Math.Max(0, dropCell.Column + from.Column - anchor.Column), Math.Max(0, dropCell.Row + from.Row - anchor.Row))
+                : dropCell;
+            var cell = NearestFree(placed, span, target, columns);
+            placed.Add((cell, span.Clamp(columns)));
+            cells.Add(cell);
+        }
+        return cells;
+    }
+
+    /// <summary>Which cells are taken: a row of flags per grid row, grown as elements are placed.</summary>
+    private sealed class Occupancy(int columns)
+    {
+        private readonly List<bool[]> _rows = [];
+
+        public int Rows => _rows.Count;
+
+        public bool IsFree(GridCell cell, GridSpan span)
+        {
+            if (cell.Column + span.Columns > columns) return false;
+            for (var row = cell.Row; row < cell.Row + span.Rows && row < _rows.Count; row++)
+            {
+                for (var column = cell.Column; column < cell.Column + span.Columns; column++)
+                {
+                    if (_rows[row][column]) return false;
+                }
+            }
+            return true;
+        }
+
+        public void Take(GridCell cell, GridSpan span)
+        {
+            while (_rows.Count < cell.Row + span.Rows) _rows.Add(new bool[columns]);
+            for (var row = cell.Row; row < cell.Row + span.Rows; row++)
+            {
+                for (var column = cell.Column; column < Math.Min(columns, cell.Column + span.Columns); column++) _rows[row][column] = true;
+            }
+        }
+
+        /// <summary>Scanning rows top to bottom and columns left to right: the first cell where the whole span is free.</summary>
+        public GridCell FirstFree(GridSpan span)
+        {
+            for (var row = 0; ; row++)
+            {
+                for (var column = 0; column + span.Columns <= columns; column++)
+                {
+                    var cell = new GridCell(column, row);
+                    if (IsFree(cell, span)) return cell;
+                }
+            }
+        }
+    }
+}
diff --git a/src/NeoFences.Core/Items/ItemsDocument.cs b/src/NeoFences.Core/Items/ItemsDocument.cs
index 69650b3..816fe34 100644
--- a/src/NeoFences.Core/Items/ItemsDocument.cs
+++ b/src/NeoFences.Core/Items/ItemsDocument.cs
@@ -178,6 +178,23 @@ public static class ItemEdits
         };
     }
 
+    /// <summary>Size ▸ (M24): these items take this size (null: the default). The same document when none of them is known.</summary>
+    public static ItemsDocument SetSize(ItemsDocument document, IReadOnlyCollection<string> itemIds, GridSpan? size) =>
+        Change(document, item => itemIds.Contains(item.Id) ? item with { Size = size?.Clamp(GridSpan.Max) } : null);
+
+    /// <summary>Free fences (M24): these items (by id) store these cells.</summary>
+    public static ItemsDocument Place(ItemsDocument document, IReadOnlyDictionary<string, GridCell> cells) =>
+        Change(document, item => cells.TryGetValue(item.Id, out var cell) ? item with { Cell = cell } : null);
+
+    private static ItemsDocument Change(ItemsDocument document, Func<VirtualItem, VirtualItem?> change)
+    {
+        if (!document.Fences.Values.Any(items => items.Any(item => change(item) is not null))) return document;
+        return document with
+        {
+            Fences = document.Fences.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<VirtualItem>)entry.Value.Select(item => change(item) ?? item).ToList()),
+        };
+    }
+
     /// <summary>Picture files in <c>icons\</c> some item still uses (the others are deleted at start).</summary>
     public static IReadOnlySet<string> ImagesInUse(ItemsDocument document) =>
         document.Fences.Values.SelectMany(items => items).Select(item => item.Icon?.Image).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
@@ -199,6 +216,8 @@ public static class ItemEdits
                     Id = string.IsNullOrWhiteSpace(item.Id) || !seenIds.Add(item.Id) ? VirtualItem.NewId() : item.Id,
                     Target = item.Target.Trim(),
                     Icon = item.Icon is { File: null or "", Image: null or "" } ? null : item.Icon,
+                    Size = item.Size?.Clamp(GridSpan.Max), // M24: a hand-edited size within 1–4 each way
+                    Cell = item.Cell is { Column: >= 0, Row: >= 0 } ? item.Cell : null,
                 })
                 .ToList();
         }
diff --git a/src/NeoFences.Core/Items/VirtualItem.cs b/src/NeoFences.Core/Items/VirtualItem.cs
index 2914deb..bc8fabf 100644
--- a/src/NeoFences.Core/Items/VirtualItem.cs
+++ b/src/NeoFences.Core/Items/VirtualItem.cs
@@ -52,6 +52,12 @@ public sealed record VirtualItem
     /// <summary>Null: the item's usual look (a game's cover tile).</summary>
     public ItemShow? ShowAs { get; init; }
 
+    /// <summary>Its size in the fence's cells (M24); null: the default (<see cref="FenceGrid.SpanOf"/>).</summary>
+    public GridSpan? Size { get; init; }
+
+    /// <summary>Its stored cell in a Free fence (M24); unused in Flow fences.</summary>
+    public GridCell? Cell { get; init; }
+
     [JsonIgnore]
     public ItemKind Kind => ItemKinds.Of(Target);
 
diff --git a/src/NeoFences.Core/Model/Fence.cs b/src/NeoFences.Core/Model/Fence.cs
index aa8a62c..ff41d39 100644
--- a/src/NeoFences.Core/Model/Fence.cs
+++ b/src/NeoFences.Core/Model/Fence.cs
@@ -21,6 +21,9 @@ public sealed record Fence
     /// <summary>A folder view (M21): the fence shows this folder live instead of virtual items. Never set on the Library.</summary>
     public FolderView? View { get; init; }
 
+    /// <summary>How its elements sit (M24): packed in order, or at fixed positions.</summary>
+    public Items.FenceLayout Layout { get; init; }
+
     [System.Text.Json.Serialization.JsonIgnore]
     public FenceKind Kind => IsLibrary ? FenceKind.Library : View is not null ? FenceKind.View : FenceKind.Items;
 
diff --git a/src/NeoFences.Core/Model/FenceEdits.cs b/src/NeoFences.Core/Model/FenceEdits.cs
index 7b59f44..6314f8a 100644
--- a/src/NeoFences.Core/Model/FenceEdits.cs
+++ b/src/NeoFences.Core/Model/FenceEdits.cs
@@ -41,6 +41,10 @@ public static class FenceEdits
     public static NeoFencesConfig SetRolledUp(NeoFencesConfig config, string fenceId, bool rolledUp) =>
         config.WithFence(Require(config, fenceId) with { RolledUp = rolledUp });
 
+    /// <summary>Layout ▸ (M24): packed in order, or at fixed positions.</summary>
+    public static NeoFencesConfig SetLayout(NeoFencesConfig config, string fenceId, Items.FenceLayout layout) =>
+        config.WithFence(Require(config, fenceId) with { Layout = layout });
+
     /// <summary>Icon-only (M8b): labels always shown, or only on hover / selection.</summary>
     public static NeoFencesConfig SetLabels(NeoFencesConfig config, string fenceId, LabelMode labels) =>
         config.WithFence(Require(config, fenceId) with { Labels = labels });
````

- [ ] **Step 4: Run the tests.** `dotnet test tests/NeoFences.Core.Tests`
  Expected: `Passed! - Failed: 0, Passed: 540`.

- [ ] **Step 5: Commit.** `git add -A && git commit -m "feat: added element sizes and the fence grid to the core: spans, cells, flow and free layouts"`

### Task 2: App — the grid panel, Size ▸ and Layout ▸

**Files:**
- Create: `src/NeoFences.App/FenceGridPanel.cs`, `src/NeoFences.App/SizePicker.cs`, `src/NeoFences.App/FenceHost.Grid.cs`
- Modify: `FenceWindow.xaml(.cs)` (the panel, per-element content sizes, Layout ▸, Free drop cell and marker),
  `FenceItemView.cs` (`Span`, `StoredCell`, `ContentWidth`, `IconDips`, `TileWidth`, `TileHeight`, `ApplySize`),
  `FenceHost.cs` (spans and cells into `ShownItem`, Layout wiring), `FenceHost.Items.cs` (Size ▸ in the item menus,
  Free drops, Sort in Free, storing new cells), `FenceHost.GameItems.cs` (Size ▸ for game items)

**Interfaces:**
- Consumes: everything Task 1 produces.
- Produces: `FenceGridPanel` (`CellWidth`, `CellHeight`, `Layout`, `Columns`, `Arrangement`, `CellAt(Point)`,
  `CellRect(GridCell, GridSpan)`); `FenceWindow.LayoutRequested`, `Columns`, `CurrentCells()`, `CurrentLayout()`,
  `LastDropCell`, `ForgetDropCell()`; `SizePicker.Create(ContextMenu, GridSpan?, Action<GridSpan?>)`.

- [ ] **Step 1: Implement.** Write this patch to `m24-2-app.patch` and apply it:

````diff
diff --git a/src/NeoFences.App/FenceGridPanel.cs b/src/NeoFences.App/FenceGridPanel.cs
new file mode 100644
index 0000000..ca6036d
--- /dev/null
+++ b/src/NeoFences.App/FenceGridPanel.cs
@@ -0,0 +1,72 @@
+using System.Windows;
+using System.Windows.Controls;
+using NeoFences.Core.Items;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// The fence's items panel (M24, spec 2026-10-05-element-sizes-design §3): every element on whole cells, spanning its
+/// columns × rows, where Core's <see cref="FenceGrid"/> puts it (packed in order, or at stored cells). The columns follow
+/// the fence's width; a layout pass that fails falls back to one element per cell in order.
+/// </summary>
+public sealed class FenceGridPanel : Panel
+{
+    public static readonly DependencyProperty CellWidthProperty = DependencyProperty.Register(nameof(CellWidth), typeof(double),
+        typeof(FenceGridPanel), new FrameworkPropertyMetadata(84.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
+
+    public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(nameof(CellHeight), typeof(double),
+        typeof(FenceGridPanel), new FrameworkPropertyMetadata(96.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
+
+    public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(nameof(Layout), typeof(FenceLayout),
+        typeof(FenceGridPanel), new FrameworkPropertyMetadata(FenceLayout.Flow, FrameworkPropertyMetadataOptions.AffectsMeasure));
+
+    public double CellWidth { get => (double)GetValue(CellWidthProperty); set => SetValue(CellWidthProperty, value); }
+    public double CellHeight { get => (double)GetValue(CellHeightProperty); set => SetValue(CellHeightProperty, value); }
+    public FenceLayout Layout { get => (FenceLayout)GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }
+
+    /// <summary>The column count of the last layout pass (the fence's width over the cell width, at least one).</summary>
+    public int Columns { get; private set; } = 1;
+
+    /// <summary>The last layout: each child's cell and span, in child order.</summary>
+    public GridArrangement Arrangement { get; private set; } = new([], [], 0);
+
+    // ponytail: arranged at every measure (one pass over a map of cells, ~µs for 500 elements); cache per element set if a profile ever shows it.
+    protected override Size MeasureOverride(Size availableSize)
+    {
+        var children = InternalChildren.Cast<UIElement>().ToList();
+        Columns = double.IsInfinity(availableSize.Width) ? Math.Max(1, children.Count) : Math.Max(1, (int)Math.Floor(availableSize.Width / CellWidth));
+        var elements = children.Select(child => (child as FrameworkElement)?.DataContext is FenceItemView view
+            ? new GridElement(view.Span, view.StoredCell) : new GridElement(GridSpan.One, null)).ToList();
+        try
+        {
+            Arrangement = FenceGrid.Arrange(elements, Columns, Layout);
+        }
+        catch (Exception failure) when (failure is not OutOfMemoryException)
+        {
+            Serilog.Log.Warning(failure, "fence grid: layout failed; one element per cell in order"); // never a crash (spec §4)
+            Arrangement = FenceGrid.Arrange([.. elements.Select(_ => new GridElement(GridSpan.One, null))], Columns, FenceLayout.Flow);
+        }
+        for (var index = 0; index < children.Count; index++)
+        {
+            var span = Arrangement.Spans[index];
+            children[index].Measure(new Size(span.Columns * CellWidth, span.Rows * CellHeight));
+        }
+        var width = double.IsInfinity(availableSize.Width) ? Columns * CellWidth : Math.Min(availableSize.Width, Columns * CellWidth);
+        return new Size(width, Arrangement.Rows * CellHeight);
+    }
+
+    protected override Size ArrangeOverride(Size finalSize)
+    {
+        var children = InternalChildren.Cast<UIElement>().ToList();
+        for (var index = 0; index < children.Count && index < Arrangement.Cells.Count; index++)
+        {
+            children[index].Arrange(CellRect(Arrangement.Cells[index], Arrangement.Spans[index]));
+        }
+        return finalSize;
+    }
+
+    /// <summary>The cell under a point of the panel.</summary>
+    public GridCell CellAt(Point point) => FenceGrid.CellAt(point.X, point.Y, CellWidth, CellHeight);
+
+    public Rect CellRect(GridCell cell, GridSpan span) => new(cell.Column * CellWidth, cell.Row * CellHeight, span.Columns * CellWidth, span.Rows * CellHeight);
+}
diff --git a/src/NeoFences.App/FenceHost.GameItems.cs b/src/NeoFences.App/FenceHost.GameItems.cs
index f9ba323..186e5c7 100644
--- a/src/NeoFences.App/FenceHost.GameItems.cs
+++ b/src/NeoFences.App/FenceHost.GameItems.cs
@@ -132,6 +132,7 @@ public sealed partial class FenceHost
         Command(showAs, "Cover tile", () => SetShowAs(item.Id, ItemShow.Cover), isChecked: GameItems.ShowsCover(item));
         Command(showAs, "Icon", () => SetShowAs(item.Id, ItemShow.Icon), isChecked: !GameItems.ShowsCover(item));
         menu.Items.Add(showAs);
+        menu.Items.Add(SizeMenu(menu, [item])); // M24
         var openFolder = new MenuItem { Header = "Open install folder", IsEnabled = installed && LibraryItemOf(item.Target)?.Game.InstallFolder is not null }; // M23
         openFolder.Click += (_, _) => OpenInstallFolder(window, item.Target);
         menu.Items.Add(openFolder);
diff --git a/src/NeoFences.App/FenceHost.Grid.cs b/src/NeoFences.App/FenceHost.Grid.cs
new file mode 100644
index 0000000..e291549
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.Grid.cs
@@ -0,0 +1,87 @@
+using System.Windows.Controls;
+using NeoFences.Core.Items;
+using NeoFences.Core.Model;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Element sizes and the fence grid (M24, spec 2026-10-05-element-sizes-design, ADR-046): Size ▸ on items, Layout ▸ Flow /
+/// Free per fence, and where dropped or sorted elements land in Free fences. Only NeoFences' own records change.
+/// </summary>
+public sealed partial class FenceHost
+{
+    /// <summary>The Size ▸ entry for these items (an items fence only).</summary>
+    private MenuItem SizeMenu(ContextMenu menu, IReadOnlyList<VirtualItem> items) =>
+        SizePicker.Create(menu, items.Count == 1 ? items[0].Size : null, size => SetSize([.. items.Select(item => item.Id)], size));
+
+    private void SetSize(IReadOnlyList<string> itemIds, GridSpan? size)
+    {
+        _items = ItemEdits.SetSize(_items, itemIds, size);
+        Log.Information("{Count} item(s) sized {Size}", itemIds.Count, size is { } span ? $"{span.Columns}x{span.Rows}" : "default");
+        ItemsChanged(checkTargets: []);
+    }
+
+    /// <summary>
+    /// Layout ▸ (spec §2): to Free, every element first stores the cell it shows at now, so nothing moves; to Flow, the
+    /// order of the list stays (stored cells stay, unused).
+    /// </summary>
+    private void SetFenceLayout(FenceWindow window, FenceLayout layout)
+    {
+        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { Kind: FenceKind.Items } fence || fence.Layout == layout) return;
+        if (layout == FenceLayout.Free) _items = ItemEdits.Place(_items, window.CurrentCells());
+        _config = FenceEdits.SetLayout(_config, fence.Id, layout);
+        Log.Information("fence {FenceId} layout: {Layout}", fence.Id, layout);
+        window.Refresh(_config.Fences.First(candidate => candidate.Id == fence.Id));
+        ItemsChanged(checkTargets: []);
+    }
+
+    private bool IsFree(string fenceId) => _config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.Layout == FenceLayout.Free;
+
+    /// <summary>
+    /// Items that just arrived in a Free fence (a drag, a drop from outside, Ctrl+drag) land on the drop cell, the others of
+    /// the drag keeping their offsets (their cells where they came from); a taken spot → the nearest free one.
+    /// </summary>
+    private void PlaceDropped(FenceWindow window, IReadOnlyList<string> itemIds, IReadOnlyList<string?> sourceKeys)
+    {
+        if (!IsFree(window.FenceId) || window.LastDropCell is not { } dropCell || itemIds.Count == 0) return;
+        var arriving = itemIds.ToHashSet(StringComparer.Ordinal);
+        var others = window.CurrentLayout().Where(entry => !arriving.Contains(entry.Key) && !sourceKeys.Contains(entry.Key))
+            .Select(entry => (entry.Cell, entry.Span)).ToList();
+        var shownCells = _windows.Values.SelectMany(shown => shown.CurrentCells()).GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.First().Value);
+        var dropped = itemIds.Select((id, index) => (Span: _items.Find(id) is { } item ? FenceGrid.SpanOf(item) : GridSpan.One,
+            From: sourceKeys.ElementAtOrDefault(index) is { } key && shownCells.TryGetValue(key, out var from) ? from : (GridCell?)null)).ToList();
+        var cells = FenceGrid.PlaceDropped(others, dropped, dropCell, window.Columns);
+        _items = ItemEdits.Place(_items, itemIds.Select((id, index) => (id, cells[index])).ToDictionary(pair => pair.id, pair => pair.Item2, StringComparer.Ordinal));
+        window.ForgetDropCell(); // a later addition (Add item…) never lands on an old drop cell
+    }
+
+    /// <summary>
+    /// Elements of a Free fence that have no stored cell yet (added by Add item…, Add games…, new games) store the free spot
+    /// they show at, so a later addition never moves them.
+    /// </summary>
+    private void PinFreeCells()
+    {
+        var cells = new Dictionary<string, GridCell>(StringComparer.Ordinal);
+        foreach (var window in _windows.Values.Where(window => IsFree(window.FenceId)))
+        {
+            var items = _items.Of(window.FenceId);
+            if (items.All(item => item.Cell is not null)) continue;
+            var arrangement = FenceGrid.Arrange([.. items.Select(item => new GridElement(FenceGrid.SpanOf(item), item.Cell))], window.Columns, FenceLayout.Free);
+            for (var index = 0; index < items.Count; index++)
+            {
+                if (items[index].Cell is null) cells[items[index].Id] = arrangement.Cells[index];
+            }
+        }
+        if (cells.Count > 0) _items = ItemEdits.Place(_items, cells);
+    }
+
+    /// <summary>Sort by in a Free fence: packed from the top-left in the new order, and those cells stored (spec §2).</summary>
+    private void PackFreeFence(FenceWindow window)
+    {
+        if (!IsFree(window.FenceId)) return;
+        var items = _items.Of(window.FenceId);
+        var arrangement = FenceGrid.Arrange([.. items.Select(item => new GridElement(FenceGrid.SpanOf(item), null))], window.Columns, FenceLayout.Flow);
+        _items = ItemEdits.Place(_items, items.Select((item, index) => (item.Id, arrangement.Cells[index])).ToDictionary(pair => pair.Id, pair => pair.Item2, StringComparer.Ordinal));
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.Items.cs b/src/NeoFences.App/FenceHost.Items.cs
index 801f875..d5b34be 100644
--- a/src/NeoFences.App/FenceHost.Items.cs
+++ b/src/NeoFences.App/FenceHost.Items.cs
@@ -112,6 +112,7 @@ public sealed partial class FenceHost
         if (items.Count > 1)
         {
             Command("Open", () => { foreach (var item in items) OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin); });
+            menu.Items.Add(SizeMenu(menu, items)); // M24
             Command($"Remove {items.Count} items from fence", () => RemoveItems(window, [.. items.Select(item => item.Id)]));
         }
         else if (GameItems.IsGame(items[0]))
@@ -136,6 +137,7 @@ public sealed partial class FenceHost
             if (onDisk && check.IsFolder && check.State == TargetState.Ok) Command("Show as folder view", () => ShowAsFolderView(item.Target)); // M21
             Command("Copy path", () => CopyText(item.Target));
             menu.Items.Add(new Separator());
+            menu.Items.Add(SizeMenu(menu, [item])); // M24
             Command("Properties…", () => ShowProperties(window, item.Id, focusName: false));
             if (check.State == TargetState.Ok) Command("Remove from fence", () => RemoveItems(window, [item.Id]));
             menu.Items.Add(new Separator());
@@ -405,6 +407,7 @@ public sealed partial class FenceHost
     /// <summary>After any item change: windows, save, watching, and a check of the targets that are new.</summary>
     private void ItemsChanged(IReadOnlyList<string> checkTargets)
     {
+        PinFreeCells(); // M24: new elements of Free fences keep the spot they show at
         RefreshWindows();
         ScheduleSave();
         UpdateWatching();
@@ -423,7 +426,17 @@ public sealed partial class FenceHost
         var fromLibrary = keys.Except(known).ToList(); // the library's and a view's key is a path (M12, M21): they become items
         if (known.Count > 0)
         {
-            _items = duplicate ? ItemEdits.Duplicate(_items, known, window.FenceId, insertAt).Document : ItemEdits.Move(_items, known, window.FenceId, insertAt);
+            if (duplicate)
+            {
+                var copies = ItemEdits.Duplicate(_items, known, window.FenceId, insertAt);
+                _items = copies.Document;
+                PlaceDropped(window, copies.NewIds, known); // M24: a Free fence puts them on the drop cell
+            }
+            else
+            {
+                _items = ItemEdits.Move(_items, known, window.FenceId, insertAt);
+                PlaceDropped(window, known, known);
+            }
             Log.Information("{Count} item(s) {Action} to fence {FenceId}", known.Count, duplicate ? "duplicated" : "moved", window.FenceId);
         }
         if (fromLibrary.Count > 0) AddTargets(window, fromLibrary, insertAt);
@@ -440,6 +453,7 @@ public sealed partial class FenceHost
     {
         var added = ItemEdits.Add(_items, window.FenceId, [.. targets.Select(VirtualItem.Create)], insertAt);
         _items = added.Document;
+        PlaceDropped(window, added.AddedIds, [.. added.AddedIds.Select(_ => (string?)null)]); // M24: a Free fence puts them on the drop cell
         Log.Information("{Added} item(s) added to fence {FenceId}; {Already} already there", added.AddedIds.Count, window.FenceId, added.AlreadyThereIds.Count);
         ItemsChanged(checkTargets: targets);
         if (added.AlreadyThereIds.Count > 0) window.SelectItems(added.AlreadyThereIds); // already in this fence: it flashes
@@ -500,6 +514,7 @@ public sealed partial class FenceHost
             try
             {
                 _items = ItemEdits.Reorder(_items, fenceId, sorted.Result); // the fence changed meanwhile: ArgumentException, its order stays
+                PackFreeFence(window); // M24: a Free fence is laid out packed in the new order
             }
             catch (Exception failure) when (failure is ArgumentException or AggregateException)
             {
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index d57021f..90f349e 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -269,6 +269,7 @@ public sealed partial class FenceHost
         window.NewLibraryRequested += CreateLibraryFence;
         window.NewFolderViewRequested += () => NewFolderView(window.Handle); // M21
         window.AddGamesRequested += () => AddGames(window); // M22
+        window.LayoutRequested += layout => SetFenceLayout(window, layout); // M24
         window.OpenFolderRequested += () => OpenViewFolder(window);
         window.ViewSettingsRequested += () => EditFolderView(window);
         window.StartupToggled += SetStartWithWindows;
@@ -337,7 +338,8 @@ public sealed partial class FenceHost
         }
         var covers = _items.Of(shown.Id).Any(GameItems.ShowsCover) ? LibraryArt() : null; // M22: game items shown as covers
         window.SetItems([.. _items.Of(shown.Id).Select(item => new ShownItem(item.Id, item.Target, item.OwnName, item.Icon, item.Note, StateOf(item.Target),
-            Tile: GameItems.ShowsCover(item), TileArt: covers is not null && covers.TryGetValue(item.Target, out var cover) ? cover : null, IsGame: GameItems.IsGame(item)))]);
+            Tile: GameItems.ShowsCover(item), TileArt: covers is not null && covers.TryGetValue(item.Target, out var cover) ? cover : null, IsGame: GameItems.IsGame(item),
+            Span: FenceGrid.SpanOf(item), Cell: item.Cell))]); // M24
     }
 
     /// <summary>Opens a path NeoFences knows (a library game, the logs or data folder).</summary>
diff --git a/src/NeoFences.App/FenceItemView.cs b/src/NeoFences.App/FenceItemView.cs
index 8e79f4b..7883b70 100644
--- a/src/NeoFences.App/FenceItemView.cs
+++ b/src/NeoFences.App/FenceItemView.cs
@@ -12,8 +12,11 @@ namespace NeoFences.App;
 /// </summary>
 /// <param name="Tile">A 2:3 tile (M22: a game item shown as its cover); <paramref name="TileArt"/> is its poster or logo.</param>
 /// <param name="IsGame">A game item (M22): "Not installed" instead of "Missing".</param>
+/// <param name="Span">Its size in cells (M24); null: 1×2 for a tile, else 1×1.</param>
+/// <param name="Cell">Its stored cell (M24, Free fences).</param>
 public sealed record ShownItem(string Key, string Target, string? Name = null, ItemIcon? Icon = null, string? Note = null,
-    TargetState State = TargetState.Ok, bool Tile = false, (string Path, bool IsPoster)? TileArt = null, bool IsGame = false);
+    TargetState State = TargetState.Ok, bool Tile = false, (string Path, bool IsPoster)? TileArt = null, bool IsGame = false,
+    GridSpan? Span = null, GridCell? Cell = null);
 
 /// <summary>One item as a fence shows it. Label and icon start as placeholders and fill in from <see cref="IconLoader"/>.</summary>
 public sealed class FenceItemView : INotifyPropertyChanged
@@ -81,6 +84,8 @@ public sealed class FenceItemView : INotifyPropertyChanged
         Tile = shown.Tile;
         TileArt = shown.TileArt;
         IsGame = shown.IsGame;
+        Span = shown.Span ?? (shown.Tile ? new GridSpan(1, 2) : GridSpan.One);
+        StoredCell = shown.Cell;
         State = shown.State;
         if (OwnName is not null) Label = OwnName;
         else if (reload || Label.Length == 0) Label = PlaceholderName(Target);
@@ -100,6 +105,37 @@ public sealed class FenceItemView : INotifyPropertyChanged
         _ => Path.GetFileNameWithoutExtension(target.TrimEnd('\\')) is { Length: > 0 } name ? name : target,
     };
 
+    /// <summary>Its size in the fence's cells (M24).</summary>
+    public GridSpan Span { get; private set; } = GridSpan.One;
+
+    /// <summary>Its stored cell (M24, Free fences), or null.</summary>
+    public GridCell? StoredCell { get; private set; }
+
+    /// <summary>Width of its content (icon or tile and the label), in DIPs: its span's cells less the cell padding (M24).</summary>
+    public double ContentWidth { get; private set { field = value; Changed(); } } = 68;
+
+    /// <summary>Its icon's size in DIPs (M24): the fence's icon size at 1×1, bigger to fill a bigger span (≤ 256).</summary>
+    public double IconDips { get; private set { field = value; Changed(); } } = 48;
+
+    public double TileWidth { get; private set { field = value; Changed(); } } = 72;
+    public double TileHeight { get; private set { field = value; Changed(); } } = 108;
+
+    /// <summary>
+    /// Sizes its content for its span on cells of this size (M24, spec §1). True when the icon size changed (the icon is
+    /// loaded again at the new size).
+    /// </summary>
+    public bool ApplySize(double cellWidth, double cellHeight, double iconDips, double labelHeight)
+    {
+        const double CellPaddingX = 8, CellPaddingY = 12, MaxIcon = 256;
+        var width = Span.Columns * cellWidth - CellPaddingX;
+        var height = Span.Rows * cellHeight - CellPaddingY - labelHeight;
+        var icon = Span == GridSpan.One ? iconDips : Math.Clamp(Math.Floor(Math.Min(width - 8, height)), iconDips, MaxIcon);
+        var tileHeight = Math.Max(16, Math.Floor(Math.Min(height, (width - 4) * 1.5)));
+        var changed = Math.Abs(icon - IconDips) > 0.5;
+        (ContentWidth, IconDips, TileWidth, TileHeight) = (width, icon, Math.Floor(tileHeight / 1.5), tileHeight);
+        return changed;
+    }
+
     /// <summary>Counts icon requests (UI thread): a slower, older load (another size or target) never wins (final review I4).</summary>
     public int IconRequest { get; set; }
 
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index 80ab9a6..00b4de5 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -1,6 +1,7 @@
 <Window x:Class="NeoFences.App.FenceWindow"
         xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        xmlns:local="clr-namespace:NeoFences.App"
         Title="NeoFences fence" Width="320" Height="220"
         WindowStyle="None" ResizeMode="CanResize" AllowsTransparency="True"
         Background="#01000000" ShowInTaskbar="False" ShowActivated="False">
@@ -96,6 +97,10 @@
                             <MenuItem x:Name="LabelsOnHoverItem" Header="On hover (icons only)" IsCheckable="True" />
                         </MenuItem>
                         <MenuItem x:Name="SortItem" Header="Sort by" />
+                        <MenuItem x:Name="LayoutItem" Header="Layout">
+                            <MenuItem x:Name="LayoutFlowItem" Header="Flow (packed)" IsCheckable="True" />
+                            <MenuItem x:Name="LayoutFreeItem" Header="Free (fixed positions)" IsCheckable="True" />
+                        </MenuItem>
                         <MenuItem x:Name="TabColorItem" Header="Colour" />
                         <MenuItem x:Name="DetachTabItem" Header="Detach tab" Visibility="Collapsed" />
                         <MenuItem x:Name="LockItem" Header="Lock position" IsCheckable="True" />
@@ -117,7 +122,8 @@
                              ScrollViewer.VerticalScrollBarVisibility="Auto">
                         <ListBox.ItemsPanel>
                             <ItemsPanelTemplate>
-                                <WrapPanel />
+                                <!-- M24: whole cells, elements spanning 1–4 × 1–4, packed (Flow) or at stored cells (Free). -->
+                                <local:FenceGridPanel />
                             </ItemsPanelTemplate>
                         </ListBox.ItemsPanel>
                         <ListBox.ItemContainerStyle>
@@ -135,7 +141,7 @@
                                                  pop-under name does not blink between neighbours (final review I2). -->
                                             <Border Background="Transparent" Padding="2">
                                                 <Border x:Name="Chrome" Background="Transparent" CornerRadius="4" Padding="2,4">
-                                                    <ContentPresenter />
+                                                    <ContentPresenter HorizontalAlignment="Center" />
                                                 </Border>
                                             </Border>
                                             <ControlTemplate.Triggers>
@@ -153,9 +159,9 @@
                         </ListBox.ItemContainerStyle>
                         <ListBox.ItemTemplate>
                             <DataTemplate>
-                                <StackPanel x:Name="Cell" Width="{DynamicResource ItemWidth}">
+                                <StackPanel x:Name="Cell" Width="{Binding ContentWidth}">
                                     <Grid x:Name="IconGrid" HorizontalAlignment="Center">
-                                        <Image x:Name="ItemIcon" Source="{Binding Icon}" Width="{DynamicResource IconSize}" Height="{DynamicResource IconSize}" />
+                                        <Image x:Name="ItemIcon" Source="{Binding Icon}" Width="{Binding IconDips}" Height="{Binding IconDips}" />
                                         <!-- Missing target (M18 spec §4): a small warning badge on the dimmed icon. -->
                                         <Border x:Name="MissingBadge" Visibility="Collapsed" HorizontalAlignment="Right" VerticalAlignment="Bottom"
                                                 Width="16" Height="16" CornerRadius="8" Background="#FFE8A33D" BorderBrush="#FF3A2A10" BorderThickness="1"
@@ -174,9 +180,9 @@
                                     </Grid>
                                     <!-- Game Library tile (M12): poster, logo, or the icon centred on a dark 2:3 tile. -->
                                     <Border x:Name="Tile" Visibility="Collapsed" HorizontalAlignment="Center" CornerRadius="4" Background="#66000000"
-                                            Width="{DynamicResource TileWidth}" Height="{DynamicResource TileHeight}" ClipToBounds="True">
+                                            Width="{Binding TileWidth}" Height="{Binding TileHeight}" ClipToBounds="True">
                                         <Grid>
-                                            <Image x:Name="TileIcon" Source="{Binding Icon}" Width="{DynamicResource IconSize}" Height="{DynamicResource IconSize}" />
+                                            <Image x:Name="TileIcon" Source="{Binding Icon}" Width="{Binding IconDips}" Height="{Binding IconDips}" />
                                             <Image x:Name="TileLogo" Source="{Binding Art}" Margin="10" Stretch="Uniform" Visibility="Collapsed" />
                                             <Image x:Name="TilePoster" Source="{Binding Art}" Stretch="UniformToFill" Visibility="Collapsed" />
                                             <!-- A not-installed game shown as its cover (M22, final review I1): the same badge as on icons. -->
@@ -195,7 +201,6 @@
                                 </StackPanel>
                                 <DataTemplate.Triggers>
                                     <DataTrigger Binding="{Binding IsTile}" Value="True">
-                                        <Setter TargetName="Cell" Property="Width" Value="{DynamicResource TileCellWidth}" />
                                         <Setter TargetName="IconGrid" Property="Visibility" Value="Collapsed" />
                                         <Setter TargetName="Tile" Property="Visibility" Value="Visible" />
                                     </DataTrigger>
@@ -239,6 +244,9 @@
                         <Rectangle x:Name="SelectionBand" Visibility="Collapsed" Fill="{DynamicResource FenceHover}"
                                    Stroke="{DynamicResource FenceSubtleText}" StrokeThickness="1" RadiusX="2" RadiusY="2" />
                         <!-- Drag-drop feedback (M3b review I2): where a drop lands. -->
+                        <!-- M24: where a drop lands in a Free fence. -->
+                        <Rectangle x:Name="DropCellMarker" Visibility="Collapsed" Stroke="{DynamicResource FenceText}" StrokeThickness="1.5"
+                                   StrokeDashArray="3 2" RadiusX="4" RadiusY="4" Fill="{DynamicResource FenceHover}" />
                         <Rectangle x:Name="InsertCaret" Visibility="Collapsed" Width="2" Fill="{DynamicResource FenceText}" RadiusX="1" RadiusY="1" />
                         <!-- Icon-only fences (M8b): the hovered or selected item's name, under its icon, over the neighbours. -->
                         <Border x:Name="HoverLabel" Visibility="Collapsed" CornerRadius="4" Padding="6,2"
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index e366804..02817e1 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -149,6 +149,8 @@ public partial class FenceWindow : Window
     public event Action? RefreshRequested;
     /// <summary>Fence menu → "New Game Library fence" (M12).</summary>
     public event Action? NewLibraryRequested;
+    /// <summary>Fence menu → Layout ▸ Flow / Free (M24).</summary>
+    public event Action<FenceLayout>? LayoutRequested;
     /// <summary>Fence menu → "Add games…" (M22).</summary>
     public event Action? AddGamesRequested;
     /// <summary>Fence menu → "New folder view…" (M21).</summary>
@@ -210,6 +212,8 @@ public partial class FenceWindow : Window
         NewLibraryItem.Click += (_, _) => NewLibraryRequested?.Invoke();
         NewFolderViewItem.Click += (_, _) => NewFolderViewRequested?.Invoke();
         AddGamesItem.Click += (_, _) => AddGamesRequested?.Invoke();
+        LayoutFlowItem.Click += (_, _) => LayoutRequested?.Invoke(FenceLayout.Flow);
+        LayoutFreeItem.Click += (_, _) => LayoutRequested?.Invoke(FenceLayout.Free);
         OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
         ViewSettingsItem.Click += (_, _) => ViewSettingsRequested?.Invoke();
         MoreLine.MouseLeftButtonUp += (_, click) => { click.Handled = true; OpenFolderRequested?.Invoke(); };
@@ -295,6 +299,11 @@ public partial class FenceWindow : Window
         _title = fence.Title;
         TitleText.Text = fence.Title;
         _kind = fence.Kind;
+        _layout = fence.Layout; // M24
+        if (_gridPanel is { } panel) panel.Layout = _layout;
+        LayoutItem.Visibility = fence.Kind == FenceKind.Items ? Visibility.Visible : Visibility.Collapsed;
+        LayoutFlowItem.IsChecked = _layout == FenceLayout.Flow;
+        LayoutFreeItem.IsChecked = _layout == FenceLayout.Free;
         // The library is NeoFences' own A–Z list of games: no items to add or sort (M12). A folder view shows its folder
         // read-only: nothing to add; "Sort by" is its own, kept (M21).
         var items = _kind == FenceKind.Items;
@@ -360,7 +369,7 @@ public partial class FenceWindow : Window
         var kind = art.IsPoster ? "Poster" : "Logo";
         // Decoded off the UI thread (a cover is a few hundred KB); only the newest request is shown.
         // Decoded at the tile's pixel width, not more: 300 covers would otherwise hold ~160 MB (M13b).
-        var decodeWidth = Math.Max(48, (int)Math.Round(Math.Round(_iconSizeDips * 1.5) * VisualTreeHelper.GetDpi(this).DpiScaleX));
+        var decodeWidth = Math.Max(48, (int)Math.Round(view.TileWidth * VisualTreeHelper.GetDpi(this).DpiScaleX)); // M24: the tile's own width
         Task.Run(() => LoadArt(art.Path, decodeWidth)).ContinueWith(loaded =>
         {
             if (view.ArtPath != art.Path || loaded.Result is not { } image) return;
@@ -643,7 +652,6 @@ public partial class FenceWindow : Window
             if (!wanted.Contains(_items[index].Key)) _items.RemoveAt(index);
         }
         // ponytail: O(n²) moves in the worst case (a full reorder of hundreds of items); a keyed diff when that shows up.
-        var iconSizePx = IconSizePx;
         for (var index = 0; index < shownItems.Count; index++)
         {
             var shown = shownItems[index];
@@ -655,16 +663,21 @@ public partial class FenceWindow : Window
             if (found >= 0)
             {
                 if (found != index) _items.Move(found, index);
-                if (_items[index].Update(shown) && IsLoaded) _iconLoader.Request(_items[index], iconSizePx);
+                var resized = _items[index].Span != (shown.Span ?? (shown.Tile ? new GridSpan(1, 2) : GridSpan.One));
+                var reload = _items[index].Update(shown);
+                if (resized) SizeView(_items[index]); // M24
+                if ((reload || resized) && IsLoaded) _iconLoader.Request(_items[index], PxOf(_items[index]));
                 ApplyArt(_items[index]); // shown as a cover now, or an icon again (M22); the same art is not loaded twice
                 continue;
             }
             var view = new FenceItemView(shown);
             ApplyArt(view);
-            if (IsLoaded) _iconLoader.Request(view, iconSizePx); // before that, Loaded requests them at the right DPI (M2b review)
+            SizeView(view); // M24: its content for its span
+            if (IsLoaded) _iconLoader.Request(view, PxOf(view)); // before that, Loaded requests them at the right DPI (M2b review)
             _items.Insert(index, view);
         }
         UpdateEmptyHint();
+        GridPanel?.InvalidateMeasure(); // M24: spans or stored cells may have changed
         // Cells may have shifted under a shown name without a scroll or selection event (final review I3).
         Dispatcher.BeginInvoke(UpdateHoverLabel, System.Windows.Threading.DispatcherPriority.Loaded);
     }
@@ -702,6 +715,84 @@ public partial class FenceWindow : Window
 
     private int IconSizePx => (int)Math.Round(_iconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);
 
+    /// <summary>An item's icon in physical pixels: its own size for its span (M24).</summary>
+    private int PxOf(FenceItemView view) => (int)Math.Round(view.IconDips * VisualTreeHelper.GetDpi(this).DpiScaleX);
+
+    private FenceGridPanel? _gridPanel;
+    private FenceLayout _layout;
+
+    /// <summary>The list's grid panel (M24), once the list has its template.</summary>
+    private FenceGridPanel? GridPanel
+    {
+        get
+        {
+            if (_gridPanel is null && FindDescendant<FenceGridPanel>(ItemList) is { } found)
+            {
+                _gridPanel = found;
+                ApplyCellSizes();
+                found.Layout = _layout;
+            }
+            return _gridPanel;
+        }
+    }
+
+    private static TDescendant? FindDescendant<TDescendant>(DependencyObject parent) where TDescendant : DependencyObject
+    {
+        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
+        {
+            var child = VisualTreeHelper.GetChild(parent, index);
+            if (child is TDescendant match) return match;
+            if (FindDescendant<TDescendant>(child) is { } deeper) return deeper;
+        }
+        return null;
+    }
+
+    /// <summary>The columns the grid shows now (M24; one until it is laid out).</summary>
+    public int Columns => GridPanel?.Columns ?? 1;
+
+    /// <summary>Where each element sits now, by key (M24): stored when a fence becomes Free, used for drops.</summary>
+    public IReadOnlyDictionary<string, GridCell> CurrentCells()
+    {
+        var cells = new Dictionary<string, GridCell>(StringComparer.Ordinal);
+        if (GridPanel?.Arrangement is not { } arrangement) return cells;
+        for (var index = 0; index < _items.Count && index < arrangement.Cells.Count; index++) cells[_items[index].Key] = arrangement.Cells[index];
+        return cells;
+    }
+
+    /// <summary>Each element's cell and span as laid out now (M24).</summary>
+    public IReadOnlyList<(string Key, GridCell Cell, GridSpan Span)> CurrentLayout()
+    {
+        if (GridPanel?.Arrangement is not { } arrangement) return [];
+        return [.. _items.Take(arrangement.Cells.Count).Select((view, index) => (view.Key, arrangement.Cells[index], arrangement.Spans[index]))];
+    }
+
+    /// <summary>The cell under the pointer during the last drop hover (M24, Free fences).</summary>
+    public GridCell? LastDropCell { get; private set; }
+
+    public void ForgetDropCell() => LastDropCell = null;
+
+    /// <summary>
+    /// Cells follow the icon size and the label mode (M24): a cell is today's item cell; each element sizes its content to
+    /// its span; an icon that changed size is loaded again.
+    /// </summary>
+    private void ApplyCellSizes()
+    {
+        var labelHeight = IsLibrary || _labelMode == LabelMode.Always ? 36.0 : 0.0;
+        var cellWidth = (double)Resources["ItemWidth"] + 8;
+        var cellHeight = _iconSizeDips + 12 + labelHeight;
+        if (_gridPanel is { } panel) (panel.CellWidth, panel.CellHeight) = (cellWidth, cellHeight);
+        foreach (var view in _items)
+        {
+            if (view.ApplySize(cellWidth, cellHeight, _iconSizeDips, labelHeight) && IsLoaded) _iconLoader.Request(view, PxOf(view));
+        }
+    }
+
+    private void SizeView(FenceItemView view)
+    {
+        var labelHeight = IsLibrary || _labelMode == LabelMode.Always ? 36.0 : 0.0;
+        view.ApplySize((double)Resources["ItemWidth"] + 8, _iconSizeDips + 12 + labelHeight, _iconSizeDips, labelHeight);
+    }
+
     /// <summary>One of <see cref="ConfigNormalizer.IconSizes"/> (DIPs). Icons are reloaded at the new size.</summary>
     public void SetIconSize(int iconSizeDips)
     {
@@ -749,6 +840,7 @@ public partial class FenceWindow : Window
         // With labels: room for two short words under small icons. Icons only: a tight grid.
         Resources["ItemWidth"] = IsLibrary ? Math.Round(_iconSizeDips * 1.5) + 12.0
             : _labelMode == LabelMode.Always ? LabelledItemWidth : _iconSizeDips + 12.0;
+        ApplyCellSizes(); // M24
     }
 
     private double LabelledItemWidth => Math.Max(76.0, _iconSizeDips + 28.0);
@@ -1064,15 +1156,13 @@ public partial class FenceWindow : Window
     /// <summary>The Recycle Bin turned full or empty, or another special icon changed (M8c).</summary>
     public void ReloadSpecialIcons()
     {
-        var iconSizePx = IconSizePx;
-        foreach (var view in _items.Where(view => view.Target.StartsWith("::", StringComparison.Ordinal))) _iconLoader.Request(view, iconSizePx);
+        foreach (var view in _items.Where(view => view.Target.StartsWith("::", StringComparison.Ordinal))) _iconLoader.Request(view, PxOf(view));
     }
 
     /// <summary>New size or DPI, or "Refresh": every icon and name is requested again in place; selection stays (M2c review carry-over).</summary>
     public void ReloadIcons()
     {
-        var iconSizePx = IconSizePx;
-        foreach (var view in _items) _iconLoader.Request(view, iconSizePx);
+        foreach (var view in _items) _iconLoader.Request(view, PxOf(view));
     }
 
     /// <summary>Starts renaming the fence title (menu, or a freshly drawn fence).</summary>
@@ -1160,6 +1250,7 @@ public partial class FenceWindow : Window
         // Over a tab header: show that tab now, so the drop lands in it (M9).
         if (TabHeaderAt(screenX, screenY) is { } hoveredTab && hoveredTab != FenceId) TabSelected?.Invoke(hoveredTab);
         var point = ItemList.PointFromScreen(new Point(screenX, screenY));
+        LastDropCell = GridPanel is { } panel ? panel.CellAt(ItemList.TranslatePoint(point, panel)) : null; // M24: Free fences drop on a cell
         var cells = new List<(double Left, double Top, double Width, double Height)>();
         var cellIndexes = new List<int>();
         for (var index = 0; index < _items.Count; index++)
@@ -1178,7 +1269,18 @@ public partial class FenceWindow : Window
     public void ShowDropFeedback(int? insertAt)
     {
         InsertCaret.Visibility = Visibility.Collapsed;
+        DropCellMarker.Visibility = Visibility.Collapsed;
         if (insertAt is not { } position) return;
+        if (_layout == FenceLayout.Free && LastDropCell is { } cell && GridPanel is { } panel)
+        {
+            // M24: a Free fence shows the cell the drop lands on.
+            var rect = panel.TransformToAncestor(ItemList).TransformBounds(panel.CellRect(cell, GridSpan.One));
+            Canvas.SetLeft(DropCellMarker, rect.Left + 2);
+            Canvas.SetTop(DropCellMarker, rect.Top + 2);
+            (DropCellMarker.Width, DropCellMarker.Height) = (Math.Max(0, rect.Width - 4), Math.Max(0, rect.Height - 4));
+            DropCellMarker.Visibility = Visibility.Visible;
+            return;
+        }
         // Caret at the left edge of the item it goes before, or after the last item.
         var before = position < _items.Count ? ItemList.ItemContainerGenerator.ContainerFromIndex(position) as ListBoxItem : null;
         var anchor = before ?? (_items.Count > 0 ? ItemList.ItemContainerGenerator.ContainerFromIndex(_items.Count - 1) as ListBoxItem : null);
diff --git a/src/NeoFences.App/SizePicker.cs b/src/NeoFences.App/SizePicker.cs
new file mode 100644
index 0000000..b2a7628
--- /dev/null
+++ b/src/NeoFences.App/SizePicker.cs
@@ -0,0 +1,71 @@
+using System.Windows;
+using System.Windows.Controls;
+using System.Windows.Media;
+using NeoFences.Core.Items;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Item menu → Size ▸ (M24, spec §2): a 4×4 grid of squares; hovering highlights columns × rows with a caption, a click
+/// sets it; "Default size" under it. Closes its menu when a size is picked.
+/// </summary>
+public static class SizePicker
+{
+    private const double Square = 16;
+
+    public static MenuItem Create(ContextMenu menu, GridSpan? current, Action<GridSpan?> pick)
+    {
+        var size = new MenuItem { Header = "Size" };
+        var caption = new TextBlock { Margin = new Thickness(2, 6, 0, 0), FontSize = 12, Text = current is { } now ? $"{now.Columns} × {now.Rows}" : "Default size" };
+        var grid = new UniformGrid4();
+        var squares = new Border[GridSpan.Max, GridSpan.Max];
+        void Highlight(int columns, int rows)
+        {
+            for (var row = 0; row < GridSpan.Max; row++)
+            {
+                for (var column = 0; column < GridSpan.Max; column++)
+                {
+                    squares[row, column].Background = column < columns && row < rows ? SystemColors.HighlightBrush : Brushes.Transparent;
+                }
+            }
+        }
+        for (var row = 0; row < GridSpan.Max; row++)
+        {
+            for (var column = 0; column < GridSpan.Max; column++)
+            {
+                var (columns, rows) = (column + 1, row + 1);
+                var square = new Border
+                {
+                    Width = Square, Height = Square, Margin = new Thickness(1), CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1),
+                    BorderBrush = SystemColors.GrayTextBrush, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand,
+                };
+                System.Windows.Automation.AutomationProperties.SetName(square, $"{columns} × {rows}");
+                square.MouseEnter += (_, _) => { Highlight(columns, rows); caption.Text = $"{columns} × {rows}"; };
+                square.MouseLeftButtonUp += (_, click) =>
+                {
+                    click.Handled = true;
+                    menu.IsOpen = false;
+                    pick(new GridSpan(columns, rows));
+                };
+                squares[row, column] = square;
+                grid.Children.Add(square);
+            }
+        }
+        if (current is { } chosen) Highlight(chosen.Columns, chosen.Rows);
+        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
+        panel.Children.Add(grid);
+        panel.Children.Add(caption);
+        size.Items.Add(new MenuItem { Header = panel, StaysOpenOnClick = true, Focusable = false });
+        var reset = new MenuItem { Header = "Default size", IsChecked = current is null };
+        reset.Click += (_, _) => pick(null);
+        size.Items.Add(new Separator());
+        size.Items.Add(reset);
+        return size;
+    }
+
+    /// <summary>Four squares a row.</summary>
+    private sealed class UniformGrid4 : System.Windows.Controls.Primitives.UniformGrid
+    {
+        public UniformGrid4() => Columns = GridSpan.Max;
+    }
+}
````

- [ ] **Step 2: Build and test.** `dotnet build` → `0 Warning(s)`; `dotnet test` → 540 passed.
  (UI behaviour is verified by TEST-CHECKLIST AJ in Task 5.)

- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: added element sizes and flow or free fence layouts with a size picker"`

### Task 3: Docs

**Files:**
- Modify: `docs/DECISIONS.md` (ADR-046), `docs/ARCHITECTURE.md` (layers, components, data shapes, 0.13.0 status),
  `docs/FEATURES.md`, `docs/TEST-CHECKLIST.md` (section AJ)
- Create: `docs/research/m24-element-sizes.md`

- [ ] **Step 1: Apply.** Write this patch to `m24-3-docs.patch` and apply it:

````diff
diff --git a/docs/ARCHITECTURE.md b/docs/ARCHITECTURE.md
index 6ef89e5..4dc64f1 100644
--- a/docs/ARCHITECTURE.md
+++ b/docs/ARCHITECTURE.md
@@ -50,10 +50,11 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 ```
 ┌─────────────────────────── NeoFences.App (WPF) ───────────────────────────┐
 │ FenceHost (+ .Items .Watching .Library .Appearance .Updates .DesktopFill  │
-│            .FolderViews .GameItems)                                       │
+│            .FolderViews .GameItems .Grid)                                 │
 │ FenceWindow · FenceItemView · IconLoader · ItemPropertiesWindow           │
 │ AppPickerWindow · RelocateWindow · DesktopFillWindow · MissingItemWindow  │
 │ FolderViewWindow · AddGamesWindow · SettingsWindow · FolderLister         │
+│ FenceGridPanel · SizePicker                                               │
 │ DrawFenceOverlay                                                          │
 └──────────────┬────────────────────────────────────────────────────────────┘
 ┌──────────────▼─────────── NeoFences.Shell (Win32/COM, CsWin32) ───────────┐
@@ -80,6 +81,8 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 | Core/FenceEdits, FenceTabs, Snapping | rename / icon size / lock / colours / new / delete fence; tabs (ADR-029); snap to 8 px gap or aligned edges during drags (ADR-015) | — |
 | Core/FencePlacement, Core/Lifecycle | px↔DIP placement, containing monitor; `RunState` (hide icons, quick-hide, Pause, game mode), session-end policy, restart throttle, watcher backoff | — |
 | Core/Library | Valve/Epic parsing, `GameCatalog` merge, `LibraryFiles` plan, `GameLaunchers.LauncherOf` (ADR-032); `GameItems` (M22): `Migrate` (a library fence → game items), `NewGames`, `AddNew`, `Retarget` (by game id), `ShowsCover` | — |
+| Core/Items (M24) | `GridSpan` (1–4 × 1–4), `GridCell`, `FenceLayout` (Flow / Free), `FenceGrid.Arrange` (dense packing; Free: stored cells, collisions and out-of-width elements to free spots), `CellAt`, `NearestFree`, `PlaceDropped`, `SpanOf` (covers 1×2); `ItemEdits.SetSize`, `Place`; repairs | — |
+| App/FenceGridPanel, SizePicker, FenceHost.Grid (M24) | the fence list's items panel: whole cells from the icon size and label mode, columns from the width, elements placed where `FenceGrid` says (a failed pass falls back to 1×1 in order); Size ▸ 4×4 picker; Layout ▸ (Flow → Free stores the shown cells); Free drops on the cell under the pointer (offsets kept, taken → nearest free); Sort in Free packs and stores; new elements of Free fences store their spot | WPF Panel |
 | App/FenceHost.GameItems, AddGamesWindow (M22) | games as items (ADR-045): migration at start / after a restore / after a scan (a snapshot first), new games into `Library.NewGamesFence`, Add games…, the game item menu (Show as cover / icon, Open install folder), "not installed" | WPF (Fluent) |
 | App/FenceHost | orchestrates config + items, monitors, windows, debounced saves (both files, config first), display changes, Explorer restarts, session end, snapshots, tray | — |
 | App/FenceHost.Items | open (arguments, run as administrator; a missing target asks Locate… / Remove), NeoFences' item menu, Windows' menu on Shift+right-click, Properties, Add item…, Locate…, drops, drag-out, one-time sort, item pictures copied into `icons\` | WPF ContextMenu, Clipboard |
@@ -122,9 +125,9 @@ happen) is behind Shift+right-click, under a line saying it acts on the real fil
 
 `%LOCALAPPDATA%\NeoFences\`
 - `config.json` (+ `.bak`, `.tmp` transient) — schema 5
-  - shape: `{ schemaVersion, settings: { hideDesktopIcons, peekHotkey, … }, fences: [ { id, title, isLibrary, view: { path, show, sort, newest, patterns } (M21), iconSize, rolledUp, locked, labels, tabs, activeTab, tabColor, customColor } ], layouts: { <fingerprint>: { monitors, fences: { <fenceId>: { monitor, x, y, w, h } } } }, library, lastLayoutFingerprint }`
+  - shape: `{ schemaVersion, settings: { hideDesktopIcons, peekHotkey, … }, fences: [ { id, title, isLibrary, view: { path, show, sort, newest, patterns } (M21), layout (M24), iconSize, rolledUp, locked, labels, tabs, activeTab, tabColor, customColor } ], layouts: { <fingerprint>: { monitors, fences: { <fenceId>: { monitor, x, y, w, h } } } }, library, lastLayoutFingerprint }`
 - `items.json` (+ `.bak`) — schema 1 (ADR-041)
-  - shape: `{ schema, fences: { <fenceId>: [ { id, target, name, icon: { file, index } | { image }, arguments, runAsAdmin, note, gameId, showAs (M22) } ] } }`
+  - shape: `{ schema, fences: { <fenceId>: [ { id, target, name, icon: { file, index } | { image }, arguments, runAsAdmin, note, gameId, showAs (M22), size: { columns, rows }, cell: { column, row } (M24) } ] } }`
   - a list is removed only with its fence (Delete fence); lists of fences the config does not have stay (a fallback config never costs items; ADR-041 amended)
 - `icons\<itemId>-<guid>.png` — pictures chosen as item icons (≤ 256 px); unused ones deleted at start
 - `backups\config-<yyyyMMdd>.json`, `backups\items-<yyyyMMdd>.json` (keep 10 each), `backups\pre-schema-5-config.json`
@@ -162,6 +165,11 @@ games… is open; after each scan game items follow their game's shortcut and ne
 Game Library fence becomes an items fence once (a "Before games became items" snapshot first). The library fence kind
 stays in the code, not in the menus (ADR-045, `research/m22-games-as-items.md`).
 
+**0.13.0 (M24, element sizes and the fence grid)**: every element spans 1–4 columns × 1–4 rows of its fence's cells
+(Size ▸, a 4×4 picker); a bigger element shows a bigger icon (≤ 256 px) or cover; game covers default to 1×2. Each fence is
+Flow (packed in order, smaller elements fill gaps) or Free (fixed positions; switching keeps everything where it is; a
+drop lands on the cell under the pointer or the nearest free spot) (ADR-046, `research/m24-element-sizes.md`).
+
 **0.12.1 (M23)**: the M21/M22 review minors — a folder view sorts its listing off the UI thread; a hidden view tab's title
 follows its folder's rename; Folder view settings takes full paths only (variables expanded) and says why OK is greyed
 out; deleting a fence refreshes Settings and stops an unneeded game scan, and new games never go to a gone fence; a game
diff --git a/docs/DECISIONS.md b/docs/DECISIONS.md
index 47b73c7..41ea795 100644
--- a/docs/DECISIONS.md
+++ b/docs/DECISIONS.md
@@ -1235,4 +1235,22 @@ of several sizes. The first step (M22, 0.12.0) makes games ordinary items.
 
 **Consequences.** Only games no fence holds go to the new-games fence, and never on a first scan (no earlier scan to
 compare with); a migration cut short runs again without doubling games, and none runs while a store is read-only. An
-uninstalled game's items show "not installed" (dimmed, ⚠, covers too) and come back on reinstall. Hiding games stays in Settings. Mixed fences size each cell by its item (tile or icon).
\ No newline at end of file
+uninstalled game's items show "not installed" (dimmed, ⚠, covers too) and come back on reinstall. Hiding games stays in Settings. Mixed fences size each cell by its item (tile or icon).
+
+## ADR-046 — Element sizes and the fence grid
+**Date:** 2026-10-05 · **Status:** Accepted · **Continues:** ADR-045 (one kind of fence)
+
+**Context.** Widgets and a folder panel (the user's vision) need room bigger than one icon; the user also wants sizes for
+any element and a choice of how elements sit, per fence.
+
+**Decision.**
+- Every element spans 1–4 columns × 1–4 rows of its fence's cells (`VirtualItem.Size`; default 1×1, 1×2 for a game
+  cover). A cell is the fence's icon cell (icon size + label mode).
+- Each fence is **Flow** (packed in order; smaller elements fill gaps) or **Free** (`VirtualItem.Cell`; elements without a
+  usable cell go to the first free spot without changing what is stored). Switching to Free stores the shown cells.
+- The layout is a pure Core function (`FenceGrid`) with a thin WPF panel (`FenceGridPanel`) replacing the WrapPanel; a
+  failed pass falls back to 1×1 in order.
+- Free drops land on the cell under the pointer (offsets kept; a taken spot → the nearest free one); Sort in Free packs.
+
+**Consequences.** Folder-view entries and the old library fence stay 1×1; no resize handles (menu picker only); spans
+above 4 later if wanted. config.json writes each fence's `layout`; items.json writes `size` / `cell` only when set.
\ No newline at end of file
diff --git a/docs/FEATURES.md b/docs/FEATURES.md
index e784253..6d91feb 100644
--- a/docs/FEATURES.md
+++ b/docs/FEATURES.md
@@ -65,7 +65,8 @@ Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fence
 | Blur tint preference (lighter/darker) | v1.7 (M14) | done | background strength slider, one value per Windows tone (ADR-036) |
 | Search palette across all fences | — | parked | built on branch `m15-search-palette` (local history bundle only), not merged |
 | Game Library (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v1.5 | done | launchers, Xbox, game folders, Desktop game shortcuts (ADR-032); since 0.12 games are items in any fence (cover tile or icon, Add games…, new games go to a chosen fence; ADR-045) |
-| One kind of fence: any item in any fence, its look from its kind and settings | 0.12 (M22) | done (games) | ADR-045; next: a folder panel element, widgets (clock, calendar), element sizes |
+| One kind of fence: any item in any fence, its look from its kind and settings | 0.12 (M22) | done (games, sizes) | ADR-045, ADR-046; next: a folder panel element, widgets (clock, calendar) |
+| Element sizes 1–4 × 1–4 and a fence grid (Flow packed / Free fixed positions) | 0.13 (M24) | done | ADR-046; Size ▸ picker, Layout ▸ per fence |
 | Folder views: types, files/folders only, newest N, live sort, "+ N more" | 0.11 (M21) | done | ADR-044; Downloads/Screenshots start newest first |
 | Auto-collect rules (the other half of dynamic collections) | later | — | replaces Rules (ADR-040) |
 | Theme packs: "Nanosuit" (Crysis HUD), "Animus" (Assassin's Creed) | v2 | — | |
diff --git a/docs/TEST-CHECKLIST.md b/docs/TEST-CHECKLIST.md
index a40a5ad..1681b15 100644
--- a/docs/TEST-CHECKLIST.md
+++ b/docs/TEST-CHECKLIST.md
@@ -606,3 +606,20 @@ Added after the M19 final review:
 | AI4 | Settings open; delete a fence | "New games go to" no longer lists it |
 | AI5 | Settings → "New games go to" dropdown open while a scan finishes | the dropdown stays open |
 | AI6 | Right-click a game that is not installed | "Open install folder" greyed out |
+
+## AJ — 0.13.0 element sizes and the fence grid (M24)
+
+| ID | Steps | Expected |
+|---|---|---|
+| AJ1 | Right-click Steam in Apps → Size ▸, hover the squares, click 2 × 2 | the caption follows the hover; Steam becomes a big icon on 2 × 2 cells; smaller apps fill the gaps beside it |
+| AJ2 | Select three apps → Size ▸ 2 × 1; then Default size | all three wide; then back to 1 × 1 |
+| AJ3 | Games fence | covers on 1 × 2 cells; a cover set to 2 × 2 is a bigger poster |
+| AJ4 | Apps → Layout ▸ Free (fixed positions) | nothing moves; Layout ▸ shows Free checked |
+| AJ5 | Free: drag an icon onto an empty cell; onto a taken one; drag three at once | lands on the cell; the nearest free spot; the three keep their arrangement |
+| AJ6 | Free: drop a file from Explorer onto an empty cell | the new item on that cell; the dashed cell marker shows during the drag |
+| AJ7 | Free: Sort by → Name | packed from the top-left in name order |
+| AJ8 | Free: make the fence narrower than an element's cell, then wider again | it shows in a free spot meanwhile; back at its cell afterwards |
+| AJ9 | Arrow keys in Apps | selection moves to the nearest element in that direction |
+| AJ10 | Labels on hover; icon size 32 and 96 | cells shrink and grow; big icons reload sharp |
+| AJ11 | A 500-entry folder view | lays out without a visible pause |
+| AJ12 | Take snapshot; change sizes and layout; restore | sizes, layout and cells come back |
diff --git a/docs/research/m24-element-sizes.md b/docs/research/m24-element-sizes.md
new file mode 100644
index 0000000..7385a91
--- /dev/null
+++ b/docs/research/m24-element-sizes.md
@@ -0,0 +1,25 @@
+# M24 — Element sizes and the fence grid (0.13.0): build notes and results
+
+Spec: `docs/superpowers/specs/2026-10-05-element-sizes-design.md` · Decision: ADR-046 · Plan:
+`docs/superpowers/plans/2026-10-05-m24-element-sizes.md`
+
+## Prototype (2026-10-05, worktree `neo_fences-m24proto`, branch `m24-proto`)
+
+Built end to end before the plan: Core test-first (14 new tests, 540 in all), App; 0 warnings. Probed live on a copy of
+the user's data (restored afterwards): Size ▸ 2 × 2 on Steam made a big icon with the smaller apps filling the gaps; the
+Games covers sit on 1 × 2 cells; Apps → Free kept all 19 elements where they were; a drag onto a taken cell went to the
+nearest free spot ((4,2) → (4,4); (0,0) → (3,0)) and left the old cell empty.
+
+### Where the build departs from the spec (the final review weighs them)
+
+- `Fence.Layout` is a plain enum, so config.json writes `layout` for every fence (`flow` by default); items.json writes
+  `size` / `cell` only when set.
+- The panel arranges at every measure (no cache: one pass over a map of cells is ~µs for 500 elements; a `ponytail:` note).
+- Arrow keys use WPF's own directional navigation, which follows the panel's positions (no extra code).
+- The Free drop marker shows a 1 × 1 cell (not the dragged element's span).
+- New elements of Free fences store the spot they show at (`PinFreeCells`), so a later addition never moves them.
+- The Size ▸ squares are Borders with automation names; screen readers see the caption and Default size, not each square.
+
+## Live check
+
+(TEST-CHECKLIST AJ — filled in after the run.)
\ No newline at end of file
````

- [ ] **Step 2: Check.** The secret scan of `git diff main` (the personal email, the private hub link) finds nothing.

- [ ] **Step 3: Commit.** `git add -A && git commit -m "docs: described element sizes in ADR-046, architecture, features, checklist AJ and the M24 research note"`

### Task 4: Final review and fix pass

- [ ] Whole-branch review on the most capable model of `main..m24-element-sizes` with the spec, this plan, its Review
  Focus and departures. Critical / Important: one fix pass, test-first where Core can show it; App-only fixes get an AJ
  row. Minors listed in `docs/research/m24-element-sizes.md` as deferred.

### Task 5: Live check (standing go while the PC is unattended)

- [ ] Back up `%LOCALAPPDATA%\NeoFences` and the Run value; run the test build; TEST-CHECKLIST AJ1–AJ12 by script
  (banners; never type into Windows Terminal); screenshots sent to the user; restore; refocus Terminal.
- [ ] Results into `docs/research/m24-element-sizes.md`; commit `docs: added the M24 live check results`.
