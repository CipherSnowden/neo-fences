using System.Diagnostics;
using System.IO;
using NeoFences.Shell;
using Serilog;
using Velopack;

namespace NeoFences.App;

/// <summary>
/// Velopack's install and uninstall hooks (M7, ADR-023). Velopack starts the installed exe with its own arguments;
/// <see cref="Run"/> handles them and exits the process before any WPF starts. Ordinary starts return at once. A hook has
/// a 30 s limit. A newer Setup.exe over an installed copy closes NeoFences, runs the install hook, force-stops anything
/// left in the install folder and ends without starting the app (seen with --silent, v1.1.1): the install hook therefore
/// shows the icons and starts NeoFences once Setup has ended (ADR-028).
/// </summary>
public static class InstallHooks
{
    // Within Velopack's 30 s per hook. Uninstall: stop ≤ 5 s (Velopack has usually closed NeoFences already) + icons ≤ 20 s.
    // Install: icons ≤ ~14 s (a 10 s retry window, each try up to 4 s), then a start that does not wait.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IconRetryLimit = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan InstallIconRetryLimit = TimeSpan.FromSeconds(10);
    /// <summary>Setup's force-stop of the install folder runs right after the hook (~40 ms in the 1.1.2-beta.1 log).</summary>
    // ponytail: fixed delay; wait on Setup's process id if a slow machine ever loses the race.
    private const int StartDelaySeconds = 4;

    public static void Run() =>
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => OnAfterInstall())
            .OnBeforeUninstallFastCallback(_ => OnBeforeUninstall())
            // Never apply a downloaded update when NeoFences.exe starts (Velopack's default): it would force-kill the running
            // copy and its watchdog with the icons hidden. Updates apply only after the clean exit (M17 final review I1).
            .SetAutoApplyOnStartup(false)
            .Run();

    /// <summary>
    /// Hard rule 2 on install and upgrade: Setup closed the old copy, perhaps with the icons hidden, and will not start
    /// the new one. The icons come back now; NeoFences starts a few seconds after Setup has ended, through cmd.exe (outside
    /// the install folder, so Setup's force-stop does not end it). If Setup or the user starts it too, the second copy
    /// waits for the first and exits (single instance).
    /// </summary>
    private static void OnAfterInstall() => WithLog(() =>
    {
        // Only icons NeoFences hid itself (its takeover-active marker): icons the user hid in Explorer stay theirs (M8a, final review).
        if (new Watchdog(AppPaths.DataDirectory, _ => { }).IsTakeoverActiveMarked)
        {
            Log.Information("install: showing the desktop icons NeoFences had hidden");
            DesktopIcons.ShowWithRetry(giveUpAfter: InstallIconRetryLimit, log: message => Log.Information("install: {Message}", message));
        }
        if (Environment.ProcessPath is not { } exePath) return;
        Log.Information("install: starting NeoFences after Setup");
        // ping waits without a window or input (timeout needs a console); "start" then runs NeoFences as a normal app. Both by
        // full path (a failing wait would start it at once, before Setup's force-stop); the exe path comes through the
        // environment, so cmd expands it once and never parses it (a "%" or "!" in a profile path; final review).
        var system = Environment.SystemDirectory;
        var start = new ProcessStartInfo(Path.Combine(system, "cmd.exe"),
            // Unquoted ping path: after /c, cmd strips a leading quote and the last one on the line (the system folder has no spaces).
            $"/d /v:off /c {Path.Combine(system, "ping.exe")} -n {StartDelaySeconds + 1} 127.0.0.1 >nul & start \"\" \"%NEOFENCES_EXE%\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = system, // not the install folder: an open working directory blocks the next Setup's rename (final review I1)
        };
        start.Environment["NEOFENCES_EXE"] = exePath;
        Process.Start(start)?.Dispose();
    });

    /// <summary>
    /// Hard rule 2: after uninstall the desktop icons are always visible, even if NeoFences was not running or had
    /// crashed (Explorer keeps "hide icons" across restarts). The sign-in entry goes if it starts this copy. The data
    /// in %LOCALAPPDATA%\NeoFences stays (user choice 2026-10-03), so a reinstall brings the fences back.
    /// </summary>
    private static void OnBeforeUninstall() => WithLog(() =>
    {
        Log.Information("uninstall: stopping NeoFences, restoring icons, removing the sign-in entry");
        StopRunningInstance();
        // The last code that can restore the icons (Velopack has already closed NeoFences and its watchdog): retry, then
        // fall back to Explorer's persisted setting (M7 review I2).
        DesktopIcons.ShowWithRetry(giveUpAfter: IconRetryLimit, log: message => Log.Information("uninstall: {Message}", message));
        if (InstallRoot() is { } installRoot) StartupRegistration.RemoveIfUnder(installRoot, log: message => Log.Information("{Message}", message));
    });

    /// <summary>
    /// Asks the running NeoFences to exit cleanly (it restores icons and tells its watchdog all is well), then waits for
    /// every other NeoFences process (main and watchdog) to end. Velopack ends whatever is left after that.
    /// </summary>
    private static void StopRunningInstance()
    {
        if (EventWaitHandle.TryOpenExisting(Program.ExitSignalName, out var exit))
        {
            using (exit) exit.Set();
        }
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < StopTimeout && OtherNeoFencesProcesses() > 0) Thread.Sleep(200);
        Log.Information("stop for install: {Remaining} NeoFences process(es) left after {Seconds:0.0} s", OtherNeoFencesProcesses(), deadline.Elapsed.TotalSeconds);
    }

    private static int OtherNeoFencesProcesses()
    {
        // Only this session: the exit signal is per session, so another signed-in user's NeoFences would only make this
        // wait the full timeout (M8a).
        var sessionId = Process.GetCurrentProcess().SessionId;
        var processes = Process.GetProcessesByName("NeoFences");
        var others = processes.Count(process => process.Id != Environment.ProcessId && process.SessionId == sessionId);
        foreach (var process in processes) process.Dispose();
        return others;
    }

    /// <summary><c>&lt;root&gt;\current\NeoFences.exe</c> → <c>&lt;root&gt;</c> (Velopack's layout); null when not installed.</summary>
    private static string? InstallRoot() =>
        Environment.ProcessPath is { } exePath && NeoFences.Core.Lifecycle.StartupPolicy.IsInstalledExe(exePath, File.Exists)
            ? Path.GetDirectoryName(Path.GetDirectoryName(exePath))
            : null;

    private static void WithLog(Action hook)
    {
        try
        {
            // Logging is set up inside the try: a broken logs folder must never stop the icon restore (M7 review I2).
            try
            {
                Directory.CreateDirectory(AppPaths.LogsDirectory);
                App.ConfigureLogging(fileName: "install-.log");
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // run the hook without a log file
            }
            hook();
        }
        catch (Exception failure)
        {
            Log.Error(failure, "install hook failed"); // never block an uninstall or update
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
