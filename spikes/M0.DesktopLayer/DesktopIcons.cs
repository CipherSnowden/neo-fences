using Windows.Win32;
using ComServiceProvider = Windows.Win32.System.Com.IServiceProvider;
using Windows.Win32.UI.Shell;

namespace NeoFences.Spikes.M0;

/// <summary>Spike for spec §4.3: hide/show native desktop icons via the documented folder-view flag (no injection).</summary>
public static class DesktopIcons
{
    public static bool TrySetHidden(bool hidden)
    {
        try
        {
            var folderView = GetDesktopFolderView();
            folderView.SetCurrentFolderFlags((uint)FOLDERFLAGS.FWF_NOICONS, hidden ? (uint)FOLDERFLAGS.FWF_NOICONS : 0u);
            Lab.Log($"desktop icons hidden={hidden} (now reports hidden={IsHidden(folderView)})");
            return true;
        }
        catch (Exception failure)
        {
            Lab.Log($"desktop icons set hidden={hidden} FAILED: {failure.GetType().Name}: {failure.Message}");
            return false;
        }
    }

    public static bool? TryIsHidden()
    {
        try { return IsHidden(GetDesktopFolderView()); }
        catch (Exception failure)
        {
            Lab.Log($"desktop icons query FAILED: {failure.GetType().Name}: {failure.Message}");
            return null;
        }
    }

    private static bool IsHidden(IFolderView2 folderView)
    {
        folderView.GetCurrentFolderFlags(out uint flags);
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
