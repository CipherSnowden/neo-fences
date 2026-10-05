using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Performance;
using Windows.Win32.System.SystemInformation;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace NeoFences.Shell;

/// <summary>One System stats reading (M25): percents 0–100, or null when Windows did not give that value.</summary>
public sealed record StatsSample(double? Cpu, double? Ram, double? Gpu, double? DiskC);

/// <summary>
/// The System stats widget's readings (M25, spec 2026-10-05-widgets-design §3), from Windows' own counters: CPU from the
/// difference between two <c>GetSystemTimes</c> readings, RAM from <c>GlobalMemoryStatusEx</c>, C: from
/// <c>GetDiskFreeSpaceEx</c>, GPU from the PDH counter of every 3D engine (summed, capped at 100). One PDH query is kept.
/// Not thread-safe: one sample at a time (the host keeps at most one in flight). Every failure leaves that value null.
/// </summary>
public sealed class SystemStats : IDisposable
{
    private const string GpuCounter = @"\GPU Engine(*engtype_3D)\Utilization Percentage";
    private readonly Action<string, Exception?> _logOnce;
    private (ulong Idle, ulong Busy)? _lastCpu;
    private PdhCloseQuerySafeHandle? _query;
    private PDH_HCOUNTER _gpu;
    private bool _gpuFailed;

    /// <param name="logOnce">Told the first time a value cannot be read (the host logs each name once).</param>
    public SystemStats(Action<string, Exception?> logOnce) => _logOnce = logOnce;

    public StatsSample Sample() => new(Cpu(), Ram(), Gpu(), DiskC());

    private double? Cpu()
    {
        if (!PInvoke.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _logOnce("CPU", new System.ComponentModel.Win32Exception());
            return null;
        }
        // Kernel time includes idle time: busy = kernel + user − idle.
        var idleTicks = Ticks(idle);
        var busyTicks = Ticks(kernel) + Ticks(user) - idleTicks;
        var previous = _lastCpu;
        _lastCpu = (idleTicks, busyTicks);
        if (previous is not { } last) return null; // the first reading has nothing to compare with
        var busy = busyTicks - last.Busy;
        var total = busy + (idleTicks - last.Idle);
        return total == 0 ? 0 : 100.0 * busy / total;
    }

    private static ulong Ticks(FILETIME time) => ((ulong)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;

    private double? Ram()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (PInvoke.GlobalMemoryStatusEx(ref status)) return status.dwMemoryLoad;
        _logOnce("RAM", new System.ComponentModel.Win32Exception());
        return null;
    }

    private double? DiskC()
    {
        if (PInvoke.GetDiskFreeSpaceEx(@"C:\", out _, out var total, out var free) && total > 0) return 100.0 * (total - free) / total;
        _logOnce("C: drive", new System.ComponentModel.Win32Exception());
        return null;
    }

    private unsafe double? Gpu()
    {
        if (_gpuFailed) return null;
        try
        {
            if (_query is null)
            {
                Check(PInvoke.PdhOpenQuery(null, 0, out var query), "open");
                _query = query;
                Check(PInvoke.PdhAddEnglishCounter(_query, GpuCounter, 0, out _gpu), "add");
                Check(PInvoke.PdhCollectQueryData(new PDH_HQUERY(_query.DangerousGetHandle())), "first collect");
                return null; // a rate needs two collections
            }
            Check(PInvoke.PdhCollectQueryData(new PDH_HQUERY(_query.DangerousGetHandle())), "collect");
            uint size = 0, count = 0;
            var status = PInvoke.PdhGetFormattedCounterArray(_gpu, PDH_FMT.PDH_FMT_DOUBLE, &size, &count, null);
            if (status != (uint)PInvoke.PDH_MORE_DATA || size == 0) return status == 0 ? 0 : null; // no 3D engines: 0 %
            var buffer = new byte[size];
            fixed (byte* bytes = buffer)
            {
                Check(PInvoke.PdhGetFormattedCounterArray(_gpu, PDH_FMT.PDH_FMT_DOUBLE, &size, &count, (PDH_FMT_COUNTERVALUE_ITEM_W*)bytes), "read");
                var items = (PDH_FMT_COUNTERVALUE_ITEM_W*)bytes;
                double sum = 0;
                for (var index = 0; index < count; index++)
                {
                    if (items[index].FmtValue.CStatus == 0) sum += items[index].FmtValue.Anonymous.doubleValue;
                }
                return Math.Min(100, sum);
            }
        }
        catch (Exception failure) when (failure is InvalidOperationException or ExternalException)
        {
            _gpuFailed = true; // no GPU counters on this PC (or a driver without them): "—" from now on
            _logOnce("GPU", failure);
            return null;
        }
    }

    private static void Check(uint status, string what)
    {
        if (status != 0) throw new InvalidOperationException($"PDH {what} failed: 0x{status:X8}");
    }

    public void Dispose()
    {
        _query?.Dispose(); // PdhCloseQuery
        _query = null;
    }
}
