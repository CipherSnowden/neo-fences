using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>Now playing's cover click (M41a): the player's window to the front, best effort (a Store app's id names no process).</summary>
public static class PlayerWindows
{
    public static bool BringToFront(string appId, Action<string> log)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(appId[(appId.LastIndexOfAny(['!', '\\', '/']) + 1)..]);
            var processes = Process.GetProcessesByName(name);
            HWND window;
            try
            {
                window = processes.Select(process => (HWND)process.MainWindowHandle).FirstOrDefault(handle => handle != HWND.Null);
            }
            finally
            {
                foreach (var process in processes) process.Dispose(); // M41b (rc.3 leftover)
            }
            if (window == HWND.Null)
            {
                log($"now playing: no window found for {appId}");
                return false;
            }
            if (PInvoke.IsIconic(window)) PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_RESTORE);
            return PInvoke.SetForegroundWindow(window);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            log($"now playing: the player's window could not come to the front ({failure.Message})");
            return false;
        }
    }
}
