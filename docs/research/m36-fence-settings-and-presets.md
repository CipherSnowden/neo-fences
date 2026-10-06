# M36 — Fence settings and presets (0.23.0): prototype probe and calls

**Date:** 2026-10-06 · **Spec:** `docs/superpowers/specs/2026-10-06-fence-settings-and-presets-design.md` · **Decision:** ADR-057
**Plan:** `docs/superpowers/plans/2026-10-06-m36-fence-settings-and-presets.md`

## Prototype probe (two runs on a copy of the owner's data, owner's OK, screenshots sent)

**Run 1** — fence menu → **Fence settings…** on the Apps fence: the window in the Settings style, every look setting "Like
all fences (…)". Each built-in preset on the fence: **Glass** (light tinted glass), **Title strip**, **Solid** (dense,
visibly roomier), **Compact** (small icons, tight), **Minimal** (almost no background; the title bar gone while the pointer
is away, back on hover). Saving the look as "Probe look" added an own chip with its ✕ (Minimal stayed lit: the first of two
equal looks). **Like all fences again** brought the Settings look back.

**Run 2** — the window after the fix below; Settings → Snapshots → **Export setup…** through Windows' save dialog: a 3 KB
file (manifest, config, items — the owner has no item icon pictures or chosen covers; found covers are found again online);
**Import setup…** of it: the question ("Replace your fences, items and settings with this file's?"), then the snapshot
"Before import" in the list and the notice; About → **Reset settings to defaults…**: the question, then every page at its
defaults. The owner's data and startup entry were restored after each run.

**Fixed after the probe:** the title's size, weight and alignment sat three in a row and cut "Like all fences (…)" — one
row each now; the Snapshots card no longer says settings are never changed (a whole snapshot brings them back).

## Calls made while prototyping

- Per-fence title fonts are back, in Fence settings (not the menu): ADR-038's "one font" amended by ADR-057.
- Spacing is a look value (a preset sets it, "Like all fences" clears it), listed under Items as the spec does; title
  alignment and spacing have no Settings value, so "Like all fences" is today's look (Left, Normal).
- Built-ins: Glass (tinted glass, 25 %, names below), Minimal (accent edge, 4 %, title bar on hover, names on hover), Title
  strip (60 %), Solid (accent edge, 85 %, Roomy), Compact (Compact, 32 px icons, names on hover; style and background like
  all fences). A saved own preset keeps the fence's icon size and labels; an own preset's name replaces it, a built-in's is
  refused.
- The title bar on hover keeps its row and fades; it always shows while rolled up, renamed, dragged or with the menu open.
  Spacing is the inset around each element (0 / 2 / 8 DIP): elements keep their size.
- The setup file carries only pictures the setup uses, each found only by its exact `icons/<file>` or `covers/<file>`
  name; oversized parts are refused or skipped; a newer export is refused with its version named.
- Snapshots before an import or a reset are whole (Settings, the Games settings, own presets); ordinary snapshots are as
  before (ADR-030). Reset also returns the Games page's switches to their defaults; folders, hidden games and chosen covers
  stay. The config schema stays 5.

## Live check

(Filled in by the live check.)
