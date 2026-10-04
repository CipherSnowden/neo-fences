using System.IO;
using System.Windows.Threading;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// One Portal fence at runtime (M4): the folder it mirrors, the subfolder it shows now (browsing is not saved; a restart
/// shows the Portal's own folder again), and its watcher. Listing and watcher setup run off the UI thread, since a
/// network or USB folder can block for seconds (M4 review I1); bursts of changes become one re-list at most every
/// 250 ms, even while a file keeps being written (I2); an unavailable or unwatched folder is retried every few
/// seconds, so a Portal comes back when its drive does (I4).
/// </summary>
public sealed class PortalState : IDisposable
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(7);

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _retryTimer;
    private readonly Action<IReadOnlyList<ItemInfo>?> _show;
    private readonly Action<Exception> _logFailure;
    private FolderWatcher? _watcher;
    private DeviceRemovalNotice? _removal;   // asks before the Portal's drive is removed (USB stick, M8d)
    private readonly nint _noticeOwner;
    private volatile bool _noticeFailureLogged;
    private readonly DispatcherTimer _backoffTimer;
    private TimeSpan _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.First;
    private DateTime _lastArm = DateTime.MinValue;
    private HashSet<string> _listedFolders = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;
    private bool _refreshing;   // a listing is running in the background (a network folder may take seconds)
    private bool _disposed;
    private bool _paused;        // game mode (M6a): changes wait
    private bool _missedChanges; // something changed while paused: re-list on resume
    // Listings in flight hold a watcher and a notice of their own until the UI takes them: those can be let go as well (M13b).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<nint, (FolderWatcher Watcher, DeviceRemovalNotice Notice)> _inFlight = new();
    private DateTime _releasedUntil = DateTime.MinValue; // just let go of a drive being removed: no re-opening for a moment (M13a)
    private static readonly TimeSpan ReleaseGrace = TimeSpan.FromSeconds(5);

    public string Root { get; }

    public string Current { get; private set; }

    public bool CanGoBack => !string.Equals(Current, Root, StringComparison.OrdinalIgnoreCase);

    /// <param name="show">Called on the UI thread with the shown folder's items, or null when it cannot be read.</param>
    /// <param name="noticeOwner">The window that receives "may this drive be removed?" (the app's message window).</param>
    public PortalState(string root, nint noticeOwner, Action<IReadOnlyList<ItemInfo>?> show, Action<Exception> logFailure)
    {
        _noticeOwner = noticeOwner;
        _backoffTimer = new DispatcherTimer();
        _backoffTimer.Tick += (_, _) =>
        {
            _backoffTimer.Stop();
            if (_paused) _missedChanges = true; // re-listed when the game ends (M8d review I1)
            else Refresh();
        };
        Root = Normalize(root);
        Current = Root;
        _show = show;
        _logFailure = logFailure;
        _refreshTimer = new DispatcherTimer { Interval = RefreshDelay };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            Refresh();
        };
        _retryTimer = new DispatcherTimer { Interval = RetryDelay };
        _retryTimer.Tick += (_, _) => { if (!_refreshing && !_paused) Refresh(); }; // never pile up blocked listings
        Refresh();
    }

    /// <summary>"Downloads › Mods › Old" while browsing below the Portal's folder.</summary>
    public string Breadcrumb(string title)
    {
        var below = Path.GetRelativePath(Root, Current);
        return below == "." ? title : title + " › " + below.Replace("\\", " › ");
    }

    /// <summary>True when the last listing showed this ref as a folder (no disk access: a network folder may block).</summary>
    public bool IsListedFolder(string itemRef) => _listedFolders.Contains(itemRef);

    /// <summary>Shows a subfolder of the Portal (double-click on a folder inside it).</summary>
    public void Browse(string folder)
    {
        Current = Normalize(folder);
        Refresh();
    }

    /// <summary>One level up, never above the Portal's own folder.</summary>
    public void Back()
    {
        if (!CanGoBack) return;
        Current = Path.GetDirectoryName(Current) is { } parent ? Normalize(parent) : Root;
        Refresh();
    }

    /// <summary>
    /// Re-arms the watcher and re-lists the shown folder in the background; only the newest request is shown. The
    /// watcher is re-created each time: a folder deleted and created again ends the old one.
    /// </summary>
    public void Refresh()
    {
        if (_disposed) return;
        if (DateTime.UtcNow < _releasedUntil)
        {
            _retryTimer.Start(); // a refresh queued just before the release would re-open the drive: the retry comes back later
            return;
        }
        _refreshing = true;
        var generation = ++_generation;
        var folder = Current;
        var dispatcher = _refreshTimer.Dispatcher;
        Task.Run(() =>
        {
            var watcher = new FolderWatcher(folder, _logFailure);
            var removal = watcher.HeldFolder is { } held ? DeviceRemovalNotice.TryRegister(_noticeOwner, held, LogNoticeFailureOnce) : null;
            if (removal is not null) _inFlight[removal.Handle] = (watcher, removal);
            var items = FolderItems.TryList(folder);
            dispatcher.BeginInvoke(() =>
            {
                // Only this listing's own entry: a handle value reused by a newer listing keeps its entry (M13c).
                if (removal is not null) _inFlight.TryRemove(new KeyValuePair<nint, (FolderWatcher, DeviceRemovalNotice)>(removal.Handle, (watcher, removal)));
                if (generation == _generation) _refreshing = false;
                if (_disposed || generation != _generation)
                {
                    watcher.Dispose(); // a newer refresh (or the fence's deletion) superseded this one
                    removal?.Dispose();
                    return;
                }
                _watcher?.Dispose();
                _watcher = watcher;
                _removal?.Dispose();
                _removal = removal;
                _lastArm = DateTime.UtcNow;
                watcher.Changed += () => dispatcher.BeginInvoke(ScheduleRefresh);
                watcher.Failed += () => dispatcher.BeginInvoke(OnWatcherFailed);
                if (watcher.HasFailed) OnWatcherFailed(); // it failed while arming, before this subscription (M8d review I2)
                _listedFolders = items is null
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(items.Where(item => item.IsFolder).Select(item => item.ItemRef), StringComparer.OrdinalIgnoreCase);
                // Unreadable or unwatched (drive not there yet): try again every few seconds until it is.
                if (items is null || !watcher.IsWatching) _retryTimer.Start();
                else _retryTimer.Stop();
                _show(items);
            });
        });
    }

    /// <summary>Game mode (spec §4.7): while paused, folder changes only mark the Portal stale; resuming re-lists once.</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused || !_missedChanges) return;
        _missedChanges = false;
        Refresh();
    }

    /// <summary>
    /// Windows asks to remove the drive holding this handle: close everything the Portal holds there, so "Safely Remove"
    /// and Eject work while it is shown (M8d). The Portal shows the folder as unavailable and comes back by itself when
    /// the drive does, or the removal was refused (the 7 s retry timer).
    /// </summary>
    public bool ReleaseForRemoval(nint handle)
    {
        if (_inFlight.TryRemove(handle, out var flying))
        {
            // A listing still on its way holds the drive: let go now; when it arrives it is stale and dropped (M13b).
            ++_generation;
            _refreshing = false;
            flying.Watcher.Dispose();
            flying.Notice.Dispose();
            _releasedUntil = DateTime.UtcNow + ReleaseGrace;
            _show(null);
            _retryTimer.Start();
            return true;
        }
        if (_removal is null || _removal.Handle != handle) return false;
        ++_generation; // a listing in flight must not re-open the folder
        _refreshing = false;
        _refreshTimer.Stop();
        _backoffTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _removal.Dispose();
        _removal = null;
        _listedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _releasedUntil = DateTime.UtcNow + ReleaseGrace;
        _show(null);
        _retryTimer.Start(); // every 7 s, after the grace: back when the drive is, or when the removal was refused
        return true;
    }

    /// <summary>A drive that refuses removal notices (a virtual drive reporting "Fixed") is said once, not on every refresh (M8d review).</summary>
    private void LogNoticeFailureOnce(Exception failure)
    {
        if (_noticeFailureLogged) return;
        _noticeFailureLogged = true;
        _logFailure(failure);
    }

    /// <summary>A watcher that fails again soon after each re-arm waits longer each time, up to a minute (M8d, M4 review).</summary>
    private void OnWatcherFailed()
    {
        if (_disposed || _backoffTimer.IsEnabled) return;
        if (_paused)
        {
            _missedChanges = true; // a stopped watcher during a game: re-list (and re-arm) when it ends (M8d review I1)
            return;
        }
        _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.Next(_failureDelay, lastRearm: _lastArm, failureAt: DateTime.UtcNow);
        _backoffTimer.Interval = _failureDelay;
        _backoffTimer.Start();
        Serilog.Log.Information("Portal folder watcher stopped ({Folder}); re-listing after {Delay}", Current, _failureDelay);
    }

    private void ScheduleRefresh()
    {
        if (_paused)
        {
            _missedChanges = true;
            return;
        }
        // Not restarted by every event: a file that keeps being written still refreshes the Portal every 250 ms.
        if (!_disposed && !_refreshTimer.IsEnabled) _refreshTimer.Start();
    }

    /// <summary>A path without a trailing separator, except a drive root ("D:\").</summary>
    private static string Normalize(string folder)
    {
        var trimmed = folder.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : trimmed;
    }

    public void Dispose()
    {
        _disposed = true;
        _refreshTimer.Stop();
        _retryTimer.Stop();
        _backoffTimer.Stop();
        _watcher?.Dispose();
        _watcher = null;
        _removal?.Dispose();
        _removal = null;
    }
}
