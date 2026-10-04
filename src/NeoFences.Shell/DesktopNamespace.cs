using System.Runtime.InteropServices;
using NeoFences.Core.Items;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace NeoFences.Shell;

/// <summary>
/// Items as children of a shell folder, for Windows' item menu (Shift+right-click, the Game Library). Desktop items go
/// through the desktop folder, which merges the user's and the Public Desktop (a file name or <c>::{CLSID}</c> is a direct
/// child); others through their own folder. One call's items must share that parent.
/// </summary>
internal static class DesktopNamespace
{
    /// <summary>A UI object (IContextMenu, …) for these items; the caller releases it.</summary>
    /// <exception cref="ArgumentException">The items are not all on the desktop or all in one folder.</exception>
    public static unsafe object GetUIObject(HWND owner, IReadOnlyList<string> itemRefs, Guid interfaceId)
    {
        var parent = ParentFolder(itemRefs);
        try
        {
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
        finally
        {
            Marshal.ReleaseComObject(parent); // M19 R3: the desktop or parent folder object, released now
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

    private static IShellFolder ParentFolder(IReadOnlyList<string> itemRefs)
    {
        if (itemRefs.Count == 0) throw new ArgumentException("No items.", nameof(itemRefs));
        // Apps are children of Start's All apps, whatever their id holds ("{GUID}\folder\x.exe" is not a path; M20).
        if (itemRefs.All(ItemKinds.IsApp)) return FolderObject("shell:AppsFolder");
        if (itemRefs.Any(ItemKinds.IsApp)) throw new ArgumentException("Apps mixed with other items.", nameof(itemRefs));
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
        try
        {
            var handler = PInvoke.BHID_SFObject;
            var folderId = typeof(IShellFolder).GUID;
            folderItem.BindToHandler(null, &handler, &folderId, out var folder);
            return (IShellFolder)folder;
        }
        finally
        {
            Marshal.ReleaseComObject(folderItem); // M19 R3
        }
    }

    private static unsafe List<nint> ChildIds(HWND owner, IShellFolder parent, IReadOnlyList<string> itemRefs)
    {
        var childIds = new List<nint>();
        try
        {
            foreach (var itemRef in itemRefs)
            {
                var childName = ItemKinds.AppIdOf(itemRef) ?? (itemRef.StartsWith("::", StringComparison.Ordinal) ? itemRef : Path.GetFileName(itemRef));
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
