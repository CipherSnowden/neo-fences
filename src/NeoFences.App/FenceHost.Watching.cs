using System.Windows.Threading;
using NeoFences.Core.Items;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Target watching (M18 spec §4): the folders holding targets are watched (at most <see cref="WatchPlan.MaxFolders"/>,
/// the busiest first); a rename in place is followed, a deletion shows the item Missing, a drive that is not there shows
/// it Unavailable. Each fence refreshes at most once every 2 s; every target is checked again every 5 minutes, when its
/// fence becomes visible, on Refresh and when a drive comes or goes. Checks run off the UI thread; games defer them.
/// </summary>
public sealed partial class FenceHost
{
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FolderChangeQuiet = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DriveChangeQuiet = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RenameQuiet = TimeSpan.FromMilliseconds(500);
    private const int MaxPendingRenames = 500;

    private readonly Dictionary<string, TargetCheck> _targetChecks = new(ItemKinds.Comparer);
    private readonly Dictionary<string, (FolderWatcher Watcher, DeviceRemovalNotice? Notice)> _targetWatchers = new(ItemKinds.Comparer);
    private HashSet<string> _wantedFolders = new(ItemKinds.Comparer);
    private readonly HashSet<string> _armingFolders = new(ItemKinds.Comparer); // watchers being opened off the UI thread
    private readonly RefreshThrottle _refreshThrottle = new();
    private readonly Dictionary<string, DispatcherTimer> _fenceRefreshTimers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _changedFolders = new(ItemKinds.Comparer);
    private readonly List<(string OldPath, string NewPath)> _seenRenames = []; // settled once quiet, or after a game (final review C1)
    private DispatcherTimer? _folderChangeTimer, _recheckTimer, _drivesTimer, _renameTimer;
    private bool _checksDeferred, _watchingStopped;
    private int _renamesSettling; // settles still running off the UI thread: folder-change checks wait for them
    private int _checkBatch; // numbers each batch of checks: an older batch finishing late never overwrites a newer answer (final review I6)
    private readonly Dictionary<string, int> _checkBatchOf = new(ItemKinds.Comparer);

    /// <summary>The last check of a target; Ok while it was never checked (fences show at once, states fill in).</summary>
    private TargetCheck CheckOf(string target) => _targetChecks.TryGetValue(target, out var check) ? check : TargetCheck.Ok;

    private TargetState StateOf(string target) => CheckOf(target).State;

    private void StartWatching()
    {
        _recheckTimer = new DispatcherTimer { Interval = RecheckInterval };
        _recheckTimer.Tick += (_, _) => CheckAllTargets(); // also re-arms watchers that stopped or were let go for a removal
        _recheckTimer.Start();
        CheckAllTargets();
    }

    private void StopWatching()
    {
        _watchingStopped = true;
        _recheckTimer?.Stop();
        _folderChangeTimer?.Stop();
        _drivesTimer?.Stop();
        _renameTimer?.Stop();
        foreach (var timer in _fenceRefreshTimers.Values) timer.Stop();
        foreach (var (watcher, notice) in _targetWatchers.Values)
        {
            watcher.Dispose();
            notice?.Dispose();
        }
        _targetWatchers.Clear();
    }

    /// <summary>Watches the folders the plan picks now; watchers no longer wanted, not watching or stopped (final review I3) are replaced.</summary>
    private void UpdateWatching()
    {
        if (_watchingStopped) return;
        _wantedFolders = WatchPlan.Folders(ItemEdits.PathTargets(_items)).ToHashSet(ItemKinds.Comparer);
        foreach (var (folder, (watcher, notice)) in _targetWatchers.Where(entry => !_wantedFolders.Contains(entry.Key) || !entry.Value.Watcher.IsWatching || entry.Value.Watcher.HasFailed).ToList())
        {
            watcher.Dispose();
            notice?.Dispose();
            _targetWatchers.Remove(folder);
        }
        var toArm = _wantedFolders.Where(folder => !_targetWatchers.ContainsKey(folder) && _armingFolders.Add(folder)).ToList();
        if (toArm.Count == 0) return;
        var noticeOwner = _messages.Handle;
        var dispatcher = Dispatcher.CurrentDispatcher;
        // Off the UI thread: opening a watcher on a sleeping disk or a share can take seconds (final review I2).
        Task.Run(() => toArm.Select(folder => (Folder: folder, Watch: Watch(folder, noticeOwner))).ToList()).ContinueWith(armed =>
        {
            foreach (var (folder, watch) in armed.Result)
            {
                _armingFolders.Remove(folder);
                if (_watchingStopped || !_wantedFolders.Contains(folder) || _targetWatchers.ContainsKey(folder))
                {
                    watch.Watcher.Dispose();
                    watch.Notice?.Dispose();
                    continue;
                }
                watch.Watcher.Changed += () => dispatcher.BeginInvoke(() => OnTargetFolderChanged(folder));
                watch.Watcher.Renamed += (oldPath, newPath) => dispatcher.BeginInvoke(() => OnTargetRenamed(oldPath, newPath));
                // Events were lost or it stopped: its targets are checked now; the next UpdateWatching (an edit, a drive, the 5-minute check) re-arms it.
                watch.Watcher.Failed += () => dispatcher.BeginInvoke(() => OnTargetFolderChanged(folder));
                _targetWatchers[folder] = watch;
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>A watcher, with a removal notice where Windows offers one, so "Safely remove" still works (M8d). Off the UI thread.</summary>
    private static (FolderWatcher Watcher, DeviceRemovalNotice? Notice) Watch(string folder, nint noticeOwner)
    {
        // Names only: a game writing logs and caches next to its .exe must not wake NeoFences on every write (final review I4).
        var watcher = new FolderWatcher(folder, failure => Log.Debug(failure, "cannot watch {Folder}; its items are checked every few minutes", folder), namesOnly: true);
        var notice = watcher.HeldFolder is { } held
            ? DeviceRemovalNotice.TryRegister(noticeOwner, held, failure => Log.Debug(failure, "no removal notice for {Folder}", held))
            : null;
        return (watcher, notice);
    }

    /// <summary>Windows asks to remove a drive holding a watched folder (the test stick, G:): let go of it now.</summary>
    private bool ReleaseTargetWatcherForRemoval(nint handle)
    {
        if (_targetWatchers.FirstOrDefault(entry => entry.Value.Notice?.Handle == handle) is not { Key: { } folder, Value: var (watcher, notice) }) return false;
        watcher.Dispose();
        notice?.Dispose();
        _targetWatchers.Remove(folder);
        return true; // re-armed when the drive is back (DrivesChanged) or by the 5-minute check
    }

    /// <summary>Something in a watched folder changed: its targets are checked 300 ms later (a busy folder never delays the others).</summary>
    private void OnTargetFolderChanged(string folder)
    {
        if (_watchingStopped) return;
        if (Current.ShellWorkDeferred)
        {
            _checksDeferred = true; // one check of everything after the game (final review I4)
            return;
        }
        _changedFolders.Add(folder);
        if (_folderChangeTimer is null)
        {
            _folderChangeTimer = new DispatcherTimer { Interval = FolderChangeQuiet };
            _folderChangeTimer.Tick += (_, _) =>
            {
                // A rename still settling: check after it, or the renamed target flashes Missing first (M18 live check AD14).
                if (_seenRenames.Count > 0 || _renameTimer?.IsEnabled == true || _renamesSettling > 0) return;
                _folderChangeTimer.Stop();
                var targets = ItemEdits.PathTargets(_items).Where(target => WatchPlan.ParentOf(target) is { } parent && _changedFolders.Contains(parent)).ToList();
                _changedFolders.Clear();
                CheckTargets(targets);
            };
        }
        if (!_folderChangeTimer.IsEnabled) _folderChangeTimer.Start(); // not restarted by every event (final review I4)
    }

    /// <summary>
    /// A file or folder was renamed in a watched folder. Items follow only once the renames are quiet and settled
    /// (<see cref="Renames.Settle"/>): an editor's save renames the original away and back (final review C1).
    /// </summary>
    private void OnTargetRenamed(string oldPath, string newPath)
    {
        if (_watchingStopped) return;
        if (_seenRenames.Count < MaxPendingRenames) _seenRenames.Add((oldPath, newPath));
        else _checksDeferred = true; // a flood: the next full check shows what is missing
        if (Current.ShellWorkDeferred) return; // settled after the game
        if (_renameTimer is null)
        {
            _renameTimer = new DispatcherTimer { Interval = RenameQuiet };
            _renameTimer.Tick += (_, _) =>
            {
                _renameTimer.Stop();
                SettleRenames();
            };
        }
        _renameTimer.Stop();
        _renameTimer.Start();
    }

    /// <summary>The renames seen so far: settled off the UI thread (it asks the disk), then followed by every item.</summary>
    private void SettleRenames()
    {
        if (_seenRenames.Count == 0) return;
        var seen = _seenRenames.ToList();
        _seenRenames.Clear();
        _renamesSettling++;
        Task.Run(() => Renames.Settle(seen, TargetProbe.Exists)).ContinueWith(settled =>
        {
            _renamesSettling--;
            if (_watchingStopped || settled.IsFaulted) return;
            foreach (var (oldPath, newPath) in settled.Result) FollowRename(oldPath, newPath);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void FollowRename(string oldPath, string newPath)
    {
        var followed = ItemEdits.Retarget(_items, oldPath, newPath);
        if (ReferenceEquals(followed, _items)) return; // not a target
        _items = followed;
        Log.Information("target renamed: {OldPath} -> {NewPath}; its items follow", oldPath, newPath);
        ItemsChanged(checkTargets: [newPath]);
    }

    /// <summary>A drive came or went: every target is checked again (and watchers re-armed) after a quiet second.</summary>
    private void OnDrivesChanged()
    {
        if (_watchingStopped) return;
        if (_drivesTimer is null)
        {
            _drivesTimer = new DispatcherTimer { Interval = DriveChangeQuiet };
            _drivesTimer.Tick += (_, _) =>
            {
                _drivesTimer.Stop();
                CheckAllTargets();
            };
        }
        _drivesTimer.Stop();
        _drivesTimer.Start();
    }

    /// <summary>Fence menu → Refresh: its targets checked now, its names and icons loaded again (the library rescans, M12).</summary>
    private void RefreshFence(FenceWindow window)
    {
        if (window.IsLibrary)
        {
            ScanLibrary(full: true);
            return;
        }
        CheckFence(window.FenceId);
        window.ReloadIcons();
    }

    private void CheckFence(string fenceId) =>
        CheckTargets([.. _items.Of(fenceId).Where(item => item.Kind == ItemKind.Path).Select(item => item.Target).Distinct(ItemKinds.Comparer)]);

    private void CheckAllTargets()
    {
        UpdateWatching();
        CheckTargets(ItemEdits.PathTargets(_items));
    }

    /// <summary>Checks these targets off the UI thread (a share answers within 2 s or counts as Unavailable); games defer it.</summary>
    private void CheckTargets(IReadOnlyList<string> targets)
    {
        if (_watchingStopped || targets.Count == 0) return;
        if (Current.ShellWorkDeferred)
        {
            _checksDeferred = true; // games get every bit of the machine: all of them after the game (spec §4.7)
            return;
        }
        var batch = ++_checkBatch;
        Task.Run(() => TargetProbe.CheckAll(targets))
            .ContinueWith(checks => ApplyChecks(checks.Result, batch), TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>New states: the fences holding a target whose state changed refresh (at most once every 2 s each).</summary>
    private void ApplyChecks(IReadOnlyList<(string Target, TargetCheck Check)> checks, int batch = int.MaxValue)
    {
        if (_watchingStopped) return;
        var changed = new HashSet<string>(ItemKinds.Comparer);
        foreach (var (target, check) in checks)
        {
            if (_checkBatchOf.TryGetValue(target, out var newest) && newest > batch) continue; // a newer answer is already there
            _checkBatchOf[target] = Math.Min(batch, _checkBatch);
            if (_targetChecks.TryGetValue(target, out var before) && before == check) continue;
            if (check.State != TargetState.Ok || before is { State: not TargetState.Ok })
                Log.Information("target {Target}: {State}", target, check.State);
            _targetChecks[target] = check;
            changed.Add(target);
        }
        if (changed.Count == 0) return;
        foreach (var (fenceId, items) in _items.Fences.Where(entry => entry.Value.Any(item => changed.Contains(item.Target))))
        {
            ScheduleFenceRefresh(fenceId);
        }
    }

    private void ScheduleFenceRefresh(string fenceId)
    {
        if (_fenceRefreshTimers.TryGetValue(fenceId, out var pending) && pending.IsEnabled) return; // already coming
        var delay = _refreshThrottle.DelayFor(fenceId, Now);
        var timer = pending ?? new DispatcherTimer();
        if (pending is null)
        {
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _refreshThrottle.Refreshed(fenceId, Now);
                if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } window) RefreshWindow(window);
            };
            _fenceRefreshTimers[fenceId] = timer;
        }
        timer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);
        timer.Start();
    }

    /// <summary>The game was left: renames seen meanwhile are followed, and every target is checked if checks waited.</summary>
    private void ApplyDeferredWatching()
    {
        if (_seenRenames.Count > 0)
        {
            Log.Information("settling {Count} rename(s) from game mode", _seenRenames.Count);
            SettleRenames();
        }
        if (!_checksDeferred) return;
        _checksDeferred = false;
        CheckAllTargets();
    }
}
