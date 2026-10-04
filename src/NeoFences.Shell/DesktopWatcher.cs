using NeoFences.Core.Membership;

namespace NeoFences.Shell;

/// <summary>
/// Watches the user's and the Public Desktop folders. Events arrive on thread-pool threads; marshal them yourself.
/// <see cref="Overflowed"/> means events were lost (buffer overflow, folder gone): re-enumerate and reconcile, and
/// recreate the watcher (.NET stops raising events after any error other than an overflow). It is also raised when an
/// event cannot be read reliably (a rename split across buffers, an item that cannot be checked right now).
/// Special icons (Recycle Bin, ...) are not watched; they are picked up by the next reconcile.
/// </summary>
public sealed class DesktopWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private volatile bool _disposed; // events still in flight after Dispose are dropped (the next start reconciles)

    public event Action<DesktopChange>? Changed;
    /// <summary>The watcher lost events or stopped: re-create it and reconcile.</summary>
    public event Action? Overflowed;
    /// <summary>One event could not be read reliably (a split rename, a locked file): reconcile; the watcher still runs (M8a review).</summary>
    public event Action? ReconcileNeeded;

    /// <param name="log">Called for a folder that cannot be watched; the other folder is still watched.</param>
    public DesktopWatcher(Action<string, Exception> log)
    {
        foreach (var directory in new[] { DesktopItems.UserDesktop, DesktopItems.PublicDesktop }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _watchers.Add(Watch(directory));
            }
            catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
            {
                log(directory, failure);
            }
        }
    }

    private FileSystemWatcher Watch(string directory)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
            IncludeSubdirectories = false,
            InternalBufferSize = 64 * 1024,
        };
        watcher.Created += (_, created) =>
        {
            switch (Visibility(created.FullPath))
            {
                case true: Raise(new DesktopChange.Created(created.FullPath)); break;
                case null: LostTrack(); break; // could not look: let a reconcile decide (M8a)
            }
        };
        watcher.Deleted += (_, deleted) => Raise(new DesktopChange.Deleted(deleted.FullPath));
        watcher.Renamed += (_, renamed) =>
        {
            // A rename split across two notification buffers arrives with one name empty: the pair is lost (M8a).
            if (string.IsNullOrEmpty(renamed.Name) || string.IsNullOrEmpty(renamed.OldName))
            {
                LostTrack();
                return;
            }
            switch (Visibility(renamed.FullPath))
            {
                case true: Raise(new DesktopChange.Renamed(renamed.OldFullPath, renamed.FullPath)); break;
                case false: Raise(new DesktopChange.Deleted(renamed.OldFullPath)); break;
                default: LostTrack(); break;
            }
        };
        // Attributes changed: a file the user (or an installer) hid or unhid. Created/Deleted are no-ops if nothing changed.
        watcher.Changed += (_, changed) =>
        {
            switch (Visibility(changed.FullPath))
            {
                case true: Raise(new DesktopChange.Created(changed.FullPath)); break;
                case false: Raise(new DesktopChange.Deleted(changed.FullPath)); break;
                default: LostTrack(); break;
            }
        };
        watcher.Error += (_, _) => { if (!_disposed) Overflowed?.Invoke(); };
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void Raise(DesktopChange change)
    {
        if (!_disposed) Changed?.Invoke(change);
    }

    private void LostTrack()
    {
        if (!_disposed) ReconcileNeeded?.Invoke();
    }

    /// <summary>
    /// Whether the item shows on the desktop; null when it could not be checked (a file still being written, a sharing
    /// violation): such an item used to be treated as gone (M2b review), now a reconcile decides (M8a).
    /// </summary>
    private static bool? Visibility(string path)
    {
        try
        {
            // GetAttributes, not Exists: Exists swallows every error as "gone", so a locked file looked deleted (M8a review).
            var attributes = File.GetAttributes(path);
            FileSystemInfo entry = attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(path) : new FileInfo(path);
            return DesktopItems.IsVisibleOnDesktop(entry);
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var watcher in _watchers) watcher.Dispose();
    }
}
