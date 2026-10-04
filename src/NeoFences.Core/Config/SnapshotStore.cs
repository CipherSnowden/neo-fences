using System.Text.Json;
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>A snapshot file as the list shows it.</summary>
public sealed record SnapshotEntry(string Path, string Name, DateTimeOffset TakenAt)
{
    /// <summary>The automatic one written before every restore (replaced each time).</summary>
    public bool IsBeforeRestore => string.Equals(System.IO.Path.GetFileName(Path), SnapshotStore.BeforeRestoreFileName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Snapshot files (M10): one per snapshot in <c>%LOCALAPPDATA%\NeoFences\snapshots\</c>, written to a temp file and then
/// swapped in, so a power cut never leaves half a snapshot. A damaged file is skipped and reported, never thrown.
/// Deleting is the App's job (the Recycle Bin, hard rule 1).
/// </summary>
public sealed class SnapshotStore(string directory)
{
    public const string BeforeRestoreFileName = "before-restore.json";

    /// <summary>Bigger files are not snapshots (a stray file): skipped unread, so a tray click never loads it (M13a).</summary>
    public const long MaxFileBytes = 16 * 1024 * 1024;

    public string Directory => directory;

    /// <summary>Files the last <see cref="List"/> could not read (path and reason).</summary>
    public IReadOnlyList<(string Path, Exception Failure)> Problems { get; private set; } = [];

    /// <summary>Why the last <see cref="Save"/> or <see cref="Rename"/> failed, or null.</summary>
    public Exception? LastFailure { get; private set; }

    /// <summary>Every readable snapshot, newest first.</summary>
    public IReadOnlyList<SnapshotEntry> List()
    {
        var entries = new List<SnapshotEntry>();
        var problems = new List<(string, Exception)>();
        string[] paths;
        try
        {
            paths = System.IO.Directory.Exists(directory) ? System.IO.Directory.GetFiles(directory, "*.json") : [];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            paths = []; // the folder itself cannot be listed: no snapshots, never a crash (final review I3, hard rule 7)
            problems.Add((directory, failure));
        }
        foreach (var path in paths)
        {
            try
            {
                var snapshot = Read(path);
                entries.Add(new SnapshotEntry(path, snapshot.Name ?? "", snapshot.TakenAt));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                problems.Add((path, failure));
            }
        }
        Problems = problems;
        return entries.OrderByDescending(entry => entry.TakenAt).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Writes a snapshot; a new file (named by its time) unless <paramref name="fileName"/> is given.</summary>
    /// <returns>The file's path, or null when it could not be written (<see cref="LastFailure"/>).</returns>
    public string? Save(Snapshot snapshot, string? fileName = null)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, fileName ?? NewFileName(snapshot.TakenAt));
            WriteSafely(path, ConfigJson.SerializeSnapshot(snapshot));
            LastFailure = null;
            return path;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            LastFailure = failure;
            return null;
        }
    }

    /// <summary>The snapshot in a file, or null when it cannot be read.</summary>
    public Snapshot? Load(string path)
    {
        try
        {
            return Read(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            LastFailure = failure;
            return null;
        }
    }

    /// <exception cref="InvalidDataException">Too big, or written by a newer NeoFences (M13a): restoring or renaming it
    /// here would drop what this version does not know.</exception>
    private static Snapshot Read(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException($"{path} is too big for a snapshot");
        var snapshot = ConfigJson.DeserializeSnapshot(File.ReadAllText(path));
        if (snapshot.SchemaVersion > NeoFencesConfig.CurrentSchemaVersion)
            throw new InvalidDataException($"{path} comes from a newer NeoFences (schema {snapshot.SchemaVersion})");
        return snapshot;
    }

    /// <summary>
    /// A new name for a snapshot (its time stays). A renamed "Before restore" becomes an ordinary snapshot file, so the
    /// next restore does not overwrite it (final review I4); our own file is moved, nothing is deleted.
    /// </summary>
    public bool Rename(string path, string newName)
    {
        if (Load(path) is not { } snapshot) return false;
        var fileName = System.IO.Path.GetFileName(path);
        if (string.Equals(fileName, BeforeRestoreFileName, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                fileName = NewFileName(snapshot.TakenAt);
                File.Move(path, System.IO.Path.Combine(directory, fileName));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                LastFailure = failure;
                return false;
            }
        }
        return Save(snapshot with { Name = newName.Trim() }, fileName) is not null;
    }

    private string NewFileName(DateTimeOffset takenAt)
    {
        var stem = $"snapshot-{takenAt:yyyy-MM-dd_HH-mm-ss}";
        var name = stem + ".json";
        for (var counter = 2; File.Exists(System.IO.Path.Combine(directory, name)); counter++) name = $"{stem}-{counter}.json";
        return name;
    }

    private static void WriteSafely(string path, string json)
    {
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp); // no *.json.tmp left behind, also after a full disk (M13a, M13b); the caller reports the failure
            throw;
        }
    }
}
