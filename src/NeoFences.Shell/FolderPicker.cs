using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>Windows' own "Select folder" dialog (for new Portal fences, M4).</summary>
public static class FolderPicker
{
    /// <returns>The chosen folder's path, or null when cancelled, the choice has no file-system path, or the dialog failed.</returns>
    public static string? TryPick(nint ownerHandle, string title, Action<Exception> logFailure)
    {
        IFileOpenDialog? dialog = null;
        try
        {
            dialog = (IFileOpenDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(FileOpenDialog).GUID)!)!;
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM);
            dialog.SetTitle(title);
            dialog.Show((HWND)ownerHandle); // throws ERROR_CANCELLED when the user cancels
            dialog.GetResult(out var item);
            return ShellItems.FileSystemPath(item);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            if (failure.HResult != unchecked((int)0x800704C7)) logFailure(failure); // HRESULT_FROM_WIN32(ERROR_CANCELLED)
            return null;
        }
        finally
        {
            if (dialog is not null) Marshal.ReleaseComObject(dialog);
        }
    }
}
