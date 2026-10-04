using System.Diagnostics;
using System.Globalization;
using NeoFences.Core.Lifecycle;

namespace NeoFences.Shell;

/// <summary>
/// Restores desktop icons if NeoFences dies with them hidden, and restarts it (ADR-005, ADR-012, ADR-013).
/// The watchdog is the same exe in <c>--watchdog</c> mode, started through a short-lived <c>--watchdog-launch</c>
/// process so it is not a child of the main process ("End process tree" cannot take both down). It runs without
/// any window, so it never appears on the "apps are blocking shutdown" screen.
/// </summary>
public sealed class Watchdog(string dataDirectory, Action<string> log)
{
    public const string LaunchArgument = "--watchdog-launch";
    public const string RunArgument = "--watchdog";

    private const int RestoreAttempts = 10;
    private static readonly TimeSpan RestoreRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SessionPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SessionEndGracePeriod = TimeSpan.FromSeconds(30);

    private string IconsHiddenPath => Path.Combine(dataDirectory, "icons-hidden");
    private string RestartLogPath => Path.Combine(dataDirectory, "watchdog-restarts.txt");
    private string CleanMarkerPath(int processId) => Path.Combine(dataDirectory, $"clean-shutdown-{processId}");
    private string SessionEndingMarkerPath(int processId) => Path.Combine(dataDirectory, $"session-ending-{processId}");

    public bool IsIconsHiddenMarked => File.Exists(IconsHiddenPath);

    /// <summary>Main process, at startup: drops stale markers for this PID, then launches the watchdog detached.</summary>
    public void LaunchDetached(int mainProcessId)
    {
        Directory.CreateDirectory(dataDirectory);
        File.Delete(CleanMarkerPath(mainProcessId));
        File.Delete(SessionEndingMarkerPath(mainProcessId));
        StartSelf($"{LaunchArgument} {mainProcessId}");
    }

    /// <summary><c>--watchdog-launch</c> mode: start the real watchdog and exit, orphaning it from the main process tree.</summary>
    public static void RunLauncher(int mainProcessId) => StartSelf($"{RunArgument} {mainProcessId}");

    /// <summary>Main process: the icons may be hidden by NeoFences right now (the watchdog restores only in that case).</summary>
    public void SetIconsHiddenMarker(bool hidden)
    {
        Directory.CreateDirectory(dataDirectory);
        if (hidden) File.WriteAllText(IconsHiddenPath, Timestamp());
        else File.Delete(IconsHiddenPath);
    }

    /// <summary>Main process, on an orderly exit the user asked for.</summary>
    public void MarkCleanShutdown(int mainProcessId) => WriteMarker(CleanMarkerPath(mainProcessId));

    /// <summary>Main process, when Windows asks to end the session (WPF then exits the app; ADR-013).</summary>
    public void MarkSessionEnding(int mainProcessId) => WriteMarker(SessionEndingMarkerPath(mainProcessId));

    /// <summary><c>--watchdog</c> mode. Returns when the main process has exited and recovery is done.</summary>
    public void Run(int mainProcessId)
    {
        log($"watching {mainProcessId}");
        try
        {
            using var mainProcess = Process.GetProcessById(mainProcessId);
            mainProcess.WaitForExit();
        }
        catch (ArgumentException)
        {
            log("main process already gone");
        }

        var plan = WatchdogPlan.For(
            cleanShutdown: ConsumeMarker(CleanMarkerPath(mainProcessId)),
            sessionEnding: ConsumeMarker(SessionEndingMarkerPath(mainProcessId)),
            iconsHidden: IsIconsHiddenMarked);
        log($"main exited: {plan}");

        if (plan.RestoreIcons) RestoreIconsWithRetry();

        switch (plan.Restart)
        {
            case WatchdogRestart.Never:
                return;
            case WatchdogRestart.IfSessionContinues:
                if (WaitForSessionToContinue()) RestartMain(reason: "session end was cancelled");
                else log("session is ending; not restarting");
                return;
            case WatchdogRestart.Throttled:
                var now = DateTimeOffset.Now;
                if (!RestartThrottle.ShouldRestart(recentRestarts: ReadRestarts(), now: now))
                {
                    log("restart limit reached (3 per 10 min), staying down");
                    return;
                }
                TryAppendRestart(now);
                RestartMain(reason: "unclean exit");
                return;
        }
    }

    /// <summary>
    /// True once Windows is no longer shutting the session down (the user cancelled). If the session really ends, this
    /// process is terminated first or the grace period runs out; either way NeoFences is not started mid-logoff.
    /// </summary>
    private bool WaitForSessionToContinue()
    {
        var deadline = DateTime.UtcNow + SessionEndGracePeriod;
        Thread.Sleep(TimeSpan.FromSeconds(2)); // let the shutdown decision settle
        while (DateTime.UtcNow < deadline)
        {
            if (!SessionState.IsShuttingDown()) return true;
            Thread.Sleep(SessionPollInterval);
        }
        return false;
    }

    private void RestoreIconsWithRetry()
    {
        // Explorer may be down at the same moment (e.g. it crashed together with us): keep trying for ~5 s, then fall
        // back to Explorer's persisted setting (DesktopIcons.ShowWithRetry).
        if (DesktopIcons.ShowWithRetry(giveUpAfter: RestoreRetryDelay * RestoreAttempts, log: log)) TryDelete(IconsHiddenPath);
    }

    private void RestartMain(string reason)
    {
        try
        {
            StartSelf(arguments: "");
            log($"main restarted ({reason})");
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            log($"could not restart main: {failure.Message}");
        }
    }

    private bool ConsumeMarker(string path)
    {
        if (!File.Exists(path)) return false;
        TryDelete(path);
        return true;
    }

    private void WriteMarker(string path)
    {
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(path, Timestamp());
    }

    private void TryAppendRestart(DateTimeOffset now)
    {
        try
        {
            File.AppendAllText(RestartLogPath, now.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            log($"could not record restart: {failure.Message}");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            log($"could not delete {Path.GetFileName(path)}: {failure.Message}");
        }
    }

    private List<DateTimeOffset> ReadRestarts()
    {
        try
        {
            return File.Exists(RestartLogPath)
                ? File.ReadAllLines(RestartLogPath)
                    .Select(line => DateTimeOffset.TryParse(line, CultureInfo.InvariantCulture, DateTimeStyles.None, out var restartTime) ? restartTime : (DateTimeOffset?)null)
                    .OfType<DateTimeOffset>()
                    .ToList()
                : [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            log($"could not read restart log: {failure.Message}");
            return [];
        }
    }

    private static string Timestamp() => DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);

    private static void StartSelf(string arguments) =>
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
}
