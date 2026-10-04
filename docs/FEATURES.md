# Features

Parity with Stardock Fences 5/6 plus NeoFences extras. Status: `—` not started · `wip` ·
`done` · `cut` (with reason). Target: release in which it ships. Update status with the commit
that changes it.

Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fences 6 Beta 2",
"Releasing Fences 6.20" (checked 2026-10-02).

## Fences parity

| Feature | Fences | Target | Milestone | Status | Notes |
|---|---|---|---|---|---|
| Fences holding desktop icons | 1+ | v1 | M2 | done | Takeover model, ADR-002; M2b (ADR-014) |
| Inbox / default fence for new items | — | v1 | M2 | done | Fences uses "unfenced desktop"; we use Inbox |
| Move / resize fences | 1+ | v1 | M2 | done | snap 8 px gap / edge alignment (M2c) |
| Scrolling inside fences | 2+ | v1 | M2 | done | thin scrollbar (M2c) |
| Per-fence icon size | 5 | v1 | M2 | done | 32/48/64/96 (M2c) |
| Thumbnails for images/videos | — | v1 | M2 | done | IShellItemImageFactory (M2b) |
| Open / select | 1+ | v1 | M2 | done | double-click opens in front; Ctrl/Shift multi-select (M2b) |
| Keyboard navigation in fences | 1+ | v1 | M2c | done | arrows, Ctrl+A, Enter once the fence is active; not after clicking from another app (ADR-015, re-checked in M8b); F2 rename and Del recycle since M3a |
| Rename / delete fence | 1+ | v1 | M2c | done | delete moves items to the Inbox |
| First-run prompt in the Inbox | — | v1 | M2 | done | asks once whether to hide desktop icons (user choice 2026-10-02) |
| Shell context menu on items | 1+ | v1 | M3 | done | Windows' classic menu incl. extensions; Shift = extended (M3a, ADR-016) |
| Rename / delete to Recycle Bin | 1+ | v1 | M3 | done | F2 in place; Del and Shift+Del always recycle (user choice, M3a) |
| Drag-drop in/out/between fences | 1+ | v1 | M3 | done | M3b, ADR-017; drops from Explorer pending user check (K4/K5) |
| Rubber-band selection | 1+ | v1 | M3 | done | M3b; Ctrl adds |
| Folder Portals | 3+ | v1 | M4 | done | live view, newest first by default (M4, ADR-018); Safely Remove works while shown (M8d, ADR-027) |
| Portal subfolder navigation | 3+ | v1 | M4 | done | browse inside with Back/Backspace; Ctrl+double-click opens Explorer (user choice) |
| Sort (name/type/date) | 2+ | v1 | M4 | done | Portals live; desktop fences one-time; type = extension |
| Draw fence by right-drag on desktop | 1+ | v1 | M5 | done | S2 keeps the plain right-click menu (ADR-020) |
| Quick-hide (double-click desktop) | 1+ | v1 | M5 | done | fences + desktop icons (user choice); a double-click on a native icon opens it |
| Peek (fences over windows) | 3+ | v1 | M5 | done | Ctrl+Alt+Space default; ends on hotkey, Esc, click outside, opening an item |
| Roll-up (double-click title) | 2+ | v1 | M5 | done | saved; locked fences too |
| Roll-up expand: hover (with delay) / click | 6.20 | v1 | M5 | done | hover (default) or click, chosen in Settings (M6b, ADR-022) |
| Smart placement spacing | 6.20 | v1 | M5 | done | first free spot with 8 px gaps (`FreeSpot`) |
| Per-monitor layout, survives resolution changes | 2+ | v1 | M1/M2 | wip | M1 Core + M2a placement/persistence done; real multi-monitor untested |
| Lock fences | 3+ | v1 | M2 | done | (M2c) |
| Icon-only fences (name on hover) | 6 | v1.1 | M8b | done | per fence (menu → Labels) + Settings default and "apply to all"; the name pops up under the hovered or selected icon (ADR-025) |
| Shortcut arrows | — | v1.1 | M8b | done | Settings switch, off by default; drawn by NeoFences (ADR-025) |
| Light/dark, acrylic look | 5 | v1 | M2/M6 | done | follows Windows live; label shadow (M2c, user choice) |
| Settings window, tray | 1+ | v1 | M6 | done | tray + Pause (M6a); Fluent settings window, hotkey recorder (M6b, ADR-022); Appearance section in v1.7 (M14) |
| Start with Windows | 1+ | v1 | M6 | done | brought forward for power-loss recovery (ADR-019); fence-menu toggle |
| Installer (per user, no admin), uninstall restores icons and keeps data | — | v1 | M7 | done | Velopack Setup.exe; auto-update from GitHub Releases since v1.8 (ADR-023, ADR-039) |
| Rules auto-sort | 3+ | v1.4 | M11 | done | type, game (launchers or a folder of mine), name, date, size; Apply rules now can be undone (ADR-031) |
| Fence tabs (+ drop onto tab header, tab color) | 6 | v1.2 | M9 | done | combine by dragging a title onto another; drag a tab out to split (ADR-029) |
| Desktop pages | 3+ | v2 | — | — | |
| Snapshots (save/restore layouts) | 2+ | v1.3 | M10 | done | tray + Settings; a restore keeps newer items and can be undone (ADR-030) |
| Desktop icon color tint | 6 | v2 | — | — | |
| Per-fence colors / custom title fonts | 3+ | v1.7 | M14 | done | 8 swatches + Custom… (Windows' colour picker); 3 colour styles; one title font for all fences, in Settings (v1.7.1, ADR-038) |

## NeoFences extras

| Feature | Target | Status | Notes |
|---|---|---|---|
| Game-mode idle (fullscreen + borderless detection, mouse hook removed) | v1 (M6) | done | M6a: notification state + app in front; Peek ignored; shell work deferred (ADR-021) |
| Watchdog: icons always restored | v1 (M0/M2) | wip | ADR-005/012: M2a host done (detached, takeover-active marker); sign-out (C8) pending |
| Live blur over Wallpaper Engine | v1 (M0/M2) | wip | ADR-011: layered + accent blur, M2a host done; user approved the current tint |
| Blur tint preference (lighter/darker) | v1.7 (M14) | done | background strength slider, one value per Windows tone (ADR-036) |
| Search palette across all fences | v1.8 (M15) | parked | built on branch `m15-search-palette`, not merged (user choice 2026-10-04: not needed yet) |
| Game Library fence (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v1.5 | M12 | done: launchers, Xbox, game folders, Desktop game shortcuts; Steam posters; hide; live rescans (ADR-032) |
| Theme packs: "Nanosuit" (Crysis HUD), "Animus" (Assassin's Creed) | v2 | — | |
| Custom Win11-style compact context menu | v2 | — | |
| Wallpaper-adaptive accent color | v1.7 (M14) | done | Wallpaper Engine preview → Windows wallpaper → Windows accent; no screen capture (ADR-036) |
| Optional local-AI auto-sort (llama.cpp) | idea | — | rules first; only if rules fall short |
| Cross-platform (Avalonia) | idea | — | only if needed; Core is portable |
