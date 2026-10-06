# M36 — Fence settings and presets (0.23.0) — design

**Date:** 2026-10-06 · **Status:** approved in brainstorm, awaiting written-spec review
**Decisions:** ADR-057 (per-fence look values that fall back to "all fences"; presets copy; export / import as one file)
**Source:** `docs/research/v1-readiness.md` (Customization: no per-fence settings window, no export / import, no look
presets); the owner's "one kind of fence" idea (per-fence settings instead of fence types; `Fence.Kind` stays in code).
**Mockups:** visual companion screens `fence-settings` (owner picked A, a small window) and `presets` (all five).

## Goal

Every fence can have its own look and behaviour, a look can be reused in one click, and a whole setup can be saved to one
file and loaded on another PC. Nothing changes for an existing setup until the owner picks something. Hard rules stand:
user files never touched; no new NuGet dependency (`System.IO.Compression` is in the BCL); Core pure and test-first.

## 1. Fence settings

- Fence menu → **Fence settings…** (next to Rename; a tab has its own) opens a small window in the Settings style,
  "<title> — fence settings". Every change shows on the fence at once; there is no OK.
- **Preset** (top): chips — "Like all fences", the five built-ins, the owner's own — and "Save this look as a preset…".
- **Look:** Colour style (Accent edge / Tinted glass / Title strip); Background strength (a slider; one value for light
  and dark mode); Title (font, size, weight, alignment left / centre / right); **Show the title bar** (a switch — off: the
  title bar shows while the mouse is over the fence, so the fence can still be moved, rolled up and renamed).
- **Items:** Icon size, Labels, Layout (as in View ▸), and **Spacing** — Compact, Normal, Roomy (cells tighter or wider).
- **"Like all fences":** each Look setting's first choice is "Like all fences (…)" with the current Settings value; a
  setting with its own value is marked; **Like all fences again** drops every own value at once.
- An existing setup starts as "Like all fences" everywhere; icon size, labels and layout keep their current values.

## 2. Presets

- A preset is a set of look values (style, background, title font / alignment / visibility, spacing) and, where it needs
  them, icon size and labels. Applying one copies its values onto the fence; the fence keeps its own colour.
- **Built-in:** Glass (tinted glass, light background, names below); Minimal (title bar on hover, almost no background,
  names on hover); Title strip (coloured title strip, dark background); Solid (accent edge, dense background, Roomy);
  Compact (small icons, Compact spacing, names on hover).
- **Own presets:** "Save this look as a preset…" asks for a name and saves the fence's current look; a small ✕ on an own
  chip deletes it (built-ins cannot be deleted); kept in the config, so an export carries them.
- Presets are copies, not links: changing a fence later does not change the preset, nor the other fences that used it.
- The "Like all fences" chip clears the fence's own look values (icon size, labels and layout stay).

## 3. Export, import, reset

- **Settings → Snapshots → Export setup…** saves one `.neofences` file (a zip): the fences, items and settings, the own
  presets, pictures chosen as item icons, covers chosen with Choose cover…, and a small manifest (NeoFences version, schema
  versions). Not the logs, not the game-library scan (it runs again on any PC), never the user's files. Export changes nothing.
- **Import setup…:** pick a file → NeoFences checks it (a NeoFences export, readable, not from a newer version) → "Replace
  your fences, items and settings with this file's? Your current setup is saved as a snapshot first." → the snapshot, then
  the file's setup and pictures go in place and the fences reload where they are. Tray → Restore snapshot undoes it. Items
  pointing at paths that do not exist on this PC show Missing as usual.
- **Settings → About → Reset settings to defaults…** (asked first, a snapshot first): every Settings page back to its
  defaults; fences, items, own presets and the Games page's data (folders, hidden games, chosen covers) untouched. Per fence:
  "Like all fences again" (§1).

## Testing

- **Core, test-first:** the look a fence shows (its own values over Settings'); presets (built-ins, apply, save, delete,
  "Like all fences"); the export's contents and checks (a damaged zip, a newer version, missing parts, a picture name that
  tries to leave its folder); what reset keeps; the normalizer for the new fields (odd values, unknown preset names).
- **Prototype probe** on a copy of the owner's data; **checklist AV**; a live check.

## Docs

GUIDE (fence settings, presets, export / import, reset) in the same commit as the change (ADR-050); ADR-057; architecture,
features, checklist AV, research note.

## Out of scope

Linked presets; importing only some fences; cloud sync; more hotkeys; per-fence widget or panel options beyond what they
have.
