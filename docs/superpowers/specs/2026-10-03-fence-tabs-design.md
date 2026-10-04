# NeoFences — Fence tabs (M9) design

**Date:** 2026-10-03 · **Status:** draft for review · **Target:** v1.2.0 · **Parity:** Fences 6 tabs

## 1. Goal and user choices

Combine fences into one box with tabs, to save desktop space (user, 2026-10-03):

| Question | User choice |
|---|---|
| Purpose | Combine existing fences into one box (Fences 6): drag one fence's title onto another. |
| Splitting out | Drag a tab header off the box onto the desktop; also "Detach tab" in the tab's menu. |
| Colours | A per-tab accent from ~8 colours (or none), shown as a bar under the tab header; the box keeps its blur look. |
| Approach | Tab groups over existing fences (each tab stays a full fence). |

Success: several fences live in one box; switching, renaming, colouring, merging, detaching and dropping items onto a
tab all work by mouse; the arrangement (tab order, active tab, colours) survives restarts, crashes and power cuts; old
configs load unchanged; nothing about items, Portals or hard rules changes.

Non-goals: tabs inside tabs; a tab that is only a sub-group of another fence's items; per-tab position or size; scroll
arrows for overflowing strips; keyboard reordering of tabs.

## 2. Data and config

Every tab is an ordinary `Fence` (items, source, sort, icon size, labels unchanged). New fields:

| Field | On | Meaning |
|---|---|---|
| `Tabs: [fence ids]` | host | Every tab of the box in order, the host itself included. Empty (or a single id) = an ordinary fence. |
| `ActiveTab: fence id?` | host | The tab shown; null or unknown = the first. |
| `TabColor: enum?` | any fence | Red, Orange, Yellow, Green, Teal, Blue, Purple, Pink, or none. |

- **Host** = a fence whose `Tabs` has two or more ids. It owns the box: the window, the placement in every per-monitor
  layout (keyed by its id), roll-up and lock. **Members** (the other ids in a host's `Tabs`) have no window and no
  placement of their own; their `RolledUp` / `Locked` are ignored while they are members.
- **Core edits** (`NeoFences.Core`, test-first), each returning a new config:
  - `FenceTabs.Merge(config, movingFenceId, targetFenceId, insertAt?)` — the moving fence (and, if it is itself a
    host, all its tabs, in order) joins the target's box (the target becomes the host if it was standalone); the moving
    fence's placements are dropped; the box's `ActiveTab` becomes the moving fence.
  - `FenceTabs.Detach(config, fenceId, placementRect)` — the tab leaves its box and gets that placement in the current
    layout. Detaching the host makes the next tab the host: it takes over `Tabs`, `ActiveTab`, `RolledUp`, `Locked` and
    the box's placements. A box left with one tab becomes an ordinary fence (`Tabs` cleared).
  - `FenceTabs.Reorder(config, hostId, fenceId, newIndex)`, `SetActive(config, hostId, fenceId)`,
    `SetColor(config, fenceId, color?)`.
  - `FenceTabs.HostOf(config, fenceId)` — the box a fence shows in (itself when standalone).
- **Normalizer repairs** on load: unknown ids dropped from `Tabs`; a fence listed by two hosts stays with the first; no
  nesting (a host listed in another host's `Tabs` becomes a member and its own `Tabs` are merged in order, de-duplicated);
  `Tabs` not containing the host gets the host added first (the host may sit anywhere in the order otherwise); a box left with one id is cleared;
  invalid `ActiveTab` → the first tab; invalid `TabColor` → none; a member's own placements are ignored (kept harmlessly
  until a detach replaces them).
- **Deleting** a fence that is a tab removes it from its box (its items go to the Inbox, as today); deleting the host
  makes the next tab the host (as Detach). The Inbox can be a tab.
- **Old configs** have no `tabs`, `activeTab` or `tabColor`: every fence is standalone; nothing moves.

## 3. Window and tab strip

```
╭───────────────────────────────────────────╮
│ Games ▔▔▔▔ │ Tools  │ Docs     │          │  ← tab strip in the 30 px title row
│ ███ red    │ ▁ blue │          │          │    colour bar under each header; active = bold, brighter
├───────────────────────────────────────────┤
│  [icon] [icon] [icon] …   (active tab's items, with its icon size / labels / sort)
╰───────────────────────────────────────────╯
```

- One `FenceWindow` per box. With one tab it looks as today (title text), plus a colour bar if the fence has a colour.
  With two or more, the title row shows headers; when they do not fit they shrink with an ellipsis (minimum ~48 DIP).
- Switching a tab swaps the window's fence: items (`SetItems` in place), icon size, labels, sort menu, Portal state,
  rename target. Icons reload as on an icon-size change. Portal tabs keep their `PortalState` (watcher, removal notice)
  while hidden; desktop tabs keep membership as always.
- **Mouse:** click a header = switch; double-click a header = rename that tab (in place); double-click empty strip =
  roll the box up/down (existing caption logic); drag empty strip = move the box (Windows' move loop, snapping);
  right-click a header = the fence menu acting on that tab (icon size, labels, sort, rename, delete, …) plus
  **Colour ▸** (8 + None) and **Detach tab** (only with two or more tabs).
- **Keyboard:** Ctrl+Tab / Ctrl+Shift+Tab switch tabs while the box has focus.
- Peek, quick-hide, game mode, roll-up hover/click, lock, Safely Remove: per box, unchanged behaviour.
- Accessibility: each header is a tab item for UI Automation (name = title, selected = active); the colour has a name.

## 4. Drag flows

- **Header drag** (NeoFences' own, not Windows' move loop): press on a header, move past the system drag threshold →
  a small translucent "tab ghost" window (click-through, like the draw-fence overlay) follows the cursor. On release:
  - over this box's strip → **reorder** to the slot under the cursor (caret shown while hovering);
  - over another box's strip or a standalone fence's title row → **merge** into it (target strip highlighted);
  - anywhere else on the desktop → **detach**: a new box at the cursor with the old box's size (clamped to the
    monitor's work area by the layout engine).
  - Esc or a right-click cancels; nothing changes until the release.
- **Fence title drag** (a standalone fence, or the strip's empty space of a box): Windows' move loop as today. While it
  moves, a fence whose title row/strip is under the cursor highlights; if the move ends there, the moved box **merges**
  into it (all its tabs, in order, at the end) instead of staying put.
- **Item drags** (from a fence, Explorer, zips): dropping on a header = into that tab, exactly as dropping into that
  tab's item area (desktop membership or Portal copy/move); hovering a header ~0.5 s switches to it.

## 5. Reliability and hard rules

- One Core edit and one save per action, after the drop; a crash or power cut mid-drag leaves the previous state.
- Load repairs any inconsistent tab state (section 2); a missing member fence simply disappears from its strip.
- Hard rules unchanged: no new Win32 (WPF capture and an overlay window); nothing touches files; membership,
  reconcile, Portals, Takeover and the watchdog do not change.
- Explorer restart, display change, DPI change: boxes re-attach and re-place like fences today (one window per box).

## 6. Testing

- **Core (xUnit, test-first):** Merge (standalone→standalone, into a box, a box into a box, insert position);
  Detach (a member, the host — placement and settings hand-over, last-but-one tab → ordinary fence); Reorder;
  SetActive; SetColor; HostOf; Delete of a tab and of a host; normalizer repairs (each case in section 2); JSON round
  trip; an old config without the new fields.
- **Live smoke (with consent; installed copy restored):** merge by title drag; switch tabs; rename by double-click;
  colour; drop items onto a header; detach by dragging a header out; reorder; restart (tabs, order, active tab,
  colours kept); roll-up / Peek / quick-hide / lock on a box; a Portal tab.
- **Checklist section V** for the hand checks.

## 7. Delivery

One milestone, **M9 — Fence tabs**, with the usual flow: prototype in scratch → live smoke → implementation plan →
approval → inline execution → final review → merge → release **v1.2.0**. ADR-029 records the decisions.
