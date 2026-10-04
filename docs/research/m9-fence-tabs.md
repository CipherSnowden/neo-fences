# M9 — Fence tabs: results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), 1920×1080 at 100 %, NeoFences 1.1.2 installed.
**Build:** M9 prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section V · **Decision:** ADR-029.

## Live smoke on the prototype (installed copy restored)

| Check | Result |
|---|---|
| V1 merge "New fence" into the Inbox by dragging its title | **Pass**: tabs [Inbox, New fence], New fence shown; 2 fence windows became 1. |
| V2 switch by clicking headers | **Pass**: Inbox (31 items) and New fence (0 items) shown in turn. |
| V4 colour via the header menu (keyboard: Colour → Teal) | **Pass**: `tabColor: teal`; teal bar under the header (screenshot). |
| V10 restart | **Pass**: one window, both tabs, New fence still shown. |
| V11 roll-up on a box (double-click empty strip) | **Pass**: 275 → 32 → 275 px. |
| V9 drop an Inbox item onto the New fence header | **Pass**: New fence 0 → 1 item. |
| V7 drag the header out onto the desktop | **Pass**: tabs cleared, 2 windows again, the fence at the drop point. |
| V11 quick-hide on/off | **Pass**: 0 → 2 fences shown. |
| Log | 0 warnings or errors. |
