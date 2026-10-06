using System.IO;
using System.Windows.Media.Imaging;
using NeoFences.Core.Items;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// The icon and name cache on disk (M37, spec §3, ADR-058): PNGs and <c>index.json</c> in <c>cache\icons\</c>, NeoFences'
/// own folder. Used by the icon loader's workers (thread-safe); a file that cannot be read or written only costs a fresh
/// load, never a failure. The decisions (key, stamp, pruning) are <see cref="IconCache"/>'s.
/// </summary>
public sealed class IconDiskCache(string directory)
{
    private readonly Lock _lock = new();
    private readonly Lock _saveLock = new(); // one index write at a time (two workers can settle together)
    private IconCacheIndex _index = new();
    private bool _dirty, _loaded;

    private string IndexPath => Path.Combine(directory, "index.json");

    /// <summary>Reads the index and drops what is too old or too much (start, off the UI thread).</summary>
    public void LoadAndPrune()
    {
        var (index, failure) = IconCacheIndex.Load(IndexPath);
        if (failure is not null) Log.Warning(failure, "icon cache: the index could not be read; the cache starts over");
        var (kept, delete) = IconCache.Prune(index, DateTimeOffset.Now);
        foreach (var file in delete)
        {
            try
            {
                File.Delete(Path.Combine(directory, Path.GetFileName(file))); // NeoFences' own cache file only
            }
            catch (Exception deleteFailure) when (deleteFailure is IOException or UnauthorizedAccessException)
            {
                Log.Warning(deleteFailure, "icon cache: {File} could not be removed", file);
            }
        }
        lock (_lock)
        {
            _index = kept;
            _loaded = true;
            _dirty = delete.Count > 0 || failure is not null;
        }
        if (delete.Count > 0) Log.Information("icon cache: {Count} old entries removed", delete.Count);
    }

    /// <summary>The cached icon and its entry (marked used now), or null.</summary>
    public (BitmapSource Icon, IconCacheEntry Entry)? TryGet(string key)
    {
        IconCacheEntry? entry;
        lock (_lock)
        {
            if (!_loaded || !_index.Entries.TryGetValue(key, out entry)) return null;
        }
        try
        {
            var icon = new BitmapImage();
            icon.BeginInit();
            icon.CacheOption = BitmapCacheOption.OnLoad; // the file is not kept open
            icon.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            icon.UriSource = new Uri(Path.Combine(directory, entry.File));
            icon.EndInit();
            icon.Freeze();
            lock (_lock)
            {
                _index = _index.Touch([key], DateTimeOffset.Now);
                _dirty = true;
            }
            return (icon, entry);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            lock (_lock)
            {
                _index = _index with { Entries = _index.Entries.Where(pair => pair.Key != key).ToDictionary(StringComparer.Ordinal) };
                _dirty = true;
            }
            Log.Debug(failure, "icon cache: {File} unreadable; loaded fresh", entry.File);
            return null;
        }
    }

    /// <summary>Stores a fresh icon (and the display name, when one was asked for) under its key.</summary>
    public void Put(string key, BitmapSource icon, string? name, IconStamp? stamp)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var file = IconCache.FileNameOf(key);
            var path = Path.Combine(directory, file);
            var temp = path + ".tmp";
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(icon));
            using (var stream = File.Create(temp)) encoder.Save(stream);
            File.Move(temp, path, overwrite: true);
            var now = DateTimeOffset.Now;
            var entry = new IconCacheEntry { File = file, Name = name, Stamp = stamp, LastUsed = now, Verified = now, Bytes = new FileInfo(path).Length };
            lock (_lock)
            {
                _index = _index.With(key, entry);
                _dirty = true;
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Debug(failure, "icon cache: {Key} not stored", key); // the icon shows; the next start loads it fresh
        }
    }

    /// <summary>Writes the index when it changed (after the icons settle, and at exit).</summary>
    public void SaveIfChanged()
    {
        lock (_saveLock)
        {
            IconCacheIndex index;
            lock (_lock)
            {
                if (!_dirty) return;
                index = _index;
                _dirty = false;
            }
            try
            {
                index.Save(IndexPath);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Log.Warning(failure, "icon cache: the index could not be written; the icons load fresh next time");
            }
        }
    }
}
