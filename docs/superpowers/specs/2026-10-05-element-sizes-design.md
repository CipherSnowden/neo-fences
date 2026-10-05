# M24 — Element sizes and the fence grid (0.13.0) — design

**Date:** 2026-10-05 · **Status:** approved in brainstorm (sections 1–3), awaiting written-spec review
**Decisions:** ADR-046 (this milestone; continues ADR-045 "one kind of fence")
**Background:** the user's vision (ADR-045): a fence holds elements whose kind and settings decide their look; widgets
(clock, calendar) and a folder panel come later and need room bigger than one icon. This milestone gives every element a
size and every fence a real grid.

## Goal

Any element in a fence can span 1–4 columns × 1–4 rows of the fence's cells. Each fence chooses how its elements sit:
**Flow** (packed in order, today's behaviour) or **Free** (fixed positions). The user's words for the qualities wanted:
flexible, good UI/UX, reliable and robust, safe, modern, performant enough.

Hard rules stand: NeoFences never modifies a user file (ADR-040); Win32/COM only in Shell; no new NuGet dependency;
failures degrade one feature, never crash.

Success: in the user's Apps fence, Steam set to 2×2 shows a big icon and the smaller apps fill the gaps around it; Games
covers are roomy 1×2 tiles; switching Apps to Free keeps everything where it is, and a dragged icon lands on the cell
under the pointer; a 500-entry folder view lays out without a visible pause.

## 1. Model and layout rules

### Data (Core)
- `GridSpan(int Columns, int Rows)`, each 1–4; `GridCell(int Column, int Row)`, each ≥ 0.
- `VirtualItem.Size` (`GridSpan?`): null = the default — 1×1, and 1×2 for a game shown as a cover (a 2:3 poster fits).
- `VirtualItem.Cell` (`GridCell?`): the stored position, used only by Free fences.
- `Fence.Layout` (`FenceLayout { Flow, Free }`, default Flow).
- items.json stays schema 1 and config schema 5 (optional fields, written only when set).

### `FenceGrid` (Core, pure, test-first)
- `Arrange(IReadOnlyList<GridElement> elements, int columns, FenceLayout layout) → GridArrangement` with each element's
  `GridCell` (in input order) and `Rows` (the rows used). `GridElement(GridSpan Span, GridCell? Stored)`.
- Every span is clamped to `columns` wide (a 3×1 element in a 2-column fence shows 2×1). `columns` ≥ 1.
- **Flow:** elements in order; each goes to the first cell, scanning rows top to bottom and columns left to right, where
  its whole span is free. Smaller elements fill gaps left beside bigger ones (dense packing).
- **Free:** first, elements with a stored cell whose span fits inside `columns` and does not overlap an element already
  placed, in input order, sit at their stored cell. Then every other element (no stored cell, out of width, or
  overlapping) goes to the first free spot, in input order. Stored cells are never changed by `Arrange`.
- `CellAt(double x, double y, double cellWidth, double cellHeight) → GridCell` (clamped to ≥ 0).
- `NearestFree(occupied, span, target, columns) → GridCell`: the free spot for `span` closest to `target` (by distance of
  top-left corners, ties by row then column), extending rows downward as needed.
- Occupancy is a bit map of cells: one pass per element; 500 elements arrange in well under a millisecond.

### Repairs (normalizer / items load)
- A span outside 1–4 is clamped; a negative cell coordinate is dropped (the element gets a free spot).
- A typo in `layout` reads as Flow (lenient enum).

### Look of a bigger element
- Cell size = the fence's icon cell as today (icon size + label mode); an element of span c × r is `c × cellWidth` wide and
  `r × cellHeight` tall, the gaps between cells included.
- Icon items: the icon grows to fit (`min(width − padding, height − label)`, capped at 256 px); the label stays under it.
- Game covers: the cover fills the space at 2:3, centred; label under it.
- Folder-view entries and the old library fence: always 1×1 (sizes are per item; entries are not items).

### Existing fences
- Become Flow with every element at its default size: the look stays close to today's (game covers become 1×2 tiles).

## 2. User interface

- **Right-click an element → Size ▸**: a 4×4 grid of small squares; hovering highlights columns × rows with a caption
  ("3 × 2"); a click sets it. "Default size" under the grid. Applies to every selected element. Folder-view entries: no
  Size entry.
- **Fence menu → Layout ▸ Flow (packed) / Free (fixed positions)**, the current one checked (items fences only; folder
  views stay Flow).
  - Flow → Free stores every element's current cell first, so nothing moves.
  - Free → Flow keeps the order of the list (stored cells stay, unused).
- **Dragging inside a fence:**
  - Flow: reorders as today; the insert caret is drawn at the cell under the pointer.
  - Free: the dragged element's top-left lands on the cell under the pointer; a taken cell → the nearest free spot that
    fits (`NearestFree`). Several dragged elements keep their relative offsets (each one that collides goes to its own
    nearest free spot). Ctrl+drag duplicates at the drop cell.
- **Drops from outside** (files, links, apps): Flow at the pointer's insert position; Free at the cell under the pointer
  or the nearest free spot.
- **Sort by** in a Free fence: packed from the top-left in the new order, and those cells are stored.
- **Arrow keys** move the selection to the nearest element in that direction (by cell), Home/End to first/last.
- Labels mode, icon size and DPI changes rearrange the grid; icons and covers reload at their new pixel size.

## 3. Units

- **Core:** `Items/GridLayout.cs` (`GridSpan`, `GridCell`, `GridElement`, `GridArrangement`, `FenceLayout`, `FenceGrid`),
  `VirtualItem` (+`Size`, `Cell`), `Fence` (+`Layout`), `ItemEdits.SetSize(document, ids, span?)`,
  `ItemEdits.Place(document, cellsById)`, config/items repairs, lenient `FenceLayout`. Tests: `FenceGridTests`.
- **App:**
  - `FenceGridPanel : Panel` (the ListBox's items panel): columns from the available width and the cell size; asks
    `FenceGrid.Arrange`; caches the arrangement until items, spans, stored cells or the column count change; exposes
    `CellAt` and the cell bounds for the drop caret.
  - `FenceItemView`: `Span`, `StoredCell`, `ContentPx` (the icon / cover size for its span); `ShownItem` carries them.
  - `FenceWindow`: the panel instead of the WrapPanel; cell sizes per element; the insert caret and drop cell from the
    panel; the Layout ▸ submenu; spatial arrow keys.
  - `SizePicker` (the 4×4 menu control) in the item menu.
  - `FenceHost`: SetSize, SetLayout (pinning cells), Free drops and drags, Sort in Free, icon requests at `ContentPx`.
- **Shell:** nothing new (icons up to 256 px already come from `IShellItemImageFactory`).

## 4. Failures

- A stored size or cell out of range is repaired at load; overlapping or out-of-width elements are shown in free spots,
  never stacked or clipped.
- A layout pass that throws (a bug) is logged and the fence falls back to one element per cell in list order (Flow,
  1×1) — never a crash.
- Big icons that cannot be made fall back to the normal-size icon scaled.

## 5. Testing and release

- Core xUnit test-first: Flow packing and gap filling, clamping to the fence width, Free stored cells, collisions and
  out-of-width elements, `CellAt`, `NearestFree`, `SetSize` / `Place`, repairs, items.json round trip.
- `TEST-CHECKLIST` section **AJ**: Size ▸ picker on one and several elements; a 2×2 Steam in Apps with gaps filled;
  game covers 1×2; Flow → Free keeps positions; Free drag onto a free cell, onto a taken one, several at once; a drop from
  Explorer in Free; Sort in Free; arrow keys; labels on hover and icon-size changes; a 500-entry folder view; a snapshot
  restore keeps sizes and cells.
- Live check by script on backed-up data (restored afterwards), screenshots sent as they are taken.
- Prototype first, then the plan with replay-verified patches; merged locally (the user's rule until 1.0). Docs: ADR-046,
  ARCHITECTURE, FEATURES, ROADMAP, SESSION-LOG, hub. Release **0.13.0** with the usual flow, asked first.

## Out of scope

Widgets, the folder panel element, resize handles (the menu picker only), sizes per folder-view entry, spans above 4.
