# Manual test checklist

Shell/UI behaviour that unit tests can't cover. Run the relevant sections at the end of every
milestone; record results in that milestone's research note or the session log.
Mark each row PASS / FAIL / N/A with a short note. "Fence" = the spike fence in M0, real fences later.

## A — Backdrop (spec §4.1)
| ID | Steps | Expected |
|---|---|---|
| A1 | Static wallpaper, layered fence with accent blur (ADR-011) | fence shows blurred wallpaper |
| A2 | Wallpaper Engine animated wallpaper running | blur shows the animation moving, no stutter |
| A3 | Click another app so the fence is inactive | blur stays (M0: DWM backdrops failed this; layered + accent passed) |
| A4 | Drag and resize the fence by its title / edges | smooth; no black flashes |
| A5 | Look at taskbar and Alt+Tab | fence appears in neither |

## B — Z-order and Win+D (spec §4.2, §4.4)
| ID | Steps | Expected (fence owned by Progman, ADR-011, unless stated) |
|---|---|---|
| B1 | Open Notepad over the fence | Notepad covers the fence |
| B2 | Click inside the fence while Notepad is focused | Notepad keeps focus; fence does not jump above it |
| B3 | Win+D | apps hide, fence stays visible (owner = Progman, ADR-011) |
| B4 | Taskbar "Show desktop" corner button | same as B3 |
| B5 | Win+D again / click an app | apps return; fence back under them |
| B6 | Win+M, then Win+Shift+M | fence never minimizes |
| B7 | `Stop-Process -Name explorer -Force` | fence survives; log shows TaskbarCreated + re-applied. Repeat with "owned by Progman": record whether the fence is destroyed |
| B8 | Run Task Manager as admin, focus it, Win+D | fence stays visible |
| B9 | Strategy "bottom only", Win+D | baseline: fence expected to disappear (confirms the problem is real) |
| B10 | Task Manager → Windows Explorer → Restart (graceful) | fence survives; `GetWindow(fence, GW_OWNER)` is the new Progman; Win+D still keeps the fence |
| B11 | Block the fence UI thread for 10 s (debug button) | desktop right-click menu and taskbar stay responsive |

## C — Desktop icons and watchdog (spec §4.3, ADR-005)
| ID | Steps | Expected |
|---|---|---|
| C1 | Hide desktop icons | icons vanish; Query reports hidden=True |
| C2 | Show desktop icons | icons return |
| C3 | Hide, then close the Lab window | icons return; log "watchdog: clean shutdown" |
| C4 | Hide, then CRASH (FailFast) | icons return within ~2 s; lab restarts |
| C5 | Hide, then kill the lab PID (`Stop-Process -Force`) | same as C4 |
| C6 | Crash 4 times within 10 min | 4th time: icons return, lab stays down ("restart limit") |
| C7 | Hide, then restart Explorer | icons hidden again within ~5 s |
| C8 | Hide, then sign out and back in | icons visible after sign-in |

## D — Desktop gestures (spec §4.5, ADR-007)
M0 spike checks. In the app they are section N (D2 → N3, D7 → N14, D8 → N13).
| ID | Steps | Expected |
|---|---|---|
| D1 | Mouse hook start; double-click empty desktop | log `GESTURE DoubleClick` |
| D2 | Double-click inside the fence / an app / the taskbar | no gesture |
| D3 | Right-click: observe; right-drag on empty desktop | `RightDragStarted` then `RightDragCompleted`; Explorer menu appears (baseline) |
| D4 | Right-click: S1; right-drag | gestures logged; record whether Explorer's menu still appears and whether the desktop gets stuck |
| D5 | Right-click: S2; right-drag, then a plain right-click | drag: no menu. Plain click: normal desktop menu appears (replayed) |
| D6 | Any mode: right-drag starting on an app window | no gesture |
| D7 | Elevated Task Manager focused; double-click desktop | record whether the gesture fires |
| D8 | Hook running: move the mouse fast, play a game briefly | no perceptible lag |
| D9 | Mouse hook stop; double-click desktop | no gesture; log "WH_MOUSE_LL removed" |

## E — Game detection (spec §4.7)
| ID | Steps | Expected |
|---|---|---|
| E1 | Focus a borderless-fullscreen game | `gameLike=True` |
| E2 | Focus an exclusive-fullscreen game (if any) | `quns=QUNS_RUNNING_D3D_FULL_SCREEN`, `gameLike=True` |
| E3 | Focus a maximized browser | `gameLike=False` |
| E4 | Taskbar auto-hide on; focus a maximized browser | `gameLike=False` (WS_CAPTION guard) |
| E5 | Click the desktop with Wallpaper Engine running | no game check logged for Progman/WorkerW |

## F — Multi-monitor (N/A on one monitor)
| ID | Steps | Expected |
|---|---|---|
| F1 | Move the fence to monitor 2; Win+D | fence stays visible |
| F2 | Double-click / right-drag on monitor 2's desktop | gestures fire |

## G — Fence host (M2a+)
| ID | Steps | Expected |
|---|---|---|
| G1 | First start with no config | one "Inbox" fence at (24,24) DIPs on the primary monitor; blurred wallpaper behind; rounded corners; not in taskbar/Alt+Tab |
| G2 | Drag and resize the fence, exit (`NeoFences.exe --exit`), start again | same position and size |
| G3 | Right-click body → New fence | second fence cascaded at (56,56); persists after restart |
| G4 | Change taskbar size/position or display scaling while running | fences stay on screen; returning to the old setting restores exact positions |
| G5 | `NeoFences.exe --exit` | clean exit; watchdog log "clean shutdown"; no processes left |
| G6 | Preview "Hide desktop icons" on, then kill NeoFences (`Stop-Process -Force`) | watchdog restores icons and restarts NeoFences, which hides them again |
| G7 | Preview off, kill NeoFences | watchdog restarts it but does NOT touch desktop icons |
| G8 | Start a second NeoFences.exe while one runs | second exits immediately; still one set of fences |
| G9 | Scripted session end (no sign-out needed): with the preview on, send WM_QUERYENDSESSION (lParam ENDSESSION_LOGOFF) to every top-level window of the main process | log `session ending (Logoff)` → `desktop icons hidden: false` → `NeoFences exited`; watchdog `IfSessionContinues` → `main restarted (session end was cancelled)`; new instance hides icons again |
| G10 | Double-click a fence title | fence does not maximize (no maximize box) |

## H — Desktop items (M2b+)
| ID | Steps | Expected |
|---|---|---|
| H1 | Start with no fenced items (or a new desktop file) | every visible desktop item (and enabled special icons, Recycle Bin first) lands in the Inbox; log `desktop reconciled: N added` |
| H2 | Fresh config (Takeover off, never answered): start; click "Not now"; restart | banner shows once in the Inbox only; after "Not now" it never returns; icons stay on the desktop |
| H3 | Look at the icons | app shortcuts show the app icon, folders the folder icon, images/videos a thumbnail; correct transparency, no black boxes |
| H4 | Look at the labels | Explorer names ("Steam", not "Steam.lnk"; extensions follow Explorer's setting); long names wrap to 2 lines with an ellipsis; tooltip shows the full name |
| H5 | Double-click a shortcut, a folder, Recycle Bin, and a shortcut whose target was deleted | each opens in front of other windows; the broken one logs `could not open` (or shows Windows' own dialog) and NeoFences keeps running |
| H6 | In Explorer: create a file on the Desktop, rename it, delete it (Recycle Bin) | the Inbox shows it within ~1 s, renames it in place, removes it; `config.json` follows |
| H7 | Unzip/copy ~500 files onto the Desktop, then delete them | all appear, then all disappear; if `lost events; reconciling` is logged the end result is still right |
| H8 | Click an item; Ctrl+click and Shift+click others; hover | single, multi and range selection highlight; hover highlight |
| H9 | Fence with more items than fit; mouse wheel over it | scrolls vertically, no horizontal scrollbar |
| H10 | Put a large video and a shortcut to a disconnected network drive on the Desktop; restart | fences appear at once and stay responsive; slow icons fill in later or stay blank |
| H11 | Turn Takeover on from the banner or menu | native icons hide; the menu item is checked; the banner is gone for good |

## I — Fence interactions (M2c+)
| ID | Steps | Expected |
|---|---|---|
| I1 | Right-click a fence's body; then the Inbox's | menu: New fence, Rename fence, Icon size ►, Lock position, Delete fence (items go to the Inbox), Hide desktop icons, Exit; the Inbox has no Delete |
| I2 | Rename fence: type a name + Enter; again + Esc; again, clear the box + Enter; restart | Enter saves, Esc cancels, empty keeps the old title; title survives restart |
| I3 | Icon size ► Small / Large / Extra large | icons re-render sharp at the new size, labels re-wrap; the size survives restart |
| I4 | Lock position, then drag the title and an edge; unlock and drag again | locked: nothing moves or resizes; unlocked: both work; lock survives restart |
| I5 | Create a fence, move some items into it (M3) or use an empty one, Delete fence | fence gone; its items at the end of the Inbox; no file touched |
| I6 | Slowly drag a fence near another fence and near the screen edge; then slowly away | snaps to an 8 px gap or lines up with the other fence's edge within ~12 px; slow drags away escape (no sticking) |
| I7 | Drag a fence's right edge near another fence's right edge | the edge lines up; the left edge stays |
| I8 | Click an item; arrow keys; Ctrl+A; Enter | selection moves; all selected; Enter opens every selected item in front; the fence stays behind other windows |
| I9 | Switch Windows to light mode and back (Settings → Personalization → Colors) | fences follow live: light veil + dark text in light mode, unchanged dark look in dark mode; both readable |
| I10 | Labels over a bright part of the wallpaper | soft shadow (dark mode) / white halo (light mode) keeps them readable |
| I11 | Fence with more items than fit; hover and drag the scrollbar | thin rounded scrollbar without arrows; thumb brightens on hover; dragging scrolls |
| I12 | Change display scaling (or move a fence to a monitor with another DPI) | icons re-render sharp at the new DPI |

## J — Item actions (M3a+)
| ID | Steps | Expected |
|---|---|---|
| J1 | Right-click an item; Shift+right-click; select several and right-click one of them | Windows' classic item menu (Open, Open with ►, Send to ►, shell extensions, Properties); Shift adds extended verbs (Copy as path, …); the menu acts on all selected items |
| J2 | Use Open with ►, Send to ► Compressed folder, Properties | submenus draw and fill in; each command runs; what it opens comes to the front |
| J3 | F2 (or the menu's Rename) on a file, then on a shortcut; Enter / Esc | box selects the name without the extension; Enter renames through Windows (shortcut keeps .lnk), Esc cancels; the item keeps its fence and position |
| J4 | Rename to a name that already exists | Windows' own conflict message; nothing renamed or lost |
| J5 | Del, Shift+Del, and the menu's Delete on a test file | always the Recycle Bin (Windows' confirmation if enabled); the item leaves the fence; Explorer's Ctrl+Z (Undo) brings it back (to the Inbox) |
| J6 | Select several items, Del | all go to the Recycle Bin in one Windows operation |
| J7 | Double-click a shortcut whose target was deleted, and a file with no associated app | Windows' own message ("Missing Shortcut" / "How do you want to open this file?"); NeoFences keeps running |
| J8 | Save a fenced Word (or Notepad) document a few times | it stays in its fence and position |
| J9 | F2 / Del on Recycle Bin or This PC | nothing happens (special items are not renamed or recycled) |
| J10 | Del a file too big for the Recycle Bin (or on a drive without one) | Windows warns that it will be deleted permanently and asks first |

## K — Drag-drop (M3b+)
| ID | Steps | Expected |
|---|---|---|
| K1 | Drag an item within a fence onto the right half of another item, then onto the left half | lands after / before it; nothing on disk changes |
| K2 | Drag one item, then a multi-selection, from one fence to another | they land at the drop position, in the order they had; files stay on the Desktop |
| K3 | Drag a fence item onto a folder item, and onto the Recycle Bin item | Windows moves it into the folder / recycles it (its own dialogs); it leaves the fence |
| K4 | **[USER]** Drag a file from an Explorer window (another folder) onto a fence; repeat holding Ctrl | Windows moves (copies with Ctrl) it to the Desktop, with its own progress/Undo; it appears in that fence at the drop position, not the Inbox |
| K5 | **[USER]** Drag a file from Explorer showing the Desktop folder onto a fence | membership only (it is already on the Desktop): it moves into that fence |
| K6 | Drag a fence item to Explorer (another folder), to a browser upload box, to Discord | Windows' drag image; Explorer moves it (same drive) or copies; apps receive the file |
| K7 | Press on empty space in a fence and drag | a band selects every item it touches; Ctrl adds to the selection |
| K8 | Drag over a fence from another fence/app | Windows' drag image stays visible over the fence |
| K9 | Drag a file whose name already exists on the Desktop onto a fence from Explorer | Windows asks (replace/skip/both); a renamed copy lands in the Inbox |

## L — Portals and sorting (M4+)
| ID | Steps | Expected |
|---|---|---|
| L1 | Fence menu → New Portal fence… → pick a folder (e.g. Downloads); cancel once first | Windows' folder dialog; cancel adds nothing; a new fence titled like the folder shows its contents, newest first |
| L2 | Add, rename, delete files in that folder from Explorer | the Portal follows within ~½ s, keeping its sort |
| L3 | Double-click a subfolder; Back button / Backspace | the Portal shows the subfolder ("Downloads › Mods"), Back returns; never above the Portal's own folder |
| L4 | Ctrl+double-click a subfolder | opens it in Explorer; the Portal stays where it was |
| L5 | Sort by ► Name / Type / Date on a Portal; restart | order changes live, the choice is checked and survives restart |
| L6 | Sort by ► Name on a desktop fence, then drag an item | one-time sort; dragging still reorders freely |
| L7 | Drag a Portal item onto a desktop fence; drag it back; Ctrl-drag | Windows moves (Ctrl: copies) it; it lands in that fence / back in the folder |
| L8 | Item menu, F2, Del, Enter inside a Portal | work like on desktop items (Windows' menu, rename, Recycle Bin) |
| L9 | Rename or delete the Portal's folder in Explorer; restore it | "not available" message; items return when the folder does |
| L10 | Delete the Portal fence | the fence goes; the folder and its files are untouched |
| L11 | A Portal on a network share or USB stick; unplug it | message, no hang or crash; back when reconnected and re-listed |

## M — Start with Windows (ADR-019)
| ID | Steps | Expected |
|---|---|---|
| M1 | Start NeoFences; check `HKCU\…\Run\NeoFences` | the quoted exe path |
| M2 | Untick "Start with Windows" in a fence menu; tick again | the entry is removed / written again; the setting survives restart |
| M3 | **[USER]** With Takeover on, cut the power (or force a hard reset); sign in again | NeoFences starts by itself: fences shown, icons hidden as before |
| M4 | Disable NeoFences in Task Manager → Startup apps; sign out and in | NeoFences does not start (Windows' choice is respected) |

## N — Desktop gestures in the app (M5, ADR-020)
| ID | Steps | Expected |
|---|---|---|
| N1 | Right-click empty desktop | Explorer's desktop menu appears as before (replayed); Esc closes it |
| N2 | Right-drag on empty desktop, release | a rectangle follows the pointer; a "New fence" appears there with its title ready to type |
| N3 | Right-drag starting on an app window or a fence | nothing drawn; the app/fence gets its normal right-drag |
| N4 | Double-click empty desktop; again | every fence (and, with Takeover off, every icon) hides; the second double-click brings them back |
| N5 | Takeover off: double-click a native icon | it opens; nothing hides |
| N6 | Quick-hidden with Takeover off: kill NeoFences in Task Manager | the watchdog shows the icons again |
| N7 | App maximized; Ctrl+Alt+Space; Esc; Ctrl+Alt+Space; click the app; Ctrl+Alt+Space; open an item from a fence | fences over the app; back under it after Esc, after the click, and after the item opens |
| N8 | Set `peekHotkey` to a combination another app owns (or "Space"); restart | log says Peek is off; nothing else breaks |
| N9 | Double-click a fence title; hover it; move away; double-click again; restart while rolled up | rolls up to the title; opens after ~0.3 s; closes ~0.5 s after leaving; unrolls; stays rolled up after restart at its full height |
| N10 | Move a rolled-up fence, then unroll it | it unrolls at the new position with its full height |
| N11 | Lock a fence; double-click its title | it rolls up / unrolls too |
| N12 | "New fence" from the menu with several fences on screen | lands in the first free spot (8 px gaps), not on top of another fence |
| N13 | **[USER]** Normal use with the hook for a while, a fast mouse, a game | no lag; the right-click menu is never lost (D8) |
| N14 | **[USER]** Elevated app focused (Task Manager as admin); double-click desktop | record whether the gesture fires (D7) |

## O — Tray, Pause, game mode (M6a, ADR-021)
| ID | Steps | Expected |
|---|---|---|
| O1 | Look at the notification area (open the overflow ^ if needed) | the NeoFences icon (blue tile, three panels); tooltip "NeoFences" |
| O2 | Left-click it; right-click it | the same menu both times: New fence · Quick-hide · Peek (Ctrl+Alt+Space) · Pause NeoFences · Exit NeoFences; a click elsewhere closes it |
| O3 | Pause NeoFences; then Pause again | fences hide, native icons show, gestures stop, tooltip "NeoFences — paused"; New fence / Quick-hide / Peek grayed; the second click brings everything back |
| O4 | Tray → New fence; Quick-hide; Peek | as from the fence menu / gestures / hotkey |
| O5 | Start a full-screen game (borderless or exclusive), play a minute, alt-tab out | log `game mode: True` within ~5 s, `WH_MOUSE_LL removed`; tooltip "idle while a game runs"; after alt-tab `game mode: False` and the hook back |
| O6 | While in a game: Ctrl+Alt+Space; save a file to the Desktop; take a screenshot into a Portal folder | no Peek; the file and screenshot appear in their fences after leaving the game |
| O7 | Kill Explorer (Task Manager → Restart Windows Explorer) | the tray icon comes back; gestures still work (hook re-installed) |
| O8 | Win+L, sign back in; double-click the desktop | quick-hide works (hook re-installed on unlock) |
| O9 | Full-screen video in a browser (F11) | game mode on while full screen (harmless idle), off after Esc |
| O10 | Tray → Exit NeoFences | the tray icon disappears; icons come back as usual |

## P — Settings window and animations (M6b, ADR-022)
| ID | Steps | Expected |
|---|---|---|
| P1 | Tray → Settings…; fence menu → Settings…; both again | one Fluent window (follows light/dark + accent), brought to the front the second time |
| P2 | Switch Windows between light and dark with Settings open | the window follows; fences follow as before |
| P3 | Toggle Start with Windows / Hide desktop icons in Settings; look at a fence menu | applied at once; the fence menu checkmarks agree (and the other way round) |
| P4 | Click the Peek hotkey box; press Ctrl+Shift+F9; use it; set it back to Ctrl+Alt+Space | "Saved"; the new combination opens Peek; restart keeps it |
| P5 | Record a combination another app owns (or a plain letter) | an explanation in the window; the old hotkey keeps working |
| P6 | Fences → "When you click their title"; roll up a fence; rest on it; click its title; move away | resting does nothing; the click opens it; it closes ~0.5 s after leaving |
| P7 | Same with a locked fence | the click opens it too |
| P8 | Roll up / unroll; quick-hide on/off | the roll-up slides (~0.2 s); quick-hide fades (~0.15 s) |
| P9 | Windows Settings → Accessibility → Visual effects → Animation effects off; repeat P8 | no animation, everything happens at once |
| P10 | Game mode off in Settings while a game runs | NeoFences leaves idle at once (hook back); on again → idle again |
| P11 | About: Open logs folder / Open data folder | Explorer opens `%LOCALAPPDATA%\NeoFences\logs` / `%LOCALAPPDATA%\NeoFences` |
| P12 | Narrator (or another screen reader) on the settings checkboxes | toggling works and is applied |

## Q — Installer (M7, ADR-023)
| ID | Steps | Expected |
|---|---|---|
| Q1 | `build\pack.ps1 -Version 1.0.0` | tests pass; `artifacts\releases\NeoFences.App-win-Setup.exe` (~72 MB) |
| Q2 | Run Setup.exe (double-click; SmartScreen: More info → Run anyway) | installs without admin; NeoFences starts with your fences; Start menu "NeoFences"; Apps list entry |
| Q3 | Settings → About | NeoFences 1.0.0 |
| Q4 | Check `HKCU\…\Run\NeoFences` | the installed copy (`%LOCALAPPDATA%\NeoFences.App\current\NeoFences.exe`) |
| Q5 | With the installed copy set up: run a development build, exit it | the sign-in entry still points at the installed copy |
| Q6 | **[USER]** Restart Windows (or cut the power with Takeover on) | the installed NeoFences starts by itself; fences and hidden icons as before |
| Q7 | Exit NeoFences from the tray, then run a newer Setup.exe over the installed one (or run it while NeoFences runs: Velopack closes it) | updates and starts the new copy; fences unchanged; icons hidden again by Takeover |
| Q8 | Apps → NeoFences → Uninstall | NeoFences stops; desktop icons visible; Start menu entry and program folder gone; sign-in entry gone; `%LOCALAPPDATA%\NeoFences` (layout, backups, logs) kept |
| Q9 | Uninstall while NeoFences had crashed (icons hidden, nothing running) | icons visible after uninstall |
| Q10 | Reinstall after Q8 | the same fences come back (data kept) |
| Q11 | Upgrade with a newer Setup.exe (silent and double-clicked) | icons visible during Setup; NeoFences runs again ~4 s after it ends; one copy only (ADR-028) |

## R — v1.1 reliability (M8a, ADR-024)
| ID | Steps | Expected |
|---|---|---|
| R1 | Zoom into each fence corner (screenshot at 4–8×), dark and light wallpaper | no square of blur outside the rounded border; corners smooth |
| R2 | Resize, roll up / unroll, move a fence to another monitor | corners stay rounded at every size |
| R3 | Save a fenced document in Word / Excel while NeoFences restarts its watcher (or with the folder briefly locked) | the document stays in its fence |
| R4 | **[USER]** Move the Desktop folder (OneDrive → Back up Desktop, or Desktop → Properties → Location) | fences keep their items and order; nothing piles into the Inbox |
| R5 | Change the system clock by an hour, then safe-save a fenced file | it stays in its fence |
| R6 | Takeover off, icons hidden in Explorer (View → Show desktop icons off); double-click empty desktop twice | fences hide and come back; Explorer's icons stay hidden |
| R7 | Pause / resume quickly after a quick-hide | fences end visible after resume |
| R8 | **[USER]** Sign out with "Restart apps" (or a cancelled shutdown) | no second tray icon after NeoFences restarts |
| R9 | `build\pack.ps1 -Version 1.x`; then a version already in `artifacts\releases` | instant "not SemVer" / "already in releases" messages; the shell's folder is unchanged |
| R10 | Unpack a big archive (1000+ files) to the Desktop during a game | after the game, one reconcile; every file appears |

## S — v1.1 fence UI and icon-only labels (M8b, ADR-025)
| ID | Steps | Expected |
|---|---|---|
| S1 | Fence menu → Labels → On hover (icons only); point at icons, then leave | names hidden; the pointed-at icon's name pops up under it, over its neighbours; gone when the pointer leaves |
| S2 | Icons-only fence: icons on the bottom row and at the left/right edges | the name stays inside the fence (moves above the icon at the bottom, clamped at the sides) |
| S3 | Icons-only fence: click an icon, then use the arrow keys | the selected icon's name shows while nothing is pointed at |
| S4 | Settings → Labels for new fences = On hover; create a fence and a Portal; then "Apply to all fences" | new fences start icons-only; Apply switches every fence |
| S5 | Settings → Show shortcut arrows on/off; try every icon size | arrows appear on shortcuts only (not on files, folders, Recycle Bin), scaled with the icon |
| S6 | Click another app, then double-click a fence title once | the fence rolls up on the first double-click |
| S7 | Settings → hotkey box: record Ctrl+Shift+= and Alt+1 | the box shows "Ctrl+Shift+=" / "Alt+1" (no "OemPlus" / "D1"); tray menu shows the same |
| S8 | Settings → click the hotkey box, press the current Peek hotkey | it is recorded ("Peek: …"), fences do not peek; after leaving the box the hotkey peeks again |
| S9 | **[USER]** Let another app own the Peek hotkey (e.g. a game overlay), start NeoFences, open Settings | "… is not active" shown; recording the same combination after the other app closes saves and works |
| S10 | **[USER]** Narrator on: record a refused combination (Ctrl+C) | Narrator reads the refusal; on the box it reads the description |
| S11 | Resize a fence near another fence's edge, down to the smallest size | the snap never makes it smaller than the minimum |
| S12 | Rename a fence to 63 letters + an emoji | the emoji is either whole or gone, never half; the box stops at 64 characters |
| S13 | Change the icon size with an item selected and the list scrolled | selection and scroll stay; picking the current size does nothing |
| S14 | F2 on an item, double-click a word in the rename box | the word is selected; the file does not open |
| S15 | Ctrl+click a selected item; Ctrl+drag a selected item | the click unselects it; the drag moves/copies it and it stays selected |
| S16 | **[USER]** Roll up a fence, move it to a monitor with another scaling, unroll | it unrolls to its full height at the new scaling |
| S17 | F2 on a native desktop icon (Takeover off), double-click inside its rename box | a word is selected; no quick-hide |
| S18 | A fence with a big video and a slow network shortcut | other icons appear without waiting for them |

## T — v1.1 shell actions and drag-drop details (M8c, ADR-026)
| ID | Steps | Expected |
|---|---|---|
| T1 | Select two items, right-click the second → Rename | the rename box opens on the right-clicked item |
| T2 | Shift + right-click an item; then select it and press Shift+F10 | extended menu for the first (more entries where the item has extended verbs); the normal menu for Shift+F10 |
| T3 | Open an item menu, click empty desktop | the menu closes |
| T4 | Del on a big folder (or many items) | the fences stay responsive while Windows recycles |
| T5 | Select several items, delete one of them in Explorer, then Del in the fence | the others are recycled; the log names the missing one |
| T6 | Settings → Personalisation → Themes → Desktop icon settings: tick / untick Control Panel | it appears in the Inbox / leaves its fence within a second or two |
| T7 | **[USER]** Empty the Recycle Bin; then delete a file | the fence's Recycle Bin icon turns empty, then full |
| T8 | **[USER]** The same file name on your Desktop and the Public Desktop (needs admin): right-click the Public one → Properties | Properties shows the Public Desktop path |
| T9 | **[USER]** Drag files out of a .zip (Explorer) over a fence and drop | no pause on hover; the files arrive at the drop position |
| T10 | **[USER]** Alt-drag a file from Explorer onto a fence | the new "… - Shortcut" lands where it was dropped |
| T11 | Drop between items in a fence where one label wraps to two lines | the caret goes to the row under the pointer, not the next row |
| T12 | **[USER]** Disconnect the drive of a redirected Desktop for a minute | one re-arm every few seconds at most, growing to a minute; reconciles when it is back |
| T13 | **[USER]** Next upgrade with Setup --silent | NeoFences runs the new version afterwards (or the log says a second instance gave up) |
| T14 | Triple-click a fence title right after clicking another app | it rolls up once (no roll back down) |
| T15 | Rename in an icons-only fence | the rename box is a normal labelled width |

## U — v1.1 Portal details (M8d, ADR-027)
| ID | Steps | Expected |
|---|---|---|
| U1 | A Portal of a folder on a USB stick; tray → Safely Remove Hardware → the stick | "safe to remove"; the Portal shows "not available"; NeoFences keeps running |
| U2 | **[USER]** Plug the stick back in | the Portal shows the folder again within ~7 s |
| U3 | A Portal on the stick; Safely Remove while a file on it is open in another app | Windows names that app; the Portal comes back within ~7 s |
| U4 | In a Portal, select a file; copy a new file into the folder | the selection and scroll stay |
| U5 | In a Portal, select two folders, press Enter | both open in Explorer; the Portal stays at its folder |
| U6 | One folder selected, Enter | the Portal browses into it (as before) |
| U7 | **[USER]** A Portal of a network folder; disconnect the network for a minute | "not available", re-tried every ~7 s; a watcher that stops while the folder stays readable re-lists with growing waits (log "re-listing after"); comes back after |
| U8 | Drag files out of a .zip onto a fence | no pause while hovering or dropping; the files arrive |
| U9 | Rename or recycle from a fence, then delete that fence at once | no crash; Windows' dialogs (if any) still appear |
| U10 | During a game, empty the Recycle Bin; leave the game | the Recycle Bin icon updates after the game |

## V — Fence tabs (M9, ADR-029)
| ID | Steps | Expected |
|---|---|---|
| V1 | Drag a fence by its title onto another fence's title | the target lights up while hovering; on release one box with two tabs; the moved fence's tab is shown |
| V2 | Click each header; Ctrl+Tab / Ctrl+Shift+Tab with the box focused | the tab's items, icon size, labels and sort show |
| V3 | Double-click a header | rename that tab in place |
| V4 | Right-click a header → Colour → a colour; then None | a coloured bar under that header; gone again |
| V5 | Drag a header along the strip | the tabs reorder |
| V6 | Drag a header onto another box's or fence's title | it moves into that box at that spot |
| V7 | Drag a header out onto the desktop; also right-click → Detach tab | a separate fence at the drop point (menu: just below the box) |
| V8 | Detach the first (host) tab | the box stays where it was with the other tabs, still rolled up / locked as before |
| V9 | Drag items from Explorer or another fence onto a header | that tab shows and the items land in it (a Portal tab copies/moves into its folder) |
| V10 | Restart NeoFences (and **[USER]** a power cut) | boxes, tab order, the shown tab and colours come back |
| V11 | Roll up, lock, Peek, quick-hide, game mode with a box | as for a single fence |
| V12 | Delete a tab (fence menu → Delete fence) | its items go to the Inbox; the box keeps the other tabs |
| V13 | A Portal tab and a desktop tab in one box; switch while files change in the Portal's folder | the Portal shows its current files when switched to |
| V14 | Resize a fence and release with the pointer over another fence's title | no merge |

## W — Snapshots (M10, ADR-030)
| ID | Steps | Expected |
|---|---|---|
| W1 | Tray → Take snapshot | a notice "Snapshot saved"; it appears in Settings → Snapshots (name = date and time) |
| W2 | Move fences, merge two into tabs, recolour a tab; then Settings → select the snapshot → Restore | fences, places, tabs, colours and icons come back as saved |
| W3 | After W1, create a file on the Desktop, then restore | the new file stays where it is (its fence, or the Inbox) |
| W4 | After W1, delete a fenced file (Recycle Bin), then restore | no ghost item; everything else as saved |
| W5 | Tray → Restore snapshot → Undo the last restore | the state from just before the restore is back |
| W6 | Settings → Rename a snapshot (Enter / Esc) | the new name shows in Settings and the tray menu; Esc keeps the old one |
| W7 | Settings → Delete a snapshot | its file is in the Recycle Bin; the list updates |
| W8 | Put a damaged `.json` in the snapshots folder; open Settings | it is not listed; the log names it; the others work |
| W9 | **[USER]** Restore with a OneDrive Desktop offline | "not restored" notice; nothing changes |
| W10 | Restore on a second monitor setup the snapshot never saw | fences of the snapshot get placed there; setups it saw get their saved places |
| W11 | Settings (keyboard / Narrator): list rows read "name, date"; buttons reachable with Tab | usable without the mouse |
| W12 | After W1, rename a fence and change its icon size, then restore | the window shows the saved title and icon size at once (no restart) |
| W13 | Settings → rename "Before restore", then restore another snapshot | the renamed one stays in the list; a new "Before restore" appears |

## X — Rules auto-sort (M11, ADR-031)
| ID | Steps | Expected |
|---|---|---|
| X1 | Settings → Rules → Add: Type of file / Images → Put in a fence → Add rule | the row reads "Images → <fence>"; it is saved in `config.json` |
| X2 | With X1, save a .png to the Desktop | it lands in that fence, not the Inbox |
| X3 | Rules "Game shortcut / Any launcher" and "Name *invoice*"; create a Steam `.url` and "invoice-1.pdf" | each lands in its rule's fence; a .txt lands in the Inbox |
| X4 | A Game rule "In a folder of mine…" = D:\GameLibrary; make a shortcut to a game there | it lands in that rule's fence |
| X5 | Untick a rule's box, save a matching file | it stays in the Inbox; ticking it again does not move it |
| X6 | Apply rules now with a hand-placed matching item elsewhere | "N items moved"; the item is in the rule's fence |
| X7 | Tray → Restore snapshot → Undo the last restore (after X6) | every moved item is back where it was |
| X8 | Fence menu → Rules for this fence… | Settings opens at Rules, the editor ready with that fence under "Put in" |
| X9 | Delete the fence a rule names | the rule shows "(fence missing)", greyed; matching items go to the Inbox |
| X10 | Move a rule up / down; two rules match one item | the upper rule decides |
| X11 | Game mode: save a matching file while a full-screen game runs, then leave the game | it is filed after the game |
| X12 | Editor: Name with an empty pattern, Age with "abc" days | a red hint says what is missing; nothing is saved |
| X13 | Hand-edit `config.json`: a rule with an unknown kind or a negative number; start | NeoFences starts; the rule is listed, unticked |
| X14 | Settings (keyboard / Narrator): rows read "<rule>" or "<rule>, off"; editor fields are named | usable without the mouse |
| X15 | Download an image with Chrome or Edge straight to the Desktop (with X1) | it lands in the rule's fence once the download finishes |
| X16 | A matching item sits in the Inbox by hand; toggle its Read-only box in Properties | it stays in the Inbox |
| X17 | An Age rule with 99999999 days | NeoFences keeps running; new items are filed as usual |

## Y — Game Library (M12, ADR-032)
| ID | Steps | Expected |
|---|---|---|
| Y1 | Tray → New Game Library fence | a "Games" fence with every installed game, A–Z; Steam games as posters, others as icons on dark tiles |
| Y2 | Tray → New Game Library fence again | no second fence; the library's tab shows if it is a tab |
| Y3 | Double-click a Steam, an Epic and a `D:\GameLibrary` game | each starts (launcher games through their launcher) |
| Y4 | Right-click a game | the shell menu without Rename, plus "Hide from library" and "Open install folder" |
| Y5 | Hide from library (or Delete) | the game leaves the fence; Settings → Game Library lists it; "Show again" brings it back |
| Y6 | Hide a `D:\GameLibrary` game that also has a Desktop shortcut, then delete the shortcut | the game stays hidden |
| Y7 | Install or uninstall a Steam game (or add / remove a folder in a game folder) | the library follows within seconds, no restart |
| Y8 | Unplug the drive of a game folder (or a Steam library), Refresh library | its games stay; the status line names the unreadable source |
| Y9 | Drag a game onto the Desktop | a copy of its shortcut lands there; the game stays in the library |
| Y10 | Drag a file onto the library fence | nothing is dropped (no cursor, no file) |
| Y11 | Settings → untick Steam | Steam games leave the library; ticking brings them back |
| Y12 | Game mode: install a game while a full-screen game runs, then leave the game | the library updates after the game |
| Y13 | Merge the library into a box with another fence (tabs), switch tabs | both tabs show correctly; the library keeps its tiles |
| Y14 | Delete the library fence | only the fence goes; games and launcher files are untouched |
| Y15 | Keyboard / Narrator: tiles read by game name; the Settings card's lists and checkboxes are named | usable without the mouse |
| Y16 | A game folder (or Steam library) on a USB drive: Safely remove it while NeoFences runs | Windows lets it go; its games stay listed; plugging it back rescans |
| Y17 | Uninstall a game by deleting its folder but leave Epic's manifest / GOG's registry entry | the game leaves the library (no broken shortcut) |

## Z — v1.6 carry-overs (M13, ADR-033)
| ID | Steps | Expected |
|---|---|---|
| Z1 | Start v1.6 once, then look at `config.json` | `"schemaVersion": 2`; tabs, rules and library settings unchanged |
| Z2 | Start an older NeoFences (≤ 1.5) on that config | it shows a fresh, read-only state and never saves over the file |
| Z3 | Put a snapshot with `"schemaVersion": 3` (or a 20 MB file) in `snapshots\` | not listed, logged once; the others work |
| Z4 | Start NeoFences twice quickly, then run `NeoFences.exe --exit` | both copies end; a later start runs normally |
| Z5 | A Portal on a USB stick whose folder was deleted (the stick still in): Safely remove | Windows lets the stick go |
| Z6 | Safely remove a stick with a Portal while files on it are changing | Windows lets it go (no veto from a refresh queued just before) |
| Z7 | Tray → New Game Library fence while quick-hide is on | quick-hide ends; the library fence shows |
| Z8 | Two rules with the same id pasted in `config.json`; start; delete one in Settings | only that one goes |
| Z9 | Game Library with an Epic DLC or Unreal Engine installed | neither shows as a game |
| Z10 | A Game Pass game (or another Xbox game with a resource name) | its real name, not "Microsoft.…" |
| Z11 | A Desktop shortcut into a Steam game's own folder (`…\steamapps\common\…\game.exe`) | the game shows once |
| Z12 | A Desktop game shortcut with a custom icon or "Run as administrator" | the library's copy keeps both |
| Z13 | Hide a `D:\GameLibrary` game, unplug D: (or rename the folder), open Settings | the hidden game is listed once, by name; Show again brings it back when D: returns |
| Z14 | A game writing files at its own root inside `D:\GameLibrary` while it runs | no library rescans while it plays |
| Z15 | Drag a file over a box whose front tab is the library, onto another tab's header | that tab shows and takes the drop; no drag image left behind on a refused drop |
| Z16 | Settings → Rules: open the editor, then try Delete / Move / Add, or fence menu → Rules for this fence… | the list stays as it is; a hint says to finish or cancel first |
| Z17 | Tick a rule's box with the keyboard (Space) | the focus stays on that box |
| Z18 | After a power cut during a library update | no stray `.…lnk` files in `library\`; the index loads |
| Z19 | Right-click a single fence's title (not a tab header) | the fence menu, not Windows' Move / Size / Close |
| Z20 | A rolled-up box with tabs, roll-up set to "click": click a tab header | that tab shows and the box opens |
| Z21 | Tray → Restore snapshot, with snapshot names hand-edited to contain a tab, a line break, 300 characters, or nothing | one line each; a blank name shows "Snapshot <date time>"; the submenu never starts with a separator |
| Z22 | Tray → Restore snapshot → More in Settings… | Settings opens with the Snapshots card in view |
| Z23 | Settings → Take snapshot; then again with the `snapshots` folder read-only | "Saved …" in the card; on failure a red line in the card and a warning (yellow) notice |
| Z24 | Settings → Delete a snapshot with "Display delete confirmation" turned on for the Recycle Bin | the confirmation sits on Settings |
| Z25 | Settings → Rename a snapshot with the keyboard (Enter or Esc); double-click the empty space below the rows | the focus goes back to that row; the empty-space double-click restores nothing |
| Z26 | Peek hotkey Ctrl+Shift and the "+" key on a German layout (OemPlus) | Settings and the tray show "Ctrl+Shift++", not "Ctrl+Shift+=" |
| Z27 | Game Library on: create, rename and delete a game shortcut on the Desktop | the library follows within about 10 s without Refresh library |
| Z28 | Settings → Rules: focus a row (or open the editor's fence list), then let a library scan finish or toggle game mode | the focus and the open list stay as they are |
| Z29 | Rules editor open on a rule; restore a snapshot without that rule; Save | the edit is kept as a new rule |
| Z30 | A Desktop `.url` of several MB without line breaks | NeoFences reads only its first 64 KB; no slowdown |

## AA — Appearance (M14, ADR-036)
| ID | Steps | Expected |
|---|---|---|
| AA1 | Update from v1.6.2 without touching Settings | every fence looks exactly as before (dark: clear; light: the same veil) |
| AA2 | Settings → Appearance → Background strength, drag from Clear to Solid (dark mode) | the fences darken live; at Solid the wallpaper barely shows |
| AA3 | Switch Windows to light mode, move the slider, switch back to dark | each mode keeps its own strength |
| AA4 | Fence menu → Colour → Red, then each colour style in Settings | Accent edge: red outline, title and bar; Tinted glass: red glass; Title strip: red title bar |
| AA5 | Fence menu → Colour → Custom…, pick a colour; then Cancel once | the fence takes the picked colour; Cancel changes nothing |
| AA6 | Title strip with Yellow, then Blue | the title reads dark on yellow, white on blue |
| AA7 | Settings → Title font: Bahnschrift, Huge, Bold | every fence title changes; the title bar grows and nothing is clipped |
| AA8 | (removed in v1.7.1: one title font for all fences, ADR-038) | — |
| AA9 | A box with tabs of different colours: switch tabs | the box takes the front tab's colour (one title font for all fences since v1.7.1); headers keep their marks |
| AA10 | Settings → "Colour fences from the wallpaper" on (Wallpaper Engine running) | uncoloured fences take the wallpaper's colour; Settings names the WE wallpaper |
| AA11 | Change the Wallpaper Engine wallpaper | within a few seconds the accent follows |
| AA12 | Close Wallpaper Engine, change the Windows wallpaper | the accent follows the Windows wallpaper |
| AA13 | Two monitors with different wallpapers | each fence takes its own monitor's colour; moving a fence across changes it |
| AA14 | A greyscale wallpaper | the fences use Windows' accent colour |
| AA15 | Uninstall a font that a fence uses | the fence falls back to Segoe UI; reinstalling brings the font back |
| AA16 | Rolled-up fence with a Huge title font | the rolled-up bar shows the whole title |
| AA17 | A game in full screen with the accent on | no CPU use from NeoFences while it runs (no timer) |
| AA18 | Open v1.7's config with v1.6 (copy) | v1.6 opens it read-only; nothing is lost |
| AA19 | Fence menu → Colour → Custom…, then Cancel; open the menu again | the fence's own choice is ticked (not Custom…) |
| AA20 | Settings → Appearance → Title font set to a font, then uninstall that font, open Settings | the box shows the font's name; fences use Segoe UI |
| AA21 | Accent edge with a very pale custom colour in light mode, and a very dark one in dark mode | the title stays readable in both |
| AA22 | Rename a snapshot to 60 letters plus an emoji; open Tray → Restore snapshot | the label is cut before the emoji, never a broken glyph |
| AA23 | Switch the keyboard layout (Win+Space) while Settings is open | the Peek hotkey box shows the new layout's characters |
| AA24 | During a full-screen game, unpack 500+ files plus a game shortcut onto the Desktop; leave the game | the game appears in the library after a few seconds |

## AC — Public releases and auto-update (M17, ADR-039)
| ID | Steps | Expected |
|---|---|---|
| AC1 | A friend's PC: download Setup from the Releases page, run it | one SmartScreen "More info → Run anyway"; NeoFences installs for the user and starts |
| AC2 | A new release is published; wait (or Settings → Updates → Check now) | a notice "NeoFences update ready — Version x.y.z is ready…"; the tray's first item "Restart to update to vx.y.z" |
| AC3 | Click "Restart to update" | icons come back, NeoFences closes, the new version starts within seconds; fences and settings as before |
| AC4 | Ignore the notice, then tray → Exit; start NeoFences again | the new version runs |
| AC5 | Sign out / shut down while an update waits; sign in again | sign-out is as fast as before; NeoFences starts as the old version and offers the update again; it installs on the next normal exit |
| AC6 | Settings → Updates → switch off; restart; watch the network (Resource Monitor) | no connection to github.com from NeoFences |
| AC7 | Offline: Check now | "Couldn't check for updates: offline…"; it tries again later; nothing else changes |
| AC8 | A full-screen game for over a day of uptime | no check and no notice while the game is in front |
| AC9 | A developer build (dotnet run) | Settings → Updates says updates are off in a developer build |
| AC10 | Push a version tag | GitHub Actions builds, tests and uploads a draft release with notes; installed copies see nothing until it is published |

## AD — Virtual items (M18, ADR-040, ADR-041)

Test data only in `%USERPROFILE%\Desktop\NeoFences-test\` (created and recycled by the test) and on the pendrive
`G:\NeoFences-test\` (LOCO_DUCK). "Byte for byte" = `cmp` of the file against a copy made before.

| ID | Steps | Expected |
|---|---|---|
| AD1 | First start with no data folder | one empty fence "Fence" with the hint "Drop files, folders or links here — or right-click → Add item…"; native desktop icons visible |
| AD2 | Drag `a.txt` from Explorer (`Desktop\NeoFences-test`) onto the fence | cursor shows the link arrow; an item appears at the drop point; `a.txt` is still in its folder, byte for byte; no Windows dialog |
| AD3 | Drag the Desktop's own `a.txt` (from the desktop) onto the fence | as AD2 (bug K5 gone); the icon stays on the desktop |
| AD4 | Drop `a.txt` onto the same fence again | not added again; the existing item is selected |
| AD5 | Drag a browser link (address-bar icon) onto the fence | a website item named after the host, with the default browser's icon; double-click opens the browser |
| AD6 | Fence menu → Add item… → Browse ▾ → A folder… → OK | a folder item; Arguments and Run as administrator greyed in Properties |
| AD7 | Drag an item to another fence; Ctrl+drag it back | moved (name and icon kept); Ctrl: a second item, the first stays |
| AD8 | Drag an item out onto the desktop or into an Explorer folder | Explorer makes a copy; the original stays where it was |
| AD9 | Select an item, Del; select three, Del | one: gone at once; three: one confirmation; the files are untouched |
| AD10 | F2 on an item; type a name; OK | the label shows the new name; the file keeps its name |
| AD11 | Properties → Change icon → From a file… → shell32.dll, any icon | the item shows that icon; Reset brings the target's icon back |
| AD12 | Properties → Change icon → From a picture… (a PNG) | the item shows the picture; `%LOCALAPPDATA%\NeoFences\icons\` holds a copy ≤ 256 px |
| AD13 | Properties of an .exe item: Arguments `-x`, Run as administrator on; open it | the UAC prompt appears; Cancel: nothing breaks (logged) |
| AD14 | Rename `a.txt` to `b.txt` in Explorer | within 2 s every item pointing at it shows `b` (own names kept) |
| AD15 | Delete `b.txt` in Explorer (Recycle Bin) | the item dims with the ! badge, tooltip "Missing: …"; restore it from the Recycle Bin: back to normal by itself |
| AD16 | Item on `G:\NeoFences-test\` → Safely remove the pendrive | Windows allows the removal; the item dims, tooltip "Drive G: is not connected"; plug it back: normal again within seconds |
| AD17 | Double-click a missing item | NeoFences' "is missing" window (Locate… / Remove from fence / Cancel), no Windows error; Locate… opens at the nearest folder that exists |
| AD18 | Shift+right-click an item | Windows' menu with a first grey line "Windows menu — acts on the real file" |
| AD19 | Right-click an item | Open · Run as administrator · Open file location · Copy path · Properties… · Remove from fence · the grey Shift hint |
| AD20 | Fence menu → Refresh | names and icons reload; states re-checked |
| AD21 | Settings → "Hide desktop icons while NeoFences runs" on; Task Manager → End task on NeoFences | icons hidden while running; back within ~5 s after the kill (watchdog) |
| AD22 | Take a snapshot; remove an item and rename another; restore the snapshot | both items back as they were; "Undo the last restore" works |
| AD23 | Delete a fence with items | asked first (count shown); the fence and its items go; files untouched |
| AD24 | Game Library fence | still lists games as tiles; dragging a game into a fence makes an item of its shortcut |
| AD25 | Kill NeoFences (Task Manager) right after adding an item; restart | the item is there (or, at worst, the fence is as before the add); no damaged files |
| AD26 | Add item… with the target `\\neofences-nohost\share\x.txt`, then Refresh the fence and drag another fence meanwhile | the item turns Unavailable within a few seconds; the fences never freeze (checks run off the UI thread, 2 s timeout) |

Sections C (Takeover parts), H, J, K, L, U and X describe the pre-pivot model (Takeover, membership, item file
actions, Portals, Rules): parked with ADR-040, not run for 0.9.

Added after the M18 final review:

| ID | Steps | Expected |
|---|---|---|
| AD27 | An item for a .docx (or any file an editor saves by renaming): open it, change it, save | the item stays OK and keeps pointing at the .docx (not at a temp file) |
| AD28 | Pull the pendrive without Safely remove; plug it back; rename `G:\NeoFences-test\g.txt` | Unavailable, then OK; the rename is followed (the watcher was re-armed) |
| AD29 | Exit NeoFences, unplug the pendrive, start NeoFences, plug the pendrive in | the G: item shows Unavailable, then OK with its real icon and name (not the placeholder) |
| AD30 | Sort by → Name on a fence holding the `\neofences-nohost\share\x.txt` item | the fences stay responsive; the sort finishes a few seconds later |
| AD31 | Drag a fence item onto the desktop's Recycle Bin | nothing is deleted (drag-out offers copy or link only; the Recycle Bin takes moves) |
| AD32 | Give an item a picture icon, take a snapshot, Remove the item, restart NeoFences, restore the snapshot | the item comes back with its picture |

## AE — Store apps, bulk fix, Add from desktop, reliability (M19, ADR-042)

Run with the test build and NeoFences' own data backed up (the installed copy stopped; its data restored afterwards).
Test files only in `%USERPROFILE%\NeoFences-m19-test\` and the pendrive's `G:\NeoFences-test\`.

| ID | Steps | Expected |
|---|---|---|
| AE1 | Add item… → Browse ▾ → An app… | the list opens at once ("Loading apps…"), then every Start app A–Z with icons (~0.3 s); typing filters it |
| AE2 | Pick Sticky Notes (OK or double-click) | target `shell:AppsFolder\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe!App`, the empty name box takes "Sticky Notes"; Arguments and Run as administrator greyed; status "● Found (an app)." |
| AE3 | Add it; double-click the item | Sticky Notes opens; the item shows the app's icon and name |
| AE4 | Drag an app from Start → All onto a fence (by hand) | a `shell:AppsFolder\…` item at the drop point; no "drop failed" warning in the log |
| AE5 | Add the typed target `shell:AppsFolder\NotAnApp.Bogus_123!App` | status "● Not installed: …"; the item is Missing (dimmed, badge, a generic icon, the label "NotAnApp.Bogus") |
| AE6 | Open that item → Locate… | the app list opens (not a file dialog); picking an app re-points the item |
| AE7 | Items for `…\NeoFences-m19-test\old\a.txt`, `b.txt`, `c.txt`; exit NeoFences; rename `old` to `new`; start; open a → Locate… → `new\a.txt` | "Fix 2 more items? They were in …\old and are now in …\new." (b, c); Fix: b and c point into `new`, a log line "2 item(s) fixed", `snapshots\before-restore.json` written |
| AE8 | Tray → Restore snapshot → "Undo the last restore or fix" | b and c point into `old` again (a stays: the undo reverts the fix, not the Locate) |
| AE9 | AE7 with `d.txt` deleted from `new` before the Locate | d is not offered; only the items whose files are in `new` |
| AE10 | Locate… a missing item to a file with another name | no "Fix N more" question |
| AE11 | Tray → Add from desktop… | the dialog opens at once ("Reading your desktop…"), then groups Games / Apps / Folders and files / Web links with counts, every row ticked, "Put in: New fence: …" (or a fence of that title) |
| AE12 | Add | new fences in free space, every ticked entry an item pointing at the desktop entry itself; nothing on the desktop changed; one log line with the counts |
| AE13 | Open Add from desktop… again | every row unticked with "already in <fence>"; Add greyed |
| AE14 | Settings → Game Library → add `D:\GameLibrary`; Add from desktop… | shortcuts into `D:\GameLibrary` are listed under Games |
| AE15 | Tick "Hide desktop icons while NeoFences runs" and Add | the icons hide (HideIcons 1, marker); the Settings switch shows on |
| AE16 | Shift+right-click the `\\neofences-nohost\share\x.txt` item | after ≤ 2 s a one-line menu "Network location not reachable"; the fence never freezes |
| AE17 | Properties of that item → Browse ▾ → A file or program…; → Change icon → From a file… | each dialog opens within ~2 s at Windows' default place; log "did not answer" |
| AE18 | Drag that item (with a reachable file item) out to Explorer | only the reachable file is copied; log "left out of the drag" |
| AE19 | Properties: type a new path and press Enter at once | OK stays greyed until the check finishes ("Checking…"), then the saved Arguments state matches the new target |
| AE20 | Restart NeoFences with the share item in a fence | the share item shows a generic icon within ~2 s; the other items' icons load normally (no stuck loader) |
| AE21 | Pendrive: add an item on G:, then pull the stick right after NeoFences starts (watchers still arming) | no veto, no error; re-plug: the rename of `g.txt` is followed |
| AE22 | Remove many items and a fence, then keep using NeoFences for a while | no growth of per-target records (log has no errors; behaviour unchanged) — covered by Core tests for the clean-up |

Added after the M19 final review:

| ID | Steps | Expected |
|---|---|---|
| AE23 | A fence with 10 items on `\\neofences-nohost\share\` (x1.txt … x10.txt); restart NeoFences | the other fences' icons appear within ~3 s (the dead share costs one 2 s wait, not 2 s per item) |
| AE24 | An app item for a program whose app id holds spaces (Epic Games Launcher from the app list); double-click | the program opens |
| AE25 | If a mapped network drive is at hand: an item on it, the drive disconnected; restart NeoFences | the item shows Unavailable with a generic icon within ~2 s; other icons are not held up |
| AE26 | An app item with a passing failure (reading-based: `AppList.Exists` treats only not-found as uninstalled) | an app is never shown Missing because Windows was not ready at sign-in |

## AF — 0.10.1 small fixes (M20)

| ID | Steps | Expected |
|---|---|---|
| AF1 | Shift+right-click a Store app item (Sticky Notes) and a program from the app list (7-Zip File Manager) | Windows' menu for the app, under its header line (no silent failure) |
| AF2 | Typed target `shell:AppsFolder\{6D809377-6AF0-444B-8957-A3773F02200E}\Fake\Fake App.exe` | Missing, labelled "Fake App" (not the whole id) |
| AF3 | A test shortcut to This PC and one to Control Panel on the desktop; Add from desktop… | both listed under Folders and files; removed afterwards |
| AF4 | Add from desktop… twice with a Game Library fence present | the second opening lists at once (the library's last scan reused); log has no scan errors |
| AF5 | Bulk fix: Fix after every proposed item was changed by hand meanwhile | log "nothing fixed"; `before-restore.json` unchanged |

## AG — 0.11.0 folder views (M21)

| ID | Steps | Expected |
|---|---|---|
| AG1 | Tray → New folder view… → Cancel in the folder dialog; again → pick a folder → Cancel in the settings | nothing created either time |
| AG2 | Tray → New folder view… → Downloads | settings prefilled: Date (newest first), only the newest 30; OK → a fence titled "Downloads" with the newest 30 entries, newest first |
| AG3 | Fence menu → New folder view… → `D:\GameLibrary`, Show: Folders only | only the game folders, A–Z; double-click one → Explorer opens it |
| AG4 | Folder view settings… → Types `*.png;*.jpg`; then `a|b` | only those files (and subfolders when showing all); `a|b` marks the hint red and disables OK |
| AG5 | In Explorer: add, rename and delete a file in a viewed folder | the view follows within a second each time; selection and scroll stay |
| AG6 | Rename the viewed folder itself in Explorer | the view follows; its title follows while it was the folder's name |
| AG7 | Delete the viewed folder (to the Recycle Bin), then restore it | "Folder not available: <path>", then the entries again within ~7 s |
| AG8 | A view of `G:\NeoFences-test` (pendrive): pull the stick; plug it back | "Folder not available", then back by itself |
| AG9 | With that view shown: Safely Remove the stick | Windows allows it; the view says "not available"; back after replugging |
| AG10 | A game in front (game mode), change the viewed folder, leave the game | no change during the game; one re-list afterwards |
| AG11 | Drag an entry from a view to Explorer; drag one onto an items fence; drop a file onto the view | a copy in Explorer (the original stays); a new item in the fence; the drop is refused |
| AG12 | Right-click an entry → Add to fence ▸ <fence>; Copy path; Open file location | the item is added and selected there; the path is on the clipboard; Explorer shows the entry |
| AG13 | Shift+right-click an entry | Windows' menu under "Windows menu — acts on the real files" |
| AG14 | Del, F2, Alt+Enter on a selected entry | nothing happens; the file is untouched |
| AG15 | A folder item in an items fence → right-click → Show as folder view | a view of that folder beside the fence |
| AG16 | A view of a folder with 600 files (`G:\NeoFences-test\many`) | 500 entries, then "+ 100 more — Open folder"; clicking it opens Explorer |
| AG17 | Sort by → Type on a view; restart NeoFences | the sort is checked in the menu and kept after the restart |
| AG18 | Merge a view into another fence's box as a tab; switch tabs | the view lists as a tab; switching back shows its entries at once |
| AG19 | Take snapshot; delete the view fence; restore the snapshot | the folder is untouched by the delete; the restore brings the view back, listing |
| AG20 | A view of the pendrive's root `G:\` (New folder view… → the drive) | the stick's own root is listed (not some folder on G: NeoFences was started in); title "G:" |
| AG21 | A view of an unreachable share (`\\nosuchhost\share`) → Folder view settings… → Browse… | the window greys for at most 2 s, then the folder dialog opens at Windows' default place; no fence freezes |
| AG22 | Leave a view's stick unplugged for a minute; read the log | one "cannot watch" warning for the outage, not one every 7 s |

## AH — 0.12.0 games as items (M22)

| ID | Steps | Expected |
|---|---|---|
| AH1 | Start 0.12.0 with a Game Library fence | the fence is now a normal fence with the same games in the same order, as cover tiles; tray → Restore snapshot lists "Before games became items (…)"; Settings → Game Library → New games go to: that fence |
| AH2 | Tray and fence menus | no "New Game Library fence"; fence menu of an items fence has "Add games…" |
| AH3 | Ctrl+drag a game tile into another fence (Apps) | a copy there, shown as a cover tile; its row grows, other cells keep their size |
| AH4 | Right-click it → Show as → Icon; then back to Cover tile | an icon cell like the apps around it; then the tile again |
| AH5 | Right-click a game item | Open · Show as · Open install folder · Copy path · Properties… · Remove from fence; Open install folder opens the game's folder |
| AH6 | Properties → a name of your own | the name shows under the cover; the target box is NeoFences' shortcut |
| AH7 | Fence menu → Add games… in Apps | every game listed with its source; games already in Apps unticked and marked "already here"; Add puts the ticked ones at the end |
| AH8 | A new game: a new sub-folder with a .exe in `D:\GameLibrary` (test), wait for the scan | a new item at the end of the new-games fence; nowhere else |
| AH9 | Remove that test folder; wait for the scan | its items show "Not installed" (dimmed, ⚠); opening asks "… is not installed" with Remove from fence / Cancel (no Locate…) |
| AH10 | Put the test folder back | the items are back by themselves |
| AH11 | Settings → New games go to: Nowhere; add another test game | no item added anywhere |
| AH12 | Restore the "Before games became items" snapshot | the library fence comes back and becomes game items again (one more snapshot first) |
| AH13 | Delete the new-games fence | Settings → New games go to: Nowhere |
| AH14 | A not-installed game shown as a cover tile, with labels always and with labels on hover | the tile is dimmed with the ⚠ badge in both modes |
| AH15 | Properties of a game item | the target box is read-only and Browse is off |
| AH16 | Open a not-installed game; press Enter in the question | nothing happens (no default button); the item stays |
| AH17 | Shift+right-click a game item → Delete | only the item leaves the fence; NeoFences' shortcut for the game stays |
| AH18 | Move a game from Games to Apps; make it come back as new (remove its test folder, scan, put it back, scan) | it stays in Apps only; no copy appears in Games |

## AI — 0.12.1 small fixes (M23)

| ID | Steps | Expected |
|---|---|---|
| AI1 | Folder view settings: Folder `Downloads`; then `%USERPROFILE%\Downloads`; Only the newest `0` | a red line "Type a full folder path…" and OK greyed; then OK on; then "…a number from 1 to 500" |
| AI2 | A view of a folder with 20,000 files, sorted by name, while a file in it is rewritten every 100 ms | every fence keeps answering (no UI-thread stall over ~100 ms) |
| AI3 | A view as a hidden tab of a box; rename its folder in Explorer | the tab header shows the new name without switching to it |
| AI4 | Settings open; delete a fence | "New games go to" no longer lists it |
| AI5 | Settings → "New games go to" dropdown open while a scan finishes | the dropdown stays open |
| AI6 | Right-click a game that is not installed | "Open install folder" greyed out |

## AJ — 0.13.0 element sizes and the fence grid (M24)

| ID | Steps | Expected |
|---|---|---|
| AJ1 | Right-click Steam in Apps → Size ▸, hover the squares, click 2 × 2 | the caption follows the hover; Steam becomes a big icon on 2 × 2 cells; smaller apps fill the gaps beside it |
| AJ2 | Select three apps → Size ▸ 2 × 1; then Default size | all three wide; then back to 1 × 1 |
| AJ3 | Games fence | covers on 1 × 2 cells; a cover set to 2 × 2 is a bigger poster |
| AJ4 | Apps → Layout ▸ Free (fixed positions) | nothing moves; Layout ▸ shows Free checked |
| AJ5 | Free: drag an icon onto an empty cell; onto a taken one; drag three at once | lands on the cell; the nearest free spot; the three keep their arrangement |
| AJ6 | Free: drop a file from Explorer onto an empty cell | the new item on that cell; the dashed cell marker shows during the drag |
| AJ7 | Free: Sort by → Name | packed from the top-left in name order |
| AJ8 | Free: make the fence narrower than an element's cell, then wider again | it shows in a free spot meanwhile; back at its cell afterwards |
| AJ9 | Arrow keys in Apps | selection moves to the nearest element in that direction |
| AJ10 | Labels on hover; icon size 32 and 96 | cells shrink and grow; big icons reload sharp |
| AJ11 | A 500-entry folder view | lays out without a visible pause |
| AJ12 | Take snapshot; change sizes and layout; restore | sizes, layout and cells come back |
| AJ13 | Free: Ctrl+drag an icon a little (onto its own cell) | the original stays; the copy lands on the nearest free spot, not on top of it |
| AJ14 | A game cover set to 2 × 4 | the poster is sharp (decoded at the tile's width), not an upscaled thumbnail |
| AJ15 | Restart NeoFences with a 64-px fence | cells are right from the first frame (no clipped labels or overlapping icons) |

## AK — 0.14.0 widgets (M25)

| ID | Steps | Expected |
|---|---|---|
| AK1 | Apps → Add widget ▸ Clock, Date, System stats (Flow); again in a Free fence | each added (end / first free spot); Clock 2 × 1, Date and Stats 2 × 2 |
| AK2 | Size ▸ 1 × 1 and 4 × 4 on each | content scales; nothing clipped |
| AK3 | Clock → Show seconds, Show date | seconds tick on whole seconds; a date line appears |
| AK4 | Windows settings → time format 12/24-hour | the clock follows (after the next tick) |
| AK5 | System stats while a CPU-heavy task runs; then idle | CPU bar rises and falls; GPU and C: shown (or "—") |
| AK6 | Pause NeoFences / quick-hide / a game in front / roll the fence up | no updates meanwhile; the time is right again at once afterwards |
| AK7 | Drag a widget to another fence; Ctrl+drag a copy; Remove from fence | moves; a second clock; removed |
| AK8 | Double-click the clock; the stats | Windows' Clock app; Task Manager |
| AK9 | Snapshot, remove the widgets, restore | widgets back with their sizes and options |
| AK10 | NeoFences' CPU with three widgets shown (Task Manager or a 30 s measurement) | well under 0.1 % |
| AK11 | A rolled-up fence holding a clock: hover it open | the clock shows the current time at once and keeps ticking while open |
| AK12 | Select a widget and an app → right-click → Open | the app opens; no Windows "get an app for this link" prompt |
| AK13 | Change the time zone (or the time) in Windows' settings | the clock follows at once |

## AL — 0.15.0 the folder panel element (M26)

| ID | Steps | Expected |
|---|---|---|
| AL1 | Start 0.15.0 over data with a folder view (Downloads) | the fence holds one panel filling it, Icons look, same entries and order; no second title; a "Before folder views became panels" snapshot |
| AL2 | Fence menu → Add folder panel… (Flow fence); again in a Free fence | a 4 × 4 Details panel at the end / the first free spot |
| AL3 | Panel menu → Look ▸ Details, List, Icons; Size ▸ 2 × 3 | each look; at 1–2 cells wide Details shows Name and Date only |
| AL4 | Click the Date modified header, then again; Name; Size | newest first, then oldest first; folders first by name; biggest first |
| AL5 | Double-click a subfolder; Back; Up; Home; Backspace; Alt+Up | browses in; the header shows the path below the folder; Up never goes above it |
| AL6 | Panel alone in a fence → Fill fence; resize the fence; Add item… | fills and follows the size; with the new item it sits on 4 × 4 cells again |
| AL7 | Folder item → Show as folder panel; panel → Show as icon | a panel in place; the folder icon again |
| AL8 | Drop a file from Explorer onto a panel; onto empty space beside it | refused over the panel; an item beside it |
| AL9 | Drag entries out to Explorer; Add to fence ▸; Shift+right-click an entry | copies (never moved); items in that fence; Windows' menu under its warning line |
| AL10 | A panel of the pendrive: Safely Remove while shown; plug back in | the drive ejects; "Folder not available"; back by itself |
| AL11 | A game in front (game mode); change files in the folder; leave the game | no listing during the game; current after |
| AL12 | NeoFences' CPU with two panels shown, idle 30 s | well under 0.1 % |
| AL13 | Snapshot from before 0.15.0 (with a folder view) → restore | the view comes back as a filling panel |
| AL14 | A lone filling panel → Add item… (or untick Fill fence) | the panel sits exactly on 4 × 4 cells; its scrollbar and last rows visible (final review I1) |
| AL15 | Free fence: drag a panel by its name row one cell right | it moves (only drops from outside are refused over panels; I2) |
| AL16 | Select a panel's last row → Down; Left in Details; then Delete | the selection stays in the panel; Delete removes nothing (I3) |
| AL17 | A file growing in a Downloads panel (a download running) | its row keeps its icon and selection; size and date update (I4) |
| AL18 | Click empty space inside a panel → Delete; select the panel by its name row | nothing removed; the panel shows a thin outline when selected (M12) |
| AL19 | Fence menu → Sort by on a fence its panel fills (a migrated view) | the panel sorts (Date newest first) (M11) |
| AL20 | A panel taller than its fence: wheel over its rows to their top, then on | the fence scrolls up to the panel's name row |

## AM — 0.16.0 auto-collect rules (M27)

| ID | Steps | Expected |
|---|---|---|
| AM1 | Apps → Auto-collect… → New rule (Desktop, Apps and shortcuts + Installers) → OK | "Add these N too?" when desktop entries match that no fence holds; the menu says "(1 rule)" |
| AM2 | A new shortcut appears on the desktop (install something, or copy a .lnk there) | an item in Apps within a second; the file stays on the desktop |
| AM3 | Remove that item from Apps; change something on the desktop; restart NeoFences | it does not come back |
| AM4 | A rule on Downloads → Installers on another fence; download an installer (a .crdownload first) | one item with the final name |
| AM5 | Two fences' rules on the same folder and kind | the fence higher in the list gets it |
| AM6 | Exit NeoFences; create a matching file in a watched folder; start NeoFences | the file is collected (catch-up) |
| AM7 | A game in front (game mode) or Pause; files arrive; leave the game / resume | collected then, not during |
| AM8 | A rule on the pendrive's `NeoFences-test` folder: Safely Remove; plug back in with a new file | ejects; the new file is collected |
| AM9 | Copy 300 matching files into a watched folder at once | 200 items; the rest logged |
| AM10 | Edit a rule's kinds; remove a rule; Cancel the dialog | saved / gone / nothing changed |
| AM11 | NeoFences' CPU with two rules, idle 30 s | well under 0.1 % |
| AM12 | A Desktop rule; exit NeoFences; a new shortcut on the **user's** Desktop; start | collected (each desktop folder catches up; final review C1) |
| AM13 | A Pictures rule on Downloads; rename a collected picture in place | one item, under the new name (I2) |
| AM14 | Restore a week-old snapshot with rules; restart NeoFences | no old files collected again (I4) |
| AM15 | Extract a 1,000-file zip into a watched folder | 200 items in one batch, one refresh (I3) |

## AN — 0.16.1 polish (M28)

| ID | Steps | Expected |
|---|---|---|
| AN1 | A 2-cell Details panel; a filling panel in a narrow fence; widen the fence | Name and Date when narrow, all four columns from ~300 DIP |
| AN2 | Icon-only fence (labels on hover) with a panel: hover and select the panel | no name pill over it |
| AN3 | Select a panel row → Menu key | the entry menu opens at the row |
| AN4 | A fence of 30 plain items: open, scroll, switch tabs | as before (no widget or panel controls built for them) |
| AN5 | Auto-collect… → New rule on a slow network folder → OK at once | "Add these N too?" still comes, once listed |
| AN6 | A game in front; restart NeoFences; files arrive in a watched folder | collected only when the game ends |
| AN7 | Pause NeoFences 10 s, resume; quick-hide and back; leave a game | the clock is right at once; CPU/GPU show a fresh value |
| AN8 | Widget → Properties… | no Change icon; the target is not previewed |
| AN9 | A `neofences:widget/weather` item (newer NeoFences) | "Unknown widget", Missing badge; double-click opens nothing |
| AN10 | Free fence: select three items, drag the middle one by a cell | the others keep their offsets from the middle one |
| AN11 | Icon size Small → Extra large | icons load once (log / no flicker) |
| AN12 | Size ▸ on two items of different sizes; arrow keys + Enter in the picker; leave the grid | nothing checked ("Mixed sizes"); the size is set; the highlight returns |
| AN13 | Icon-only fence with game covers | covers full size (cells as wide as a cover) |
| AN14 | Free fence: drag a fence item together with a panel entry onto a cell | both land at the drop cell |
| AN15 | Auto-collect… open while files arrive; OK without changing a rule; restart | nothing collected again (the newer watermark kept) |
| AN16 | A Free fence as a hidden tab; Add item… into it through the tray drop or Add from desktop; show the tab | the new items sit in that fence's own columns |
| AN17 | Stats in a rolled-up fence while a clock elsewhere stays visible; open the roll-up after a minute | the first CPU/GPU value is fresh (a 2 s rate), not the minute's average |
| AN18 | Size ▸: arrow keys to 3 × 2, hover 1 × 1, leave the grid, Enter; Left at column 1 | sets the size shown; Left at the edge closes the picker |
| AN19 | Auto-collect: a new rule on a slow share, OK, start a game before the listing returns | no question over the game (logged) |
| AN20 | Icon-only Free fence of icons with one game cover (Ctrl-copy a cover in) | cells as wide as a cover; icons keep their stored cells or move to free ones; back when the cover goes |
