using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace NeoFences.Shell;

/// <summary>
/// Items as children of a shell folder, for item menus, drag data and per-item drop targets. Desktop items go through
/// the desktop folder, which merges the user's and the Public Desktop (a file name or <c>::{CLSID}</c> is a direct
/// child); a Portal's items go through their own folder (M4). One call's items must share that parent.
/// </summary>
internal static class DesktopNamespace
{
    /// <summary>A UI object (IContextMenu, IDataObject, IDropTarget, …) for these items; the caller releases it.</summary>
    /// <exception cref="ArgumentException">The items are not all on the desktop or all in one folder.</exception>
    public static unsafe object GetUIObject(HWND owner, IReadOnlyList<string> itemRefs, Guid interfaceId)
    {
        var parent = ParentFolder(itemRefs);
        var childIds = ChildIds(owner, parent, itemRefs);
        try
        {
            fixed (nint* ids = childIds.ToArray())
            {
                parent.GetUIObjectOf(owner, (uint)childIds.Count, (ITEMIDLIST**)ids, &interfaceId, null, out var uiObject);
                return uiObject;
            }
        }
        finally
        {
            foreach (var childId in childIds) Marshal.FreeCoTaskMem(childId);
        }
    }

    /// <summary>The Desktop folder's own drop target: what Explorer does when something is dropped on the desktop.</summary>
    public static unsafe object DesktopDropTarget(HWND owner)
    {
        PInvoke.SHGetDesktopFolder(out var desktop).ThrowOnFailure();
        return DropTargetOf(owner, desktop);
    }

    /// <summary>A folder's own drop target (a Portal's folder): Windows moves/copies into it, with its dialogs and Undo.</summary>
    public static object FolderDropTarget(HWND owner, string folderPath) => DropTargetOf(owner, FolderObject(folderPath));

    /// <summary>
    /// True for containers that take drops themselves: folders, zips and Recycle Bin. Files (even ones Windows lists as
    /// drop targets, like programs or text files) are not: dropping on them reorders the fence instead (M3b).
    /// </summary>
    public static unsafe bool IsDropContainer(HWND owner, string itemRef)
    {
        try
        {
            var parent = ParentFolder([itemRef]);
            var childIds = ChildIds(owner, parent, [itemRef]);
            try
            {
                var attributes = (uint)(SFGAO_FLAGS.SFGAO_DROPTARGET | SFGAO_FLAGS.SFGAO_FOLDER);
                fixed (nint* ids = childIds.ToArray())
                {
                    parent.GetAttributesOf(1, (ITEMIDLIST**)ids, ref attributes);
                }
                const uint Container = (uint)(SFGAO_FLAGS.SFGAO_DROPTARGET | SFGAO_FLAGS.SFGAO_FOLDER);
                return (attributes & Container) == Container;
            }
            finally
            {
                foreach (var childId in childIds) Marshal.FreeCoTaskMem(childId);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>A special icon, or a file or folder directly on the user's or the Public Desktop.</summary>
    public static bool IsDesktopItem(string itemRef) =>
        itemRef.StartsWith("::", StringComparison.Ordinal) || IsDesktopFolder(Path.GetDirectoryName(itemRef));

    /// <summary>A Public Desktop file whose name also exists on the user's Desktop (both show on the desktop).</summary>
    private static bool IsShadowedPublicItem(string itemRef) =>
        !itemRef.StartsWith("::", StringComparison.Ordinal)
        && string.Equals(Path.GetDirectoryName(itemRef)?.TrimEnd('\\'), DesktopItems.PublicDesktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
        && Path.Exists(Path.Combine(DesktopItems.UserDesktop, Path.GetFileName(itemRef)));

    public static bool IsDesktopFolder(string? folderPath)
    {
        if (folderPath is null) return false;
        var trimmed = folderPath.TrimEnd('\\', '/');
        return string.Equals(trimmed, DesktopItems.UserDesktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, DesktopItems.PublicDesktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    private static unsafe object DropTargetOf(HWND owner, IShellFolder folder)
    {
        var dropTargetId = typeof(Windows.Win32.System.Ole.IDropTarget).GUID;
        folder.CreateViewObject(owner, &dropTargetId, out var dropTarget);
        return dropTarget;
    }

    private static IShellFolder ParentFolder(IReadOnlyList<string> itemRefs)
    {
        if (itemRefs.Count == 0) throw new ArgumentException("No items.", nameof(itemRefs));
        // The desktop folder parses a name to the user's copy: a Public Desktop item with the same name is reached through
        // its own folder, or the menu and drag would act on the user's file (M3a/M3b review).
        if (itemRefs.All(IsDesktopItem) && !itemRefs.Any(IsShadowedPublicItem))
        {
            PInvoke.SHGetDesktopFolder(out var desktop).ThrowOnFailure();
            return desktop;
        }
        var parentPath = Path.GetDirectoryName(itemRefs[0]) ?? throw new ArgumentException("An item has no folder.", nameof(itemRefs));
        if (itemRefs.Any(itemRef => !string.Equals(Path.GetDirectoryName(itemRef), parentPath, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Items from different folders.", nameof(itemRefs));
        return FolderObject(parentPath);
    }

    private static unsafe IShellFolder FolderObject(string folderPath)
    {
        PInvoke.SHCreateItemFromParsingName(folderPath, null, out IShellItem folderItem).ThrowOnFailure();
        var handler = PInvoke.BHID_SFObject;
        var folderId = typeof(IShellFolder).GUID;
        folderItem.BindToHandler(null, &handler, &folderId, out var folder);
        return (IShellFolder)folder;
    }

    private static unsafe List<nint> ChildIds(HWND owner, IShellFolder parent, IReadOnlyList<string> itemRefs)
    {
        var childIds = new List<nint>();
        try
        {
            foreach (var itemRef in itemRefs)
            {
                var childName = itemRef.StartsWith("::", StringComparison.Ordinal) ? itemRef : Path.GetFileName(itemRef);
                ITEMIDLIST* childId;
                uint attributes = 0;
                fixed (char* name = childName)
                {
                    parent.ParseDisplayName(owner, null, name, null, &childId, ref attributes);
                }
                childIds.Add((nint)childId);
            }
            return childIds;
        }
        catch
        {
            foreach (var childId in childIds) Marshal.FreeCoTaskMem(childId);
            throw;
        }
    }
}
