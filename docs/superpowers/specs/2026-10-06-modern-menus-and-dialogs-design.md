# M35 — Modern menus and dialogs (0.22.0) — design

**Date:** 2026-10-06 · **Status:** approved in brainstorm, awaiting written-spec review
**Decisions:** ADR-056 (one menu style for fences, items and the tray, drawn by NeoFences)
**Source:** `docs/research/v1-readiness.md` (Look and UX): three menu styles, a 29-entry fence menu, old MessageBox
questions, Settings as one long scroll with checkboxes, the 1995 colour picker.
**Mockups:** visual companion screens `menus`, `settings`, `colours` (owner picked A, A, A).

## Goal

Everything the owner clicks looks like one modern Windows 11 app in light and dark: one menu style everywhere with
icons, a short grouped fence menu, Settings in sections with toggle switches, swatches and a modern colour picker, one
message dialog. Behaviour stays as it is except where a menu entry moves. Hard rules stand: no new NuGet dependency;
Win32 only in `NeoFences.Shell`; the fences' own drawing (items, tiles, panels, widgets) is not restyled.

## 1. Menus

- **One style** (a shared resource dictionary in the App, applied to every `ContextMenu`, `MenuItem` and `Separator`):
  rounded corners (8 px), a soft shadow, 30 px rows, a soft hover highlight, a check glyph for checked entries, sub-menu
  arrows, light or dark like the fences (follows Windows' app mode; changes live). Destructive entries ("Delete fence",
  "Remove …") in a red ink at the end of their menu.
- **Icons** from Windows' icon font (Segoe Fluent Icons, falling back to Segoe MDL2 Assets), set through one helper with
  named glyphs; an entry without a glyph keeps the icon column empty so labels line up.
- **The fence menu** (12 lines): **Add ▸** (Item…, From desktop…, Games…, Folder panel…, Widget ▸ Clock / Date / System
  stats) · **View ▸** (Icon size ▸, Labels ▸, Sort by ▸, Layout ▸) · Auto-collect… · — · Rename (F2) · **Colour ▸** (§3) ·
  Lock position · Refresh · — · **New fence ▸** (Empty, Folder panel…) · Settings… · — · Detach tab (tabs only) · **Delete
  fence** (red). "Start with Windows" is only in Settings → General; "Exit NeoFences" only in the tray.
- **Item menus** (items, games, folder panels, widgets, several items) keep their entries in one order: Open first, then
  their own actions (Show as, Choose cover…, Size ▸, Open install folder, Copy path …), then Properties…, then Remove
  (red) last; the "Shift+right-click: Windows' menu" hint stays.
- **The tray menu** is the same WPF menu, opened at the pointer by a right-click on the tray icon, closed by a click
  elsewhere or Esc; "Leave safe mode" and "Undo delete" stay on top when they apply, Exit at the bottom. The native Win32
  popup menu goes.
- **Keyboard:** arrows, Enter, Esc and access keys as in any Windows menu.

## 2. Settings

- A **section list** on the left with icons — General, Fences, Appearance, Games, Game mode, Snapshots, Updates, About —
  the open one marked with the accent; only its page shows. "Game Library" becomes **Games** (window texts and guide too).
- Each section's existing cards move onto its page unchanged; **toggle switches** (with On / Off beside them) replace every
  on/off checkbox; choices (lists, sliders, the hotkey box) stay as they are.
- The **banner** (safe mode, changes not saved) shows above whichever page is open.
- **Opening a page directly:** tray → Restore snapshot → "More in Settings…" opens Snapshots; "Choose cover…" with online
  art off points to Games; everything else opens on General.
- **Keyboard:** the arrow keys move through the section list; Tab goes into the page.

## 3. Colour and dialogs

- **Colour ▸** (fence menu) shows a panel inside the menu: round swatches — None and the existing fence colours, the
  current one ringed — set with one click; **Use my accent colour** (the fence takes Windows' current accent as its
  colour); **Custom colour…**.
- **Custom colour…** opens a small Windows 11-style picker: a saturation/brightness field, a hue bar, a hex box and a
  preview; OK / Cancel; Esc cancels. Windows' `ChooseColor` dialog and its Shell wrapper go.
- **One message dialog** in the same style (title, text, one or two buttons, the default focused, Esc for the cancel
  button) replaces the 4 MessageBoxes (auto-collect's question, deleting a fence when its snapshot failed, two "this picture
  cannot be used" warnings) and also reports a failed Steam pick in Choose cover… (M34 deferred minor).

## Testing

- **Core, test-first:** colour maths (hex ↔ RGB ↔ HSV round trips, clamping, odd hex input), the swatch shown as current
  (a swatch, a custom colour, none), "use my accent" storing the accent as the fence's colour.
- **Prototype probe** on a copy of the owner's data: every menu (fence, item, game, panel, widget, tray), Settings pages,
  the colour picker and the dialog, in dark and light, screenshots to the owner.
- **Checklist AU** and a live check on a backup of the owner's data.

## Docs

GUIDE and README menu paths ("fence menu → Add → Item…", "Settings → Games") in the same commit as the change (ADR-050);
ADR-056; architecture, features, checklist AU, research note.

## Out of scope

A keyboard way into the fences and screen-reader work (M38); per-fence settings (M36); themes beyond light and dark;
moving the welcome or undo bar buttons to the new style.
