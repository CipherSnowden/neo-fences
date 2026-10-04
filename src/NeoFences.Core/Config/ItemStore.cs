using NeoFences.Core.Items;

namespace NeoFences.Core.Config;

/// <param name="IsReadOnly">True when items.json must not be overwritten this session (a newer NeoFences wrote it, or it
/// could not be read right now): the document returned is the best fallback.</param>
public sealed record ItemsLoadResult(ItemsDocument Document, ConfigLoadSource Source, string? CorruptCopyPath, bool IsReadOnly);

/// <summary>
/// Loads and saves <c>items.json</c> (ADR-041): the same safe save, <c>.bak</c>, daily backups and recovery chain as
/// <c>config.json</c>, in a file of its own, so a damaged items file never takes the settings with it (or the other way).
/// </summary>
public sealed class ItemStore(string directory, TimeProvider? timeProvider = null)
{
    public const string FileName = "items.json";

    private readonly JsonStore<ItemsDocument> _store = new(directory, FileName, ItemsDocument.CurrentSchemaVersion, ConfigJson.DeserializeItems,
        document => document.Schema, ConfigJson.SerializeItems, timeProvider ?? TimeProvider.System);

    public string ItemsPath => _store.FilePath;
    public string BackupPath => _store.BackupPath;
    public string BackupsDirectory => _store.BackupsDirectory;

    /// <summary>Why the last save could not write or prune the daily backup (null when it could).</summary>
    public Exception? LastBackupFailure => _store.LastBackupFailure;

    public ItemsLoadResult Load()
    {
        var (document, source, corruptCopyPath, isReadOnly) = _store.Load();
        return new(document is null ? new ItemsDocument() : ItemEdits.Repair(document), source, corruptCopyPath, isReadOnly);
    }

    /// <returns>False when saving is blocked (see <see cref="ItemsLoadResult.IsReadOnly"/>).</returns>
    public bool Save(ItemsDocument document) => _store.Save(document);
}
