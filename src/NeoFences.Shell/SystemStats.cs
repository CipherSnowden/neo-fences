using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using NeoFences.Core.Items;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Performance;
using Windows.Win32.System.SystemInformation;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace NeoFences.Shell;

/// <summary>
/// The System stats widget's readings (M25, spec 2026-10-05-widgets-design §3; M31, ADR-052): CPU from the difference
/// between two <c>GetSystemTimes</c> readings, RAM from <c>GlobalMemoryStatusEx</c>; the CPU temperature and the main
/// graphics card from MSI Afterburner or HWiNFO when one shares its sensors (read-only, no admin), otherwise GPU use from
/// the PDH counter of every 3D engine (the busiest adapter) and its temperature the way Task Manager reads it. One PDH
/// query is kept. Not thread-safe: one sample at a time (the host keeps at most one in flight). Every failure leaves that
/// value null.
/// </summary>
public sealed class SystemStats : IDisposable
{
    private const string GpuCounter = @"\GPU Engine(*engtype_3D)\Utilization Percentage";
    private const long MaxSensorBytes = 4 << 20; // a sane cap on a mapping's copy (Afterburner's is ~90 KB)
    private readonly Action<string, Exception?> _logOnce;
    private (ulong Idle, ulong Busy)? _lastCpu;
    private PdhCloseQuerySafeHandle? _query;
    private PDH_HCOUNTER _gpu;
    private PDH_HCOUNTER _gpuMemory; // M31 final review I3: dedicated video memory per adapter (optional)
    private const string GpuMemoryCounter = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private bool _gpuFailed;
    private readonly object _gate = new(); // a reading and Dispose never overlap (exit while a reading runs; final review M1)
    private bool _disposed;

    /// <param name="logOnce">Told the first time a value cannot be read (the host logs each name once).</param>
    public SystemStats(Action<string, Exception?> logOnce) => _logOnce = logOnce;

    /// <summary>The outside monitor the last reading came from ("MSI Afterburner", "HWiNFO"), or null: Windows only.</summary>
    public string? Source { get; private set; }

    public StatsSample Sample()
    {
        lock (_gate)
        {
            if (_disposed) return new StatsSample(null, null, null, null, null, null);
            var outside = Outside();
            Source = outside?.Source;
            var cpu = Cpu();
            var (used, total) = Ram();
            double? gpu = outside?.Gpu, gpuTemp = outside?.GpuTemp;
            if (gpu is null || gpuTemp is null)
            {
                var (windowsGpu, adapter) = Gpu();
                gpu ??= windowsGpu;
                gpuTemp ??= Widgets.LuidOf(adapter) is { } luid ? AdapterTemperature(luid) : null;
            }
            return new StatsSample(cpu, outside?.CpuTemp, gpu, gpuTemp, used, total);
        }
    }

    // ---------- MSI Afterburner, then HWiNFO (M31, ADR-052) ----------

    private delegate SensorReading? SensorParser(ReadOnlySpan<byte> memory);

    // M32: each source's last main GPU, so the choice is sticky (Widgets.StickyGpu).
    private string? _afterburnerGpu, _hwinfoGpu, _windowsAdapter;

    /// <summary>Afterburner, completed value by value from HWiNFO when it lacks one (final review I2).</summary>
    private SensorReading? Outside()
    {
        var afterburner = Shared(SensorFormats.AfterburnerMapping, memory => SensorFormats.Afterburner(memory, _afterburnerGpu), mutex: null);
        if (afterburner?.GpuKey is { } afterburnerGpu) _afterburnerGpu = afterburnerGpu;
        if (afterburner is { CpuTemp: not null, Gpu: not null, GpuTemp: not null }) return afterburner;
        var hwinfo = Shared(SensorFormats.HwinfoMapping, memory => SensorFormats.Hwinfo(memory, _hwinfoGpu), mutex: SensorFormats.HwinfoMutex);
        if (hwinfo?.GpuKey is { } hwinfoGpu) _hwinfoGpu = hwinfoGpu;
        return SensorFormats.Merge(afterburner, hwinfo);
    }

    /// <summary>
    /// A copy of a monitor's shared memory, parsed; null when it is not running (the usual case) or unreadable. The copy is a
    /// rented buffer (M32: no ~90 KB array per reading), taken under the monitor's mutex when it has one (M32: HWiNFO's,
    /// waited for at most 20 ms; without it the copy is taken as before).
    /// </summary>
    private SensorReading? Shared(string mapping, SensorParser parse, string? mutex)
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(mapping, MemoryMappedFileRights.Read);
            using var view = map.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            var length = (int)Math.Min(view.Length, MaxSensorBytes);
            var bytes = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
            try
            {
                using (Held(mutex)) view.ReadExactly(bytes, 0, length);
                return parse(bytes.AsSpan(0, length));
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(bytes);
            }
        }
        catch (FileNotFoundException)
        {
            return null; // not running
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) // unreadable, or anything odd: nothing (final review I1)
        {
            _logOnce(mapping, failure);
            return null;
        }
    }

    /// <summary>The monitor's mutex held while copying (released on dispose), or nothing when there is none or it is busy.</summary>
    private static IDisposable? Held(string? name)
    {
        if (name is null) return null;
        try
        {
            if (!Mutex.TryOpenExisting(name, out var mutex)) return null;
            try
            {
                if (mutex.WaitOne(20)) return new Release(mutex);
            }
            catch (AbandonedMutexException)
            {
                return new Release(mutex); // its owner ended while holding it: ours now
            }
            mutex.Dispose();
            return null; // busy: copy without it, as before
        }
        catch (Exception failure) when (failure is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            return null; // no access to it: copy without it
        }
    }

    private sealed class Release(Mutex mutex) : IDisposable
    {
        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    // ---------- GPU temperature as Task Manager reads it (M31) ----------
    // CsWin32 has no D3DKMT functions without the WDK metadata package (a new dependency), so these few are declared here
    // (CLAUDE.md hard rule 5's exception).

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromLuid { public uint LuidLow; public int LuidHigh; public uint Adapter; }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct QueryAdapterInfo { public uint Adapter; public int Type; public void* Data; public uint DataSize; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterPerfData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOc, MemoryBandwidth, PcieBandwidth;
        public uint FanRpm, Power, Temperature; // Temperature in tenths of a degree Celsius
        public byte PowerStateOverride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter { public uint Adapter; }

    private const int AdapterPerfDataType = 62; // KMTQAITYPE_ADAPTERPERFDATA (WDDM 2.7)

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapterFromLuid open);

    [DllImport("gdi32.dll")]
    private static extern unsafe int D3DKMTQueryAdapterInfo(QueryAdapterInfo* query);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter close);

    /// <summary>The adapter's temperature in °C, or null (an older driver, no sensor, or the call failed).</summary>
    private unsafe double? AdapterTemperature((int High, uint Low) luid)
    {
        try
        {
            var open = new OpenAdapterFromLuid { LuidLow = luid.Low, LuidHigh = luid.High };
            if (D3DKMTOpenAdapterFromLuid(ref open) != 0) return null;
            try
            {
                var perf = new AdapterPerfData();
                var query = new QueryAdapterInfo { Adapter = open.Adapter, Type = AdapterPerfDataType, Data = &perf, DataSize = (uint)sizeof(AdapterPerfData) };
                return D3DKMTQueryAdapterInfo(&query) == 0 && perf.Temperature > 0 ? perf.Temperature / 10.0 : null;
            }
            finally
            {
                var close = new CloseAdapter { Adapter = open.Adapter };
                D3DKMTCloseAdapter(ref close);
            }
        }
        catch (Exception failure) when (failure is EntryPointNotFoundException or DllNotFoundException)
        {
            _logOnce("GPU temperature", failure);
            return null;
        }
    }

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

    /// <summary>
    /// RAM in use and installed, in GB (M31: absolute, not a percent). The total is the installed memory (final review M1:
    /// "/ 32 GB", not the 31.1 GB Windows can use); in use is what Windows reports as used.
    /// </summary>
    private (double? Used, double? Total) Ram()
    {
        const double Gb = 1024.0 * 1024 * 1024;
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!PInvoke.GlobalMemoryStatusEx(ref status))
        {
            _logOnce("RAM", new System.ComponentModel.Win32Exception());
            return (null, null);
        }
        var installed = PInvoke.GetPhysicallyInstalledSystemMemory(out var kilobytes) && kilobytes * 1024 >= status.ullTotalPhys ? kilobytes * 1024.0 : status.ullTotalPhys;
        return ((status.ullTotalPhys - status.ullAvailPhys) / Gb, installed / Gb);
    }

    /// <summary>
    /// GPU use and the adapter it belongs to, whose temperature is read too (M31): the main graphics card — the adapter with
    /// the most dedicated video memory in use (final review I3) — or, without that counter, the busiest one (M28).
    /// </summary>
    private unsafe (double? Percent, string Adapter) Gpu()
    {
        if (_gpuFailed) return (null, "");
        try
        {
            if (_query is null)
            {
                Check(PInvoke.PdhOpenQuery(null, 0, out var query), "open");
                _query = query;
                Check(PInvoke.PdhAddEnglishCounter(_query, GpuCounter, 0, out _gpu), "add");
                _gpuMemory = PInvoke.PdhAddEnglishCounter(_query, GpuMemoryCounter, 0, out var memory) == 0 ? memory : default; // optional
                Check(PInvoke.PdhCollectQueryData(new PDH_HQUERY(_query.DangerousGetHandle())), "first collect");
                return (null, ""); // a rate needs two collections
            }
            if (PInvoke.PdhCollectQueryData(new PDH_HQUERY(_query.DangerousGetHandle())) != 0) return Reopen(); // a GPU driver update: try again from scratch
            if (Values(_gpu) is not { } engines) return Reopen();
            var dedicated = _gpuMemory == default ? [] : Values(_gpuMemory) ?? [];
            var (adapter, percent) = Widgets.MainGpu(engines, dedicated, _windowsAdapter); // M32: sticky
            if (adapter.Length > 0) _windowsAdapter = adapter;
            return (percent, adapter);
        }
        catch (Exception failure) when (failure is InvalidOperationException or ExternalException)
        {
            _gpuFailed = true; // the counter cannot be opened or added: no GPU counters on this PC, "—" from now on
            _logOnce("GPU", failure);
            return (null, "");
        }
    }

    /// <summary>A counter's instances and values (valid or new data); empty when it has none; null when the read failed.</summary>
    private static unsafe List<(string Instance, double Value)>? Values(PDH_HCOUNTER counter)
    {
        uint size = 0, count = 0;
        var status = PInvoke.PdhGetFormattedCounterArray(counter, PDH_FMT.PDH_FMT_DOUBLE, &size, &count, null);
        if (status != (uint)PInvoke.PDH_MORE_DATA || size == 0) return status == 0 ? [] : null; // no instances (no 3D engines: 0 %)
        var buffer = new byte[size];
        fixed (byte* bytes = buffer)
        {
            if (PInvoke.PdhGetFormattedCounterArray(counter, PDH_FMT.PDH_FMT_DOUBLE, &size, &count, (PDH_FMT_COUNTERVALUE_ITEM_W*)bytes) != 0) return null;
            var items = (PDH_FMT_COUNTERVALUE_ITEM_W*)bytes;
            var values = new List<(string Instance, double Value)>((int)count);
            for (var index = 0; index < count; index++)
            {
                if (items[index].FmtValue.CStatus is 0 or 1) values.Add((items[index].szName.ToString(), items[index].FmtValue.Anonymous.doubleValue)); // valid data, or new data (review M2)
            }
            return values;
        }
    }

    /// <summary>
    /// A reading failed after the query worked (a GPU driver update removes the engines for a moment): the query is closed
    /// and opened again at the next reading, which shows "—" once (final review M2).
    /// </summary>
    private (double?, string) Reopen()
    {
        _query?.Dispose();
        _query = null;
        return (null, "");
    }

    private static void Check(uint status, string what)
    {
        if (status != 0) throw new InvalidOperationException($"PDH {what} failed: 0x{status:X8}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _query?.Dispose(); // PdhCloseQuery
            _query = null;
        }
    }
}
