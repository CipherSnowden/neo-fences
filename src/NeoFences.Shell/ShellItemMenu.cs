using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>What the user picked in Windows' item menu that NeoFences runs itself instead of the shell.</summary>
/// <summary>Custom: one of the caller's own commands (the Game Library's "Hide from library", M12).</summary>
public enum ItemMenuChoice { None, Rename, Delete, Custom }

/// <summary>
/// Windows' own right-click menu for desktop items (the classic menu: Open, Open with, Send to, Properties, shell
/// extensions; the Windows 11 compact menu is Explorer-private, spec §6). Rename and Delete are handed back so
/// NeoFences renames in place and always recycles (hard rule 1, user decision 2026-10-02).
/// </summary>
public static class ShellItemMenu
{
    private const uint FirstCommandId = 1;
    private const uint LastCommandId = 0x7FFF;
    private const uint CmicMaskUnicode = 0x00004000; // not in the Win32 metadata CsWin32 reads (shobjidl.h)

    /// <summary>The menu being shown, for <see cref="HandleMenuMessage"/> (owner-drawn and lazy submenus like "Send to").</summary>
    private static IContextMenu2? _openMenu;

    /// <summary>Call from the owner window's message hook while a menu may be open; true when the menu consumed it.</summary>
    public static unsafe bool HandleMenuMessage(int message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (_openMenu is null) return false;
        const int WmInitMenuPopup = 0x0117, WmDrawItem = 0x002B, WmMeasureItem = 0x002C, WmMenuChar = 0x0120;
        if (message is not (WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar)) return false;
        try
        {
            if (_openMenu is IContextMenu3 menu3)
            {
                LRESULT handledResult;
                menu3.HandleMenuMsg2((uint)message, (WPARAM)(nuint)wParam, (LPARAM)lParam, &handledResult);
                result = handledResult;
            }
            else
            {
                _openMenu.HandleMenuMsg((uint)message, (WPARAM)(nuint)wParam, (LPARAM)lParam);
            }
            return true;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // CsWin32 turns an extension's failure HRESULT into NotImplementedException, InvalidCastException, … (M3a review C1).
            return false;
        }
    }

    /// <summary>
    /// Shows the menu for these items at a screen point and runs the chosen shell command.
    /// </summary>
    /// <param name="extended">Shift held: the extended menu ("Copy as path", "Open PowerShell here", …).</param>
    /// <returns>Rename/Delete for the caller to run; None when the shell ran the command, the user cancelled, or the menu could not be built.</returns>
    /// <param name="logFailure">Told when the menu could not be built or a command failed (a broken shell extension).</param>
    public static ItemMenuChoice Show(nint ownerHandle, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended, Action<Exception> logFailure) =>
        Show(ownerHandle, itemRefs, screenX, screenY, extended, logFailure, customCommands: [], canRename: true, out _);

    /// <param name="customCommands">Added at the end, after a separator; the chosen one comes back as <paramref name="customCommand"/>.</param>
    /// <param name="canRename">False: the shell's Rename is not offered (a Game Library shortcut, M12).</param>
    public static unsafe ItemMenuChoice Show(nint ownerHandle, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool extended, Action<Exception> logFailure,
        IReadOnlyList<string> customCommands, bool canRename, out int customCommand)
    {
        customCommand = -1;
        var owner = (HWND)ownerHandle;
        HMENU menu = default;
        IContextMenu? contextMenu = null;
        try
        {
            if (itemRefs.Count == 0) return ItemMenuChoice.None;
            contextMenu = (IContextMenu)DesktopNamespace.GetUIObject(owner, itemRefs, typeof(IContextMenu).GUID);

            menu = PInvoke.CreatePopupMenu();
            var flags = PInvoke.CMF_NORMAL | (canRename ? PInvoke.CMF_CANRENAME : 0) | (extended ? PInvoke.CMF_EXTENDEDVERBS : 0);
            var queried = contextMenu.QueryContextMenu(menu, 0, FirstCommandId, LastCommandId, flags);
            if (queried.Failed) // a broken extension can fail the whole menu without throwing (M3a review)
            {
                logFailure(new COMException("QueryContextMenu failed", queried.Value));
                return ItemMenuChoice.None;
            }
            if (customCommands.Count > 0) PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);
            for (var index = 0; index < customCommands.Count; index++)
            {
                fixed (char* text = customCommands[index]) PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, LastCommandId + 1 + (uint)index, text);
            }
            _openMenu = contextMenu as IContextMenu2;

            PInvoke.SetForegroundWindow(owner); // the menu closes on a click elsewhere only if its owner is foreground
            var command = (uint)PInvoke.TrackPopupMenuEx(menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON), screenX, screenY, owner, null).Value;
            // The documented menu dance: when the fence could not become foreground, this lets the next click close it (M3a review).
            PInvoke.PostMessage(owner, PInvoke.WM_NULL, 0, 0);
            _openMenu = null;
            if (command < FirstCommandId) return ItemMenuChoice.None;
            if (command > LastCommandId)
            {
                customCommand = (int)(command - LastCommandId - 1);
                return ItemMenuChoice.Custom;
            }

            var verb = Verb(contextMenu, command - FirstCommandId);
            if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase)) return ItemMenuChoice.Rename;
            if (string.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase)) return ItemMenuChoice.Delete;
            Invoke(contextMenu, command - FirstCommandId, owner, screenX, screenY);
            return ItemMenuChoice.None;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            logFailure(failure); // the item vanished or a shell extension failed: no menu or no command, nothing lost
            return ItemMenuChoice.None;
        }
        finally
        {
            _openMenu = null;
            if (!menu.IsNull) PInvoke.DestroyMenu(menu);
            if (contextMenu is not null) Marshal.ReleaseComObject(contextMenu);
        }
    }

    private static unsafe string? Verb(IContextMenu contextMenu, uint offset)
    {
        const int Length = 64;
        var buffer = stackalloc char[Length];
        try
        {
            contextMenu.GetCommandString(offset, PInvoke.GCS_VERBW, null, (PSTR)(byte*)buffer, Length);
            var verb = new ReadOnlySpan<char>(buffer, Length); // bounded: an extension may fill it without a terminator
            var end = verb.IndexOf('\0');
            return new string(end >= 0 ? verb[..end] : verb);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null; // many commands have no verb (E_NOTIMPL, E_INVALIDARG): they still run through Invoke
        }
    }

    private static unsafe void Invoke(IContextMenu contextMenu, uint offset, HWND owner, int screenX, int screenY)
    {
        var info = new CMINVOKECOMMANDINFOEX
        {
            cbSize = (uint)sizeof(CMINVOKECOMMANDINFOEX),
            fMask = CmicMaskUnicode | PInvoke.CMIC_MASK_PTINVOKE,
            hwnd = owner,
            lpVerb = (PCSTR)(byte*)offset,
            lpVerbW = (PCWSTR)(char*)offset,
            nShow = (int)SHOW_WINDOW_CMD.SW_SHOWNORMAL,
            ptInvoke = new System.Drawing.Point(screenX, screenY),
        };
        PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // what the command opens may come to the front
        contextMenu.InvokeCommand((CMINVOKECOMMANDINFO*)&info);
    }
}
