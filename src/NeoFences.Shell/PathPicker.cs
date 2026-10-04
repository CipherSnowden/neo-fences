using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls.Dialogs;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace NeoFences.Shell;

/// <summary>Windows' own "Open" dialog for a file or a folder (Add item…, Properties → Browse, Locate…, the library's folders).</summary>
public static class PathPicker
{
    /// <returns>The chosen folder's path, or null when cancelled, the choice has no file-system path, or the dialog failed.</returns>
    /// <param name="startFolder">Where the dialog opens (Locate…: the nearest folder that still exists), or null for Windows' choice.</param>
    public static string? TryPickFolder(nint ownerHandle, string title, Action<Exception> logFailure, string? startFolder = null) =>
        Pick(ownerHandle, title, logFailure, startFolder, pickFolders: true, filters: []);

    /// <param name="filters">(name, patterns) pairs such as ("Pictures", "*.png;*.jpg"); empty: every file.</param>
    public static string? TryPickFile(nint ownerHandle, string title, Action<Exception> logFailure, string? startFolder = null,
        IReadOnlyList<(string Name, string Patterns)>? filters = null) =>
        Pick(ownerHandle, title, logFailure, startFolder, pickFolders: false, filters: filters ?? []);

    private static unsafe string? Pick(nint ownerHandle, string title, Action<Exception> logFailure, string? startFolder, bool pickFolders,
        IReadOnlyList<(string Name, string Patterns)> filters)
    {
        IFileOpenDialog? dialog = null;
        var pinned = new List<GCHandle>();
        try
        {
            dialog = (IFileOpenDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(FileOpenDialog).GUID)!)!;
            dialog.GetOptions(out var options);
            // No dereferencing: a picked shortcut stays the shortcut (its own item), as the user chose it.
            options |= FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM | FILEOPENDIALOGOPTIONS.FOS_NODEREFERENCELINKS;
            if (pickFolders) options |= FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS;
            dialog.SetOptions(options);
            dialog.SetTitle(title);
            if (filters.Count > 0)
            {
                var specs = new COMDLG_FILTERSPEC[filters.Count];
                for (var index = 0; index < filters.Count; index++)
                {
                    var name = GCHandle.Alloc(filters[index].Name + "\0", GCHandleType.Pinned);
                    var patterns = GCHandle.Alloc(filters[index].Patterns + "\0", GCHandleType.Pinned);
                    pinned.Add(name);
                    pinned.Add(patterns);
                    specs[index] = new COMDLG_FILTERSPEC
                    {
                        pszName = (char*)name.AddrOfPinnedObject(),
                        pszSpec = (char*)patterns.AddrOfPinnedObject(),
                    };
                }
                dialog.SetFileTypes(specs);
            }
            if (startFolder is not null && PInvoke.SHCreateItemFromParsingName(startFolder, null, out IShellItem folder).Succeeded)
            {
                dialog.SetFolder(folder);
                Marshal.ReleaseComObject(folder);
            }
            dialog.Show((HWND)ownerHandle); // throws ERROR_CANCELLED when the user cancels
            dialog.GetResult(out var item);
            try { return ShellItems.FileSystemPath(item); }
            finally { Marshal.ReleaseComObject(item); } // M19 R3: released now, not by the finalizer
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            if (failure.HResult != unchecked((int)0x800704C7)) logFailure(failure); // HRESULT_FROM_WIN32(ERROR_CANCELLED)
            return null;
        }
        finally
        {
            foreach (var handle in pinned) handle.Free();
            if (dialog is not null) Marshal.ReleaseComObject(dialog);
        }
    }
}

/// <summary>Windows' "Change Icon" dialog (Properties → Change icon → From a file…): an icon in an .ico, .exe or .dll.</summary>
public static class IconPicker
{
    private const int MaxPath = 260;

    /// <param name="file">The file to show first (the item's own icon file, or its target); null: shell32.dll.</param>
    /// <returns>The chosen file (environment variables expanded) and index, or null when cancelled.</returns>
    public static unsafe (string File, int Index)? TryPick(nint ownerHandle, string? file, int index)
    {
        var buffer = new char[MaxPath];
        var start = file ?? @"%SystemRoot%\System32\shell32.dll";
        if (start.Length >= MaxPath) start = @"%SystemRoot%\System32\shell32.dll";
        start.CopyTo(buffer);
        var chosen = index;
        fixed (char* path = buffer)
        {
            if (PInvoke.PickIconDlg((HWND)ownerHandle, path, MaxPath, &chosen) == 0) return null;
            return (Environment.ExpandEnvironmentVariables(new string(path)), chosen);
        }
    }
}
