using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// The NeoFences icon in the notification area (spec §6, M6a). Its clicks arrive at <c>windowHandle</c> as
/// <see cref="CallbackMessage"/>. Explorer forgets every tray icon when it restarts: call <see cref="Show"/> again on
/// TaskbarCreated.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    public static readonly uint CallbackMessage = PInvoke.WM_APP + 2;
    private const uint IconId = 1;
    private const int ApplicationIconResource = 32512; // .NET's ApplicationIcon resource id, also IDI_APPLICATION
    private const uint KeySelect = PInvoke.NIN_SELECT | 1; // NIN_KEYSELECT (NINF_KEY), not in the metadata

    private readonly nint _windowHandle;
    private readonly Action<string> _log;
    private readonly HICON _icon;
    private bool _iconShared; // Windows' own icon (LR_SHARED): never destroyed by us
    private string _tooltip;

    public TrayIcon(nint windowHandle, string tooltip, Action<string> log)
    {
        _windowHandle = windowHandle;
        _tooltip = tooltip;
        _log = log;
        _icon = LoadSmallIcon();
    }

    /// <summary>
    /// Adds the icon (again: after an Explorer restart). Never throws; a missing icon only loses the tray.
    /// A busy Explorer (sign-in autostart) can answer ERROR_TIMEOUT even when it did add the icon: then the modify
    /// succeeds instead, and the version must still be set, or clicks arrive in the old format (M6a review I2).
    /// </summary>
    /// <returns>False when Explorer took neither the add nor the modify: try again shortly.</returns>
    public bool Show()
    {
        var data = Data();
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data) && !PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data))
        {
            _log("tray: icon could not be added yet");
            return false;
        }
        data.uVersion = PInvoke.NOTIFYICON_VERSION_4; // clicks as NIN_SELECT / WM_CONTEXTMENU with the point in wParam
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
    }

    /// <summary>A short notice from the tray icon (Windows shows it as a toast), e.g. "Snapshot saved" (M10).</summary>
    /// <param name="warning">A failure ("not saved", "not restored"): Windows' warning icon instead of the info one (M13c).</param>
    public void ShowBalloon(string title, string text, bool warning = false)
    {
        var data = Data();
        data.uFlags |= NOTIFY_ICON_DATA_FLAGS.NIF_INFO;
        data.dwInfoFlags = warning ? NOTIFY_ICON_INFOTIP_FLAGS.NIIF_WARNING : NOTIFY_ICON_INFOTIP_FLAGS.NIIF_INFO;
        var titleText = title.AsSpan(0, Math.Min(title.Length, 63));
        titleText.CopyTo(data.szInfoTitle.AsSpan());
        data.szInfoTitle[titleText.Length] = '\0';
        var body = text.AsSpan(0, Math.Min(text.Length, 255));
        body.CopyTo(data.szInfo.AsSpan());
        data.szInfo[body.Length] = '\0';
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data)) _log("tray: notice could not be shown");
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        var data = Data();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
    }

    /// <summary>
    /// A click on the icon (left, right or keyboard) asks for the menu (user choice 2026-10-03: left-click opens it too).
    /// Reads a <see cref="CallbackMessage"/>: the event in lParam's low word, the screen point in wParam.
    /// </summary>
    public static bool IsMenuRequest(nint wParam, nint lParam, out int screenX, out int screenY)
    {
        var trayEvent = (uint)(lParam & 0xFFFF);
        screenX = (short)(wParam & 0xFFFF);
        screenY = (short)((wParam >> 16) & 0xFFFF);
        return trayEvent is PInvoke.NIN_SELECT or KeySelect or PInvoke.WM_CONTEXTMENU;
    }

    private unsafe NOTIFYICONDATAW Data()
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = (HWND)_windowHandle,
            uID = IconId,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        var tip = _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127));
        tip.CopyTo(data.szTip.AsSpan());
        data.szTip[tip.Length] = '\0';
        return data;
    }

    /// <summary>
    /// The exe's own icon at the tray's size for the system DPI; Windows' default app icon if it has none. LoadImage, not
    /// LoadIconMetric: that one lives in comctl32 v6, which a WPF app does not load (it crashed the M6a prototype).
    /// </summary>
    private unsafe HICON LoadSmallIcon()
    {
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem());
        var module = PInvoke.GetModuleHandle((PCWSTR)null);
        var icon = PInvoke.LoadImage((HINSTANCE)(nint)module, (PCWSTR)(char*)ApplicationIconResource, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, 0);
        if (!icon.IsNull) return (HICON)(nint)icon;
        _log("tray: the exe has no icon; using Windows' default");
        _iconShared = true;
        return (HICON)(nint)PInvoke.LoadImage(HINSTANCE.Null, (PCWSTR)(char*)ApplicationIconResource, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_SHARED);
    }

    public void Dispose()
    {
        var data = Data();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
        if (!_icon.IsNull && !_iconShared) PInvoke.DestroyIcon(_icon);
    }
}

/// <summary>One tray-menu entry; <see cref="Id"/> 0 is a separator.</summary>
public sealed record TrayMenuItem(int Id, string Text = "", bool Checked = false, bool Enabled = true)
{
    public static readonly TrayMenuItem Separator = new(0);

    /// <summary>A submenu ("Restore snapshot ▸", M10); its own <see cref="Id"/> is never returned.</summary>
    public IReadOnlyList<TrayMenuItem>? Children { get; init; }
}

/// <summary>
/// The tray menu (M35, ADR-056): NeoFences draws it like its other menus; Windows' part is only the documented dance —
/// NeoFences comes to the foreground first, so a click anywhere else closes the menu.
/// </summary>
public static class TrayMenu
{
    public static void BringForward(nint ownerHandle) => PInvoke.SetForegroundWindow((HWND)ownerHandle);
}

/// <summary>Session lock/unlock notices (WM_WTSSESSION_CHANGE) for a window: the mouse hook is re-installed on unlock.</summary>
public static class SessionNotifications
{
    public const int WmSessionChange = (int)PInvoke.WM_WTSSESSION_CHANGE;

    public static bool IsUnlock(nint wParam) => (uint)wParam == PInvoke.WTS_SESSION_UNLOCK;

    public static bool Register(nint windowHandle) => PInvoke.WTSRegisterSessionNotification((HWND)windowHandle, PInvoke.NOTIFY_FOR_THIS_SESSION);

    public static void Unregister(nint windowHandle) => PInvoke.WTSUnRegisterSessionNotification((HWND)windowHandle);
}
