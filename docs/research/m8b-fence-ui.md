# M8b — fence UI and icon-only labels: results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), 1920×1080 at 100 %, Takeover on, NeoFences 1.0.1 installed.
**Build:** M8b prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section S · **Decision:** ADR-025.

## Live smoke on the prototype (installed copy restored after each run)

| Check | Result |
|---|---|
| Tray menu and Settings show the Peek hotkey as key caps | **Pass**: "Peek Ctrl+Alt+Space"; the box has the description as HelpText. |
| Settings → Labels for new fences = On hover | **Pass**: `defaultLabels: onHover` saved; existing fences unchanged. |
| Settings → Apply to all fences | **Pass**: every fence `onHover`; the Inbox shows icons only (9 per row instead of 5). |
| Pop-under name (S1, S3) | **Pass**: pointing at an icon shows its name under it ("AISnatch"); gone after leaving; a selected icon shows its name ("Apex Light"). |
| Fence menu → Labels → On hover (keyboard) | **Pass**: only that fence changes. |
| Shortcut arrows (S5) | **Pass**: arrows on shortcuts, none on Recycle Bin. |
| Recording the current Peek hotkey (S8) | **Pass**: recorded ("Peek: Ctrl+Alt+Space"), no peek; registered again when the box lost focus. |
| First title double-click after Explorer was in front (S6) | **Pass**: rolls up on the first double-click, with Explorer in front and after closing it (275 → 32 px). |
| Peek, Esc, quick-hide on/off (M5 regressions) | **Pass**: 2/2 fences topmost, 0 after Esc, 0 shown in quick-hide, 2 after. |
| Log | 0 warnings or errors across the runs. |

## Keyboard focus after a click from another app (M2c carry-over)

An experiment called `Activate()` on every press in a fence. With Explorer in front, a click on a fence icon still did
not make the fence the foreground window (2 of 2 tries), so F2 would have gone elsewhere. Fences sit on the desktop's
input queue (ADR-015); the experiment was removed and the limit stays documented.

## Automation notes

- WPF context menus are not found from the UI Automation root by `AutomationId`; the fence menu was driven by
  keyboard (Down ×5, Right, Down, Enter) instead.
- A `ComboBoxItem` is selected through its `ListItem` element; a name search alone finds the inner text.
