using NeoFences.Core.Config;
using NeoFences.Core.Library;

namespace NeoFences.Shell;

/// <summary>
/// Keeps NeoFences' library folder in step with a <see cref="LibraryPlan"/> (M12, spec §3): writes changed shortcuts
/// (temp file, then swapped in), removes only files listed in its own index, and saves the index. User files are never
/// touched: the folder is NeoFences' own. Call on an STA thread.
/// </summary>
public static partial class LibraryWriter
{
    [System.Text.RegularExpressions.GeneratedRegex(@"^\.[0-9a-f]{32}\.(lnk|url)$")]
    private static partial System.Text.RegularExpressions.Regex TempName();

    public const string IndexFileName = "index.json";

    /// <summary>The saved index, or an empty one when missing or damaged (the next scan rebuilds it).</summary>
    public static LibraryState ReadIndex(string folder)
    {
        try
        {
            var path = Path.Combine(folder, IndexFileName);
            return File.Exists(path) ? LibraryFiles.Repair(ConfigJson.DeserializeLibrary(File.ReadAllText(path))) : new LibraryState();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new LibraryState();
        }
    }

    /// <returns>The state actually on disk: an item whose file could not be written is left out (it is tried again next time).</returns>
    /// <param name="previous">The index before this plan: what is on disk when a write or delete fails (M13a).</param>
    /// <param name="hidden">The hidden games to remember in the index (M13b).</param>
    public static LibraryState Apply(string folder, IReadOnlyList<LibraryItem> previous, LibraryPlan plan, IReadOnlyList<HiddenEntry> hidden, Action<string, Exception> logFailure)
    {
        Directory.CreateDirectory(folder);
        // Temp files of a write a power cut interrupted: NeoFences' own names (".<32 hex>.lnk/.url"), never anything else (M13b).
        foreach (var stray in Directory.EnumerateFiles(folder, ".*").Where(path => TempName().IsMatch(Path.GetFileName(path))))
        {
            try { File.Delete(stray); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        var failedDeletes = new List<string>();
        foreach (var fileName in plan.Delete)
        {
            try
            {
                File.Delete(Path.Combine(folder, fileName)); // our own shortcut, listed in our index
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                logFailure(fileName, failure);
                failedDeletes.Add(fileName);
            }
        }
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lost = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toWrite = plan.Items.Where(item => plan.Write.Contains(item) || !File.Exists(Path.Combine(folder, item.FileName))); // also files deleted by hand
        foreach (var item in toWrite)
        {
            var path = Path.Combine(folder, item.FileName);
            var temp = Path.Combine(folder, $".{Guid.NewGuid():N}{Path.GetExtension(item.FileName)}");
            try
            {
                if (!TryCopyShortcut(item, temp)) ShellLinks.Write(temp, item.Game.Launch, item.Game.IconPath is { } icon && IsIconSource(icon) ? icon : null);
                // On the disk before it replaces the old one: a power cut never leaves a zero-filled shortcut that the index
                // lists and no scan rewrites (final review, re-graded minor 2).
                using (var written = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) written.Flush(flushToDisk: true);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(item.FileName, failure);
                (File.Exists(path) ? failed : lost).Add(item.FileName); // no old file to fall back on: dropped from the index (M13b)
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        var state = LibraryFiles.Settle(previous, plan, failedWrites: failed, failedDeletes: failedDeletes, lostFiles: lost) with { Hidden = hidden };
        var index = Path.Combine(folder, IndexFileName);
        var indexTemp = index + ".tmp";
        // Written through to the disk before the swap, like config.json: a power cut never leaves an empty index (M13b).
        using (var stream = new FileStream(indexTemp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(ConfigJson.SerializeLibrary(state));
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        File.Move(indexTemp, index, overwrite: true);
        return state;
    }

    /// <summary>
    /// The user's own Desktop shortcut, copied as is: its icon and "Run as administrator" stay (M13b). A copy is made plain
    /// (a read-only original would make the copy impossible to replace or delete: Hide would stop working; final review,
    /// re-graded minor 1). False when there is nothing to copy or the copy failed: the shortcut is written instead.
    /// </summary>
    private static bool TryCopyShortcut(LibraryItem item, string temp)
    {
        if (item.Game.ShortcutFile is not { } source || !File.Exists(source)
            || !Path.GetExtension(source).Equals(Path.GetExtension(item.FileName), StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            File.Copy(source, temp, overwrite: true);
            File.SetAttributes(temp, FileAttributes.Normal);
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return false;
        }
    }

    /// <summary>Shortcut icons come from programs and icon files, not from PNG logos (those show on the tile instead).</summary>
    public static bool IsIconSource(string path) => Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".ico" or ".dll";
}
