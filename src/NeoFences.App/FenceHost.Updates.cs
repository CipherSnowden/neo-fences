using System.IO;
using System.Windows.Threading;
using NeoFences.Core.Updates;
using NeoFences.Shell;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace NeoFences.App;

/// <summary>What Settings → Updates shows (M17).</summary>
/// <param name="Available">An installed copy (not a developer build): updates can work at all.</param>
/// <param name="ReadyVersion">A downloaded update waiting for a restart, or null.</param>
public sealed record UpdatesView(bool Available, bool AutoUpdate, string Status, string? ReadyVersion);

/// <summary>
/// Auto-update (M17, spec 2026-10-04-public-releases-design §4): Velopack checks the public GitHub releases at start and once
/// a day (never during a game, never when switched off), downloads quietly, then offers "Restart to update"; an ignored
/// update is applied after the next normal exit. The update always runs after NeoFences' clean exit (hard rule 2).
/// </summary>
public sealed partial class FenceHost
{
    public const string ReleasesRepository = "https://github.com/CipherSnowden/neo-fences";
    private const int TrayRestartToUpdate = 13; // M17
    private static readonly TimeSpan UpdateTick = TimeSpan.FromHours(1);
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1); // never slows the start

    private UpdateManager? _updates;      // null: a developer build, or Velopack could not start (logged)
    private VelopackAsset? _readyUpdate;  // downloaded, waiting for a restart
    private UpdateState _updateState = new(LastCheck: null, LastFailed: false);
    private DispatcherTimer? _updateTimer;
    private bool _updateBusy, _restartAfterUpdate;
    private CancellationTokenSource? _updateCancel; // switched off or a game starts: the download stops (final review M2)
    private string _updateStatus = "";
    private readonly HashSet<string> _loggedUpdateFailures = new(StringComparer.Ordinal);

    /// <summary>
    /// The GitHub releases, or a local folder of releases from <c>NEOFENCES_UPDATE_SOURCE</c> (a rehearsal on this PC without
    /// GitHub). A copy not installed by Setup never updates.
    /// </summary>
    private void StartUpdates()
    {
        try
        {
            var local = Environment.GetEnvironmentVariable("NEOFENCES_UPDATE_SOURCE");
            IUpdateSource source = local is { Length: > 0 } && Directory.Exists(local)
                ? new SimpleFileSource(new DirectoryInfo(local))
                : new GithubSource(ReleasesRepository, accessToken: null, prerelease: false);
            var manager = new UpdateManager(source);
            if (!manager.IsInstalled)
            {
                _updateStatus = "Updates are off in a developer build.";
                return;
            }
            _updates = manager;
            _updateStatus = $"Version {manager.CurrentVersion}.";
            if (manager.UpdatePendingRestart is { } waiting) // downloaded in an earlier run: offered again, applied at the next clean exit
            {
                _readyUpdate = waiting;
                _updateStatus = $"Version {waiting.Version} is ready. It installs when you restart NeoFences.";
            }
            if (local is { Length: > 0 }) Log.Information("updates from the local folder {Folder} (rehearsal)", local);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "updates unavailable"); // hard rule 7: NeoFences runs on without them
            _updateStatus = "Updates are unavailable (see the log).";
            return;
        }
        // M33: after a crash, look for a fix soon (it may be the cure), within the user's choice and never in a game.
        var afterCrash = StartMode is AppStart.Restarted or AppStart.SafeMode;
        _updateTimer = new DispatcherTimer { Interval = afterCrash ? TimeSpan.FromSeconds(10) : FirstCheckDelay };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = UpdateTick;
            CheckForUpdate(manual: afterCrash && _config.Settings.AutoUpdate && !_gameMode);
            afterCrash = false;
        };
        _updateTimer.Start();
    }

    /// <summary>The schedule (or "Check now"): look, download quietly, then offer the restart.</summary>
    private async void CheckForUpdate(bool manual)
    {
        if (_updates is not { } updates || _updateBusy || _readyUpdate is not null) return;
        if (!manual && !UpdatePolicy.ShouldCheck(DateTimeOffset.Now, _updateState, enabled: _config.Settings.AutoUpdate, installed: true, gameMode: _gameMode))
            return;
        _updateBusy = true;
        _updateCancel = new CancellationTokenSource();
        var cancel = _updateCancel.Token;
        _updateStatus = "Checking for updates…";
        RefreshSettings();
        try
        {
            var found = await updates.CheckForUpdatesAsync();
            cancel.ThrowIfCancellationRequested();
            _updateState = new UpdateState(DateTimeOffset.Now, LastFailed: false);
            if (found is null)
            {
                _updateStatus = $"Up to date (version {updates.CurrentVersion}) · checked {DateTime.Now:HH:mm}.";
                return;
            }
            var version = found.TargetFullRelease.Version.ToString();
            _updateStatus = $"Downloading version {version}…";
            RefreshSettings();
            await updates.DownloadUpdatesAsync(found, progress: null, cancelToken: cancel);
            _readyUpdate = found.TargetFullRelease;
            _updateStatus = $"Version {version} is ready. It installs when you restart NeoFences.";
            Log.Information("update {Version} downloaded; waiting for a restart", version);
            if (!_gameMode) // never a notice over a game; the tray item and Settings say it (final review M3)
                _trayIcon?.ShowBalloon("NeoFences update ready", $"Version {version} is ready. Restart to update (tray menu), or it installs when you exit NeoFences.");
        }
        catch (OperationCanceledException)
        {
            _updateStatus = "Update check stopped (switched off, or a game started). It runs again later.";
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            _updateState = new UpdateState(DateTimeOffset.Now, LastFailed: true); // again in 6 h
            _updateStatus = $"Couldn't check for updates ({DateTime.Now:HH:mm}): {Reason(failure)}";
            if (_loggedUpdateFailures.Add(failure.GetType().Name)) Log.Warning(failure, "update check failed; trying again later");
        }
        finally
        {
            _updateBusy = false;
            _updateCancel?.Dispose();
            _updateCancel = null;
            RefreshSettings();
        }
    }

    private static string Reason(Exception failure) => failure switch
    {
        System.Net.Http.HttpRequestException => "offline, or GitHub cannot be reached.",
        TaskCanceledException or TimeoutException => "the connection timed out.",
        _ => "see the log.",
    };

    /// <summary>Tray or Settings → "Restart to update": NeoFences' normal clean exit, then Velopack installs and restarts it.</summary>
    private void RestartToUpdate()
    {
        if (_readyUpdate is null) return;
        Log.Information("restarting to update to {Version}", _readyUpdate.Version);
        _restartAfterUpdate = true;
        ExitRequested?.Invoke();
    }

    /// <summary>
    /// The last step of a clean exit (never at sign-out or shutdown): a downloaded update is installed once NeoFences has
    /// ended; after "Restart to update" the new version starts, after a normal Exit it stays closed.
    /// </summary>
    private void ApplyUpdateOnExit()
    {
        if (_updates is not { } updates || _readyUpdate is not { } ready) return;
        // Updates switched off: a plain Exit does not install (an explicit "Restart to update" still does; final review M2).
        if (!_restartAfterUpdate && !_config.Settings.AutoUpdate) return;
        if (_watchdog.IsIconsHiddenMarked)
        {
            // The icons could not be shown just now: the watchdog is still restoring them, and Update.exe would end it
            // (hard rule 2). The update waits for the next exit (final review M1).
            Log.Warning("update {Version} not installed at this exit: the desktop icons are still being restored", ready.Version);
            return;
        }
        try
        {
            updates.WaitExitThenApplyUpdates(ready, silent: true, restart: _restartAfterUpdate, restartArgs: []);
            Log.Information("update {Version} will install now that NeoFences exits (restart: {Restart})", ready.Version, _restartAfterUpdate);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "update {Version} could not be started; it is offered again next time", ready.Version);
        }
    }

    /// <summary>Switched off or a game starts: a check or download in progress stops (no network while gaming or off).</summary>
    private void StopUpdateDownload() => _updateCancel?.Cancel();

    private void SetAutoUpdate(bool enabled)
    {
        if (!enabled) StopUpdateDownload();
        _config = _config with { Settings = _config.Settings with { AutoUpdate = enabled } };
        ScheduleSave();
        if (enabled) CheckForUpdate(manual: false); // switched on: the schedule decides at once
        RefreshSettings();
    }

    /// <summary>The tray's first item while an update waits (M17).</summary>
    private IEnumerable<TrayMenuItem> UpdateTrayItems() => _readyUpdate is { } ready
        ? [new TrayMenuItem(TrayRestartToUpdate, $"Restart to update to v{ready.Version}"), TrayMenuItem.Separator]
        : [];

    private UpdatesView UpdatesView() =>
        new(_updates is not null, _config.Settings.AutoUpdate, _updateStatus, _readyUpdate?.Version.ToString());
}
