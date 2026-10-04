using System.Runtime.InteropServices;
using NeoFences.Core.Items;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>An entry of Start's All apps: its AppUserModelID and the name Start shows.</summary>
public sealed record InstalledApp(string AppId, string Name);

/// <summary>
/// Start's All apps (M19, ADR-042): Store apps and programs alike, as <c>shell:AppsFolder\&lt;id&gt;</c> items. Listing and
/// checking ask the shell; neither touches an app.
/// </summary>
public static class AppList
{
    /// <summary>The AppsFolder's parsing name: an app dragged from Start is one of its children.</summary>
    private const string AppsFolderRef = "::{4234D49B-0245-4DF3-B780-3893943456E1}";

    /// <summary>Every app, A–Z by name. Call on an STA thread that is not the UI thread (~0.3 s for ~200 apps).</summary>
    public static unsafe IReadOnlyList<InstalledApp> Enumerate()
    {
        var apps = new List<InstalledApp>();
        IShellItem? folder = null;
        IEnumShellItems? children = null;
        try
        {
            var folderId = PInvoke.FOLDERID_AppsFolder;
            var itemId = typeof(IShellItem).GUID;
            PInvoke.SHGetKnownFolderItem(&folderId, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, default, &itemId, out var created).ThrowOnFailure();
            folder = (IShellItem)created;
            var handler = PInvoke.BHID_EnumItems;
            var enumId = typeof(IEnumShellItems).GUID;
            folder.BindToHandler(null, &handler, &enumId, out var enumerator);
            children = (IEnumShellItems)enumerator;
            var batch = new IShellItem[1];
            while (true)
            {
                uint fetched;
                children.Next(1, batch, &fetched);
                if (fetched == 0) break;
                var child = batch[0];
                try
                {
                    if (Name(child, SIGDN.SIGDN_PARENTRELATIVEPARSING) is { Length: > 0 } appId && Name(child, SIGDN.SIGDN_NORMALDISPLAY) is { Length: > 0 } name)
                        apps.Add(new InstalledApp(appId, name));
                }
                finally
                {
                    Marshal.ReleaseComObject(child);
                }
            }
        }
        finally
        {
            if (children is not null) Marshal.ReleaseComObject(children);
            if (folder is not null) Marshal.ReleaseComObject(folder);
        }
        return [.. apps.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Windows still knows this app (an uninstalled one no longer parses). Call off the UI thread.</summary>
    public static bool Exists(string appTarget)
    {
        try
        {
            PInvoke.SHCreateItemFromParsingName(appTarget, null, out IShellItem item).ThrowOnFailure();
            Marshal.ReleaseComObject(item);
            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>An item dragged from Start's All apps: its <c>shell:AppsFolder\&lt;id&gt;</c> target; null for anything else.</summary>
    internal static string? AppTargetOf(IShellItem item)
    {
        IShellItem? parent = null;
        try
        {
            item.GetParent(out parent);
            return string.Equals(Name(parent, SIGDN.SIGDN_DESKTOPABSOLUTEPARSING), AppsFolderRef, StringComparison.OrdinalIgnoreCase)
                   && Name(item, SIGDN.SIGDN_PARENTRELATIVEPARSING) is { Length: > 0 } appId
                ? ItemKinds.AppTarget(appId) : null;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null; // no parent (the desktop itself), or a handler that refuses
        }
        finally
        {
            if (parent is not null) Marshal.ReleaseComObject(parent);
        }
    }

    private static unsafe string? Name(IShellItem item, SIGDN form)
    {
        try
        {
            item.GetDisplayName(form, out var name);
            try { return name.ToString(); }
            finally { Marshal.FreeCoTaskMem((nint)name.Value); }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
    }
}
