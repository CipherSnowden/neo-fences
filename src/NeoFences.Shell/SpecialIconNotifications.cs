using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace NeoFences.Shell;

/// <summary>
/// Tells NeoFences when a special desktop icon changes (M8c, M2b carry-over): the Recycle Bin turns full or empty (a
/// shell change notice to a window), or the user shows or hides an icon in "Desktop icon settings" (a registry change).
/// Either may arrive in bursts; the caller debounces. A part that cannot be registered is logged and left out.
/// </summary>
public sealed class SpecialIconNotifications : IDisposable
{
    /// <summary>Posted to the owner window for a Recycle Bin change (handle it like any window message).</summary>
    public static readonly uint Message = PInvoke.WM_APP + 3;

    private const string HideDesktopIconsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    private readonly uint _recycleBinRegistration; // the Recycle Bin's own item and contents
    private readonly uint _imageRegistration;      // icon image updates (its full/empty icon is one); rare, unlike item changes
    private readonly RegistryKey? _key;
    private readonly AutoResetEvent _keyChanged = new(false);
    private readonly RegisteredWaitHandle? _keyWait;
    private readonly Action _settingsChanged;
    private readonly Action<string, Exception> _log;
    private readonly object _gate = new();
    private bool _keyFailed;
    private readonly nint _rootPidl = ZeroedPidl(); // the empty ID list: the namespace root (kept alive while registered)

    private static nint ZeroedPidl()
    {
        var pidl = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(2);
        System.Runtime.InteropServices.Marshal.WriteInt16(pidl, 0);
        return pidl;
    }
    private volatile bool _disposed;

    /// <param name="settingsChanged">"Desktop icon settings" changed; called on a thread-pool thread.</param>
    public unsafe SpecialIconNotifications(nint ownerHandle, Action settingsChanged, Action<string, Exception> log)
    {
        _settingsChanged = settingsChanged;
        _log = log;
        try
        {
            // The Recycle Bin's own changes, and icon image updates anywhere (its full/empty icon is one).
            PInvoke.SHGetKnownFolderIDList(PInvoke.FOLDERID_RecycleBinFolder, 0, null, out var recycleBin).ThrowOnFailure();
            try
            {
                // Items moving in or out of the bin arrive as file-system events of its folders: interrupt level too, recursive.
                _recycleBinRegistration = Register(ownerHandle, recycleBin, recursive: true, SHCNE_ID.SHCNE_ALLEVENTS); // only the bin's own
                _imageRegistration = Register(ownerHandle, (ITEMIDLIST*)_rootPidl, recursive: true, SHCNE_ID.SHCNE_UPDATEIMAGE);
                if (_recycleBinRegistration == 0 || _imageRegistration == 0) log("Recycle Bin notices", new InvalidOperationException("SHChangeNotifyRegister failed"));
            }
            finally
            {
                PInvoke.CoTaskMemFree(recycleBin);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            log("Recycle Bin notices", failure);
        }
        try
        {
            // Windows creates the key on the first change; creating it empty changes nothing shown.
            _key = Registry.CurrentUser.CreateSubKey(HideDesktopIconsKey, writable: false);
            ArmKeyNotification();
            _keyWait = ThreadPool.RegisterWaitForSingleObject(_keyChanged, (_, _) => OnKeyChanged(), null, Timeout.Infinite, executeOnlyOnce: false);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            log("desktop icon settings notices", failure);
        }
    }

    /// <summary>Classic delivery (no SHCNRF_NewDelivery): the message needs no SHChangeNotification_Lock to be freed.</summary>
    private static unsafe uint Register(nint ownerHandle, ITEMIDLIST* folder, bool recursive, SHCNE_ID events)
    {
        var entry = new SHChangeNotifyEntry { pidl = folder, fRecursive = recursive };
        return PInvoke.SHChangeNotifyRegister((HWND)ownerHandle, SHCNRF_SOURCE.SHCNRF_ShellLevel | SHCNRF_SOURCE.SHCNRF_InterruptLevel,
            (int)events, Message, 1, &entry);
    }

    /// <summary>True for this class's window message; the message's content is not needed (the caller re-reads).</summary>
    public static bool IsRecycleBinNotice(int message) => (uint)message == Message;

    private void OnKeyChanged()
    {
        // On a thread-pool thread: nothing may escape (hard rule 7). The lock lets Dispose wait for a running callback.
        lock (_gate)
        {
            if (_disposed || _keyFailed) return;
            try
            {
                ArmKeyNotification(); // one notice per registration: register again first, so no change is missed
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                _keyFailed = true; // the key was deleted (a registry cleaner): stop watching, say so once (M8c review I1)
                _log("desktop icon settings notices", failure);
            }
        }
        _settingsChanged();
    }

    private void ArmKeyNotification()
    {
        // Thread agnostic: the registration survives the thread-pool thread that made it (Windows 8+).
        var result = PInvoke.RegNotifyChangeKeyValue(_key!.Handle, false,
            REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC,
            _keyChanged.SafeWaitHandle, true);
        if (result != WIN32_ERROR.ERROR_SUCCESS) throw new System.ComponentModel.Win32Exception((int)result);
    }

    public void Dispose()
    {
        lock (_gate) // a registry callback still running finishes first; later ones see _disposed
        {
            if (_disposed) return;
            _disposed = true;
            if (_recycleBinRegistration != 0) PInvoke.SHChangeNotifyDeregister(_recycleBinRegistration);
            if (_imageRegistration != 0) PInvoke.SHChangeNotifyDeregister(_imageRegistration);
            _keyWait?.Unregister(null);
            _key?.Dispose();
            _keyChanged.Dispose();
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(_rootPidl);
        }
    }
}
