namespace NeoFences.Core.Layouts;

/// <summary>A monitor as NeoFences.Shell reports it. Work-area sizes are in DIPs.</summary>
/// <param name="DeviceId">Stable per physical monitor (Shell derives it from the device path).</param>
public sealed record DisplayMonitor(
    string DeviceId,
    int PixelWidth,
    int PixelHeight,
    int ScalePercent,
    double WorkWidth,
    double WorkHeight,
    bool IsPrimary);

/// <summary>Identifies a display configuration: which monitors, at which resolution and scale.</summary>
public static class DisplayFingerprint
{
    /// <example>"2mon:DELL-3840x2160@150%+LG-1920x1080@100%"</example>
    public static string Of(IReadOnlyCollection<DisplayMonitor> monitors) =>
        $"{monitors.Count}mon:" + string.Join("+", monitors
            .OrderBy(monitor => monitor.DeviceId, StringComparer.Ordinal)
            .Select(monitor => $"{monitor.DeviceId}-{monitor.PixelWidth}x{monitor.PixelHeight}@{monitor.ScalePercent}%"));

    /// <summary>
    /// Device ids must be unique (they key every layout), but cloned or mirrored outputs can report the same device
    /// path: the second and later copies get "#2", "#3"… (M8a). They are numbered in <paramref name="orderKeys"/> order
    /// (the GDI device name), not enumeration order, which can change between boots (M8a review); ids keep input order.
    /// </summary>
    public static IReadOnlyList<string> UniqueDeviceIds(IReadOnlyList<string> deviceIds, IReadOnlyList<string>? orderKeys = null)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var unique = new string[deviceIds.Count];
        foreach (var index in Enumerable.Range(0, deviceIds.Count).OrderBy(index => orderKeys?[index] ?? "", StringComparer.Ordinal).ThenBy(index => index))
        {
            var deviceId = deviceIds[index];
            seen[deviceId] = seen.TryGetValue(deviceId, out var count) ? count + 1 : 1;
            unique[index] = seen[deviceId] == 1 ? deviceId : $"{deviceId}#{seen[deviceId]}";
        }
        return unique;
    }
}
