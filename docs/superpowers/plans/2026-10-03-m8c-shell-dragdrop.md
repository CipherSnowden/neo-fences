# M8c — v1.1 Shell Actions and Drag-Drop Details Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The third carry-over batch (ROADMAP M8, batch c): item menu, recycle and open details, drag-drop details, live special icons, the M8a/M8b review minors, and NeoFences starting reliably after a Setup upgrade.

**Architecture:**
- `NeoFences.Core`:
  - `ReconcileReport.UsedMemories`, with memories inserted by index;
  - `DropZones.InsertIndex` (rows reach down to their tallest cell);
  - `DisplayFingerprint.UniqueDeviceIds(…, orderKeys)`;
  - `ConfigNormalizer` repairs `LabelMode` values.
- `NeoFences.Shell`:
  - `SpecialIconNotifications` (new: registry + Recycle Bin notices) and `DesktopItems.SpecialIconRefs`;
  - same-name Public Desktop items through their own folder;
  - item menu: HRESULT check and `WM_NULL`;
  - recycle skips missing items;
  - drag-drop: lazy CF_HDROP, virtual names, links, mixed drags, container cache;
  - monitor ids numbered in GDI order;
  - the watcher's `ReconcileNeeded` split out;
  - `FenceWindowChrome.MessageTime`.
- `NeoFences.App`:
  - `ShellWorker` (new STA queue for open, recycle and rename);
  - FenceHost wiring: menu, recycle, watcher coalescing and backoff, special icons, used memories;
  - FenceWindow: clicked item first, drop rows, icons after layout, own double-click with message time, rename width, dead code;
  - Settings help text and wrapping;
  - single-instance wait in `App`.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335, Serilog, xUnit, Velopack 1.2.161. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §5 (reconcile), §6 (shell integration, drag-drop), §7 (reliability). **Decisions:** ADR-016, ADR-017, ADR-023, ADR-024, ADR-025; **ADR-026** (new, Task 4). User choice (2026-10-03): menu → Rename with several selected renames the right-clicked item only.

**Pre-verified (2026-10-03):** every code block below was compiled together (0 warnings, 0 errors), and **281/281 tests pass**. On the real desktop, with the user's consent (the installed 1.1.0 was restored after each run):
- Menu → Rename with two items selected opened on the right-clicked item.
- An outside click closed the item menu.
- Del recycled on the shell worker while the UI kept responding.
- Turning the Control Panel desktop icon on and off in the registry fenced it and removed it again within about 2 s.
- A recycle triggered the Recycle Bin notice.
- The M8b regressions passed on the M8c build (Settings, first title double-click, Peek, quick-hide).
- A second instance waited about 5 s, logged and exited.
- Not checked live: the watcher coalescing (3000 files did not overflow the watcher), drag-drop from zips and Alt-drags, and the same-name Public Desktop item. These are user checks (T9, T10, T8).

## Global Constraints

- **Hard rule 1:** NeoFences acts only on the shell's own `rename` / `delete` verbs (other "Rename …" entries, like PowerRename, run as the shell's). Recycling never deletes permanently; missing items are skipped, not "deleted".
- **Hard rules 4, 5 and 7:** new Win32 goes through CsWin32 in Shell. A notice that cannot be registered is logged, and special icons then change only after a restart.
- **Hard rule 2:** the single-instance wait never starts a second FenceHost; a second instance exits before touching the desktop.
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- **NeoFences 1.1.0 is installed on the user's PC.** Smokes restore the installed copy (`-RestoreExe "$env:LOCALAPPDATA\NeoFences.App\current\NeoFences.exe"`). Ask before mouse runs, print TEST RUNNING / TEST COMPLETE, and never type into Windows Terminal. Menu clicks in scripts match exact entries (`Rena&me`), and "outside" clicks go to empty desktop (300, 950), never into an open menu.

## Review Focus

1. **The shell worker and window lifetimes.** Recycle or rename queued, then the fence is deleted or the app exits; a Windows dialog open at shutdown. Expected: no crash; owner handles of closed windows only lose dialog ownership; shutdown never hangs on the worker.
2. **Special-icon notices in bursts and failures.** Emptying a big Recycle Bin; Explorer restarting; the registry key missing or locked down. Expected: one refresh per burst; nothing loops (the refresh itself must not trigger notices); failures are logged once.
3. **Watcher recovery timing.** An error during game mode, during shutdown, and a watcher that fails at once on every re-arm. Expected: reconcile deferred in games; no timer after shutdown; backoff up to a minute.
4. **Mixed and virtual drops.** Desktop and non-desktop files dropped together; a zip drag that is cancelled; Ctrl (copy) on desktop items. Expected: membership moves only for desktop items; nothing extracted on hover; copies get the Inbox when names differ.
5. **The single-instance wait.** Start-with-Windows racing a manual start; `--exit` while the second instance waits. Expected: exactly one FenceHost; the waiting instance exits cleanly.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Membership/FenceMembership.cs`, `Layouts/DropZones.cs`, `Layouts/DisplayMonitor.cs`, `Config/ConfigNormalizer.cs` + `tests/…/Membership/M8cCoreTests.cs` | reconcile memories, drop rows, monitor order, label repair | 1 |
| `src/NeoFences.Shell/SpecialIconNotifications.cs` (new), `DesktopItems.cs`, `DesktopNamespace.cs`, `ShellItemMenu.cs`, `ShellFileOps.cs`, `ShellDragDrop.cs`, `Monitors.cs`, `DesktopWatcher.cs`, `FenceWindowChrome.cs`, `NativeMethods.txt` | shell details | 2 |
| `src/NeoFences.App/ShellWorker.cs` (new), `App.cs`, `FenceHost.cs`, `FenceWindow.xaml(.cs)`, `SettingsWindow.xaml(.cs)`, `SystemMessageWindow.cs` | wiring | 3 |
| `docs/TEST-CHECKLIST.md` (T), `docs/research/m8c-shell-dragdrop.md`, `DECISIONS.md` (ADR-026), `ROADMAP.md`, `ARCHITECTURE.md`, `SESSION-LOG.md`, hub | docs | 4 |

---

### Task 1: Core — reconcile memories, drop rows, monitor order, label repair

**Files:**
- Modify: `docs/ROADMAP.md` (claim M8c).
- Create: `tests/NeoFences.Core.Tests/Membership/M8cCoreTests.cs`.
- Replace: `src/NeoFences.Core/Membership/FenceMembership.cs`, `src/NeoFences.Core/Layouts/DropZones.cs`, `src/NeoFences.Core/Layouts/DisplayMonitor.cs` and `src/NeoFences.Core/Config/ConfigNormalizer.cs`.

**Interfaces:**
- Produces:
  - `ReconcileReport.UsedMemories` (`IReadOnlyList<RememberedPlacement>`, init, default empty);
  - `DropZones.InsertIndex(IReadOnlyList<(double Left, double Top, double Width, double Height)> cells, double pointX, double pointY)`;
  - `DisplayFingerprint.UniqueDeviceIds(IReadOnlyList<string> deviceIds, IReadOnlyList<string>? orderKeys = null)`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m8c-shell-dragdrop
```
In `docs/ROADMAP.md`, after the M8b review minors list, add `- [x] M8c implementation plan` and `- [~] M8c — claimed by session 2026-10-03 m8c`.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M8c"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Membership/M8cCoreTests.cs`:
```csharp
using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

/// <summary>M8c carry-overs in Core: reconcile memories, drop rows, stable monitor numbering, label validation.</summary>
public class M8cCoreTests
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<string> ItemsOf(NeoFencesConfig config, string fenceId) =>
        config.Fences.Single(fence => fence.Id == fenceId).Items;

    [Fact]
    public void Reconcile_ReportsTheMemoriesItUsed()
    {
        // Used memories are consumed by the caller, so a later, unrelated file of the same name does not follow them (M8a review).
        var games = Fence.Create("Games");
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };
        RememberedPlacement used = new(Desktop + "report.docx", games.Id, 0, Now + FenceMembership.SafeSaveWindow);
        RememberedPlacement unused = new(Desktop + "other.txt", games.Id, 0, Now + FenceMembership.SafeSaveWindow);

        var (_, report) = FenceMembership.Reconcile(config, [Desktop + "report.docx"], remembered: [used, unused], now: Now);

        Assert.Equal([used], report.UsedMemories);
    }

    [Fact]
    public void Reconcile_InsertsSeveralMemoriesForOneFence_ByTheirIndex()
    {
        // A drop of z.txt then a.txt at positions 1 and 2: the listing (by name) brings a.txt first (M8a review).
        var games = Fence.Create("Games") with { Items = [Desktop + "m.lnk", Desktop + "n.lnk"] };
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };
        var later = Now + FenceMembership.ArrivalWindow;
        RememberedPlacement[] remembered = [new(Desktop + "z.txt", games.Id, 1, later), new(Desktop + "a.txt", games.Id, 2, later)];

        var (reconciled, _) = FenceMembership.Reconcile(config,
            [Desktop + "a.txt", Desktop + "m.lnk", Desktop + "n.lnk", Desktop + "z.txt"], remembered: remembered, now: Now);

        Assert.Equal([Desktop + "m.lnk", Desktop + "z.txt", Desktop + "a.txt", Desktop + "n.lnk"], ItemsOf(reconciled, games.Id));
    }

    [Fact]
    public void DropInsertIndex_UsesTheWholeRow_NotTheShortItem()
    {
        // Row 1: a tall item (two-line label) and a short one; row 2 starts at 80 (M3b review).
        (double, double, double, double)[] cells = [(0, 0, 76, 80), (76, 0, 76, 50), (0, 80, 76, 50)];

        // Under the short item's label but inside the row, left of its centre: before it.
        Assert.Equal(1, DropZones.InsertIndex(cells, pointX: 90, pointY: 65));
        // Right of its centre: after it (before row 2).
        Assert.Equal(2, DropZones.InsertIndex(cells, pointX: 140, pointY: 65));
        // Past the last row: at the end.
        Assert.Equal(3, DropZones.InsertIndex(cells, pointX: 10, pointY: 200));
        Assert.Equal(0, DropZones.InsertIndex([], pointX: 10, pointY: 10));
    }

    [Fact]
    public void UniqueDeviceIds_NumbersDuplicatesInAStableOrder()
    {
        // Enumeration order can change between boots; numbering follows the GDI device name instead (M8a review).
        Assert.Equal(["DELL#2", "LG", "DELL"],
            DisplayFingerprint.UniqueDeviceIds(["DELL", "LG", "DELL"], orderKeys: [@"\\.\DISPLAY3", @"\\.\DISPLAY2", @"\\.\DISPLAY1"]));
        Assert.Equal(["DELL", "DELL#2"], DisplayFingerprint.UniqueDeviceIds(["DELL", "DELL"]));
    }

    [Fact]
    public void Normalizer_RepairsUnknownLabelModes()
    {
        // A hand-edited "labels": 2 would hide names with no way to show them (M8b review).
        var games = Fence.Create("Games") with { Labels = (LabelMode)2 };
        var config = new NeoFencesConfig
        {
            Fences = [Fence.Create("Inbox") with { IsInbox = true }, games],
            Settings = new Settings { DefaultLabels = (LabelMode)7 },
        };

        var normalized = ConfigNormalizer.Normalize(config);

        Assert.Equal(LabelMode.Always, normalized.Fences.Single(fence => fence.Id == games.Id).Labels);
        Assert.Equal(LabelMode.Always, normalized.Settings.DefaultLabels);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/NeoFences.Core.Tests`
Expected: build FAILS: `CS0117 InsertIndex`, `CS1061 UsedMemories` and `CS1739 orderKeys`.

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Membership/FenceMembership.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Membership;

/// <param name="Suspicious">True when part of the desktop could not be listed (an unreadable folder, or an empty listing): the affected memberships were kept, not pruned.</param>
public sealed record ReconcileReport(IReadOnlyList<string> Removed, IReadOnlyList<string> AddedToInbox, bool Suspicious = false)
{
    /// <summary>Remembered placements this reconcile applied: the caller drops them, so they are used once (M8a review).</summary>
    public IReadOnlyList<RememberedPlacement> UsedMemories { get; init; } = [];
}

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
    /// flagged. Everything else missing from a readable folder is dropped (M2b review: no ghosts), except:
    /// <list type="bullet">
    /// <item>a missing item whose file name now exists exactly once elsewhere, unfenced, takes that new path in place: a
    /// moved Desktop folder (OneDrive Known Folder Move) keeps every arrangement (M8a);</item>
    /// <item>an unknown item with an unexpired <paramref name="remembered"/> placement returns there instead of the Inbox:
    /// a safe-save whose watcher events were lost (M8a).</item>
    /// </list>
    /// </summary>
    public static (NeoFencesConfig Config, ReconcileReport Report) Reconcile(
        NeoFencesConfig config, IEnumerable<string> desktopItems, IReadOnlyCollection<string>? unavailableFolders = null,
        IReadOnlyList<RememberedPlacement>? remembered = null, DateTimeOffset? now = null)
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

        // Present but not yet fenced, by file name: candidates for an item whose folder moved.
        var fencedPresent = config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop)
            .SelectMany(fence => fence.Items).Where(presentSpelling.ContainsKey).ToHashSet(ItemRef.Comparer);
        var unfencedByName = presentOrder
            .Where(itemRef => !fencedPresent.Contains(itemRef) && !itemRef.StartsWith("::", StringComparison.Ordinal))
            .GroupBy(itemRef => Path.GetFileName(itemRef), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

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
                else if (!itemRef.StartsWith("::", StringComparison.Ordinal)
                         && unfencedByName.TryGetValue(Path.GetFileName(itemRef), out var candidates)
                         && candidates.Count == 1 && placed.Add(candidates[0]))
                {
                    kept.Add(candidates[0]); // same name, new folder: the same item moved (an ambiguous name is never guessed)
                }
                else
                {
                    removed.Add(itemRef);
                }
            }
            return fence with { Items = kept };
        }).ToList();

        var reconciled = config with { Fences = fences };
        var live = (remembered ?? []).Where(placement => now is null || placement.ExpiresAt >= now).ToList();
        var added = new List<string>();
        var returning = new List<RememberedPlacement>();
        foreach (var itemRef in presentOrder.Where(itemRef => !placed.Contains(itemRef)))
        {
            var memory = live.LastOrDefault(placement => ItemRef.Comparer.Equals(placement.ItemRef, itemRef));
            if (memory is not null && reconciled.Fences.Any(fence => fence.Id == memory.FenceId && fence.Source.Kind == FenceSourceKind.Desktop))
            {
                returning.Add(memory with { ItemRef = itemRef }); // the shell's current spelling
            }
            else
            {
                added.Add(itemRef);
            }
        }
        // By index, not listing order: several arrivals for one fence land where they were dropped (M8a review).
        foreach (var memory in returning.OrderBy(memory => memory.Index))
        {
            var home = reconciled.Fences.First(fence => fence.Id == memory.FenceId);
            var items = home.Items.ToList();
            items.Insert(Math.Clamp(memory.Index, 0, items.Count), memory.ItemRef);
            reconciled = reconciled.WithFence(home with { Items = items });
        }
        var usedMemories = live.Where(memory => returning.Any(used => ItemRef.Comparer.Equals(used.ItemRef, memory.ItemRef))).ToList();
        if (added.Count > 0)
        {
            reconciled = reconciled.WithFence(reconciled.Inbox with { Items = [.. reconciled.Inbox.Items, .. added] });
        }
        return (reconciled, new ReconcileReport(removed, added, suspicious) { UsedMemories = usedMemories });
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

    /// <summary>How long files dropped from Explorer are expected: Windows creates each when it starts copying it (M3b review I1).</summary>
    public static readonly TimeSpan ArrivalWindow = TimeSpan.FromMinutes(3);

    /// <summary>
    /// <see cref="Apply(NeoFencesConfig, DesktopChange)"/> that also survives "safe saves": editors replace a file by
    /// deleting (or renaming away) the original and creating (or renaming a temp file onto) the same name. A fenced item
    /// that disappears is remembered for <see cref="SafeSaveWindow"/>; if the same ref comes back in that time it returns
    /// to its fence and position instead of the Inbox. Keep the returned list and pass it to the next call.
    /// </summary>
    public static (NeoFencesConfig Config, IReadOnlyList<RememberedPlacement> Recent) Apply(
        NeoFencesConfig config, DesktopChange change, IReadOnlyList<RememberedPlacement> recent, DateTimeOffset now)
    {
        var remembered = recent.Where(placement => placement.ExpiresAt >= now).ToList();
        switch (change)
        {
            case DesktopChange.Deleted deleted when FindOwner(config, deleted.ItemRef) is { } deletedOwner:
                var index = deletedOwner.Items.ToList().FindIndex(existing => ItemRef.Comparer.Equals(existing, deleted.ItemRef));
                remembered.Add(new RememberedPlacement(deleted.ItemRef, deletedOwner.Id, index, now + SafeSaveWindow));
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
                remembered.Add(new RememberedPlacement(renamed.OldRef, owner.Id, oldIndex, now + SafeSaveWindow));
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
    /// when each appears within <see cref="ArrivalWindow"/> it goes to that fence at the drop position, in order.
    /// </summary>
    public static IReadOnlyList<RememberedPlacement> ExpectArrivals(
        IReadOnlyList<RememberedPlacement> recent, IReadOnlyList<string> itemRefs, string fenceId, int insertAt, DateTimeOffset now) =>
        [.. recent.Where(placement => placement.ExpiresAt >= now),
         .. itemRefs.Select((itemRef, offset) => new RememberedPlacement(itemRef, fenceId, insertAt + offset, now + ArrivalWindow))];

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
        var fence = Fence.Create(title, source) with { Labels = config.Settings.DefaultLabels };
        return (config with { Fences = [.. config.Fences, fence] }, fence);
    }

    /// <summary>A Portal: a fence that shows a folder live (M4). New Portals sort newest first (user choice 2026-10-03).</summary>
    /// <exception cref="ArgumentException">No folder given.</exception>
    public static (NeoFencesConfig Config, Fence Fence) CreatePortal(NeoFencesConfig config, string title, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("A Portal needs a folder.", nameof(folderPath));
        var portal = Fence.Create(title, FenceSource.Portal(folderPath)) with { Sort = FenceSort.Date, Labels = config.Settings.DefaultLabels };
        return (config with { Fences = [.. config.Fences, portal] }, portal);
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
`src/NeoFences.Core/Layouts/DropZones.cs`:
```csharp
namespace NeoFences.Core.Layouts;

/// <summary>
/// Where a drop over a folder-like item means "into it" (M3b review I2): only the middle half of the cell's width and its
/// upper three quarters (icon and first label line). The edges and the rest of the label reorder, so a drop meant to
/// land between two folders never moves a file into one of them.
/// </summary>
public static class DropZones
{
    public static bool IsInto(double cellLeft, double cellTop, double cellWidth, double cellHeight, double pointX, double pointY) =>
        pointX >= cellLeft + cellWidth / 4 && pointX <= cellLeft + cellWidth * 3 / 4
        && pointY >= cellTop && pointY <= cellTop + cellHeight * 3 / 4;

    /// <summary>
    /// The index to insert before when dropping at a point, cells in reading order (left to right, rows top to bottom).
    /// A row reaches down to its tallest cell, so the space under a short label still belongs to its row (M3b review).
    /// </summary>
    public static int InsertIndex(IReadOnlyList<(double Left, double Top, double Width, double Height)> cells, double pointX, double pointY)
    {
        var rowBottoms = new Dictionary<long, double>();
        foreach (var (_, top, _, height) in cells)
        {
            var row = (long)Math.Round(top);
            rowBottoms[row] = Math.Max(rowBottoms.GetValueOrDefault(row, double.MinValue), top + height);
        }
        for (var index = 0; index < cells.Count; index++)
        {
            var (left, top, width, _) = cells[index];
            var sameRow = pointY >= top && pointY < rowBottoms[(long)Math.Round(top)];
            if (pointY < top || (sameRow && pointX < left + width / 2)) return index;
        }
        return cells.Count;
    }
}
```
`src/NeoFences.Core/Layouts/DisplayMonitor.cs`:
```csharp
namespace NeoFences.Core.Layouts;

/// <summary>A monitor as NeoFences.Shell reports it. Work-area sizes are in DIPs.</summary>
/// <param name="DeviceId">Stable per physical monitor (Shell derives it from the device path).</param>
public sealed record DisplayMonitor(
    string DeviceId,
    int PixelWidth,
    int PixelHeight,
    int ScalePercent,
    double WorkWidth,
    double WorkHeight,
    bool IsPrimary);

/// <summary>Identifies a display configuration: which monitors, at which resolution and scale.</summary>
public static class DisplayFingerprint
{
    /// <example>"2mon:DELL-3840x2160@150%+LG-1920x1080@100%"</example>
    public static string Of(IReadOnlyCollection<DisplayMonitor> monitors) =>
        $"{monitors.Count}mon:" + string.Join("+", monitors
            .OrderBy(monitor => monitor.DeviceId, StringComparer.Ordinal)
            .Select(monitor => $"{monitor.DeviceId}-{monitor.PixelWidth}x{monitor.PixelHeight}@{monitor.ScalePercent}%"));

    /// <summary>
    /// Device ids must be unique (they key every layout), but cloned or mirrored outputs can report the same device
    /// path: the second and later copies get "#2", "#3"… (M8a). They are numbered in <paramref name="orderKeys"/> order
    /// (the GDI device name), not enumeration order, which can change between boots (M8a review); ids keep input order.
    /// </summary>
    public static IReadOnlyList<string> UniqueDeviceIds(IReadOnlyList<string> deviceIds, IReadOnlyList<string>? orderKeys = null)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var unique = new string[deviceIds.Count];
        foreach (var index in Enumerable.Range(0, deviceIds.Count).OrderBy(index => orderKeys?[index] ?? "", StringComparer.Ordinal).ThenBy(index => index))
        {
            var deviceId = deviceIds[index];
            seen[deviceId] = seen.TryGetValue(deviceId, out var count) ? count + 1 : 1;
            unique[index] = seen[deviceId] == 1 ? deviceId : $"{deviceId}#{seen[deviceId]}";
        }
        return unique;
    }
}
```
`src/NeoFences.Core/Config/ConfigNormalizer.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>
/// Repairs a loaded (possibly hand-edited) config without losing items: exactly one Inbox, unique fence
/// ids, no nulls, supported icon sizes and enum values, each desktop item in at most one fence, no items on
/// portal fences, the Inbox always a desktop fence, and layouts free of null or non-finite entries.
/// </summary>
public static class ConfigNormalizer
{
    public static IReadOnlyList<int> IconSizes { get; } = [32, 48, 64, 96];

    public static NeoFencesConfig Normalize(NeoFencesConfig config)
    {
        var defaults = new Settings();
        var settings = config.Settings ?? defaults;
        settings = settings with
        {
            PeekHotkey = string.IsNullOrWhiteSpace(settings.PeekHotkey) ? defaults.PeekHotkey : settings.PeekHotkey,
            RollupExpand = Enum.IsDefined(settings.RollupExpand) ? settings.RollupExpand : defaults.RollupExpand,
            DefaultLabels = Enum.IsDefined(settings.DefaultLabels) ? settings.DefaultLabels : defaults.DefaultLabels, // M8b review
        };

        var seenFenceIds = new HashSet<string>(StringComparer.Ordinal);
        var seenItems = new HashSet<string>(ItemRef.Comparer);
        var inboxFound = false;
        var fences = new List<Fence>();

        foreach (var loadedFence in config.Fences ?? [])
        {
            if (loadedFence is null) continue;
            var isInbox = loadedFence.IsInbox && !inboxFound;
            var source = loadedFence.Source is { } loadedSource && Enum.IsDefined(loadedSource.Kind) ? loadedSource : FenceSource.Desktop;
            var fence = loadedFence with
            {
                Id = string.IsNullOrWhiteSpace(loadedFence.Id) || !seenFenceIds.Add(loadedFence.Id) ? Fence.NewId() : loadedFence.Id,
                Title = loadedFence.Title ?? "",
                Source = isInbox ? FenceSource.Desktop : source,
                IsInbox = isInbox,
                Sort = Enum.IsDefined(loadedFence.Sort) ? loadedFence.Sort : FenceSort.Manual,
                Labels = Enum.IsDefined(loadedFence.Labels) ? loadedFence.Labels : LabelMode.Always, // a hand-edited number (M8b review)
                IconSize = IconSizes.Contains(loadedFence.IconSize) ? loadedFence.IconSize : 48,
            };
            seenFenceIds.Add(fence.Id);
            inboxFound |= fence.IsInbox;

            var items = fence.Source.Kind == FenceSourceKind.Desktop
                ? (loadedFence.Items ?? []).Where(itemRef => !string.IsNullOrWhiteSpace(itemRef) && seenItems.Add(itemRef)).ToList()
                : [];
            fences.Add(fence with { Items = items });
        }

        if (!inboxFound)
        {
            fences.Insert(0, Fence.Create("Inbox") with { IsInbox = true });
        }

        return config with
        {
            Settings = settings,
            Fences = fences,
            Layouts = NormalizeLayouts(config.Layouts),
        };
    }

    private static Dictionary<string, Layout> NormalizeLayouts(IReadOnlyDictionary<string, Layout>? loadedLayouts)
    {
        var layouts = new Dictionary<string, Layout>();
        foreach (var (fingerprint, layout) in loadedLayouts ?? new Dictionary<string, Layout>())
        {
            if (layout is null) continue;
            layouts[fingerprint] = new Layout
            {
                Monitors = (layout.Monitors ?? new Dictionary<string, MonitorArea>())
                    .Where(entry => entry.Value is { IsUsable: true })
                    .ToDictionary(entry => entry.Key, entry => entry.Value),
                Fences = (layout.Fences ?? new Dictionary<string, FenceRect>())
                    .Where(entry => entry.Value is { } rect && !string.IsNullOrWhiteSpace(rect.Monitor)
                                    && double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.W) && double.IsFinite(rect.H))
                    .ToDictionary(entry => entry.Key, entry => entry.Value),
            };
        }
        return layouts;
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 281`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "feat: reported the reconcile memories used and placed them by index, kept drop rows to their tallest item, numbered duplicate monitors stably and repaired unknown label modes"
```

---

### Task 2: Shell — notices, same-name items, menu, recycle, drag-drop, watcher

**Files:**
- Create: `src/NeoFences.Shell/SpecialIconNotifications.cs`.
- Replace: `src/NeoFences.Shell/NativeMethods.txt`, `DesktopItems.cs`, `DesktopNamespace.cs`, `ShellItemMenu.cs`, `ShellFileOps.cs`, `ShellDragDrop.cs`, `Monitors.cs`, `DesktopWatcher.cs` and `FenceWindowChrome.cs`.

**Interfaces:**
- Consumes: Task 1 (`UniqueDeviceIds` with `orderKeys`).
- Produces:
  - `SpecialIconNotifications(nint ownerHandle, Action settingsChanged, Action<string, Exception> log)`, `.Message`, `IsRecycleBinNotice(int)`;
  - `DesktopItems.SpecialIconRefs()`;
  - `ShellFileOps.TryRecycle` → `(bool Started, IReadOnlyList<string> Refused, IReadOnlyList<string> Missing)`;
  - `DesktopWatcher.ReconcileNeeded`;
  - `FenceWindowChrome.MessageTime()`.

- [ ] **Step 1: Files**

`src/NeoFences.Shell/NativeMethods.txt`:
```
// Win32 APIs used by NeoFences.Shell. CsWin32 generates bindings for each name.
AllowSetForegroundWindow
AppendMenu
BHID_SFObject
BITMAP
BITMAPINFO
CallNextHookEx
CLIPBOARD_FORMAT
CLSID_DragDropHelper
CMF_CANRENAME
CMF_EXTENDEDVERBS
CMF_NORMAL
CMIC_MASK_PTINVOKE
CMINVOKECOMMANDINFOEX
CreatePopupMenu
CreateRoundRectRgn
CUIAutomation
DeleteObject
DestroyIcon
DestroyMenu
DIB_USAGE
DISPLAY_DEVICEW
DragQueryFile
DROPEFFECT
DVASPECT
DWM_WINDOW_CORNER_PREFERENCE
DwmSetWindowAttribute
DWMWINDOWATTRIBUTE
EnumDisplayDevices
EnumDisplayMonitors
EVENT_SYSTEM_FOREGROUND
FileOpenDialog
FILEOPENDIALOGOPTIONS
FileOperation
FILEOPERATION_FLAGS
FindWindow
FOLDERFLAGS
FORMATETC
GCS_VERBW
GET_ANCESTOR_FLAGS
GET_WINDOW_CMD
GetAncestor
GetClassName
GetCurrentThreadId
GetCursorPos
GetDC
GetDIBits
GetDoubleClickTime
GetDpiForMonitor
GetDpiForSystem
GetForegroundWindow
GetMessage
GetModuleHandle
GetMonitorInfo
GetObject
GetSystemMetrics
GetSystemMetricsForDpi
GetWindow
GetWindowLongPtr
GetWindowRect
GetWindowText
GetWindowThreadProcessId
HDROP
HOT_KEY_MODIFIERS
HWND_BOTTOM
HWND_NOTOPMOST
HWND_TOPMOST
IContextMenu
IContextMenu2
IContextMenu3
IDataObject
IDropTarget
IDropTargetHelper
IFileOpenDialog
IFileOperation
IFolderView2
INPUT
IServiceProvider
IShellBrowser
IShellFolder
IShellItem
IShellItemImageFactory
IShellView
IShellWindows
IUIAutomation
IUIAutomationElement
LoadImage
MODIFIERKEYS_FLAGS
MONITOR_DPI_TYPE
MONITORINFOEXW
MONITORINFOF_PRIMARY
MSLLHOOKSTRUCT
NIN_SELECT
NOTIFY_FOR_THIS_SESSION
NOTIFY_ICON_MESSAGE
NOTIFYICON_VERSION_4
NOTIFYICONDATAW
POINTL
PostMessage
PostThreadMessage
RegisterDragDrop
RegisterHotKey
RegisterWindowMessage
ReleaseDC
ReleaseStgMedium
RevokeDragDrop
SendInput
SET_WINDOW_POS_FLAGS
SetForegroundWindow
SetWindowLongPtr
SetWindowPos
SetWindowRgn
SetWindowsHookEx
SetWinEventHook
SFGAO_FLAGS
SHCreateItemFromParsingName
SHDoDragDrop
Shell_NotifyIcon
ShellWindows
SHGetDesktopFolder
SHOW_WINDOW_CMD
SHQueryUserNotificationState
SID_STopLevelBrowser
SIGDN
SIIGBF
STGMEDIUM
SYSTEM_METRICS_INDEX
TRACK_POPUP_MENU_FLAGS
TrackPopupMenuEx
TYMED
UIA_CONTROLTYPE_ID
UnhookWinEvent
UnregisterHotKey
WINDOW_EX_STYLE
WINDOW_LONG_PTR_INDEX
WINDOW_STYLE
WindowFromPoint
WINDOWPOS
WINDOWS_HOOK_ID
WINEVENT_OUTOFCONTEXT
WM_APP
WM_CONTEXTMENU
WM_LBUTTONDOWN
WM_MOUSEMOVE
WM_NULL
WM_QUIT
WM_RBUTTONDOWN
WM_RBUTTONUP
WM_WTSSESSION_CHANGE
WTS_SESSION_UNLOCK
WTSRegisterSessionNotification
WTSUnRegisterSessionNotification
SHChangeNotifyRegister
SHChangeNotifyDeregister
SHChangeNotifyEntry
SHCNE_ID
SHCNRF_SOURCE
SHGetKnownFolderIDList
FOLDERID_RecycleBinFolder
CoTaskMemFree
RegNotifyChangeKeyValue
REG_NOTIFY_FILTER
RegisterClipboardFormat
FILEGROUPDESCRIPTORW
GlobalLock
GlobalUnlock
GetMessageTime
```
`src/NeoFences.Shell/SpecialIconNotifications.cs`:
```csharp
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace NeoFences.Shell;

/// <summary>
/// Tells NeoFences when a special desktop icon changes (M8c, M2b carry-over): the Recycle Bin turns full or empty (a
/// shell change notice to a window), or the user shows or hides an icon in "Desktop icon settings" (a registry change).
/// Either may arrive in bursts; the caller debounces. A part that cannot be registered is logged and left out.
/// </summary>
public sealed class SpecialIconNotifications : IDisposable
{
    /// <summary>Posted to the owner window for a Recycle Bin change (handle it like any window message).</summary>
    public static readonly uint Message = PInvoke.WM_APP + 3;

    private const string HideDesktopIconsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    private readonly uint _recycleBinRegistration; // the Recycle Bin's own item and contents
    private readonly uint _imageRegistration;      // icon image updates (its full/empty icon is one); rare, unlike item changes
    private readonly RegistryKey? _key;
    private readonly AutoResetEvent _keyChanged = new(false);
    private readonly RegisteredWaitHandle? _keyWait;
    private readonly Action _settingsChanged;
    private readonly nint _rootPidl = ZeroedPidl(); // the empty ID list: the namespace root (kept alive while registered)

    private static nint ZeroedPidl()
    {
        var pidl = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(2);
        System.Runtime.InteropServices.Marshal.WriteInt16(pidl, 0);
        return pidl;
    }
    private volatile bool _disposed;

    /// <param name="settingsChanged">"Desktop icon settings" changed; called on a thread-pool thread.</param>
    public unsafe SpecialIconNotifications(nint ownerHandle, Action settingsChanged, Action<string, Exception> log)
    {
        _settingsChanged = settingsChanged;
        try
        {
            // The Recycle Bin's own changes, and icon image updates anywhere (its full/empty icon is one).
            PInvoke.SHGetKnownFolderIDList(PInvoke.FOLDERID_RecycleBinFolder, 0, null, out var recycleBin).ThrowOnFailure();
            try
            {
                // Items moving in or out of the bin arrive as file-system events of its folders: interrupt level too, recursive.
                _recycleBinRegistration = Register(ownerHandle, recycleBin, recursive: true, SHCNE_ID.SHCNE_ALLEVENTS); // only the bin's own
                _imageRegistration = Register(ownerHandle, (ITEMIDLIST*)_rootPidl, recursive: true, SHCNE_ID.SHCNE_UPDATEIMAGE);
                if (_recycleBinRegistration == 0 || _imageRegistration == 0) log("Recycle Bin notices", new InvalidOperationException("SHChangeNotifyRegister failed"));
            }
            finally
            {
                PInvoke.CoTaskMemFree(recycleBin);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            log("Recycle Bin notices", failure);
        }
        try
        {
            // Windows creates the key on the first change; creating it empty changes nothing shown.
            _key = Registry.CurrentUser.CreateSubKey(HideDesktopIconsKey, writable: false);
            ArmKeyNotification();
            _keyWait = ThreadPool.RegisterWaitForSingleObject(_keyChanged, (_, _) => OnKeyChanged(), null, Timeout.Infinite, executeOnlyOnce: false);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            log("desktop icon settings notices", failure);
        }
    }

    /// <summary>Classic delivery (no SHCNRF_NewDelivery): the message needs no SHChangeNotification_Lock to be freed.</summary>
    private static unsafe uint Register(nint ownerHandle, ITEMIDLIST* folder, bool recursive, SHCNE_ID events)
    {
        var entry = new SHChangeNotifyEntry { pidl = folder, fRecursive = recursive };
        return PInvoke.SHChangeNotifyRegister((HWND)ownerHandle, SHCNRF_SOURCE.SHCNRF_ShellLevel | SHCNRF_SOURCE.SHCNRF_InterruptLevel,
            (int)events, Message, 1, &entry);
    }

    /// <summary>True for this class's window message; the message's content is not needed (the caller re-reads).</summary>
    public static bool IsRecycleBinNotice(int message) => (uint)message == Message;

    private void OnKeyChanged()
    {
        if (_disposed) return;
        ArmKeyNotification(); // one notice per registration: register again first, so no change is missed
        _settingsChanged();
    }

    private void ArmKeyNotification()
    {
        // Thread agnostic: the registration survives the thread-pool thread that made it (Windows 8+).
        var result = PInvoke.RegNotifyChangeKeyValue(_key!.Handle, false,
            REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC,
            _keyChanged.SafeWaitHandle, true);
        if (result != WIN32_ERROR.ERROR_SUCCESS) throw new System.ComponentModel.Win32Exception((int)result);
    }

    public void Dispose()
    {
        _disposed = true;
        if (_recycleBinRegistration != 0) PInvoke.SHChangeNotifyDeregister(_recycleBinRegistration);
        if (_imageRegistration != 0) PInvoke.SHChangeNotifyDeregister(_imageRegistration);
        _keyWait?.Unregister(null);
        _key?.Dispose();
        _keyChanged.Dispose();
        System.Runtime.InteropServices.Marshal.FreeCoTaskMem(_rootPidl);
    }
}
```
`src/NeoFences.Shell/DesktopItems.cs`:
```csharp
using Microsoft.Win32;
using NeoFences.Core.Model;

namespace NeoFences.Shell;

/// <param name="UnavailableFolders">Desktop folders that could not be listed (missing or unreadable): their items are unknown, not gone.</param>
public sealed record DesktopListing(IReadOnlyList<string> ItemRefs, IReadOnlyList<string> UnavailableFolders);

/// <summary>
/// What Explorer shows on the desktop, as item refs (ADR-002): the visible files and folders of the user's Desktop
/// (OneDrive-redirected or not) and the Public Desktop, plus the special icons the user enabled in "Desktop icon
/// settings" (Recycle Bin, This PC, ...) as "::{CLSID}".
/// </summary>
public static class DesktopItems
{
    private const string HideDesktopIconsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    /// <summary>Special desktop icons and whether Windows shows each one when the user never changed the setting.</summary>
    private static readonly (string Clsid, bool ShownByDefault)[] SpecialIcons =
    [
        ("{645FF040-5081-101B-9F08-00AA002F954E}", true),  // Recycle Bin
        ("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false), // This PC
        ("{59031a47-3f72-44a7-89c5-5595fe6b30ee}", false), // User's files
        ("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", false), // Network
        ("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", false), // Control Panel
    ];

    // DoNotVerify: a missing (offline, unmounted) folder still has a path, so its items are recognised as unknown, not gone.
    /// <summary>The Recycle Bin's item ref.</summary>
    public const string RecycleBinRef = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    public static string UserDesktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);

    public static string PublicDesktop => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>The special icons the user shows in "Desktop icon settings", as "::{CLSID}" (one registry read).</summary>
    public static IReadOnlyList<string> SpecialIconRefs()
    {
        var itemRefs = new List<string>();
        using var hideKey = Registry.CurrentUser.OpenSubKey(HideDesktopIconsKey);
        foreach (var (clsid, shownByDefault) in SpecialIcons)
        {
            var hidden = hideKey?.GetValue(clsid) is int setting ? setting != 0 : !shownByDefault;
            if (!hidden) itemRefs.Add("::" + clsid);
        }
        return itemRefs;
    }

    /// <summary>Special icons first (as Explorer arranges them), then files and folders by name.</summary>
    public static DesktopListing Enumerate()
    {
        var itemRefs = SpecialIconRefs().ToList();

        var files = new List<string>();
        var unavailable = new List<string>();
        foreach (var directory in new[] { UserDesktop, PublicDesktop }.Where(path => path.Length > 0).Distinct(ItemRef.Comparer))
        {
            try
            {
                // ToList first: a folder that fails halfway counts as unavailable, with none of its entries listed.
                files.AddRange(new DirectoryInfo(directory).EnumerateFileSystemInfos()
                    .Where(IsVisibleOnDesktop)
                    .Select(entry => entry.FullName)
                    .ToList());
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                unavailable.Add(directory); // missing (DirectoryNotFoundException is an IOException) or unreadable
            }
        }
        itemRefs.AddRange(files.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase));
        return new DesktopListing(itemRefs, unavailable);
    }

    /// <summary>Hidden and system entries (desktop.ini, Office ~$ lock files) are not shown by Explorer either.</summary>
    public static bool IsVisibleOnDesktop(FileSystemInfo entry) =>
        (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
}
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
/// Items as children of a shell folder, for item menus, drag data and per-item drop targets. Desktop items go through
/// the desktop folder, which merges the user's and the Public Desktop (a file name or <c>::{CLSID}</c> is a direct
/// child); a Portal's items go through their own folder (M4). One call's items must share that parent.
/// </summary>
internal static class DesktopNamespace
{
    /// <summary>A UI object (IContextMenu, IDataObject, IDropTarget, …) for these items; the caller releases it.</summary>
    /// <exception cref="ArgumentException">The items are not all on the desktop or all in one folder.</exception>
    public static unsafe object GetUIObject(HWND owner, IReadOnlyList<string> itemRefs, Guid interfaceId)
    {
        var parent = ParentFolder(itemRefs);
        var childIds = ChildIds(owner, parent, itemRefs);
        try
        {
            fixed (nint* ids = childIds.ToArray())
            {
                parent.GetUIObjectOf(owner, (uint)childIds.Count, (ITEMIDLIST**)ids, &interfaceId, null, out var uiObject);
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
        return DropTargetOf(owner, desktop);
    }

    /// <summary>A folder's own drop target (a Portal's folder): Windows moves/copies into it, with its dialogs and Undo.</summary>
    public static object FolderDropTarget(HWND owner, string folderPath) => DropTargetOf(owner, FolderObject(folderPath));

    /// <summary>
    /// True for containers that take drops themselves: folders, zips and Recycle Bin. Files (even ones Windows lists as
    /// drop targets, like programs or text files) are not: dropping on them reorders the fence instead (M3b).
    /// </summary>
    public static unsafe bool IsDropContainer(HWND owner, string itemRef)
    {
        try
        {
            var parent = ParentFolder([itemRef]);
            var childIds = ChildIds(owner, parent, [itemRef]);
            try
            {
                var attributes = (uint)(SFGAO_FLAGS.SFGAO_DROPTARGET | SFGAO_FLAGS.SFGAO_FOLDER);
                fixed (nint* ids = childIds.ToArray())
                {
                    parent.GetAttributesOf(1, (ITEMIDLIST**)ids, ref attributes);
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

    /// <summary>A special icon, or a file or folder directly on the user's or the Public Desktop.</summary>
    public static bool IsDesktopItem(string itemRef) =>
        itemRef.StartsWith("::", StringComparison.Ordinal) || IsDesktopFolder(Path.GetDirectoryName(itemRef));

    /// <summary>A Public Desktop file whose name also exists on the user's Desktop (both show on the desktop).</summary>
    private static bool IsShadowedPublicItem(string itemRef) =>
        !itemRef.StartsWith("::", StringComparison.Ordinal)
        && string.Equals(Path.GetDirectoryName(itemRef)?.TrimEnd('\\'), DesktopItems.PublicDesktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
        && Path.Exists(Path.Combine(DesktopItems.UserDesktop, Path.GetFileName(itemRef)));

    public static bool IsDesktopFolder(string? folderPath)
    {
        if (folderPath is null) return false;
        var trimmed = folderPath.TrimEnd('\\', '/');
        return string.Equals(trimmed, DesktopItems.UserDesktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, DesktopItems.PublicDesktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    private static unsafe object DropTargetOf(HWND owner, IShellFolder folder)
    {
        var dropTargetId = typeof(Windows.Win32.System.Ole.IDropTarget).GUID;
        folder.CreateViewObject(owner, &dropTargetId, out var dropTarget);
        return dropTarget;
    }

    private static IShellFolder ParentFolder(IReadOnlyList<string> itemRefs)
    {
        if (itemRefs.Count == 0) throw new ArgumentException("No items.", nameof(itemRefs));
        // The desktop folder parses a name to the user's copy: a Public Desktop item with the same name is reached through
        // its own folder, or the menu and drag would act on the user's file (M3a/M3b review).
        if (itemRefs.All(IsDesktopItem) && !itemRefs.Any(IsShadowedPublicItem))
        {
            PInvoke.SHGetDesktopFolder(out var desktop).ThrowOnFailure();
            return desktop;
        }
        var parentPath = Path.GetDirectoryName(itemRefs[0]) ?? throw new ArgumentException("An item has no folder.", nameof(itemRefs));
        if (itemRefs.Any(itemRef => !string.Equals(Path.GetDirectoryName(itemRef), parentPath, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Items from different folders.", nameof(itemRefs));
        return FolderObject(parentPath);
    }

    private static unsafe IShellFolder FolderObject(string folderPath)
    {
        PInvoke.SHCreateItemFromParsingName(folderPath, null, out IShellItem folderItem).ThrowOnFailure();
        var handler = PInvoke.BHID_SFObject;
        var folderId = typeof(IShellFolder).GUID;
        folderItem.BindToHandler(null, &handler, &folderId, out var folder);
        return (IShellFolder)folder;
    }

    private static unsafe List<nint> ChildIds(HWND owner, IShellFolder parent, IReadOnlyList<string> itemRefs)
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
                    parent.ParseDisplayName(owner, null, name, null, &childId, ref attributes);
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
            var queried = contextMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, flags);
            if (queried.Failed) // a broken extension can fail the whole menu without throwing (M3a review)
            {
                logFailure(new COMException("QueryContextMenu failed", queried.Value));
                return ItemMenuChoice.None;
            }
            _openMenu = contextMenu as IContextMenu2;

            PInvoke.SetForegroundWindow(owner); // the menu closes on a click elsewhere only if its owner is foreground
            var command = (uint)PInvoke.TrackPopupMenuEx(menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON), screenX, screenY, owner, null).Value;
            // The documented menu dance: when the fence could not become foreground, this lets the next click close it (M3a review).
            PInvoke.PostMessage(owner, PInvoke.WM_NULL, 0, 0);
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
`src/NeoFences.Shell/ShellFileOps.cs`:
```csharp
using System.Runtime.InteropServices;
using NeoFences.Core.Model;
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
    /// <summary>
    /// Recycles the items. Items on drives without a Recycle Bin (USB sticks, network shares, optical/RAM drives) are
    /// left alone and returned: there Windows could only delete them for good, which NeoFences never does (hard rule 1,
    /// user decision 2026-10-03). Special items are skipped.
    /// </summary>
    /// <returns>Started: false when nothing could be started (true even if the user cancelled Windows' dialog). Missing:
    /// items that no longer exist, skipped so the others are still recycled (M3a review).</returns>
    public static (bool Started, IReadOnlyList<string> Refused, IReadOnlyList<string> Missing) TryRecycle(nint ownerHandle, IReadOnlyList<string> itemRefs)
    {
        var files = itemRefs.Where(itemRef => !itemRef.StartsWith("::", StringComparison.Ordinal)).ToList();
        var refused = files.Where(file => !HasRecycleBin(file)).ToList();
        var missing = new List<string>();
        var items = new List<IShellItem>();
        foreach (var file in files.Except(refused))
        {
            try { items.Add(Create(file)); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { missing.Add(file); }
        }
        try
        {
            var started = items.Count == 0 || Run(ownerHandle, operation =>
            {
                foreach (var item in items) operation.DeleteItem(item, null);
            });
            return (started, refused, missing);
        }
        finally
        {
            foreach (var item in items) Marshal.ReleaseComObject(item);
        }
    }

    /// <summary>
    /// True when the item's drive keeps a Recycle Bin: local fixed drives (an external USB hard disk counts). Removable
    /// sticks and cards, network shares (mapped or UNC), CD/DVD and RAM drives do not.
    /// </summary>
    public static bool HasRecycleBin(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\", StringComparison.Ordinal)) return false; // UNC share
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false; // unknown: never risk a permanent delete
        }
    }

    /// <param name="newName">As typed. If Explorer hides this item's extension (shortcuts, or the user's setting), Windows keeps it.</param>
    /// <returns>False also for a name that is not a plain name: "sub\x" or "..\x" would move the file off the Desktop (M3a review I2).</returns>
    public static bool TryRename(nint ownerHandle, string itemRef, string newName) =>
        !itemRef.StartsWith("::", StringComparison.Ordinal)
        && FileNames.IsValidNewName(newName)
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
        catch (Exception failure) when (failure is not OutOfMemoryException)
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
/// <param name="ItemRef">The item whose "drop into" zone is under the point (Core DropZones), if any.</param>
/// <param name="InsertAt">Index in the fence's shown list to insert before (see FenceMembership.MoveItems).</param>
public readonly record struct FenceDropPoint(string? ItemRef, int InsertAt);

/// <summary>What a fence does with drops; NeoFences.App supplies these (all called on the UI thread).</summary>
/// <param name="HitTest">Item and insert position under a screen point.</param>
/// <param name="MoveItems">Desktop items dropped on the fence (from a fence or from Explorer's Desktop): membership only, no file operation.</param>
/// <param name="ExpectArrivals">Desktop refs Windows is about to copy or move onto the Desktop for this drop, and where they go.</param>
/// <param name="Recycle">Files dropped on the Recycle Bin item: always recycled by NeoFences, never deleted (hard rule 1).</param>
/// <param name="ShowFeedback">Where the drop would land (null hides it): an insertion caret, or a highlighted container when into is true.</param>
/// <param name="LogFailure">A drop that could not be handed to Windows.</param>
public sealed record FenceDropHandlers(
    Func<int, int, FenceDropPoint> HitTest,
    Action<IReadOnlyList<string>, int> MoveItems,
    Action<IReadOnlyList<string>, int> ExpectArrivals,
    Action<IReadOnlyList<string>> Recycle,
    Action<FenceDropPoint?, bool> ShowFeedback,
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
    /// <param name="portalFolder">For a Portal fence: the folder it shows right now (drops become real moves/copies into it, M4).</param>
    public static IDisposable RegisterFence(nint fenceHandle, FenceDropHandlers handlers, Func<string?>? portalFolder = null)
    {
        var target = new FenceDropTarget((HWND)fenceHandle, handlers, portalFolder);
        // WPF registers its own drop target on every window; NeoFences does not use WPF drag-drop, so replace it.
        PInvoke.RevokeDragDrop((HWND)fenceHandle);
        PInvoke.RegisterDragDrop((HWND)fenceHandle, target).ThrowOnFailure();
        return target;
    }

    private static readonly ushort FileGroupDescriptorFormat = (ushort)PInvoke.RegisterClipboardFormat("FileGroupDescriptorW");

    /// <summary>True when the source describes virtual files (zip contents, phones, mail attachments).</summary>
    internal static unsafe bool OffersVirtualFiles(IDataObject dataObject)
    {
        var format = DescriptorFormat();
        try { return dataObject.QueryGetData(&format).Value == 0; } // S_OK; S_FALSE and errors mean no
        catch (Exception failure) when (failure is not OutOfMemoryException) { return false; }
    }

    /// <summary>The names of virtual files being dropped (no extraction); empty when there are none.</summary>
    internal static unsafe List<string> VirtualFileNames(IDataObject dataObject)
    {
        var names = new List<string>();
        var format = DescriptorFormat();
        STGMEDIUM medium = default;
        try
        {
            dataObject.GetData(&format, out medium);
            var group = (FILEGROUPDESCRIPTORW*)PInvoke.GlobalLock(medium.u.hGlobal);
            if (group is null) return names;
            try
            {
                var descriptors = &group->fgd.e0;
                for (var index = 0; index < group->cItems; index++)
                {
                    var name = descriptors[index].cFileName.ToString();
                    if (!name.Contains('\\')) names.Add(name); // files in subfolders arrive inside their folder
                }
            }
            finally
            {
                PInvoke.GlobalUnlock(medium.u.hGlobal);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // No descriptors: nothing to place; Windows still handles the drop.
        }
        finally
        {
            if (medium.u.hGlobal.Value != null) PInvoke.ReleaseStgMedium(ref medium);
        }
        return names;
    }

    private static FORMATETC DescriptorFormat() => new()
    {
        cfFormat = FileGroupDescriptorFormat,
        dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
        lindex = -1,
        tymed = (uint)TYMED.TYMED_HGLOBAL,
    };

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
    private sealed unsafe class FenceDropTarget(HWND fence, FenceDropHandlers handlers, Func<string?>? portalFolder) : IDropTarget, IDisposable
    {
        private IDataObject? _dataObject;
        private bool _desktopItemsOnly;      // desktop fence: items already on the Desktop (membership only)
        private string? _portalFolder;       // Portal fence: the folder it shows (drops are real file operations)
        private bool _sameFolderOnly;        // Portal fence: items already in that folder (nothing to do)
        private string? _hoveredItem;        // item whose own drop target is active
        private readonly Dictionary<string, bool> _containers = new(StringComparer.OrdinalIgnoreCase); // per drag: DragOver runs per mouse move
        private bool _overRecycleBin;         // NeoFences recycles itself here (never the Recycle Bin's own drop)
        private IDropTarget? _shellTarget;   // forwarded-to target while it is entered
        private IDropTargetHelper? _imageHelper;

        public void DragEnter(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            LeaveShellTarget(); // a previous drag that failed half-way must not leak into this one
            Reset();
            _dataObject = pDataObj;
            _portalFolder = portalFolder?.Invoke();
            // A virtual source (zip contents, a phone) would extract every file to answer CF_HDROP: not on enter (M3b review).
            IReadOnlyList<string> dragged = CurrentDrag ?? (OffersVirtualFiles(pDataObj) ? [] : DroppedFiles(pDataObj));
            _desktopItemsOnly = _portalFolder is null && dragged.Count > 0 && dragged.All(DesktopNamespace.IsDesktopItem);
            _sameFolderOnly = _portalFolder is not null && dragged.Count > 0
                && dragged.All(itemRef => string.Equals(Path.GetDirectoryName(itemRef), _portalFolder, StringComparison.OrdinalIgnoreCase));
            _imageHelper = TryCreateImageHelper();
            var allowed = *pdwEffect;
            Update(grfKeyState, pt, pdwEffect, allowed);
            var effect = *pdwEffect;
            WithImageHelper(helper =>
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                helper.DragEnter(fence, pDataObj, &point, effect);
            });
        }

        public void DragOver(MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            Update(grfKeyState, pt, pdwEffect, *pdwEffect);
            var effect = *pdwEffect;
            WithImageHelper(helper =>
            {
                var point = new System.Drawing.Point(pt.x, pt.y);
                helper.DragOver(&point, effect);
            });
        }

        public void DragLeave()
        {
            LeaveShellTarget();
            WithImageHelper(helper => helper.DragLeave());
            handlers.ShowFeedback(null, false);
            Reset();
        }

        public void Drop(IDataObject pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect)
        {
            var allowed = *pdwEffect;
            try
            {
                Update(grfKeyState, pt, pdwEffect, allowed);
                var shownEffect = *pdwEffect;
                WithImageHelper(helper =>
                {
                    var point = new System.Drawing.Point(pt.x, pt.y);
                    helper.Drop(pDataObj, &point, shownEffect);
                });
                var drop = handlers.HitTest(pt.x, pt.y);
                if (_overRecycleBin)
                {
                    handlers.Recycle(CurrentDrag ?? DroppedFiles(pDataObj)); // always the Recycle Bin, never a delete
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
                    return;
                }
                if (_shellTarget is null && _sameFolderOnly)
                {
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE; // a Portal item dropped back into its own folder
                    return;
                }
                if (_shellTarget is null)
                {
                    // Membership only. Report "none" so the source never deletes anything after a "move".
                    handlers.MoveItems(CurrentDrag ?? DroppedFiles(pDataObj), drop.InsertAt);
                    *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
                    return;
                }
                if (_hoveredItem is null && _portalFolder is null)
                {
                    // Desktop items in a mixed drag join this fence; the rest arrive through Windows (M3b review).
                    var files = CurrentDrag ?? DroppedFiles(pDataObj);
                    var onDesktop = files.Where(DesktopNamespace.IsDesktopItem).ToList();
                    if (onDesktop.Count > 0) handlers.MoveItems(onDesktop, drop.InsertAt);
                    var arriving = files.Count > 0 ? files.Except(onDesktop).Select(Path.GetFileName).OfType<string>().ToList() : VirtualFileNames(pDataObj);
                    handlers.ExpectArrivals(DesktopRefsFor(arriving), drop.InsertAt + onDesktop.Count);
                }
                var target = _shellTarget;
                _shellTarget = null; // Drop replaces DragLeave for it
                try
                {
                    *pdwEffect = allowed; // the source's choices, so a right-button drop menu offers them all
                    target.Drop(pDataObj, grfKeyState, pt, pdwEffect);
                }
                finally
                {
                    Marshal.ReleaseComObject(target);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                handlers.LogFailure(failure);
                *pdwEffect = DROPEFFECT.DROPEFFECT_NONE;
            }
            finally
            {
                LeaveShellTarget();
                handlers.ShowFeedback(null, false);
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
                var drop = handlers.HitTest(pt.x, pt.y);
                var hovered = drop.ItemRef;
                var dropsOnItem = hovered is not null && !(CurrentDrag?.Contains(hovered, StringComparer.OrdinalIgnoreCase) ?? false)
                                  && IsDropContainer(hovered);
                var wantedItem = dropsOnItem ? hovered : null;
                var wantsShell = dropsOnItem || !(_desktopItemsOnly || _sameFolderOnly);

                handlers.ShowFeedback(drop, wantedItem is not null);
                // The Recycle Bin's own drop target deletes permanently with Shift held, or refuses: NeoFences recycles
                // itself and only shows "move" here, whatever keys are held (M3b review C1).
                _overRecycleBin = string.Equals(wantedItem, DesktopItems.RecycleBinRef, StringComparison.OrdinalIgnoreCase);
                if (_overRecycleBin)
                {
                    LeaveShellTarget();
                    *effect = allowed & DROPEFFECT.DROPEFFECT_MOVE;
                    return;
                }
                if (_shellTarget is not null && (!wantsShell || wantedItem != _hoveredItem)) LeaveShellTarget();
                if (!wantsShell)
                {
                    // Desktop fence: a membership move, nothing on disk changes. Portal: already in this folder.
                    *effect = _sameFolderOnly ? DROPEFFECT.DROPEFFECT_NONE : allowed & DROPEFFECT.DROPEFFECT_MOVE;
                    return;
                }
                if (_shellTarget is null)
                {
                    _shellTarget = (IDropTarget)(wantedItem is null
                        ? (_portalFolder is null ? DesktopNamespace.DesktopDropTarget(fence) : DesktopNamespace.FolderDropTarget(fence, _portalFolder))
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

        /// <summary>The drag image is cosmetic: a failing helper must not break the drop or leave state behind.</summary>
        private void WithImageHelper(Action<IDropTargetHelper> call)
        {
            if (_imageHelper is null) return;
            try { call(_imageHelper); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { handlers.LogFailure(failure); }
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

        private bool IsDropContainer(string itemRef)
        {
            if (!_containers.TryGetValue(itemRef, out var container)) _containers[itemRef] = container = DesktopNamespace.IsDropContainer(fence, itemRef);
            return container;
        }

        private void Reset()
        {
            _containers.Clear();
            _dataObject = null;
            _desktopItemsOnly = false;
            _portalFolder = null;
            _sameFolderOnly = false;
            _overRecycleBin = false;
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

        /// <summary>
        /// Where Windows will put dropped files: the user's Desktop, same names, or "name - Shortcut.lnk" when the drop makes
        /// shortcuts (Alt, or a link-only source). Renamed copies ("name (2)") still go to the Inbox.
        /// </summary>
        // ponytail: the English " - Shortcut" suffix; other display languages name links differently (their links go to the Inbox).
        private static List<string> DesktopRefsFor(IEnumerable<string> fileNames) =>
            fileNames.SelectMany(name => new[] { name, name + " - Shortcut.lnk" })
                .Select(name => Path.Combine(DesktopItems.UserDesktop, name)).ToList();
    }
}
```
`src/NeoFences.Shell/Monitors.cs`:
```csharp
using NeoFences.Core.Layouts;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace NeoFences.Shell;

/// <summary>Enumerates monitors in physical pixels (requires per-monitor DPI awareness, see app.manifest).</summary>
public static class Monitors
{
    private const uint GetDeviceInterfaceName = 0x1; // EDD_GET_DEVICE_INTERFACE_NAME

    public static unsafe IReadOnlyList<MonitorPlacement> Enumerate()
    {
        var handles = new List<HMONITOR>();
        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (monitor, _, _, _) =>
        {
            handles.Add(monitor);
            return true;
        }, 0);

        var placements = new List<MonitorPlacement>();
        var gdiNames = new List<string>();
        foreach (var handle in handles)
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (!PInvoke.GetMonitorInfo(handle, (MONITORINFO*)&info)) continue;

            PInvoke.GetDpiForMonitor(handle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
            var bounds = info.monitorInfo.rcMonitor;
            var work = info.monitorInfo.rcWork;
            var gdiDeviceName = info.szDevice.ToString();
            gdiNames.Add(gdiDeviceName);

            placements.Add(new MonitorPlacement(
                DeviceId: StableDeviceId(gdiDeviceName),
                PixelWidth: bounds.right - bounds.left,
                PixelHeight: bounds.bottom - bounds.top,
                WorkLeftPx: work.left,
                WorkTopPx: work.top,
                WorkWidthPx: work.right - work.left,
                WorkHeightPx: work.bottom - work.top,
                ScalePercent: dpiX == 0 ? 100 : (int)Math.Round(dpiX * 100.0 / 96),
                IsPrimary: (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0));
        }
        // Cloned or mirrored outputs can report the same device path; ids key every layout, so they must be unique (M8a).
        var uniqueIds = DisplayFingerprint.UniqueDeviceIds(placements.Select(placement => placement.DeviceId).ToList(), orderKeys: gdiNames);
        return placements.Select((placement, index) => placement with { DeviceId = uniqueIds[index] }).ToList();
    }

    /// <summary>
    /// The monitor's device interface path (unique per physical monitor and port, stable across reboots).
    /// Falls back to the GDI name ("\\.\DISPLAY1"), which is unique but can change when ports change.
    /// </summary>
    private static unsafe string StableDeviceId(string gdiDeviceName)
    {
        var device = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        if (PInvoke.EnumDisplayDevices(gdiDeviceName, 0, ref device, GetDeviceInterfaceName))
        {
            var interfacePath = device.DeviceID.ToString();
            if (!string.IsNullOrWhiteSpace(interfacePath)) return interfacePath;
        }
        return gdiDeviceName;
    }
}
```
`src/NeoFences.Shell/DesktopWatcher.cs`:
```csharp
using NeoFences.Core.Membership;

namespace NeoFences.Shell;

/// <summary>
/// Watches the user's and the Public Desktop folders. Events arrive on thread-pool threads; marshal them yourself.
/// <see cref="Overflowed"/> means events were lost (buffer overflow, folder gone): re-enumerate and reconcile, and
/// recreate the watcher (.NET stops raising events after any error other than an overflow). It is also raised when an
/// event cannot be read reliably (a rename split across buffers, an item that cannot be checked right now).
/// Special icons (Recycle Bin, ...) are not watched; they are picked up by the next reconcile.
/// </summary>
public sealed class DesktopWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private volatile bool _disposed; // events still in flight after Dispose are dropped (the next start reconciles)

    public event Action<DesktopChange>? Changed;
    /// <summary>The watcher lost events or stopped: re-create it and reconcile.</summary>
    public event Action? Overflowed;
    /// <summary>One event could not be read reliably (a split rename, a locked file): reconcile; the watcher still runs (M8a review).</summary>
    public event Action? ReconcileNeeded;

    /// <param name="log">Called for a folder that cannot be watched; the other folder is still watched.</param>
    public DesktopWatcher(Action<string, Exception> log)
    {
        foreach (var directory in new[] { DesktopItems.UserDesktop, DesktopItems.PublicDesktop }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _watchers.Add(Watch(directory));
            }
            catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
            {
                log(directory, failure);
            }
        }
    }

    private FileSystemWatcher Watch(string directory)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
            IncludeSubdirectories = false,
            InternalBufferSize = 64 * 1024,
        };
        watcher.Created += (_, created) =>
        {
            switch (Visibility(created.FullPath))
            {
                case true: Raise(new DesktopChange.Created(created.FullPath)); break;
                case null: LostTrack(); break; // could not look: let a reconcile decide (M8a)
            }
        };
        watcher.Deleted += (_, deleted) => Raise(new DesktopChange.Deleted(deleted.FullPath));
        watcher.Renamed += (_, renamed) =>
        {
            // A rename split across two notification buffers arrives with one name empty: the pair is lost (M8a).
            if (string.IsNullOrEmpty(renamed.Name) || string.IsNullOrEmpty(renamed.OldName))
            {
                LostTrack();
                return;
            }
            switch (Visibility(renamed.FullPath))
            {
                case true: Raise(new DesktopChange.Renamed(renamed.OldFullPath, renamed.FullPath)); break;
                case false: Raise(new DesktopChange.Deleted(renamed.OldFullPath)); break;
                default: LostTrack(); break;
            }
        };
        // Attributes changed: a file the user (or an installer) hid or unhid. Created/Deleted are no-ops if nothing changed.
        watcher.Changed += (_, changed) =>
        {
            switch (Visibility(changed.FullPath))
            {
                case true: Raise(new DesktopChange.Created(changed.FullPath)); break;
                case false: Raise(new DesktopChange.Deleted(changed.FullPath)); break;
                default: LostTrack(); break;
            }
        };
        watcher.Error += (_, _) => { if (!_disposed) Overflowed?.Invoke(); };
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void Raise(DesktopChange change)
    {
        if (!_disposed) Changed?.Invoke(change);
    }

    private void LostTrack()
    {
        if (!_disposed) ReconcileNeeded?.Invoke();
    }

    /// <summary>
    /// Whether the item shows on the desktop; null when it could not be checked (a file still being written, a sharing
    /// violation): such an item used to be treated as gone (M2b review), now a reconcile decides (M8a).
    /// </summary>
    private static bool? Visibility(string path)
    {
        try
        {
            // GetAttributes, not Exists: Exists swallows every error as "gone", so a locked file looked deleted (M8a review).
            var attributes = File.GetAttributes(path);
            FileSystemInfo entry = attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(path) : new FileInfo(path);
            return DesktopItems.IsVisibleOnDesktop(entry);
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var watcher in _watchers) watcher.Dispose();
    }
}
```
`src/NeoFences.Shell/FenceWindowChrome.cs`:
```csharp
using System.Runtime.InteropServices;
using NeoFences.Core.Layouts;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>Native look and placement of a fence window: tool-window styles, accent blur, rounded corners, pixel placement.</summary>
public static class FenceWindowChrome
{
    /// <summary>
    /// Tint (AABBGGRR) passed with the accent blur. M2c found Windows ignores it for ACCENT_ENABLE_BLURBEHIND (changing it
    /// changed nothing); the light-mode veil is drawn by the fence itself (ADR-015).
    /// </summary>
    public const uint DefaultTint = 0x40201A16;

    /// <summary>
    /// Not in the taskbar or Alt+Tab, not activated just by being shown, and no maximize/minimize boxes: without
    /// them a double-click on the title (a Fences habit, roll-up in M5) or Aero Snap cannot maximize the fence
    /// and save a full-screen rect (M2a review).
    /// </summary>
    public static void ApplyToolWindowStyles(nint handle)
    {
        var style = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE,
            style & ~(nint)(WINDOW_STYLE.WS_MAXIMIZEBOX | WINDOW_STYLE.WS_MINIMIZEBOX));
        var exStyle = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            exStyle | (nint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE));
    }

    /// <summary>Blur of whatever is behind the window, focus-independent. Requires a layered (AllowsTransparency) window.</summary>
    /// <returns>False if Windows rejected the call; the fence then shows its plain tint (fallback).</returns>
    public static unsafe bool ApplyAccentBlur(nint handle, uint tintAbgr = DefaultTint)
    {
        var policy = new AccentPolicy { AccentState = AccentEnableBlurBehind, GradientColor = tintAbgr };
        var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = (nint)(&policy), SizeOfData = sizeof(AccentPolicy) };
        return SetWindowCompositionAttribute(handle, ref data) != 0;
    }

    /// <summary>
    /// Rounded corners drawn by Windows 11 itself (DWMWA_WINDOW_CORNER_PREFERENCE), which also round the accent blur.
    /// A window region does not: Windows draws the blur over the whole rectangle, so a square of blur showed outside
    /// the rounded border (user screenshot 2026-10-03, M8a). On Windows 10 the call is refused and the fence keeps
    /// square blur corners under its rounded border (the region WindowChrome keeps still shapes clicks).
    /// </summary>
    /// <returns>False when Windows refused (Windows 10): the caller logs it once (M8a review).</returns>
    public static unsafe bool UseRoundedCorners(nint handle)
    {
        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        return PInvoke.DwmSetWindowAttribute((HWND)handle, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, sizeof(DWM_WINDOW_CORNER_PREFERENCE)).Succeeded;
    }

    /// <summary>When the message being handled was posted (ms tick, wraps): a click's own time, not the time it is handled.</summary>
    public static uint MessageTime() => unchecked((uint)PInvoke.GetMessageTime());

    /// <summary>The user's double-click time and size: a title double-click is recognised by NeoFences itself (M8b).</summary>
    public static (uint Milliseconds, int WidthPx, int HeightPx) DoubleClickSettings() =>
        (PInvoke.GetDoubleClickTime(), PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDOUBLECLK), PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDOUBLECLK));

    /// <summary>Reads the RECT that WM_MOVING / WM_SIZING point to (lParam).</summary>
    public static unsafe PixelRect ReadRect(nint rectPointer)
    {
        var rect = (RECT*)rectPointer;
        return new PixelRect(rect->left, rect->top, rect->right - rect->left, rect->bottom - rect->top);
    }

    /// <summary>Writes back the RECT of WM_MOVING / WM_SIZING; Windows then moves or sizes the window there.</summary>
    public static unsafe void WriteRect(nint rectPointer, PixelRect value) =>
        *(RECT*)rectPointer = new RECT(value.X, value.Y, value.X + value.Width, value.Y + value.Height);

    public static PixelRect GetPixelRect(nint handle)
    {
        PInvoke.GetWindowRect((HWND)handle, out var rect);
        return new PixelRect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
    }

    /// <summary>
    /// Sends the fence below every app window. Showing a window puts it on top of the z-order; because the fence is
    /// owned by Progman, "bottom" ends just above the desktop (an owned window always stays above its owner).
    /// </summary>
    public static void SendToBack(nint handle) =>
        PInvoke.SetWindowPos((HWND)handle, HWND.HWND_BOTTOM, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>
    /// Call from WM_WINDOWPOSCHANGING (lParam): any z-order change (activation by a click, Show, another app being
    /// restored) ends at the bottom, just above the desktop. Being owned by Progman alone does not keep an activated
    /// fence below apps (user report 2026-10-02: the Inbox drew over Firefox).
    /// </summary>
    public static unsafe void KeepAtBottom(nint windowPosPointer)
    {
        var windowPos = (WINDOWPOS*)windowPosPointer;
        if ((windowPos->flags & SET_WINDOW_POS_FLAGS.SWP_NOZORDER) == 0) windowPos->hwndInsertAfter = HWND.HWND_BOTTOM;
    }

    /// <summary>Peek (spec §4.6): fences above every window while on; back to the bottom (above the desktop) when off.</summary>
    public static void SetTopmost(nint handle, bool topmost)
    {
        const SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        PInvoke.SetWindowPos((HWND)handle, topmost ? HWND.HWND_TOPMOST : HWND.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
        if (!topmost) SendToBack(handle);
    }

    /// <summary>A topmost window the mouse passes through (the draw-a-fence rectangle): never activated, never hit.</summary>
    public static void MakeOverlay(nint handle)
    {
        var exStyle = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle | (nint)(WINDOW_EX_STYLE.WS_EX_TRANSPARENT
            | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST));
    }

    /// <summary>Mouse position in physical screen pixels (roll-up hover).</summary>
    public static (int X, int Y) GetCursorPosition()
    {
        PInvoke.GetCursorPos(out var position);
        return (position.X, position.Y);
    }

    /// <summary>Moves and sizes without changing z-order or activating.</summary>
    public static void SetPixelRect(nint handle, PixelRect rect)
    {
        const SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        // ponytail: set twice so a move onto a monitor with another DPI (WM_DPICHANGED resizes us) still ends at the requested size.
        PInvoke.SetWindowPos((HWND)handle, HWND.Null, rect.X, rect.Y, rect.Width, rect.Height, flags);
        PInvoke.SetWindowPos((HWND)handle, HWND.Null, rect.X, rect.Y, rect.Width, rect.Height, flags);
    }

    // ponytail: undocumented user32 API with no CsWin32 metadata (CLAUDE.md rule 5 exception, ADR-011); fallback is the tint.
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;
}
```

- [ ] **Step 2: Build** — `dotnet build src/NeoFences.Shell` → 0 warnings, 0 errors. The App does not build until Task 3 (`TryRecycle` returns three values).

- [ ] **Step 3: Commit**

```powershell
git add src/NeoFences.Shell
git commit -m "feat: watched the recycle bin and desktop icon settings, reached same-name public desktop items through their folder and refined the item menu, recycling and drops"
```

---

### Task 3: App — shell worker, wiring, fence window, Settings, single instance

**Files:**
- Create: `src/NeoFences.App/ShellWorker.cs`.
- Replace: `src/NeoFences.App/App.cs`, `FenceHost.cs`, `FenceWindow.xaml`, `FenceWindow.xaml.cs`, `SettingsWindow.xaml`, `SettingsWindow.xaml.cs` and `SystemMessageWindow.cs`.

**Interfaces:**
- Consumes: Tasks 1 and 2.
- Produces:
  - `ShellWorker.Run(Action)`;
  - `FenceWindow.ItemMenuRequested(refs, x, y, fromKeyboard)` and `ReloadSpecialIcons()`;
  - `SystemMessageWindow.SpecialIconsChanged`.

- [ ] **Step 1: Files**

`src/NeoFences.App/ShellWorker.cs`:
```csharp
using System.Collections.Concurrent;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// One STA thread for shell operations that can block or show Windows' dialogs (open, recycle, rename): the fences keep
/// responding meanwhile, and shell handlers get the apartment they expect (M3a review). Operations run in order; a
/// failure is logged, never thrown.
/// </summary>
public sealed class ShellWorker : IDisposable
{
    private readonly BlockingCollection<Action> _operations = new();

    public ShellWorker()
    {
        var worker = new Thread(Work) { IsBackground = true, Name = "NeoFences shell worker" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public void Run(Action operation)
    {
        if (!_operations.IsAddingCompleted) _operations.TryAdd(operation);
    }

    private void Work()
    {
        foreach (var operation in _operations.GetConsumingEnumerable())
        {
            try
            {
                operation();
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                Log.Warning(failure, "a shell operation failed");
            }
        }
    }

    public void Dispose() => _operations.CompleteAdding();
}
```
`src/NeoFences.App/App.cs`:
```csharp
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>The WPF application in normal mode (see <see cref="Program"/>).</summary>
public sealed class App : Application
{
    private Mutex? _singleInstance;
    private EventWaitHandle? _exitSignal;
    private RegisteredWaitHandle? _exitWait;
    private bool _ownsSingleInstance;
    private FenceHost? _host;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs startupArgs)
    {
        base.OnStartup(startupArgs);

        _singleInstance = new Mutex(initiallyOwned: true, name: @"Local\NeoFences.Main", out _ownsSingleInstance);
        // An instance still exiting (Setup closing the old version while starting the new one) gets a few seconds to go,
        // instead of this one quitting silently and leaving the desktop without fences (M8c: 1.1.0 did not start after Setup).
        if (!_ownsSingleInstance) _ownsSingleInstance = WaitForPreviousInstance(_singleInstance);
        ConfigureLogging(fileName: "neofences-.log");
        if (!_ownsSingleInstance)
        {
            Log.Information("NeoFences is already running (pid {ProcessId} exits)", Environment.ProcessId);
            Log.CloseAndFlush();
            Shutdown();
            return;
        }
        Log.Information("NeoFences starting, pid {ProcessId}, OS {OsVersion}", Environment.ProcessId, Environment.OSVersion.Version);
        if (SessionState.IsShuttingDown())
        {
            // Never hide icons in a session that is ending: they would stay hidden at the next sign-in (FWF_NOICONS persists).
            Log.Warning("session is shutting down; not starting");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, unhandled) => OnFatal(unhandled.Exception, source: "UI thread");
        AppDomain.CurrentDomain.UnhandledException += (_, unhandled) => OnFatal(unhandled.ExceptionObject as Exception, source: "background thread");
        // WPF answers WM_QUERYENDSESSION itself and then shuts the app down (it cannot be bypassed without blocking
        // sign-out). We bring the icons back right here, still inside the query (ADR-013).
        SessionEnding += (_, sessionEnding) =>
        {
            Log.Information("session ending ({Reason})", sessionEnding.ReasonSessionEnding);
            _host?.OnSessionEnding();
        };

        _exitSignal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, Program.ExitSignalName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => Dispatcher.BeginInvoke(Shutdown), null, Timeout.Infinite, executeOnlyOnce: true);

        _host = new FenceHost();
        _host.ExitRequested += Shutdown;
        _host.Start();
    }

    private void OnFatal(Exception? failure, string source)
    {
        // Not handled on purpose: the process dies and the watchdog restores icons and restarts us (ADR-005).
        Log.Fatal(failure, "unhandled exception on the {Source}", source);
        _host?.EmergencyRestoreIcons();
        Log.CloseAndFlush();
    }

    protected override void OnExit(ExitEventArgs exitArgs)
    {
        if (_host is not null)
        {
            _host.Shutdown();
            Log.Information("NeoFences exited");
        }
        Log.CloseAndFlush();
        _exitWait?.Unregister(null);
        _exitSignal?.Dispose();
        if (_ownsSingleInstance) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(exitArgs);
    }

    /// <summary>Shared: a second instance (or an install hook) can add its line while the running one holds the file.</summary>
    public static void ConfigureLogging(string fileName) =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(AppPaths.LogsDirectory, fileName), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true)
            .CreateLogger();

    private static bool WaitForPreviousInstance(Mutex singleInstance)
    {
        try
        {
            return singleInstance.WaitOne(TimeSpan.FromSeconds(5));
        }
        catch (AbandonedMutexException)
        {
            return true; // the previous instance was killed: the mutex is ours now
        }
    }
}
```
`src/NeoFences.App/SystemMessageWindow.cs`:
```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// Hidden top-level window that receives system broadcasts: Explorer restarts (TaskbarCreated), display or
/// work-area changes, and light/dark mode switches. Message-only windows do not get broadcasts, so this is an
/// invisible normal window.
/// It also receives global hotkeys (WM_HOTKEY), the tray icon's clicks and session lock/unlock notices.
/// Sign-out / shutdown is WPF's job (Application.SessionEnding, ADR-013), not this window's.
/// </summary>
public sealed class SystemMessageWindow : IDisposable
{
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private const int SpiSetWorkArea = 0x002F;

    private readonly HwndSource _source;

    public event Action? ExplorerRestarted;
    public event Action? DisplayChanged;
    public event Action? ThemeChanged;
    /// <summary>A RegisterHotKey hotkey of this window was pressed (its id).</summary>
    public event Action<int>? HotkeyPressed;
    /// <summary>The tray icon was clicked: show the tray menu at this screen point (px).</summary>
    public event Action<int, int>? TrayMenuRequested;
    /// <summary>The user signed back in from the lock screen (WM_WTSSESSION_CHANGE).</summary>
    public event Action? SessionUnlocked;
    /// <summary>The Recycle Bin (or another icon image) changed: refresh the special icons (M8c).</summary>
    public event Action? SpecialIconsChanged;

    /// <summary>False when Windows refused unlock notices (very early at sign-in): the hook is then not re-installed on unlock.</summary>
    public bool SessionNotificationsActive { get; }

    /// <summary>Receives the global hotkeys (Peek, M5).</summary>
    public nint Handle => _source.Handle;

    public SystemMessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("NeoFences.SystemMessages") { WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(OnMessage);
        SessionNotificationsActive = SessionNotifications.Register(_source.Handle);
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case GlobalHotkey.WmHotkey:
                HotkeyPressed?.Invoke((int)wParam);
                return 0;
            case SessionNotifications.WmSessionChange when SessionNotifications.IsUnlock(wParam):
                SessionUnlocked?.Invoke();
                return 0;
            case WmDisplayChange:
            case WmDpiChanged:
            case WmSettingChange when wParam == SpiSetWorkArea:
                DisplayChanged?.Invoke();
                return 0;
            // Light/dark switch: WM_SETTINGCHANGE with the string "ImmersiveColorSet".
            case WmSettingChange when lParam != 0 && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet":
                ThemeChanged?.Invoke();
                return 0;
        }
        if (SpecialIconNotifications.IsRecycleBinNotice(message))
        {
            SpecialIconsChanged?.Invoke();
            return 0;
        }
        if ((uint)message == DesktopHost.TaskbarCreatedMessage) ExplorerRestarted?.Invoke();
        if ((uint)message == TrayIcon.CallbackMessage && TrayIcon.IsMenuRequest(wParam, lParam, out var screenX, out var screenY))
        {
            TrayMenuRequested?.Invoke(screenX, screenY);
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        SessionNotifications.Unregister(_source.Handle);
        _source.Dispose();
    }
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
        <!-- Replaced in code (ApplyChrome) with the 8-DIP corner radius: ADR-024. -->
        <WindowChrome GlassFrameThickness="0" CaptionHeight="30" ResizeBorderThickness="6" UseAeroCaptionButtons="False" />
    </WindowChrome.WindowChrome>
    <Window.Resources>
        <sys:Double x:Key="IconSize" xmlns:sys="clr-namespace:System;assembly=System.Runtime">48</sys:Double>
        <sys:Double x:Key="ItemWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">76</sys:Double>
        <sys:Double x:Key="EditItemWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">76</sys:Double>
        <!-- Icon-only fences (M8b): labels collapse, the name pops under the hovered or selected icon instead. -->
        <Visibility x:Key="LabelVisibility">Visible</Visibility>
        <sys:Boolean x:Key="ItemToolTips" xmlns:sys="clr-namespace:System;assembly=System.Runtime">True</sys:Boolean>
        <Visibility x:Key="ShortcutArrowVisibility">Collapsed</Visibility>
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
            <DockPanel x:Name="TitleBar" Background="Transparent">
                <!-- Portal browsing (M4): back to the parent folder. Hit-testable inside the caption area. -->
                <Button x:Name="BackButton" DockPanel.Dock="Left" Visibility="Collapsed" Content="‹" ToolTip="Back (Backspace)"
                        Margin="6,3,0,3" Padding="8,0" FontSize="16" Style="{StaticResource BannerButton}"
                        WindowChrome.IsHitTestVisibleInChrome="True" />
                <TextBlock x:Name="TitleText" Foreground="{DynamicResource FenceText}" FontWeight="SemiBold" Margin="12,0"
                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
            </DockPanel>
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
                        <MenuItem x:Name="NewPortalItem" Header="New Portal fence…" />
                        <MenuItem x:Name="RenameItem" Header="Rename fence" />
                        <MenuItem x:Name="IconSizeItem" Header="Icon size" />
                        <MenuItem x:Name="LabelsItem" Header="Labels">
                            <MenuItem x:Name="LabelsAlwaysItem" Header="Always" IsCheckable="True" />
                            <MenuItem x:Name="LabelsOnHoverItem" Header="On hover (icons only)" IsCheckable="True" />
                        </MenuItem>
                        <MenuItem x:Name="SortItem" Header="Sort by" />
                        <MenuItem x:Name="OpenFolderItem" Header="Open folder in Explorer" Visibility="Collapsed" />
                        <MenuItem x:Name="LockItem" Header="Lock position" IsCheckable="True" />
                        <MenuItem x:Name="DeleteItem" Header="Delete fence (items go to the Inbox)" />
                        <Separator />
                        <MenuItem x:Name="TakeoverItem" Header="Hide desktop icons" IsCheckable="True" />
                        <MenuItem x:Name="StartupItem" Header="Start with Windows" IsCheckable="True" />
                        <MenuItem x:Name="SettingsItem" Header="Settings…" />
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
                                <Setter Property="ToolTipService.IsEnabled" Value="{DynamicResource ItemToolTips}" />
                                <EventSetter Event="MouseEnter" Handler="OnItemMouseEnter" />
                                <EventSetter Event="MouseLeave" Handler="OnItemMouseLeave" />
                                <Setter Property="AutomationProperties.Name" Value="{Binding Label}" />
                                <Setter Property="FocusVisualStyle" Value="{x:Null}" />
                                <Setter Property="Template">
                                    <Setter.Value>
                                        <ControlTemplate TargetType="ListBoxItem">
                                            <!-- The outer border fills the gaps between cells, so the pointer is always over some cell and the
                                                 pop-under name does not blink between neighbours (final review I2). -->
                                            <Border Background="Transparent" Padding="2">
                                                <Border x:Name="Chrome" Background="Transparent" CornerRadius="4" Padding="2,4">
                                                    <ContentPresenter />
                                                </Border>
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
                                <StackPanel x:Name="Cell" Width="{DynamicResource ItemWidth}">
                                    <Grid HorizontalAlignment="Center">
                                        <Image Source="{Binding Icon}" Width="{DynamicResource IconSize}" Height="{DynamicResource IconSize}" />
                                        <!-- Shortcut arrow (M8b, Settings switch): a white tile with a blue curved arrow, like Windows draws. -->
                                        <Viewbox x:Name="ShortcutArrow" Visibility="Collapsed" HorizontalAlignment="Left" VerticalAlignment="Bottom"
                                                 Width="{DynamicResource ArrowSize}" Height="{DynamicResource ArrowSize}" IsHitTestVisible="False">
                                            <Canvas Width="16" Height="16">
                                                <Rectangle Width="16" Height="16" RadiusX="2" RadiusY="2" Fill="White" Stroke="#FF8A8A8A" StrokeThickness="0.75" />
                                                <Path Data="M4,12 C4,7.5 6.5,5.5 10,5.5 L10,3.5 L13,6.75 L10,10 L10,8 C7.5,8 5.5,9 4,12 Z" Fill="#FF2A6FD6" />
                                            </Canvas>
                                        </Viewbox>
                                    </Grid>
                                    <Grid Margin="0,4,0,0">
                                        <TextBlock x:Name="Label" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
                                                   Visibility="{DynamicResource LabelVisibility}"
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
                                    <DataTrigger Binding="{Binding IsShortcut}" Value="True">
                                        <Setter TargetName="ShortcutArrow" Property="Visibility" Value="{DynamicResource ShortcutArrowVisibility}" />
                                    </DataTrigger>
                                    <DataTrigger Binding="{Binding IsEditing}" Value="True">
                                        <Setter TargetName="LabelBox" Property="Visibility" Value="Visible" />
                                        <Setter TargetName="Label" Property="Visibility" Value="Hidden" />
                                        <!-- Icons-only cells are narrow: the rename box gets a labelled cell's width (M8b review). -->
                                        <Setter TargetName="Cell" Property="Width" Value="{DynamicResource EditItemWidth}" />
                                    </DataTrigger>
                                </DataTemplate.Triggers>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                    <!-- Portal whose folder cannot be read (missing, offline, denied). -->
                    <TextBlock x:Name="PortalMessage" Visibility="Collapsed" Margin="12" TextWrapping="Wrap"
                               Foreground="{DynamicResource FenceSubtleText}" IsHitTestVisible="False" />
                    <!-- Rubber-band selection (M3b): drawn while dragging on empty space. -->
                    <Canvas IsHitTestVisible="False" ClipToBounds="True">
                        <Rectangle x:Name="SelectionBand" Visibility="Collapsed" Fill="{DynamicResource FenceHover}"
                                   Stroke="{DynamicResource FenceSubtleText}" StrokeThickness="1" RadiusX="2" RadiusY="2" />
                        <!-- Drag-drop feedback (M3b review I2): where a drop lands, or which folder takes it. -->
                        <Rectangle x:Name="InsertCaret" Visibility="Collapsed" Width="2" Fill="{DynamicResource FenceText}" RadiusX="1" RadiusY="1" />
                        <!-- Icon-only fences (M8b): the hovered or selected item's name, under its icon, over the neighbours. -->
                        <Border x:Name="HoverLabel" Visibility="Collapsed" CornerRadius="4" Padding="6,2"
                                Background="{DynamicResource HoverLabelBackground}" BorderBrush="{DynamicResource FenceBorder}" BorderThickness="1">
                            <TextBlock x:Name="HoverLabelText" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
                                       TextAlignment="Center" TextWrapping="Wrap" MaxWidth="180" />
                        </Border>
                        <Rectangle x:Name="DropHighlight" Visibility="Collapsed" Fill="{DynamicResource FenceSelected}"
                                   Stroke="{DynamicResource FenceText}" StrokeThickness="1" RadiusX="4" RadiusY="4" />
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
using System.Windows.Media.Animation;
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
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmNcLeftButtonDoubleClick = 0x00A3;
    private const int HitTestCaption = 2;
    private const double CornerRadiusDips = 8;
    private const double CaptionHeightDips = 30;
    private const double ResizeBorderDips = 6;

    // The menu lists ConfigNormalizer.IconSizes (one list, M2c review carry-over); these are only their names.
    private static readonly Dictionary<int, string> IconSizeNames = new() { [32] = "Small", [48] = "Medium", [64] = "Large", [96] = "Extra large" };

    private readonly ObservableCollection<FenceItemView> _items = [];
    private readonly IconLoader _iconLoader;
    private ListBoxItem? _hoverLabelContainer; // the cell the pop-under name belongs to
    private int _iconSizeDips;
    private bool _renaming;
    private string _title = "";
    private readonly bool _isPortal;
    private DragTracker? _drag;
    private bool _locked;
    private bool _rolledUp;
    private readonly RollUpExpansion _expansion; // rolled up, but open right now (hover or click, M5/M6b)
    private int _fullHeightPx;              // height when not rolled up (physical pixels)
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer;
    private readonly System.Windows.Threading.DispatcherTimer _heightAnimation;
    private System.Diagnostics.Stopwatch _heightClock = new();
    private int _heightFrom;
    private int _heightTo;
    private int _fadeGeneration;
    private int? _moveHeightPx; // a move that interrupted a roll-up animation keeps this height (M6b review M2)
    private static readonly TimeSpan RollUpDuration = TimeSpan.FromMilliseconds(200); // spec §6
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(150)); // spec §6

    public string FenceId { get; }

    public nint Handle { get; private set; }

    /// <summary>Windows refused the rounded-corner preference (Windows 10); the host logs it once.</summary>
    public bool CornersUnavailable { get; private set; }

    /// <summary>Peek (M5): while set the fence may rise above apps instead of staying at the bottom.</summary>
    public bool Peeking { get; set; }

    /// <summary>
    /// Asked before each roll-up or fade (spec §6): off when Windows' "Animation effects" are off, and in game mode
    /// (spec §4.7). Set by the host.
    /// </summary>
    public Func<bool> AnimationsAllowed { get; set; } = () => false;

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
    /// <summary>Right-click or menu key on items: refs (the clicked item first), screen point, opened from the keyboard.</summary>
    public event Action<IReadOnlyList<string>, int, int, bool>? ItemMenuRequested;
    /// <summary>Del: send these items to the Recycle Bin.</summary>
    public event Action<IReadOnlyList<string>>? RecycleRequested;
    /// <summary>An in-place rename was confirmed: item ref, new name as typed.</summary>
    public event Action<string, string>? ItemRenameRequested;
    /// <summary>The user started dragging these items out of the fence (M3b).</summary>
    public event Action<IReadOnlyList<string>>? DragRequested;
    /// <summary>Portal (M4): back to the parent folder (Back button, Backspace).</summary>
    public event Action? BackRequested;
    public event Action? NewPortalRequested;
    public event Action<FenceSort>? SortRequested;
    public event Action? OpenFolderRequested;
    /// <summary>The "Start with Windows" toggle changed (ADR-019).</summary>
    public event Action<bool>? StartupToggled;
    /// <summary>"Settings…" in the fence menu (M6b).</summary>
    public event Action? SettingsRequested;
    /// <summary>Double-click on the title: roll up to the title bar, or back down (M5).</summary>
    public event Action? RollUpToggled;
    /// <summary>Fence menu → Labels (M8b): always, or only on hover / selection.</summary>
    public event Action<LabelMode>? LabelModeRequested;

    private Point? _pressPoint;                 // left button pressed on an item: a drag may start
    private ListBoxItem? _deferredSelect;       // pressed on an already selected item: select it alone only on release
    private Point? _bandStart;                  // left button pressed on empty space: rubber band
    private ListBoxItem? _deferredToggle;       // Ctrl+press on a selected item: unselect it on release unless it was dragged
    private LabelMode _labelMode = LabelMode.Always;
    private ListBoxItem? _hoveredContainer;
    private (uint At, Point Where)? _lastCaptionPress; // a title double-click recognised by NeoFences itself (M8b); message time
    private uint? _recognisedDoubleClickAt;

    public FenceWindow(Fence fence, bool takeoverActive, bool lightTheme, IconLoader iconLoader, RollupExpand rollupExpand)
    {
        _expansion = new RollUpExpansion(rollupExpand);
        FenceId = fence.Id;
        _iconLoader = iconLoader;
        InitializeComponent();
        _title = fence.Title;
        TitleText.Text = fence.Title;
        _isPortal = fence.Source.Kind == FenceSourceKind.Portal;
        OpenFolderItem.Visibility = _isPortal ? Visibility.Visible : Visibility.Collapsed;
        DeleteItem.Header = _isPortal ? "Delete fence (the folder is not touched)" : "Delete fence (items go to the Inbox)";
        // Desktop fences sort once (dragging keeps working); Portals keep the chosen order live, so it is checked.
        foreach (var (sort, name) in new[] { (FenceSort.Name, "Name"), (FenceSort.Type, "Type"), (FenceSort.Date, "Date (newest first)") })
        {
            var sortItem = new MenuItem { Header = name, Tag = sort, IsCheckable = _isPortal, IsChecked = _isPortal && fence.Sort == sort };
            sortItem.Click += (_, _) => SortRequested?.Invoke(sort);
            SortItem.Items.Add(sortItem);
        }
        NewPortalItem.Click += (_, _) => NewPortalRequested?.Invoke();
        OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
        BackButton.Click += (_, _) => BackRequested?.Invoke();
        TakeoverItem.IsChecked = takeoverActive;
        DeleteItem.Visibility = fence.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        foreach (var size in ConfigNormalizer.IconSizes)
        {
            var sizeItem = new MenuItem { Header = IconSizeNames.GetValueOrDefault(size, $"{size} px"), Tag = size, IsCheckable = true };
            sizeItem.Click += (_, _) => IconSizeRequested?.Invoke(size);
            IconSizeItem.Items.Add(sizeItem);
        }
        NewFenceItem.Click += (_, _) => NewFenceRequested?.Invoke();
        RenameItem.Click += (_, _) => BeginRename();
        LockItem.Click += (_, _) => LockToggled?.Invoke(LockItem.IsChecked);
        DeleteItem.Click += (_, _) => DeleteRequested?.Invoke();
        TakeoverItem.Click += (_, _) => TakeoverToggled?.Invoke(TakeoverItem.IsChecked);
        ExitItem.Click += (_, _) => ExitRequested?.Invoke();
        StartupItem.Click += (_, _) => StartupToggled?.Invoke(StartupItem.IsChecked);
        SettingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        LabelsAlwaysItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.Always);
        LabelsOnHoverItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.OnHover);
        TitleBox.MaxLength = FenceEdits.MaxTitleLength; // the cut in FenceEdits.Rename never surprises the user (M2c review)
        ItemList.SelectionChanged += (_, _) => UpdateHoverLabel();
        ItemList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateHoverLabel()));
        HoverLabel.SizeChanged += (_, _) => PlaceHoverLabel(); // the bound name arrived or changed (final review I3)
        Loaded += (_, _) => ReloadIcons(); // placed on its monitor: one load at the right size (mixed DPI, M2b review)
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
        _labelMode = fence.Labels;
        SetIconSize(fence.IconSize);
        SetLabelMode(fence.Labels);
        _rolledUp = fence.RolledUp;
        // Rolled up, the fence opens on hover or click (setting) and closes again shortly after the pointer leaves.
        _hoverTimer = new System.Windows.Threading.DispatcherTimer { Interval = HoverTick };
        _hoverTimer.Tick += (_, _) => OnHoverTick();
        if (_rolledUp) _hoverTimer.Start();
        _heightAnimation = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _heightAnimation.Tick += (_, _) => StepHeight();
        Closed += (_, _) =>
        {
            _hoverTimer.Stop(); // a deleted fence must not keep ticking on its dead handle (M5 review M1)
            _heightAnimation.Stop();
        };
        SetLocked(fence.Locked);
        // Unlocked, the title is caption (WM_NCLBUTTONDBLCLK); locked, it is client area.
        TitleBar.MouseLeftButtonDown += (_, click) =>
        {
            if (click.ClickCount == 2) RollUpToggled?.Invoke();
            else if (click.ClickCount == 1) ClickToOpen();
        };
        SourceInitialized += OnSourceInitialized;
        // Icons are rendered for one DPI. DpiChanged is routed: every new item raises it too, so react only to the window's own.
        DpiChanged += (_, dpiChange) =>
        {
            if (dpiChange.OriginalSource != this || dpiChange.OldDpi.PixelsPerDip == dpiChange.NewDpi.PixelsPerDip) return;
            // A rolled-up fence keeps its full height in physical pixels: rescale it with the monitor (M5 review carry-over).
            _fullHeightPx = (int)Math.Round(_fullHeightPx * dpiChange.NewDpi.DpiScaleY / dpiChange.OldDpi.DpiScaleY);
            ReloadIcons();
        };
    }

    public void SetTakeoverChecked(bool active) => TakeoverItem.IsChecked = active;

    public void SetStartupChecked(bool startWithWindows) => StartupItem.IsChecked = startWithWindows;

    public void ShowTakeoverPrompt(bool visible) => TakeoverPrompt.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetTitle(string title)
    {
        _title = title;
        TitleText.Text = title;
    }

    /// <summary>Portal (M4): what the title shows while browsing ("Downloads › Mods"), and whether Back is offered.</summary>
    public void SetPortalLocation(string breadcrumb, bool canGoBack)
    {
        TitleText.Text = breadcrumb;
        BackButton.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Portal (M4): a message instead of items (folder missing or unreadable); null hides it.</summary>
    public void ShowPortalMessage(string? message)
    {
        PortalMessage.Text = message ?? "";
        PortalMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Portal (M4): the sort shown as checked.</summary>
    public void SetSortChecked(FenceSort sort)
    {
        foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = _isPortal && (FenceSort)sortItem.Tag == sort;
    }

    /// <summary>
    /// Shows exactly these items in this order, by moving, adding and removing views in place: items already shown keep
    /// their name, icon, selection and the scroll position (M2b review carry-over; duplicate refs are tolerated).
    /// </summary>
    public void SetItems(IReadOnlyList<string> itemRefs)
    {
        if (_items.Select(view => view.ItemRef).SequenceEqual(itemRefs, StringComparer.Ordinal)) return;
        var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var itemRef in itemRefs) wanted[itemRef] = wanted.GetValueOrDefault(itemRef) + 1;
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            var itemRef = _items[index].ItemRef;
            if (wanted.GetValueOrDefault(itemRef) > 0) wanted[itemRef]--;
            else RemoveItemAt(index);
        }
        // ponytail: O(n²) moves in the worst case (a full reorder of hundreds of items); a keyed diff when that shows up.
        var iconSizePx = IconSizePx;
        for (var index = 0; index < itemRefs.Count; index++)
        {
            if (index < _items.Count && _items[index].ItemRef == itemRefs[index]) continue;
            var found = -1;
            for (var later = index + 1; later < _items.Count && found < 0; later++)
            {
                if (_items[later].ItemRef == itemRefs[index]) found = later;
            }
            if (found >= 0)
            {
                CancelRename(_items[found]);
                _items.Move(found, index);
                continue;
            }
            var view = new FenceItemView(itemRefs[index]);
            if (IsLoaded) _iconLoader.Request(view, iconSizePx); // before that, Loaded requests them at the right DPI (M2b review)
            _items.Insert(index, view);
        }
        // Cells may have shifted under a shown name without a scroll or selection event (final review I3).
        Dispatcher.BeginInvoke(UpdateHoverLabel, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// A rename whose own item is moved or removed would lose its box (and commit half-typed text): cancel only that one,
    /// right before. An item that merely shifts keeps its box (M3a review I3; final review M1).
    /// </summary>
    private static void CancelRename(FenceItemView view)
    {
        if (view.IsEditing) view.IsEditing = false;
    }

    private void RemoveItemAt(int index)
    {
        CancelRename(_items[index]);
        _items.RemoveAt(index);
    }

    private int IconSizePx => (int)Math.Round(_iconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);

    /// <summary>One of <see cref="ConfigNormalizer.IconSizes"/> (DIPs). Icons are reloaded at the new size.</summary>
    public void SetIconSize(int iconSizeDips)
    {
        if (iconSizeDips == _iconSizeDips) return; // nothing to reload (M2c review carry-over)
        _iconSizeDips = iconSizeDips;
        Resources["IconSize"] = (double)iconSizeDips;
        Resources["ArrowSize"] = Math.Max(12.0, Math.Round(iconSizeDips * 0.36));
        ApplyItemWidth();
        foreach (var sizeItem in IconSizeItem.Items.OfType<MenuItem>()) sizeItem.IsChecked = (int)sizeItem.Tag == iconSizeDips;
        ReloadIcons();
    }

    /// <summary>Labels always, or icons only with the name popping under the hovered or selected icon (M8b, user choice).</summary>
    public void SetLabelMode(LabelMode labelMode)
    {
        _labelMode = labelMode;
        Resources["LabelVisibility"] = labelMode == LabelMode.Always ? Visibility.Visible : Visibility.Collapsed;
        Resources["ItemToolTips"] = labelMode == LabelMode.Always; // the pop-under name replaces the tooltip
        LabelsAlwaysItem.IsChecked = labelMode == LabelMode.Always;
        LabelsOnHoverItem.IsChecked = labelMode == LabelMode.OnHover;
        ApplyItemWidth();
        UpdateHoverLabel();
    }

    /// <summary>Settings → "Show shortcut arrows" (M8b, off by default).</summary>
    public void SetShortcutArrows(bool show) => Resources["ShortcutArrowVisibility"] = show ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyItemWidth()
    {
        Resources["EditItemWidth"] = LabelledItemWidth;
        ApplyCellWidth();
    }

    private void ApplyCellWidth() =>
        // With labels: room for two short words under small icons. Icons only: a tight grid.
        Resources["ItemWidth"] = _labelMode == LabelMode.Always ? LabelledItemWidth : _iconSizeDips + 12.0;

    private double LabelledItemWidth => Math.Max(76.0, _iconSizeDips + 28.0);

    private void OnItemMouseEnter(object sender, MouseEventArgs args)
    {
        _hoveredContainer = sender as ListBoxItem;
        UpdateHoverLabel();
    }

    private void OnItemMouseLeave(object sender, MouseEventArgs args)
    {
        if (ReferenceEquals(_hoveredContainer, sender)) _hoveredContainer = null;
        UpdateHoverLabel();
    }

    /// <summary>
    /// Icon-only fences: the name of the hovered item (else the one selected item) pops up under its icon, over the
    /// neighbours, kept inside the fence (above the icon when there is no room below). Drawn in the fence window itself,
    /// so it stays at the fence's place in the window stack (no popup window above other apps).
    /// </summary>
    private void UpdateHoverLabel()
    {
        var container = _labelMode != LabelMode.OnHover ? null
            : _hoveredContainer ?? (ItemList.SelectedItems.Count == 1 ? ItemList.ItemContainerGenerator.ContainerFromItem(ItemList.SelectedItem) as ListBoxItem : null);
        if (container is not { DataContext: FenceItemView { IsEditing: false } view, IsVisible: true })
        {
            HoverLabel.Visibility = Visibility.Collapsed;
            return;
        }
        _hoverLabelContainer = container;
        HoverLabel.DataContext = view; // bound: a name that loads later shows up (final review I3)
        // Never wider than the fence (narrow icon-only fences, final review I1); 18 = padding + border + margins.
        HoverLabelText.MaxWidth = Math.Clamp(ItemList.ActualWidth - 18, 24, 180);
        HoverLabel.Visibility = Visibility.Visible;
        PlaceHoverLabel();
    }

    /// <summary>Under the cell, clamped to the fence's sides; above it when there is no room; hidden when it is scrolled away.</summary>
    private void PlaceHoverLabel()
    {
        if (HoverLabel.Visibility != Visibility.Visible || _hoverLabelContainer is not { IsVisible: true } container) return;
        HoverLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = HoverLabel.DesiredSize;
        var cell = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
        if (cell.Bottom <= 0 || cell.Top >= ItemList.ActualHeight)
        {
            HoverLabel.Visibility = Visibility.Collapsed; // a selected item scrolled out of view (final review I1)
            return;
        }
        var left = Math.Clamp(cell.Left + cell.Width / 2 - size.Width / 2, 2, Math.Max(2, ItemList.ActualWidth - size.Width - 2));
        var top = cell.Bottom - 2;
        if (top + size.Height > ItemList.ActualHeight - 2) top = Math.Max(2, cell.Top - size.Height + 2);
        Canvas.SetLeft(HoverLabel, left);
        Canvas.SetTop(HoverLabel, top);
    }

    /// <summary>Locked: the title no longer drags and the edges no longer resize.</summary>
    public void SetLocked(bool locked)
    {
        LockItem.IsChecked = locked;
        _locked = locked;
        ApplyChrome();
    }

    /// <summary>Locked: no drag, no resize. Rolled up: drag, no resize (its height is the stored full height).</summary>
    private void ApplyChrome()
    {
        // A fresh WindowChrome each time: editing the attached one in place is not re-applied after an unlock.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(0),
            CaptionHeight = _locked ? 0 : CaptionHeightDips,
            ResizeBorderThickness = new Thickness(_locked || _rolledUp ? 0 : ResizeBorderDips),
            // WindowChrome owns the window region and re-applies it on every resize: a radius of 0 kept resetting it to
            // a square, so the blur showed outside the rounded border (user screenshot 2026-10-03). Same radius as the border.
            CornerRadius = new CornerRadius(CornerRadiusDips),
            UseAeroCaptionButtons = false,
        });
    }

    /// <summary>Puts the fence at its full rect (physical pixels); rolled up, only the title bar of it shows.</summary>
    public void Place(PixelRect fullRect)
    {
        _heightAnimation.Stop();
        _fullHeightPx = fullRect.Height;
        FenceWindowChrome.SetPixelRect(Handle, _rolledUp && !_expansion.Expanded ? fullRect with { Height = RolledUpHeightPx } : fullRect);
    }

    public void SetRolledUp(bool rolledUp)
    {
        if (rolledUp == _rolledUp) return;
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (rolledUp && !_expansion.Expanded) _fullHeightPx = _heightAnimation.IsEnabled ? _heightTo : current.Height;
        _rolledUp = rolledUp;
        _expansion.Reset();
        ApplyChrome();
        AnimateHeight(rolledUp ? RolledUpHeightPx : _fullHeightPx);
        if (rolledUp) _hoverTimer.Start();
        else _hoverTimer.Stop();
    }

    /// <summary>Roll-up expand mode from settings (M6b): hover or click.</summary>
    public void SetRollupExpand(RollupExpand mode) => _expansion.Mode = mode;

    /// <summary>Shows at once (Pause, layout): a quick-hide fade still running must not hide the fence afterwards (M8a).</summary>
    public void ShowNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Show();
    }

    /// <summary>Hides at once (Pause).</summary>
    public void HideNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    /// <summary>Quick-hide (spec §6): a 150 ms fade, then hidden. Without animations it hides at once.</summary>
    public void HideFaded()
    {
        var generation = ++_fadeGeneration;
        if (!IsVisible || !AnimationsAllowed())
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
            return;
        }
        var fade = new DoubleAnimation(0, FadeDuration);
        fade.Completed += (_, _) =>
        {
            if (generation != _fadeGeneration) return; // shown again meanwhile
            Hide();
            BeginAnimation(OpacityProperty, null);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Shows the fence (fading in when animations are on). The host sends it to the bottom afterwards.</summary>
    public void ShowFaded()
    {
        ++_fadeGeneration;
        var fadeIn = AnimationsAllowed();
        BeginAnimation(OpacityProperty, null);
        Show();
        if (fadeIn) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeDuration));
    }

    /// <summary>Click mode: a single click on the rolled-up title opens it.</summary>
    private bool ClickToOpen()
    {
        if (!_rolledUp || !_expansion.Click()) return false;
        AnimateHeight(_fullHeightPx);
        return true;
    }

    /// <summary>Roll-up and roll-down move the bottom edge over 200 ms (ease-out); at once without animations.</summary>
    private void AnimateHeight(int targetPx)
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (!AnimationsAllowed() || _drag is not null || current.Height == targetPx)
        {
            _heightAnimation.Stop();
            FenceWindowChrome.SetPixelRect(Handle, current with { Height = targetPx });
            return;
        }
        _heightFrom = current.Height;
        _heightTo = targetPx;
        _heightClock = System.Diagnostics.Stopwatch.StartNew();
        _heightAnimation.Start();
    }

    private void StepHeight()
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        var progress = Math.Min(1.0, _heightClock.Elapsed / RollUpDuration);
        if (_drag is not null) progress = 1; // never fight a move: jump to the end
        var eased = 1 - Math.Pow(1 - progress, 3);
        FenceWindowChrome.SetPixelRect(Handle, current with { Height = (int)Math.Round(_heightFrom + (_heightTo - _heightFrom) * eased) });
        if (progress >= 1) _heightAnimation.Stop();
    }

    private static readonly TimeSpan HoverTick = TimeSpan.FromMilliseconds(100);

    /// <summary>Title row plus the 1-DIP border above and below it.</summary>
    private int RolledUpHeightPx => (int)Math.Round((CaptionHeightDips + 2) * VisualTreeHelper.GetDpi(this).DpiScaleY);

    // ponytail: polls the cursor every 100 ms while rolled up (no mouse-leave on a no-activate layered window when
    // the pointer leaves fast); in hover mode it also opens during a file drag, which is wanted.
    private void OnHoverTick()
    {
        if (Handle == 0 || !_rolledUp) return;
        if (_drag is not null || BodyContextMenu.IsOpen || _renaming || _items.Any(view => view.IsEditing)) return; // never close under the user
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        var rect = FenceWindowChrome.GetPixelRect(Handle);
        // While the height animates, judge "inside" against where the fence is going, so it does not flicker shut.
        var height = _heightAnimation.IsEnabled ? Math.Max(rect.Height, _heightTo) : rect.Height;
        var inside = cursorX >= rect.X && cursorX < rect.X + rect.Width && cursorY >= rect.Y && cursorY < rect.Y + height;
        if (!_expansion.Tick(inside)) return;
        AnimateHeight(_expansion.Expanded ? _fullHeightPx : RolledUpHeightPx);
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
        var hoverLabel = new SolidColorBrush(light ? Color.FromArgb(0xF0, 0xF4, 0xF4, 0xF4) : Color.FromArgb(0xE6, 0x20, 0x22, 0x28));
        hoverLabel.Freeze();
        Resources["HoverLabelBackground"] = hoverLabel;
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

    /// <summary>The Recycle Bin turned full or empty, or another special icon changed (M8c).</summary>
    public void ReloadSpecialIcons()
    {
        var iconSizePx = IconSizePx;
        foreach (var view in _items.Where(view => view.ItemRef.StartsWith("::", StringComparison.Ordinal))) _iconLoader.Request(view, iconSizePx);
    }

    /// <summary>New size or DPI: every icon is requested again in place; names, selection and renames stay (M2c review carry-over).</summary>
    private void ReloadIcons()
    {
        var iconSizePx = IconSizePx;
        foreach (var view in _items) _iconLoader.Request(view, iconSizePx);
    }

    /// <summary>Starts renaming the fence title (menu, or a freshly drawn fence).</summary>
    public void BeginRename()
    {
        _renaming = true;
        TitleBox.Text = _title;
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
        if (commit && TitleBox.Text != _title) RenameRequested?.Invoke(TitleBox.Text);
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
            case Key.Back when _isPortal:
                BackRequested?.Invoke();
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
        // The clicked item first: menu → Rename renames it, whatever else is selected (user choice 2026-10-03).
        List<string> refs = [clicked.ItemRef, .. ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).Where(itemRef => itemRef != clicked.ItemRef)];
        ItemMenuRequested?.Invoke(refs, (int)anchor.X, (int)anchor.Y, args.CursorLeft < 0);
    }

    /// <summary>The item under a screen point (physical pixels) and the index to insert before when dropping there.</summary>
    public FenceDropPoint HitTest(int screenX, int screenY)
    {
        var point = ItemList.PointFromScreen(new Point(screenX, screenY));
        string? hovered = null;
        var cells = new List<(double Left, double Top, double Width, double Height)>();
        var cellIndexes = new List<int>();
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            // Only the middle of an item means "into it" (folders, Recycle Bin); its edges reorder (M3b review I2).
            if (DropZones.IsInto(bounds.Left, bounds.Top, bounds.Width, bounds.Height, point.X, point.Y)) hovered = _items[index].ItemRef;
            cells.Add((bounds.Left, bounds.Top, bounds.Width, bounds.Height));
            cellIndexes.Add(index);
        }
        // Rows reach down to their tallest item (mixed label heights, M3b review).
        var cellAt = DropZones.InsertIndex(cells, point.X, point.Y);
        return new FenceDropPoint(hovered, cellAt < cellIndexes.Count ? cellIndexes[cellAt] : _items.Count);
    }

    /// <summary>Shows where a drag would land: a caret before the insert position, or a highlight on the container taking it.</summary>
    public void ShowDropFeedback(FenceDropPoint? drop, bool into)
    {
        InsertCaret.Visibility = Visibility.Collapsed;
        DropHighlight.Visibility = Visibility.Collapsed;
        if (drop is not { } point) return;
        if (into && _items.FirstOrDefault(view => view.ItemRef == point.ItemRef) is { } target
            && ItemList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem targetContainer)
        {
            var cell = targetContainer.TransformToAncestor(ItemList).TransformBounds(new Rect(targetContainer.RenderSize));
            Canvas.SetLeft(DropHighlight, cell.Left);
            Canvas.SetTop(DropHighlight, cell.Top);
            DropHighlight.Width = cell.Width;
            DropHighlight.Height = cell.Height;
            DropHighlight.Visibility = Visibility.Visible;
            return;
        }
        // Caret at the left edge of the item it goes before, or after the last item.
        var before = point.InsertAt < _items.Count ? ItemList.ItemContainerGenerator.ContainerFromIndex(point.InsertAt) as ListBoxItem : null;
        var anchor = before ?? (_items.Count > 0 ? ItemList.ItemContainerGenerator.ContainerFromIndex(_items.Count - 1) as ListBoxItem : null);
        if (anchor is null) return;
        var bounds = anchor.TransformToAncestor(ItemList).TransformBounds(new Rect(anchor.RenderSize));
        Canvas.SetLeft(InsertCaret, (before is null ? bounds.Right : bounds.Left) - 1);
        Canvas.SetTop(InsertCaret, bounds.Top + 4);
        InsertCaret.Height = Math.Max(0, bounds.Height - 8);
        InsertCaret.Visibility = Visibility.Visible;
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
        // Text selection in the rename box must never start a drag of the file (M3b review I3).
        if (args.OriginalSource is DependencyObject pressed && FindAncestor<TextBox>(pressed) is not null) return;
        var container = args.OriginalSource is DependencyObject element ? FindAncestor<ListBoxItem>(element) : null;
        if (container is null)
        {
            // Empty space: rubber band. Without Ctrl it starts a new selection. The list takes focus, so a pending rename
            // commits and the keys after the band go to the list (M3b review carry-over).
            ItemList.Focus();
            _bandStart = args.GetPosition(ItemList);
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ItemList.SelectedItems.Clear();
            ItemList.CaptureMouse();
            args.Handled = true;
            return;
        }
        _pressPoint = args.GetPosition(ItemList);
        // Ctrl+press on a selected item: unselect it on release, not now, so Ctrl+drag can still copy the selection (M3b review).
        if (container.IsSelected && Keyboard.Modifiers == ModifierKeys.Control && args.ClickCount == 1)
        {
            _deferredToggle = container;
            container.Focus();
            args.Handled = true;
            return;
        }
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
        _deferredToggle = null; // dragged: the item stays selected
        var dragged = ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).ToList();
        if (dragged.Count > 0) DragRequested?.Invoke(dragged); // returns when the drag ends (Windows' modal loop)
    }

    private void OnListRelease(object sender, MouseButtonEventArgs args)
    {
        _pressPoint = null;
        if (_deferredToggle is { } toggled)
        {
            toggled.IsSelected = false;
            _deferredToggle = null;
        }
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
        // A double-click in the rename box selects a word; it must not open the file (M3a review carry-over).
        if (args.OriginalSource is DependencyObject source && FindAncestor<TextBox>(source) is not null) return;
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is ListBoxItem { DataContext: FenceItemView view })
            OpenRequested?.Invoke(view.ItemRef);
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        Handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(Handle).AddHook(OnMessage);
        FenceWindowChrome.ApplyToolWindowStyles(Handle);
        FenceWindowChrome.ApplyAccentBlur(Handle);
        if (!FenceWindowChrome.UseRoundedCorners(Handle)) CornersUnavailable = true; // Windows 10: square blur corners (ADR-024)
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
                if (!Peeking) FenceWindowChrome.KeepAtBottom(lParam); // fences never rise above apps, except during Peek
                break;
            // Click mode: the first press on a rolled-up title opens it instead of starting a move (M6b).
            case WmNcLeftButtonDown when wParam == HitTestCaption && IsSecondCaptionClick(lParam):
                RollUpToggled?.Invoke();
                handled = true;
                return 0;
            case WmNcLeftButtonDown when wParam == HitTestCaption && ClickToOpen():
                handled = true;
                return 0;
            case WmNcLeftButtonDoubleClick when wParam == HitTestCaption:
                _lastCaptionPress = null;
                // A third quick click after a recognised double-click arrives as DBLCLK: not a second toggle (M8b review).
                if (!IsRightAfterRecognisedDoubleClick()) RollUpToggled?.Invoke();
                handled = true;
                return 0;
            case WmEnterSizeMove:
                // A move that starts during a roll-up animation finishes it. Windows' move loop captured the partial rect
                // and proposes it on every step, so WM_MOVING keeps the finished height too (M6b review M2).
                _moveHeightPx = null;
                if (_heightAnimation.IsEnabled)
                {
                    _heightAnimation.Stop();
                    _moveHeightPx = _heightTo;
                    FenceWindowChrome.SetPixelRect(Handle, FenceWindowChrome.GetPixelRect(Handle) with { Height = _heightTo });
                }
                _drag = new DragTracker(FenceWindowChrome.GetPixelRect(Handle));
                break;
            case WmMoving when SnapRect is not null && _drag is not null:
                var proposed = FenceWindowChrome.ReadRect(lParam);
                if (_moveHeightPx is { } heightPx) proposed = proposed with { Height = heightPx };
                FenceWindowChrome.WriteRect(lParam, _drag.Step(proposed, snap: rect => SnapRect(rect, SnapEdges.Move)));
                handled = true;
                return 1;
            case WmSizing when SnapRect is not null && _drag is not null:
                var edges = SizingEdges((int)wParam);
                FenceWindowChrome.WriteRect(lParam, _drag.Step(FenceWindowChrome.ReadRect(lParam), snap: rect => SnapRect(rect, edges)));
                handled = true;
                return 1;
            case WmExitSizeMove:
                _drag = null;
                _moveHeightPx = null;
                // Rolled up, the window is shorter than the fence: the stored rect keeps the full height.
                var moved = FenceWindowChrome.GetPixelRect(Handle);
                MovedByUser?.Invoke(this, _rolledUp ? moved with { Height = _fullHeightPx } : moved);
                if (_rolledUp && !_expansion.Expanded && moved.Height != RolledUpHeightPx) AnimateHeight(RolledUpHeightPx); // a move during an animation
                break;
        }
        return 0;
    }

    /// <summary>
    /// Every caption press is remembered; a second one within the double-click time and size counts as a double-click.
    /// Right after another window hands the fence the foreground, Windows sends the second press as a plain
    /// WM_NCLBUTTONDOWN instead of WM_NCLBUTTONDBLCLK, so the first double-click did nothing (M6a finding 4, M8b).
    /// </summary>
    /// <param name="pointParam">The press's screen point (WM_NCLBUTTONDOWN's lParam): where it happened, not where the
    /// cursor is when a busy UI thread gets to it; its time is the message's own (M8b review).</param>
    private bool IsSecondCaptionClick(nint pointParam)
    {
        var (milliseconds, widthPx, heightPx) = FenceWindowChrome.DoubleClickSettings();
        var at = FenceWindowChrome.MessageTime();
        var (x, y) = ((short)(pointParam & 0xFFFF), (short)((pointParam >> 16) & 0xFFFF));
        var isSecond = _lastCaptionPress is { } last && unchecked(at - last.At) <= milliseconds
                       && Math.Abs(x - last.Where.X) <= widthPx / 2.0 && Math.Abs(y - last.Where.Y) <= heightPx / 2.0;
        _lastCaptionPress = isSecond ? null : (at, new Point(x, y));
        if (isSecond) _recognisedDoubleClickAt = at;
        return isSecond;
    }

    private bool IsRightAfterRecognisedDoubleClick() =>
        _recognisedDoubleClickAt is { } recognised && unchecked(FenceWindowChrome.MessageTime() - recognised) <= FenceWindowChrome.DoubleClickSettings().Milliseconds;

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
`src/NeoFences.App/SettingsWindow.xaml`:
```xml
<Window x:Class="NeoFences.App.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NeoFences settings" Width="600" Height="680" MinWidth="460" MinHeight="400"
        WindowStartupLocation="CenterScreen" ThemeMode="System">
    <!-- Fluent (WPF's built-in theme, ThemeMode="System"): follows Windows light/dark and the accent colour (spec §6).
         Set only on this window: the fences keep their own look. Changes apply at once; there is no OK button. -->
    <Window.Resources>
        <Style x:Key="SectionHeader" TargetType="TextBlock">
            <Setter Property="FontSize" Value="14" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Margin" Value="2,20,0,8" />
        </Style>
        <Style x:Key="Card" TargetType="Border">
            <Setter Property="Background" Value="{DynamicResource CardBackgroundFillColorDefaultBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource CardStrokeColorDefaultBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="6" />
            <Setter Property="Padding" Value="16,12" />
            <Setter Property="Margin" Value="0,0,0,4" />
        </Style>
        <Style x:Key="Description" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
            <Setter Property="FontSize" Value="12" />
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="Margin" Value="0,2,0,0" />
        </Style>
    </Window.Resources>
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="28,16,28,28">
            <TextBlock Text="Settings" FontSize="28" FontWeight="SemiBold" />

            <TextBlock Text="General" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="StartupBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Start with Windows" />
                    <StackPanel>
                        <TextBlock Text="Start with Windows" TextWrapping="Wrap" />
                        <TextBlock x:Name="StartupDescription" Style="{StaticResource Description}" Text="Your fences come back by themselves after a restart or a power cut." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="TakeoverBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Hide desktop icons" />
                    <StackPanel>
                        <TextBlock Text="Hide desktop icons" TextWrapping="Wrap" />
                        <TextBlock x:Name="TakeoverDescription" Style="{StaticResource Description}" Text="Show your desktop items only in fences. They are back on the desktop whenever NeoFences is paused or closed." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <TextBox x:Name="HotkeyBox" DockPanel.Dock="Right" Width="190" IsReadOnly="True" IsReadOnlyCaretVisible="False"
                                 VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Peek hotkey" />
                        <StackPanel>
                            <TextBlock Text="Peek hotkey" TextWrapping="Wrap" />
                            <TextBlock x:Name="HotkeyDescription" Style="{StaticResource Description}" Text="Shows your fences above all windows. Click the box, then press the new combination (with Alt or Win, or an F-key)." />
                        </StackPanel>
                    </DockPanel>
                    <!-- Assertive live region: screen readers announce a refused combination at once (M6b review carry-over). -->
                    <TextBlock x:Name="HotkeyStatus" Style="{StaticResource Description}" Margin="0,8,0,0" Visibility="Collapsed"
                               AutomationProperties.LiveSetting="Assertive" />
                </StackPanel>
            </Border>

            <TextBlock Text="Fences" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <ComboBox x:Name="LabelsBox" DockPanel.Dock="Right" Width="230" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Labels for new fences">
                            <ComboBoxItem Content="Always" Tag="Always" />
                            <ComboBoxItem Content="On hover (icons only)" Tag="OnHover" />
                        </ComboBox>
                        <StackPanel>
                            <TextBlock Text="Labels for new fences" TextWrapping="Wrap" />
                            <TextBlock x:Name="LabelsDescription" Style="{StaticResource Description}" Text="Icons only shows an item's name when you point at it or select it. Each fence can also choose in its own menu." />
                        </StackPanel>
                    </DockPanel>
                    <Button x:Name="LabelsApplyAllButton" Content="Apply to all fences" HorizontalAlignment="Left" Margin="0,10,0,0" />
                </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="ArrowsBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Show shortcut arrows" />
                    <StackPanel>
                        <TextBlock Text="Show shortcut arrows" TextWrapping="Wrap" />
                        <TextBlock x:Name="ArrowsDescription" Style="{StaticResource Description}" Text="The small arrow Windows draws on shortcut icons." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <ComboBox x:Name="RollupBox" DockPanel.Dock="Right" Width="230" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Rolled-up fences open">
                        <ComboBoxItem Content="When the mouse rests on them" Tag="Hover" />
                        <ComboBoxItem Content="When you click their title" Tag="Click" />
                    </ComboBox>
                    <StackPanel>
                        <TextBlock Text="Rolled-up fences open" TextWrapping="Wrap" />
                        <TextBlock x:Name="RollupDescription" Style="{StaticResource Description}" Text="Double-click a fence's title to roll it up to its title bar." />
                    </StackPanel>
                </DockPanel>
            </Border>

            <TextBlock Text="Game mode" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <CheckBox x:Name="GameModeBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Go idle while a full-screen game runs" />
                        <StackPanel>
                            <TextBlock Text="Go idle while a full-screen game runs" TextWrapping="Wrap" />
                            <TextBlock x:Name="GameModeDescription" Style="{StaticResource Description}" Text="NeoFences removes its mouse hook and waits with background work, so games get every bit of input. Fences stay where they are." />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock x:Name="GameModeStatus" Style="{StaticResource Description}" Margin="0,8,0,0" />
                </StackPanel>
            </Border>

            <TextBlock Text="About and logs" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="VersionText" />
                    <TextBlock x:Name="DataFolderText" Style="{StaticResource Description}" />
                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                        <Button x:Name="OpenLogsButton" Content="Open logs folder" Margin="0,0,8,0" />
                        <Button x:Name="OpenDataButton" Content="Open data folder" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </StackPanel>
    </ScrollViewer>
</Window>
```
`src/NeoFences.App/SettingsWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>What the settings window shows; the host builds it from the config and the run state.</summary>
/// <param name="PeekHotkey">As a person reads it (key caps).</param>
/// <param name="PeekHotkeyActive">False when Windows refused it (another app owns it): the window says so.</param>
public sealed record SettingsView(
    bool StartWithWindows, bool Takeover, string PeekHotkey, bool PeekHotkeyActive, RollupExpand RollupExpand,
    bool GameModeEnabled, bool GameModeActive, string Version, string DataFolder, LabelMode DefaultLabels, bool ShowShortcutArrows);

/// <summary>
/// The settings window (spec §6, M6b): General, Fences, Game mode, About and logs. Every change is reported to the host
/// at once; the host applies, saves and calls <see cref="Show(SettingsView)"/> back with the result.
/// </summary>
public partial class SettingsWindow : Window
{
    private bool _updating; // filling the controls from the host must not report changes back

    public event Action<bool>? StartWithWindowsChanged;
    public event Action<bool>? TakeoverChanged;
    /// <summary>A new Peek hotkey was pressed in the box (text like "Ctrl+Alt+P"); answer with <see cref="ShowHotkeyResult"/>.</summary>
    public event Action<string>? PeekHotkeyChosen;
    public event Action<RollupExpand>? RollupExpandChanged;
    public event Action<bool>? GameModeChanged;
    public event Action? OpenLogsRequested;
    public event Action? OpenDataRequested;
    /// <summary>The hotkey box got (true) or lost (false) the keyboard: the host releases the Peek hotkey meanwhile (M6b review).</summary>
    public event Action<bool>? HotkeyRecording;
    public event Action<LabelMode>? DefaultLabelsChanged;
    public event Action<LabelMode>? LabelsAppliedToAll;
    public event Action<bool>? ShortcutArrowsChanged;

    public SettingsWindow()
    {
        InitializeComponent();
        // Checked/Unchecked, not Click: UI Automation (Narrator, Toggle) changes the box without a click (M6b smoke).
        OnToggled(StartupBox, isChecked => StartWithWindowsChanged?.Invoke(isChecked));
        OnToggled(TakeoverBox, isChecked => TakeoverChanged?.Invoke(isChecked));
        OnToggled(GameModeBox, isChecked => GameModeChanged?.Invoke(isChecked));
        RollupBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && RollupBox.SelectedItem is ComboBoxItem { Tag: string mode }) RollupExpandChanged?.Invoke(Enum.Parse<RollupExpand>(mode));
        };
        HotkeyBox.PreviewKeyDown += OnHotkeyKeyDown;
        HotkeyBox.GotKeyboardFocus += (_, _) =>
        {
            HotkeyRecording?.Invoke(true); // so pressing the current combination is recorded, not Peek
            ShowHotkeyHint("Press the new combination… (Esc keeps the current one)");
        };
        HotkeyBox.LostKeyboardFocus += (_, _) =>
        {
            HotkeyRecording?.Invoke(false);
            if (HotkeyStatus.Tag is null) HotkeyStatus.Visibility = Visibility.Collapsed;
        };
        OnToggled(ArrowsBox, isChecked => ShortcutArrowsChanged?.Invoke(isChecked));
        LabelsBox.SelectionChanged += (_, _) => { if (!_updating) DefaultLabelsChanged?.Invoke(SelectedLabels); };
        LabelsApplyAllButton.Click += (_, _) => LabelsAppliedToAll?.Invoke(SelectedLabels);
        // Screen readers read each setting's description with it (M6b review carry-over).
        foreach (var (control, description) in new (UIElement, TextBlock)[]
                 { (StartupBox, StartupDescription), (TakeoverBox, TakeoverDescription), (HotkeyBox, HotkeyDescription),
                   (LabelsBox, LabelsDescription), (ArrowsBox, ArrowsDescription), (RollupBox, RollupDescription), (GameModeBox, GameModeDescription) })
        {
            System.Windows.Automation.AutomationProperties.SetHelpText(control, description.Text);
        }
        OpenLogsButton.Click += (_, _) => OpenLogsRequested?.Invoke();
        OpenDataButton.Click += (_, _) => OpenDataRequested?.Invoke();
    }

    private void OnToggled(CheckBox box, Action<bool> report)
    {
        box.Checked += (_, _) => { if (!_updating) report(true); };
        box.Unchecked += (_, _) => { if (!_updating) report(false); };
    }

    private LabelMode SelectedLabels => LabelsBox.SelectedIndex == 1 ? LabelMode.OnHover : LabelMode.Always;

    public void Show(SettingsView view)
    {
        _updating = true;
        LabelsBox.SelectedIndex = view.DefaultLabels == LabelMode.OnHover ? 1 : 0;
        ArrowsBox.IsChecked = view.ShowShortcutArrows;
        if (!view.PeekHotkeyActive && !HotkeyBox.IsKeyboardFocused)
        {
            ShowHotkeyResult(saved: false, message: $"{view.PeekHotkey} is not active: Windows or another app owns it. Record another combination.");
        }
        StartupBox.IsChecked = view.StartWithWindows;
        TakeoverBox.IsChecked = view.Takeover;
        HotkeyBox.Text = view.PeekHotkey;
        RollupBox.SelectedIndex = view.RollupExpand == RollupExpand.Click ? 1 : 0;
        GameModeBox.IsChecked = view.GameModeEnabled;
        GameModeStatus.Text = !view.GameModeEnabled ? "Off: NeoFences stays fully active during games."
            : view.GameModeActive ? "Right now: idle, a full-screen app is in front." : "Right now: active (no full-screen app in front).";
        VersionText.Text = $"NeoFences {view.Version}";
        DataFolderText.Text = $"Settings, backups and logs: {view.DataFolder}";
        _updating = false;
    }

    /// <summary>The host's answer to <see cref="PeekHotkeyChosen"/>: saved, or why not (invalid, or taken by another app).</summary>
    public void ShowHotkeyResult(bool saved, string message)
    {
        var changed = HotkeyStatus.Text != message || HotkeyStatus.Visibility != Visibility.Visible;
        HotkeyStatus.Tag = saved ? null : "error"; // an error stays visible after the box loses focus
        HotkeyStatus.Text = message;
        HotkeyStatus.Foreground = saved ? SecondaryText : System.Windows.Media.Brushes.IndianRed;
        HotkeyStatus.Visibility = Visibility.Visible;
        if (changed) AnnounceHotkeyStatus(); // Settings refreshes often (game mode): say a warning once
    }

    private void AnnounceHotkeyStatus() =>
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(HotkeyStatus)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);

    /// <summary>Fluent's secondary text colour (the grey a missing theme resource falls back to).</summary>
    private System.Windows.Media.Brush SecondaryText => TryFindResource("TextFillColorSecondaryBrush") as System.Windows.Media.Brush ?? SystemColors.GrayTextBrush;

    private void ShowHotkeyHint(string hint)
    {
        HotkeyStatus.Tag = null;
        HotkeyStatus.Text = hint;
        HotkeyStatus.Foreground = SecondaryText;
        HotkeyStatus.Visibility = Visibility.Visible;
    }

    /// <summary>Records a combination: modifiers alone only preview; Esc leaves the box; the first other key decides.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs pressed)
    {
        var key = pressed.Key == Key.System ? pressed.SystemKey : pressed.Key; // Alt combinations arrive as Key.System
        // Tab / Shift+Tab move on and Alt+F4 closes, as everywhere: never trap them in the box (M6b review I1).
        if (key == Key.Tab && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None) return;
        if (key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt) return;
        pressed.Handled = true;
        key = key switch { Key.ImeProcessed => pressed.ImeProcessedKey, Key.DeadCharProcessed => pressed.DeadCharProcessedKey, _ => key };
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        var modifiers = Keyboard.Modifiers;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            ShowHotkeyHint(string.Join("+", parts.Append("…")));
            return;
        }
        parts.Add(key.ToString());
        PeekHotkeyChosen?.Invoke(string.Join("+", parts));
    }
}
```
`src/NeoFences.App/FenceHost.cs`:
```csharp
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Config;
using NeoFences.Core.Input;
using NeoFences.Core.Layouts;
using NeoFences.Core.Lifecycle;
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
    private const int PeekHotkeyId = 1;
    private const int PeekEscapeHotkeyId = 2;
    private static readonly TimeSpan DrawFrame = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan TrayRetryInterval = TimeSpan.FromSeconds(2);
    private const int TrayRetryAttempts = 15;

    private readonly ConfigStore _store = new(AppPaths.DataDirectory);
    private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
    private readonly SystemMessageWindow _messages = new();
    private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
    private DesktopWatcher? _desktopWatcher;
    private readonly ShellWorker _shellWorker = new(); // open, recycle, rename: off the UI thread, on STA (M3a review)
    private SpecialIconNotifications? _specialIcons;
    private readonly DispatcherTimer _specialIconsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // Watcher trouble arrives in bursts: one re-arm and one reconcile per burst; a watcher that fails again at once waits longer (M8a review).
    private readonly DispatcherTimer _watcherRecoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _rearmWatcher;
    private DateTime _lastWatcherRearm = DateTime.MinValue;
    private TimeSpan _watcherRearmDelay = TimeSpan.FromMilliseconds(500);
    // Safe-save memory and expected drop arrivals (FenceMembership.SafeSaveWindow).
    private IReadOnlyList<RememberedPlacement> _rememberedPlacements = [];
    private readonly Dictionary<string, IDisposable> _dropRegistrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PortalState> _portals = new(StringComparer.Ordinal); // M4: Portal fences by id
    private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
    private IReadOnlyList<MonitorPlacement> _monitors = [];
    private bool _takeoverActive;
    private bool _lightTheme = SystemTheme.AppsUseLightTheme();
    private bool _sessionEnding;
    // M5 desktop gestures
    private DesktopMouseHook? _mouseHook;
    private bool _quickHidden;              // double-click on the desktop: fences (and icons) hidden until the next one
    private bool _iconsHiddenByUser;        // RunState.IconsHiddenByUser: set when quick-hide begins, cleared when it ends
    private DrawFenceOverlay? _drawOverlay; // right-drag on the desktop: the fence being drawn
    private DispatcherTimer? _drawTimer;
    private (int X, int Y) _drawStart;
    private GlobalHotkey? _peekHotkey;
    private GlobalHotkey? _peekEscapeHotkey; // Esc ends Peek; registered only while peeking
    private bool _peeking;
    private bool _recordingHotkey;          // Settings' hotkey box has the keyboard: the Peek hotkey is released
    private string? _peekHotkeyProblem;     // why the Peek hotkey is not registered (Settings shows it), null when it is
    private bool _cornersLogged;
    // M6a
    private bool _paused;                    // tray: Pause NeoFences (not saved)
    private bool _gameMode;                  // a full-screen app is in front: idle (spec §4.7, ADR-021)
    private ForegroundWatcher? _foregroundWatcher;
    private TrayIcon? _trayIcon;
    private readonly List<DesktopChange> _deferredDesktopChanges = [];
    private bool _reconcileDeferred;
    private const int MaxDeferredDesktopChanges = 500;
    private const int TrayNewFence = 1, TrayQuickHide = 2, TrayPeek = 3, TrayPause = 4, TrayExit = 5, TraySettings = 6;
    private SettingsWindow? _settingsWindow; // M6b: one at a time

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
        _messages.HotkeyPressed += OnHotkey;
        _messages.TrayMenuRequested += ShowTrayMenu;
        _messages.SessionUnlocked += OnSessionUnlocked;
        _messages.SpecialIconsChanged += ScheduleSpecialIconRefresh;
        _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
        _watcherRecoveryTimer.Tick += (_, _) => RecoverDesktopWatcher();
    }

    public void Start()
    {
        var loaded = _store.Load();
        Log.Information("config loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath})",
            loaded.Source, loaded.IsReadOnly, loaded.CorruptCopyPath);
        _config = loaded.Config;
        _watchdog.LaunchDetached(Environment.ProcessId);
        ApplyStartup(); // after a power loss NeoFences must come back by itself (ADR-019)

        RefreshMonitors();
        foreach (var fence in _config.Fences) OpenWindow(fence);
        StartDesktopWatcher(); // first: an item created while the startup reconcile lists the desktop is not missed (M8a)
        ReconcileDesktop();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _specialIcons = new SpecialIconNotifications(_messages.Handle,
            settingsChanged: () => dispatcher.BeginInvoke(ScheduleSpecialIconRefresh),
            log: (what, failure) => Log.Warning(failure, "{What} unavailable: special icons change after a restart", what));
        ApplyLayout();
        if (_config.Settings.Takeover) SetTakeover(true);
        else if (_watchdog.IsTakeoverActiveMarked)
        {
            // Icons may still be hidden from a run whose Takeover-off never got saved (both processes killed): show them.
            Log.Warning("takeover-active marker found while Takeover is off; showing desktop icons");
            SetIconsHidden(false);
        }
        StartGestures();
        if (!_messages.SessionNotificationsActive) Log.Warning("unlock notices unavailable: the mouse hook is re-installed only after an Explorer restart");
        try
        {
            _trayIcon = new TrayIcon(_messages.Handle, TrayTooltip(), log: message => Log.Warning("{Message}", message));
            ShowTrayIcon();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "tray icon unavailable; fences and gestures keep working"); // hard rule 7: only the tray is lost
        }
        StartGameMode();
        ScheduleSave();
    }

    /// <summary>
    /// The clock for safe-save and drop memory: wall time at start plus a monotonic stopwatch, so a clock change (time
    /// sync, daylight saving, the user) cannot expire or extend a memory early (M8a).
    /// </summary>
    private static readonly DateTimeOffset ClockStart = DateTimeOffset.Now;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static DateTimeOffset Now => ClockStart + Clock.Elapsed;

    /// <summary>All of NeoFences' run-time modes together (Core rules: which fences, icons and hooks they imply).</summary>
    private RunState Current => new(Takeover: _takeoverActive, QuickHidden: _quickHidden, Paused: _paused, GameMode: _gameMode,
        IconsHiddenByUser: _iconsHiddenByUser);

    /// <summary>
    /// Windows asked to end the session (WPF's SessionEnding, inside WM_QUERYENDSESSION). WPF then shuts the app
    /// down, which may be the last thing that ever runs, so the icons come back now and the watchdog is told it is
    /// a session end, not a user exit: if the user cancels the shutdown, the watchdog restarts NeoFences (ADR-013).
    /// </summary>
    public void OnSessionEnding()
    {
        _sessionEnding = true;
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        _trayIcon?.Dispose(); // also on session end: a cancelled shutdown restarts us, and the old icon would linger (M8a)
        _trayIcon = null;
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        // The marker also catches a show that failed earlier (quick-hide ending while Explorer was busy; M5 review M6).
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
        _foregroundWatcher?.Dispose();
        _mouseHook?.Dispose();
        _peekHotkey?.Dispose();
        _peekEscapeHotkey?.Dispose();
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        _specialIcons?.Dispose();
        _shellWorker.Dispose();
        _iconLoader.Dispose();
        _messages.Dispose();
    }

    /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
    public void EmergencyRestoreIcons()
    {
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) DesktopIcons.TrySetHidden(false);
    }

    private void OpenWindow(Fence fence)
    {
        var window = new FenceWindow(fence, takeoverActive: _takeoverActive, lightTheme: _lightTheme, iconLoader: _iconLoader, rollupExpand: _config.Settings.RollupExpand)
        {
            // Spec §6: no animations when Windows' "Animation effects" are off; spec §4.7: none while gaming.
            AnimationsAllowed = () => !_gameMode && SystemParameters.ClientAreaAnimation,
        };
        window.SnapRect = (rect, edges) => SnapFence(window, rect, edges);
        window.MovedByUser += OnFenceMoved;
        window.RenameRequested += title => RenameFence(window, title);
        window.IconSizeRequested += iconSize => SetFenceIconSize(window, iconSize);
        window.LockToggled += locked => SetFenceLocked(window, locked);
        window.DeleteRequested += () => DeleteFence(window);
        window.NewFenceRequested += CreateFence;
        window.TakeoverToggled += SetTakeover;
        window.ExitRequested += () => ExitRequested?.Invoke();
        window.OpenRequested += itemRef => OpenOrBrowse(window, itemRef);
        window.ItemMenuRequested += (itemRefs, screenX, screenY, fromKeyboard) => ShowItemMenu(window, itemRefs, screenX, screenY, fromKeyboard);
        window.RecycleRequested += itemRefs => RecycleItems(window, itemRefs);
        window.ItemRenameRequested += (itemRef, newName) => RenameItem(window, itemRef, newName);
        window.BackRequested += () => BrowsePortal(window, back: true);
        window.NewPortalRequested += () => CreatePortal(window);
        window.StartupToggled += SetStartWithWindows;
        window.SettingsRequested += OpenSettings;
        window.LabelModeRequested += labels => SetFenceLabels(window, labels);
        window.SetShortcutArrows(_config.Settings.ShowShortcutArrows);
        window.SetStartupChecked(_config.Settings.StartWithWindows);
        window.SortRequested += sort => SortFence(window, sort);
        window.OpenFolderRequested += () => { if (_portals.TryGetValue(window.FenceId, out var portal)) OpenItem(portal.Current, ownerHandle: window.Handle); };
        if (fence.Source is { Kind: FenceSourceKind.Portal, Path: { } portalPath })
        {
            _portals[fence.Id] = new PortalState(portalPath, show: items => ShowPortal(window, items),
                logFailure: failure => Log.Warning(failure, "cannot watch Portal folder of {FenceId}", fence.Id));
        }
        window.DragRequested += itemRefs =>
            ShellDragDrop.TryDrag(window.Handle, itemRefs, logFailure: failure => Log.Warning(failure, "could not start dragging {ItemRefs}", itemRefs));
        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
        window.RollUpToggled += () => ToggleRollUp(window);
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        if (window.CornersUnavailable && !_cornersLogged)
        {
            _cornersLogged = true;
            Log.Information("Windows refused rounded corners (Windows 10?): fences keep square blur corners"); // M8a review
        }
        RegisterDrops(window); // needs the HWND
        if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", fence.Id);
        _windows[fence.Id] = window;
    }

    /// <summary>Full pass over the Desktop folders: at start and whenever watcher events were lost.</summary>
    private void ReconcileDesktop()
    {
        var listing = DesktopItems.Enumerate();
        var (reconciled, report) = FenceMembership.Reconcile(_config, listing.ItemRefs, listing.UnavailableFolders,
            remembered: _rememberedPlacements, now: Now);
        _config = reconciled;
        _rememberedPlacements = [.. _rememberedPlacements.Except(report.UsedMemories)]; // each memory places one item once (M8a review)
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
        _desktopWatcher.Overflowed += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: true));
        _desktopWatcher.ReconcileNeeded += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: false));
    }

    /// <summary>
    /// Events were lost or the watcher stopped (re-arm: .NET disables it after a non-overflow error), or one event could
    /// not be read (reconcile only). Bursts are gathered into one recovery (M8a review).
    /// </summary>
    private void OnDesktopWatcherTrouble(bool rearm)
    {
        if (_desktopWatcher is null) return; // shut down meanwhile
        _rearmWatcher |= rearm;
        if (_watcherRecoveryTimer.IsEnabled) return;
        _watcherRecoveryTimer.Interval = _rearmWatcher ? _watcherRearmDelay : TimeSpan.FromMilliseconds(500);
        _watcherRecoveryTimer.Start();
    }

    private void RecoverDesktopWatcher()
    {
        _watcherRecoveryTimer.Stop();
        if (_desktopWatcher is null) return;
        if (_rearmWatcher)
        {
            _rearmWatcher = false;
            // A watcher that fails again soon after a re-arm (a folder going offline) waits longer each time, up to a minute.
            var now = DateTime.UtcNow;
            _watcherRearmDelay = now - _lastWatcherRearm < TimeSpan.FromSeconds(10)
                ? TimeSpan.FromTicks(Math.Min(_watcherRearmDelay.Ticks * 2, TimeSpan.FromMinutes(1).Ticks))
                : TimeSpan.FromMilliseconds(500);
            _lastWatcherRearm = now;
            Log.Warning("desktop watcher lost events or stopped; re-arming and reconciling (next re-arm after {Delay})", _watcherRearmDelay);
            _desktopWatcher.Dispose();
            StartDesktopWatcher();
        }
        else
        {
            Log.Information("a desktop change could not be read; reconciling");
        }
        if (Current.ShellWorkDeferred) _reconcileDeferred = true; // after the game
        else ReconcileDesktop();
    }

    private void ScheduleSpecialIconRefresh()
    {
        _specialIconsTimer.Stop(); // a burst (several icons, a Recycle Bin emptying) is one refresh
        _specialIconsTimer.Start();
    }

    /// <summary>"Desktop icon settings" changed (reconcile), or the Recycle Bin turned full or empty (new icon) (M8c).</summary>
    private void RefreshSpecialIcons()
    {
        _specialIconsTimer.Stop();
        var shown = DesktopItems.SpecialIconRefs().ToHashSet(ItemRef.Comparer);
        var fenced = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items)
            .Where(itemRef => itemRef.StartsWith("::", StringComparison.Ordinal)).ToHashSet(ItemRef.Comparer);
        if (!shown.SetEquals(fenced))
        {
            Log.Information("desktop icon settings changed; reconciling");
            if (Current.ShellWorkDeferred) _reconcileDeferred = true;
            else ReconcileDesktop();
        }
        foreach (var window in _windows.Values) window.ReloadSpecialIcons();
        Log.Information("special icons refreshed");
    }

    private void OnDesktopChanged(DesktopChange change)
    {
        if (Current.ShellWorkDeferred)
        {
            // Applied in order when the game is left; a flood (a big download unpacking) becomes one reconcile instead (M8a).
            if (_deferredDesktopChanges.Count < MaxDeferredDesktopChanges) _deferredDesktopChanges.Add(change);
            else _reconcileDeferred = true;
            return;
        }
        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
        RefreshWindows();
        ScheduleSave();
    }

    private void RefreshWindows()
    {
        var showPrompt = !_config.Settings.TakeoverPromptAnswered && !_takeoverActive;
        foreach (var fence in _config.Fences)
        {
            if (!_windows.TryGetValue(fence.Id, out var window)) continue;
            if (!_portals.ContainsKey(fence.Id)) window.SetItems(fence.Items); // Portals refresh on their own (M4 review I1)
            window.ShowTakeoverPrompt(showPrompt && fence.IsInbox);
        }
    }

    private void OpenItem(string itemRef, nint ownerHandle)
    {
        // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6);
        // on an STA thread, as shell handlers and Windows' error dialog expect (M3a review).
        _shellWorker.Run(() =>
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
                    _rememberedPlacements = FenceMembership.ExpectArrivals(_rememberedPlacements, itemRefs, window.FenceId, insertAt, Now),
                Recycle: itemRefs => RecycleItems(window, itemRefs),
                ShowFeedback: window.ShowDropFeedback,
                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId)),
                portalFolder: () => _portals.TryGetValue(window.FenceId, out var portal) ? portal.Current : null);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "fence {FenceId} cannot accept drops", window.FenceId); // degrade: everything else still works
        }
    }

    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool fromKeyboard)
    {
        // Shift+right-click is the extended menu; Shift+F10 is just the keyboard's normal menu (M3a review).
        var extended = !fromKeyboard && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs));
        switch (choice)
        {
            case ItemMenuChoice.Rename:
                window.BeginItemRename(itemRefs[0]); // the right-clicked item (it comes first)
                break;
            case ItemMenuChoice.Delete:
                RecycleItems(window, itemRefs); // always the Recycle Bin, even with Shift held (hard rule 1)
                break;
        }
    }

    /// <summary>
    /// Windows moves them to the Recycle Bin (with its own dialogs) on the shell worker, so a long recycle never freezes
    /// the fences (M3a review); the watcher then removes them from the fence.
    /// </summary>
    private void RecycleItems(FenceWindow window, IReadOnlyList<string> itemRefs)
    {
        Log.Information("recycling {Count} item(s)", itemRefs.Count);
        var ownerHandle = window.Handle;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var (started, refused, missing) = ShellFileOps.TryRecycle(ownerHandle, itemRefs);
            if (!started) Log.Warning("recycle did not run or was cancelled: {ItemRefs}", itemRefs);
            if (missing.Count > 0) Log.Information("skipped {Count} item(s) that no longer exist: {ItemRefs}", missing.Count, missing);
            if (refused.Count > 0) dispatcher.BeginInvoke(() => ExplainRefusedRecycle(window, refused));
        });
    }

    private static void ExplainRefusedRecycle(FenceWindow window, IReadOnlyList<string> refused)
    {
        Log.Information("not deleting {Count} item(s) on drives without a Recycle Bin", refused.Count);
        System.Windows.MessageBox.Show(window,
            (refused.Count == 1 ? $"\"{Path.GetFileName(refused[0])}\" is" : $"{refused.Count} items are") +
            " on a drive without a Recycle Bin (a USB stick or network drive), so NeoFences won't delete it." +
            " Deleting there would be permanent; if you really mean it, delete it in Explorer.",
            "NeoFences", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>Windows renames the file (on the shell worker: its conflict dialogs); the watcher keeps it in its fence and position.</summary>
    private void RenameItem(FenceWindow window, string itemRef, string newName)
    {
        var ownerHandle = window.Handle;
        _shellWorker.Run(() =>
        {
            if (!ShellFileOps.TryRename(ownerHandle, itemRef, newName)) Log.Warning("rename did not run or was cancelled: {ItemRef}", itemRef);
        });
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
                window.Place(FencePlacement.ToPixels(rect, monitor));
                if (!window.IsVisible && Current.FencesVisible)
                {
                    window.ShowNow();
                    if (_peeking)
                    {
                        // A fence made during Peek (its menus no longer end it, M5 review I1) joins the others on top.
                        window.Peeking = true;
                        FenceWindowChrome.SetTopmost(window.Handle, topmost: true);
                    }
                    else FenceWindowChrome.SendToBack(window.Handle); // Show puts it above every app; fences live just above the desktop
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
            thresholdPx: (int)Math.Round(SnapThresholdDips * monitor.Scale),
            // A resize snap never makes the fence smaller than its minimum (M2c review carry-over).
            minWidthPx: (int)Math.Round(LayoutEngine.MinWidth * monitor.Scale),
            minHeightPx: (int)Math.Round(LayoutEngine.MinHeight * monitor.Scale));
    }

    private void RenameFence(FenceWindow window, string title)
    {
        _config = FenceEdits.Rename(_config, window.FenceId, title);
        window.SetTitle(_config.Fences.First(fence => fence.Id == window.FenceId).Title);
        RefreshPortal(window); // a Portal browsing a subfolder shows its breadcrumb again
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
        if (_portals.Remove(window.FenceId, out var portal)) portal.Dispose(); // the folder itself is never touched
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

    /// <summary>"New Portal fence…": Windows' folder dialog, then a fence that mirrors the folder (M4).</summary>
    private void CreatePortal(FenceWindow owner)
    {
        var folder = FolderPicker.TryPick(owner.Handle, "Choose the folder for the new Portal fence",
            logFailure: failure => Log.Warning(failure, "folder dialog failed"));
        if (folder is null) return;
        var title = Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } name ? name : folder;
        (_config, var portal) = FenceMembership.CreatePortal(_config, title: title, folderPath: folder);
        Log.Information("Portal fence created for {Folder}", folder);
        OpenWindow(portal);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
    }

    /// <summary>Asks a Portal to re-list its folder (in the background; ShowPortal follows).</summary>
    private void RefreshPortal(FenceWindow window)
    {
        if (_portals.TryGetValue(window.FenceId, out var portal)) portal.Refresh();
    }

    /// <summary>Shows a Portal's listing (null: the folder cannot be read) in its sort order (M4).</summary>
    private void ShowPortal(FenceWindow window, IReadOnlyList<ItemInfo>? items)
    {
        if (!_portals.TryGetValue(window.FenceId, out var portal)) return;
        if (_config.Fences.FirstOrDefault(candidate => candidate.Id == window.FenceId) is not { } fence) return;
        window.SetPortalLocation(portal.Breadcrumb(fence.Title), portal.CanGoBack);
        window.SetSortChecked(fence.Sort);
        window.ShowPortalMessage(items is null ? $"This folder is not available right now:\n{portal.Current}" : null);
        window.SetItems(items is null ? [] : ItemSorting.Order(items, fence.Sort));
    }

    /// <summary>Double-click / Enter: inside a Portal a folder is browsed in place (Ctrl opens it in Explorer, user choice 2026-10-03).</summary>
    private void OpenOrBrowse(FenceWindow window, string itemRef)
    {
        var inExplorer = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        if (_portals.TryGetValue(window.FenceId, out var portal) && !inExplorer && portal.IsListedFolder(itemRef))
        {
            portal.Browse(itemRef); // re-lists in the background
            return;
        }
        OpenItem(itemRef, ownerHandle: window.Handle);
        SetPeek(false); // like Fences: Peek ends once something is opened from it
    }

    private void BrowsePortal(FenceWindow window, bool back)
    {
        if (!back || !_portals.TryGetValue(window.FenceId, out var portal) || !portal.CanGoBack) return;
        portal.Back(); // re-lists in the background
    }

    /// <summary>"Sort by": a Portal keeps the order live; a desktop fence is sorted once (M4).</summary>
    private void SortFence(FenceWindow window, FenceSort sort)
    {
        var fence = _config.Fences.First(candidate => candidate.Id == window.FenceId);
        if (_portals.ContainsKey(fence.Id))
        {
            _config = FenceEdits.SetSort(_config, fence.Id, sort);
            RefreshPortal(window);
        }
        else
        {
            try
            {
                _config = FenceEdits.SetItemOrder(_config, fence.Id, ItemSorting.Order(FolderItems.Describe(fence.Items), sort));
            }
            catch (ArgumentException mismatch)
            {
                Log.Warning(mismatch, "sort of fence {FenceId} refused: the sorted list did not match its items", fence.Id); // never crash (M4 review I3)
                return;
            }
            RefreshWindows();
        }
        ScheduleSave();
    }

    private void ApplyStartup() =>
        StartupRegistration.Apply(_config.Settings.StartWithWindows, Environment.ProcessPath ?? "", log: message => Log.Information("{Message}", message));

    private void SetStartWithWindows(bool startWithWindows)
    {
        _config = _config with { Settings = _config.Settings with { StartWithWindows = startWithWindows } };
        ApplyStartup();
        foreach (var window in _windows.Values) window.SetStartupChecked(startWithWindows);
        SaveNow();
        RefreshSettings();
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
        SetQuickHidden(false); // an explicit icons choice ends quick-hide first, so the two never disagree
        _takeoverActive = active;
        // Any explicit choice (banner or menu) answers the first-run question.
        _config = _config with { Settings = _config.Settings with { Takeover = active, TakeoverPromptAnswered = true } };
        SetIconsHidden(Current.IconsHidden);
        foreach (var window in _windows.Values) window.SetTakeoverChecked(active);
        RefreshSettings();
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
        Current.IconsHidden ? SetIconsHidden(true)
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
        SetPeek(false); // re-attaching sends fences to the bottom; Peek (and its global Esc) must end with it (M5 review M2)
        ShowTrayIcon(); // Explorer forgot every tray icon
        ReinstallMouseHook(); // a hook Windows dropped silently comes back here at the latest (M5 review)
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

    /// <summary>The WH_MOUSE_LL desktop gestures and the Peek hotkey (M5). Either failing only turns that feature off.</summary>
    private void StartGestures()
    {
        UpdateMouseHook();
        UpdatePeekHotkey();
    }

    /// <summary>The Peek hotkey is registered exactly while wanted: released to Windows and the game while paused or gaming.</summary>
    private void UpdatePeekHotkey()
    {
        // Released while Settings records a new one, so pressing the current combination is recorded, not Peek (M6b review).
        if ((Current.PeekHotkeyWanted && !_recordingHotkey) == _peekHotkey is not null) return;
        if (_peekHotkey is not null)
        {
            _peekHotkey.Dispose();
            _peekHotkey = null;
            return;
        }
        _peekHotkey = TryRegisterPeekHotkey(_config.Settings.PeekHotkey, out var problem);
        _peekHotkeyProblem = _peekHotkey is null ? problem : null;
        if (_peekHotkey is null) Log.Warning("Peek is off: {Problem}", problem);
    }

    private GlobalHotkey? TryRegisterPeekHotkey(string text, out string problem)
    {
        if (!Hotkey.TryParse(text, out var hotkey) || !Enum.TryParse<System.Windows.Input.Key>(hotkey.Key, ignoreCase: true, out var key))
        {
            problem = $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.";
            return null;
        }
        var virtualKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            problem = $"{hotkey.DisplayText} has no key Windows can watch for. Pick another key."; // would say "Saved" and never fire (M6b review M4)
            return null;
        }
        var registration = new GlobalHotkey(_messages.Handle, PeekHotkeyId);
        if (registration.TryRegister(hotkey, (uint)virtualKey))
        {
            problem = "";
            Log.Information("Peek hotkey {Hotkey} registered", hotkey);
            return registration;
        }
        registration.Dispose();
        problem = $"{hotkey.DisplayText} is already taken by Windows or another app. Pick another combination.";
        return null;
    }

    /// <summary>Settings: a new Peek hotkey. It is kept only if Windows accepts it; otherwise the old one stays.</summary>
    private (bool Saved, string Message) SetPeekHotkey(string text)
    {
        if (!Hotkey.TryParse(text, out var parsed)) return (false, $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.");
        // A global hotkey swallows its keys in every app (M6b review I1): only safe combinations are recorded.
        if (!parsed.IsSafeToRecord) return (false, $"{parsed.DisplayText} would stop working everywhere else (typing, Tab, closing windows). Use Alt or Win with a key, or an F-key.");
        var normalized = parsed.ToString();
        // The same combination again is a retry when it is not registered (Settings showed it as not active, M6b review).
        if (normalized == _config.Settings.PeekHotkey && _peekHotkeyProblem is null) return (true, $"Peek: {parsed.DisplayText}");
        _peekHotkey?.Dispose();
        _peekHotkey = null;
        var registration = TryRegisterPeekHotkey(normalized, out var problem);
        if (registration is null)
        {
            UpdatePeekHotkey(); // the old one again
            return (false, problem);
        }
        registration.Dispose(); // proven free; UpdatePeekHotkey registers it whenever it is wanted
        _peekHotkeyProblem = null;
        _config = _config with { Settings = _config.Settings with { PeekHotkey = normalized } };
        UpdatePeekHotkey();
        SaveNow();
        return (true, $"Saved. Peek: {parsed.DisplayText}");
    }

    /// <summary>Tray or fence menu → Settings… (M6b). One window; a second request brings it to the front.</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var window = new SettingsWindow();
        window.StartWithWindowsChanged += SetStartWithWindows;
        window.TakeoverChanged += SetTakeover;
        window.PeekHotkeyChosen += text =>
        {
            var (saved, message) = SetPeekHotkey(text);
            RefreshSettings();
            window.ShowHotkeyResult(saved, message);
        };
        window.RollupExpandChanged += SetRollupExpand;
        window.HotkeyRecording += recording =>
        {
            _recordingHotkey = recording;
            UpdatePeekHotkey();
            // Leaving the box re-registers: Settings must show if that failed (final review I5).
            if (!recording) RefreshSettings();
        };
        window.DefaultLabelsChanged += labels =>
        {
            _config = _config with { Settings = _config.Settings with { DefaultLabels = labels } };
            SaveNow();
        };
        window.LabelsAppliedToAll += labels =>
        {
            _config = FenceEdits.SetLabelsEverywhere(_config, labels);
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetLabelMode(labels);
            Log.Information("labels for every fence: {Labels}", labels);
            SaveNow();
            RefreshSettings();
        };
        window.ShortcutArrowsChanged += show =>
        {
            _config = _config with { Settings = _config.Settings with { ShowShortcutArrows = show } };
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetShortcutArrows(show);
            SaveNow();
        };
        window.GameModeChanged += SetGameModeEnabled;
        window.OpenLogsRequested += () => OpenItem(AppPaths.LogsDirectory, ownerHandle: 0);
        window.OpenDataRequested += () => OpenItem(AppPaths.DataDirectory, ownerHandle: 0);
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _recordingHotkey = false;
            UpdatePeekHotkey();
        };
        _settingsWindow = window;
        RefreshSettings();
        window.Show();
        window.Activate();
    }

    private void RefreshSettings() => _settingsWindow?.Show(new SettingsView(
        StartWithWindows: _config.Settings.StartWithWindows,
        Takeover: _takeoverActive,
        PeekHotkey: PeekHotkeyDisplay,
        PeekHotkeyActive: _peekHotkeyProblem is null,
        RollupExpand: _config.Settings.RollupExpand,
        GameModeEnabled: _config.Settings.GameMode,
        GameModeActive: _gameMode,
        Version: typeof(FenceHost).Assembly.GetName().Version?.ToString(3) ?? "",
        DataFolder: AppPaths.DataDirectory,
        DefaultLabels: _config.Settings.DefaultLabels,
        ShowShortcutArrows: _config.Settings.ShowShortcutArrows));

    /// <summary>The Peek hotkey as a person reads it ("Ctrl+Shift+=", not "Ctrl+Shift+OemPlus").</summary>
    private string PeekHotkeyDisplay =>
        Hotkey.TryParse(_config.Settings.PeekHotkey, out var hotkey) ? hotkey.DisplayText : _config.Settings.PeekHotkey;

    /// <summary>Fence menu → Labels (M8b).</summary>
    private void SetFenceLabels(FenceWindow window, LabelMode labels)
    {
        _config = FenceEdits.SetLabels(_config, window.FenceId, labels);
        window.SetLabelMode(labels);
        ScheduleSave();
    }

    private void SetRollupExpand(RollupExpand mode)
    {
        _config = _config with { Settings = _config.Settings with { RollupExpand = mode } };
        foreach (var window in _windows.Values) window.SetRollupExpand(mode);
        Log.Information("roll-up expands on: {Mode}", mode);
        SaveNow();
    }

    private void SetGameModeEnabled(bool enabled)
    {
        _config = _config with { Settings = _config.Settings with { GameMode = enabled } };
        Log.Information("game mode enabled: {Enabled}", enabled);
        SaveNow();
        CheckGameMode(); // turning it off in a game ends idle at once
        RefreshSettings();
    }

    /// <summary>The hook exists exactly while it is wanted: not while paused or gaming (hard rule 3, spec §4.7).</summary>
    private void UpdateMouseHook()
    {
        if (Current.MouseHookWanted == _mouseHook is not null) return;
        if (_mouseHook is not null)
        {
            EndDrawOverlay();
            _mouseHook.Dispose();
            _mouseHook = null;
            return;
        }
        var dispatcher = Dispatcher.CurrentDispatcher;
        _mouseHook = new DesktopMouseHook(
            onGesture: (gesture, screenX, screenY) => dispatcher.BeginInvoke(() => OnDesktopGesture(gesture, screenX, screenY)),
            onPeekClickOutside: () => dispatcher.BeginInvoke(() => SetPeek(false)),
            log: message => Log.Information("{Message}", message));
    }

    /// <summary>Windows drops a low-level hook silently (LowLevelHooksTimeout): a fresh one after Explorer restarts and on unlock.</summary>
    private void ReinstallMouseHook()
    {
        if (_mouseHook is null) return; // not wanted now; it comes back when it is
        EndDrawOverlay();
        _mouseHook.Dispose();
        _mouseHook = null;
        UpdateMouseHook();
    }

    private void OnSessionUnlocked()
    {
        Log.Information("session unlocked; re-installing the mouse hook");
        ReinstallMouseHook();
        CheckGameMode();
    }

    /// <summary>Game mode (spec §4.7, ADR-021): checked on every foreground change, and again shortly after (the signal lags).</summary>
    private void StartGameMode()
    {
        // Backstop for a game that goes full screen after the last re-check (M6a review I1).
        var poll = new DispatcherTimer { Interval = GameModePolicy.PollInterval };
        poll.Tick += (_, _) => CheckGameMode();
        poll.Start();
        _foregroundWatcher = new ForegroundWatcher(onForegroundChanged: OnForegroundChanged,
            logFailure: failure => Log.Error(failure, "game mode check failed"));
        if (!_foregroundWatcher.IsWatching) Log.Warning("game mode: foreground changes cannot be watched; game mode is off");
        CheckGameMode();
    }

    private void OnForegroundChanged()
    {
        CheckGameMode();
        foreach (var delay in GameModePolicy.RecheckDelays)
        {
            // ponytail: one short-lived timer per foreground change and delay; a coalescing timer if switching storms ever show up in a profile.
            var recheck = new DispatcherTimer { Interval = delay };
            recheck.Tick += (_, _) =>
            {
                recheck.Stop();
                CheckGameMode();
            };
            recheck.Start();
        }
    }

    private void CheckGameMode()
    {
        var gameMode = GameModePolicy.IsGameActive(enabled: _config.Settings.GameMode, foreground: GameDetection.TakeSnapshot());
        if (gameMode == _gameMode) return;
        _gameMode = gameMode;
        Log.Information("game mode: {GameMode}", gameMode);
        if (gameMode) SetPeek(false);
        UpdateMouseHook();
        foreach (var portal in _portals.Values) portal.SetPaused(gameMode);
        if (!gameMode) ApplyDeferredShellWork();
        UpdatePeekHotkey();
        _trayIcon?.SetTooltip(TrayTooltip());
        RefreshSettings();
    }

    /// <summary>The game was left: Desktop changes made meanwhile apply in order (or one reconcile if events were lost).</summary>
    private void ApplyDeferredShellWork()
    {
        if (_reconcileDeferred)
        {
            _reconcileDeferred = false;
            _deferredDesktopChanges.Clear();
            ReconcileDesktop();
            return;
        }
        if (_deferredDesktopChanges.Count == 0) return;
        Log.Information("applying {Count} desktop change(s) from game mode", _deferredDesktopChanges.Count);
        foreach (var change in _deferredDesktopChanges)
            (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
        _deferredDesktopChanges.Clear();
        RefreshWindows();
        ScheduleSave();
    }

    /// <summary>Pause (tray): the desktop goes back to Windows — fences hidden, icons shown, the hook gone — until resumed.</summary>
    private void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        if (paused)
        {
            SetPeek(false);
            EndDrawOverlay();
        }
        _paused = paused;
        if (paused)
        {
            _quickHidden = false; // resuming shows everything
            _iconsHiddenByUser = false;
        }
        foreach (var window in _windows.Values)
        {
            if (!Current.FencesVisible) window.HideNow();
            else if (!window.IsVisible)
            {
                window.ShowNow();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        EnsureIconState(); // also shows icons left hidden by an earlier failed show: Pause "restores icons" (spec §6, M6a review M1)
        UpdateMouseHook();
        UpdatePeekHotkey();
        RefreshSettings(); // a hotkey that cannot be registered on resume shows in an open Settings (final review I5)
        _trayIcon?.SetTooltip(TrayTooltip());
        Log.Information("paused: {Paused}", paused);
    }

    private string TrayTooltip() =>
        _paused ? "NeoFences — paused" : _gameMode ? "NeoFences — idle while a game runs" : "NeoFences";

    /// <summary>Tray menu (spec §6; user choice 2026-10-03: left-click opens it too).</summary>
    private void ShowTrayMenu(int screenX, int screenY)
    {
        var chosen = TrayMenu.Show(_messages.Handle,
        [
            new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
            new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
            new TrayMenuItem(TrayPeek, $"Peek\t{PeekHotkeyDisplay}", Checked: _peeking, Enabled: !_paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TraySettings, "Settings…"),
            new TrayMenuItem(TrayPause, "Pause NeoFences", Checked: _paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayExit, "Exit NeoFences"),
        ], screenX, screenY);
        switch (chosen)
        {
            case TrayNewFence:
                SetQuickHidden(false); // a new fence must be visible (M6a review I3)
                CreateFence();
                break;
            case TrayQuickHide: SetQuickHidden(!_quickHidden); break;
            case TrayPeek: SetPeek(!_peeking); break;
            case TrayPause: SetPaused(!_paused); break;
            case TraySettings: OpenSettings(); break;
            case TrayExit: ExitRequested?.Invoke(); break;
        }
    }

    private void OnDesktopGesture(DesktopGesture gesture, int screenX, int screenY)
    {
        switch (gesture)
        {
            // A double-click on a visible native icon opens it; only empty desktop toggles quick-hide.
            case DesktopGesture.DoubleClick when Current.IconsHidden
                || !DesktopWindows.IsOverDesktopIcon(screenX, screenY, log: message => Log.Warning("{Message}", message)):
                SetQuickHidden(!_quickHidden);
                break;
            case DesktopGesture.RightDragStarted:
                BeginDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCompleted:
                EndDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCancelled:
                EndDrawOverlay(); // the drag's right-up was never seen (M5 review I2)
                break;
        }
    }

    /// <summary>Quick-hide (M5, user choice): fences and the native desktop icons go away together and come back together.</summary>
    private void SetQuickHidden(bool hidden)
    {
        if (hidden == _quickHidden) return;
        if (hidden) SetPeek(false);
        // Icons the user had hidden through Explorer stay theirs: quick-hide neither hides nor later shows them (M8a).
        // Hidden now without the takeover-active marker: the user hid them in Explorer, so they stay the user's. With the marker
        // set, NeoFences hid them (an earlier show failed) and the end of this quick-hide retries the show (M8a review I1).
        if (hidden && !_takeoverActive) _iconsHiddenByUser = DesktopIcons.TryIsHidden() == true && !_watchdog.IsTakeoverActiveMarked;
        var iconsWereHidden = Current.IconsHidden;
        _quickHidden = hidden;
        foreach (var window in _windows.Values)
        {
            if (hidden) window.HideFaded(); // 150 ms fade (spec §6)
            else
            {
                window.ShowFaded();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        if (Current.IconsHidden != iconsWereHidden) SetIconsHidden(Current.IconsHidden); // RunState decides, user-hidden icons included
        if (!hidden) _iconsHiddenByUser = false;
        Log.Information("quick-hide: {Hidden}", hidden);
    }

    private void BeginDrawFence(int startX, int startY)
    {
        if (_mouseHook is not { } mouseHook) return;
        EndDrawOverlay();
        _drawStart = (startX, startY);
        var overlay = new DrawFenceOverlay(_lightTheme);
        overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        overlay.Show();
        _drawOverlay = overlay;
        _drawTimer = new DispatcherTimer { Interval = DrawFrame };
        // A lost right-up (Win+L, UAC, an elevated window) is ended by the next click (RightDragCancelled). The button
        // state cannot tell: the swallowed right-press never reaches Windows' key state (M5 review I2, smoke finding).
        _drawTimer.Tick += (_, _) => overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        _drawTimer.Start();
    }

    /// <summary>The right button came up: a new fence where the rectangle was, its title ready to type.</summary>
    private void EndDrawFence(int endX, int endY)
    {
        if (!EndDrawOverlay()) return; // the start was never seen
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        SetQuickHidden(false);
        var pixels = DrawFenceOverlay.Between(_drawStart, (endX, endY));
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        (_config, var fence) = FenceMembership.CreateFence(_config, "New fence");
        // Too small a drag still makes a usable fence: the layout clamps it to the minimum size.
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: fence.Id, rect: FencePlacement.FromPixels(pixels, monitor));
        Log.Information("fence drawn on the desktop at {Pixels}", pixels);
        OpenWindow(fence);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
        if (_windows.TryGetValue(fence.Id, out var window)) window.BeginRename();
    }

    /// <returns>True when an overlay was showing.</returns>
    private bool EndDrawOverlay()
    {
        _drawTimer?.Stop();
        _drawTimer = null;
        if (_drawOverlay is null) return false;
        _drawOverlay.Close();
        _drawOverlay = null;
        return true;
    }

    private void OnHotkey(int hotkeyId)
    {
        CheckGameMode(); // fresh: a game may have gone full screen since the last check (M6a review I1)
        // Paused: the desktop belongs to Windows. Gaming: fences must not rise over the game, and the click-outside hook is off.
        if (_paused || _gameMode) return;
        if (hotkeyId == PeekHotkeyId) SetPeek(!_peeking);
        else if (hotkeyId == PeekEscapeHotkeyId) SetPeek(false);
    }

    /// <summary>Peek (M5): every fence above all windows until the hotkey again, Esc, a click outside, or an item opens.</summary>
    private void SetPeek(bool peeking)
    {
        if (peeking == _peeking) return;
        if (peeking) SetQuickHidden(false);
        _peeking = peeking;
        if (_mouseHook is not null) _mouseHook.PeekActive = peeking;
        // Every fence first: raising one restacks its siblings (all owned by Progman), and a sibling still keeping
        // itself at the bottom would drop back (M5 smoke: only one fence rose).
        foreach (var window in _windows.Values) window.Peeking = peeking;
        foreach (var window in _windows.Values) FenceWindowChrome.SetTopmost(window.Handle, peeking);
        _peekEscapeHotkey?.Dispose();
        _peekEscapeHotkey = null;
        if (peeking)
        {
            _peekEscapeHotkey = new GlobalHotkey(_messages.Handle, PeekEscapeHotkeyId);
            if (!_peekEscapeHotkey.TryRegister(new Hotkey(Ctrl: false, Alt: false, Shift: false, Win: false, Key: "Escape"), virtualKey: 0x1B))
                Log.Warning("Esc is taken by another app; Peek ends with its hotkey or a click outside");
        }
        Log.Information("peek: {Peeking}", peeking);
    }

    /// <summary>Title double-click (M5): rolled up to its title bar, or back. Stored, so it survives a restart.</summary>
    private void ToggleRollUp(FenceWindow window)
    {
        var rolledUp = !_config.Fences.First(fence => fence.Id == window.FenceId).RolledUp;
        _config = FenceEdits.SetRolledUp(_config, window.FenceId, rolledUp);
        window.SetRolledUp(rolledUp);
        ScheduleSave();
    }

    /// <summary>Adds the tray icon, retrying while Explorer is still busy (sign-in autostart, Explorer restart).</summary>
    private void ShowTrayIcon()
    {
        if (_trayIcon is not { } trayIcon || trayIcon.Show()) return;
        var attempts = 0;
        var retry = new DispatcherTimer { Interval = TrayRetryInterval };
        retry.Tick += (_, _) =>
        {
            if (trayIcon.Show() || ++attempts >= TrayRetryAttempts)
            {
                retry.Stop();
                Log.Information("tray icon retry finished after {Attempts} attempt(s)", attempts + 1);
            }
        };
        retry.Start();
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
            else if (_store.LastBackupFailure is { } backupFailure) Log.Warning(backupFailure, "config saved, but the daily backups could not be written or pruned");
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
Expected: 0 warnings, 0 errors; `Passed: 281`.

- [ ] **Step 3: Live checks (ask first; print TEST RUNNING / TEST COMPLETE; restore the installed copy)**

Run with Windows PowerShell (the scripts live in the session scratchpad and dot-source `m8b-helpers.ps1`):
- `m8c-smoke.ps1 -Exe <branch exe>`: T1–T4, T6, Recycle Bin notice, burst;
- `m8b-smoke.ps1 -Exe <branch exe>`: M8b regressions (the double-click code changed).

Then start the branch exe while the installed copy runs. Expected: it exits after about 5 s and logs "NeoFences is already running".

Expected: every line as in `docs/research/m8c-shell-dragdrop.md`.

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.App
git commit -m "feat: ran open, recycle and rename on a shell worker, renamed the right-clicked item, refreshed special icons live, coalesced watcher recovery and waited for an exiting instance"
```

---

### Task 4: Verification and docs

- [ ] **Step 1: Append section T to `docs/TEST-CHECKLIST.md`**

```markdown

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
```

- [ ] **Step 2: Append ADR-026 to `docs/DECISIONS.md`**

```markdown

## ADR-026 — Shell actions and drag-drop details (M8c): shell worker, special-icon notices, drop rows, start after Setup
**Date:** 2026-10-03 · **Status:** Accepted · **Refines:** ADR-016 (item actions), ADR-017 (drag-drop), ADR-023 (installer), ADR-024

**Context.** M8c is the third batch of carry-overs: the item menu, recycling and opening (M3a review), drag-drop
details (M3b review), special desktop icons (M2b), the M8a review's reconcile/watcher/monitor minors and the M8b
review's minors. During the v1.1.0 upgrade (2026-10-03), Setup closed NeoFences but did not start 1.1.0 again.

**Decision.**
- **One STA shell worker** (`ShellWorker`) runs open, recycle and rename in order, off the UI thread: a long recycle
  or a conflict dialog no longer freezes the fences, and shell handlers get the apartment they expect. Open used to run
  on the thread pool (MTA). The "no Recycle Bin on this drive" message is shown back on the UI thread.
- **Item menu.**
  - The right-clicked item comes first, so menu → Rename renames *it* when several are selected (user choice
    2026-10-03: the right-clicked item only).
  - Shift+F10 opens the normal menu; only Shift + right-click asks for extended verbs.
  - A failed `QueryContextMenu` HRESULT (a broken extension) is logged instead of showing an empty menu.
  - `WM_NULL` is posted after `TrackPopupMenuEx` (the documented dance), so an outside click closes the menu.
  - Note: on this PC the menu also lists PowerToys' "Rename with PowerRename"; NeoFences takes only the shell's own
    `rename` and `delete` verbs (checked live: their verbs are still `rename` / `delete`).
- **Recycling several items** skips items that no longer exist instead of cancelling all; they are logged.
- **Same name on the user's and the Public Desktop.** The desktop folder parses a name to the user's copy, so a Public
  Desktop item whose name the user's Desktop also has is reached through its own folder (menu, drag, drop target).
- **Special icons live** (`SpecialIconNotifications`):
  - "Desktop icon settings" changes are watched on the registry key (`RegNotifyChangeKeyValue`, thread agnostic); a
    changed set of special icons reconciles.
  - The Recycle Bin's own changes (all events on its folder, shell and interrupt level) and icon-image updates reload
    the special icons, so its full/empty icon follows. Bursts are debounced (400 ms).
- **Drag-drop.**
  - A virtual source (zip contents, phones) is not asked for CF_HDROP on enter: that made Windows extract every file
    before the drop. Its arriving names come from the file descriptors.
  - Desktop items in a mixed drag join the fence; the rest arrive through Windows.
  - Drops that make shortcuts expect "name - Shortcut.lnk" too (English Windows only; other languages go to the Inbox).
  - Container checks are cached per drag; drop rows reach down to their tallest item (`DropZones.InsertIndex`, tested).
- **Watcher and reconcile.**
  - A stopped watcher (re-arm) is told apart from one unreadable event (reconcile only). Bursts become one recovery;
    a watcher that fails again within 10 s of a re-arm waits longer each time (up to a minute).
  - Reconcile reports the memories it used (`ReconcileReport.UsedMemories`); the host drops them, and several
    memories for one fence are inserted by index (tested).
- **Monitors.** Duplicate device ids are numbered in GDI-name order, not enumeration order (tested).
- **Start after Setup.** A new instance that finds the single-instance mutex taken waits up to 5 s for the old one to
  exit (Setup closing the old version while starting the new), and logs when it gives up. Logging is shared between
  processes for that line.
- **M8b review minors.** NeoFences' own title double-click uses the click's message time and point, and a DBLCLK right
  after a recognised double-click (a third click) does not toggle again; invalid `LabelMode` values in a hand-edited
  config are repaired; every Settings description is its control's help text and row titles wrap; the rename box in an
  icons-only fence gets a labelled cell's width; dead code removed. Icons are first requested once the window is
  placed (one load at the right DPI).

**Consequences.**
- Whether Velopack's Setup itself starts the app after a silent upgrade is still to be confirmed at the next upgrade
  (the log now says if a second instance gave up). The same-version Setup only offers Repair.
- Key caps still use US key names (M8b review minor, deferred).
```

- [ ] **Step 3: Write `docs/research/m8c-shell-dragdrop.md`**

```markdown
# M8c — shell actions and drag-drop details: results

**Date:** 2026-10-03 · **Machine:** Windows 11 Pro 25H2 (26200), 1920×1080 at 100 %, Takeover on, NeoFences 1.1.0 installed, PowerToys installed.
**Build:** M8c prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section T · **Decision:** ADR-026.

## Live smoke on the prototype (installed copy restored after each run)

| Check | Result |
|---|---|
| T1 menu → Rename with two selected | **Pass**: the box opened on the right-clicked item (`nf-m8c-b.txt`). |
| T2 Shift + right-click / Shift+F10 | Menus open; for a .txt file the extended menu adds no entries here, so the two cannot be told apart by count. |
| T3 outside click closes the menu | **Pass**. |
| T4 Del recycles on the shell worker | **Pass**: the file went to the Recycle Bin; the UI kept responding. |
| T6 Desktop icon settings → Control Panel on, then back | **Pass**: fenced within ~2 s, removed again after the setting was restored. |
| Recycle Bin notice after a recycle | **Pass** once registered for all events on the bin (first attempts with update events only, at shell level, saw nothing). |
| Watcher burst (3000 files created and deleted) | No overflow happened (no recovery needed); every file came and went. Coalescing is untested live. |
| M8b regressions (Settings, first title double-click, Peek, quick-hide) | **Pass** on the M8c build. |
| Second instance while one runs | **Pass**: waited ~5 s, logged "already running", exited. |
| Log | 0 errors or warnings. |

## Findings

1. **Not a bug: "Rename with PowerRename".** The first smoke clicked the first menu entry starting with "Rena", which on
   this PC is PowerToys' PowerRename (verb `{1861E28B-…}`), and opened PowerRename windows (closed again). The shell's
   own Rename (`Rena&me`) still has the verb `rename`, Delete `delete`: NeoFences' handling is right.
2. **"Copy as path" is in the normal Windows 11 menu**, so it is no longer a marker for the extended menu.
3. **The same-version Setup only offers Repair** ("NeoFences is already installed"), so a Setup-over-install of the same
   version cannot show the upgrade behaviour. The next real upgrade will.
4. **The Recycle Bin's changes arrive as file-system events of its folders**: the registration needs interrupt level
   and all events on the bin; update events at shell level alone never came.
```
Add a row for each Task 3 live check that differs from the prototype run.

- [ ] **Step 4: Update the other docs**
  - `ROADMAP.md`: `[x]` with " — M8c (ADR-026)":
    - live special icons;
    - icons after layout (mixed DPI);
    - the M3a review lines (same name user/Public, multi-item recycle, menu dismissal, menu Rename with several (right-clicked item, user choice), QueryContextMenu log + Shift+F10, open/recycle off the UI thread);
    - the M3b review lines (drop rows, lazy CF_HDROP + container cache, mixed drags + arrivals, Public/user same name);
    - the M8a review lines (reconcile memories, Overflowed coalescing, UniqueDeviceIds order);
    - the Setup restart line ("a new instance waits for the exiting one; confirm at the next upgrade, T13");
    - the M8b review lines except key caps (double-click, LabelMode validation, Settings help/wrap, rename width, dead code).
    - Add `- [x] M8c — done <date> (ADR-026, research/m8c-shell-dragdrop.md)` and `- [ ] T7, T8, T9, T10, T12, T13 — user checks`.
  - `ARCHITECTURE.md`: add the shell worker to the Threads table; add an "M8c complete" line.
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 5: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 6: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-026, M8c checks and results, and ticked the M8c carry-overs"
```
