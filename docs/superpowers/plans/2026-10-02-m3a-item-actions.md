# M3a — Item Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let people act on desktop items in fences the way they do on the desktop:
- Windows' own right-click menu;
- F2 rename;
- Del to the Recycle Bin (always, even Shift+Del);
- Windows' own message when something cannot open;
- documents that stay in their fence when an editor saves them by replacing the file.

**Architecture:**
- `NeoFences.Core`: a safe-save memory in `FenceMembership.Apply` (`RecentRemoval`, 5 s).
- `NeoFences.Shell`:
  - `ShellItemMenu`: `IContextMenu` from the desktop `IShellFolder`, with `IContextMenu2/3` message forwarding;
  - `ShellFileOps`: `IFileOperation` recycle and rename;
  - `ShellItems.TryOpen`: now shows Windows' error UI.
- `NeoFences.App`:
  - `FenceWindow`: an in-place rename box, Del/F2/Enter keys, the item right-click menu, and UI Automation names;
  - `FenceHost`: runs the actions and keeps the safe-save memory.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335, Serilog, xUnit. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §6 (item right-click, keys, open). **Decisions:**
- ADR-002 (classic menu);
- ADR-014 (watcher);
- ADR-015 (activation);
- **ADR-016** (new, Task 5).

The user decided on 2026-10-02: split M3 into M3a and M3b, and Shift+Del also recycles.

**Pre-verified (2026-10-02):** every code block below was compiled together (0 warnings, 0 errors), and **122/122 tests pass**. The prototype then passed the Task 3 smoke on the real desktop (mouse and keyboard driven, with the user's consent):
- F2 renamed `zz-m3a-rename.txt` to `zz-m3a-renamed.txt`; the extension was kept and so was the position;
- Del sent `zz-m3a-recycle.txt` to the Recycle Bin, and it left the fence;
- right-click showed Windows' full classic menu, including the user's shell extensions;
- double-clicking a broken shortcut showed Windows' "Missing Shortcut" dialog;
- a Word-style save (rename away, hidden temp renamed on, delete) kept the position.

The prototype also found three bugs, all already fixed in the code below:
- **Visible temp name.** A Word save whose renamed-away temp file is visible arrives as a rename, which the first safe-save draft missed.
- **UI Automation search.** UIA's desktop-wide search does not reach fence windows, so the smoke searches from each fence handle.
- **Slow clicks.** Two slow single clicks are not a double-click (a smoke-script fix).

## Global Constraints

- **Hard rule 1:** NeoFences never deletes permanently. Every delete path recycles: Del, Shift+Del, and Delete in Windows' menu. If Windows cannot recycle an item, Windows itself warns and asks (`FOF_WANTNUKEWARNING`).
- **Hard rules 4 and 7:** all COM is in `NeoFences.Shell`, and every shell failure returns false or None and is logged, never thrown.
- Special items (`::{CLSID}`) are never renamed or recycled.
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- The smoke takes the mouse and keyboard for about 60 s and leaves one test file in the Recycle Bin. Ask the user first; afterwards it refocuses Windows Terminal (user request).

## Review Focus

1. **A real Word or Notepad save of a fenced document.** Expected: it stays in its fence and position. Pinned by the 7 `SafeSaveTests` (Task 1), which cover both Word variants; the smoke does it with file operations.
2. **Shift+Del, and Delete with Shift held in Windows' menu.** Expected: the Recycle Bin, never a permanent delete. The verb is intercepted in `ShellItemMenu`, and `ShellFileOps` always sets `FOFX_RECYCLEONDELETE`; live check J5.
3. **Rename to an existing name, or cancel Windows' dialog.** Expected: Windows' own conflict UI, with nothing lost. `TryRename` returns false on cancel; live check J4.
4. **A shell extension that throws or hangs inside the menu.** Expected: NeoFences survives, because the `COMException` is caught and the menu is simply not shown. A hang is Windows' own behaviour, the same as Explorer's.
5. **Rename box without focus (another app in front).** Expected: the rename is abandoned rather than left stuck. Covered by the guard in `OnLabelBoxVisibleChanged`, as in M2c.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Membership/RecentRemoval.cs`, `FenceMembership.cs` | safe-save memory | 1 |
| `src/NeoFences.Shell/ShellItemMenu.cs` | Windows' item menu | 2 |
| `src/NeoFences.Shell/ShellFileOps.cs` | recycle and rename (IFileOperation) | 2 |
| `src/NeoFences.Shell/ShellItems.cs`, `NativeMethods.txt` | open with Windows' error UI; bindings | 2 |
| `src/NeoFences.App/FenceItemView.cs`, `FenceWindow.xaml(.cs)`, `FenceHost.cs` | rename box, keys, item menu, actions | 3 |
| `docs/TEST-CHECKLIST.md` (section J), `docs/research/m3a-item-actions.md` | verification | 4 |
| `docs/DECISIONS.md` (ADR-016), `ARCHITECTURE.md`, `FEATURES.md`, `ROADMAP.md`, `SESSION-LOG.md`, hub | docs sync | 5 |

---

### Task 1: Core — safe-save memory

**Files:**
- Modify: `docs/ROADMAP.md` (claim M3a)
- Create: `src/NeoFences.Core/Membership/RecentRemoval.cs`, `tests/NeoFences.Core.Tests/Membership/SafeSaveTests.cs`
- Modify: `src/NeoFences.Core/Membership/FenceMembership.cs` (full new content below)

**Interfaces:**
- Produces:
  - `record RecentRemoval(string ItemRef, string FenceId, int Index, DateTimeOffset RemovedAt)`;
  - `FenceMembership.SafeSaveWindow` (5 s);
  - `FenceMembership.Apply(NeoFencesConfig config, DesktopChange change, IReadOnlyList<RecentRemoval> recent, DateTimeOffset now) -> (NeoFencesConfig Config, IReadOnlyList<RecentRemoval> Recent)`.

  The old `Apply(config, change)` stays.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m3a-item-actions
```
In `docs/ROADMAP.md`, append ` — [~] claimed by session 2026-10-02 m3a` to the `### M3a — …` heading line.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M3a"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Membership/SafeSaveTests.cs`:
```csharp
using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

/// <summary>
/// Editors save by replacing the file: Word renames doc.docx away (to a hidden ~WRL….tmp) and renames its hidden temp file
/// onto doc.docx; others delete and recreate it. The document must stay in its fence and position (M2b review I4).
/// </summary>
public class SafeSaveTests
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private const string Doc = Desktop + "report.docx";
    private const string Before = Desktop + "a.txt";
    private const string After = Desktop + "b.txt";
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static (NeoFencesConfig Config, Fence Work) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true };
        var work = Fence.Create("Work") with { Items = [Before, Doc, After] };
        return (new NeoFencesConfig { Fences = [inbox, work] }, work);
    }

    private static IReadOnlyList<string> ItemsOf(NeoFencesConfig config, string fenceId) => config.Fences.Single(fence => fence.Id == fenceId).Items;

    [Fact]
    public void WordSave_KeepsTheDocumentInItsFenceAndPosition()
    {
        var (config, work) = Sample();

        // doc → ~WRL0001.tmp (hidden): the watcher reports it as a delete; then ~WRD0001.tmp (hidden, never fenced) → doc.
        var (afterDelete, recent) = FenceMembership.Apply(config, new DesktopChange.Deleted(Doc), recent: [], now: Now);
        var (afterRename, _) = FenceMembership.Apply(afterDelete, new DesktopChange.Renamed(Desktop + "~WRD0001.tmp", Doc), recent, now: Now.AddMilliseconds(300));

        Assert.Equal([Before, Doc, After], ItemsOf(afterRename, work.Id));
        Assert.Empty(afterRename.Inbox.Items);
    }

    [Fact]
    public void WordSave_WithAVisibleTempName_KeepsTheDocumentInItsFenceAndPosition()
    {
        // Same save when the renamed-away original is not hidden: doc → ~WRL0001.tmp arrives as a rename, and the
        // temp file is deleted at the end.
        var (config, work) = Sample();
        const string renamedAway = Desktop + "~WRL0001.tmp";

        var (step1, recent1) = FenceMembership.Apply(config, new DesktopChange.Renamed(Doc, renamedAway), recent: [], now: Now);
        var (step2, recent2) = FenceMembership.Apply(step1, new DesktopChange.Renamed(Desktop + "~WRD0001.tmp", Doc), recent1, now: Now.AddMilliseconds(200));
        var (step3, _) = FenceMembership.Apply(step2, new DesktopChange.Deleted(renamedAway), recent2, now: Now.AddMilliseconds(400));

        Assert.Equal([Before, Doc, After], ItemsOf(step3, work.Id));
        Assert.Empty(step3.Inbox.Items);
    }

    [Fact]
    public void PlainRename_StaysPut_AndForgetsNothingUseful()
    {
        var (config, work) = Sample();
        const string renamed = Desktop + "final report.docx";

        var (afterRename, _) = FenceMembership.Apply(config, new DesktopChange.Renamed(Doc, renamed), recent: [], now: Now);

        Assert.Equal([Before, renamed, After], ItemsOf(afterRename, work.Id));
    }

    [Fact]
    public void DeleteThenCreate_KeepsTheDocumentInItsFenceAndPosition()
    {
        var (config, work) = Sample();

        var (afterDelete, recent) = FenceMembership.Apply(config, new DesktopChange.Deleted(Doc), recent: [], now: Now);
        var (afterCreate, _) = FenceMembership.Apply(afterDelete, new DesktopChange.Created(Doc), recent, now: Now.AddSeconds(1));

        Assert.Equal([Before, Doc, After], ItemsOf(afterCreate, work.Id));
    }

    [Fact]
    public void SameNameLongAfterADelete_IsANewItem_GoesToTheInbox()
    {
        var (config, work) = Sample();

        var (afterDelete, recent) = FenceMembership.Apply(config, new DesktopChange.Deleted(Doc), recent: [], now: Now);
        var (afterCreate, remaining) = FenceMembership.Apply(afterDelete, new DesktopChange.Created(Doc), recent, now: Now.Add(FenceMembership.SafeSaveWindow).AddSeconds(1));

        Assert.Equal([Before, After], ItemsOf(afterCreate, work.Id));
        Assert.Equal([Doc], afterCreate.Inbox.Items);
        Assert.Empty(remaining);
    }

    [Fact]
    public void FenceDeletedMeanwhile_GoesToTheInbox()
    {
        var (config, work) = Sample();

        var (afterDelete, recent) = FenceMembership.Apply(config, new DesktopChange.Deleted(Doc), recent: [], now: Now);
        var withoutFence = FenceMembership.DeleteFence(afterDelete, work.Id);
        var (afterCreate, _) = FenceMembership.Apply(withoutFence, new DesktopChange.Created(Doc), recent, now: Now.AddSeconds(1));

        Assert.Equal([Before, After, Doc], afterCreate.Inbox.Items);
    }

    [Fact]
    public void ReturningItem_IsRememberedOnce()
    {
        var (config, _) = Sample();

        var (afterDelete, recent) = FenceMembership.Apply(config, new DesktopChange.Deleted(Doc), recent: [], now: Now);
        var (_, remaining) = FenceMembership.Apply(afterDelete, new DesktopChange.Created(Doc), recent, now: Now.AddSeconds(1));

        Assert.Empty(remaining);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS: `CS0117: 'FenceMembership' does not contain a definition for 'SafeSaveWindow'`, `CS1739 … 'recent'`.

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Membership/RecentRemoval.cs`:
```csharp
namespace NeoFences.Core.Membership;

/// <summary>A fenced item that just disappeared: where it was, and when (safe-save memory, <see cref="FenceMembership.SafeSaveWindow"/>).</summary>
public sealed record RecentRemoval(string ItemRef, string FenceId, int Index, DateTimeOffset RemovedAt);
```
Replace `src/NeoFences.Core/Membership/FenceMembership.cs` with:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Membership;

/// <param name="Suspicious">True when part of the desktop could not be listed (an unreadable folder, or an empty listing): the affected memberships were kept, not pruned.</param>
public sealed record ReconcileReport(IReadOnlyList<string> Removed, IReadOnlyList<string> AddedToInbox, bool Suspicious = false);

/// <summary>
/// Which desktop item lives in which fence (spec §5). Pure functions: each returns a new config.
/// Item refs compare case-insensitively (<see cref="ItemRef.Comparer"/>). Portal fences hold no items.
/// </summary>
public static class FenceMembership
{
    /// <summary>
    /// Startup reconcile: drops refs that no longer exist, adopts the shell's current spelling, keeps each
    /// item in its first fence only, and appends unknown items to the Inbox in the order given.
    /// Refs under <paramref name="unavailableFolders"/> (Desktop folders the shell layer could not list: offline
    /// redirect, unmounted OneDrive) are kept, and so is everything if the listing is empty; the report is then
    /// flagged. Everything else missing from a readable folder is dropped (M2b review: no ghosts).
    /// </summary>
    public static (NeoFencesConfig Config, ReconcileReport Report) Reconcile(
        NeoFencesConfig config, IEnumerable<string> desktopItems, IReadOnlyCollection<string>? unavailableFolders = null)
    {
        var presentSpelling = new Dictionary<string, string>(ItemRef.Comparer);
        var presentOrder = new List<string>();
        foreach (var desktopItem in desktopItems)
        {
            if (presentSpelling.TryAdd(desktopItem, desktopItem)) presentOrder.Add(desktopItem);
        }

        var unlistedPrefixes = (unavailableFolders ?? []).Select(folder => folder.TrimEnd('\\') + "\\").ToList();
        bool MayStillExist(string itemRef) =>
            presentOrder.Count == 0 || unlistedPrefixes.Any(prefix => itemRef.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        var suspicious = false;

        var placed = new HashSet<string>(ItemRef.Comparer);
        var removed = new List<string>();
        var fences = config.Fences.Select(fence =>
        {
            if (fence.Source.Kind != FenceSourceKind.Desktop) return fence;
            var kept = new List<string>();
            foreach (var itemRef in fence.Items)
            {
                if (presentSpelling.TryGetValue(itemRef, out var currentSpelling))
                {
                    if (placed.Add(currentSpelling)) kept.Add(currentSpelling);
                }
                else if (MayStillExist(itemRef))
                {
                    suspicious = true;
                    if (placed.Add(itemRef)) kept.Add(itemRef);
                }
                else
                {
                    removed.Add(itemRef);
                }
            }
            return fence with { Items = kept };
        }).ToList();

        var added = presentOrder.Where(itemRef => !placed.Contains(itemRef)).ToList();
        var reconciled = config with { Fences = fences };
        if (added.Count > 0)
        {
            reconciled = reconciled.WithFence(reconciled.Inbox with { Items = [.. reconciled.Inbox.Items, .. added] });
        }
        return (reconciled, new ReconcileReport(removed, added, suspicious));
    }

    /// <summary>Applies one watcher event: created → Inbox, deleted → removed, renamed → same fence and position.</summary>
    public static NeoFencesConfig Apply(NeoFencesConfig config, DesktopChange change) => change switch
    {
        DesktopChange.Created created => AddItem(config, created.ItemRef),
        DesktopChange.Deleted deleted => RemoveItem(config, deleted.ItemRef),
        DesktopChange.Renamed renamed => RenameItem(config, renamed.OldRef, renamed.NewRef),
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown desktop change."),
    };

    /// <summary>How long a removed item's fence and position are remembered, so a replace-save puts it back (M3a).</summary>
    public static readonly TimeSpan SafeSaveWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <see cref="Apply(NeoFencesConfig, DesktopChange)"/> that also survives "safe saves": editors replace a file by
    /// deleting (or renaming away) the original and creating (or renaming a temp file onto) the same name. A fenced item
    /// that disappears is remembered for <see cref="SafeSaveWindow"/>; if the same ref comes back in that time it returns
    /// to its fence and position instead of the Inbox. Keep the returned list and pass it to the next call.
    /// </summary>
    public static (NeoFencesConfig Config, IReadOnlyList<RecentRemoval> Recent) Apply(
        NeoFencesConfig config, DesktopChange change, IReadOnlyList<RecentRemoval> recent, DateTimeOffset now)
    {
        var remembered = recent.Where(removal => now - removal.RemovedAt <= SafeSaveWindow).ToList();
        switch (change)
        {
            case DesktopChange.Deleted deleted when FindOwner(config, deleted.ItemRef) is { } deletedOwner:
                var index = deletedOwner.Items.ToList().FindIndex(existing => ItemRef.Comparer.Equals(existing, deleted.ItemRef));
                remembered.Add(new RecentRemoval(deleted.ItemRef, deletedOwner.Id, index, now));
                return (RemoveItem(config, deleted.ItemRef), remembered);
            case DesktopChange.Renamed renamed when FindOwner(config, renamed.OldRef) is { } owner:
                // Word renames the original away before renaming its temp file onto the old name: remember the old name too.
                var oldIndex = owner.Items.ToList().FindIndex(existing => ItemRef.Comparer.Equals(existing, renamed.OldRef));
                remembered.Add(new RecentRemoval(renamed.OldRef, owner.Id, oldIndex, now));
                return (RenameItem(config, renamed.OldRef, renamed.NewRef), remembered);
            case DesktopChange.Created created when FindOwner(config, created.ItemRef) is null:
                return (Return(config, created.ItemRef, remembered), remembered);
            case DesktopChange.Renamed renamed when FindOwner(config, renamed.OldRef) is null && FindOwner(config, renamed.NewRef) is null:
                return (Return(config, renamed.NewRef, remembered), remembered);
            default:
                return (Apply(config, change), remembered);
        }
    }

    /// <summary>Puts an appearing item back where it was removed from moments ago, or in the Inbox. Consumes the memory.</summary>
    private static NeoFencesConfig Return(NeoFencesConfig config, string itemRef, List<RecentRemoval> remembered)
    {
        var removal = remembered.FindLast(candidate => ItemRef.Comparer.Equals(candidate.ItemRef, itemRef));
        if (removal is null) return AddItem(config, itemRef);
        remembered.Remove(removal);
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == removal.FenceId);
        return fence is { Source.Kind: FenceSourceKind.Desktop }
            ? MoveItem(config, itemRef, fence.Id, removal.Index)
            : AddItem(config, itemRef);
    }

    /// <summary>A new desktop item appeared: goes to <paramref name="targetFenceId"/> (a drop target) or the Inbox. No-op if already fenced.</summary>
    public static NeoFencesConfig AddItem(NeoFencesConfig config, string itemRef, string? targetFenceId = null)
    {
        if (FindOwner(config, itemRef) is not null) return config;
        var target = targetFenceId is null ? config.Inbox : RequireDesktopFence(config, targetFenceId);
        return config.WithFence(target with { Items = [.. target.Items, itemRef] });
    }

    /// <summary>A desktop item was deleted.</summary>
    public static NeoFencesConfig RemoveItem(NeoFencesConfig config, string itemRef)
    {
        var owner = FindOwner(config, itemRef);
        return owner is null
            ? config
            : config.WithFence(owner with { Items = owner.Items.Where(existing => !ItemRef.Comparer.Equals(existing, itemRef)).ToList() });
    }

    /// <summary>A desktop item was renamed (including case-only renames). Unknown old ref: treated as a new item.</summary>
    public static NeoFencesConfig RenameItem(NeoFencesConfig config, string oldRef, string newRef)
    {
        var owner = FindOwner(config, oldRef);
        if (owner is null) return AddItem(config, newRef);
        // New name already fenced (an event storm can deliver its create before the rename): keep that entry only.
        if (!ItemRef.Comparer.Equals(oldRef, newRef) && FindOwner(config, newRef) is not null) return RemoveItem(config, oldRef);
        return config.WithFence(owner with
        {
            Items = owner.Items.Select(existing => ItemRef.Comparer.Equals(existing, oldRef) ? newRef : existing).ToList(),
        });
    }

    /// <summary>Moves an item to another desktop fence (or reorders within one). Index is clamped; null appends.</summary>
    public static NeoFencesConfig MoveItem(NeoFencesConfig config, string itemRef, string toFenceId, int? index = null)
    {
        var target = RequireDesktopFence(config, toFenceId);
        var withoutItem = RemoveItem(config, itemRef);
        target = withoutItem.Fences.First(fence => fence.Id == toFenceId);
        var items = target.Items.ToList();
        items.Insert(Math.Clamp(index ?? items.Count, 0, items.Count), itemRef);
        return withoutItem.WithFence(target with { Items = items });
    }

    public static (NeoFencesConfig Config, Fence Fence) CreateFence(NeoFencesConfig config, string title, FenceSource? source = null)
    {
        var fence = Fence.Create(title, source);
        return (config with { Fences = [.. config.Fences, fence] }, fence);
    }

    /// <summary>Deletes a fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
    /// <exception cref="InvalidOperationException">The fence is the Inbox.</exception>
    public static NeoFencesConfig DeleteFence(NeoFencesConfig config, string fenceId)
    {
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == fenceId)
                    ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
        if (fence.IsInbox) throw new InvalidOperationException("The Inbox cannot be deleted.");

        var remaining = config with { Fences = config.Fences.Where(candidate => candidate.Id != fenceId).ToList() };
        return remaining.WithFence(remaining.Inbox with { Items = [.. remaining.Inbox.Items, .. fence.Items] });
    }

    private static Fence? FindOwner(NeoFencesConfig config, string itemRef) =>
        config.Fences.FirstOrDefault(fence => fence.Items.Contains(itemRef, ItemRef.Comparer));

    private static Fence RequireDesktopFence(NeoFencesConfig config, string fenceId)
    {
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == fenceId)
                    ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
        return fence.Source.Kind == FenceSourceKind.Desktop
            ? fence
            : throw new ArgumentException("Portal fences hold no items; moving into them is a file operation.", nameof(fenceId));
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 122`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "feat: kept documents in their fence across editor safe-saves"
```

---

### Task 2: Shell — item menu, recycle, rename, open feedback

**Files:**
- Create: `src/NeoFences.Shell/ShellItemMenu.cs`, `src/NeoFences.Shell/ShellFileOps.cs`
- Modify (full new content): `src/NeoFences.Shell/ShellItems.cs`, `src/NeoFences.Shell/NativeMethods.txt`

**Interfaces:**
- Produces:
  - `enum ItemMenuChoice { None, Rename, Delete }`;
  - `ShellItemMenu.Show(nint ownerHandle, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended) -> ItemMenuChoice`;
  - `ShellItemMenu.HandleMenuMessage(int message, nint wParam, nint lParam, out nint result) -> bool`;
  - `ShellFileOps.TryRecycle(nint ownerHandle, IReadOnlyList<string> itemRefs) -> bool` and `ShellFileOps.TryRename(nint ownerHandle, string itemRef, string newName) -> bool`;
  - `ShellItems.TryOpen(string itemRef, nint ownerHandle) -> bool`. This signature changed: it now takes an owner.

Notes for the implementer:
- `CMIC_MASK_UNICODE` is not in CsWin32's metadata, so it is a local constant.
- The `FOF_*` and `FOFX_*` flags are members of `FILEOPERATION_FLAGS`.
- `ParseDisplayName` takes `ref uint` attributes.

- [ ] **Step 1: Bindings and files**

`src/NeoFences.Shell/NativeMethods.txt`:
```
// Win32 APIs used by NeoFences.Shell. CsWin32 generates bindings for each name.
AllowSetForegroundWindow
BITMAP
BITMAPINFO
CMF_CANRENAME
CMF_EXTENDEDVERBS
CMF_NORMAL
CMIC_MASK_PTINVOKE
CMINVOKECOMMANDINFOEX
CreatePopupMenu
CreateRoundRectRgn
DeleteObject
DestroyMenu
DIB_USAGE
DISPLAY_DEVICEW
EnumDisplayDevices
EnumDisplayMonitors
FileOperation
FILEOPERATION_FLAGS
FindWindow
FOLDERFLAGS
GCS_VERBW
GET_WINDOW_CMD
GetDC
GetDIBits
GetDpiForMonitor
GetMonitorInfo
GetObject
GetSystemMetrics
GetWindow
GetWindowLongPtr
GetWindowRect
HWND_BOTTOM
IContextMenu
IContextMenu2
IContextMenu3
IFileOperation
IFolderView2
IServiceProvider
IShellBrowser
IShellFolder
IShellItem
IShellItemImageFactory
IShellView
IShellWindows
MONITOR_DPI_TYPE
MONITORINFOEXW
MONITORINFOF_PRIMARY
RegisterWindowMessage
ReleaseDC
SET_WINDOW_POS_FLAGS
SetForegroundWindow
SetWindowLongPtr
SetWindowPos
SetWindowRgn
SHCreateItemFromParsingName
ShellWindows
SHGetDesktopFolder
SHOW_WINDOW_CMD
SID_STopLevelBrowser
SIGDN
SIIGBF
SYSTEM_METRICS_INDEX
TRACK_POPUP_MENU_FLAGS
TrackPopupMenuEx
WINDOW_EX_STYLE
WINDOW_LONG_PTR_INDEX
WINDOW_STYLE
```
`src/NeoFences.Shell/ShellItemMenu.cs`:
```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>What the user picked in Windows' item menu that NeoFences runs itself instead of the shell.</summary>
public enum ItemMenuChoice { None, Rename, Delete }

/// <summary>
/// Windows' own right-click menu for desktop items (the classic menu: Open, Open with, Send to, Properties, shell
/// extensions; the Windows 11 compact menu is Explorer-private, spec §6). Rename and Delete are handed back so
/// NeoFences renames in place and always recycles (hard rule 1, user decision 2026-10-02).
/// </summary>
public static class ShellItemMenu
{
    private const uint FirstCommandId = 1;
    private const uint LastCommandId = 0x7FFF;
    private const uint CmicMaskUnicode = 0x00004000; // not in the Win32 metadata CsWin32 reads (shobjidl.h)

    /// <summary>The menu being shown, for <see cref="HandleMenuMessage"/> (owner-drawn and lazy submenus like "Send to").</summary>
    private static IContextMenu2? _openMenu;

    /// <summary>Call from the owner window's message hook while a menu may be open; true when the menu consumed it.</summary>
    public static unsafe bool HandleMenuMessage(int message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (_openMenu is null) return false;
        const int WmInitMenuPopup = 0x0117, WmDrawItem = 0x002B, WmMeasureItem = 0x002C, WmMenuChar = 0x0120;
        if (message is not (WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar)) return false;
        try
        {
            if (_openMenu is IContextMenu3 menu3)
            {
                LRESULT handledResult;
                menu3.HandleMenuMsg2((uint)message, (WPARAM)(nuint)wParam, (LPARAM)lParam, &handledResult);
                result = handledResult;
            }
            else
            {
                _openMenu.HandleMenuMsg((uint)message, (WPARAM)(nuint)wParam, (LPARAM)lParam);
            }
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    /// <summary>
    /// Shows the menu for these items at a screen point and runs the chosen shell command.
    /// </summary>
    /// <param name="extended">Shift held: the extended menu ("Copy as path", "Open PowerShell here", …).</param>
    /// <returns>Rename/Delete for the caller to run; None when the shell ran the command, the user cancelled, or the menu could not be built.</returns>
    public static unsafe ItemMenuChoice Show(nint ownerHandle, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended)
    {
        var owner = (HWND)ownerHandle;
        var childIds = new List<nint>();
        HMENU menu = default;
        IContextMenu? contextMenu = null;
        try
        {
            PInvoke.SHGetDesktopFolder(out var desktop).ThrowOnFailure();
            // The desktop folder merges the user's and the Public Desktop, so a file name (or ::{CLSID}) is a child of it.
            foreach (var itemRef in itemRefs)
            {
                var childName = itemRef.StartsWith("::", StringComparison.Ordinal) ? itemRef : Path.GetFileName(itemRef);
                ITEMIDLIST* childId;
                uint attributes = 0;
                fixed (char* name = childName)
                {
                    desktop.ParseDisplayName(owner, null, name, null, &childId, ref attributes);
                }
                childIds.Add((nint)childId);
            }
            if (childIds.Count == 0) return ItemMenuChoice.None;

            var contextMenuId = typeof(IContextMenu).GUID;
            fixed (nint* ids = childIds.ToArray())
            {
                desktop.GetUIObjectOf(owner, (uint)childIds.Count, (ITEMIDLIST**)ids, &contextMenuId, null, out var uiObject);
                contextMenu = (IContextMenu)uiObject;
            }

            menu = PInvoke.CreatePopupMenu();
            var flags = PInvoke.CMF_NORMAL | PInvoke.CMF_CANRENAME | (extended ? PInvoke.CMF_EXTENDEDVERBS : 0);
            contextMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, flags);
            _openMenu = contextMenu as IContextMenu2;

            PInvoke.SetForegroundWindow(owner); // the menu closes on a click elsewhere only if its owner is foreground
            var command = (uint)PInvoke.TrackPopupMenuEx(menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON), screenX, screenY, owner, null).Value;
            _openMenu = null;
            if (command < FirstCommandId) return ItemMenuChoice.None;

            var verb = Verb(contextMenu, command - FirstCommandId);
            if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase)) return ItemMenuChoice.Rename;
            if (string.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase)) return ItemMenuChoice.Delete;
            Invoke(contextMenu, command - FirstCommandId, owner, screenX, screenY);
            return ItemMenuChoice.None;
        }
        catch (Exception failure) when (failure is COMException or ArgumentException or FileNotFoundException)
        {
            return ItemMenuChoice.None; // the item vanished or a shell extension failed: no menu, nothing lost
        }
        finally
        {
            _openMenu = null;
            if (!menu.IsNull) PInvoke.DestroyMenu(menu);
            foreach (var childId in childIds) Marshal.FreeCoTaskMem(childId);
            if (contextMenu is not null) Marshal.ReleaseComObject(contextMenu);
        }
    }

    private static unsafe string? Verb(IContextMenu contextMenu, uint offset)
    {
        const int Length = 64;
        var buffer = stackalloc char[Length];
        try
        {
            contextMenu.GetCommandString(offset, PInvoke.GCS_VERBW, null, (PSTR)(byte*)buffer, Length);
            return new string(buffer);
        }
        catch (COMException)
        {
            return null; // many commands have no verb
        }
    }

    private static unsafe void Invoke(IContextMenu contextMenu, uint offset, HWND owner, int screenX, int screenY)
    {
        var info = new CMINVOKECOMMANDINFOEX
        {
            cbSize = (uint)sizeof(CMINVOKECOMMANDINFOEX),
            fMask = CmicMaskUnicode | PInvoke.CMIC_MASK_PTINVOKE,
            hwnd = owner,
            lpVerb = (PCSTR)(byte*)offset,
            lpVerbW = (PCWSTR)(char*)offset,
            nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL,
            ptInvoke = new System.Drawing.Point(screenX, screenY),
        };
        PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // what the command opens may come to the front
        contextMenu.InvokeCommand((CMINVOKECOMMANDINFO*)&info);
    }
}
```
`src/NeoFences.Shell/ShellFileOps.cs`:
```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>
/// File operations through Windows' own engine (IFileOperation): its dialogs, progress, conflicts and Undo (Ctrl+Z in
/// Explorer). Deleting always goes to the Recycle Bin (hard rule 1); if Windows cannot recycle an item it asks the user.
/// </summary>
public static class ShellFileOps
{
    /// <returns>False when nothing could be started; true even if the user cancelled in Windows' dialog.</returns>
    public static bool TryRecycle(nint ownerHandle, IReadOnlyList<string> itemRefs) =>
        Run(ownerHandle, operation =>
        {
            foreach (var itemRef in itemRefs.Where(itemRef => !itemRef.StartsWith("::", StringComparison.Ordinal)))
                operation.DeleteItem(Create(itemRef), null);
        });

    /// <param name="newName">As typed. If Explorer hides this item's extension (shortcuts, or the user's setting), Windows keeps it.</param>
    public static bool TryRename(nint ownerHandle, string itemRef, string newName) =>
        !itemRef.StartsWith("::", StringComparison.Ordinal)
        && Run(ownerHandle, operation => operation.RenameItem(Create(itemRef), newName, null));

    private static bool Run(nint ownerHandle, Action<IFileOperation> queue)
    {
        IFileOperation? operation = null;
        try
        {
            operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(FileOperation).GUID)!)!;
            operation.SetOwnerWindow((HWND)ownerHandle);
            // Undo-able, recycle instead of delete, and warn before anything would be deleted permanently.
            operation.SetOperationFlags(FILEOPERATION_FLAGS.FOF_ALLOWUNDO | FILEOPERATION_FLAGS.FOF_WANTNUKEWARNING
                | FILEOPERATION_FLAGS.FOFX_RECYCLEONDELETE | FILEOPERATION_FLAGS.FOFX_ADDUNDORECORD);
            queue(operation);
            operation.PerformOperations();
            return true;
        }
        catch (Exception failure) when (failure is COMException or ArgumentException or FileNotFoundException or UnauthorizedAccessException)
        {
            return false; // includes the user cancelling Windows' dialog (HRESULT_FROM_WIN32(ERROR_CANCELLED))
        }
        finally
        {
            if (operation is not null) Marshal.ReleaseComObject(operation);
        }
    }

    private static IShellItem Create(string itemRef)
    {
        PInvoke.SHCreateItemFromParsingName(itemRef, null, out IShellItem item).ThrowOnFailure();
        return item;
    }
}
```
`src/NeoFences.Shell/ShellItems.cs`:
```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>32-bit premultiplied BGRA pixels, top-down rows (ready for WPF's Pbgra32 BitmapSource).</summary>
public sealed record ShellImage(int Width, int Height, byte[] Pixels);

/// <summary>Display names, icons/thumbnails and opening of desktop items, by item ref (parsing name).</summary>
public static class ShellItems
{
    /// <summary>The name Explorer shows ("Crysis 2", not "Crysis 2.lnk"; "Recycle Bin" in the user's language).</summary>
    public static unsafe string? TryGetDisplayName(string itemRef)
    {
        try
        {
            var item = Create(itemRef);
            item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, out var name);
            try { return name.ToString(); }
            finally { Marshal.FreeCoTaskMem((nint)name.Value); }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// The thumbnail (images, videos) or icon at <paramref name="sizePx"/>. Call from an STA thread that is not the
    /// UI thread (slow for big files). Null when the shell has nothing (the item vanished, a broken handler).
    /// </summary>
    public static unsafe ShellImage? TryGetImage(string itemRef, int sizePx)
    {
        HBITMAP bitmap = default;
        try
        {
            var factory = (IShellItemImageFactory)Create(itemRef);
            factory.GetImage(new SIZE(sizePx, sizePx), SIIGBF.SIIGBF_RESIZETOFIT, &bitmap);
            return ReadPixels(bitmap);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (!bitmap.IsNull) PInvoke.DeleteObject(bitmap);
        }
    }

    /// <summary>
    /// Opens with the default verb, like a double-click in Explorer. If it cannot, Windows shows its own message
    /// (broken shortcut, no app for this file type) owned by <paramref name="ownerHandle"/> (M2b finding H5).
    /// </summary>
    /// <returns>False when nothing could open it (no associated app, the user cancelled a UAC prompt, the item is gone).</returns>
    public static bool TryOpen(string itemRef, nint ownerHandle)
    {
        try
        {
            var startInfo = itemRef.StartsWith("::", StringComparison.Ordinal)
                ? new ProcessStartInfo("explorer.exe", "shell:" + itemRef) { UseShellExecute = true }
                : new ProcessStartInfo(itemRef) { UseShellExecute = true };
            startInfo.ErrorDialog = true;
            startInfo.ErrorDialogParentHandle = ownerHandle;
            // The user just clicked our (never-activated) fence: let the opened window come to the front.
            PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
            Process.Start(startInfo)?.Dispose();
            return true;
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    private static IShellItem Create(string itemRef)
    {
        PInvoke.SHCreateItemFromParsingName(itemRef, null, out IShellItem item).ThrowOnFailure();
        return item;
    }

    private static unsafe ShellImage? ReadPixels(HBITMAP bitmap)
    {
        BITMAP header;
        if (PInvoke.GetObject(bitmap, sizeof(BITMAP), &header) == 0 || header.bmBitsPixel != 32) return null;

        var width = header.bmWidth;
        var height = Math.Abs(header.bmHeight);
        var pixels = new byte[width * height * 4];
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // negative: top-down rows
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,  // BI_RGB
            },
        };
        var screenDc = PInvoke.GetDC(HWND.Null);
        try
        {
            fixed (byte* pixelBuffer = pixels)
            {
                if (PInvoke.GetDIBits(screenDc, bitmap, 0, (uint)height, pixelBuffer, &info, DIB_USAGE.DIB_RGB_COLORS) == 0) return null;
            }
        }
        finally
        {
            PInvoke.ReleaseDC(HWND.Null, screenDc);
        }
        // Some thumbnail handlers return opaque images with every alpha byte 0: show them opaque, not invisible.
        var hasAlpha = false;
        for (var alphaIndex = 3; alphaIndex < pixels.Length && !hasAlpha; alphaIndex += 4) hasAlpha = pixels[alphaIndex] != 0;
        if (!hasAlpha) for (var alphaIndex = 3; alphaIndex < pixels.Length; alphaIndex += 4) pixels[alphaIndex] = 255;
        return new ShellImage(width, height, pixels);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/NeoFences.Shell`
Expected: 0 warnings, 0 errors. The full solution builds only after Task 3, because `FenceHost` still calls the old `TryOpen(itemRef)`.

- [ ] **Step 3: Commit**

```powershell
git add src/NeoFences.Shell
git commit -m "feat: added windows item menu, recycle, rename and open error ui to shell"
```

---

### Task 3: App — rename box, keys, item menu, actions

**Files:** replace `src/NeoFences.App/FenceItemView.cs`, `FenceWindow.xaml`, `FenceWindow.xaml.cs`, `FenceHost.cs` with the content below.

**Interfaces:**
- Consumes: Tasks 1 and 2.
- Produces:
  - `FenceItemView.IsEditing` and `EditName`;
  - `FenceWindow.BeginItemRename(string itemRef)`;
  - events `ItemMenuRequested(IReadOnlyList<string>, int, int)`, `RecycleRequested(IReadOnlyList<string>)`, `ItemRenameRequested(string, string)`.

  Items expose `AutomationProperties.Name` = label.

- [ ] **Step 1: Replace the four files**

`src/NeoFences.App/FenceItemView.cs`:
```csharp
using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace NeoFences.App;

/// <summary>One desktop item as a fence shows it. Label and icon start as placeholders and fill in from <see cref="IconLoader"/>.</summary>
public sealed class FenceItemView(string itemRef) : INotifyPropertyChanged
{
    public string ItemRef { get; } = itemRef;

    public string Label
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); }
    } = itemRef.StartsWith("::", StringComparison.Ordinal) ? "" : Path.GetFileNameWithoutExtension(itemRef);

    public ImageSource? Icon
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
    }

    /// <summary>The label is being renamed in place (F2 or the item menu's Rename).</summary>
    public bool IsEditing
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing))); }
    }

    /// <summary>Text in the rename box.</summary>
    public string EditName
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditName))); }
    } = "";

    public event PropertyChangedEventHandler? PropertyChanged;
}
```
`src/NeoFences.App/FenceWindow.xaml`:
```xml
<Window x:Class="NeoFences.App.FenceWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NeoFences fence" Width="320" Height="220"
        WindowStyle="None" ResizeMode="CanResize" AllowsTransparency="True"
        Background="#01000000" ShowInTaskbar="False" ShowActivated="False">
    <!-- Layered window (AllowsTransparency) + accent blur, owned by Progman: ADR-011.
         The 1/255-alpha background keeps the empty area hit-testable (fully transparent pixels click through).
         Colours are DynamicResources set by ApplyTheme (light/dark follows Windows, M2c). -->
    <WindowChrome.WindowChrome>
        <WindowChrome GlassFrameThickness="0" CaptionHeight="30" ResizeBorderThickness="6"
                      CornerRadius="0" UseAeroCaptionButtons="False" />
    </WindowChrome.WindowChrome>
    <Window.Resources>
        <sys:Double x:Key="IconSize" xmlns:sys="clr-namespace:System;assembly=System.Runtime">48</sys:Double>
        <sys:Double x:Key="ItemWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">76</sys:Double>
        <Style x:Key="BannerButton" TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource FenceText}" />
            <Setter Property="Padding" Value="10,3" />
            <Setter Property="Margin" Value="0,0,6,0" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="Chrome" Background="{DynamicResource FenceHover}" CornerRadius="4" Padding="{TemplateBinding Padding}">
                            <ContentPresenter />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <!-- Thin scrollbar (spec §6): no arrows, a rounded thumb that brightens under the mouse. -->
        <Style TargetType="ScrollBar">
            <Setter Property="Width" Value="6" />
            <Setter Property="MinWidth" Value="6" />
            <Setter Property="Margin" Value="0,4,2,4" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ScrollBar">
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Border x:Name="ThumbChrome" CornerRadius="3" Background="{DynamicResource FenceScrollThumb}" />
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="ThumbChrome" Property="Background" Value="{DynamicResource FenceSubtleText}" />
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <Border CornerRadius="8" BorderBrush="{DynamicResource FenceBorder}" BorderThickness="1" Background="{DynamicResource FenceVeil}">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="30" />
                <RowDefinition Height="Auto" />
                <RowDefinition />
            </Grid.RowDefinitions>
            <TextBlock x:Name="TitleText" Foreground="{DynamicResource FenceText}" FontWeight="SemiBold" Margin="12,0"
                       VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
            <!-- Rename: replaces the title while editing. Hit-testable inside the caption area. -->
            <TextBox x:Name="TitleBox" Visibility="Collapsed" Margin="8,4" Padding="3,1" FontWeight="SemiBold"
                     VerticalContentAlignment="Center" WindowChrome.IsHitTestVisibleInChrome="True"
                     Foreground="{DynamicResource FenceText}" Background="{DynamicResource FenceHover}"
                     BorderBrush="{DynamicResource FenceBorder}" CaretBrush="{DynamicResource FenceText}" />
            <!-- First run (Inbox only): ask once whether NeoFences should take over the desktop icons. -->
            <Border x:Name="TakeoverPrompt" Grid.Row="1" Visibility="Collapsed" Background="{DynamicResource FenceHover}"
                    Margin="8,0,8,6" Padding="10,8" CornerRadius="6">
                <StackPanel>
                    <TextBlock Foreground="{DynamicResource FenceText}" TextWrapping="Wrap"
                               Text="Hide the desktop icons and keep them only in fences?" />
                    <TextBlock Foreground="{DynamicResource FenceSubtleText}" FontSize="11" TextWrapping="Wrap" Margin="0,2,0,6"
                               Text="Your files stay where they are. Turn it off any time from the right-click menu." />
                    <StackPanel Orientation="Horizontal">
                        <Button x:Name="PromptHideButton" Content="Hide them" Style="{StaticResource BannerButton}" />
                        <Button x:Name="PromptLaterButton" Content="Not now" Style="{StaticResource BannerButton}" />
                    </StackPanel>
                </StackPanel>
            </Border>
            <Border x:Name="Body" Grid.Row="2" BorderBrush="{DynamicResource FenceDivider}" BorderThickness="0,1,0,0" Background="#01000000">
                <Border.ContextMenu>
                    <ContextMenu x:Name="BodyContextMenu">
                        <MenuItem x:Name="NewFenceItem" Header="New fence" />
                        <MenuItem x:Name="RenameItem" Header="Rename fence" />
                        <MenuItem x:Name="IconSizeItem" Header="Icon size" />
                        <MenuItem x:Name="LockItem" Header="Lock position" IsCheckable="True" />
                        <MenuItem x:Name="DeleteItem" Header="Delete fence (items go to the Inbox)" />
                        <Separator />
                        <MenuItem x:Name="TakeoverItem" Header="Hide desktop icons" IsCheckable="True" />
                        <Separator />
                        <MenuItem x:Name="ExitItem" Header="Exit NeoFences" />
                    </ContextMenu>
                </Border.ContextMenu>
                <ListBox x:Name="ItemList" Background="Transparent" BorderThickness="0" Padding="4"
                         SelectionMode="Extended" ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                         ScrollViewer.VerticalScrollBarVisibility="Auto">
                    <ListBox.ItemsPanel>
                        <ItemsPanelTemplate>
                            <WrapPanel />
                        </ItemsPanelTemplate>
                    </ListBox.ItemsPanel>
                    <ListBox.ItemContainerStyle>
                        <Style TargetType="ListBoxItem">
                            <Setter Property="ToolTip" Value="{Binding Label}" />
                            <Setter Property="AutomationProperties.Name" Value="{Binding Label}" />
                            <Setter Property="FocusVisualStyle" Value="{x:Null}" />
                            <Setter Property="Template">
                                <Setter.Value>
                                    <ControlTemplate TargetType="ListBoxItem">
                                        <Border x:Name="Chrome" Background="Transparent" CornerRadius="4" Margin="2" Padding="2,4">
                                            <ContentPresenter />
                                        </Border>
                                        <ControlTemplate.Triggers>
                                            <Trigger Property="IsMouseOver" Value="True">
                                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
                                            </Trigger>
                                            <Trigger Property="IsSelected" Value="True">
                                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
                                            </Trigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </Setter.Value>
                            </Setter>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Width="{DynamicResource ItemWidth}">
                                <Image Source="{Binding Icon}" Width="{DynamicResource IconSize}" Height="{DynamicResource IconSize}"
                                       HorizontalAlignment="Center" />
                                <Grid Margin="0,4,0,0">
                                    <TextBlock x:Name="Label" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
                                               TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"
                                               MaxHeight="32" Effect="{DynamicResource LabelShadow}" />
                                    <!-- In-place rename (M3a): Enter renames through Windows, Esc cancels. -->
                                    <TextBox x:Name="LabelBox" Visibility="Collapsed" FontSize="12" TextAlignment="Center" TextWrapping="Wrap"
                                             MaxHeight="48" Text="{Binding EditName, UpdateSourceTrigger=PropertyChanged}"
                                             KeyDown="OnLabelBoxKeyDown" LostKeyboardFocus="OnLabelBoxLostFocus"
                                             IsVisibleChanged="OnLabelBoxVisibleChanged" />
                                </Grid>
                            </StackPanel>
                            <DataTemplate.Triggers>
                                <DataTrigger Binding="{Binding IsEditing}" Value="True">
                                    <Setter TargetName="LabelBox" Property="Visibility" Value="Visible" />
                                    <Setter TargetName="Label" Property="Visibility" Value="Hidden" />
                                </DataTrigger>
                            </DataTemplate.Triggers>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </Border>
        </Grid>
    </Border>
</Window>
```
`src/NeoFences.App/FenceWindow.xaml.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>One fence on the desktop. Placement and persistence are the host's job; this window reports what the user did.</summary>
public partial class FenceWindow : Window
{
    private const int WmSizing = 0x0214;
    private const int WmMoving = 0x0216;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const double CornerRadiusDips = 8;
    private const double CaptionHeightDips = 30;
    private const double ResizeBorderDips = 6;

    private static readonly (int Size, string Name)[] IconSizeNames = [(32, "Small"), (48, "Medium"), (64, "Large"), (96, "Extra large")];

    private readonly ObservableCollection<FenceItemView> _items = [];
    private readonly IconLoader _iconLoader;
    private IReadOnlyList<string> _itemRefs = [];
    private int _iconSizeDips;
    private bool _renaming;
    private DragTracker? _drag;

    public string FenceId { get; }

    public nint Handle { get; private set; }

    /// <summary>Asked while the user drags an edge or the title: returns where the window should go (snapping).</summary>
    public Func<PixelRect, SnapEdges, PixelRect>? SnapRect { get; set; }

    /// <summary>Raised after the user finishes moving or resizing, with the new physical-pixel rect.</summary>
    public event Action<FenceWindow, PixelRect>? MovedByUser;

    public event Action? NewFenceRequested;
    public event Action<bool>? TakeoverToggled;
    public event Action? ExitRequested;
    public event Action<string>? OpenRequested;
    /// <summary>The first-run question was answered: true = hide the desktop icons.</summary>
    public event Action<bool>? TakeoverPromptAnswered;
    public event Action<string>? RenameRequested;
    public event Action<int>? IconSizeRequested;
    public event Action<bool>? LockToggled;
    public event Action? DeleteRequested;
    /// <summary>Right-click (or the menu key) on items: show Windows' item menu for these refs at this screen point (px).</summary>
    public event Action<IReadOnlyList<string>, int, int>? ItemMenuRequested;
    /// <summary>Del: send these items to the Recycle Bin.</summary>
    public event Action<IReadOnlyList<string>>? RecycleRequested;
    /// <summary>An in-place rename was confirmed: item ref, new name as typed.</summary>
    public event Action<string, string>? ItemRenameRequested;

    public FenceWindow(Fence fence, bool takeoverActive, bool lightTheme, IconLoader iconLoader)
    {
        FenceId = fence.Id;
        _iconLoader = iconLoader;
        InitializeComponent();
        TitleText.Text = fence.Title;
        TakeoverItem.IsChecked = takeoverActive;
        DeleteItem.Visibility = fence.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (size, name) in IconSizeNames)
        {
            var sizeItem = new MenuItem { Header = name, Tag = size, IsCheckable = true };
            sizeItem.Click += (_, _) => IconSizeRequested?.Invoke(size);
            IconSizeItem.Items.Add(sizeItem);
        }
        NewFenceItem.Click += (_, _) => NewFenceRequested?.Invoke();
        RenameItem.Click += (_, _) => BeginRename();
        LockItem.Click += (_, _) => LockToggled?.Invoke(LockItem.IsChecked);
        DeleteItem.Click += (_, _) => DeleteRequested?.Invoke();
        TakeoverItem.Click += (_, _) => TakeoverToggled?.Invoke(TakeoverItem.IsChecked);
        ExitItem.Click += (_, _) => ExitRequested?.Invoke();
        ItemList.ItemsSource = _items;
        ItemList.MouseDoubleClick += OnItemDoubleClick;
        ItemList.KeyDown += OnItemListKeyDown;
        Body.ContextMenuOpening += OnBodyContextMenuOpening;
        TitleBox.KeyDown += OnTitleBoxKeyDown;
        TitleBox.LostKeyboardFocus += (_, _) => EndRename(commit: true);
        PromptHideButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(true);
        PromptLaterButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(false);
#if DEBUG
        // Checklist B11: a hung fence UI thread must not freeze the desktop or taskbar (owner input-queue attachment, ADR-011).
        var freezeItem = new MenuItem { Header = "Debug: freeze this UI thread for 10 s (B11)" };
        freezeItem.Click += (_, _) => Thread.Sleep(TimeSpan.FromSeconds(10));
        BodyContextMenu.Items.Add(freezeItem);
#endif
        ApplyTheme(lightTheme);
        SetIconSize(fence.IconSize);
        SetLocked(fence.Locked);
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => ApplyRoundedCorners();
        // Icons are rendered for one DPI. DpiChanged is routed: every new item raises it too, so react only to the window's own.
        DpiChanged += (_, dpiChange) =>
        {
            if (dpiChange.OriginalSource == this && dpiChange.OldDpi.PixelsPerDip != dpiChange.NewDpi.PixelsPerDip) ReloadIcons();
        };
    }

    public void SetTakeoverChecked(bool active) => TakeoverItem.IsChecked = active;

    public void ShowTakeoverPrompt(bool visible) => TakeoverPrompt.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetTitle(string title) => TitleText.Text = title;

    /// <summary>Shows exactly these items in this order. Items already shown keep their loaded name and icon.</summary>
    public void SetItems(IReadOnlyList<string> itemRefs)
    {
        _itemRefs = itemRefs;
        if (_items.Select(view => view.ItemRef).SequenceEqual(itemRefs, StringComparer.Ordinal)) return;
        var existing = _items.ToDictionary(view => view.ItemRef, StringComparer.Ordinal);
        var iconSizePx = (int)Math.Round(_iconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);
        _items.Clear();
        foreach (var itemRef in itemRefs)
        {
            if (!existing.TryGetValue(itemRef, out var view))
            {
                view = new FenceItemView(itemRef);
                _iconLoader.Request(view, iconSizePx);
            }
            _items.Add(view);
        }
    }

    /// <summary>One of <see cref="ConfigNormalizer.IconSizes"/> (DIPs). Icons are reloaded at the new size.</summary>
    public void SetIconSize(int iconSizeDips)
    {
        _iconSizeDips = iconSizeDips;
        Resources["IconSize"] = (double)iconSizeDips;
        Resources["ItemWidth"] = Math.Max(76.0, iconSizeDips + 28.0); // room for two short label words under small icons
        foreach (var sizeItem in IconSizeItem.Items.OfType<MenuItem>()) sizeItem.IsChecked = (int)sizeItem.Tag == iconSizeDips;
        ReloadIcons();
    }

    /// <summary>Locked: the title no longer drags and the edges no longer resize.</summary>
    public void SetLocked(bool locked)
    {
        LockItem.IsChecked = locked;
        // A fresh WindowChrome each time: editing the attached one in place is not re-applied after an unlock.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(0),
            CaptionHeight = locked ? 0 : CaptionHeightDips,
            ResizeBorderThickness = new Thickness(locked ? 0 : ResizeBorderDips),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
    }

    /// <summary>Colours for Windows' light or dark app mode (M2c: fences follow Windows).</summary>
    public void ApplyTheme(bool light)
    {
        var ink = light ? Colors.Black : Colors.White;
        SolidColorBrush Ink(byte alpha)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B));
            brush.Freeze();
            return brush;
        }
        // The accent blur ignores its tint colour (ACCENT_ENABLE_BLURBEHIND), so the veil is drawn here: none in dark mode
        // (the look the user approved in M2a), a dense light veil in light mode so dark text reads on any wallpaper.
        var veil = new SolidColorBrush(light ? Color.FromArgb(0xB8, 0xF2, 0xF2, 0xF2) : Colors.Transparent);
        veil.Freeze();
        Resources["FenceVeil"] = veil;
        Resources["FenceText"] = Ink(light ? (byte)0xE6 : (byte)0xFF);
        Resources["FenceSubtleText"] = Ink(0xA0);
        Resources["FenceBorder"] = Ink(light ? (byte)0x33 : (byte)0x40);
        Resources["FenceDivider"] = Ink(light ? (byte)0x1F : (byte)0x26);
        Resources["FenceHover"] = Ink(light ? (byte)0x14 : (byte)0x22);
        Resources["FenceSelected"] = Ink(light ? (byte)0x2A : (byte)0x44);
        Resources["FenceScrollThumb"] = Ink(light ? (byte)0x44 : (byte)0x55);
        // Soft halo behind labels, like desktop icon labels: readable on busy or bright wallpapers (user choice).
        var shadow = new DropShadowEffect
        {
            Color = light ? Colors.White : Colors.Black,
            ShadowDepth = light ? 0 : 1,
            BlurRadius = 4,
            Opacity = 0.9,
        };
        shadow.Freeze();
        Resources["LabelShadow"] = shadow;
    }

    private void ReloadIcons()
    {
        _items.Clear(); // SetItems then requests every icon again
        SetItems(_itemRefs);
    }

    private void BeginRename()
    {
        _renaming = true;
        TitleBox.Text = TitleText.Text;
        TitleText.Visibility = Visibility.Hidden;
        TitleBox.Visibility = Visibility.Visible;
        Activate(); // keyboard input needs the fence active; it stays at the bottom (owned by Progman, ADR-011)
        // With another app in front the fence may not get focus (ADR-015); then typing would go elsewhere and the box
        // could never close, so give up instead (M2c review).
        if (!TitleBox.Focus() || !IsActive)
        {
            EndRename(commit: false);
            return;
        }
        TitleBox.SelectAll();
    }

    private void EndRename(bool commit)
    {
        if (!_renaming) return;
        _renaming = false;
        TitleBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        if (commit && TitleBox.Text != TitleText.Text) RenameRequested?.Invoke(TitleBox.Text);
    }

    private void OnTitleBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Escape)) return;
        EndRename(commit: args.Key == Key.Enter);
        ItemList.Focus();
        args.Handled = true;
    }

    private void OnItemListKeyDown(object sender, KeyEventArgs args)
    {
        if (args.OriginalSource is TextBox) return; // keys typed into the rename box
        var selected = ItemList.SelectedItems.OfType<FenceItemView>().ToList();
        switch (args.Key)
        {
            case Key.Enter:
                foreach (var view in selected) OpenRequested?.Invoke(view.ItemRef);
                break;
            case Key.Delete when selected.Count > 0:
                RecycleRequested?.Invoke(selected.Select(view => view.ItemRef).ToList()); // Shift+Del too: always the Recycle Bin
                break;
            case Key.F2 when selected.Count == 1:
                BeginItemRename(selected[0].ItemRef);
                break;
            default:
                return;
        }
        args.Handled = true;
    }

    /// <summary>Starts renaming one item in place (F2, or Rename in Windows' item menu). Special items cannot be renamed.</summary>
    public void BeginItemRename(string itemRef)
    {
        var view = _items.FirstOrDefault(candidate => candidate.ItemRef == itemRef);
        if (view is null || itemRef.StartsWith("::", StringComparison.Ordinal)) return;
        ItemList.ScrollIntoView(view);
        view.EditName = view.Label;
        view.IsEditing = true; // the box shows; OnLabelBoxVisibleChanged focuses it
    }

    private void OnLabelBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not TextBox { IsVisible: true, DataContext: FenceItemView view } box) return;
        Activate();
        // Without focus typing would go to another app and the box could never close (ADR-015): give up instead.
        if (!box.Focus() || !IsActive)
        {
            view.IsEditing = false;
            return;
        }
        // Like Explorer: select the name, not the extension.
        var extensionStart = box.Text.LastIndexOf('.');
        box.Select(0, extensionStart > 0 ? extensionStart : box.Text.Length);
    }

    private void OnLabelBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (sender is not TextBox { DataContext: FenceItemView view } || args.Key is not (Key.Enter or Key.Escape)) return;
        EndItemRename(view, commit: args.Key == Key.Enter);
        ItemList.Focus();
        args.Handled = true;
    }

    private void OnLabelBoxLostFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (sender is TextBox { DataContext: FenceItemView view }) EndItemRename(view, commit: true);
    }

    private void EndItemRename(FenceItemView view, bool commit)
    {
        if (!view.IsEditing) return;
        view.IsEditing = false;
        var newName = view.EditName.Trim();
        if (commit && newName.Length > 0 && newName != view.Label) ItemRenameRequested?.Invoke(view.ItemRef, newName);
    }

    /// <summary>Right-click on an item opens Windows' item menu instead of the fence menu.</summary>
    private void OnBodyContextMenuOpening(object sender, ContextMenuEventArgs args)
    {
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is not ListBoxItem { DataContext: FenceItemView clicked } container) return;
        args.Handled = true;
        if (!container.IsSelected)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
        }
        // From the mouse, or (menu key: CursorLeft < 0) from the item's corner. PointToScreen gives physical pixels.
        var anchor = args.CursorLeft >= 0 ? PointToScreen(Mouse.GetPosition(this)) : container.PointToScreen(new Point(container.ActualWidth / 2, container.ActualHeight / 2));
        var refs = ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).ToList();
        if (refs.Count == 0) refs = [clicked.ItemRef];
        ItemMenuRequested?.Invoke(refs, (int)anchor.X, (int)anchor.Y);
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is ListBoxItem { DataContext: FenceItemView view })
            OpenRequested?.Invoke(view.ItemRef);
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        Handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(Handle).AddHook(OnMessage);
        FenceWindowChrome.ApplyToolWindowStyles(Handle);
        FenceWindowChrome.ApplyAccentBlur(Handle);
    }

    private void ApplyRoundedCorners()
    {
        if (Handle == 0) return;
        var pixels = FenceWindowChrome.GetPixelRect(Handle);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        FenceWindowChrome.ApplyRoundedCorners(Handle, pixels.Width, pixels.Height, (int)Math.Round(CornerRadiusDips * scale));
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Windows' item menu draws "Send to", "Open with" and shell-extension entries through its owner window.
        if (ShellItemMenu.HandleMenuMessage(message, wParam, lParam, out var menuResult))
        {
            handled = true;
            return menuResult;
        }
        switch (message)
        {
            case WmEnterSizeMove:
                _drag = new DragTracker(FenceWindowChrome.GetPixelRect(Handle));
                break;
            case WmMoving when SnapRect is not null && _drag is not null:
                FenceWindowChrome.WriteRect(lParam, _drag.Step(FenceWindowChrome.ReadRect(lParam), snap: rect => SnapRect(rect, SnapEdges.Move)));
                handled = true;
                return 1;
            case WmSizing when SnapRect is not null && _drag is not null:
                var edges = SizingEdges((int)wParam);
                FenceWindowChrome.WriteRect(lParam, _drag.Step(FenceWindowChrome.ReadRect(lParam), snap: rect => SnapRect(rect, edges)));
                handled = true;
                return 1;
            case WmExitSizeMove:
                _drag = null;
                MovedByUser?.Invoke(this, FenceWindowChrome.GetPixelRect(Handle));
                break;
        }
        return 0;
    }

    /// <summary>WM_SIZING's WMSZ_* value as the edges being dragged.</summary>
    private static SnapEdges SizingEdges(int sizingEdge) => sizingEdge switch
    {
        1 => SnapEdges.Left,
        2 => SnapEdges.Right,
        3 => SnapEdges.Top,
        4 => SnapEdges.Top | SnapEdges.Left,
        5 => SnapEdges.Top | SnapEdges.Right,
        6 => SnapEdges.Bottom,
        7 => SnapEdges.Bottom | SnapEdges.Left,
        8 => SnapEdges.Bottom | SnapEdges.Right,
        _ => SnapEdges.None,
    };
}
```
`src/NeoFences.App/FenceHost.cs`:
```csharp
using System.IO;
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Membership;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Owns the fences on the desktop: loads the config, places one <see cref="FenceWindow"/> per fence on the current
/// monitors, keeps fence contents in step with the Desktop folders (reconcile at start, then watcher events),
/// saves changes (debounced 500 ms, ADR-006), and keeps everything attached through display changes,
/// Explorer restarts and sign-out (ADR-011, ADR-013). Every Win32/COM call goes through NeoFences.Shell.
/// </summary>
public sealed class FenceHost
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReattachInterval = TimeSpan.FromMilliseconds(500);
    private const double SnapGapDips = 8;        // spec §6: 8 px spacing from other fences and screen edges
    private const double SnapThresholdDips = 12; // how close an edge must come before it snaps
    private const int ReattachAttempts = 10;

    private readonly ConfigStore _store = new(AppPaths.DataDirectory);
    private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
    private readonly SystemMessageWindow _messages = new();
    private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
    private DesktopWatcher? _desktopWatcher;
    private IReadOnlyList<RecentRemoval> _recentRemovals = []; // safe-save memory (FenceMembership.SafeSaveWindow)
    private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
    private IReadOnlyList<MonitorPlacement> _monitors = [];
    private bool _takeoverActive;
    private bool _lightTheme = SystemTheme.AppsUseLightTheme();
    private bool _sessionEnding;

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
    }

    public void Start()
    {
        var loaded = _store.Load();
        Log.Information("config loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath})",
            loaded.Source, loaded.IsReadOnly, loaded.CorruptCopyPath);
        _config = loaded.Config;
        _watchdog.LaunchDetached(Environment.ProcessId);

        RefreshMonitors();
        foreach (var fence in _config.Fences) OpenWindow(fence);
        ReconcileDesktop();
        StartDesktopWatcher();
        ApplyLayout();
        if (_config.Settings.Takeover) SetTakeover(true);
        else if (_watchdog.IsTakeoverActiveMarked)
        {
            // Icons may still be hidden from a run whose Takeover-off never got saved (both processes killed): show them.
            Log.Warning("takeover-active marker found while Takeover is off; showing desktop icons");
            SetIconsHidden(false);
        }
        ScheduleSave();
    }

    /// <summary>
    /// Windows asked to end the session (WPF's SessionEnding, inside WM_QUERYENDSESSION). WPF then shuts the app
    /// down, which may be the last thing that ever runs, so the icons come back now and the watchdog is told it is
    /// a session end, not a user exit: if the user cancels the shutdown, the watchdog restarts NeoFences (ADR-013).
    /// </summary>
    public void OnSessionEnding()
    {
        _sessionEnding = true;
        SaveNow();
        if (_takeoverActive) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        SaveNow();
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        if (_takeoverActive) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        _iconLoader.Dispose();
        _messages.Dispose();
    }

    /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
    public void EmergencyRestoreIcons()
    {
        if (_takeoverActive) DesktopIcons.TrySetHidden(false);
    }

    private void OpenWindow(Fence fence)
    {
        var window = new FenceWindow(fence, takeoverActive: _takeoverActive, lightTheme: _lightTheme, iconLoader: _iconLoader);
        window.SnapRect = (rect, edges) => SnapFence(window, rect, edges);
        window.MovedByUser += OnFenceMoved;
        window.RenameRequested += title => RenameFence(window, title);
        window.IconSizeRequested += iconSize => SetFenceIconSize(window, iconSize);
        window.LockToggled += locked => SetFenceLocked(window, locked);
        window.DeleteRequested += () => DeleteFence(window);
        window.NewFenceRequested += CreateFence;
        window.TakeoverToggled += SetTakeover;
        window.ExitRequested += () => ExitRequested?.Invoke();
        window.OpenRequested += itemRef => OpenItem(itemRef, ownerHandle: window.Handle);
        window.ItemMenuRequested += (itemRefs, screenX, screenY) => ShowItemMenu(window, itemRefs, screenX, screenY);
        window.RecycleRequested += itemRefs => RecycleItems(window, itemRefs);
        window.ItemRenameRequested += (itemRef, newName) => RenameItem(window, itemRef, newName);
        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", fence.Id);
        _windows[fence.Id] = window;
    }

    /// <summary>Full pass over the Desktop folders: at start and whenever watcher events were lost.</summary>
    private void ReconcileDesktop()
    {
        var listing = DesktopItems.Enumerate();
        var (reconciled, report) = FenceMembership.Reconcile(_config, listing.ItemRefs, listing.UnavailableFolders);
        _config = reconciled;
        if (listing.UnavailableFolders.Count > 0) Log.Warning("desktop folders not readable: {Folders}", listing.UnavailableFolders);
        if (report.Suspicious) Log.Warning("kept fenced items from an unreadable or empty desktop listing until a later reconcile");
        Log.Information("desktop reconciled: {AddedCount} added to the Inbox, {RemovedCount} removed", report.AddedToInbox.Count, report.Removed.Count);
        RefreshWindows();
        ScheduleSave();
    }

    private void StartDesktopWatcher()
    {
        // A folder that cannot be watched degrades to "its changes show after a restart" (hard rule 7).
        _desktopWatcher = new DesktopWatcher((folder, failure) => Log.Error(failure, "cannot watch {Folder}; its changes show after a restart", folder));
        var dispatcher = Dispatcher.CurrentDispatcher;
        _desktopWatcher.Changed += change => dispatcher.BeginInvoke(() => OnDesktopChanged(change));
        _desktopWatcher.Overflowed += () => dispatcher.BeginInvoke(OnDesktopWatcherError);
    }

    /// <summary>Events were lost, or the watcher stopped (.NET disables it after a non-overflow error): start over.</summary>
    private void OnDesktopWatcherError()
    {
        if (_desktopWatcher is null) return; // shut down meanwhile
        Log.Warning("desktop watcher lost events or stopped; re-arming and reconciling");
        _desktopWatcher.Dispose();
        StartDesktopWatcher();
        ReconcileDesktop();
    }

    private void OnDesktopChanged(DesktopChange change)
    {
        (_config, _recentRemovals) = FenceMembership.Apply(_config, change, _recentRemovals, DateTimeOffset.Now);
        RefreshWindows();
        ScheduleSave();
    }

    private void RefreshWindows()
    {
        var showPrompt = !_config.Settings.TakeoverPromptAnswered && !_takeoverActive;
        foreach (var fence in _config.Fences)
        {
            if (!_windows.TryGetValue(fence.Id, out var window)) continue;
            window.SetItems(fence.Items);
            window.ShowTakeoverPrompt(showPrompt && fence.IsInbox);
        }
    }

    private static void OpenItem(string itemRef, nint ownerHandle)
    {
        // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6).
        Task.Run(() =>
        {
            if (!ShellItems.TryOpen(itemRef, ownerHandle)) Log.Warning("could not open {ItemRef}", itemRef);
        });
    }

    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY)
    {
        var extended = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        switch (ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended))
        {
            case ItemMenuChoice.Rename:
                window.BeginItemRename(itemRefs[0]);
                break;
            case ItemMenuChoice.Delete:
                RecycleItems(window, itemRefs); // always the Recycle Bin, even with Shift held (hard rule 1)
                break;
        }
    }

    /// <summary>Windows moves them to the Recycle Bin (with its own dialogs); the watcher then removes them from the fence.</summary>
    private static void RecycleItems(FenceWindow window, IReadOnlyList<string> itemRefs)
    {
        Log.Information("recycling {Count} item(s)", itemRefs.Count);
        if (!ShellFileOps.TryRecycle(window.Handle, itemRefs)) Log.Warning("recycle did not run or was cancelled: {ItemRefs}", itemRefs);
    }

    /// <summary>Windows renames the file; the watcher's rename event keeps it in its fence and position.</summary>
    private static void RenameItem(FenceWindow window, string itemRef, string newName)
    {
        if (!ShellFileOps.TryRename(window.Handle, itemRef, newName)) Log.Warning("rename did not run or was cancelled: {ItemRef}", itemRef);
    }

    private void AnswerTakeoverPrompt(bool hideIcons)
    {
        Log.Information("first-run question answered: hide desktop icons {HideIcons}", hideIcons);
        if (hideIcons) SetTakeover(true);
        else
        {
            _config = _config with { Settings = _config.Settings with { TakeoverPromptAnswered = true } };
            RefreshWindows();
            SaveNow();
        }
    }

    private void ApplyLayout()
    {
        if (_monitors.Count == 0)
        {
            Log.Warning("no monitors reported; keeping the current layout");
            return;
        }
        try
        {
            var (resolved, layout) = LayoutEngine.Resolve(_config, _monitors.Select(monitor => monitor.ToDisplayMonitor()).ToList());
            _config = resolved;
            foreach (var (fenceId, rect) in layout.Fences)
            {
                if (!_windows.TryGetValue(fenceId, out var window)) continue;
                var monitor = _monitors.First(candidate => candidate.DeviceId == rect.Monitor);
                FenceWindowChrome.SetPixelRect(window.Handle, FencePlacement.ToPixels(rect, monitor));
                if (!window.IsVisible)
                {
                    window.Show();
                    FenceWindowChrome.SendToBack(window.Handle); // Show puts it above every app; fences live just above the desktop
                }
            }
        }
        catch (ArgumentException unusableDisplay)
        {
            // Garbage from a monitor query mid-change (M1 review): skip this resolve, the next display event retries.
            Log.Warning(unusableDisplay, "display query unusable; keeping the current layout");
        }
    }

    private void RefreshMonitors()
    {
        _monitors = Monitors.Enumerate();
        Log.Information("monitors: {Monitors}", string.Join("; ", _monitors.Select(monitor =>
            $"{monitor.DeviceId} {monitor.PixelWidth}x{monitor.PixelHeight}@{monitor.ScalePercent}% " +
            $"work {monitor.WorkLeftPx},{monitor.WorkTopPx} {monitor.WorkWidthPx}x{monitor.WorkHeightPx}{(monitor.IsPrimary ? " primary" : "")}")));
    }

    private void OnFenceMoved(FenceWindow window, PixelRect pixels)
    {
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: window.FenceId, rect: FencePlacement.FromPixels(pixels, monitor));
        ScheduleSave();
    }

    private PixelRect SnapFence(FenceWindow window, PixelRect rect, SnapEdges edges)
    {
        if (_monitors.Count == 0) return rect;
        var monitor = FencePlacement.ContainingMonitor(rect, _monitors);
        var others = _windows.Values.Where(other => other != window && other.IsVisible).Select(other => FenceWindowChrome.GetPixelRect(other.Handle)).ToList();
        return Snapping.Snap(rect, edges,
            workArea: new PixelRect(monitor.WorkLeftPx, monitor.WorkTopPx, monitor.WorkWidthPx, monitor.WorkHeightPx),
            others: others,
            gapPx: (int)Math.Round(SnapGapDips * monitor.Scale),
            thresholdPx: (int)Math.Round(SnapThresholdDips * monitor.Scale));
    }

    private void RenameFence(FenceWindow window, string title)
    {
        _config = FenceEdits.Rename(_config, window.FenceId, title);
        window.SetTitle(_config.Fences.First(fence => fence.Id == window.FenceId).Title);
        ScheduleSave();
    }

    private void SetFenceIconSize(FenceWindow window, int iconSize)
    {
        _config = FenceEdits.SetIconSize(_config, window.FenceId, iconSize);
        window.SetIconSize(iconSize);
        ScheduleSave();
    }

    private void SetFenceLocked(FenceWindow window, bool locked)
    {
        _config = FenceEdits.SetLocked(_config, window.FenceId, locked);
        window.SetLocked(locked);
        ScheduleSave();
    }

    /// <summary>Removes the fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
    private void DeleteFence(FenceWindow window)
    {
        _config = FenceMembership.DeleteFence(_config, window.FenceId);
        _windows.Remove(window.FenceId);
        window.Close();
        RefreshWindows();
        ScheduleSave();
    }

    private void OnThemeChanged()
    {
        var light = SystemTheme.AppsUseLightTheme();
        if (light == _lightTheme) return;
        _lightTheme = light;
        Log.Information("Windows app mode changed; light: {Light}", light);
        foreach (var window in _windows.Values) window.ApplyTheme(light);
    }

    private void OnDisplayChanged()
    {
        RefreshMonitors();
        ApplyLayout();
        ScheduleSave();
    }

    private void CreateFence()
    {
        (_config, var fence) = FenceMembership.CreateFence(_config, "New fence");
        OpenWindow(fence);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
    }

    private void SetTakeover(bool active)
    {
        _takeoverActive = active;
        // Any explicit choice (banner or menu) answers the first-run question.
        _config = _config with { Settings = _config.Settings with { Takeover = active, TakeoverPromptAnswered = true } };
        SetIconsHidden(active);
        foreach (var window in _windows.Values) window.SetTakeoverChecked(active);
        RefreshWindows();
        SaveNow(); // not debounced: the saved setting must match the takeover-active marker if we are killed next
    }

    /// <returns>True when Windows confirmed the new state.</returns>
    private bool SetIconsHidden(bool hidden)
    {
        // The watchdog must know whenever icons may be hidden: mark before hiding, unmark only after a confirmed show.
        if (hidden) TryMarker(() => _watchdog.SetTakeoverActive(true), what: "takeover-active marker");
        var applied = DesktopIcons.TrySetHidden(hidden);
        if (applied && !hidden) TryMarker(() => _watchdog.SetTakeoverActive(false), what: "takeover-active marker");
        if (applied) Log.Information("desktop icons hidden: {Hidden}", hidden);
        else Log.Warning("could not set desktop icons hidden: {Hidden}", hidden);
        return applied;
    }

    /// <summary>Icons as they should be: hidden while Takeover is on, shown (and unmarked) otherwise.</summary>
    private bool EnsureIconState() =>
        _takeoverActive ? SetIconsHidden(true)
        : !_watchdog.IsTakeoverActiveMarked || SetIconsHidden(false);

    private static void TryMarker(Action writeMarker, string what)
    {
        try
        {
            writeMarker();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "could not update the {What}", what);
        }
    }

    private void OnExplorerRestarted()
    {
        Log.Information("Explorer restarted; re-attaching fences");
        var attempts = 0;
        var retryTimer = new DispatcherTimer { Interval = ReattachInterval };
        retryTimer.Tick += (_, _) =>
        {
            attempts++;
            var unattachedCount = _windows.Values.Count(window => !DesktopHost.AttachToDesktop(window.Handle));
            foreach (var window in _windows.Values) FenceWindowChrome.SendToBack(window.Handle);
            var iconsOk = EnsureIconState();
            if ((unattachedCount == 0 && iconsOk) || attempts >= ReattachAttempts)
            {
                retryTimer.Stop();
                Log.Information("re-attach after Explorer restart: {Attempts} attempt(s), unattached {UnattachedCount}, icons ok {IconsOk}",
                    attempts, unattachedCount, iconsOk);
            }
        };
        retryTimer.Start();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            if (!_store.Save(_config)) Log.Warning("config not saved: config.json is read-only this session");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "config save failed");
        }
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 122`.

- [ ] **Step 3: Live smoke (ask the user first: mouse and keyboard for about 60 s, one test file ends in the Recycle Bin)**

Save as `<scratchpad>\m3a-smoke.ps1`, then run `& "<scratchpad>\m3a-smoke.ps1" -Scratch "<scratchpad>"`:
```powershell
# M3a live smoke: F2 rename, Del → Recycle Bin, Windows item menu, broken-shortcut message, Word-style save.
# Uses only its own zz-m3a-* files on the Desktop (one ends in the Recycle Bin), then refocuses Windows Terminal.
param([string]$Exe = "F:\projects\neo_fences\src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe", [string]$Scratch = $env:TEMP)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type -Namespace NfM3a -Name N -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
'@
function Pause-Ms([int]$ms) { [System.Threading.Thread]::Sleep($ms) }
function Key([byte]$vk) { [NfM3a.N]::keybd_event($vk,0,0,[UIntPtr]::Zero); [NfM3a.N]::keybd_event($vk,0,2,[UIntPtr]::Zero); Pause-Ms 150 }
function Click([int]$x, [int]$y, [uint32]$down = 2, [uint32]$up = 4) { [void][NfM3a.N]::SetCursorPos($x, $y); Pause-Ms 150; [NfM3a.N]::mouse_event($down,0,0,0,[UIntPtr]::Zero); [NfM3a.N]::mouse_event($up,0,0,0,[UIntPtr]::Zero); Pause-Ms 400 }
function Foreground { $text = New-Object System.Text.StringBuilder 256; [void][NfM3a.N]::GetWindowText([NfM3a.N]::GetForegroundWindow(), $text, 256); "$text" }
function Wait-Until([scriptblock]$condition, [int]$seconds = 10) { $deadline = (Get-Date).AddSeconds($seconds); while (-not (& $condition) -and (Get-Date) -lt $deadline) { Pause-Ms 250 }; [bool](& $condition) }
function Inbox { @(((Get-Content "$env:LOCALAPPDATA\NeoFences\config.json" -Raw | ConvertFrom-Json).fences | Where-Object isInbox).items) }
function Save-Shot([string]$name, [int]$x, [int]$y, [int]$w, [int]$h) { $bitmap = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bitmap); $g.CopyFromScreen($x, $y, 0, 0, $bitmap.Size); $bitmap.Save("$Scratch\$name.png"); $g.Dispose(); $bitmap.Dispose(); "$Scratch\$name.png" }
# Finds an item in any fence by its label (UIA from each fence window: the desktop-wide search does not reach them),
# scrolls it into view, and returns its centre in screen pixels; $null if not found.
Add-Type -Namespace NfM3aFind -Name W -MemberDefinition @'
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
'@
function Item-Centre([string]$label) {
  $fences = New-Object System.Collections.Generic.List[IntPtr]
  [void][NfM3aFind.W]::EnumWindows({ param($h, $l) $s = New-Object System.Text.StringBuilder 64; [void][NfM3aFind.W]::GetWindowText($h, $s, 64); if ("$s" -eq "NeoFences fence") { $fences.Add($h) }; $true }, [IntPtr]::Zero)
  $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $label)
  foreach ($fence in $fences) {
    $item = [System.Windows.Automation.AutomationElement]::FromHandle($fence).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $item) { continue }
    try { $item.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView(); Pause-Ms 300 } catch { }
    $box = $item.Current.BoundingRectangle; return [pscustomobject]@{ X = [int]($box.X + $box.Width / 2); Y = [int]($box.Y + $box.Height / 3) } }
  $null }


$desk = [Environment]::GetFolderPath('Desktop'); $shell = New-Object -ComObject Shell.Application
& $Exe --exit; [void](Wait-Until { -not (Get-Process NeoFences -ErrorAction SilentlyContinue) } 15); Start-Process $Exe; Pause-Ms 5000
foreach ($name in 'zz-m3a-rename.txt', 'zz-m3a-recycle.txt', 'zz-m3a-safe.txt') { Set-Content "$desk\$name" "m3a smoke" }
$link = (New-Object -ComObject WScript.Shell).CreateShortcut("$desk\zz-m3a-broken.lnk"); $link.TargetPath = "C:\nf-missing\gone.exe"; $link.Save()
"test files in Inbox: " + (Wait-Until { (Inbox) -contains "$desk\zz-m3a-safe.txt" -and (Inbox) -contains "$desk\zz-m3a-broken.lnk" })
$shell.MinimizeAll(); Pause-Ms 1500; Click 900 950   # desktop gets focus (activation follows it, ADR-015)

# 1. F2 rename keeps the extension and the position.
$before = [array]::IndexOf((Inbox), "$desk\zz-m3a-rename.txt")
$spot = Item-Centre 'zz-m3a-rename.txt'; if (-not $spot) { "item not found; aborting"; return }; Click $spot.X $spot.Y; Key 0x71
[System.Windows.Forms.SendKeys]::SendWait("zz-m3a-renamed{ENTER}")
"F2 rename: file renamed " + (Wait-Until { Test-Path "$desk\zz-m3a-renamed.txt" }) + ", same position " + (Wait-Until { [array]::IndexOf((Inbox), "$desk\zz-m3a-renamed.txt") -eq $before })

# 2. Del sends to the Recycle Bin (answers Windows' confirmation if the user has it on).
$spot = Item-Centre 'zz-m3a-recycle.txt'; Click $spot.X $spot.Y; Key 0x2E; Pause-Ms 1200
if ((Foreground) -match 'Delete') { "Windows asked: '$(Foreground)'"; Key 0x0D }
"Del: gone from Desktop " + (Wait-Until { -not (Test-Path "$desk\zz-m3a-recycle.txt") }) + ", in Recycle Bin " + [bool]($shell.NameSpace(10).Items() | Where-Object Name -like 'zz-m3a-recycle*') + ", out of the fence " + (Wait-Until { (Inbox) -notcontains "$desk\zz-m3a-recycle.txt" })

# 3. Windows' item menu on right-click.
$spot = Item-Centre 'zz-m3a-renamed.txt'; Click $spot.X $spot.Y 8 16; Pause-Ms 1200
Save-Shot "m3a-item-menu" ($spot.X - 20) ($spot.Y - 20) 420 560
Key 0x1B; Pause-Ms 400

# 4. A broken shortcut shows Windows' own message (answered No: keep the shortcut).
$spot = Item-Centre 'zz-m3a-broken'; [void][NfM3a.N]::SetCursorPos($spot.X, $spot.Y); Pause-Ms 150; foreach ($press in 1..2) { [NfM3a.N]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [NfM3a.N]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Pause-Ms 60 }; Pause-Ms 2500
"broken shortcut: foreground '$(Foreground)'"; Save-Shot "m3a-broken" 560 300 800 420
if ((Foreground) -match 'Shortcut|Problem') { Key 0x1B; Pause-Ms 500 }

# 5. Word-style save: original renamed away, temp renamed onto it, renamed-away copy deleted.
$before = [array]::IndexOf((Inbox), "$desk\zz-m3a-safe.txt")
Rename-Item "$desk\zz-m3a-safe.txt" '~WRL0001.tmp'; Pause-Ms 200
Set-Content "$desk\~WRD0001.tmp" "saved"; (Get-Item "$desk\~WRD0001.tmp").Attributes = 'Hidden'; Pause-Ms 200
Rename-Item "$desk\~WRD0001.tmp" 'zz-m3a-safe.txt' -Force; Pause-Ms 200
(Get-Item "$desk\zz-m3a-safe.txt" -Force).Attributes = 'Normal'; [System.IO.File]::Delete("$desk\~WRL0001.tmp"); Pause-Ms 1500
"Word-style save: same position " + ([array]::IndexOf((Inbox), "$desk\zz-m3a-safe.txt") -eq $before) + " (before $before, now $([array]::IndexOf((Inbox), "$desk\zz-m3a-safe.txt")))"

# Clean up our own files, restore windows, bring Windows Terminal back.
foreach ($name in 'zz-m3a-renamed.txt', 'zz-m3a-rename.txt', 'zz-m3a-safe.txt', 'zz-m3a-broken.lnk', '~WRL0001.tmp', '~WRD0001.tmp') { if (Test-Path "$desk\$name") { [System.IO.File]::Delete("$desk\$name") } }
"cleaned: " + (Wait-Until { -not ((Inbox) -like '*zz-m3a*') })
$shell.UndoMinimizeALL(); Pause-Ms 1000
$terminal = Get-Process WindowsTerminal -ErrorAction SilentlyContinue | Select-Object -First 1
if ($terminal) { [NfM3a.N]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [void](New-Object -ComObject WScript.Shell).AppActivate($terminal.Id); [NfM3a.N]::keybd_event(0x12,0,2,[UIntPtr]::Zero) }
"foreground at end: '$(Foreground)'"
```
Expected:
- `F2 rename: file renamed True, same position True`;
- `Del: gone from Desktop True, in Recycle Bin True, out of the fence True`;
- `m3a-item-menu.png` shows Windows' classic item menu;
- `broken shortcut: foreground 'Missing Shortcut'`;
- `Word-style save: same position True`;
- `cleaned: True`;
- Windows Terminal is in front at the end.

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.App
git commit -m "feat: added item menu, in-place rename, recycle and open feedback to fences"
```

---

### Task 4: Verification

**Files:** `docs/TEST-CHECKLIST.md` (append section J), `docs/research/m3a-item-actions.md` (create)

- [ ] **Step 1: Append section J**

```markdown
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
```

- [ ] **Step 2: Run J1–J10.** The smoke covers J1 (menu), J3 (F2), J5 (Del), J7 (broken shortcut) and J8 (safe save). **[USER]** runs J2, J4, J6, J9 and J10, and a real Word save.

- [ ] **Step 3: Write `docs/research/m3a-item-actions.md`** with the results and the prototype findings (the visible-temp Word variant, UIA search scope, double-click timing).

- [ ] **Step 4: Commit**

```powershell
git add docs/TEST-CHECKLIST.md docs/research/m3a-item-actions.md
git commit -m "docs: added M3a item action checks and results"
```

---

### Task 5: Docs sync

- [ ] **Step 1: Append ADR-016 to `docs/DECISIONS.md`**

```markdown
## ADR-016 — Item actions through Windows' own engine; delete always recycles; safe-save memory
**Date:** 2026-10-02 · **Status:** Accepted

**Context.** M3a adds what people do to desktop items in a fence: the right-click menu, rename, delete, and feedback when
something cannot open. It also fixes the deferred M2b review finding I4: an editor's "safe save" moved the document to
the Inbox. The user chose (2026-10-02) that Shift+Del also goes to the Recycle Bin, keeping hard rule 1 absolute.

**Decision.**
- **Item menu.** Windows' classic `IContextMenu` for the selected items, from the desktop `IShellFolder`. That folder
  merges the user's and the Public Desktop, so a file name or `::{CLSID}` is a child of it. The menu is shown with
  `TrackPopupMenuEx`. While it is open, the fence window forwards menu messages to `IContextMenu2/3`, which "Send to",
  "Open with" and owner-drawn extension entries need. Shift adds extended verbs. Windows 11's compact menu is
  Explorer-private (spec §6).
- **Rename and Delete verbs** from that menu are taken over: Rename edits in place, and Delete always recycles, even
  with Shift held.
- **Delete** uses `IFileOperation` with `FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE | FOFX_ADDUNDORECORD |
  FOF_WANTNUKEWARNING`. That gives Windows' own confirmation, progress and Undo. If an item cannot be recycled,
  Windows warns and asks before deleting it permanently. Special items are never recycled.
- **Rename:** an in-place box selects the name without the extension, like Explorer. The rename itself is done by
  `IFileOperation.RenameItem` (conflicts, Undo, hidden extensions). The watcher's rename event keeps the item in place.
- **Open failures** show Windows' own message (`ErrorDialog`, owned by the fence), e.g. "Missing Shortcut".
- **Safe-save memory (Core).** A fenced item that disappears is remembered for 5 s with its fence and index, whether
  it was deleted or renamed away (Word renames `doc` → `~WRL….tmp` before renaming its temp file onto `doc`). If the
  same ref appears again in that time, it goes back to that fence and position.
- **Accessibility:** items expose their label as the UI Automation name. Screen readers use it, and so does the
  scripted smoke.

**Consequences.**
- Delete is never permanent from NeoFences; permanently deleting means emptying the Recycle Bin in Windows.
- A real delete followed within 5 s by a new file with the same name lands where the old one was. That is
  harmless, and arguably expected.
- Menu, rename and delete need the fence active for keyboard (F2/Del), which inherits ADR-015's activation limit.
```

- [ ] **Step 2: Update the other docs**
  - `ARCHITECTURE.md`: add `ShellItemMenu`, `ShellFileOps` and the safe-save memory. Replace the planned "Shell/ShellActions" row with the real classes.
  - `FEATURES.md`: set "Shell context menu on items" and "Rename / delete to Recycle Bin" to done (M3a). In the keyboard row, F2/Del are now done.
  - `ROADMAP.md`: tick M3a, and tick the M2b safe-save carry-over and the M2b open-feedback carry-over.
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 3: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 4: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-016 and synced docs for M3a"
```
