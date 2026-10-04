# M16 — v1.7.1 polish: results

**Date:** 2026-10-04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.7.0 installed.
**Build:** M16 prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` AA19–AA24 · **Decision:** ADR-038.

## Live checks on the prototype (the user's go each time; config and installed copy restored; Firefox PiP untouched)

| Check | Result |
|---|---|
| AA20 Settings with a removed title font ("NoSuchFont M16") | **Pass**: the box shows the name. |
| Fence menu | **Pass**: Colour present, Title font gone (ADR-038). |
| Colour → Custom… → Esc | **Pass**: Windows' colour dialog opened and cancelled; the fence menu opens again afterwards (screenshot). The tick after Cancel was not read back by the script (its UIA lookup missed the reopened menu): AA19 by hand. |
| Before ADR-038: the fence menu's Font list | opened in ~250 ms with 87 fonts after the faces were deferred — moot once the menu was removed. |
| Log | 0 warnings or errors in every run. |

The user decided during this batch: one title font for all fences, set in Settings; the fence menu keeps only Colour.

## Core (test-first)

7 new tests (429 in all): tray labels cut before an emoji; Accent edge titles readable with pale and dark custom colours
in both tones, readable colours unchanged; one title font for all fences, an old per-fence font ignored and dropped.

## Branch check after the final review (the user's go; config and installed copy restored)

| Check | Result |
|---|---|
| Settings with a removed font; fence menu without Title font; Custom… → Cancel | **Pass** (as above) |
| Log | 0 warnings or errors; config byte-identical afterwards. |
