using NeoFences.Core.Library;

namespace NeoFences.Core.Items;

/// <summary>The groups of "Add from desktop…" (M19, spec 2026-10-05 §3), in the order the dialog shows them.</summary>
public enum DesktopGroup { Games, Apps, FoldersAndFiles, WebLinks }

/// <summary>One desktop entry as the dialog reads it.</summary>
/// <param name="ItemRef">Its full path, or <c>::{CLSID}</c> for a Windows icon (Recycle Bin, This PC).</param>
/// <param name="LinkTarget">A .lnk / .url's target ("" when it has no file path, e.g. a Store app shortcut); null when it is not a
/// shortcut or could not be read.</param>
public sealed record DesktopEntry(string ItemRef, bool IsFolder, string? LinkTarget, string? LinkArguments);

public static class DesktopSorting
{
    private static readonly string[] ProgramExtensions = [".exe", ".bat", ".cmd", ".com", ".msc", ".ps1", ".appref-ms"];
    private static readonly string[] SystemFolders = ["Program Files", "Program Files (x86)", "ProgramData", "Windows"];
    private const string AppsFolderRef = "::{4234D49B-0245-4DF3-B780-3893943456E1}"; // Start's All apps

    /// <summary>
    /// Game folders that can mean "a game is in here": not a drive root and not a system folder (Program Files, Windows…),
    /// which a sloppy launcher record can name as an install folder and which would make every program a game (M20).
    /// </summary>
    public static IReadOnlyList<string> UsableGameFolders(IEnumerable<string> folders) =>
        [.. folders.Where(folder =>
        {
            var trimmed = folder.Trim().TrimEnd('\\');
            if (trimmed.Length <= 2) return false; // "D:" or nothing
            var rest = trimmed.Length > 3 && trimmed[1] == ':' && trimmed[2] == '\\' ? trimmed[3..] : null;
            return rest is null || !SystemFolders.Contains(rest, StringComparer.OrdinalIgnoreCase);
        })];

    /// <summary>
    /// Games: what the Game Library counts as a game (a launcher link or library folder, a target under one of the user's
    /// game folders or a scanned game's install folder, or an entry its scan already knows as a game). Web links: a .url to
    /// an http(s) address. Apps: any other shortcut to a program, an app or a link without a file path, and programs right on
    /// the desktop. Folders and files: everything else, Windows' icons too.
    /// </summary>
    /// <param name="gameFolders">The user's game folders and the install folders the Game Library's scan found.</param>
    /// <param name="knownGames">Desktop entries the Game Library's scan counts as games (its Desktop shortcuts), or null.</param>
    public static DesktopGroup GroupOf(DesktopEntry entry, IReadOnlyList<string> gameFolders, IReadOnlySet<string>? knownGames = null)
    {
        if (entry.ItemRef.StartsWith("::", StringComparison.Ordinal) || entry.IsFolder) return DesktopGroup.FoldersAndFiles;
        if (knownGames?.Contains(entry.ItemRef) == true) return DesktopGroup.Games;
        if (entry.LinkTarget is not { } target)
        {
            var isShortcut = Path.GetExtension(entry.ItemRef).ToLowerInvariant() is ".lnk" or ".url";
            return !isShortcut && IsProgram(entry.ItemRef) ? DesktopGroup.Apps : DesktopGroup.FoldersAndFiles;
        }
        if (GameLaunchers.LauncherOf($"{target} {entry.LinkArguments}".Trim()) is not null
            || gameFolders.Any(folder => target.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) return DesktopGroup.Games;
        if (ItemKinds.IsWebsite(target)) return DesktopGroup.WebLinks;
        // A shortcut to a Windows place (Control Panel, This PC) or a file:// link: a place, not a program (M20).
        if (target.StartsWith(AppsFolderRef, StringComparison.OrdinalIgnoreCase)) return DesktopGroup.Apps;
        if (target.StartsWith("::", StringComparison.Ordinal) || target.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return DesktopGroup.FoldersAndFiles;
        if (target.Length == 0 || ItemKinds.IsApp(target) || IsProgram(target) || target.Contains(':') && !target.Contains('\\'))
            return DesktopGroup.Apps; // "ms-settings:display", another app's link scheme
        return DesktopGroup.FoldersAndFiles;
    }

    private static bool IsProgram(string path) => ProgramExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
}
