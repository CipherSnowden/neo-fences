using System.Globalization;
using System.IO;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Entry point. The helper modes (<c>--exit</c>, <c>--watchdog-launch</c>, <c>--watchdog</c>) run without WPF, so the
/// watchdog owns no window: it never answers (or blocks) WM_QUERYENDSESSION and never shows on the "apps are
/// blocking shutdown" screen (M2a review C1/M2). Only the normal mode starts the WPF <see cref="App"/>.
/// </summary>
public static class Program
{
    public const string ExitArgument = "--exit";
    public const string ExitSignalName = @"Local\NeoFences.Exit";

    [STAThread]
    public static int Main(string[] args)
    {
        InstallHooks.Run(); // Velopack: install / update / uninstall hooks exit here; ordinary starts continue (M7)
        // Not the install folder (Start menu, Setup and watchdog starts land there): an open working directory, inherited by
        // everything opened from a fence, blocks the next Setup's rename of that folder (v1.1.2 final review I1).
        Environment.CurrentDirectory = Environment.SystemDirectory;
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        switch (args)
        {
            case [ExitArgument]:
                if (EventWaitHandle.TryOpenExisting(ExitSignalName, out var runningInstanceExit)) runningInstanceExit.Set();
                return 0;
            case [Watchdog.LaunchArgument, var launchProcessIdText]:
                Watchdog.RunLauncher(int.Parse(launchProcessIdText, CultureInfo.InvariantCulture));
                return 0;
            case [Watchdog.RunArgument, var mainProcessIdText]:
                return RunWatchdog(int.Parse(mainProcessIdText, CultureInfo.InvariantCulture));
        }

        var app = new App();
        return app.Run();
    }

    private static int RunWatchdog(int mainProcessId)
    {
        App.ConfigureLogging(fileName: "watchdog-.log");
        try
        {
            new Watchdog(AppPaths.DataDirectory, message => Log.Information("{Message}", message)).Run(mainProcessId);
            return 0;
        }
        catch (Exception failure)
        {
            Log.Fatal(failure, "watchdog failed");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
