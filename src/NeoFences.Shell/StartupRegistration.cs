using Microsoft.Win32;
using NeoFences.Core.Lifecycle;

namespace NeoFences.Shell;

/// <summary>
/// The per-user "run at sign-in" entry (HKCU\…\Run, no admin rights), ADR-019. If the user disables NeoFences under
/// Task Manager → Startup apps, Windows records that separately (StartupApproved) and keeps honouring it; this entry
/// does not override it.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NeoFences";

    /// <summary>Uninstall (M7): removes the entry only if it starts the copy being uninstalled. Never throws.</summary>
    public static void RemoveIfUnder(string installRoot, Action<string> log)
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            var registered = StartupPolicy.ExePathOf(run?.GetValue(ValueName) as string);
            if (run is null || registered is null) return;
            var root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(registered).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            run.DeleteValue(ValueName, throwOnMissingValue: false);
            log("start with Windows: removed for uninstall");
        }
        catch (Exception failure) when (failure is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
        {
            log($"start with Windows: could not remove the sign-in entry ({failure.Message})");
        }
    }

    /// <summary>Where Velopack installs NeoFences (packId NeoFences.App, ADR-023).</summary>
    public static string DefaultInstalledExe { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoFences.App", "current", "NeoFences.exe");

    /// <summary>
    /// Writes or removes the entry to match the setting. It starts the installed copy whenever one exists, else this
    /// build (StartupPolicy.SignInTarget, ADR-023). Never throws.
    /// </summary>
    public static void Apply(bool startWithWindows, string exePath, Action<string> log)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            var current = run.GetValue(ValueName) as string;
            var target = StartupPolicy.SignInTarget(startWithWindows, exePath, Path.GetTempPath(), currentRunCommand: current,
                defaultInstalledExe: DefaultInstalledExe, fileExists: File.Exists);
            if (target is not null)
            {
                var command = StartupPolicy.RunCommand(target);
                if (current == command) return;
                run.SetValue(ValueName, command);
                log($"start with Windows: registered {command}");
            }
            else if (!startWithWindows && run.GetValue(ValueName) is not null)
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
                log("start with Windows: removed");
            }
            else if (startWithWindows)
            {
                log($"start with Windows: left to {current ?? "(none)"} (this build runs from the temp folder)");
            }
        }
        catch (Exception failure) when (failure is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            log($"start with Windows: could not update the sign-in entry ({failure.Message})");
        }
    }
}
