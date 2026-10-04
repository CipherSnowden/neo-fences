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
        else
        {
            using var staleExit = ExitSignal();
            staleExit.Reset(); // a stale "--exit" from before this start must not stop it
        }
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

        _exitSignal = ExitSignal();
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

    /// <summary>
    /// "--exit" sets this; manual-reset so a copy waiting to start sees it too and gives up instead of starting as the
    /// running one exits (M13a). The owner resets it when it starts.
    /// </summary>
    private static EventWaitHandle ExitSignal() => new(initialState: false, EventResetMode.ManualReset, Program.ExitSignalName);

    private static bool WaitForPreviousInstance(Mutex singleInstance)
    {
        try
        {
            using var exit = ExitSignal();
            if (exit.WaitOne(0))
            {
                // Set before this start: it is the exit the running instance is carrying out now, not one meant for this
                // copy. Wait for it to finish, then start (as 1.5 did; M13a final review M1).
                var owned = singleInstance.WaitOne(TimeSpan.FromSeconds(5));
                if (owned) exit.Reset();
                return owned;
            }
            // Whichever comes first: the previous instance leaving (start), or an "--exit" while waiting: everyone (give up).
            return WaitHandle.WaitAny([singleInstance, exit], TimeSpan.FromSeconds(5)) == 0;
        }
        catch (AbandonedMutexException)
        {
            return true; // the previous instance was killed: the mutex is ours now
        }
    }
}
