# M30 — First-run welcome (0.18.0) — design

**Date:** 2026-10-06 · **Status:** approved in brainstorm, awaiting written-spec review
**Decisions:** ADR-051 (the welcome lives in the first fence)

## Goal

A friend who installs NeoFences sees one empty fence titled "Fence" with the hint "Drop files, folders or links here — or
right-click → Add item…", and nothing else: the tray icon may sit behind Windows 11's ^ arrow, and nothing says what
NeoFences is or how to sort the desktop. This milestone turns that first fence into a welcome.

The user chose: the welcome lives **in the first fence** (no extra window); one Windows notice says where the tray icon
is; **Sort my desktop…** that makes fences removes the still-empty welcome fence; hiding Windows' own icons is offered
right after sorting.

Success: within a minute of installing, a friend has their desktop sorted into fences, knows where the tray icon is and
that Help exists — and nobody who updates an existing copy ever sees the welcome.

Hard rules stand: no file is touched (sorting makes links; removing the welcome fence removes an empty fence only).
No new dependency; no config schema bump.

## 1. Which fence is the welcome

- `Fence.Welcome` (bool, default false). Written to `config.json` only when true (`"welcome": true`), so existing
  configs and every other fence are unchanged.
- `NeoFencesConfig.CreateDefault()` returns the same single fence as today ("Fence"), with `Welcome = true`.
  `CreateDefault` is used when `config.json` is missing, unreadable with no usable backup, or pre-pivot
  (`ConfigLoadSource.Fresh`) — each is a fresh start and gets the welcome.
- An updating copy loads its own config, which never has the flag: no welcome.
- The flag is persisted so a restart or power cut before the friend acts keeps the welcome.

## 2. What the welcome fence shows

While `Welcome` is true and the fence has no items, its content area shows (instead of today's empty hint):

- **Welcome to NeoFences** (heading)
- "Fences hold links to your apps, games, files and websites — your files never move."
- Buttons: **Sort my desktop…** · **Add item…** · **Guide**
- "or drop files, folders or links here" (dropping works as on any fence)

The title bar, fence menu, move, resize, roll-up and the rest behave as on any fence. The content fits the default fence
size (320 × 220); a smaller fence scrolls it.

- **Sort my desktop…** opens the existing Add from desktop dialog (tray / fence menu → Add from desktop…).
- **Add item…** opens the existing Add item dialog for this fence.
- **Guide** opens the online guide (the same as tray → Help).

## 3. How the welcome ends

- **The fence gets its first item** (drop, Add item…, Sort my desktop with this fence as a target, auto-collect): the flag
  is cleared and saved with the item; from then on it is a normal fence (title "Fence" unless renamed).
- **Sort my desktop…** (or Add from desktop… from anywhere) that adds at least one item, makes at least one new fence, and
  leaves the welcome fence empty: the welcome fence is removed, as **Delete fence** removes an empty fence (no file is
  involved). Cancelling the dialog, or a sort that adds nothing, keeps it.
- **Delete fence** on it: removed as any fence.
- Only a fence with the flag is ever removed this way.

The decision is one pure Core function, test-first:
`WelcomeEdits.AfterDesktopFill(config, items, addedCount, newFenceCount)` → the config with the welcome fence removed
(or unchanged), and `WelcomeEdits.ClearIfFilled(config, items)` → the flag cleared on any welcome fence that has items.

## 4. Hiding Windows' own desktop icons

After sorting, the desktop shows both the fences' links and Windows' own icons. The brainstorm chose "ask once after
sorting"; the Add from desktop dialog **already** has that choice: the checkbox **Hide desktop icons while NeoFences runs**
(shown only while icons are visible; applied after the items are added). Ruling: no second question — the welcome
reuses the dialog's checkbox, so the friend sees the choice exactly once, at the moment they sort. (Costs if wrong: one
added question after the dialog.)

## 5. The tray notice

On a start whose config load was `Fresh`, once the tray icon exists, one Windows notice (the tray icon's balloon, as
"Snapshot saved" uses):

- Title: **NeoFences is running**
- Text: "Its icon is in the notification area — on Windows 11 maybe behind the ^ arrow. Click it for the menu and Help."

Not shown in game mode (as the update notice). Not repeated on later starts.

## 6. Docs

- `docs/GUIDE.md`: a short "First start" part at the top (the welcome fence, its three buttons, the tray notice); the
  README quick start names **Sort my desktop…** as the first step (CLAUDE.md: menus/gestures update the guide in the same
  commit). A screenshot of the welcome fence from a demo (fresh data folder) replaces nothing; it is added.
- ADR-051, ARCHITECTURE 0.18.0 paragraph, FEATURES row, TEST-CHECKLIST section AP, `docs/research/m30-welcome.md`.

## 7. Testing

- Core (xUnit, test-first): `Welcome` round-trips and is omitted when false; `CreateDefault` has one welcome fence;
  `AfterDesktopFill` removes the welcome fence only when (added > 0, new fences > 0, welcome fence empty) and never a
  fence without the flag; `ClearIfFilled` clears the flag when the fence has items and leaves other fences alone.
- Live check (asked first; data folder and Run value backed up and restored): start the branch build with an empty data
  folder → the welcome fence and the tray notice; screenshot; Guide opens the guide; Sort my desktop… → fences made,
  welcome fence gone, the dialog's hide-icons choice shown; a restart before acting keeps the welcome; an existing config
  shows no welcome.

## Out of scope

A welcome window or tour; changing Add from desktop's groups; a Settings switch to show the welcome again.
