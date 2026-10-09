using System.IO.Compression;
using System.Text;
using System.Text.Json;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>What an export says about itself (<c>manifest.json</c>).</summary>
public sealed record SetupManifest
{
    public string Format { get; init; } = "";
    public int FormatVersion { get; init; }
    public string AppVersion { get; init; } = "";
    public int ConfigSchema { get; init; }
    public int ItemsSchema { get; init; }
    public DateTimeOffset ExportedAt { get; init; }
}

/// <summary>A picture that travels with a setup: its folder in NeoFences' data (<c>icons</c> or <c>covers</c>) and file name.</summary>
public sealed record SetupPicture(string Folder, string FileName);

public sealed record SetupPictureData(SetupPicture Picture, byte[] Bytes);

/// <summary>A setup read from a file: normalized, its items repaired, the pictures it uses.</summary>
public sealed record SetupRead(SetupManifest Manifest, NeoFencesConfig Config, ItemsDocument Items, IReadOnlyList<SetupPictureData> Pictures);

/// <summary>Why a file cannot be imported; the message is shown to the owner as it is.</summary>
public sealed class SetupFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Export setup… / Import setup… (M36, spec §3, ADR-057): one <c>.neofences</c> file, a zip of <c>manifest.json</c>,
/// <c>config.json</c> (fences, Settings, Games settings, own presets), <c>items.json</c> and the pictures they use (item icon
/// pictures in <c>icons/</c>, chosen covers in <c>covers/</c>). Never the logs, the game-library scan or a user's file.
/// </summary>
public static class SetupFile
{
    public const string Extension = ".neofences";
    public const string FormatName = "NeoFences setup";
    public const int CurrentFormatVersion = 1;

    public const string NotASetup = "This file is not a NeoFences setup.";
    public const string Damaged = "This file is damaged: NeoFences cannot read its setup.";
    public const string Unreadable = "NeoFences could not open this file.";

    /// <summary>Bigger parts are not ours (a zip bomb, a stray file): the JSON files and each picture.</summary>
    internal const long MaxJsonBytes = SnapshotStore.MaxFileBytes;
    internal const long MaxPictureBytes = 32 * 1024 * 1024;

    private const string IconsFolder = "icons", CoversFolder = "covers";

    /// <summary>The pictures a setup uses, by file name only (a hand-edited name never points outside its folder).</summary>
    public static IReadOnlyList<SetupPicture> PicturesOf(NeoFencesConfig config, ItemsDocument items) =>
        items.Fences.Values.SelectMany(list => list).Select(item => item.Icon?.Image).Select(name => (Folder: IconsFolder, Name: name))
            .Concat(config.Library.CoverChoices.Values.Select(name => (Folder: CoversFolder, Name: (string?)name)))
            .Select(picture => (picture.Folder, Name: WindowsPath.FileName(picture.Name ?? "")))
            .Where(picture => picture.Name.Length > 0)
            .Select(picture => new SetupPicture(picture.Folder, picture.Name))
            .Distinct()
            .ToList();

    /// <summary>
    /// Writes the setup to <paramref name="path"/> (a temp file first, then moved: never half a file). Changes nothing else.
    /// </summary>
    /// <param name="dataDirectory">NeoFences' data folder, holding <c>icons\</c> and <c>covers\</c>.</param>
    /// <returns>The pictures that were gone and are not in the file.</returns>
    /// <exception cref="IOException">The file could not be written (also <see cref="UnauthorizedAccessException"/>).</exception>
    public static IReadOnlyList<SetupPicture> Write(string path, NeoFencesConfig config, ItemsDocument items, string dataDirectory, string appVersion, DateTimeOffset now)
    {
        var missing = new List<SetupPicture>();
        // A name of its own (final review): a .tmp left by a power cut, or one that is not ours, is never opened or deleted.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                var manifest = new SetupManifest
                {
                    Format = FormatName, FormatVersion = CurrentFormatVersion, AppVersion = appVersion,
                    ConfigSchema = NeoFencesConfig.CurrentSchemaVersion, ItemsSchema = ItemsDocument.CurrentSchemaVersion, ExportedAt = now,
                };
                WriteText(zip, "manifest.json", ConfigJson.SerializeManifest(manifest));
                WriteText(zip, "config.json", ConfigJson.Serialize(config));
                WriteText(zip, "items.json", ConfigJson.SerializeItems(items));
                foreach (var picture in PicturesOf(config, items))
                {
                    var source = Path.Combine(dataDirectory, picture.Folder, picture.FileName);
                    if (File.Exists(source)) zip.CreateEntryFromFile(source, $"{picture.Folder}/{picture.FileName}", CompressionLevel.Fastest);
                    else missing.Add(picture);
                }
            }
            File.Move(temp, path, overwrite: true);
            return missing;
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }
    }

    /// <summary>Reads and checks an export: a NeoFences export, readable, not from a newer NeoFences.</summary>
    /// <param name="importedAt">When given, its auto-collect rules start looking then (final review I1): what is already on
    /// this PC is not collected as new.</param>
    /// <exception cref="SetupFileException">It cannot be imported; the message says why.</exception>
    public static SetupRead Read(string path, DateTimeOffset? importedAt = null)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifest = Text(zip, "manifest.json") is { } manifestText ? TryManifest(manifestText) : null;
            if (manifest?.Format != FormatName) throw new SetupFileException(NotASetup);
            if (manifest.FormatVersion > CurrentFormatVersion || manifest.ConfigSchema > NeoFencesConfig.CurrentSchemaVersion
                || manifest.ItemsSchema > ItemsDocument.CurrentSchemaVersion)
                throw Newer(manifest.AppVersion);

            var config = ConfigJson.Deserialize(Text(zip, "config.json") ?? throw new SetupFileException(Damaged));
            if (config.SchemaVersion > NeoFencesConfig.CurrentSchemaVersion) throw Newer(manifest.AppVersion);
            if (config.SchemaVersion < ConfigStore.FirstVirtualItemsSchema) throw new SetupFileException(Damaged);
            config = ConfigNormalizer.Normalize(config);
            if (importedAt is { } now) config = Snapshots.StartCollectingAt(config, now);
            var items = ConfigJson.DeserializeItems(Text(zip, "items.json") ?? throw new SetupFileException(Damaged));
            if (items.Schema > ItemsDocument.CurrentSchemaVersion) throw Newer(manifest.AppVersion);
            items = ItemEdits.Prune(ItemEdits.Repair(items), config.Fences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal));

            var pictures = new List<SetupPictureData>();
            foreach (var picture in PicturesOf(config, items))
            {
                // Only the exact name "folder/file" is looked up: an entry such as "icons/../x" is never one of them.
                if (zip.GetEntry($"{picture.Folder}/{picture.FileName}") is not { } entry || entry.Length > MaxPictureBytes) continue;
                if (ReadCapped(entry, MaxPictureBytes) is { } bytes) pictures.Add(new SetupPictureData(picture, bytes));
            }
            return new SetupRead(manifest, config, items, pictures);
        }
        catch (SetupFileException)
        {
            throw;
        }
        catch (InvalidDataException failure) // not a zip, or a damaged one
        {
            throw new SetupFileException(File.Exists(path) && IsZip(path) ? Damaged : NotASetup, failure);
        }
        catch (JsonException failure)
        {
            throw new SetupFileException(Damaged, failure);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new SetupFileException(Unreadable, failure);
        }
    }

    /// <summary>
    /// Puts a read setup's pictures into NeoFences' <c>icons\</c> and <c>covers\</c> (a picture of the same name is replaced).
    /// Each is written on its own: one that fails is reported and the rest still go in (the item shows its usual icon).
    /// </summary>
    /// <returns>The pictures that could not be written, with why.</returns>
    public static IReadOnlyList<(SetupPicture Picture, Exception Failure)> PlacePictures(SetupRead read, string dataDirectory)
    {
        var failures = new List<(SetupPicture, Exception)>();
        foreach (var (picture, bytes) in read.Pictures)
        {
            try
            {
                var folder = Path.Combine(dataDirectory, picture.Folder);
                Directory.CreateDirectory(folder);
                var target = Path.Combine(folder, WindowsPath.FileName(picture.FileName));
                File.WriteAllBytes(target + ".tmp", bytes);
                File.Move(target + ".tmp", target, overwrite: true);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                failures.Add((picture, failure));
            }
        }
        return failures;
    }

    private static SetupFileException Newer(string appVersion) =>
        new($"This file comes from a newer NeoFences ({(string.IsNullOrWhiteSpace(appVersion) ? "unknown version" : appVersion)}). Update NeoFences first.");

    private static SetupManifest? TryManifest(string text)
    {
        try
        {
            return ConfigJson.DeserializeManifest(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(ZipArchive zip, string name)
    {
        if (zip.GetEntry(name) is not { } entry) return null;
        if (entry.Length > MaxJsonBytes) throw new SetupFileException(Damaged);
        return ReadCapped(entry, MaxJsonBytes) is { } bytes ? Encoding.UTF8.GetString(bytes) : throw new SetupFileException(Damaged);
    }

    /// <summary>The entry's bytes, or null when it holds more than it says (more than <paramref name="limit"/>).</summary>
    private static byte[]? ReadCapped(ZipArchiveEntry entry, long limit)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limit) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static bool IsZip(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            Span<byte> head = stackalloc byte[2];
            return file.Read(head) == 2 && head[0] == (byte)'P' && head[1] == (byte)'K';
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
    }
}
