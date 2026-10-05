using System.IO;
using System.Windows.Threading;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// A folder's live listing and its watcher: the Game Library fence's own folder (M12; Portals' code until M18) and every
/// folder view's folder (M21).
/// Listing and watcher setup run off the UI thread (M4 review I1); bursts of changes become one re-list at most every
/// 250 ms (I2); an unavailable or unwatched folder is retried every few seconds (I4).
/// </summary>
public sealed class FolderLister : IDisposable
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(7);

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _retryTimer;
    private readonly Action<IReadOnlyList<ItemInfo>?> _show;
    private readonly Action<Exception> _logFailure;
    private readonly string _label; // "library folder" / "folder view": log lines say which
    private readonly Action<string, string>? _renamed;
    private readonly bool _namesOnly;
    private FolderWatcher? _watcher;
    private DeviceRemovalNotice? _removal;   // asks before the library drive is removed (USB stick, M8d)
    private readonly nint _noticeOwner;
    private volatile bool _noticeFailureLogged;
    private volatile bool _watchFailureLogged; // an unavailable folder fails at every 7 s retry: said once per outage (final review I1)
    private readonly DispatcherTimer _backoffTimer;
    private TimeSpan _failureDelay = NeoFences.Core.Lifecycle.WatcherBackoff.First;
    private DateTime _lastArm = DateTime.MinValue;
    private int _generation;
    private bool _refreshing;   // a listing is running in the background (a network folder may take seconds)
    private bool _disposed;
    private bool _paused;        // game mode (M6a): changes wait
    private bool _missedChanges; // something changed while paused: re-list on resume
    // Listings in flight hold a watcher and a notice of their own until the UI takes them: those can be let go as well (M13b).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<nint, (FolderWatcher Watcher, DeviceRemovalNotice Notice)> _inFlight = new();
    private DateTime _releasedUntil = DateTime.MinValue; // just let go of a drive being removed: no re-opening for a moment (M13a)
    private static readonly TimeSpan ReleaseGrace = TimeSpan.FromSeconds(5);

    public string Folder { get; }

    /// <param name="show">Called on the UI thread with the shown folder's items, or null when it cannot be read.</param>
    /// <param name="noticeOwner">The window that receives "may this drive be removed?" (the app's message window).</param>
    /// <param name="renamed">Called on the UI thread with the old and new path when the folder itself is renamed in place (M21).</param>
    /// <param name="namesOnly">Only entries appearing, going or renamed re-list it (M27: auto-collect); writes do not.</param>
    /// <param name="startPaused">Started during a game (M28): the first listing waits for <see cref="SetPaused"/>(false).</param>
    public FolderLister(string folder, nint noticeOwner, string label, Action<IReadOnlyList<ItemInfo>?> show, Action<Exception> logFailure,
        Action<string, string>? renamed = null, bool namesOnly = false, bool startPaused = false)
    {
        _namesOnly = namesOnly;
        _label = label;
        _renamed = renamed;
        _noticeOwner = noticeOwner;
        _backoffTimer = new DispatcherTimer();
        _backoffTimer.Tick += (_, _) =>
        {
            _backoffTimer.Stop();
            if (_paused) _missedChanges = true; // re-listed when the game ends (M8d review I1)
            else Refresh();
        };
        Folder = NeoFences.Core.Items.FolderViews.ListedFolder(folder); // a drive root keeps its "\" (final review I2)
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
        if (startPaused) (_paused, _missedChanges) = (true, true); // listed when the game ends
        else Refresh();
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
        var folder = Folder;
        var dispatcher = _refreshTimer.Dispatcher;
        Task.Run(() =>
        {
            var watcher = new FolderWatcher(folder, LogWatchFailureOnce, namesOnly: _namesOnly);
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
                if (_renamed is { } renamed)
                {
                    watcher.Renamed += (oldPath, newPath) =>
                    {
                        if (NeoFences.Core.Items.FolderViews.SameFolder(oldPath, folder)) dispatcher.BeginInvoke(() => { if (!_disposed) renamed(oldPath, newPath); });
                    };
                }
                if (watcher.HasFailed) OnWatcherFailed(); // it failed while arming, before this subscription (M8d review I2)
                // Unreadable or unwatched (drive not there yet): try again every few seconds until it is.
                if (items is not null && watcher.IsWatching) _watchFailureLogged = false; // back: the next outage is said again
                if (items is null || !watcher.IsWatching) _retryTimer.Start();
                else _retryTimer.Stop();
                _show(items);
            });
        });
    }

    /// <summary>Game mode (spec §4.7): while paused, folder changes only mark the listing stale; resuming re-lists once.</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused || !_missedChanges) return;
        _missedChanges = false;
        Refresh();
    }

    /// <summary>
    /// Windows asks to remove the drive holding this handle: close everything the lister holds there, so "Safely Remove"
    /// and Eject work (M8d). It shows nothing and comes back by itself when the drive does, or the removal was refused
    /// (the 7 s retry timer).
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
        _releasedUntil = DateTime.UtcNow + ReleaseGrace;
        _show(null);
        _retryTimer.Start(); // every 7 s, after the grace: back when the drive is, or when the removal was refused
        return true;
    }

    private void LogWatchFailureOnce(Exception failure)
    {
        if (_watchFailureLogged) return;
        _watchFailureLogged = true;
        _logFailure(failure);
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
        Serilog.Log.Information("{Label} watcher stopped ({Folder}); re-listing after {Delay}", _label, Folder, _failureDelay);
    }

    private void ScheduleRefresh()
    {
        if (_paused)
        {
            _missedChanges = true;
            return;
        }
        // Not restarted by every event: a file that keeps being written still re-lists every 250 ms.
        if (!_disposed && !_refreshTimer.IsEnabled) _refreshTimer.Start();
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
