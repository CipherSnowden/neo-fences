# M35 — Modern menus and dialogs (0.22.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-06-modern-menus-and-dialogs-design.md` · Plan: `docs/superpowers/plans/2026-10-06-m35-modern-menus-and-dialogs.md`

## Build

- Mockups with the owner (visual companion): menus A (grouped, with icons), Settings A (a section list on the left), colour A
  (swatches in the menu, a small picker for anything else).
- Approach: NeoFences draws its own menus (one shared style merged into the application) rather than WPF's Fluent theme
  app-wide (it would restyle the fences' own lists and panels) or native dark menus (undocumented, no icons).
- Prototype probe on a copy of the owner's data (owner's OK): the fence menu with Add ▸ / View ▸ / Colour ▸, the colour
  picker, an item menu, a game menu with Size ▸, the tray menu with Restore snapshot ▸, Settings General / Games /
  Appearance. Fixed after the probe: "Delete fence" carried its old long text (now a tooltip); Settings' selected section was
  a heavy accent block (now a subtle highlight with an accent pill); two texts still said "Add games…".
- Calls made while prototyping (rulings): Delete fence's reassurance as a tooltip; Remove last also for missing items;
  Open folder first in a panel's menu; "Use my accent colour" stores the accent of that moment; None plus the eight fence
  colours; the tray menu placed with the first fence's DPI; the delete-without-snapshot question says why; Settings 820 ×
  680; Game mode's lightning icon; an unused `using` in `PathPicker` went with `ChooseColor`.
- Replay-verified on `main` (8 build-error lines before the Core code, 773 tests after, 0 warnings).

## Review (Opus, whole branch)

0 critical, 1 important plus 1 raised, both fixed: a failed Steam pick in Choose cover… opened a modal dialog with no owner
and no taskbar button, which could hide behind another app and block every fence (now a tray notice); snapshot names with
"&" showed "&&" and names with "_" lost it in the WPF menus (the Win32 doubling went; menu entries escape their own access
keys; test `MenuLabel_IsOneSafeLine`). 774 tests. Deferred minors: the tray menu's place at 125/150 % when no fence exists;
the swatch row's keyboard ring starts at None and can linger; no Exit when the tray icon is unavailable (Task Manager only);
no access-key letters in menus; two spec test items (current swatch, accent) not in Core; the hex box applies "#e23" as
shorthand while typing and OK with invalid hex keeps the last colour; an unused style and name in Settings; the GUIDE's
item-menu and Settings screenshots show the old look.
