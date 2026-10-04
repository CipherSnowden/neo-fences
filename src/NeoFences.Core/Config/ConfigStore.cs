using System.Text.Json;
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

public enum ConfigLoadSource { Primary, Backup, DailyBackup, Fresh }

/// <param name="CorruptCopyPath">Where an unreadable config.json was preserved, if it was.</param>
/// <param name="IsReadOnly">
/// True when config.json must not be overwritten this session: it was written by a newer NeoFences, or it
/// could not be read (locked by antivirus, OneDrive or an editor). The config returned is the best fallback.
/// </param>
public sealed record ConfigLoadResult(NeoFencesConfig Config, ConfigLoadSource Source, string? CorruptCopyPath, bool IsReadOnly);

/// <summary>
/// Loads and saves <c>config.json</c> (ADR-006): atomic replace with <c>.bak</c>, one backup per day
/// (newest 10 kept), and a recovery chain for corrupt files. Debouncing saves is the caller's job.
/// </summary>
public sealed class ConfigStore(string directory, TimeProvider? timeProvider = null)
{
    public const string FileName = "config.json";
    public const int DailyBackupsKept = 10;

    /// <summary>
    /// One copy of the last config from before schema 2, kept in <c>backups\</c> for good (M13b): daily backups rotate out
    /// after 10 days, and NeoFences ≤ 1.5 can only read schema 1. Not a "config-*.json" name: never picked or pruned as a daily.
    /// </summary>
    public static string PreviousSchemaCopyName => $"pre-schema-{NeoFencesConfig.CurrentSchemaVersion}-config.json";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private bool _saveBlocked;

    public string ConfigPath => Path.Combine(directory, FileName);
    public string BackupPath => ConfigPath + ".bak";
    public string BackupsDirectory => Path.Combine(directory, "backups");
    private string TempPath => ConfigPath + ".tmp";

    public ConfigLoadResult Load()
    {
        _saveBlocked = false;
        string? corruptCopyPath = null;

        switch (TryRead(ConfigPath, out var primary))
        {
            case ReadOutcome.Ok:
                return new(ConfigNormalizer.Normalize(primary!), ConfigLoadSource.Primary, null, IsReadOnly: false);
            case ReadOutcome.NewerSchema:
                _saveBlocked = true;
                return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, null, IsReadOnly: true);
            case ReadOutcome.Unreadable:
                // Not corrupt, just inaccessible right now: show the best fallback, but never overwrite it.
                _saveBlocked = true;
                break;
            case ReadOutcome.Corrupt:
                corruptCopyPath = Path.Combine(directory, $"config.corrupt-{_time.GetLocalNow():yyyyMMdd-HHmmss}.json");
                try
                {
                    File.Copy(ConfigPath, corruptCopyPath, overwrite: true);
                }
                catch (Exception copyFailure) when (copyFailure is IOException or UnauthorizedAccessException)
                {
                    corruptCopyPath = null;
                    _saveBlocked = true; // could not preserve it, so do not overwrite it either
                }
                break;
        }

        if (TryRead(BackupPath, out var backup) == ReadOutcome.Ok)
        {
            return new(ConfigNormalizer.Normalize(backup!), ConfigLoadSource.Backup, corruptCopyPath, IsReadOnly: _saveBlocked);
        }

        foreach (var dailyBackupPath in DailyBackupsNewestFirst())
        {
            if (TryRead(dailyBackupPath, out var daily) == ReadOutcome.Ok)
            {
                return new(ConfigNormalizer.Normalize(daily!), ConfigLoadSource.DailyBackup, corruptCopyPath, IsReadOnly: _saveBlocked);
            }
        }

        return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, corruptCopyPath, IsReadOnly: _saveBlocked);
    }

    /// <returns>
    /// False when saving is blocked: config.json belongs to a newer NeoFences version (checked on disk on every
    /// save, so a second store or a save without a prior Load cannot overwrite it either), or Load found it
    /// unreadable.
    /// </returns>
    public bool Save(NeoFencesConfig config)
    {
        if (_saveBlocked || TryRead(ConfigPath, out _) == ReadOutcome.NewerSchema) return false;

        Directory.CreateDirectory(directory);
        var json = ConfigJson.Serialize(config);
        using (var tempFile = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(tempFile))
        {
            writer.Write(json);
            writer.Flush();
            tempFile.Flush(flushToDisk: true);
        }

        if (File.Exists(ConfigPath))
        {
            File.Replace(TempPath, ConfigPath, BackupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(TempPath, ConfigPath);
        }

        // The daily backup is a convenience: its failure (a full disk, a file in the way) must not fail the save (M8a).
        try
        {
            WriteDailyBackup(json);
            KeepPreviousSchemaCopy();
            LastBackupFailure = null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            LastBackupFailure = failure;
        }
        return true;
    }

    /// <summary>Why the last save could not write or prune the daily backup (null when it could). Reported apart from Save.</summary>
    public Exception? LastBackupFailure { get; private set; }

    private void WriteDailyBackup(string json)
    {
        Directory.CreateDirectory(BackupsDirectory);
        // First save of the day wins: a config that went bad later in the day cannot overwrite it.
        var todayPath = Path.Combine(BackupsDirectory, $"config-{_time.GetLocalNow():yyyyMMdd}.json");
        if (!File.Exists(todayPath)) File.WriteAllText(todayPath, json);

        foreach (var expiredPath in DailyBackupsNewestFirst().Skip(DailyBackupsKept))
        {
            File.Delete(expiredPath);
        }
    }

    private bool _previousSchemaChecked; // once per run: with no older file to find, every save would re-read up to 11 files (M13c)

    private void KeepPreviousSchemaCopy()
    {
        if (_previousSchemaChecked) return;
        var copy = Path.Combine(BackupsDirectory, PreviousSchemaCopyName);
        if (!File.Exists(copy))
        {
            foreach (var candidate in DailyBackupsNewestFirst().Prepend(BackupPath))
            {
                if (TryRead(candidate, out var old) != ReadOutcome.Ok || old!.SchemaVersion >= NeoFencesConfig.CurrentSchemaVersion) continue;
                File.Copy(candidate, copy); // a failure throws past the flag below: the next save tries again (M13c final review)
                break;
            }
        }
        _previousSchemaChecked = true;
    }

    private IEnumerable<string> DailyBackupsNewestFirst() =>
        Directory.Exists(BackupsDirectory)
            ? Directory.GetFiles(BackupsDirectory, "config-*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    private enum ReadOutcome { Missing, Ok, Corrupt, NewerSchema, Unreadable }

    private const int ReadAttempts = 3;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(100);

    private static ReadOutcome TryRead(string path, out NeoFencesConfig? config)
    {
        config = null;
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
                // ponytail: blocking retry (~200 ms worst case), fine for startup; make async if Load moves off-thread.
                if (attempt == ReadAttempts) return ReadOutcome.Unreadable;
                Thread.Sleep(ReadRetryDelay);
            }
        }

        try
        {
            config = ConfigJson.Deserialize(json);
        }
        catch (JsonException)
        {
            return ReadOutcome.Corrupt;
        }
        if (config.SchemaVersion > NeoFencesConfig.CurrentSchemaVersion) return ReadOutcome.NewerSchema;
        return config.SchemaVersion < 1 ? ReadOutcome.Corrupt : ReadOutcome.Ok;
    }
}
