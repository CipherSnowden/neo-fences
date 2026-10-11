namespace NeoFences.Shell;

/// <summary>
/// "Click wallpaper to reveal desktop" (M41a, ADR-066): the shell's own "minimize all" moves the windows aside, so the
/// fences and the wallpaper come into view, and its "undo minimize all" brings them back. Not ToggleDesktop: right after a
/// click on the wallpaper the desktop has the focus, and ToggleDesktop then takes the desktop as already shown and does
/// nothing (VM probe, 2026-10-11).
/// </summary>
public static class DesktopReveal
{
    /// <summary>
    /// Shell.Application's MinimizeAll / UndoMinimizeALL, late-bound: CsWin32 has no projection of the shell's automation
    /// object (its IDispatch interfaces live in a type library, not in the Win32 metadata). Call from the UI thread (an STA).
    /// </summary>
    public static bool Set(bool revealed, Action<string> log)
    {
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!);
            shell!.GetType().InvokeMember(revealed ? "MinimizeAll" : "UndoMinimizeALL", System.Reflection.BindingFlags.InvokeMethod,
                binder: null, target: shell, args: null);
            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            log($"reveal desktop: the shell refused ({failure.Message})");
            return false;
        }
        finally
        {
            if (shell is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>Windows' double-click time (ms): a click becomes a reveal only once a second click can no longer follow.</summary>
    public static uint DoubleClickTime => Windows.Win32.PInvoke.GetDoubleClickTime();
}
