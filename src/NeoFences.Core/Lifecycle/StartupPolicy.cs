namespace NeoFences.Core.Lifecycle;

/// <summary>
/// Start with Windows (ADR-019, refined by ADR-023). Desktop icons NeoFences hid survive a hard power loss (Explorer
/// persists them), so NeoFences has to come back at sign-in by itself to show fences, or the desktop is empty.
/// </summary>
public static class StartupPolicy
{
    /// <summary>
    /// Which exe the sign-in entry should start, or null for "leave it alone":
    /// <list type="bullet">
    /// <item>nothing while the setting is off;</item>
    /// <item>an installed copy always names itself;</item>
    /// <item>an entry that already starts an installed copy that still exists stays as it is;</item>
    /// <item>otherwise the installed copy at its usual place, if it exists, even when a development or temp build asks
    /// (the setting toggled in a dev build must not hand sign-in to that build, M7 review I3);</item>
    /// <item>otherwise this build, except from the temp folder (that copy is deleted later; sign-in would start nothing).</item>
    /// </list>
    /// </summary>
    /// <param name="currentRunCommand">The entry as it is now (null when there is none).</param>
    /// <param name="defaultInstalledExe">Where Velopack installs NeoFences: %LOCALAPPDATA%NeoFences.AppcurrentNeoFences.exe.</param>
    public static string? SignInTarget(bool startWithWindows, string exePath, string tempFolder, string? currentRunCommand,
        string defaultInstalledExe, Func<string, bool> fileExists)
    {
        if (!startWithWindows) return null;
        if (IsInstalledExe(exePath, fileExists)) return exePath;
        if (ExePathOf(currentRunCommand) is { } registered && IsInstalledExe(registered, fileExists) && fileExists(registered)) return registered;
        if (IsInstalledExe(defaultInstalledExe, fileExists) && fileExists(defaultInstalledExe)) return defaultInstalledExe;
        return exePath.StartsWith(tempFolder, StringComparison.OrdinalIgnoreCase) ? null : exePath;
    }

    /// <summary>
    /// An exe installed by Velopack: <c>&lt;root&gt;\current\NeoFences.exe</c> next to <c>&lt;root&gt;\Update.exe</c>.
    /// </summary>
    public static bool IsInstalledExe(string exePath, Func<string, bool> fileExists)
    {
        var folder = Path.GetDirectoryName(exePath);
        if (folder is null || !string.Equals(Path.GetFileName(folder), "current", StringComparison.OrdinalIgnoreCase)) return false;
        var root = Path.GetDirectoryName(folder);
        return root is not null && fileExists(Path.Combine(root, "Update.exe"));
    }

    /// <summary>The command line stored for sign-in: the quoted exe path.</summary>
    public static string RunCommand(string exePath) => $"\"{exePath}\"";

    /// <summary>The exe path inside a stored command (quoted or not); null for none.</summary>
    public static string? ExePathOf(string? runCommand)
    {
        if (string.IsNullOrWhiteSpace(runCommand)) return null;
        var command = runCommand.Trim();
        if (command[0] != '"') return command;
        var closing = command.IndexOf('"', 1);
        return closing > 1 ? command[1..closing] : null;
    }
}
