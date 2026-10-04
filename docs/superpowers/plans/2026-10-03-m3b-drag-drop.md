# M3b — Drag-drop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drag-drop for fences, plus rubber-band selection:
- **Inside and between fences:** dragging items changes membership only and never touches files.
- **Out to apps and Explorer:** Windows' own drag (real files).
- **In from Explorer:** Windows copies or moves the files onto the Desktop, and they land in the fence they were dropped on.
- **Onto folders and the Recycle Bin item:** handled by that item, as on the desktop.

**Architecture:**
- `NeoFences.Core`:
  - `FenceMembership.MoveItems` defines the drop-index rule: insert before the item shown at the index, during the drag.
  - `FenceMembership.ExpectArrivals` reuses the safe-save memory, renamed `RememberedPlacement`, so dropped files land at the drop position.
- `NeoFences.Shell`:
  - `DesktopNamespace` holds the desktop-folder children, shared by the item menu, the drag data and the item drop targets;
  - `ShellDragDrop` provides `TryDrag` (`SHDoDragDrop` with the shell's `IDataObject`) and `RegisterFence`, an `IDropTarget` per fence that forwards to the shell where files really move.
- `NeoFences.App`: `FenceWindow` starts drags, hit-tests drop points and draws the rubber band; `FenceHost` registers drops and applies moves and arrivals.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335 (OLE drag-drop, `IDropTargetHelper`), Serilog, xUnit. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §6 (drag-drop table, rubber band). **Decisions:**
- ADR-016 (item actions);
- **ADR-017** (new, Task 5).

The user decided on 2026-10-02 to split M3 into M3a and M3b.

**Pre-verified (2026-10-03):** every code block below was compiled together (0 warnings, 0 errors), and **147/147 tests pass**. The prototype then passed the Task 3 smoke on the real desktop (with the user's consent while they were remote):
- reorder within the Inbox;
- a drag into the "Games" fence (membership only, the file stayed on the Desktop);
- rubber-band selection;
- a drag out to an Explorer window (Windows moved the file; it left the Desktop and the fence);
- a drag onto the Recycle Bin item (Windows recycled it).

**Not scripted: drops in from Explorer.** Explorer ignores synthetic drag gestures, and a helper process's OLE drag did not run either. Neither could even drop onto the bare desktop, so this is the test harness, not NeoFences. That path is checklist K4/K5, by hand.

**Prototype findings** (fixed in the code below):
- **WPF already registers a drop target** on every window, so `RegisterDragDrop` failed with `DRAGDROP_E_ALREADYREGISTERED`; NeoFences revokes WPF's first.
- **Text files report themselves as drop targets**, which turned reordering into "drop onto that file". Only containers (folders, Recycle Bin) take drops themselves.

## Global Constraints

- **Hard rule 1:** a drop between or within fences never moves, copies or deletes a file. It reports `DROPEFFECT_NONE` so the source cannot delete after a "move". Real file operations happen only in Windows' own drop targets (the Desktop folder, folders, Recycle Bin), with Windows' dialogs and Undo.
- **Hard rules 4 and 7:** all COM is in `NeoFences.Shell`; every drag or drop failure is logged, and the drop simply does nothing.
- Drop index: the target's list as displayed during the drag, still including the dragged items. Reading order (rows, then left to right); the right half of an item means after it.
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- The smoke takes the mouse for about 90 s and leaves one test file in the Recycle Bin. Ask first, and refocus Windows Terminal at the end.

## Review Focus

1. **Dragging fence items onto a fence or the bare desktop, holding Shift/Ctrl.** Expected: no file is moved, copied or deleted. Fence drops report NONE (Task 2 code). The bare desktop is Windows' own drop target and the files are already there.
2. **A multi-selection dragged to a position inside the same fence.** Expected: it lands where shown, keeping the items' order. Pinned by the `DragDropTests` (Task 1).
3. **Files from Explorer dropped on a fence (K4).** Expected: Windows moves or copies them, and they appear in that fence at the drop position. Pinned in Core by `FilesDroppedFromExplorer_ArriveWhereTheyWereDropped`; by hand K4.
4. **A drop while the fence list rebuilds, or a fence deleted mid-drag.** Expected: no crash; the drop is logged or ignored. Failures are caught in `Drop`/`Update`, and a fence's registration is revoked when it is deleted.
5. **Name conflicts on drop (K9).** Expected: Windows' dialog appears; a renamed copy lands in the Inbox. That is documented, not tested.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Membership/RememberedPlacement.cs` (renamed from `RecentRemoval.cs`), `FenceMembership.cs` | `MoveItems`, `ExpectArrivals`, generalised placement memory | 1 |
| `src/NeoFences.Shell/DesktopNamespace.cs`, `ShellDragDrop.cs`, `ShellItemMenu.cs`, `NativeMethods.txt` | desktop children, drag source, fence drop target | 2 |
| `src/NeoFences.App/FenceWindow.xaml(.cs)`, `FenceHost.cs` | drag start, hit test, rubber band, registration | 3 |
| `docs/TEST-CHECKLIST.md` (section K), `docs/research/m3b-drag-drop.md` | verification | 4 |
| `docs/DECISIONS.md` (ADR-017), `ARCHITECTURE.md`, `FEATURES.md`, `ROADMAP.md`, `SESSION-LOG.md`, hub | docs sync | 5 |

---

### Task 1: Core — drop index and expected arrivals

**Files:**
- Modify: `docs/ROADMAP.md` (claim M3b)
- Rename: `src/NeoFences.Core/Membership/RecentRemoval.cs` → `RememberedPlacement.cs` (content below)
- Modify: `src/NeoFences.Core/Membership/FenceMembership.cs` (full new content below), `src/NeoFences.App/FenceHost.cs` (type name only, in Task 3)
- Create: `tests/NeoFences.Core.Tests/Membership/DragDropTests.cs`

**Interfaces:**
- Produces:
  - `record RememberedPlacement(string ItemRef, string FenceId, int Index, DateTimeOffset RememberedAt)`;
  - `FenceMembership.MoveItems(NeoFencesConfig config, IReadOnlyList<string> itemRefs, string toFenceId, int insertAt) -> NeoFencesConfig`;
  - `FenceMembership.ExpectArrivals(IReadOnlyList<RememberedPlacement> recent, IReadOnlyList<string> itemRefs, string fenceId, int insertAt, DateTimeOffset now) -> IReadOnlyList<RememberedPlacement>`.
- Changes: `Apply(…, recent, now)` now takes and returns `RememberedPlacement`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m3b-drag-drop
```
In `docs/ROADMAP.md`, append ` — [~] claimed by session 2026-10-03 m3b` to the `### M3b — …` heading line.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M3b"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Membership/DragDropTests.cs`:
```csharp
using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

/// <summary>
/// Dropping fence items (M3b). The drop index is a position in the target fence's list as the user sees it during the
/// drag, i.e. still including the dragged items: "insert before the item that is shown at this index".
/// </summary>
public class DragDropTests
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private const string A = Desktop + "a.lnk", B = Desktop + "b.lnk", C = Desktop + "c.lnk", D = Desktop + "d.lnk";
    private const string X = Desktop + "x.txt", Y = Desktop + "y.txt";
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 2, 0, 0, TimeSpan.Zero);

    private static (NeoFencesConfig Config, Fence Games, Fence Work) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true };
        var games = Fence.Create("Games") with { Items = [A, B, C, D] };
        var work = Fence.Create("Work") with { Items = [X, Y] };
        return (new NeoFencesConfig { Fences = [inbox, games, work] }, games, work);
    }

    private static IReadOnlyList<string> ItemsOf(NeoFencesConfig config, string fenceId) => config.Fences.Single(fence => fence.Id == fenceId).Items;

    [Fact]
    public void WithinAFence_DraggingForward_LandsBeforeTheTargetItem()
    {
        var (config, games, _) = Sample();

        Assert.Equal([B, C, A, D], ItemsOf(FenceMembership.MoveItems(config, [A], games.Id, insertAt: 3), games.Id));
    }

    [Fact]
    public void WithinAFence_DraggingBackward_LandsBeforeTheTargetItem()
    {
        var (config, games, _) = Sample();

        Assert.Equal([A, D, B, C], ItemsOf(FenceMembership.MoveItems(config, [D], games.Id, insertAt: 1), games.Id));
    }

    [Theory]
    [InlineData(1)] // before itself
    [InlineData(2)] // just after itself
    public void DroppedOnItsOwnSpot_NothingMoves(int insertAt)
    {
        var (config, games, _) = Sample();

        Assert.Equal([A, B, C, D], ItemsOf(FenceMembership.MoveItems(config, [B], games.Id, insertAt), games.Id));
    }

    [Fact]
    public void BetweenFences_LandsAtTheDropPosition()
    {
        var (config, games, work) = Sample();

        var moved = FenceMembership.MoveItems(config, [A], work.Id, insertAt: 1);

        Assert.Equal([X, A, Y], ItemsOf(moved, work.Id));
        Assert.Equal([B, C, D], ItemsOf(moved, games.Id));
    }

    [Fact]
    public void SeveralItems_KeepTheOrderTheyHadInTheirFences_NotTheSelectionOrder()
    {
        var (config, _, work) = Sample();

        var moved = FenceMembership.MoveItems(config, [C, A], work.Id, insertAt: 2);

        Assert.Equal([X, Y, A, C], ItemsOf(moved, work.Id));
    }

    [Fact]
    public void IndexPastTheEnd_Appends_UnknownRefsAreIgnored()
    {
        var (config, games, work) = Sample();

        var moved = FenceMembership.MoveItems(config, [A, Desktop + "gone.txt"], work.Id, insertAt: 99);

        Assert.Equal([X, Y, A], ItemsOf(moved, work.Id));
        Assert.Equal([B, C, D], ItemsOf(moved, games.Id));
    }

    [Fact]
    public void OntoAPortalFence_Throws()
    {
        var (config, _, _) = Sample();
        var portal = Fence.Create("Downloads", FenceSource.Portal(@"C:\Users\cipher\Downloads"));
        config = config with { Fences = [.. config.Fences, portal] };

        Assert.Throws<ArgumentException>(() => FenceMembership.MoveItems(config, [A], portal.Id, insertAt: 0));
    }

    [Fact]
    public void FilesDroppedFromExplorer_ArriveWhereTheyWereDropped()
    {
        var (config, _, work) = Sample();
        const string first = Desktop + "photo.jpg", second = Desktop + "notes.txt";

        var expected = FenceMembership.ExpectArrivals(recent: [], itemRefs: [first, second], fenceId: work.Id, insertAt: 1, now: Now);
        var (afterFirst, stillExpected) = FenceMembership.Apply(config, new DesktopChange.Created(first), expected, now: Now.AddMilliseconds(300));
        var (afterSecond, _) = FenceMembership.Apply(afterFirst, new DesktopChange.Created(second), stillExpected, now: Now.AddMilliseconds(400));

        Assert.Equal([X, first, second, Y], ItemsOf(afterSecond, work.Id));
        Assert.Empty(afterSecond.Inbox.Items);
    }

    [Fact]
    public void ADroppedFileThatArrivesTooLate_GoesToTheInbox()
    {
        var (config, _, work) = Sample();
        const string slow = Desktop + "big.iso";

        var expected = FenceMembership.ExpectArrivals(recent: [], itemRefs: [slow], fenceId: work.Id, insertAt: 0, now: Now);
        var (arrived, _) = FenceMembership.Apply(config, new DesktopChange.Created(slow), expected, now: Now.Add(FenceMembership.SafeSaveWindow).AddSeconds(1));

        Assert.Equal([slow], arrived.Inbox.Items);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0117: 'FenceMembership' does not contain a definition for 'MoveItems'` and `… 'ExpectArrivals'`.

- [ ] **Step 4: Implement**

```powershell
git mv src/NeoFences.Core/Membership/RecentRemoval.cs src/NeoFences.Core/Membership/RememberedPlacement.cs
```
`src/NeoFences.Core/Membership/RememberedPlacement.cs`:
```csharp
namespace NeoFences.Core.Membership;

/// <summary>
/// Where an item should go if it (re)appears within <see cref="FenceMembership.SafeSaveWindow"/>: a fenced item that just
/// disappeared (editor safe-save, M3a) or a file just dropped onto a fence from Explorer (M3b).
/// </summary>
public sealed record RememberedPlacement(string ItemRef, string FenceId, int Index, DateTimeOffset RememberedAt);
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
    public static (NeoFencesConfig Config, IReadOnlyList<RememberedPlacement> Recent) Apply(
        NeoFencesConfig config, DesktopChange change, IReadOnlyList<RememberedPlacement> recent, DateTimeOffset now)
    {
        var remembered = recent.Where(removal => now - removal.RememberedAt <= SafeSaveWindow).ToList();
        switch (change)
        {
            case DesktopChange.Deleted deleted when FindOwner(config, deleted.ItemRef) is { } deletedOwner:
                var index = deletedOwner.Items.ToList().FindIndex(existing => ItemRef.Comparer.Equals(existing, deleted.ItemRef));
                remembered.Add(new RememberedPlacement(deleted.ItemRef, deletedOwner.Id, index, now));
                return (RemoveItem(config, deleted.ItemRef), remembered);
            // A temp file renamed onto a name that just disappeared (Excel, LibreOffice, atomic writers): the temp may
            // already sit in the Inbox from its Created event; drop it and put the returning name back (M3a review I1).
            case DesktopChange.Renamed renamed
                when FindOwner(config, renamed.NewRef) is null
                     && remembered.Any(removal => ItemRef.Comparer.Equals(removal.ItemRef, renamed.NewRef)):
                return (Return(RemoveItem(config, renamed.OldRef), renamed.NewRef, remembered), remembered);
            case DesktopChange.Renamed renamed when FindOwner(config, renamed.OldRef) is { } owner:
                // Word renames the original away before renaming its temp file onto the old name: remember the old name too.
                var oldIndex = owner.Items.ToList().FindIndex(existing => ItemRef.Comparer.Equals(existing, renamed.OldRef));
                remembered.Add(new RememberedPlacement(renamed.OldRef, owner.Id, oldIndex, now));
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
    private static NeoFencesConfig Return(NeoFencesConfig config, string itemRef, List<RememberedPlacement> remembered)
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

    /// <summary>
    /// Drag-drop of fence items (M3b). Moves them, in the order they had in their fences (not the selection order), to
    /// <paramref name="toFenceId"/>, before the item shown at <paramref name="insertAt"/> in the target as displayed
    /// during the drag (still including the dragged items). Past the end appends; unknown refs are ignored.
    /// </summary>
    /// <exception cref="ArgumentException">The target is unknown or a Portal fence.</exception>
    public static NeoFencesConfig MoveItems(NeoFencesConfig config, IReadOnlyList<string> itemRefs, string toFenceId, int insertAt)
    {
        var target = RequireDesktopFence(config, toFenceId);
        var moving = new HashSet<string>(itemRefs, ItemRef.Comparer);
        var ordered = config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop)
            .SelectMany(fence => fence.Items).Where(moving.Contains).ToList();
        if (ordered.Count == 0) return config;

        var displayedIndex = Math.Clamp(insertAt, 0, target.Items.Count);
        var movingBeforeDrop = target.Items.Take(displayedIndex).Count(moving.Contains);
        var withoutMoving = config with
        {
            Fences = config.Fences.Select(fence => fence.Source.Kind == FenceSourceKind.Desktop
                ? fence with { Items = fence.Items.Where(itemRef => !moving.Contains(itemRef)).ToList() }
                : fence).ToList(),
        };
        var remainingTarget = withoutMoving.Fences.First(fence => fence.Id == toFenceId);
        var items = remainingTarget.Items.ToList();
        items.InsertRange(displayedIndex - movingBeforeDrop, ordered);
        return withoutMoving.WithFence(remainingTarget with { Items = items });
    }

    /// <summary>
    /// Files were dropped onto a fence from outside (Explorer) and Windows is copying or moving them to the Desktop:
    /// when each appears within <see cref="SafeSaveWindow"/> it goes to that fence at the drop position, in order.
    /// </summary>
    public static IReadOnlyList<RememberedPlacement> ExpectArrivals(
        IReadOnlyList<RememberedPlacement> recent, IReadOnlyList<string> itemRefs, string fenceId, int insertAt, DateTimeOffset now) =>
        [.. recent.Where(placement => now - placement.RememberedAt <= SafeSaveWindow),
         .. itemRefs.Select((itemRef, offset) => new RememberedPlacement(itemRef, fenceId, insertAt + offset, now))];

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
In `src/NeoFences.App/FenceHost.cs`, replace `RecentRemoval` with `RememberedPlacement`, so the solution keeps building. Task 3 replaces the whole file anyway.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 147`

- [ ] **Step 6: Commit**

```powershell
git add -A src/NeoFences.Core src/NeoFences.App tests/NeoFences.Core.Tests
git commit -m "feat: added drop-index moves and expected drop arrivals to core"
```

---

### Task 2: Shell — drag source and fence drop target

**Files:**
- Create: `src/NeoFences.Shell/DesktopNamespace.cs`, `src/NeoFences.Shell/ShellDragDrop.cs`
- Modify (full new content): `src/NeoFences.Shell/ShellItemMenu.cs`, `src/NeoFences.Shell/NativeMethods.txt`

**Interfaces:**
- Produces:
  - `record struct FenceDropPoint(string? ItemRef, int InsertAt)`;
  - `record FenceDropHandlers(Func<int,int,FenceDropPoint> HitTest, Action<IReadOnlyList<string>,int> MoveItems, Action<IReadOnlyList<string>,int> ExpectArrivals, Action<Exception> LogFailure)`;
  - `ShellDragDrop.TryDrag(nint ownerHandle, IReadOnlyList<string> itemRefs, Action<Exception> logFailure) -> bool`;
  - `ShellDragDrop.RegisterFence(nint fenceHandle, FenceDropHandlers handlers) -> IDisposable`.

  `ShellItemMenu` now uses `DesktopNamespace` (no behaviour change).

Notes for the implementer:
- `SHDoDragDrop` takes `System.Runtime.InteropServices.ComTypes.IDataObject`, while CsWin32's `IDropTarget` uses its own `IDataObject`. Both wrap the same COM object.
- `SFGAO_FLAGS` lives in `Windows.Win32.System.SystemServices`.
- `CLSID_DragDropHelper` is a constant; create the helper with `Type.GetTypeFromCLSID`.

- [ ] **Step 1: Files**

`src/NeoFences.Shell/NativeMethods.txt`:
```
// Win32 APIs used by NeoFences.Shell. CsWin32 generates bindings for each name.
AllowSetForegroundWindow
BITMAP
BITMAPINFO
CLIPBOARD_FORMAT
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
CLSID_DragDropHelper
DragQueryFile
DROPEFFECT
DVASPECT
EnumDisplayDevices
EnumDisplayMonitors
FileOperation
FILEOPERATION_FLAGS
FindWindow
FOLDERFLAGS
FORMATETC
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
HDROP
HWND_BOTTOM
IContextMenu
IContextMenu2
IContextMenu3
IDataObject
IDropTarget
IDropTargetHelper
IFileOperation
IFolderView2
IServiceProvider
IShellBrowser
IShellFolder
IShellItem
IShellItemImageFactory
IShellView
IShellWindows
MODIFIERKEYS_FLAGS
MONITOR_DPI_TYPE
MONITORINFOEXW
MONITORINFOF_PRIMARY
POINTL
RegisterDragDrop
RegisterWindowMessage
ReleaseDC
ReleaseStgMedium
RevokeDragDrop
SET_WINDOW_POS_FLAGS
SetForegroundWindow
SetWindowLongPtr
SetWindowPos
SetWindowRgn
SFGAO_FLAGS
SHCreateItemFromParsingName
SHDoDragDrop
ShellWindows
SHGetDesktopFolder
SHOW_WINDOW_CMD
SID_STopLevelBrowser
SIGDN
SIIGBF
STGMEDIUM
SYSTEM_METRICS_INDEX
TRACK_POPUP_MENU_FLAGS
TrackPopupMenuEx
TYMED
WINDOW_EX_STYLE
WINDOW_LONG_PTR_INDEX
WINDOW_STYLE
WINDOWPOS
```
`src/NeoFences.Shell/DesktopNamespace.cs`:
```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace NeoFences.Shell;

/// <summary>
/// Desktop items as children of the desktop shell folder, which merges the user's and the Public Desktop: a file name
/// (or <c>::{CLSID}</c>) is a direct child. Item menus, drag data and per-item drop targets all come from here.
/// </summary>
internal static class DesktopNamespace
{
    /// <summary>A UI object (IContextMenu, IDataObject, IDropTarget, …) for these items; the caller releases it.</summary>
    public static unsafe object GetUIObject(HWND owner, IReadOnlyList<string> itemRefs, Guid interfaceId)
    {
        PInvoke.SHGetDesktopFolder(out var desktop).ThrowOnFailure();
        var childIds = ChildIds(owner, desktop, itemRefs);
        try
        {
            fixed (nint* ids = childIds.ToArray())
            {
                desktop.GetUIObjectOf(owner, (uint)childIds.Count, (ITEMIDLIST**)ids, &interfaceId, null, out var uiObject);
                return uiObject;
            }
        }
        finally
        {
            foreach (var childId in childIds) Marshal.FreeCoTaskMem(childId);
        }
    }

    /// <summary>The Desktop folder's own drop target: what Explorer does when something is dropped on the desktop.</summary>
    public static unsafe object DesktopDropTarget(HWND owner)
    {
        PInvoke.SHGetDesktopFolder(out var desktop).ThrowOnFailure();
        var dropTargetId = typeof(Windows.Win32.System.Ole.IDropTarget).GUID;
        desktop.CreateViewObject(owner, &dropTargetId, out var dropTarget);
        return dropTarget;
    }

    /// <summary>
    /// True for containers that take drops themselves: folders and Recycle Bin. Files (even ones Windows lists as drop
    /// targets, like programs or text files) are not: dropping on them reorders the fence instead (M3b).
    /// </summary>
    public static unsafe bool IsDropContainer(HWND owner, string itemRef)
    {
        try
        {
            PInvoke.SHGetDesktopFolder(out var desktop).ThrowOnFailure();
            var childIds = ChildIds(owner, desktop, [itemRef]);
            try
            {
                var attributes = (uint)(SFGAO_FLAGS.SFGAO_DROPTARGET | SFGAO_FLAGS.SFGAO_FOLDER);
                fixed (nint* ids = childIds.ToArray())
                {
                    desktop.GetAttributesOf(1, (ITEMIDLIST**)ids, ref attributes);
                }
                const uint Container = (uint)(SFGAO_FLAGS.SFGAO_DROPTARGET | SFGAO_FLAGS.SFGAO_FOLDER);
                return (attributes & Container) == Container;
            }
            finally
            {
                foreach (var childId in childIds) Marshal.FreeCoTaskMem(childId);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return false;
        }
    }

    private static unsafe List<nint> ChildIds(HWND owner, IShellFolder desktop, IReadOnlyList<string> itemRefs)
    {
        var childIds = new List<nint>();
        try
        {
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
            return childIds;
        }
        catch
        {
            foreach (var childId in childIds) Marshal.FreeCoTaskMem(childId);
            throw;
        }
    }
}
```
`src/NeoFences.Shell/ShellDragDrop.cs`:
```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace NeoFences.Shell;

/// <summary>Where a drop on a fence lands, asked of the fence for a screen point (physical pixels).</summary>
/// <param name="ItemRef">The item under the point, if any.</param>
/// <param name="InsertAt">Index in the fence's shown list to insert before (see FenceMembership.MoveItems).</param>
public readonly record struct FenceDropPoint(string? ItemRef, int InsertAt);

/// <summary>What a fence does with drops; NeoFences.App supplies these (all called on the UI thread).</summary>
/// <param name="HitTest">Item and insert position under a screen point.</param>
/// <param name="MoveItems">Desktop items dropped on the fence (from a fence or from Explorer's Desktop): membership only, no file operation.</param>
/// <param name="ExpectArrivals">Desktop refs Windows is about to copy or move onto the Desktop for this drop, and where they go.</param>
/// <param name="LogFailure">A drop that could not be handed to Windows.</param>
public sealed record FenceDropHandlers(
    Func<int, int, FenceDropPoint> HitTest,
    Action<IReadOnlyList<string>, int> MoveItems,
    Action<IReadOnlyList<string>, int> ExpectArrivals,
    Action<Exception> LogFailure);

/// <summary>
/// Drag-drop with Windows' own engine (spec §6, M3b). Dragging out uses the shell's data object for the items, so
/// apps and Explorer get real files (copy/move/link as they decide) and Windows draws the drag image. Drops on a fence:
/// <list type="bullet">
/// <item>desktop items (from any fence, or Explorer showing the Desktop folder) only change membership: no file operation;</item>
/// <item>on an item that takes drops (folder, Recycle Bin, program) they go to that item, as on the desktop;</item>
/// <item>anything else goes to the Desktop folder's own drop target (Windows copies/moves, with progress and Undo),
/// and the arriving files are placed in this fence at the drop position.</item>
/// </list>
/// </summary>
public static class ShellDragDrop
{
    /// <summary>Items being dragged out of a fence right now (one drag at a time; drops on fences are membership moves).</summary>
    internal static IReadOnlyList<string>? CurrentDrag { get; private set; }

    /// <summary>Starts a drag of these items; returns when it ends. The drag image, cursor and effects are Windows'.</summary>
    /// <returns>False when the drag could not start (an item vanished, a broken shell extension).</returns>
    public static unsafe bool TryDrag(nint ownerHandle, IReadOnlyList<string> itemRefs, Action<Exception> logFailure)
    {
        ComDataObject? dataObject = null;
        try
        {
            dataObject = (ComDataObject)DesktopNamespace.GetUIObject((HWND)ownerHandle, itemRefs, typeof(ComDataObject).GUID);
            CurrentDrag = itemRefs;
            PInvoke.SHDoDragDrop((HWND)ownerHandle, dataObject, null,
                DROPEFFECT.DROPEFFECT_COPY | DROPEFFECT.DROPEFFECT_MOVE | DROPEFFECT.DROPEFFECT_LINK, out _);
            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            logFailure(failure);
            return false;
        }
        finally
        {
            CurrentDrag = null;
            if (dataObject is not null) Marshal.ReleaseComObject(dataObject);
        }
    }

    /// <summary>Makes the fence window a drop target. Dispose (before the window is destroyed) to unregister.</summary>
    public static IDisposable RegisterFence(nint fenceHandle, FenceDropHandlers handlers)
    {
        var target = new FenceDropTarget((HWND)fenceHandle, handlers);
        // WPF registers its own drop target on every window; NeoFences does not use WPF drag-drop, so replace it.
        PInvoke.RevokeDragDrop((HWND)fenceHandle);
        PInvoke.RegisterDragDrop((HWND)fenceHandle, target).ThrowOnFailure();
        return target;
    }

    /// <summary>Paths in the drop's file list (CF_HDROP); empty for virtual items (zip contents, phones).</summary>
    internal static unsafe List<string> DroppedFiles(IDataObject dataObject)
    {
        var files = new List<string>();
        var format = new FORMATETC
        {
            cfFormat = (ushort)CLIPBOARD_FORMAT.CF_HDROP,
            dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = (uint)TYMED.TYMED_HGLOBAL,
        };
        STGMEDIUM medium = default;
        try
        {
            dataObject.GetData(&format, out medium);
            var drop = (HDROP)(nint)medium.u.hGlobal.Value;
            var count = PInvoke.DragQueryFile(drop, uint.MaxValue, default, 0);
            var buffer = new char[32768];
            for (uint index = 0; index < count; index++)
            {
                var length = PInvoke.DragQueryFile(drop, index, buffer);
                files.Add(new string(buffer, 0, (int)length));
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // No file list: nothing to place; Windows still handles the drop.
        }
        finally
        {
            if (medium.u.hGlobal.Value != null) PInvoke.ReleaseStgMedium(ref medium);
        }
        return files;
    }

    /// <summary>
    /// IDropTarget for one fence. Forwards to the shell (the hovered item's drop target, or the Desktop folder's) for
    /// real file drops, and keeps Windows' drag image visible over the fence (IDropTargetHelper).
    /// </summary>
    private sealed unsafe class FenceDropTarget(HWND fence, FenceDropHandlers handlers) : IDropTarget, IDisposable
    {
        private IDataObject? _dataObject;
        private bool _desktopItemsOnly;      // a fence item, or files that already sit on the Desktop
        private string? _hoveredItem;        // item whose own drop target is active
        private IDropTarget? _shellTarget;   // forwarded-to target while it is entered
        private IDropTargetHelper? _imageHelper;

        public void DragEnter(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            _dataObject = pDataObj;
            _desktopItemsOnly = CurrentDrag is not null || IsAllDesktopFiles(DroppedFiles(pDataObj));
            _imageHelper = TryCreateImageHelper();
            var allowed = *pdwEffect;
            Update(grfKeyState, pt, pdwEffect, allowed);
            if (_imageHelper is not null)
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                _imageHelper.DragEnter(fence, pDataObj, &point, *pdwEffect);
            }
        }

        public void DragOver(MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            Update(grfKeyState, pt, pdwEffect, *pdwEffect);
            if (_imageHelper is not null)
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                _imageHelper.DragOver(&point, *pdwEffect);
            }
        }

        public void DragLeave()
        {
            LeaveShellTarget();
            _imageHelper?.DragLeave();
            Reset();
        }

        public void Drop(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            var allowed = *pdwEffect;
            try
            {
                Update(grfKeyState, pt, pdwEffect, allowed);
                if (_imageHelper is not null)
                {
                    var point = new System.Drawing.Point(pt.x, pt.y);
                    _imageHelper.Drop(pDataObj, &point, *pdwEffect);
                }
                var drop = handlers.HitTest(pt.x, pt.y);
                    if (_shellTarget is null)
                {
                    // Membership only. Report "none" so the source never deletes anything after a "move".
                    handlers.MoveItems(CurrentDrag ?? DroppedFiles(pDataObj), drop.InsertAt);
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
                    return;
                }
                if (_hoveredItem is null) handlers.ExpectArrivals(DesktopRefsFor(DroppedFiles(pDataObj)), drop.InsertAt);
                    var target = _shellTarget;
                _shellTarget = null; // Drop replaces DragLeave for it
                target.Drop(pDataObj, grfKeyState, pt, pdwEffect);
                Marshal.ReleaseComObject(target);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                    handlers.LogFailure(failure);
                *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
            }
            finally
            {
                LeaveShellTarget();
                Reset();
            }
        }

        public void Dispose()
        {
            PInvoke.RevokeDragDrop(fence);
            Reset();
        }

        /// <summary>Picks where the drag would land now and asks it for the effect (or computes ours).</summary>
        private void Update(MODIFIERKEYS_FLAGS keys, POINTL pt, DROPEFFECT* effect, DROPEFFECT allowed)
        {
            try
            {
                var hovered = handlers.HitTest(pt.x, pt.y).ItemRef;
                var dropsOnItem = hovered is not null && !(CurrentDrag?.Contains(hovered, StringComparer.OrdinalIgnoreCase) ?? false)
                                  && DesktopNamespace.IsDropContainer(fence, hovered);
                var wantedItem = dropsOnItem ? hovered : null;
                var wantsShell = dropsOnItem || !_desktopItemsOnly;

                if (_shellTarget is not null && (!wantsShell || wantedItem != _hoveredItem)) LeaveShellTarget();
                if (!wantsShell)
                {
                    *effect = allowed & DROPEFFECT.DROPEFFECT_MOVE; // a membership move; nothing on disk changes
                    return;
                }
                if (_shellTarget is null)
                {
                    _shellTarget = (IDropTarget)(wantedItem is null
                        ? DesktopNamespace.DesktopDropTarget(fence)
                        : DesktopNamespace.GetUIObject(fence, [wantedItem], typeof(IDropTarget).GUID));
                    _hoveredItem = wantedItem;
                    *effect = allowed;
                    _shellTarget.DragEnter(_dataObject!, keys, pt, effect);
                    return;
                }
                *effect = allowed;
                _shellTarget.DragOver(keys, pt, effect);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                handlers.LogFailure(failure);
                *effect = DROPEFFECT.DROPEFFECT_NONE;
            }
        }

        private void LeaveShellTarget()
        {
            if (_shellTarget is null) return;
            try { _shellTarget.DragLeave(); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
            Marshal.ReleaseComObject(_shellTarget);
            _shellTarget = null;
            _hoveredItem = null;
        }

        private void Reset()
        {
            _dataObject = null;
            _desktopItemsOnly = false;
            if (_imageHelper is not null) Marshal.ReleaseComObject(_imageHelper);
            _imageHelper = null;
        }

        private static IDropTargetHelper? TryCreateImageHelper()
        {
            try
            {
                return (IDropTargetHelper)Activator.CreateInstance(Type.GetTypeFromCLSID(PInvoke.CLSID_DragDropHelper)!)!;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                return null; // no drag image over fences; the drop still works
            }
        }

        private static bool IsAllDesktopFiles(List<string> files) =>
            files.Count > 0 && files.All(file => IsOnDesktop(Path.GetDirectoryName(file)));

        private static bool IsOnDesktop(string? directory) =>
            directory is not null
            && (string.Equals(directory, DesktopItems.UserDesktop, StringComparison.OrdinalIgnoreCase)
                || string.Equals(directory, DesktopItems.PublicDesktop, StringComparison.OrdinalIgnoreCase));

        /// <summary>Where Windows will put dropped files: the user's Desktop, same names (renamed copies go to the Inbox).</summary>
        private static List<string> DesktopRefsFor(List<string> files) =>
            files.Select(file => Path.Combine(DesktopItems.UserDesktop, Path.GetFileName(file))).ToList();
    }
}
```
`src/NeoFences.Shell/ShellItemMenu.cs`:
```csharp
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
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
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // CsWin32 turns an extension's failure HRESULT into NotImplementedException, InvalidCastException, … (M3a review C1).
            return false;
        }
    }

    /// <summary>
    /// Shows the menu for these items at a screen point and runs the chosen shell command.
    /// </summary>
    /// <param name="extended">Shift held: the extended menu ("Copy as path", "Open PowerShell here", …).</param>
    /// <returns>Rename/Delete for the caller to run; None when the shell ran the command, the user cancelled, or the menu could not be built.</returns>
    /// <param name="logFailure">Told when the menu could not be built or a command failed (a broken shell extension).</param>
    public static unsafe ItemMenuChoice Show(nint ownerHandle, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended, Action<Exception> logFailure)
    {
        var owner = (HWND)ownerHandle;
        HMENU menu = default;
        IContextMenu? contextMenu = null;
        try
        {
            if (itemRefs.Count == 0) return ItemMenuChoice.None;
            contextMenu = (IContextMenu)DesktopNamespace.GetUIObject(owner, itemRefs, typeof(IContextMenu).GUID);

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
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            logFailure(failure); // the item vanished or a shell extension failed: no menu or no command, nothing lost
            return ItemMenuChoice.None;
        }
        finally
        {
            _openMenu = null;
            if (!menu.IsNull) PInvoke.DestroyMenu(menu);
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
            var verb = new ReadOnlySpan<char>(buffer, Length); // bounded: an extension may fill it without a terminator
            var end = verb.IndexOf('\0');
            return new string(end >= 0 ? verb[..end] : verb);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null; // many commands have no verb (E_NOTIMPL, E_INVALIDARG): they still run through Invoke
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

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 147`.

- [ ] **Step 3: Commit**

```powershell
git add src/NeoFences.Shell
git commit -m "feat: added shell drag source and fence drop target"
```

---

### Task 3: App — drag start, drop hit test, rubber band

**Files:** replace `src/NeoFences.App/FenceWindow.xaml`, `FenceWindow.xaml.cs`, `FenceHost.cs` with the content below.

**Interfaces:**
- Consumes: Tasks 1 and 2.
- Produces: `FenceWindow.HitTest(int screenX, int screenY) -> FenceDropPoint` and the event `DragRequested(IReadOnlyList<string>)`. `FenceHost` keeps `_rememberedPlacements` and one drop registration per fence, which is disposed when the fence is deleted.

- [ ] **Step 1: Replace the three files**

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
                <Grid>
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
                    <!-- Rubber-band selection (M3b): drawn while dragging on empty space. -->
                    <Canvas IsHitTestVisible="False">
                        <Rectangle x:Name="SelectionBand" Visibility="Collapsed" Fill="{DynamicResource FenceHover}"
                                   Stroke="{DynamicResource FenceSubtleText}" StrokeThickness="1" RadiusX="2" RadiusY="2" />
                    </Canvas>
                </Grid>
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
    private const int WmWindowPosChanging = 0x0046;
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
    /// <summary>The user started dragging these items out of the fence (M3b).</summary>
    public event Action<IReadOnlyList<string>>? DragRequested;

    private Point? _pressPoint;                 // left button pressed on an item: a drag may start
    private ListBoxItem? _deferredSelect;       // pressed on an already selected item: select it alone only on release
    private Point? _bandStart;                  // left button pressed on empty space: rubber band

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
        ItemList.PreviewMouseLeftButtonDown += OnListPress;
        ItemList.PreviewMouseMove += OnListMove;
        ItemList.PreviewMouseLeftButtonUp += OnListRelease;
        ItemList.LostMouseCapture += (_, _) => EndBand();
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
        CancelItemRenames();
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

    /// <summary>
    /// A rebuild regenerates every item: an open rename box would lose focus (and commit half-typed text) or come back
    /// and swallow the next keystroke. Cancel it first; with IsEditing already false the focus loss commits nothing
    /// (M3a review I3).
    /// </summary>
    private void CancelItemRenames()
    {
        foreach (var view in _items.Where(view => view.IsEditing)) view.IsEditing = false;
    }

    private void ReloadIcons()
    {
        CancelItemRenames();
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

    /// <summary>The item under a screen point (physical pixels) and the index to insert before when dropping there.</summary>
    public FenceDropPoint HitTest(int screenX, int screenY)
    {
        var point = ItemList.PointFromScreen(new Point(screenX, screenY));
        string? hovered = null;
        var insertAt = _items.Count;
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            if (bounds.Contains(point)) hovered = _items[index].ItemRef;
            // Reading order (rows, then left to right): the first item that comes after the point.
            var sameRow = point.Y >= bounds.Top && point.Y < bounds.Bottom;
            if (point.Y < bounds.Top || (sameRow && point.X < bounds.Left + bounds.Width / 2))
            {
                insertAt = Math.Min(insertAt, index);
            }
        }
        return new FenceDropPoint(hovered, insertAt);
    }

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject source) where TAncestor : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TAncestor match) return match;
        }
        return null;
    }

    private void OnListPress(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null) return;
        if (args.OriginalSource is TextBox) return;
        var container = args.OriginalSource is DependencyObject element ? FindAncestor<ListBoxItem>(element) : null;
        if (container is null)
        {
            // Empty space: rubber band. Without Ctrl it starts a new selection.
            _bandStart = args.GetPosition(ItemList);
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ItemList.SelectedItems.Clear();
            ItemList.CaptureMouse();
            args.Handled = true;
            return;
        }
        _pressPoint = args.GetPosition(ItemList);
        // Pressing one of several selected items must keep the selection, so they can be dragged together.
        if (container.IsSelected && ItemList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None && args.ClickCount == 1)
        {
            _deferredSelect = container;
            container.Focus();
            args.Handled = true;
        }
    }

    private void OnListMove(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed)
        {
            _pressPoint = null;
            return;
        }
        var position = args.GetPosition(ItemList);
        if (_bandStart is { } bandStart)
        {
            UpdateBand(bandStart, position);
            return;
        }
        if (_pressPoint is not { } pressPoint) return;
        if (Math.Abs(position.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressPoint = null;
        _deferredSelect = null;
        var dragged = ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).ToList();
        if (dragged.Count > 0) DragRequested?.Invoke(dragged); // returns when the drag ends (Windows' modal loop)
    }

    private void OnListRelease(object sender, MouseButtonEventArgs args)
    {
        _pressPoint = null;
        if (_deferredSelect is { } container)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
            _deferredSelect = null;
        }
        if (_bandStart is not null)
        {
            ItemList.ReleaseMouseCapture(); // ends the band via LostMouseCapture
            args.Handled = true;
        }
    }

    /// <summary>Selects every item the band touches (added to the selection when Ctrl was held at the start).</summary>
    private void UpdateBand(Point start, Point current)
    {
        var band = new Rect(start, current);
        Canvas.SetLeft(SelectionBand, band.Left);
        Canvas.SetTop(SelectionBand, band.Top);
        SelectionBand.Width = band.Width;
        SelectionBand.Height = band.Height;
        SelectionBand.Visibility = Visibility.Visible;
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            if (bounds.IntersectsWith(band)) container.IsSelected = true;
            else if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) container.IsSelected = false;
        }
    }

    private void EndBand()
    {
        _bandStart = null;
        SelectionBand.Visibility = Visibility.Collapsed;
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
            case WmWindowPosChanging:
                FenceWindowChrome.KeepAtBottom(lParam); // fences never rise above apps (Peek in M5 will lift this)
                break;
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
    // Safe-save memory and expected drop arrivals (FenceMembership.SafeSaveWindow).
    private IReadOnlyList<RememberedPlacement> _rememberedPlacements = [];
    private readonly Dictionary<string, IDisposable> _dropRegistrations = new(StringComparer.Ordinal);
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
        window.DragRequested += itemRefs =>
            ShellDragDrop.TryDrag(window.Handle, itemRefs, logFailure: failure => Log.Warning(failure, "could not start dragging {ItemRefs}", itemRefs));
        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        RegisterDrops(window); // needs the HWND
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
        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, DateTimeOffset.Now);
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

    /// <summary>Makes the fence accept drops (M3b): fence items and Desktop files move membership; other files go to Windows.</summary>
    private void RegisterDrops(FenceWindow window)
    {
        try
        {
            _dropRegistrations[window.FenceId] = ShellDragDrop.RegisterFence(window.Handle, new FenceDropHandlers(
                HitTest: window.HitTest,
                MoveItems: (itemRefs, insertAt) =>
                {
                    _config = FenceMembership.MoveItems(_config, itemRefs, window.FenceId, insertAt);
                    RefreshWindows();
                    ScheduleSave();
                },
                ExpectArrivals: (itemRefs, insertAt) =>
                    _rememberedPlacements = FenceMembership.ExpectArrivals(_rememberedPlacements, itemRefs, window.FenceId, insertAt, DateTimeOffset.Now),
                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId)));
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "fence {FenceId} cannot accept drops", window.FenceId); // degrade: everything else still works
        }
    }

    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY)
    {
        var extended = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs));
        switch (choice)
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
        if (_dropRegistrations.Remove(window.FenceId, out var registration)) registration.Dispose();
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
Expected: 0 warnings, 0 errors; `Passed: 147`. The log has no `cannot accept drops` lines after a start.

- [ ] **Step 3: Live smoke (ask first: mouse for about 90 s, one test file to the Recycle Bin; refocuses Windows Terminal)**

Save as `<scratchpad>\m3b-smoke.ps1`, then run `& "<scratchpad>\m3b-smoke.ps1" -Scratch "<scratchpad>"`:
```powershell
# M3b live smoke: drag-drop within/between fences, in from Explorer, out to Explorer, onto Recycle Bin, rubber band.
# Uses only zz-m3b-* files (Desktop + a temp folder); needs a fence titled "Games" besides the Inbox; refocuses Terminal.
param([string]$Exe = "F:\projects\neo_fences\src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe", [string]$Scratch = $env:TEMP)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace NfM3b -Name N -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
[DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
'@
function Pause-Ms([int]$ms) { [System.Threading.Thread]::Sleep($ms) }
function Key([byte]$vk) { [NfM3b.N]::keybd_event($vk,0,0,[UIntPtr]::Zero); [NfM3b.N]::keybd_event($vk,0,2,[UIntPtr]::Zero); Pause-Ms 150 }
function Click([int]$x, [int]$y) { [void][NfM3b.N]::SetCursorPos($x, $y); Pause-Ms 150; [NfM3b.N]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [NfM3b.N]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Pause-Ms 400 }
# A hand-speed drag: press, move in small steps (OLE's drag loop polls the mouse), release.
function Drag([int]$fromX, [int]$fromY, [int]$toX, [int]$toY) {
  [void][NfM3b.N]::SetCursorPos($fromX, $fromY); Pause-Ms 200; [NfM3b.N]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Pause-Ms 200
  foreach ($step in 1..30) { [void][NfM3b.N]::SetCursorPos([int]($fromX + ($toX - $fromX) * $step / 30), [int]($fromY + ($toY - $fromY) * $step / 30)); Pause-Ms 25 }
  Pause-Ms 400; [NfM3b.N]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Pause-Ms 1500 }
function Foreground { $text = New-Object System.Text.StringBuilder 256; [void][NfM3b.N]::GetWindowText([NfM3b.N]::GetForegroundWindow(), $text, 256); "$text" }
function Wait-Until([scriptblock]$condition, [int]$seconds = 8) { $deadline = (Get-Date).AddSeconds($seconds); while (-not (& $condition) -and (Get-Date) -lt $deadline) { Pause-Ms 250 }; [bool](& $condition) }
function Config { Get-Content "$env:LOCALAPPDATA\NeoFences\config.json" -Raw | ConvertFrom-Json }
function Fence-Items([string]$title) { @(((Config).fences | Where-Object { $_.title -eq $title -or ($title -eq 'Inbox' -and $_.isInbox) } | Select-Object -First 1).items) }
function Fence-Rect([string]$title) { $c = Config; $f = $c.fences | Where-Object title -eq $title | Select-Object -First 1; $r = $c.layouts.($c.lastLayoutFingerprint).fences.($f.id); [pscustomobject]@{ X = [int]$r.x; Y = [int]$r.y; W = [int]$r.w; H = [int]$r.h } }
function Windows-Titled([string]$title) { $found = New-Object System.Collections.Generic.List[IntPtr]
  [void][NfM3b.N]::EnumWindows({ param($h, $l) if ([NfM3b.N]::IsWindowVisible($h)) { $s = New-Object System.Text.StringBuilder 128; [void][NfM3b.N]::GetWindowText($h, $s, 128); if ("$s" -like $title) { $found.Add($h) } }; $true }, [IntPtr]::Zero); $found }
function Element-Centre([IntPtr[]]$windows, [string]$name) {
  $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
  foreach ($window in $windows) { $item = [System.Windows.Automation.AutomationElement]::FromHandle($window).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if (-not $item) { continue }; try { $item.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView(); Pause-Ms 300 } catch { }
    $box = $item.Current.BoundingRectangle; return [pscustomobject]@{ X = [int]($box.X + $box.Width / 2); Y = [int]($box.Y + $box.Height / 3); Element = $item } }
  $null }
function Item-Centre([string]$label) { Element-Centre (Windows-Titled 'NeoFences fence') $label }

$desk = [Environment]::GetFolderPath('Desktop'); $shell = New-Object -ComObject Shell.Application
foreach ($stale in (Windows-Titled 'm3b-drag*')) { [void][NfM3b.N]::PostMessage($stale, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) }; Pause-Ms 800
$outside = Join-Path $env:TEMP 'm3b-drag'; New-Item -ItemType Directory -Force $outside | Out-Null
foreach ($name in 'zz-m3b-a.txt', 'zz-m3b-b.txt') { Set-Content "$desk\$name" "m3b smoke" }
"test files in Inbox: " + (Wait-Until { (Fence-Items 'Inbox') -contains "$desk\zz-m3b-b.txt" })
"Games starts empty: $((Fence-Items 'Games').Count -eq 0)"
$shell.MinimizeAll(); Pause-Ms 1500

# 1. Reorder within the Inbox: a dropped on b's right half lands after b.
$b = Item-Centre 'zz-m3b-b.txt'; $a = Item-Centre 'zz-m3b-a.txt'; $b = Item-Centre 'zz-m3b-b.txt' # scrolling settles before reading both
if (-not ($a -and $b)) { "test items not found; aborting"; return }
Drag $a.X $a.Y ($b.X + 30) $b.Y
$inbox = Fence-Items 'Inbox'
"reorder: b before a $([array]::IndexOf($inbox, "$desk\zz-m3b-b.txt") -lt [array]::IndexOf($inbox, "$desk\zz-m3b-a.txt"))"

# 2. Between fences: a into Games; the file stays on the Desktop (membership only).
$games = Fence-Rect 'Games'; $a = Item-Centre 'zz-m3b-a.txt'
Drag $a.X $a.Y ($games.X + 160) ($games.Y + 120)
"into Games: $((Fence-Items 'Games') -contains "$desk\zz-m3b-a.txt"), file still on the Desktop $(Test-Path "$desk\zz-m3b-a.txt")"

# 3. An Explorer window for dragging out. (Dropping in from Explorer cannot be scripted: Explorer ignores synthetic drag
#    gestures, and a helper process's OLE drag does not run either; that path is checklist K4, by hand.)
Start-Process explorer.exe $outside
"Explorer window: " + (Wait-Until { (Windows-Titled 'm3b-drag*').Count -gt 0 })
$explorerWindow = (Windows-Titled 'm3b-drag*')[0]; if (-not $explorerWindow) { "no Explorer window; aborting"; return }
[void][NfM3b.N]::SetWindowPos($explorerWindow, [IntPtr]::Zero, 40, 300, 760, 560, 0x0040); Pause-Ms 1500

# 4. Rubber band over Games selects both items.
Drag ($games.X + 6) ($games.Y + 40) ($games.X + $games.W - 12) ($games.Y + $games.H - 8)
$inGames = @((Fence-Items 'Games') | ForEach-Object { Split-Path $_ -Leaf })
$selected = @($inGames | ForEach-Object { $found = Item-Centre $_; if ($found) { $found.Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } else { $false } })
"rubber band selected all $($inGames.Count) item(s) in Games: $($selected.Count -gt 0 -and -not ($selected -contains $false))"

# 5. Out to Explorer: Windows moves the file into the folder; it leaves the Desktop and the fence.
Click ($games.X + 160) ($games.Y + 205)   # clear the selection (empty space)
$b = Item-Centre 'zz-m3b-b.txt'
if ($b) {
  Drag $b.X $b.Y 420 760
  "out to Explorer: in the folder " + (Wait-Until { Test-Path "$outside\zz-m3b-b.txt" }) + ", left the Desktop " + (Wait-Until { -not (Test-Path "$desk\zz-m3b-b.txt") }) + ", gone from the Inbox " + (Wait-Until { -not ((Fence-Items 'Inbox') -contains "$desk\zz-m3b-b.txt") })
} else { "out to Explorer: b not found" }

# 6. Onto the Recycle Bin item: Windows recycles the file.
$bin = Item-Centre 'Recycle Bin'; $a = Item-Centre 'zz-m3b-a.txt'
if ($a -and $bin) { Drag $a.X $a.Y $bin.X $bin.Y } else { "Recycle Bin or a not found" }
if ((Foreground) -match 'Delete') { "Windows asked: '$(Foreground)'"; Key 0x0D }
"onto Recycle Bin: gone from the Desktop " + (Wait-Until { -not (Test-Path "$desk\zz-m3b-a.txt") }) + ", in Recycle Bin " + [bool]($shell.NameSpace(10).Items() | Where-Object Name -like 'zz-m3b-a*')

# Clean up our files, close Explorer, restore windows, refocus Windows Terminal.
Get-ChildItem $desk | Where-Object Name -like 'zz-m3b-*' | ForEach-Object { [System.IO.File]::Delete($_.FullName) }
if ($explorerWindow) { [void][NfM3b.N]::PostMessage($explorerWindow, 0x10, [IntPtr]::Zero, [IntPtr]::Zero); Pause-Ms 500 }
Remove-Item -Recurse -Force $outside -ErrorAction SilentlyContinue
"cleaned: Games empty " + (Wait-Until { (Fence-Items 'Games').Count -eq 0 }) + ", no test files in fences " + (Wait-Until { -not (((Config).fences.items) -like '*zz-m3b*') })
$shell.UndoMinimizeALL(); Pause-Ms 1000
$terminal = Get-Process WindowsTerminal -ErrorAction SilentlyContinue | Select-Object -First 1
if ($terminal) { [NfM3b.N]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [void](New-Object -ComObject WScript.Shell).AppActivate($terminal.Id); [NfM3b.N]::keybd_event(0x12,0,2,[UIntPtr]::Zero) }
"NeoFences alive: " + [bool](Get-CimInstance Win32_Process -Filter "Name='NeoFences.exe'" | Where-Object { $_.CommandLine -notmatch '--watchdog' })
```
Expected:
- `reorder: b before a True`;
- `into Games: True, file still on the Desktop True`;
- `rubber band selected all 1 item(s) in Games: True`;
- `out to Explorer: in the folder True, left the Desktop True, gone from the Inbox True`;
- `onto Recycle Bin: gone from the Desktop True, in Recycle Bin True`;
- `cleaned: … True`;
- `NeoFences alive: True`.

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.App
git commit -m "feat: added drag-drop and rubber-band selection to fences"
```

---

### Task 4: Verification

**Files:** `docs/TEST-CHECKLIST.md` (append section K), `docs/research/m3b-drag-drop.md` (create)

- [ ] **Step 1: Append section K**

```markdown
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
```

- [ ] **Step 2: Run K1–K9.** The smoke covers K1, K2 (single item), K3 (Recycle Bin), K6 (Explorer) and K7. **[USER]**: K4, K5, K8, K9, multi-item K2, the K3 folder case, and K6 for apps.

- [ ] **Step 3: Write `docs/research/m3b-drag-drop.md`.** Include the results, the two prototype findings, and the harness limit (scripted drags from Explorer do not run).

- [ ] **Step 4: Commit**

```powershell
git add docs/TEST-CHECKLIST.md docs/research/m3b-drag-drop.md
git commit -m "docs: added M3b drag-drop checks and results"
```

---

### Task 5: Docs sync

- [ ] **Step 1: Append ADR-017 to `docs/DECISIONS.md`**

```markdown
## ADR-017 — Drag-drop through OLE with the shell's objects; drops between fences never touch files
**Date:** 2026-10-03 · **Status:** Accepted

**Context.** M3b is the last part of spec §6's interaction table: drag-drop within, between, into and out of fences,
plus rubber-band selection. Hard rule 1 still applies: a drag must never lose or hide files by accident.

**Decision.**
- **Dragging out** uses `SHDoDragDrop` with the shell's own `IDataObject` for the items (from the desktop
  `IShellFolder`). Apps and Explorer get real files and decide copy, move or link, and Windows draws the drag image.
  A drag starts after the system drag distance on a pressed item. Pressing one of several selected items keeps the
  selection, so they drag together.
- **Dropping on a fence** goes through each fence's own `IDropTarget` (`RegisterDragDrop`, replacing the one WPF
  registers on every window):
  - **Desktop items** — a fence's own drag, or files that already sit on the user's or the Public Desktop: membership
    only (`FenceMembership.MoveItems`). The drop reports `DROPEFFECT_NONE`, so the drag source can never delete
    anything after a "move".
  - **Hovering a container item** (a folder, or the Recycle Bin: `SFGAO_FOLDER | SFGAO_DROPTARGET`) forwards to that
    item's drop target, as on the desktop. Files that also call themselves drop targets (programs, text files) do
    not: dropping on them reorders instead.
  - **Anything else** (files from another folder) forwards to the Desktop folder's own drop target. Windows then
    copies or moves, with its progress, conflict dialog and Undo. Their Desktop paths are first registered as expected
    arrivals (`FenceMembership.ExpectArrivals`, the safe-save memory generalised to `RememberedPlacement`), so they
    land in that fence at the drop position.
  - `IDropTargetHelper` keeps Windows' drag image visible over fences.
- **Drop index:** the position in the target fence's list as shown during the drag, still including the dragged
  items, i.e. "insert before the item shown there". Reading order is rows, then left to right; the right half of an
  item counts as after it. This resolves the M1 deferred "MoveItem index semantics".
- **Rubber band:** pressing on empty space and dragging selects every item the band touches (Ctrl adds).

**Consequences.**
- A dropped file that Windows renames on a name conflict ("x - Copy") lands in the Inbox.
- Dropping onto a program to open the file with it is not supported inside fences.
- Drops in from Explorer could not be scripted: Explorer ignores synthetic drag gestures. That path is checked by
  hand (checklist K4).
```

- [ ] **Step 2: Update the other docs**
  - `ARCHITECTURE.md`: add `DesktopNamespace` and `ShellDragDrop`; the remaining "Shell/ShellActions (M3b)" row becomes those.
  - `FEATURES.md`: "Drag-drop in/out/between fences" is done (M3b), with K4/K5 by hand.
  - `ROADMAP.md`: tick M3b, and tick the M1 carry-over "MoveItem drop-index semantics".
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 3: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 4: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-017 and synced docs for M3b"
```
