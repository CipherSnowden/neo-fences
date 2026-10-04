using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// "Safely Remove" / Eject for a drive NeoFences watches (M8d; item targets, the library): any open handle on a drive vetoes
/// its removal, and a watcher keeps two (the folder and its parent). Windows asks every window registered for a handle on
/// the drive first (DBT_DEVICEQUERYREMOVE to the owner window); NeoFences then closes everything it holds there.
/// </summary>
public sealed class DeviceRemovalNotice : IDisposable
{
    public const int WmDeviceChange = 0x0219;
    public const int QueryRemove = 0x8001; // DBT_DEVICEQUERYREMOVE
    public const int RemoveComplete = 0x8004; // DBT_DEVICEREMOVECOMPLETE: also the only notice of a stick pulled without asking

    private readonly SafeFileHandle _folder;
    private readonly HDEVNOTIFY _registration;

    /// <summary>The folder handle Windows names in its notice.</summary>
    public nint Handle { get; }

    private DeviceRemovalNotice(SafeFileHandle folder, HDEVNOTIFY registration)
    {
        _folder = folder;
        _registration = registration;
        Handle = folder.DangerousGetHandle();
    }

    /// <summary>
    /// Registers for removal requests of the drive holding this folder. Only local drives can be removed this way
    /// (USB sticks and disks, card readers, optical drives); null for network folders or when Windows refuses.
    /// </summary>
    public static unsafe DeviceRemovalNotice? TryRegister(nint ownerHandle, string folderPath, Action<Exception> logFailure)
    {
        try
        {
            var root = Path.GetPathRoot(folderPath);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\", StringComparison.Ordinal)) return null; // UNC share
            if (new DriveInfo(root).DriveType is not (DriveType.Removable or DriveType.Fixed or DriveType.CDRom)) return null;
            // Attributes only, sharing everything: this handle never gets in the way of anything but the removal itself.
            var folder = PInvoke.CreateFile(folderPath, 0x80, // FILE_READ_ATTRIBUTES
                FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
                null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS, null);
            if (folder.IsInvalid) throw new System.ComponentModel.Win32Exception();
            var filter = new DEV_BROADCAST_HANDLE
            {
                dbch_size = (uint)sizeof(DEV_BROADCAST_HANDLE),
                dbch_devicetype = (uint)DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_HANDLE,
                dbch_handle = (HANDLE)folder.DangerousGetHandle(),
            };
            var registration = PInvoke.RegisterDeviceNotification((HANDLE)ownerHandle, &filter, REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE);
            if (registration.IsNull)
            {
                var failure = new System.ComponentModel.Win32Exception();
                folder.Dispose();
                throw failure;
            }
            return new DeviceRemovalNotice(folder, registration);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            logFailure(failure); // the drive then just cannot be removed safely while it is watched (as before)
            return null;
        }
    }

    /// <summary>The handle a WM_DEVICECHANGE notice is about, or 0 when it is not a handle notice.</summary>
    public static unsafe nint HandleOf(nint lParam)
    {
        if (lParam == 0) return 0;
        var header = (DEV_BROADCAST_HDR*)lParam;
        return header->dbch_devicetype == DEV_BROADCAST_HDR_DEVICE_TYPE.DBT_DEVTYP_HANDLE ? ((DEV_BROADCAST_HANDLE*)lParam)->dbch_handle : 0;
    }

    private bool _disposed;

    /// <summary>Safe to call twice (a lister can let go of an in-flight notice and then dispose it again, M13b).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PInvoke.UnregisterDeviceNotification(_registration);
        _folder.Dispose();
    }
}
