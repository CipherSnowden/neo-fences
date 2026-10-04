# M18 — Virtual items (0.9.0): build, final review and live check

**Status (2026-10-04, ~21:35):** Tasks 0–6 of `docs/superpowers/plans/2026-10-04-m18-virtual-items.md` done on branch
`m18-virtual-items` (worktree `F:\projects\neo_fences-m18`); final whole-branch review done and its fix pass committed;
**Task 7 live check in progress** (most rows PASS, the rest listed below). Not merged, not released.

## Handoff — read this first in the next session

1. Worktree `F:\projects\neo_fences-m18`, branch `m18-virtual-items`, base `e3dbabe` (main has only the claim on top of
   the plan). Build: `dotnet build` (0 warnings), `dotnet test` → 404 Core tests pass.
2. Ledger (rulings, deferred minors, fixes): `F:\projects\neo_fences-m18\.superpowers\sdd\2026-10-04-m18-virtual-items\progress.md`
   (git-ignored). Final review report: summarised under "Final review" below.
3. **Live check state on this PC right now:**
   - The **test build** runs: `src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe` (3 fences: "Fence", a second
     "New fence", the Game Library). Its data `%LOCALAPPDATA%\NeoFences\` was created by the test (nothing was
     installed before; delete it at cleanup with `m18-reset.ps1`).
   - Test files: `%USERPROFILE%\Desktop\NeoFences-test\` (`b.txt`, `run.cmd`, `sub\`, `out\`), the desktop file
     `%USERPROFILE%\Desktop\NeoFences-test-desk.txt`, and `G:\NeoFences-test\g.txt` on the pendrive (LOCO_DUCK).
   - The **pendrive was ejected** by the test (AD16): it must be unplugged and plugged back in (asked of the user).
   - "Hide desktop icons" is back **off**; HideIcons = 0.
4. **Scripts** (Claude's scratchpad of this session,
   `C:\Users\cipher\AppData\Local\Temp\claude\F--projects-neo-fences\af2208d4-5556-4bfa-843d-b88fa06ac897\scratchpad\`):
   `m18-live-helpers.ps1` (helpers), `m18-live.ps1` (AD1–AD31 automated run from a clean state), `m18-live3.ps1`
   (Properties via the item menu, AD10–AD12, AD23), `m18-live4.ps1` (AD16 eject, AD21 hide + kill, AD25), `m18-reset.ps1`
   (guarded cleanup), `m18-restart.ps1`, `m18-renameflash.ps1`. Run with
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <script>`. Write/edit scripts **only with the Write/Edit
   tools** (shell-escaped edits lost backslashes and once emptied the scratchpad — memory `feedback-desktop-automation`).
5. **Remaining live rows** (do automatically where possible, ask the user only for physical steps; take screenshots):
   AD3 (desktop icon dragged onto a fence — UIA cannot find desktop icons here; drag from the icon's position, e.g.
   found with `DesktopIcons.TryIsIconAt` or a screenshot), AD5 (browser link drag — Firefox), AD11 (icon from a file:
   the Change Icon dialog opened but the script's OK did not set the icon), AD13 (Run as administrator → UAC: user
   clicks No), AD22 + AD32 (snapshot take / remove item with picture / restart / restore via tray), AD23 (Delete fence:
   the script found the confirmation but the fence was not deleted — check by hand), AD28 (pull the stick without
   Safely remove, re-plug, rename g.txt → followed), AD29 (NeoFences started while G: was absent → re-plug → item OK with
   its real icon). Then cleanup (`m18-reset.ps1`, restore minimized windows, refocus Windows Terminal), write the
   final results here, commit `docs: added the M18 live check results`.
6. **After the live check:** superpowers:finishing-a-development-branch — ask before merging to main; then release
   0.9.0 (bump `<Version>` in `NeoFences.App.csproj`, push main → CI green → tag `v0.9.0` → draft → install check →
   publish; every outward step asked); session end: SESSION-LOG entry, ROADMAP ticks (M18), PIVOT "Next steps", hub
   refresh (`url` from `CLAUDE.local.md`); delete the plan's `.superpowers/sdd` workspace.

## Live check results so far (TEST-CHECKLIST AD)

| ID | Result | Notes |
|---|---|---|
| AD1 | PASS | one empty fence "Fence", hint shown, native icons visible (HideIcons 0) |
| AD2 | PASS | Explorer drop: link cursor, item at the drop point, file byte-identical, no dialog (by hand and scripted) |
| AD4 | PASS | the same drop again adds nothing |
| AD5 | PASS (typed) | `www.github.com` → https://www.github.com, label "github.com", browser icon; browser drag still to do |
| AD6 | PASS | Add item… folder: "Found (a folder)", Arguments greyed |
| AD7 | PASS | moved to a second fence; Ctrl+drag duplicated back |
| AD8 | PASS after fix | drag-out made nothing → fixed (shell item array data object); copy in `out`, original byte-identical |
| AD9 | PASS | Del removes one item; Ctrl+A Del asks once; files untouched |
| AD10 | PASS | Properties (from the item menu): own name shown as the label |
| AD12 | PASS | picture icon copied into `icons\` at 256×171 |
| AD14 | PASS | rename followed by both items; no Missing flash after the fix |
| AD15 | PASS | recycled → `"Missing"` logged; restored from the Recycle Bin → `"Ok"` |
| AD16 | PASS | with a G: item watched, Safely remove (CM eject) was **allowed**; item `"Unavailable"` |
| AD17 | PASS | opening a missing item shows NeoFences' question with Locate… |
| AD18 | PASS | Shift+right-click: Windows' menu, first line grey "Windows menu — acts on the real file" |
| AD19 | PASS | item menu: Open · Run as administrator · Open file location · Copy path · Properties… · Remove from fence · grey Shift hint |
| AD20 | PASS | Refresh: no errors |
| AD21 | PASS | Hide icons on → HideIcons 1, marker; forced kill → icons back, watchdog restarted NeoFences, setting kept |
| AD24 | PASS | Game Library: 12 tiles; a game dragged into a fence became an item of its library shortcut |
| AD25 | PASS | killed right after an add: the item is there after the restart, items.json intact |
| AD26 | PASS | unreachable share: `"Unavailable"` 1 s after the add; fences responsive throughout |
| AD27 | PASS | editor-style save (rename away, temp onto the name, delete): target kept, never Missing |
| AD30 | PASS | Sort by with the share item: fences stayed responsive |
| AD31 | PASS | an item dropped onto the desktop Recycle Bin: nothing deleted |
| gesture | PASS after fix | double-clicking a desktop icon quick-hid fences and icons (see findings) |

## Findings of the live check (all fixed on the branch)

- **Double-clicking a desktop icon quick-hid everything** (pre-existing M5 check, now visible because icons stay shown):
  UI Automation reports the live web wallpaper's Chromium page at every desktop point on this PC, so "is an icon here"
  was always no. Now `DesktopIcons.TryIsIconAt` asks Explorer's folder view for icon positions and spacing (the list view
  found by class; `IShellView.GetWindow` is refused as an input-synchronous call). Commit `49bc962`.
- **Drag-out to Explorer did nothing**: `SHCreateDataObject` without a parent folder gave a data object Explorer ignored;
  now `SHCreateShellItemArrayFromIDLists` + `BHID_DataObject`. Commit `5880aba`.
- **Drops read as shell items** (Explorer, desktop): real paths, special items as `::{GUID}`, zip/phone contents skipped
  without extraction. Commit `b66846a`.
- **A rename flashed Missing for a moment**: folder-change checks now wait while renames settle. Commits `b66ea45`, `8d06251`.

## Final review (Opus, whole branch e3dbabe..43f0610) and the fix pass

Verdict "with fixes"; focus 1 (drops never move the source) and hard rule 1 hold. Fixed (commits `30520b9`, `c6d2e52`,
`250be72`): C1 editor saves re-pointed items at temp files (Core `Renames.Settle`, 5 tests); I1 item lists pruned at save
after a session on a fallback config (pruning removed, ADR-041 amended); I2 Sort by on the UI thread; I3 stopped watchers
never re-armed; I4 watchers woke on every write; I5 returning targets kept placeholder icons; I6 root probes with a
timeout for every drive, newest check wins; I7 snapshot pictures deleted at start; a crash when the Missing dialog's
fence window had closed; dialogs now in the taskbar. Deferred minors (for M19) are in the ledger and below.

Deferred minors: arming watchers cannot be released for a removal; UI-thread shell calls on possibly unreachable targets
(drag-out parse, Shift+right-click menu, Properties start paths, Locate's folder walk); `PathPicker` / `DesktopNamespace`
COM objects not released; a faulted arming batch is lost silently; per-target dictionaries never shrink; Properties OK
during "Checking…" uses the previous folder flag; icon loaders may block on dead-share items at start.
