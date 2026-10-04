# NeoFences — Snapshots (M10) design

**Date:** 2026-10-03 · **Status:** draft for review · **Target:** v1.3.0 · **Parity:** Fences "Snapshots"

## 1. Goal and user choices

Save a good desktop arrangement under a name and put it back when things got shuffled (user, 2026-10-03):

| Question | User choice |
|---|---|
| Purpose | Undo a messy desktop (not switching between setups). |
| Items newer than the snapshot | Stay in their current fence if it still exists after the restore; otherwise the Inbox. |
| Where | Tray menu ("Take snapshot", "Restore snapshot ▸") and a Snapshots section in Settings (restore, rename, delete). Every restore first saves an automatic "Before restore" snapshot. |
| Approach | One file per snapshot in the data folder; Core take/restore functions, tested. |

Success: one click saves the arrangement; a restore brings back fences, their places, their tabs and which icons are in
which fence; anything that changed meanwhile is handled as chosen above; a restore can always be undone; nothing
touches files or Settings; a damaged snapshot or a power cut never breaks NeoFences.

Non-goals: switching between setups as a mode, scheduled/automatic snapshots (the daily config backups exist),
restoring Settings, sharing snapshots between PCs (the files can be copied by hand, nothing more).

## 2. What a snapshot is and where it lives

- **Folder:** `%LOCALAPPDATA%\NeoFences\snapshots\`. One file per snapshot: `snapshot-yyyy-MM-dd_HH-mm-ss.json` (a
  counter suffix if two land in the same second). The automatic one is always `before-restore.json`, replaced on each
  restore.
- **Content** (camelCase JSON, the config's own format and converters):
  `schemaVersion`, `name` (any text, shown in the UI), `takenAt` (local time with offset), `fences` (every fence with
  every field: id, title, source, items, sort, icon size, labels, tabs, active tab, colour, roll-up, lock), `layouts`
  (every monitor setup: monitors and fence places), `lastLayoutFingerprint`. No Settings; no files.
- **Core `SnapshotStore(directory)`** (file I/O, like `ConfigStore`):
  - `List()` → entries (path, name, takenAt), newest first; a file that cannot be read or parsed is skipped and reported
    (`Problems`), never thrown.
  - `Save(snapshot, fileName?)` → temp file, then swapped into place (`File.Move` overwrite / `File.Replace`), so a power cut
    never leaves a half-written snapshot; returns the path, or null on an I/O failure (reported).
  - `Load(path)` → the snapshot, or null when damaged (reported).
  - `Rename(path, newName)` → rewrites the name inside the file (same safe write).
  - Deleting is the App's job: the file goes to the Recycle Bin through the shell (never a permanent delete).
- **Core `Snapshots.Take(config, name, now)`** → the snapshot of the current arrangement (fences and layouts copied).

## 3. What restore does

**Core `Snapshots.Restore(current, snapshot, desktopNow)`** → the new config (pure, tested first):

1. **Fences** become the snapshot's (all fields). Fences made after the snapshot are gone; Portal folders are never
   touched. The Inbox is always present (the normalizer adds one if a damaged snapshot lacks it).
2. **Icons the snapshot knows** go back to their saved fence and position when they are on the desktop now
   (`desktopNow`, the shell's listing); icons deleted since are dropped from the fences.
3. **Icons the snapshot does not know** (on the desktop now, in no restored fence) stay in their current fence when that
   fence id exists after the restore (appended at its end), else go to the Inbox (end), in their current order.
4. **Places:** for every monitor setup in the snapshot, its layout replaces the current one; setups the snapshot never
   saw keep their current places (the layout engine drops places of fences that no longer exist).
   `lastLayoutFingerprint` stays the current one.
5. **Settings** are the current ones, untouched.
6. The result goes through `ConfigNormalizer.Normalize` (tab repair, one Inbox, no duplicates).

**In the App:** "Before restore" is written first — if that fails, the restore does not happen (a message says why);
the desktop listing must be complete (no unavailable Desktop folder), else the restore is refused with a message;
then one config change, saved at once, and the windows rebuilt as after a tab change (`SyncBoxes`, layout, items,
Portals re-listed).

## 4. UI

- **Tray menu:** "Take snapshot" (saves at once, named "Snapshot 3 Oct 22:45"; a tray balloon confirms) and
  "Restore snapshot ▸" with the 10 newest by name, then "Before restore" when present, then "More in Settings…".
  No confirmation: "Before restore" undoes a restore.
- **Settings → Snapshots card:** a list (name, date and time), buttons **Take snapshot**, **Restore**, **Rename**
  (in place), **Delete** (Recycle Bin), and "Open snapshots folder". Keyboard and Narrator usable (list items named
  "name, date").

## 5. Reliability and hard rules

- Snapshot writes are temp-then-swap; damaged snapshots are skipped and logged; a restore is one edit and one save,
  after "Before restore" was written.
- Hard rule 1: nothing touches user files; a deleted snapshot file goes to the Recycle Bin. Hard rule 7: snapshot I/O
  failures are reported and degrade the feature only.
- Game mode: no effect (snapshots are taken and restored only on request).

## 6. Testing

- **Core (xUnit, test-first):** Take/Restore round trip (fences, tabs, colours, roll-up, lock, places); known items back
  in place; deleted items dropped; new items stay in their surviving fence / go to the Inbox; Settings kept; layouts
  merged by setup; a snapshot without an Inbox repaired; `SnapshotStore` save/list/load/rename, newest first, a damaged
  file skipped, a leftover temp file ignored, two snapshots in the same second.
- **Live smoke (with consent; installed copy restored):** take a snapshot; move a fence, merge two fences into tabs,
  add a desktop file; restore → the arrangement is back and the new file stays put; restore "Before restore" → the
  shuffled state is back; delete a snapshot → Recycle Bin.
- **Checklist section W.**

## 7. Delivery

Milestone **M10 — Snapshots**: prototype → live smoke → plan → approval → inline execution → final review → merge →
release **v1.3.0**. ADR-030 records the decisions.
