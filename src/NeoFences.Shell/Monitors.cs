using NeoFences.Core.Layouts;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace NeoFences.Shell;

/// <summary>Enumerates monitors in physical pixels (requires per-monitor DPI awareness, see app.manifest).</summary>
public static class Monitors
{
    private const uint GetDeviceInterfaceName = 0x1; // EDD_GET_DEVICE_INTERFACE_NAME

    public static unsafe IReadOnlyList<MonitorPlacement> Enumerate()
    {
        var handles = new List<HMONITOR>();
        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (monitor, _, _, _) =>
        {
            handles.Add(monitor);
            return true;
        }, 0);

        var placements = new List<MonitorPlacement>();
        var gdiNames = new List<string>();
        foreach (var handle in handles)
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (!PInvoke.GetMonitorInfo(handle, (MONITORINFO*)&info)) continue;

            PInvoke.GetDpiForMonitor(handle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
            var bounds = info.monitorInfo.rcMonitor;
            var work = info.monitorInfo.rcWork;
            var gdiDeviceName = info.szDevice.ToString();
            gdiNames.Add(gdiDeviceName);

            placements.Add(new MonitorPlacement(
                DeviceId: StableDeviceId(gdiDeviceName),
                PixelWidth: bounds.right - bounds.left,
                PixelHeight: bounds.bottom - bounds.top,
                WorkLeftPx: work.left,
                WorkTopPx: work.top,
                WorkWidthPx: work.right - work.left,
                WorkHeightPx: work.bottom - work.top,
                ScalePercent: dpiX == 0 ? 100 : (int)Math.Round(dpiX * 100.0 / 96),
                IsPrimary: (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0));
        }
        // Cloned or mirrored outputs can report the same device path; ids key every layout, so they must be unique (M8a).
        var uniqueIds = DisplayFingerprint.UniqueDeviceIds(placements.Select(placement => placement.DeviceId).ToList(), orderKeys: gdiNames);
        return placements.Select((placement, index) => placement with { DeviceId = uniqueIds[index] }).ToList();
    }

    /// <summary>
    /// The monitor's device interface path (unique per physical monitor and port, stable across reboots).
    /// Falls back to the GDI name ("\\.\DISPLAY1"), which is unique but can change when ports change.
    /// </summary>
    private static unsafe string StableDeviceId(string gdiDeviceName)
    {
        var device = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        if (PInvoke.EnumDisplayDevices(gdiDeviceName, 0, ref device, GetDeviceInterfaceName))
        {
            var interfacePath = device.DeviceID.ToString();
            if (!string.IsNullOrWhiteSpace(interfacePath)) return interfacePath;
        }
        return gdiDeviceName;
    }
}
