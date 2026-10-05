using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace NeoFences.Core.Items;

/// <summary>What an outside monitor publishes (M31, ADR-052): the CPU temperature and the main graphics card's use and temperature.</summary>
public sealed record SensorReading(string Source, double? CpuTemp, double? Gpu, double? GpuTemp, string? GpuKey = null);

/// <summary>
/// The shared-memory layouts of MSI Afterburner and HWiNFO64 (M31, ADR-052), parsed from a copy of the bytes: pure, so a
/// saved sample tests it. A wrong signature, a short buffer or an absurd count is nothing (null), never an exception. The
/// main graphics card is the one using the most video memory (an integrated GPU uses little), so readings never flip.
/// </summary>
public static partial class SensorFormats
{
    public const string AfterburnerMapping = "MAHMSharedMemory";
    public const string HwinfoMapping = @"Global\HWiNFO_SENS_SM2";
    private const int MaxEntries = 4096;

    // ---------- MSI Afterburner: MAHM_SHARED_MEMORY_HEADER / _ENTRY (v2) ----------

    private const uint AfterburnerSignature = 0x4D41484D; // "MAHM"; 0xDEAD once Afterburner has closed
    private const int NameLength = 260, ValueAt = 5 * NameLength; // szSrcName … szRecommendedFormat, then float data

    public static SensorReading? Afterburner(ReadOnlySpan<byte> memory, string? previousGpu = null)
    {
        if (memory.Length < 20 || U32(memory, 0) != AfterburnerSignature) return null;
        var headerSize = U32(memory, 8);
        var count = U32(memory, 12);
        var entrySize = U32(memory, 16);
        if (headerSize < 20 || entrySize < ValueAt + 4 || count > MaxEntries || headerSize + (long)count * entrySize > memory.Length) return null;

        double? cpuTemp = null;
        var gpus = new Dictionary<int, (double? Usage, double? Temp, double? Memory)>();
        for (var index = 0; index < count; index++)
        {
            var entry = memory.Slice((int)(headerSize + index * entrySize), (int)entrySize);
            var value = BinaryPrimitives.ReadSingleLittleEndian(entry[ValueAt..]);
            if (!float.IsFinite(value) || value > 1e30f) continue; // FLT_MAX: no value
            var name = Text(entry[..NameLength]);
            if (name == "CPU temperature") { cpuTemp = value; continue; }
            if (AfterburnerGpu().Match(name) is not { Success: true } gpu) continue;
            var number = 1;
            if (gpu.Groups[1].Value.Length > 0 && !int.TryParse(gpu.Groups[1].Value, out number)) continue; // final review I1: odd names are nothing
            var known = gpus.GetValueOrDefault(number);
            gpus[number] = gpu.Groups[2].Value switch
            {
                "usage" => known with { Usage = value },
                "temperature" => known with { Temp = value },
                _ => known with { Memory = value },
            };
        }
        var main = MainGpu(gpus.Select(pair => (pair.Key, pair.Value.Memory)), previousGpu);
        var chosen = main is { } key ? gpus[key] : default;
        return new SensorReading("MSI Afterburner", cpuTemp, chosen.Usage, chosen.Temp, main?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [GeneratedRegex("^GPU(\\d*) (usage|temperature|memory usage)$")]
    private static partial Regex AfterburnerGpu();

    // ---------- HWiNFO64: HWiNFO_SENSORS_SHARED_MEM2 (packed) ----------

    private const uint HwinfoSignature = 0x53695748; // "HWiS"; "DEAD" while sharing is off
    private const uint TypeTemp = 1, TypeUsage = 7, TypeOther = 8;
    private const int LabelAt = 12, LabelLength = 128, HwinfoValueAt = 284;

    /// <summary>The CPU temperature, best first: AMD's control/die value, Intel's package, then fallbacks.</summary>
    private static readonly string[] CpuTempLabels = ["CPU (Tctl/Tdie)", "CPU Package", "CPU Die (average)", "CPU (Tctl)", "Core Max", "CPU"];

    public static SensorReading? Hwinfo(ReadOnlySpan<byte> memory, string? previousGpu = null)
    {
        if (memory.Length < 44 || U32(memory, 0) != HwinfoSignature) return null;
        var readingsAt = U32(memory, 32);
        var readingSize = U32(memory, 36);
        var count = U32(memory, 40);
        if (readingSize < HwinfoValueAt + 8 || count > MaxEntries || readingsAt + (long)count * readingSize > memory.Length) return null;

        var cpu = new Dictionary<string, double>(StringComparer.Ordinal);
        var gpus = new Dictionary<int, (double? Usage, double? Temp, double? Memory)>();
        for (var index = 0; index < count; index++)
        {
            var reading = memory.Slice((int)(readingsAt + index * readingSize), (int)readingSize);
            var type = U32(reading, 0);
            var sensor = (int)U32(reading, 4);
            var label = Text(reading.Slice(LabelAt, LabelLength));
            var value = BinaryPrimitives.ReadDoubleLittleEndian(reading[HwinfoValueAt..]);
            if (!double.IsFinite(value)) continue;
            if (type == TypeTemp && Array.IndexOf(CpuTempLabels, label) >= 0) cpu.TryAdd(label, value);
            var known = gpus.GetValueOrDefault(sensor);
            switch (type, label)
            {
                case (TypeTemp, "GPU Temperature"): gpus[sensor] = known with { Temp = value }; break;
                case (TypeUsage, "GPU Core Load"): gpus[sensor] = known with { Usage = value }; break;
                case (TypeOther, "GPU Memory Allocated" or "GPU D3D Memory Dedicated"): gpus[sensor] = known with { Memory = Math.Max(value, known.Memory ?? 0) }; break;
            }
        }
        double? cpuTemp = CpuTempLabels.FirstOrDefault(cpu.ContainsKey) is { } best ? cpu[best] : null;
        var main = MainGpu(gpus.Where(pair => pair.Value.Temp is not null || pair.Value.Usage is not null).Select(pair => (pair.Key, pair.Value.Memory)), previousGpu);
        var chosen = main is { } key ? gpus[key] : default;
        return new SensorReading("HWiNFO", cpuTemp, chosen.Usage, chosen.Temp, main?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Each value from the first source that has it (final review I2): Afterburner without its CPU temperature graph still
    /// lets HWiNFO's CPU temperature through. The source names both when the second one gave something.
    /// </summary>
    public static SensorReading? Merge(SensorReading? first, SensorReading? second)
    {
        if (first is null || second is null) return first ?? second;
        var merged = first with { CpuTemp = first.CpuTemp ?? second.CpuTemp, Gpu = first.Gpu ?? second.Gpu, GpuTemp = first.GpuTemp ?? second.GpuTemp };
        return merged == first ? first : merged with { Source = $"{first.Source} + {second.Source}" };
    }

    /// <summary>
    /// The GPU using the most video memory. A lone GPU is it; several without memory readings are left to Windows (final
    /// review I3: the first one is often the built-in GPU). Sticky across readings (M32, <see cref="Widgets.StickyGpu"/>).
    /// </summary>
    private static int? MainGpu(IEnumerable<(int Key, double? Memory)> gpus, string? previous)
    {
        var list = gpus.OrderBy(gpu => gpu.Key).ToList();
        if (list.Count == 1) return list[0].Key;
        if (!list.Any(gpu => gpu.Memory is not null)) return null;
        var chosen = Widgets.StickyGpu(previous, [.. list.Select(gpu => (gpu.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), gpu.Memory ?? 0))]);
        return chosen is null ? null : int.Parse(chosen, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static uint U32(ReadOnlySpan<byte> memory, int at) => BinaryPrimitives.ReadUInt32LittleEndian(memory[at..]);

    /// <summary>A NUL-terminated ANSI name (sensor names are ASCII).</summary>
    private static string Text(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? field : field[..end]);
    }
}
