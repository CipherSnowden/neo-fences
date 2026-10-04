using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <param name="CorruptCopyPath">Where an unreadable config.json was preserved, if it was.</param>
/// <param name="IsReadOnly">
/// True when config.json must not be overwritten this session: it was written by a newer NeoFences, or it
/// could not be read (locked by antivirus, OneDrive or an editor). The config returned is the best fallback.
/// </param>
public sealed record ConfigLoadResult(NeoFencesConfig Config, ConfigLoadSource Source, string? CorruptCopyPath, bool IsReadOnly)
{
    /// <summary>
    /// True when the config's fences are the user's real ones (read from a file, writable). A fresh or read-only fallback
    /// does not know them: item lists of fences it lacks must then be kept, not dropped at save (ADR-041).
    /// </summary>
    public bool KnowsTheFences => Source != ConfigLoadSource.Fresh && !IsReadOnly;
}

/// <summary>
/// Loads and saves <c>config.json</c> (ADR-006): atomic replace with <c>.bak</c>, one backup per day (newest 10 kept),
/// and a recovery chain for corrupt files (<see cref="JsonStore{T}"/>). A config from before the virtual items (schema
/// &lt; 5, ADR-040) is not migrated: NeoFences starts fresh, and the old file stays in <c>backups\</c>. Debouncing saves
/// is the caller's job.
/// </summary>
public sealed class ConfigStore
{
    public const string FileName = "config.json";
    public const int DailyBackupsKept = JsonStore<NeoFencesConfig>.DailyBackupsKept;

    /// <summary>The first schema of the virtual items (M18): older configs describe Desktop membership, Portals and rules.</summary>
    public const int FirstVirtualItemsSchema = 5;

    /// <summary>
    /// One copy of the last config from before the current schema, kept in <c>backups\</c> for good (M13b): daily backups
    /// rotate out after 10 days. Not a "config-*.json" name: never picked or pruned as a daily.
    /// </summary>
    public static string PreviousSchemaCopyName => $"pre-schema-{NeoFencesConfig.CurrentSchemaVersion}-config.json";

    private readonly JsonStore<NeoFencesConfig> _store;
    private bool _previousSchemaChecked; // once per run: with no older file to find, every save would re-read up to 11 files (M13c)

    public ConfigStore(string directory, TimeProvider? timeProvider = null)
    {
        _store = new JsonStore<NeoFencesConfig>(directory, FileName, NeoFencesConfig.CurrentSchemaVersion, ConfigJson.Deserialize,
            config => config.SchemaVersion, ConfigJson.Serialize, timeProvider ?? TimeProvider.System);
    }

    public string ConfigPath => _store.FilePath;
    public string BackupPath => _store.BackupPath;
    public string BackupsDirectory => _store.BackupsDirectory;

    /// <summary>Why the last save could not write or prune the daily backup (null when it could). Reported apart from Save.</summary>
    public Exception? LastBackupFailure { get; private set; }

    public ConfigLoadResult Load()
    {
        var (config, source, corruptCopyPath, isReadOnly) = _store.Load();
        if (config is null) return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, corruptCopyPath, isReadOnly);
        // From before the virtual items: a fresh start (spec §1); the first save keeps the old file as PreviousSchemaCopyName.
        if (config.SchemaVersion < FirstVirtualItemsSchema) return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, corruptCopyPath, isReadOnly);
        return new(ConfigNormalizer.Normalize(config), source, corruptCopyPath, isReadOnly);
    }

    /// <returns>
    /// False when saving is blocked: config.json belongs to a newer NeoFences version (checked on disk on every
    /// save, so a second store or a save without a prior Load cannot overwrite it either), or Load found it
    /// unreadable.
    /// </returns>
    public bool Save(NeoFencesConfig config)
    {
        if (!_store.Save(config)) return false;
        LastBackupFailure = _store.LastBackupFailure;
        try
        {
            KeepPreviousSchemaCopy();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            LastBackupFailure = failure;
        }
        return true;
    }

    private void KeepPreviousSchemaCopy()
    {
        if (_previousSchemaChecked) return;
        var copy = Path.Combine(BackupsDirectory, PreviousSchemaCopyName);
        if (!File.Exists(copy))
        {
            foreach (var candidate in _store.DailyBackupsNewestFirst().Prepend(BackupPath))
            {
                if (_store.TryRead(candidate, out var old) != JsonStore<NeoFencesConfig>.ReadOutcome.Ok
                    || old!.SchemaVersion >= NeoFencesConfig.CurrentSchemaVersion) continue;
                Directory.CreateDirectory(BackupsDirectory);
                File.Copy(candidate, copy); // a failure throws past the flag below: the next save tries again (M13c final review)
                break;
            }
        }
        _previousSchemaChecked = true;
    }
}
