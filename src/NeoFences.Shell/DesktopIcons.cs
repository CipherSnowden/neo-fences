using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.Shell;
using ComServiceProvider = Windows.Win32.System.Com.IServiceProvider;

namespace NeoFences.Shell;

/// <summary>
/// Hides/shows the native desktop icons via the documented folder-view flag (ADR-002, no injection).
/// Every call runs on a thread-pool (MTA) thread with a time limit: COM calls to Explorer fail with
/// RPC_E_CANTCALLOUT_ININPUTSYNCCALL inside a sent message such as WM_QUERYENDSESSION (M2a review C1),
/// and a hung Explorer must not hang the caller.
/// </summary>
public static class DesktopIcons
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(4);

    /// <returns>False when the desktop folder view is unavailable (e.g. Explorer restarting) or did not answer in time.</returns>
    public static bool TrySetHidden(bool hidden) => RunOffThread(() =>
    {
        GetDesktopFolderView().SetCurrentFolderFlags((uint)FOLDERFLAGS.FWF_NOICONS, hidden ? (uint)FOLDERFLAGS.FWF_NOICONS : 0u);
        return IsHidden() == hidden;
    });

    /// <summary>
    /// Shows the icons, retrying while Explorer is busy or restarting until <paramref name="giveUpAfter"/>. If Explorer
    /// never answers, writes Explorer's own persisted setting (HideIcons = 0), which it reads when it next starts, so the
    /// icons are visible after the next Explorer start or sign-in at the latest (M7 review I2: the uninstall hook is the
    /// last code that can restore them).
    /// </summary>
    /// <returns>True when Explorer confirmed the icons are shown now.</returns>
    public static bool ShowWithRetry(TimeSpan giveUpAfter, Action<string> log)
    {
        var deadline = DateTime.UtcNow + giveUpAfter;
        for (var attempt = 1; ; attempt++)
        {
            if (TrySetHidden(false))
            {
                log($"desktop icons shown (attempt {attempt})");
                return true;
            }
            if (DateTime.UtcNow >= deadline) break;
            Thread.Sleep(500);
        }
        try
        {
            using var advanced = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            advanced.SetValue("HideIcons", 0, Microsoft.Win32.RegistryValueKind.DWord);
            log("desktop icons: Explorer did not answer; HideIcons set to 0 for its next start");
        }
        catch (Exception failure) when (failure is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            log($"FAILED to show desktop icons ({failure.Message})");
        }
        return false;
    }

    /// <returns>Null when the desktop folder view is unavailable or did not answer in time.</returns>
    public static bool? TryIsHidden() => RunOffThread(() => (bool?)IsHidden());

    /// <summary>
    /// A visible desktop icon is at this screen point, by Explorer's own icon positions and spacing. UI Automation cannot
    /// tell on every desktop: behind a live web wallpaper it reports the wallpaper's page, never the icon (M18 live check).
    /// </summary>
    /// <returns>Null when the desktop folder view is unavailable or did not answer in time.</returns>
    public static bool? TryIsIconAt(int screenX, int screenY) => RunOffThread(() => (bool?)IsIconAt(screenX, screenY));

    private static unsafe bool IsIconAt(int screenX, int screenY)
    {
        if (IsHidden()) return false;
        var view = GetDesktopFolderView();
        // Item positions are in the icon list's client coordinates (the whole desktop, from the primary monitor's corner).
        // Found by class: IShellView.GetWindow is an input-synchronous call Explorer refuses from here.
        var list = DesktopListView();
        if (list.IsNull) return false;
        var point = new System.Drawing.Point(screenX, screenY);
        PInvoke.ScreenToClient(list, ref point);
        System.Drawing.Point spacing;
        view.GetSpacing(&spacing);
        view.ItemCount(_SVGIO.SVGIO_ALLVIEW, out var count);
        for (var index = 0; index < count; index++)
        {
            ITEMIDLIST* item = null;
            view.Item(index, &item);
            try
            {
                System.Drawing.Point position;
                view.GetItemPosition(item, &position);
                if (point.X >= position.X && point.X < position.X + spacing.X && point.Y >= position.Y && point.Y < position.Y + spacing.Y) return true;
            }
            finally
            {
                PInvoke.CoTaskMemFree(item);
            }
        }
        return false;
    }

    private static T? RunOffThread<T>(Func<T> comCall)
    {
        var call = Task.Run(() =>
        {
            try
            {
                return comCall();
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // Any COM HRESULT may surface here (COMException, UnauthorizedAccessException, ArgumentException, ...):
                // a failed icon call degrades this one feature, it never crashes the caller (CLAUDE.md rule 7).
                return default;
            }
        });
        return call.Wait(CallTimeout) ? call.Result : default;
    }

    /// <summary>The desktop's icon list: under Progman (24H2+) or under the WorkerW that holds SHELLDLL_DefView (before).</summary>
    private static HWND DesktopListView()
    {
        var host = PInvoke.FindWindow("Progman", null);
        var view = PInvoke.FindWindowEx(host, HWND.Null, "SHELLDLL_DefView", null);
        for (var worker = HWND.Null; view.IsNull;)
        {
            worker = PInvoke.FindWindowEx(HWND.Null, worker, "WorkerW", null);
            if (worker.IsNull) return HWND.Null;
            view = PInvoke.FindWindowEx(worker, HWND.Null, "SHELLDLL_DefView", null);
        }
        return PInvoke.FindWindowEx(view, HWND.Null, "SysListView32", null);
    }

    private static bool IsHidden()
    {
        GetDesktopFolderView().GetCurrentFolderFlags(out uint flags);
        return (flags & (uint)FOLDERFLAGS.FWF_NOICONS) != 0;
    }

    // Raymond Chen, "Manipulating the positions of desktop icons": ShellWindows -> desktop -> top-level browser -> active view.
    private static IFolderView2 GetDesktopFolderView()
    {
        var shellWindows = (IShellWindows)new ShellWindows();
        object location = 0;   // CSIDL_DESKTOP as VT_I4
        object root = null!;   // VT_EMPTY
        var dispatch = shellWindows.FindWindowSW(location, root, ShellWindowTypeConstants.SWC_DESKTOP, out _,
            ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);
        var serviceProvider = (ComServiceProvider)dispatch;
        serviceProvider.QueryService(PInvoke.SID_STopLevelBrowser, out IShellBrowser browser);
        browser.QueryActiveShellView(out IShellView view);
        return (IFolderView2)view;
    }
}
