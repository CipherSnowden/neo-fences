# M18 — code map for the virtual-items pivot (2026-10-04)

Read-only survey of what the pivot (ADR-040, docs/PIVOT-2026-10-04.md) removes, keeps and changes. Line numbers as of
commit a634b5b (0.8.0).

## Surprises that shape the design
- **The Game Library runs on the Portal code**: `PortalState` (App, 248 lines) lists `AppPaths.LibraryDirectory` with
  `FolderWatcher`/`DeviceRemovalNotice`/`WatcherBackoff`; rendering in `FenceHost.ShowPortal` (Library branch). Portals
  cannot simply be deleted — `PortalState` shrinks to a Library lister (drop browse/back/breadcrumb).
- **The Library reads the Desktop**: `GameScanners.cs:383` calls `DesktopItems.Enumerate()` (Desktop-shortcut games),
  `:400/403` call `Rules.LauncherOf` (keep: move it out of Rules.cs); `FenceHost.Library.OnDesktopShortcutChange` is fed by
  the desktop watcher — without it, the Library needs its own trigger (its periodic scan / own watcher).
- **Shift+right-click keeps Windows' item menu**: keep `ShellItemMenu` + `DesktopNamespace.GetUIObject` (19-37).
- **Labels**: `IconLoader` always overwrites the label with the shell display name — a virtual item's own name must win.

## Remove (≈1,200 src lines + ≈990 test lines / 67 tests)
- Core: `Membership/FenceMembership.cs` (keep `CreateFence` logic), `DesktopChange.cs`, `RememberedPlacement.cs`,
  `Model/Rules.cs` (move `LauncherOf`), `Model/FileNames.cs`.
- Shell: `DesktopWatcher.cs`, `ShellFileOps.cs`, `ItemFactsReader.cs`; optional `SpecialIconNotifications.cs` (keep only
  if Recycle Bin items need the full/empty icon refresh).
- App: `SettingsWindow.Rules.cs`.
- Tests: Membership/{FenceMembership, DragDrop, SafeSave, ReconcileRecovery, DesktopChange}Tests, Model/{Rules, Portal,
  FileNames}Tests, 2 reconcile tests in M8cCoreTests.

## Shrink / rewrite
- `ShellDragDrop.cs` (459): keep `TryDrag` (drag out) and `DroppedFiles` (CF_HDROP); replace `FenceDropTarget` (which
  forwarded to Desktop/folder shell drop targets = real file moves) with an item-creating target.
- `DesktopNamespace.cs`: keep `GetUIObject`; drop drop-targets / desktop-folder checks. `DesktopItems.cs`: keep
  `Enumerate` (Library), special refs. `FolderItems.cs`: keep `FolderWatcher` (later: target watching), `TryList` (Library).
- `Snapshots.Restore` (55-87): rewrite to "restore fences and items as saved"; `FenceHost.RestoreSnapshot` (1318-1354)
  no longer needs a desktop listing.
- `ConfigNormalizer` (127): drop Inbox/Portal/Rules repair and the global one-fence-per-item dedup (26-63, 70, 97-101).

## Modify (main surgery)
- `FenceHost.cs` (1807): remove takeover/reconcile/watcher/rules/portal/item-file-action ranges (fields 39-84; Start
  131-142; 299-518; 532-636; 748-752; 775-998 parts; 1018-1029; settings 1170-1283 parts; 1502-1535); keep `OpenItem`
  (520-530), icon-hide (`SetIconsHidden`, 1032-1058, re-purposed), exit/restore paths (178-221).
- `FenceWindow.xaml.cs` (1473): remove Portal/Takeover/rename-in-place/recycle/drop-into-container (≈200 lines);
  `SetItems` (632-666) takes item records; keys Del/F2/Backspace re-mapped (Del = remove item).
- `FenceWindow.xaml`: drop Back button, Takeover banner, New Portal / Open folder / Rules / "items go to the Inbox" menu
  texts, `LabelBox`, `PortalMessage`.
- `FenceItemView.cs` (69): from an item record (id, target, name/icon overrides); drop IsEditing/EditName.
- `IconLoader.cs` (67): custom name/icon precedence; custom icon sources.
- Model: `Fence.Items` strings → item records; `FenceSource` loses Desktop/Portal (Library stays); `IsInbox`, `Rules`,
  `Settings.Takeover`/`TakeoverPromptAnswered` go; `HideDesktopIcons` (RunState `IconsHidden`) stays as the option.
- Settings: Takeover card → "Hide desktop icons while NeoFences runs"; Rules section removed.
- `Watchdog.cs`: rename the takeover marker to the hide-icons marker.

## How items open and load today (to keep)
Double-click → `OpenRequested` → `FenceHost.OpenItem` → `ShellWorker.RunAlone` (STA) → `ShellItems.TryOpen`
(`ProcessStartInfo{UseShellExecute, ErrorDialog}`, `::{GUID}` → `explorer shell:::{GUID}`). Icons/labels:
`IconLoader.Request` → 2 STA workers → `ShellItems.TryGetDisplayName` / `TryGetImage` → frozen BitmapSource on the UI thread.
