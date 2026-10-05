# Features

Parity with Stardock Fences 5/6 plus NeoFences extras. Status: `—` not started · `wip` ·
`done` · `cut` (with reason) · `parked` (ADR-040: may come back as dynamic fences). Target: release in which it ships.
Update status with the commit that changes it. Versions: 0.x since the pivot (ADR-040); "v1.x" in older rows are
pre-reset dev builds.

Sources for Fences 6: stardock.com news posts "Now Announcing: Fences 6", "Fences 6 Beta 2",
"Releasing Fences 6.20" (checked 2026-10-02).

## Fences parity

| Feature | Fences | Target | Milestone | Status | Notes |
|---|---|---|---|---|---|
| Fences holding desktop icons (Takeover) | 1+ | — | M2 | parked | ADR-040: fences hold virtual items instead; the desktop stays as Windows shows it |
| Virtual items: own name, icon, path, arguments, run as admin, note | — | 0.9 | M18 | done | ADR-040/041; Properties dialog; the same target in several fences |
| Add item… (file, folder, app, website) | — | 0.9 | M18 | done | fence menu; Browse or a typed path / web address; "An app…" lists Start's apps (0.10) |
| Store / Start apps as items | — | 0.10 | M19 | done | `shell:AppsFolder\<id>`: from the app list or dragged from Start; Missing when uninstalled (ADR-042) |
| Add from desktop… | — | 0.10 | M19 | done | fence menu or tray: desktop entries grouped (Games / Apps / Folders and files / Web links), each group into a fence; items point at the desktop entries (ADR-042) |
| Inbox / default fence for new items | — | — | M2 | parked | no auto-fill (ADR-040) |
| Move / resize fences | 1+ | v1 | M2 | done | snap 8 px gap / edge alignment (M2c) |
| Scrolling inside fences | 2+ | v1 | M2 | done | thin scrollbar (M2c) |
| Per-fence icon size | 5 | v1 | M2 | done | 32/48/64/96 (M2c) |
| Thumbnails for images/videos | — | v1 | M2 | done | IShellItemImageFactory (M2b) |
| Open / select | 1+ | v1 | M2 | done | double-click / Enter; arguments and run as admin since 0.9 |
| Keyboard navigation in fences | 1+ | v1 | M2c | done | arrows, Ctrl+A, Enter; Del removes the item, F2 / Alt+Enter Properties (0.9) |
| Rename / delete fence | 1+ | v1 | M2c | done | delete removes the fence and its items, asked first (0.9); files never touched |
| Item menu | 1+ | 0.9 | M18 | done | NeoFences' own safe menu; Windows' full menu on Shift+right-click, labelled "acts on the real file" |
| Rename / delete items | 1+ | 0.9 | M18 | done | rename = the item's own name; Del = remove the item (no file operation, ADR-040) |
| Drag-drop in/out/between fences | 1+ | 0.9 | M18 | done | drops create items (link, never move); Ctrl+drag duplicates; dragging out copies |
| Missing / unavailable targets | — | 0.9 | M18 | done | watched (≤ 64 folders), renames followed, Locate… / Remove, per-fence Refresh; a generic icon for their type (0.10) |
| Bulk fix of missing items | — | 0.10 | M19 | done | after one Locate…, "Fix N more items?" for the others from the same old place; undo from the tray (ADR-042) |
| Rubber-band selection | 1+ | v1 | M3 | done | M3b; Ctrl adds |
| Folder Portals → folder views | 3+ | 0.11 | M21 | done | ADR-044: read-only (no drop into the folder, no rename/delete from NeoFences' menu); subfolders open in Explorer |
| Sort (name/type/date) | 2+ | v1 | M4 | done | one time; by the names shown (0.9) |
| Draw fence by right-drag on desktop | 1+ | v1 | M5 | done | S2 keeps the plain right-click menu (ADR-020) |
| Quick-hide (double-click desktop) | 1+ | v1 | M5 | done | fences + desktop icons (user choice); a double-click on a native icon opens it |
| Peek (fences over windows) | 3+ | v1 | M5 | done | Ctrl+Alt+Space default; ends on hotkey, Esc, click outside, opening an item |
| Roll-up (double-click title) | 2+ | v1 | M5 | done | saved; locked fences too |
| Roll-up expand: hover (with delay) / click | 6.20 | v1 | M5 | done | hover (default) or click, chosen in Settings (M6b, ADR-022) |
| Smart placement spacing | 6.20 | v1 | M5 | done | first free spot with 8 px gaps (`FreeSpot`) |
| Per-monitor layout, survives resolution changes | 2+ | v1 | M1/M2 | wip | M1 Core + M2a placement/persistence done; real multi-monitor untested |
| Lock fences | 3+ | v1 | M2 | done | (M2c) |
| Icon-only fences (name on hover) | 6 | v1.1 | M8b | done | per fence (menu → Labels) + Settings default and "apply to all" (ADR-025) |
| Shortcut arrows | — | v1.1 | M8b | done | Settings switch, off by default; drawn by NeoFences (ADR-025) |
| Light/dark, acrylic look | 5 | v1 | M2/M6 | done | follows Windows live; label shadow (M2c, user choice) |
| Settings window, tray | 1+ | v1 | M6 | done | tray + Pause (M6a); Fluent settings window (M6b); Appearance (M14) |
| Hide desktop icons while NeoFences runs | — | 0.9 | M18 | done | Settings switch, off by default; restored on exit, crash and kill (watchdog) |
| Start with Windows | 1+ | v1 | M6 | done | brought forward for power-loss recovery (ADR-019); fence-menu toggle |
| Installer (per user, no admin), uninstall restores icons and keeps data | — | v1 | M7 | done | Velopack Setup.exe; auto-update from GitHub Releases (ADR-023, ADR-039) |
| Rules auto-sort | 3+ | — | M11 | parked | ADR-040: later as auto-collect rules (M20) |
| Fence tabs (+ drop onto tab header, tab color) | 6 | v1.2 | M9 | done | combine by dragging a title onto another; drag a tab out to split (ADR-029) |
| Desktop pages | 3+ | — | — | — | |
| Snapshots (save/restore layouts) | 2+ | v1.3 | M10 | done | tray + Settings; fences, places and items (0.9); can be undone (ADR-030) |
| Desktop icon color tint | 6 | — | — | — | |
| Per-fence colors / custom title fonts | 3+ | v1.7 | M14 | done | 8 swatches + Custom…; 3 colour styles; one title font for all fences (ADR-038) |

## NeoFences extras

| Feature | Target | Status | Notes |
|---|---|---|---|
| Game-mode idle (fullscreen + borderless detection, mouse hook removed) | v1 (M6) | done | M6a; target checks and renames wait for the game's end (0.9) |
| Watchdog: icons always restored | v1 (M0/M2) | done | when "Hide desktop icons" is on (icons-hidden marker); sign-out (C8) pending |
| Live blur over Wallpaper Engine | v1 (M0/M2) | wip | ADR-011: layered + accent blur; user approved the current tint |
| Blur tint preference (lighter/darker) | v1.7 (M14) | done | background strength slider, one value per Windows tone (ADR-036) |
| Search palette across all fences | — | parked | built on branch `m15-search-palette` (local history bundle only), not merged |
| Game Library (Steam/Epic/GOG/Ubisoft Connect/EA, cover art) | v1.5 | done | launchers, Xbox, game folders, Desktop game shortcuts (ADR-032); since 0.12 games are items in any fence (cover tile or icon, Add games…, new games go to a chosen fence; ADR-045) |
| One kind of fence: any item in any fence, its look from its kind and settings | 0.12 (M22) | done (games, sizes) | ADR-045, ADR-046; next: a folder panel element, widgets (clock, calendar) |
| Element sizes 1–4 × 1–4 and a fence grid (Flow packed / Free fixed positions) | 0.13 (M24) | done | ADR-046; Size ▸ picker, Layout ▸ per fence |
| Folder views: types, files/folders only, newest N, live sort, "+ N more" | 0.11 (M21) | done | ADR-044; Downloads/Screenshots start newest first |
| Auto-collect rules (the other half of dynamic collections) | later | — | replaces Rules (ADR-040) |
| Theme packs: "Nanosuit" (Crysis HUD), "Animus" (Assassin's Creed) | v2 | — | |
| Custom Win11-style compact context menu | v2 | — | |
| Wallpaper-adaptive accent color | v1.7 (M14) | done | Wallpaper Engine preview → Windows wallpaper → Windows accent; no screen capture (ADR-036) |
| Optional local-AI auto-sort (llama.cpp) | idea | — | |
| Cross-platform (Avalonia) | idea | — | only if needed; Core is portable |
