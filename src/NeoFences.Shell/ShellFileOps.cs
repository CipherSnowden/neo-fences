using System.Runtime.InteropServices;
using NeoFences.Core.Model;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>
/// File operations through Windows' own engine (IFileOperation): its dialogs, progress, conflicts and Undo (Ctrl+Z in
/// Explorer). Deleting always goes to the Recycle Bin (hard rule 1); if Windows cannot recycle an item it asks the user.
/// </summary>
public static class ShellFileOps
{
    /// <summary>
    /// Recycles the items. Items on drives without a Recycle Bin (USB sticks, network shares, optical/RAM drives) are
    /// left alone and returned: there Windows could only delete them for good, which NeoFences never does (hard rule 1,
    /// user decision 2026-10-03). Special items are skipped.
    /// </summary>
    /// <returns>Started: false when nothing could be started (true even if the user cancelled Windows' dialog). Missing:
    /// items that no longer exist, skipped so the others are still recycled (M3a review).</returns>
    public static (bool Started, IReadOnlyList<string> Refused, IReadOnlyList<string> Missing) TryRecycle(nint ownerHandle, IReadOnlyList<string> itemRefs)
    {
        var files = itemRefs.Where(itemRef => !itemRef.StartsWith("::", StringComparison.Ordinal)).ToList();
        var refused = files.Where(file => !HasRecycleBin(file)).ToList();
        var missing = new List<string>();
        var items = new List<IShellItem>();
        foreach (var file in files.Except(refused))
        {
            try { items.Add(Create(file)); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { missing.Add(file); }
        }
        try
        {
            var started = items.Count == 0 || Run(ownerHandle, operation =>
            {
                foreach (var item in items) operation.DeleteItem(item, null);
            });
            return (started, refused, missing);
        }
        finally
        {
            foreach (var item in items) Marshal.ReleaseComObject(item);
        }
    }

    /// <summary>
    /// True when the item's drive keeps a Recycle Bin: local fixed drives (an external USB hard disk counts). Removable
    /// sticks and cards, network shares (mapped or UNC), CD/DVD and RAM drives do not.
    /// </summary>
    public static bool HasRecycleBin(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\", StringComparison.Ordinal)) return false; // UNC share
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (Exception failure) when (failure is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false; // unknown: never risk a permanent delete
        }
    }

    /// <param name="newName">As typed. If Explorer hides this item's extension (shortcuts, or the user's setting), Windows keeps it.</param>
    /// <returns>False also for a name that is not a plain name: "sub\x" or "..\x" would move the file off the Desktop (M3a review I2).</returns>
    public static bool TryRename(nint ownerHandle, string itemRef, string newName) =>
        !itemRef.StartsWith("::", StringComparison.Ordinal)
        && FileNames.IsValidNewName(newName)
        && Run(ownerHandle, operation => operation.RenameItem(Create(itemRef), newName, null));

    private static bool Run(nint ownerHandle, Action<IFileOperation> queue)
    {
        IFileOperation? operation = null;
        try
        {
            operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(FileOperation).GUID)!)!;
            operation.SetOwnerWindow((HWND)ownerHandle);
            // Undo-able, recycle instead of delete, and warn before anything would be deleted permanently.
            operation.SetOperationFlags(FILEOPERATION_FLAGS.FOF_ALLOWUNDO | FILEOPERATION_FLAGS.FOF_WANTNUKEWARNING
                | FILEOPERATION_FLAGS.FOFX_RECYCLEONDELETE | FILEOPERATION_FLAGS.FOFX_ADDUNDORECORD);
            queue(operation);
            operation.PerformOperations();
            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return false; // includes the user cancelling Windows' dialog (HRESULT_FROM_WIN32(ERROR_CANCELLED))
        }
        finally
        {
            if (operation is not null) Marshal.ReleaseComObject(operation);
        }
    }

    private static IShellItem Create(string itemRef)
    {
        PInvoke.SHCreateItemFromParsingName(itemRef, null, out IShellItem item).ThrowOnFailure();
        return item;
    }
}
