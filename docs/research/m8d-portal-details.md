# M8d — Portal details: results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.1.0 installed · **Stick:** LOCO_DUCK (HP USB
Flash Drive, exFAT, G:), lent by the user for tests; only `G:\NeoFences-test` was used.
**Build:** M8d prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section U · **Decision:** ADR-027.

## Safely Remove (U1)

`CM_Request_Device_Eject` on the stick's USB storage device (what Safely Remove does), with a Portal of
`G:\NeoFences-test` shown:

| Build | Result |
|---|---|
| v1.1.0 (installed) | **Vetoed**: veto type 6 (device), `STORAGE\Volume\{…}` — the volume was in use (the Portal's watchers). |
| M8d prototype | **Ejected**; log "drive of Portal … is being removed: released it"; the Portal shows "This folder is not available right now"; NeoFences kept running. |

The stick stayed ejected afterwards (the user replugs it; U2).

Script lessons:
- Explorer's `FolderItem.InvokeVerb('Eject')` did nothing from a script (the verb's name is "E&ject").
- Ejecting the disk node itself is an illegal request (veto 8): Safely Remove ejects its parent, the USB storage device.

## Other checks on the prototype

| Check | Result |
|---|---|
| U4 a new file arrives while one is selected | **Pass**: items grew (gamma.txt), alpha.txt stayed selected. |
| U5 Enter on two folders | **Pass**: two Explorer windows; the Portal stayed at its folder. |
| M8b regressions (Settings, first title double-click, Peek, quick-hide) | **Pass**. |
| Log | 0 errors. |
