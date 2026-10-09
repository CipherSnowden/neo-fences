# Roadmap

Checkbox legend: `[ ]` open · `[~]` claimed/in progress (say by whom) · `[x]` done · `[!]` failed (see note).
Claim a task and commit the claim **before** starting it (see CLAUDE.md session protocol).
Milestone details and exit criteria: spec §9.

## Now — the pivot (2026-10-04, ADR-040, docs/PIVOT-2026-10-04.md)

- [x] Decided: fences hold **virtual items**; no file operations; native icons visible (optional hide); versions 0.x; fresh GitHub repo
- [x] Docs written (PIVOT, ADR-040, CLAUDE.md hard rules)
- [x] Reset: local history bundle in F:\projects\ (neo_fences-history-2026-10-04.bundle), GitHub reset to one commit (0.8.0), release/tags/branch deleted
- [x] Installed v1.8.0 and its data removed from this PC (data backed up to F:\projects\neofences-data-backup-2026-10-04)
- [x] **M18 — Virtual items (0.9.0)** — spec `docs/superpowers/specs/2026-10-04-virtual-items-design.md`, plan `docs/superpowers/plans/2026-10-04-m18-virtual-items.md`, ADR-040/041
  - [x] Core: virtual items and their edits; target checks, watch plan, refresh throttle
  - [x] Core: config schema 5, items.json (`ItemStore`, `JsonStore`), snapshots with items; Membership, Rules, Portals, Inbox and Takeover removed
  - [x] Shell: drops create items (link, never move), drag-out copies, pickers, target probe, Windows' menu with its header line
  - [x] App: items in fences, item menu, Properties / Add item…, Locate…, target watching, "Hide desktop icons while NeoFences runs"
  - [x] Docs: ARCHITECTURE, FEATURES, TEST-CHECKLIST section AD
  - [x] Final review (Opus) and its fix pass; live check: TEST-CHECKLIST AD 32/32 pass, five bugs fixed on the way (`research/m18-virtual-items.md`)
  - [x] Merged to main (fast-forward), version 0.9.0
  - [x] Release 0.9.0: main pushed (CI green) → tag v0.9.0 → draft → install check on this PC → published 2026-10-04
- [ ] Before 1.0.0 (first version for friends): code signing deferred — unsigned is accepted for 1.0 (user, 2026-10-06: no paid signing yet; see the 1.0 plan below). Name decided: stays NeoFences (ADR-043)

### Path to 1.0.0 (review 2026-10-06, `docs/research/v1-readiness.md`)

User decisions 2026-10-06: safety net first; online game covers in 1.0 as an opt-in (off by default, ADR at M34);
a short freeware licence drafted in M39 for the user's approval; Hyper-V test machines on this PC for M38 (ask again
before setting them up); a release candidate for friends before 1.0.0. Unsigned and not open source for 1.0.

- [x] **M33 — Safety net** (0.20.0; released 2026-10-06): crash-loop message and safe mode, save-failure notices, undo for remove item / delete fence, gesture switches (quick-hide, right-drag; off = no mouse hook), marker-before-hide, watchdog kept alive, damaged-file fallback, log size and privacy, update after a crash
  - [x] Released 0.20.0 on 2026-10-06 (the installed 0.19.1 updated itself in 2 s; data unchanged; logs now write %USERPROFILE%)
- [x] **M34 — Icons and game tiles** (0.21.0; released 2026-10-06): crisp icons at any scaling, rounded and theme-aware cover tiles, the no-art tile, Set cover…, opt-in online covers, 2:3 cover sizes, row spacing, accent selection, Steam art from disk, website and Xbox tiles
  - [x] Released 0.21.0 on 2026-10-06 (the installed 0.20.0 updated itself in 3 s; data unchanged; the covers question showed on the real setup)
- [x] **M35 — Modern menus and dialogs** (0.22.0; released 2026-10-06): Fluent fence/item menus, dark tray menu, the fence menu regrouped (~10 entries), one Fluent confirm dialog, a modern colour picker
  - [x] Released 0.22.0 on 2026-10-06 (the installed 0.21.0 updated itself in 3 s; data unchanged; the new tray menu after the update)
- [x] **M36 — Fence settings and presets** (0.23.0; released 2026-10-06): per-fence settings window (transparency, title alignment/font, hide title bar, labels, spacing), look presets, export/import, Settings navigation + switches + reset
  - [x] Released 0.23.0 on 2026-10-06 (the installed 0.22.0 updated itself; data unchanged)
- [x] **M37 — Performance at scale** (0.24.0; released 2026-10-06): icon cache, virtualized fences, cheaper label shadows, idle timers that stop, ready-to-run publish; 500 items / 50 fences test
  - [x] Released 0.24.0 on 2026-10-06 (the installed 0.23.0 updated itself in 2 s; data unchanged)
- [x] **M38 — Compatibility and accessibility** (0.25.0; released 2026-10-10): Peek takes the keyboard with visible focus, Tab between fences, Esc back to your app (ADR-060); a scripted Windows 11 test pass in a Hyper-V VM, 12 of 12 (`tools/vm/`; Windows 10 dropped, ADR-059). Not picked by the owner for M38: two monitors with mixed scaling, High Contrast, screen-reader names
  - [x] Released 0.25.0 on 2026-10-10 (VM install check: 0.24.0 updated itself to 0.25.0 from the release files, 12 of 12)
- [x] **M38.1 — Test VMs for live checks** (merged 2026-10-07): a second VM `NF-Win11-Dev` (a copy of `NF-Win11`) and `tools/vm/Invoke-VmChecks.ps1 -Live` (8 of 8 on a copy of the owner's data; the release-candidate pass still 12 of 12) — live checks and probes of branch builds run in the VM (test fences, copies of some of the owner's files), not on the owner's desktop
- [x] **M39 — Release readiness** (0.26.0; released 2026-10-10): freeware licence, PRIVACY, SECURITY, third-party notices (shipped next to the exe; Settings → About → Licence and notices), issue forms, checksums and build attestations on releases, README and guide refresh with new screenshots, a landing page (GitHub Pages), Core tests on Linux in CI (ADR-061); outreach (VirusTotal, Microsoft review, winget) prepared in docs/RELEASING.md for 1.0
  - [x] Released 0.26.0 on 2026-10-10 (Pages and private vulnerability reporting on; checksums and attestations verified; VM install check 13 of 13)
- [~] **1.0.0-rc** to friends (claimed by session 2026-10-10 release-candidate): RC tags as GitHub pre-releases that stable copies never see, RC installs follow RCs; about a week, no blockers; then **1.0.0**
- After 1.0: Avalonia spike → Linux (KDE) → macOS; Microsoft Store build; sensor picker; clock options; desktop pages; search palette; theme packs; translations
- [x] **M19 — 0.10.0: Store apps as items, bulk fix of missing items, Add from desktop…, M18 reliability leftovers** — spec `docs/superpowers/specs/2026-10-05-m19-apps-relocate-desktop-fill-design.md`, plan `docs/superpowers/plans/2026-10-05-m19-apps-relocate-desktop-fill.md`, ADR-042/043
  - [x] Prototype with live probes (app list, Start drag, generic icons, bulk fix, Add from desktop on the real desktop); plan with replay-verified patches
  - [x] Core (apps, Relocation, DesktopSorting, StaleEntries), Shell (AppList, generic icons, COM releases), App (app list, bulk fix + undo, Add from desktop, R1–R8)
  - [x] Final Opus review "with fixes": dead-share probes, mapped drives, app answers, quoted app launch, Locate owner guard fixed (460 tests); minors deferred (`research/m19-apps-relocate-desktop-fill.md`)
  - [x] Live check TEST-CHECKLIST AE (29/30; AE25 needs a mapped drive) + AD2/AD8/AD14/AD18; merged to main, version 0.10.0
  - [x] Release 0.10.0: main pushed (CI green) → tag v0.10.0 → draft → install check (the installed 0.9.0 updated itself) → published 2026-10-05
- [x] **M20 — 0.10.1: the deferred M19 review minors** (bounded; design agreed in chat 2026-10-05; the R1 microsecond race stays as is)
  - [x] Fixes test-first (468 tests); live check TEST-CHECKLIST AF 4/4 (+ AF5 by reading); merged to main, version 0.10.1
  - [x] Released 0.10.1 on 2026-10-05: CI green, draft with delta, install check (the installed 0.10.0 updated itself to 0.10.1, data unchanged), published, hub refreshed
- [x] **M21 — 0.11.0: folder views** (read-only live folder fences; spec 2026-10-05-folder-views-design, ADR-044)
  - [x] Prototype (worktree), plan with replay-verified patches, native build (Core 29 new test cases), Opus final review: 3 important + 3 re-graded fixed (502 tests)
  - [x] Live check TEST-CHECKLIST AG: 19 pass; AG8/AG9 (stick pull, Safely Remove) skipped by the user's choice; AG10 not run; found and fixed: views from one fence stacked on one spot; merged to main
  - [x] Released 0.11.0 on 2026-10-05: CI green, draft with delta, install check (the installed 0.10.1 updated itself to 0.11.0, data unchanged), published, hub refreshed
  - Deferred minors: fixed in 0.12.1 (M23)
- [x] **M22 — 0.12.0: one kind of fence — games become items** (spec 2026-10-05-games-as-items-design, ADR-045)
  - [x] Prototype probed on a copy of the user's data, plan with replay-verified patches, native build; Opus review: 5 important + 2 re-graded fixed (515 tests)
  - [x] Live check TEST-CHECKLIST AH 18/18 (scripted, PC unattended); merged to main (user: merge locally until 1.0)
  - [x] Released 0.12.0 on 2026-10-05: CI green, draft with delta, install check (the installed 0.11.0 updated itself; the Games fence became 12 game items after its safety snapshot), published, hub refreshed
  - Deferred minors: fixed in 0.12.1 (M23)
- [x] **M23 — 0.12.1: the M21/M22 review minors** (bounded; design agreed in chat 2026-10-05)
  - [x] Fixes (Core test-first: FolderPath, two library fences; 526 tests); live check TEST-CHECKLIST AI 6/6 (20,000-file view: fences answered within 6 ms during churn); merged to main
  - [x] Released 0.12.1 on 2026-10-05: CI green, draft with delta, install check (the installed 0.12.0 updated itself, data unchanged), published, hub refreshed
- [x] **M24 — 0.13.0: element sizes and the fence grid** (spec 2026-10-05-element-sizes-design, ADR-046)
  - [x] Prototype probed on a copy of the user's data, plan with replay-verified patches, native build; Opus review: 5 important fixed (542 tests)
  - [x] Live check TEST-CHECKLIST AJ 14/15 (AJ6, a drop from Explorer onto a Free cell, by hand later); merged to main
  - [x] Released 0.13.0 on 2026-10-05: CI green, draft with delta, install check (the installed 0.12.1 updated itself; config gained each fence's layout, nothing else changed), published, hub refreshed
  - Deferred minors: in `research/m24-element-sizes.md`
- [x] **M25 — 0.14.0: widgets (clock, date, system stats)** (spec 2026-10-05-widgets-design, ADR-047)
  - [x] Prototype probed on a copy of the user's data (0.003 % CPU), plan with replay-verified patches, native build; Opus review: 5 important + 2 re-graded fixed (559 tests)
  - [x] Live check TEST-CHECKLIST AK 11/13 (AK4 12/24-hour and AK13 time zone by hand later); merged to main
  - [x] Released 0.14.0 on 2026-10-05 (the installed 0.13.0 updated itself)
  - Deferred minors: in `research/m25-widgets.md`
- [x] **M26 — 0.15.0: the folder panel element** (a folder as Details / List / Icons inside any fence; replaces folder-view fences; spec 2026-10-05-folder-panel-design, ADR-048)
  - [x] Prototype probed on a copy of the user's data, plan with replay-verified patches, native build; Opus review: 4 important + 4 re-graded fixed (606 tests)
  - [x] Live check TEST-CHECKLIST AL by script (AL8, AL10, AL11, AL15, AL17, AL20 by hand later); a tall panel's wheel fix; merged to main
  - [x] Released 0.15.0 on 2026-10-05 (the installed 0.14.0 updated itself in 3 s; its Downloads view became a filling panel, a snapshot first)
  - Deferred minors: in `research/m26-folder-panel.md`
- [x] **M27 — 0.16.0: auto-collect rules** (new files of the desktop or a folder become items in a fence; spec 2026-10-05-auto-collect-design, ADR-049)
  - [x] Prototype probed on a copy of the user's data, plan with replay-verified patches, native build; Opus review: 1 critical + 3 important + 2 re-graded fixed (633 tests)
  - [x] Live check TEST-CHECKLIST AM by script on a test folder (the real desktop, pendrive, AM12, AM14 by hand later); merged to main
  - [x] Released 0.16.0 on 2026-10-05 (the installed 0.15.0 updated itself in 3 s; data unchanged)
  - Deferred minors: in `research/m27-auto-collect.md`
- [x] **M28 — 0.16.1: polish** (the 20 deferred minors of M24–M27; spec 2026-10-05-polish-design)
  - [x] Prototype (a visual probe caught a template bug), plan with replay-verified patches, native build; Opus review: 0 critical / 0 important, 5 re-graded minors fixed (643 tests)
  - [x] Live check TEST-CHECKLIST AN by script (the rest by hand later); merged to main
  - [x] Released 0.16.1 on 2026-10-05 (the installed 0.16.0 updated itself in 3 s; data unchanged)
  - Deferred minors: in `research/m28-polish.md`
- [x] **M29 — 0.17.0: a guide for friends** (README rewrite, docs/GUIDE.md with screenshots, Help in the tray and Settings)
  - [x] Spec, plan (app change as a replay-verified patch), native build; screenshots from a demo setup (Release build)
  - [x] Opus review: 0 critical / 3 important (tray overflow, browser download warning, guide rule into CLAUDE.md), 6 wording minors fixed (643 tests)
  - [x] Live check AO1, AO2, AO4 by script; merged to main
  - [ ] AO3 (links on GitHub) after the push; AO5 (quick start on a fresh user) by hand
  - [x] Released 0.17.0 on 2026-10-06 (the installed 0.16.1 updated itself in 3 s; data unchanged; tray shows Help)
  - Deferred minors: in `research/m29-guide.md`
- [x] **M30 — 0.18.0: first-run welcome** (a welcome in the first fence, Sort my desktop, the tray notice)
  - [x] Spec, plan with replay-verified patches, native build (655 tests)
  - [x] Opus review: 0 critical / 1 important (read-only start showed the welcome) + 1 raised minor (a set-up welcome fence removed), fixed (658 tests)
  - [x] Live check AP1, AP2, AP5, AP6, AP8, AP9 by script; merged to main
  - [ ] AP3, AP4, AP7 and the tray notice by hand
  - [x] Released 0.18.0 on 2026-10-06 (the installed 0.17.0 updated itself in 2 s; data unchanged; no welcome on the existing setup)
  - Deferred minors: in `research/m30-welcome.md`
- [x] **M31 — 0.19.0: modern widgets and sensors, polish** (widget restyle; CPU/GPU temperatures via Afterburner or HWiNFO; RAM in GB; M29–M30 minors)
  - [x] Mockups in the visual companion (style B with A's bars), spec, prototype + probe, replay-verified plan, native build
  - [x] Opus review: 0 critical / 5 important (odd names, per-value sources, the graphics card by video memory, 2×2 legibility) + RAM installed, fixed (682 tests)
  - [x] Live check AQ1, AQ3, AQ4 by script (stats equal Afterburner); new guide shots; merged to main
  - [ ] AQ2 (light mode), AQ5 (Afterburner closed), AQ6 by hand
  - [x] Released 0.19.0 on 2026-10-06 (the installed 0.18.0 updated itself in 2 s; data unchanged)
  - [x] The user's desktop with every feature (2026-10-06): Desk top-left (Clock with date, Date, Stats), Clair Obscur and Detroit at 2×2 in a Free Games fence, Downloads as a Details panel; snapshot 2026-10-06 02:45 first
  - Deferred minors: in `research/m31-widgets.md` and the session log
- [x] **M32 — 0.19.1: polish** (M31 minors: accent bars, sensor buffer, sticky GPU, hint line, HWiNFO lock, CJK date, notice in a game, Free spots saved at first layout, tidy-ups, checklist)
  - [x] Spec, prototype checked against Afterburner, replay-verified plan, native build (697 tests)
  - [x] Opus review: 0 critical / 3 important (notice after a game at first start, date line with Windows' regional formats, accent on DWM's message) fixed (700 tests)
  - [x] Live check AR1 (accent change, scripted with the user's OK; restored) and AR2 (Free cells stored at start); merged to main
  - [ ] AR3–AR8 by hand
  - [x] Released 0.19.1 on 2026-10-06 (the installed 0.19.0 updated itself in 2 s; data unchanged; the Games fence's Free cells stored at that start)
  - Deferred minors: in `research/m32-polish.md`
- Everything below this section is the pre-pivot history (v1.x = pre-reset dev builds); open items there are superseded unless M18 revives them.

## Before the pivot

- [x] Brainstorm + v1 design spec — session 2026-10-02
- [x] Docs scaffold (CLAUDE.md, docs/*) — session 2026-10-02
- [x] Project hub artifact (`docs/hub/neofences-hq.html`, ADR-010) — session 2026-10-02
- [x] User review of v1 spec — approved 2026-10-02
- [x] M0 implementation plan (`docs/superpowers/plans/2026-10-02-m0-desktop-layer-spike.md`)
- [x] User review of M0 plan — approved 2026-10-02, execution: inline (native)
- [x] User: install .NET 10 SDK (10.0.401 installed 2026-10-02)
- [x] Merge `m0-desktop-layer-spike` into `main` — done 2026-10-02 after review fixes
- [x] M1 implementation plan (`docs/superpowers/plans/2026-10-02-m1-core-config.md`, 7 tasks, 58 pre-verified tests)
- [x] User review of M1 plan — approved 2026-10-02, execution: inline (native)
- [x] Merge `m1-core-config` into `main` — done 2026-10-02 after review fixes
- [x] M2 split into M2a / M2b / M2c; M2a plan written (`docs/superpowers/plans/2026-10-02-m2a-fence-host.md`, 6 tasks, 87 pre-verified tests + smoke run)
- [x] User review of M2a plan — approved 2026-10-02, execution: inline (native)
- [x] Merge `m2a-fence-host` — done 2026-10-02 after review fixes (ADR-013)
- [x] M2b implementation plan (`docs/superpowers/plans/2026-10-02-m2b-desktop-items.md`, 5 tasks, 92 pre-verified tests + smoke run on the real desktop)
- [x] User review of M2b plan — approved 2026-10-02, execution: inline (native)
- [x] Merge `m2b-desktop-items` — done 2026-10-02 after review fixes (ADR-014 amended)
- [x] M2c implementation plan (`docs/superpowers/plans/2026-10-02-m2c-fence-interactions.md`, 5 tasks, 113 pre-verified tests + prototype smoke on the real desktop; user chose: follow Windows light/dark, soft label shadow)
- [x] User review of M2c plan — approved 2026-10-02, execution: inline (native)

## M0 — Spike: desktop layer (GO/NO-GO) — done 2026-10-02: GO, Takeover conditional on C8 (ADR-011)
- [x] See-through fence over Wallpaper Engine — DWM backdrop failed (flat when inactive); layered + accent blur passed
- [x] Survives Win+D / Show desktop button / Win+M — via owned-by-Progman (raise-on-Win+D failed). Aero Peek hover not tested
- [x] Survives a forced Explorer kill (TaskbarCreated) on 25H2 — re-ownership + graceful restart still to verify
- [x] Hide/restore native icons via IFolderView2 FWF_NOICONS
- [x] Watchdog restores icons after crash / kill; 3-per-10-min limit
- [x] WH_MOUSE_LL: desktop double-click; right-drag with S2 suppression (S1 failed: desktop lock-up)
- [x] Findings in `docs/research/desktop-layer.md`, `docs/TEST-CHECKLIST.md`, ADR-011

## M1 — Core + config — done 2026-10-02
- [x] Solution + projects (`src/`, `tests/`)
- [x] Model, ConfigStore (atomic save, backups, corrupt recovery), LayoutEngine, Membership — test-first (71 tests incl. review fixes)

## M2 — Fences render items (daily usable) — split into M2a / M2b / M2c (ADR-012)
### M2a — Fence host — done 2026-10-02 (ADR-012, research/m2a-fence-host.md)
### M2b — Desktop items (icons, open/select, change notifications, Takeover + first run) — done 2026-10-02 (ADR-014, research/m2b-desktop-items.md)
Carry-overs from M2b (limitations, research note):
- [x] M2c: styled thin scrollbar; label readability on bright wallpapers (light/dark); re-render icons after DPI change — done in M2c
- [x] Decide on shortcut arrows on fence icons — a Settings switch, off by default (user, M8b; ADR-025)
- [x] M3: visible feedback when an item cannot be opened — done in M3a (Windows' own message)
- [x] Live special-icon toggles / Recycle Bin full-empty icon (SHChangeNotifyRegister) — M8c (ADR-026)
- [ ] H10 (slow/broken shell handlers) — optional user check
Carry-overs from the M2b review:
- [x] **Before M3 drag-drop:** safe-save (delete/rename-away + rename-onto) must return a document to its fence and position (short-lived "recently removed" memory in Core, with a test pinning Word's sequence)
- [x] Start the desktop watcher before the startup reconcile (items created in between are missed until restart) — M8a (ADR-024)
- [x] `SetItems`: diff instead of Clear + re-add (keeps scroll/selection; bursts are O(N²)); tolerate duplicate refs — M8b (ADR-025)
- [x] Icons: accept non-32-bpp bitmaps; release IShellItem RCWs; one slow item delays the queue (two loader threads) — M8b (ADR-025)
- [x] Icons: load after layout on mixed-DPI setups — M8c (ADR-026)
- [x] Watcher: rename split across buffers (empty name) → reconcile; re-check visibility on transient IO errors; save events arriving after shutdown — M8a (ADR-024)
### M2c — Fence interactions (keyboard, snap, lock, scroll, icon size, rename, light/dark) — done 2026-10-02, merged (ADR-015, research/m2c-fence-interactions.md)
Carry-overs from M2c:
- [x] Keyboard focus when a fence is clicked while another app is in front (activation follows the desktop's input queue) — stays a known limit (ADR-015; Activate() probe in research/m8b-fence-ui.md)
- [x] Rubber-band selection — done in M3b
- [x] New fences cascade onto an existing fence's position — replaced by smart placement (FreeSpot) in M5
- [ ] I10 (labels over bright wallpaper) and I12 (scaling change) — user checks
Carry-overs from the M2c review (minors):
- [x] Icon size change: re-request on existing views (keep labels/selection), skip when unchanged — M8b (ADR-025)
- [x] Title cut at 64 must not split a surrogate pair; TitleBox.MaxLength — M8b (ADR-025)
- [x] Resize snap must respect the minimum window size — M8b (ADR-025)
- [ ] Measure label DropShadowEffect cost with ~200 items / software rendering
- [x] Build the icon-size menu from ConfigNormalizer.IconSizes (one list) — M8b (ADR-025)
Carry-overs from M0 (ADR-011, research note "M2 re-verification"):
- [x] Layered fence window + accent blur, rounded corners via window region (clips the blur) — user approved the current tint; preference in v2
- [x] Re-run on layered + owned: A2 (pixel diff), B2, B3/B4/B6, B7 forced + B10 graceful (GW_OWNER verified, unattached 0) — A4 smoothness [~] waiting on user; D2 moves to M5 (desktop gestures)
- [x] UI-thread hang test (B11): desktop menu and taskbar responsive
- [x] Detached watchdog (G6/G7); session end redone after review (ADR-013) and verified by scripted G9 — real cancelled-shutdown check [~] waiting on user
- [~] Real sign-out test (C8) incl. whether FWF_NOICONS persists — persistence answered by a real power loss (2026-10-03): it persists → start with Windows (ADR-019); a clean sign-out with icons restored is still a user check
- [~] Elevated-foreground checks (B8 needs a UAC prompt, waiting on user; D7 in M5)
Carry-overs from the M1 review (deferred minors):
- [x] Define MoveItem drop-index semantics for drag-drop — done in M3b (`MoveItems`: index in the list as shown during the drag)
- [x] DisplayMonitor.DeviceId from the device path (must be unique; duplicates throw) — M8a (ADR-024)
- [x] Keep fence positions unclamped in stored layouts (clamp for display only); place fences created on another setup from the last layout — M8a (ADR-024)
- [x] Report daily-backup write failures separately from Save; consider re-matching refs after a OneDrive Known Folder Move — M8a (ADR-024)

## M3 — Shell actions — split into M3a / M3b (user, 2026-10-02)
### M3a — Item actions (Windows item menu, F2 rename, Del to Recycle Bin, open-failure feedback, safe-save keeps fence) — done 2026-10-02, merged (ADR-016, research/m3a-item-actions.md)
- [ ] J2, J4, J6, J9, J10 and a real Word save — user checks
Carry-overs from the M3a review (minors):
- [x] Double-click inside the rename box must not open the file — M8b (ADR-025)
- [x] Same file name on the user and Public Desktop: shell menu acts on the user copy — M8c (ADR-026)
- [x] Multi-item recycle: skip unresolvable refs instead of cancelling all — M8c (ADR-026)
- [x] Item menu dismissal when the fence cannot be foreground (+ PostMessage WM_NULL) — M8c (ADR-026)
- [x] Menu Rename with several selected: right-clicked item / batch rename — M8c: the right-clicked item (user choice; ADR-026)
- [x] Log QueryContextMenu failures; Shift+F10 = normal menu — M8c (ADR-026)
- [x] Safe-save memory: monotonic clock; reconcile consults it — M8a (ADR-024)
- [x] Open error dialog and recycle off the UI thread (STA worker) — M8c (ADR-026)
- [x] M3a implementation plan (`docs/superpowers/plans/2026-10-02-m3a-item-actions.md`, 5 tasks, 122 pre-verified tests + prototype smoke on the real desktop; user chose: Shift+Del also recycles)
- [x] User review of M3a plan — approved 2026-10-02, execution: inline (native)
### M3b — Drag-drop (between fences, out to apps, in from Explorer) + rubber-band selection — done 2026-10-03, merged (ADR-017, research/m3b-drag-drop.md)
- [ ] K4/K5 (drops in from Explorer), K8, K9, multi-item K2, folder K3, app K6 — user checks
Carry-overs from the M3b review (minors):
- [x] Drop hit test: rows by tallest item (mixed label heights) — M8c (ADR-026)
- [x] Focus the list on an empty-space press (rename commit, keys after a band); Ctrl+press on a selected item: toggle on release — M8b (ADR-025)
- [x] Read CF_HDROP lazily (virtual files); cache container checks per hovered item — M8c (ADR-026)
- [x] Mixed drags: the Desktop subset joins the drop fence; arrivals for links and virtual items — M8c (ADR-026)
- [x] Public/user Desktop same name (item menu, drag) — M8c (ADR-026)
- [x] M3b implementation plan (`docs/superpowers/plans/2026-10-03-m3b-drag-drop.md`, 5 tasks, 147 pre-verified tests + prototype smoke on the real desktop; drops from Explorer by hand)
- [x] User review of M3b plan — approved 2026-10-03, execution: inline (native)
## M4 — Portals (user choices 2026-10-03: browse subfolders inside the fence with Back; new Portals sort newest first)
- [x] M4 implementation plan (`docs/superpowers/plans/2026-10-03-m4-portals.md`, 5 tasks, 163 pre-verified tests + prototype smoke on the real desktop)
- [x] User review of M4 plan — approved 2026-10-03, execution: inline (native)
- [x] M4 — done 2026-10-03, merged (ADR-018, research/m4-portals.md)
- [ ] L1 (pick a real folder), L6, L8, L11, Ctrl-drag L7 — user checks
Carry-overs from the M4 review (minors):
- [x] Portal on USB blocks Safely Remove (close watchers on device query-remove) — M8d (ADR-027; checked on the user's stick)
- [x] Incremental Portal list updates (keep selection/rename; big folders) — M8b in-place diff (checked live in M8d)
- [x] Portal of the Desktop folder: use the real parent folder, not the merged desktop — covered by M8c's same-name rule (ADR-027)
- [x] Enter on several folders in a Portal; cache drop-target checks per drag; watcher error backoff — M8d (cache: M8c; backoff: WatcherBackoff)
## M5 — Desktop gestures
- [x] Before M5: start with Windows (power-loss recovery; user decision 2026-10-03) — done, ADR-019
- [x] M5 implementation plan
- [x] M5 — done 2026-10-03 (ADR-020, research/m5-desktop-gestures.md)
- [ ] N5, N6, N8 (need Takeover off / a changed hotkey), N13, N14 — user checks
Carry-overs from the M5 review (minors):
- [x] Rolled-up fence moved to a monitor with another DPI keeps the old DPI's full height — M8b (ADR-025)
- [x] peekHotkey digits ("Ctrl+Alt+1") bind the wrong key — fixed in M6b
- [x] Quick-hide with Takeover off can show icons the user hid in Explorer; skip icons that were already hidden — M8a (ADR-024)
- [x] Retry showing icons on exit when quick-hide's show failed (check the takeover-active marker) — M6a
- [x] Replay the right-click only if the pointer is still over the desktop — M6a
- [x] Icon hit test: treat a native icon's rename box (Edit) as "on an icon" — M8b (ADR-025)
- [x] Hook thread priority Highest; re-install the hook on Explorer restart and session unlock — M6a
## M6 — Polish + game mode (done: M6a + M6b)
User choices 2026-10-03: split into **M6a** (game mode + mouse-hook hardening + tray + Pause) and **M6b** (settings window + animations); game mode = go idle (fences stay, no hiding); tray left-click opens the tray menu.
- [x] M6a implementation plan
- [x] M6a — done 2026-10-03 (ADR-021, research/m6a-game-mode-tray.md)
- [ ] O1, O2 (real icon), O5/O6 with a real game, O7–O10 — user checks
- [x] First title double-click on a fence that just became the foreground does not roll it up (second does; M5 behaviour, research/m6a finding 4) — M8b: NeoFences recognises the double-click itself (ADR-025)
Carry-overs from the M6a review (minors):
- [x] Session end returns before disposing the tray icon (ghost icon after a cancelled shutdown + restart) — M8a (ADR-024)
- [x] SetQuickHidden / OnDesktopGesture still decide icons outside RunState — M8a (ADR-024)
- [x] Peek hotkey stays registered while paused or gaming — released in M6b (RunState.PeekHotkeyWanted)
- [x] Log a failed session-notification registration; use PInvoke.WM_WTSSESSION_CHANGE instead of 0x02B1 — M8a (ADR-024)
- [x] Cap the queue of Desktop changes deferred during a game (one reconcile past ~500) — M8a (ADR-024)
- [x] M6b implementation plan — user choices 2026-10-03: roll-up offers hover (default) and click; no Appearance section in v1 (tint/opacity with v2 themes)
- [x] M6b — done 2026-10-03 (ADR-022, research/m6b-settings-animations.md)
- [ ] P2, P3, P5, P7, P9, P11, P12 (and P10 with a real game) — user checks
Carry-overs from the M6b review (minors):
- [x] Pause / ApplyLayout use raw Show/Hide (a stale quick-hide fade could hide a fence within 150 ms) — M8a (ADR-024)
- [x] Friendly key names in the hotkey box ("Oemplus", "D1") — M8b (ADR-025)
- [x] Release the Peek hotkey while the recorder box has focus (pressing it there toggles Peek) — M8b (ADR-025)
- [x] Settings should show when the Peek hotkey is not registered, and re-entering it should retry — M8b (ADR-025)
- [x] Accessibility: announce hotkey errors (LiveSetting), link descriptions as HelpText — M8b (ADR-025)
- [x] Game mode = (QUNS_BUSY or D3D_FULL_SCREEN) AND foreground is a non-shell, non-lock-screen app; re-check shortly after activation (QUNS lags ~1 s); investigate why cover-monitor never fired; test E2 (exclusive fullscreen) and E4 (auto-hide taskbar) (M0 findings 4–5) — done in M6a (cover-monitor check dropped, ADR-021)

## M7 — Package (Velopack) → v1.0
User choices 2026-10-03: installer only for now (no update source; GitHub auto-update later); uninstall keeps the data
in %LOCALAPPDATA%\NeoFences (icons always restored).
- [x] M7 implementation plan
- [x] M7 — done 2026-10-03 (ADR-023, research/m7-installer.md) — **v1.0 released 2026-10-03 (local installer, tag v1.0.0), installed on the dev PC**
- [x] Q2, Q8, Q10 by the user 2026-10-03: uninstalled and reinstalled by hand; icons hidden again automatically, fences back
- [x] Q3 by the user 2026-10-03: Settings → About shows 1.0.0
- [x] Q6 by the user 2026-10-03: the installed NeoFences started by itself after a restart
- [x] Q7 2026-10-03: v1.0.1 Setup over the installed 1.0.0 upgraded in place (fences, Takeover and start-with-Windows kept; 0.3 MB delta built)
- [ ] Q9 — user check (skipped for now by the user)
- [x] Later: update source (GitHub Releases), code signing — update source M17; signing deferred (ADR-039)
Carry-overs from the M7 review (minors):
- [x] Uninstall stop-wait counts other users' NeoFences processes (filter by session) — M8a (ADR-024)
- [x] InstallHooks.InstallRoot: reuse StartupPolicy.IsInstalledExe — M8a (ADR-024)
- [x] pack.ps1: validate -Version first; clear or document the re-pack-same-version prompt; Push/Pop-Location — M8a (ADR-024)
- [x] Uninstall hook restores desktop icons — M7 (InstallHooks, ADR-023)

## M8 — v1.1: carry-overs + icon-only labels (user, 2026-10-03)
User choices: the open carry-overs (~45) in 4 batches, reliability first — M8a reliability (icons, startup, watchers,
game queue, tray ghost, pack script), M8b fence UI + icon-only labels, M8c shell actions + drag-drop details, M8d Portal
details. Icon-only labels: per fence ("Labels ► Always / On hover") with a Settings default for new fences and an
"apply to all"; when hidden, the name pops up under the hovered (or selected) icon, over its neighbours. Stale
carry-overs (rubber band, cascade → smart placement, M2c scrollbar/DPI) are ticked as done.
- [x] M8a implementation plan
- [x] M8a — done 2026-10-03 (ADR-024, research/m8a-reliability.md), incl. the user-reported corner fix
- [x] **v1.0.1 released and installed 2026-10-03** (tag v1.0.1): rounded corners + the M8a reliability fixes
- [ ] R3–R8, R10 — user checks
Carry-overs from the M8a review (minors):
- [x] Reconcile: consume the memories it applies; insert several memories for one fence by index — M8c (ADR-026)
- [x] Coalesce watcher Overflowed (one re-arm + reconcile per burst; back off a watcher that errors at once) — M8c (ADR-026)
- [x] Log a refused DWM corner preference; drop the stale CornerRadius="0" in XAML — M8b
- [x] Check the WindowChrome region radius on Windows 10 — dropped: Windows 11 only (ADR-059)
- [x] UniqueDeviceIds: number duplicates in a stable order (GDI name / bounds) — M8c (ADR-026)
- [x] Polish: stacked summaries above FenceHost.Current, dead second tray dispose, backup-prune message, pack.ps1 leading zeros — M8b
- [x] M8b implementation plan
- [x] M8b — done 2026-10-03 (ADR-025, research/m8b-fence-ui.md): icon-only fences, shortcut arrows, fence UI + Settings carry-overs
- [ ] S9, S10, S16 — user checks
- [x] **v1.1.0 released and installed 2026-10-03** (tag v1.1.0): icon-only fences, shortcut arrows, the M8b fixes
- [x] Setup over an installed copy closed NeoFences but did not start 1.1.0 again (1.0.1 did): icons stayed hidden ~30 s with no app; investigate (start after a Setup upgrade, M8c) — M8c: a new instance waits for the exiting one; confirm at the next upgrade (T13)
Carry-overs from the M8b review (minors):
- [x] Own title double-click: swallow a DBLCLK right after a recognised one (triple-click); use GetMessageTime / lParam point — M8c (ADR-026)
- [x] ConfigNormalizer: validate LabelMode values (integers in a hand-edited config) — M8c (ADR-026)
- [x] Settings: HelpText for every setting's description; row titles wrap at the minimum width — M8c (ADR-026)
- [x] Icon-only fences: give the rename box the labelled-cell width — M8c (ADR-026)
- [x] Hotkey key caps from the keyboard layout (MapVirtualKey) instead of US names — M13c
- [x] Dead code: FenceWindow._itemRefs, CancelItemRenames, double summary on SetItems — M8c (ADR-026)
- [x] M8c implementation plan
- [x] M8c — done 2026-10-03 (ADR-026, research/m8c-shell-dragdrop.md): shell worker, item menu, live special icons, drag-drop details, review minors
- [ ] T7, T8, T9, T10, T12, T13 — user checks
Carry-overs from the M8c review (minors):
- [x] Zip selections: use the file descriptors at drop time too (CF_HDROP still extracts on the UI thread when dropped) — M8d
- [x] Mixed drops: hand Windows only the non-desktop items (Ctrl makes "- Copy" duplicates; Public items move to the user Desktop); arrival index after items moved from above — ruled, ADR-035
- [x] Mixed selection with a shadowed Public Desktop item: no menu/drag (logged) — split per folder — ruled, ADR-035
- [x] Shell worker: skip queued operations at shutdown; owner handle 0 when the fence is gone — M8d: owner 0 when the fence is gone (skipping queued work at shutdown deferred: it would drop a requested recycle)
- [x] Title double-click: clear the recognised time on DBLCLK (a fast click run can swallow a real double-click) — M8d
- [x] Special-icon reloads during game mode: defer like the reconcile — M8d
- [x] --exit while a second instance waits for the mutex: the waiter should give up too — M13a
- [x] DropZones.InsertIndex is a no-op with WrapPanel (rows already share the tallest height): correct ADR-026 or drop it — M13c (ADR-026 amended by ADR-035)
- [x] M8d implementation plan
- [x] M8d — done 2026-10-03 (ADR-027, research/m8d-portal-details.md): Safely Remove with Portals, Portal backoff, Enter on several items; the M8 carry-over batches are done
- [ ] U2, U7 — user checks
Carry-overs from the M8d review (minors):
- [x] A release can be undone by watcher events or a refresh queued just before it (one vetoed eject; a retry works) — M13a
- [x] A Portal whose folder is missing but whose parent is on the stick registers no notice (the parent watcher vetoes) — M13a
- [x] Virtual sources: MoveItems path and the Recycle Bin cursor (Move shown, nothing done) — ruled, ADR-035
- [x] Dead _reconcileDeferred branch in RefreshSpecialIcons; reuse the removal notice when the folder is unchanged — M13c (removal notice reuse ruled, ADR-035)
- [x] **v1.1.1 released and installed 2026-10-03** (tag v1.1.1): M8c + M8d
- [x] **Silent Setup upgrades leave NeoFences stopped** (T13 failed, verbose Setup log): Setup kills the app, runs the
  `--veloapp-install` hook, force-stops anything left in the install folder, finishes — and never starts the app, so
  fences are gone and desktop icons stay hidden until the next sign-in (hard rule 2). Fix idea: the install hook shows
  the icons, and starts NeoFences after Setup ends (a launcher outside the install folder, since the force-stop runs after
  the hook). Not yet checked: an interactive (double-clicked) Setup upgrade — v1.1.2 (ADR-028; checked with 1.1.2-beta.1)
- [x] v1.1.2 start after Setup — done 2026-10-03
- [x] **v1.1.2 released and installed 2026-10-03** (tag v1.1.2): silent upgrade from 1.1.2-beta.1 — NeoFences started by itself 3.9 s after Setup (Q11, silent)
Carry-overs from the v1.1.2 review (minors / declined):
- [ ] Elevated or other-user Setup: the started copy inherits Setup's token (an elevated NeoFences loses drag-drop from Explorer)
- [ ] Check once whether VELOPACK_* variables reach NeoFences (and apps opened from fences) through the install hook
- [ ] Q11 with a double-clicked Setup — user check

## M9 — Fence tabs (v1.2, spec 2026-10-03-fence-tabs-design)
User choices 2026-10-03: combine fences into one box (Fences 6), drag a tab out to split, a per-tab accent colour.
- [x] M9 spec and implementation plan
- [x] M9 — done 2026-10-03 (ADR-029, research/m9-fence-tabs.md)
- [ ] V10 (power cut), V13 — user checks
- [x] **v1.2.0 released and installed 2026-10-03** (tag v1.2.0): fence tabs; restarted itself 3.9 s after Setup; config unchanged apart from the new `tabs` fields
Carry-overs from the M9 review (minors):
- [x] Mixed DPI: a detached tab keeps the box's pixel size (not its DIP size); the ghost uses the source monitor's scale — ruled, ADR-035
- [x] SchemaVersion bump for tabs (an older build saving the config drops the tab fields) — M13a
- [x] Click-to-expand on a rolled-up box: a header click switches the tab but does not expand — M13c
- [x] A minimum header width (~48 DIP) with many tabs in a narrow box; an insertion caret for reorders — ruled, ADR-035
- [x] Reorder to the same index / the same colour return new instances; a repaired heir's old rect can return — M13c (the heir's rect ruled, ADR-035)

## M10 — Snapshots (v1.3, spec 2026-10-03-snapshots-design)
User choices 2026-10-03: undo a messy desktop; newer items stay put; tray + Settings; "Before restore" first.
- [x] M10 spec and implementation plan
- [x] M10 — done 2026-10-03 (ADR-030, research/m10-snapshots.md)
- [ ] W9 — user check
- [x] **v1.3.0 released and installed 2026-10-03** (tag v1.3.0): snapshots; restarted itself 4.1 s after Setup; config unchanged
Carry-overs from the M10 review (minors):
- [x] Failure notices only in the tray balloon (Settings-started actions, Do Not Disturb); NIIF_WARNING for failures — M13c
- [x] Tray snapshot names: blank, tab, newline and very long names; the restore submenu can start with a separator — M13c
- [x] Refuse a snapshot with a newer schemaVersion (a rename would drop unknown fields); size cap per file in List — M13a
- [x] Unfenced newcomers in Restore rely on HashSet order; a failed write leaves *.json.tmp — M13a
- [x] Settings: delete dialog owner, focus after rename / list rebuild, double-click only on rows, "More in Settings…" scrolls to the card — M13c
- [x] Test gaps: restore with a Portal, newcomers across fences, case-insensitive refs, Save failure, null-field files — M13a
- [x] Right-clicking a single fence's title shows Windows' system menu (Move / Size / Close) instead of the fence menu — M13c
Carry-overs from the M11 review (minors):
- [x] Rules editor: an edit is lost when its rule is deleted or the fence menu starts another; its fence list goes stale while open; ticking a rule loses keyboard focus — M13b
- [x] Item facts: scan only the start of a .url; expand %VARIABLES% in raw shortcut targets; detect steam:// in a shortcut's arguments — M13b
- [x] Re-id duplicate rule ids on load — M13a

## M11 — Rules auto-sort (v1.4, spec 2026-10-03-rules-design)
User choices 2026-10-03: file new items + Apply rules now; type, game, name, date, size; Settings → Rules list; a game folder of my own.
- [x] M11 spec and implementation plan
- [x] M11 — done 2026-10-03 (ADR-031, research/m11-rules.md)
- [x] **v1.4.0 released and installed 2026-10-03** (tag v1.4.0): rules auto-sort; restarted itself 4.1 s after Setup; config unchanged apart from the new empty `rules`

## M12 — Game Library (v1.5, spec 2026-10-03-game-library-design)
User choices 2026-10-03: every game in one place; launchers, Xbox, my game folders, Desktop game shortcuts; posters where on disk; one A–Z fence.
- [x] M12 spec and implementation plan
- [x] M12 — done 2026-10-04 (ADR-032, research/m12-game-library.md)
- [x] **v1.5.0 released and installed 2026-10-04** (tag v1.5.0): game library; restarted itself 4.1 s after Setup; config unchanged apart from the new default `library` settings
Carry-overs from the M12 review (minors):
- [x] Drag over a box whose front tab is the library: hovering another tab's header does not switch to it; a refused drop may leave the drag image — M13b
- [x] A failed shortcut write or delete leaves the index out of step; one bad Epic/Xbox entry marks the whole source unreadable — M13a
- [x] A Desktop shortcut into a launcher's install sub-folder shows twice (merge by containment); Desktop-shortcut games lose a custom icon / run-as-admin — M13b
- [x] 300+ games: program search on every scan, a root write watcher on game folders, posters decoded at 300 px — M13b
- [x] Epic DLC / Unreal Engine listed; Xbox "ms-resource:" names fall back to the package name — M13b
- [x] Tray "New Game Library fence" while quick-hidden stays invisible; Directory.Exists on the UI thread when creating the fence / opening an install folder — M13a
- [x] Status line shows raw scan keys; Settings can list a hidden game twice while a source is unreadable (a scan finishing after exit: fixed in M13a) — M13b

## M13 — v1.6 carry-overs (user choice 2026-10-04: 3 batches, each released)
- [x] M13a plan (data safety → v1.6.0)
- [x] M13a — done 2026-10-04 (ADR-033, research/m13a-data-safety.md)
- [x] **v1.6.0 released and installed 2026-10-04** (tag v1.6.0): data safety; restarted itself 4.1 s after Setup; config now schema 2, otherwise unchanged
Carry-overs from the M13a review (minors, for M13b):
- [x] A locked Epic manifest drops that game for one scan; a folder vanishing mid-scan marks all of Epic unreadable — M13b
- [x] Power cut mid library write: temp shortcuts and unindexed files orphaned (sweep NeoFences' own temp names); write index.json through — M13b
- [x] Settle keeps an entry whose hand-deleted file could not be re-created (fix or reword); a snapshot temp file after a failed write (disk full) — M13b
- [x] Keep a one-time pre-v2 copy of config.json (the last schema-1 backup rotates out after 10 save-days) — M13b
- [x] A Portal listing or library scan in flight can veto one eject (its own notice handle) — M13b
- [x] Tidy: unused dispatcher local, nested block in XboxGame, "right away if refused" comments, library stop before the session-end return — M13b
- [x] M13b — done 2026-10-04 (ADR-034, research/m13b-library-polish.md)
- [x] **v1.6.1 released and installed 2026-10-04** (tag v1.6.1): Game Library and rules polish; restarted itself after Setup; config unchanged
Carry-overs from the M13b review (minors, for M13c):
- [x] Desktop shortcut changes (deleted, renamed, retargeted) do not rescan the library — M13c (a retarget in place waits for Refresh library, ADR-035)
- [x] In-flight eject entries removed by handle value only (TryRemove with the value) — M13c
- [x] KeepPreviousSchemaCopy re-checks on every save when no schema-1 file exists (once per process) — M13c
- [x] The drag handler hit-tests twice on every fence; Settings refreshes rebuild the rules list (row focus, an open dropdown) — M13c
- [x] An edit is lost if its rule disappears underneath (a snapshot restore); Take(64) does not bound a .url without line breaks — M13c
- [x] M13c — done 2026-10-04 (ADR-035, research/m13c-ux-polish.md)
- [x] **v1.6.2 released and installed 2026-10-04** (tag v1.6.2): fence and Settings UX; restarted itself after Setup; config unchanged
Carry-overs from the M13c review (minors):
- [x] Hotkey status messages ("Saved. Peek: …", "… is not active") still use US key names — M16
- [x] Hotkey label does not follow a keyboard layout switch while Settings is open (WM_INPUTLANGCHANGE) — M16
- [x] Desktop changes that arrive through ReconcileDesktop (game-mode flood, Explorer restart, watcher overflow) do not rescan the library — M16
- [x] One drag frame over a Library tab header still shows drop feedback (re-check AcceptsDrops after the hit-test) — M16
- [x] .url reads allocate a 128 KB buffer each (ArrayPool); a 60-character tray label can split an emoji's surrogate pair — M16

Tasks for M2–M8 are broken down when the previous milestone closes.

## M14 — Appearance (v1.7, spec 2026-10-04-appearance-design)
- [x] M14 — done 2026-10-04 (ADR-036, research/m14-appearance.md)
- [x] **v1.7.0 released and installed 2026-10-04** (tag v1.7.0): Appearance; restarted itself after Setup; config upgraded to schema 3 with a pre-schema-3 copy kept
Carry-overs from the M14 review (minors):
- [x] Colour → Custom… stays ticked after Cancel — M16
- [x] A detached monitor with an empty rect could shift Wallpaper Engine's MonitorN mapping (verify with two monitors) — M16
- [x] Settings font box is blank when the global font was removed (the name is kept) — M16
- [x] Fence menu → Title font → Font builds ~300 items on first open (UI thread); brushes rebuilt on every restyle — M16
- [x] Weak title contrast with some custom colours (Accent edge on the light veil); a font-size change mid roll-up can leave the bar 4 px off — M16
- [ ] AA checks by hand (AA3 light mode, AA11–AA13 wallpaper changes and two monitors, AA15 a removed font, AA17 a game)

## M15 — Search palette (v1.8, spec 2026-10-04-search-palette-design) — parked
- [ ] M15 — built, reviewed and fixed on branch `m15-search-palette` (tip d7e106b: 446 tests; ADR-037 and the results are on the branch); **parked 2026-10-04 by user choice** (not needed yet; PowerToys Command Palette covers general search). Resume: rebase on main, branch check with the user's go, merge, release.
- [ ] If resumed: guard against PowerToys shortcuts (PowerToys' keyboard hook swallows combinations before RegisterHotKey; read its settings and refuse/warn), and an "Off" choice for the search hotkey
- [ ] Later, small: type-to-jump inside a focused fence (typing selects the first matching item, like Explorer); fence menu → "Search this fence…" (the palette scoped to one fence)
- [ ] v2 idea: NeoFences results inside PowerToys Command Palette (an extension; needs an ADR: Microsoft's extension NuGet + an MSIX-packaged helper)

## M16 — v1.7.1 polish (user choice 2026-10-04)
- [x] M16 — done 2026-10-04 (ADR-038, research/m16-polish.md)
- [x] **v1.7.1 released and installed 2026-10-04** (tag v1.7.1): polish, one title font for all fences; restarted itself after Setup; config unchanged except dropped per-fence fonts
Carry-overs from the M16 review (minors):
- [ ] A redundant library scan after the startup reconcile or deferred shell work (skip when one is running or pending)
- [ ] Colour → Custom… stays ticked when Windows' colour picker itself throws (rare)
- [ ] AA19 (Custom… → Cancel tick) and AA21–AA24 by hand

## M17 — Public releases and auto-update (v1.8, spec 2026-10-04-public-releases-design)
- [x] Repository public, history scrubbed, README (2026-10-04, with the user's go)
- [x] M17 — done 2026-10-04 (ADR-039, research/m17-public-releases.md)
- [x] **v1.8.0 released 2026-10-04: the first public release** (tag v1.8.0; CI green, 438 tests; draft installed and checked here — restarted itself in 4 s, config upgraded to schema 4; published on the Releases page)
- [ ] AC checks on a friend's PC (AC1, AC6–AC8)
Carry-overs from the M17 review (minors):
- [ ] NEOFENCES_UPDATE_SOURCE pointing at a missing folder silently uses GitHub (and logs "local folder"): warn instead
- [ ] Check whether `vpk download github` fetches draft releases (a re-run release workflow could refuse "version already packed")
