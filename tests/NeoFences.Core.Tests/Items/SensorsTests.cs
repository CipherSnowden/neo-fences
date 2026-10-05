using System.Buffers.Binary;
using System.Text;
using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>M31 (ADR-052): readings MSI Afterburner and HWiNFO publish in shared memory, parsed from byte samples.</summary>
public class SensorsTests
{
    // ---------- MSI Afterburner: "MAHM" v2 (header 32 bytes, entries of 1324: five 260-byte strings, then the value) ----------

    private static byte[] Afterburner(params (string Name, float Value)[] entries) => AfterburnerWith(0x4D41484D, entries);

    private static byte[] AfterburnerWith(uint signature, (string Name, float Value)[] entries)
    {
        const int Header = 32, Entry = 1324;
        var memory = new byte[Header + entries.Length * Entry];
        BinaryPrimitives.WriteUInt32LittleEndian(memory, signature);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(4), 0x20000);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(8), Header);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(12), (uint)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(16), Entry);
        for (var index = 0; index < entries.Length; index++)
        {
            var at = Header + index * Entry;
            Encoding.Latin1.GetBytes(entries[index].Name).CopyTo(memory, at);
            BinaryPrimitives.WriteSingleLittleEndian(memory.AsSpan(at + 1300), entries[index].Value);
        }
        return memory;
    }

    [Fact]
    public void Afterburner_GivesCpuTemperature_AndTheGraphicsCardUsingTheMostVideoMemory()
    {
        // As on the user's PC: GPU1 is the integrated one (little video memory), GPU2 the graphics card.
        var memory = Afterburner(("CPU temperature", 48.9f), ("CPU usage", 11.5f), ("GPU1 temperature", 44f), ("GPU2 temperature", 50f),
            ("GPU1 usage", 0f), ("GPU2 usage", 8f), ("GPU1 memory usage", 0.4f), ("GPU2 memory usage", 3889.7f));

        var reading = SensorFormats.Afterburner(memory);

        Assert.NotNull(reading);
        Assert.Equal("MSI Afterburner", reading.Source);
        Assert.Equal(48.9, reading.CpuTemp!.Value, precision: 1);
        Assert.Equal(8, reading.Gpu);
        Assert.Equal(50, reading.GpuTemp);
    }

    [Fact]
    public void Afterburner_WithOneUnnumberedGpu_UsesIt()
    {
        var reading = SensorFormats.Afterburner(Afterburner(("GPU temperature", 61f), ("GPU usage", 97f)));

        Assert.Equal(61, reading!.GpuTemp);
        Assert.Equal(97, reading.Gpu);
        Assert.Null(reading.CpuTemp); // not monitored in Afterburner
    }

    [Fact]
    public void Afterburner_IgnoresItsNoValueMarker()
    {
        // Afterburner writes FLT_MAX for a value it has none of (seen: "Framerate Min" with no game running).
        var reading = SensorFormats.Afterburner(Afterburner(("CPU temperature", float.MaxValue)));
        Assert.Null(reading!.CpuTemp);
    }

    [Theory]
    [InlineData(0xDEAD)] // Afterburner closed: it marks the memory dead
    [InlineData(0x12345678)]
    public void Afterburner_WithAnotherSignature_IsNothing(uint signature) =>
        Assert.Null(SensorFormats.Afterburner(AfterburnerWith(signature, [("CPU temperature", 50f)])));

    [Fact]
    public void Afterburner_ShortOrGarbled_IsNothing()
    {
        var memory = Afterburner(("CPU temperature", 50f));
        Assert.Null(SensorFormats.Afterburner(memory.AsSpan(0, 20)));
        Assert.Null(SensorFormats.Afterburner(memory.AsSpan(0, memory.Length - 10))); // the entries run past the end
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(12), 1_000_000); // an absurd entry count
        Assert.Null(SensorFormats.Afterburner(memory));
    }

    // ---------- HWiNFO: "HWiS" (packed header 44 bytes; sensors of 264; readings of 316, value at 284) ----------

    private const uint Temp = 1, Usage = 7, Other = 8;

    private static byte[] Hwinfo(string[] sensors, params (uint Type, int Sensor, string Label, double Value)[] readings) =>
        HwinfoWith(0x53695748, sensors, readings);

    private static byte[] HwinfoWith(uint signature, string[] sensors, (uint Type, int Sensor, string Label, double Value)[] readings)
    {
        const int Header = 44, Sensor = 264, Reading = 316;
        var memory = new byte[Header + sensors.Length * Sensor + readings.Length * Reading];
        BinaryPrimitives.WriteUInt32LittleEndian(memory, signature);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(20), Header);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(24), Sensor);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(28), (uint)sensors.Length);
        var readingsAt = Header + sensors.Length * Sensor;
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(32), (uint)readingsAt);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(36), Reading);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(40), (uint)readings.Length);
        for (var index = 0; index < sensors.Length; index++) Encoding.Latin1.GetBytes(sensors[index]).CopyTo(memory, Header + index * Sensor + 8);
        for (var index = 0; index < readings.Length; index++)
        {
            var at = readingsAt + index * Reading;
            BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(at), readings[index].Type);
            BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(at + 4), (uint)readings[index].Sensor);
            Encoding.Latin1.GetBytes(readings[index].Label).CopyTo(memory, at + 12);
            BinaryPrimitives.WriteDoubleLittleEndian(memory.AsSpan(at + 284), readings[index].Value);
        }
        return memory;
    }

    [Fact]
    public void Hwinfo_GivesTheCpuTemperature_AndTheGraphicsCardUsingTheMostVideoMemory()
    {
        var memory = Hwinfo(["CPU [#0]: AMD Ryzen 7", "GPU [#0]: AMD Radeon(TM) Graphics", "GPU [#1]: NVIDIA GeForce RTX 4060"],
            (Temp, 0, "Core Max", 55), (Temp, 0, "CPU (Tctl/Tdie)", 52.5),
            (Temp, 1, "GPU Temperature", 44), (Usage, 1, "GPU Core Load", 3), (Other, 1, "GPU Memory Allocated", 300),
            (Temp, 2, "GPU Temperature", 58), (Usage, 2, "GPU Core Load", 41), (Other, 2, "GPU Memory Allocated", 4200));

        var reading = SensorFormats.Hwinfo(memory);

        Assert.Equal("HWiNFO", reading!.Source);
        Assert.Equal(52.5, reading.CpuTemp); // the package/die value wins over a per-core one
        Assert.Equal(41, reading.Gpu);
        Assert.Equal(58, reading.GpuTemp);
    }

    [Fact]
    public void Hwinfo_OnAnIntelPc_UsesTheCpuPackage()
    {
        var reading = SensorFormats.Hwinfo(Hwinfo(["CPU [#0]: Intel Core i7"], (Temp, 0, "Core Max", 70), (Temp, 0, "CPU Package", 66)));
        Assert.Equal(66, reading!.CpuTemp);
        Assert.Null(reading.GpuTemp);
    }

    [Theory]
    [InlineData(0x44414544)] // "DEAD": sharing switched off (the free version stops after 12 hours)
    [InlineData(0)]
    public void Hwinfo_WithAnotherSignature_IsNothing(uint signature) =>
        Assert.Null(SensorFormats.Hwinfo(HwinfoWith(signature, ["CPU"], [(Temp, 0, "CPU Package", 60)])));

    [Fact]
    public void Hwinfo_ShortOrGarbled_IsNothing()
    {
        var memory = Hwinfo(["CPU"], (Temp, 0, "CPU Package", 60));
        Assert.Null(SensorFormats.Hwinfo(memory.AsSpan(0, 40)));
        Assert.Null(SensorFormats.Hwinfo(memory.AsSpan(0, memory.Length - 1)));
    }
}
