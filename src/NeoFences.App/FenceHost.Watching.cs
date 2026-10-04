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
    private const int MaxDeferredRenames = 500;

    private readonly Dictionary<string, TargetCheck> _targetChecks = new(ItemKinds.Comparer);
    private readonly Dictionary<string, (FolderWatcher Watcher, DeviceRemovalNotice? Notice)> _targetWatchers = new(ItemKinds.Comparer);
    private HashSet<string> _wantedFolders = new(ItemKinds.Comparer);
    private readonly HashSet<string> _armingFolders = new(ItemKinds.Comparer); // watchers being opened off the UI thread
    private readonly RefreshThrottle _refreshThrottle = new();
    private readonly Dictionary<string, DispatcherTimer> _fenceRefreshTimers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _changedFolders = new(ItemKinds.Comparer);
    private readonly List<(string OldPath, string NewPath)> _deferredRenames = [];
    private DispatcherTimer? _folderChangeTimer, _recheckTimer, _drivesTimer;
    private bool _checksDeferred, _watchingStopped;

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
        foreach (var timer in _fenceRefreshTimers.Values) timer.Stop();
        foreach (var (watcher, notice) in _targetWatchers.Values)
        {
            watcher.Dispose();
            notice?.Dispose();
        }
        _targetWatchers.Clear();
    }

    /// <summary>Watches the folders the plan picks now; watchers no longer wanted (or not watching any more) go.</summary>
    private void UpdateWatching()
    {
        if (_watchingStopped) return;
        _wantedFolders = WatchPlan.Folders(ItemEdits.PathTargets(_items)).ToHashSet(ItemKinds.Comparer);
        foreach (var (folder, (watcher, notice)) in _targetWatchers.Where(entry => !_wantedFolders.Contains(entry.Key) || !entry.Value.Watcher.IsWatching).ToList())
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
                // Events were lost or it stopped: its targets are checked now; the 5-minute check re-arms it.
                watch.Watcher.Failed += () => dispatcher.BeginInvoke(() => OnTargetFolderChanged(folder));
                _targetWatchers[folder] = watch;
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>A watcher, with a removal notice where Windows offers one, so "Safely remove" still works (M8d). Off the UI thread.</summary>
    private static (FolderWatcher Watcher, DeviceRemovalNotice? Notice) Watch(string folder, nint noticeOwner)
    {
        var watcher = new FolderWatcher(folder, failure => Log.Debug(failure, "cannot watch {Folder}; its items are checked every few minutes", folder));
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

    /// <summary>Something in a watched folder changed: its targets are checked once the burst is over.</summary>
    private void OnTargetFolderChanged(string folder)
    {
        if (_watchingStopped) return;
        _changedFolders.Add(folder);
        if (_folderChangeTimer is null)
        {
            _folderChangeTimer = new DispatcherTimer { Interval = FolderChangeQuiet };
            _folderChangeTimer.Tick += (_, _) =>
            {
                _folderChangeTimer.Stop();
                var targets = ItemEdits.PathTargets(_items).Where(target => WatchPlan.ParentOf(target) is { } parent && _changedFolders.Contains(parent)).ToList();
                _changedFolders.Clear();
                CheckTargets(targets);
            };
        }
        _folderChangeTimer.Stop();
        _folderChangeTimer.Start();
    }

    /// <summary>A target (or a folder holding targets) was renamed in place: every item pointing at it follows (spec §4).</summary>
    private void OnTargetRenamed(string oldPath, string newPath)
    {
        if (_watchingStopped) return;
        if (Current.ShellWorkDeferred)
        {
            if (_deferredRenames.Count < MaxDeferredRenames) _deferredRenames.Add((oldPath, newPath)); // after the game
            else _checksDeferred = true; // too many: the check after the game shows what is missing
            return;
        }
        FollowRename(oldPath, newPath);
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
        Task.Run(() => targets.Select(target => (target, TargetProbe.Check(target))).ToList())
            .ContinueWith(checks => ApplyChecks(checks.Result), TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>New states: the fences holding a target whose state changed refresh (at most once every 2 s each).</summary>
    private void ApplyChecks(IReadOnlyList<(string Target, TargetCheck Check)> checks)
    {
        if (_watchingStopped) return;
        var changed = new HashSet<string>(ItemKinds.Comparer);
        foreach (var (target, check) in checks)
        {
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
        if (_deferredRenames.Count > 0)
        {
            var renames = _deferredRenames.ToList();
            _deferredRenames.Clear();
            Log.Information("following {Count} rename(s) from game mode", renames.Count);
            foreach (var (oldPath, newPath) in renames) FollowRename(oldPath, newPath); // also at a session end during a game
        }
        if (!_checksDeferred) return;
        _checksDeferred = false;
        CheckAllTargets();
    }
}
