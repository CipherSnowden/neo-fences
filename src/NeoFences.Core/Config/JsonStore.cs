using System.Text.Json;

namespace NeoFences.Core.Config;

public enum ConfigLoadSource { Primary, Backup, DailyBackup, Fresh }

/// <summary>
/// One JSON file of NeoFences' own data (ADR-006, ADR-041): atomic replace with <c>.bak</c>, one backup per day (newest
/// 10 kept), a recovery chain for damaged files, and never a save over a file from a newer NeoFences. <c>config.json</c>
/// and <c>items.json</c> are each one of these.
/// </summary>
/// <param name="repair">Run on every read (M33): an exception here counts as a damaged file, so the backups are tried.</param>
/// <param name="readRetryDelay">Between read attempts of a locked file (M33: 4 attempts, about 5 s in all by default).</param>
internal sealed class JsonStore<T>(string directory, string fileName, int currentSchema, Func<string, T> deserialize, Func<T, int> schemaOf,
    Func<T, string> serialize, TimeProvider time, Func<T, T>? repair = null, TimeSpan? readRetryDelay = null) where T : class
{
    public const int DailyBackupsKept = 10;
    private const int ReadAttempts = 4;
    private readonly TimeSpan _readRetryDelay = readRetryDelay ?? TimeSpan.FromMilliseconds(1600);

    private bool _saveBlocked;

    public string FilePath => Path.Combine(directory, fileName);
    public string BackupPath => FilePath + ".bak";
    public string BackupsDirectory => Path.Combine(directory, "backups");
    private string Stem => Path.GetFileNameWithoutExtension(fileName);

    /// <summary>Why the last save could not write or prune the daily backup (null when it could). Reported apart from Save.</summary>
    public Exception? LastBackupFailure { get; private set; }

    /// <returns>The best readable version (null: start empty), where it came from, where a damaged file was preserved, and
    /// whether the file must not be overwritten this session (written by a newer NeoFences, or unreadable right now).</returns>
    public (T? Value, ConfigLoadSource Source, string? CorruptCopyPath, bool IsReadOnly) Load()
    {
        _saveBlocked = false;
        string? corruptCopyPath = null;

        switch (TryRead(FilePath, out var primary))
        {
            case ReadOutcome.Ok:
                return (primary, ConfigLoadSource.Primary, null, false);
            case ReadOutcome.NewerSchema:
                _saveBlocked = true;
                return (null, ConfigLoadSource.Fresh, null, true);
            case ReadOutcome.Unreadable:
                // Not corrupt, just inaccessible right now: show the best fallback, but never overwrite it.
                _saveBlocked = true;
                break;
            case ReadOutcome.Corrupt:
                corruptCopyPath = Path.Combine(directory, $"{Stem}.corrupt-{time.GetLocalNow():yyyyMMdd-HHmmss}.json");
                try
                {
                    File.Copy(FilePath, corruptCopyPath, overwrite: true);
                }
                catch (Exception copyFailure) when (copyFailure is IOException or UnauthorizedAccessException)
                {
                    corruptCopyPath = null;
                    _saveBlocked = true; // could not preserve it, so do not overwrite it either
                }
                break;
        }

        if (TryRead(BackupPath, out var backup) == ReadOutcome.Ok) return (backup, ConfigLoadSource.Backup, corruptCopyPath, _saveBlocked);
        foreach (var dailyBackupPath in DailyBackupsNewestFirst())
        {
            if (TryRead(dailyBackupPath, out var daily) == ReadOutcome.Ok) return (daily, ConfigLoadSource.DailyBackup, corruptCopyPath, _saveBlocked);
        }
        return (null, ConfigLoadSource.Fresh, corruptCopyPath, _saveBlocked);
    }

    /// <returns>
    /// False when saving is blocked: the file belongs to a newer NeoFences version (checked on disk on every save, so a
    /// second store or a save without a prior Load cannot overwrite it either), or Load found it unreadable.
    /// </returns>
    public bool Save(T value)
    {
        if (_saveBlocked || TryRead(FilePath, out _, probe: true) == ReadOutcome.NewerSchema) return false; // one quick look (M33 review I6)
        Directory.CreateDirectory(directory);
        var json = serialize(value);
        SafeFile.Write(FilePath, json, BackupPath);
        // The daily backup is a convenience: its failure (a full disk, a file in the way) must not fail the save (M8a).
        try
        {
            WriteDailyBackup(json);
            LastBackupFailure = null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            LastBackupFailure = failure;
        }
        return true;
    }

    private void WriteDailyBackup(string json)
    {
        Directory.CreateDirectory(BackupsDirectory);
        // First save of the day wins: a file that went bad later in the day cannot overwrite it.
        var todayPath = Path.Combine(BackupsDirectory, $"{Stem}-{time.GetLocalNow():yyyyMMdd}.json");
        if (!File.Exists(todayPath)) File.WriteAllText(todayPath, json);
        foreach (var expiredPath in DailyBackupsNewestFirst().Skip(DailyBackupsKept)) File.Delete(expiredPath);
    }

    public IEnumerable<string> DailyBackupsNewestFirst() =>
        Directory.Exists(BackupsDirectory)
            ? Directory.GetFiles(BackupsDirectory, $"{Stem}-*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    public enum ReadOutcome { Missing, Ok, Corrupt, NewerSchema, Unreadable }

    /// <param name="probe">Save's schema check: one attempt and no repair, so a save never waits on a locked file.</param>
    public ReadOutcome TryRead(string path, out T? value, bool probe = false)
    {
        value = null;
        if (!File.Exists(path)) return ReadOutcome.Missing;

        string? json = null;
        for (var attempt = 1; json is null; attempt++)
        {
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception readFailure) when (readFailure is IOException or UnauthorizedAccessException)
            {
                // ponytail: blocking retry (~5 s worst case, M33: antivirus or OneDrive holding the file at sign-in), fine for
                // startup; make async if Load moves off-thread.
                if (probe || attempt == ReadAttempts) return ReadOutcome.Unreadable;
                Thread.Sleep(_readRetryDelay);
            }
        }

        try
        {
            value = deserialize(json);
            var schema = schemaOf(value);
            if (schema > currentSchema) return ReadOutcome.NewerSchema;
            if (schema < 1) return ReadOutcome.Corrupt;
            if (repair is not null && !probe) value = repair(value);
            return ReadOutcome.Ok;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) // M33: anything odd in parse or repair is damage
        {
            value = null;
            return ReadOutcome.Corrupt;
        }
    }
}

/// <summary>Writes to a temp file flushed to disk, then swaps it in: a power cut never leaves half a file.</summary>
internal static class SafeFile
{
    /// <param name="backupPath">Where the replaced file goes (its <c>.bak</c>), or null to keep no copy.</param>
    public static void Write(string path, string text, string? backupPath)
    {
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(text);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temp, path, backupPath, ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp); // no *.tmp left behind, also after a full disk (M13a, M13b); the caller reports the failure
            throw;
        }
    }
}
