using System.Globalization;

namespace NeoFences.Core.Items;

/// <summary>The widgets a fence can hold (M25, spec 2026-10-05-widgets-design).</summary>
public enum WidgetKind { Clock, Date, Stats }

/// <summary>A widget's options: the clock's seconds and date line (M25); the stats' temperatures in °F (M31).</summary>
public sealed record WidgetOptions
{
    public bool Seconds { get; init; }
    public bool Date { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Fahrenheit { get; init; } // M32: written only when set (clocks never carry it)
}

/// <summary>
/// One System stats reading (M25; M31: temperatures in °C and RAM in GB, no disk): null where no source gave the value.
/// </summary>
public sealed record StatsSample(double? Cpu, double? CpuTemp, double? Gpu, double? GpuTemp, double? RamUsedGb, double? RamTotalGb);

/// <summary>
/// One tile of the System stats widget (M31): its label, the bar's percent (0–100), the number and its unit, a hint under a
/// missing value, and whether it spans both columns.
/// </summary>
public sealed record StatTile(string Label, double Percent, string Value, string Unit, string Hint = "", bool Wide = false);

/// <summary>The Date widget's page: weekday, day number, month and year.</summary>
public sealed record DatePage(string Weekday, string Day, string MonthYear);

/// <summary>
/// Widgets (M25, ADR-047): elements stored like items with a <c>neofences:widget/&lt;kind&gt;</c> target, so sizes,
/// layouts, drags, copies and snapshots are the items'. Never checked or watched; nothing on disk behind them. Pure.
/// </summary>
public static class Widgets
{
    public const string TargetPrefix = "neofences:widget/";

    public static string Target(WidgetKind kind) => TargetPrefix + kind.ToString().ToLowerInvariant();

    /// <summary>The widget a target names, or null (an unknown kind — from a newer NeoFences — is <see cref="IsUnknown"/>).</summary>
    public static WidgetKind? Of(string target) =>
        target.StartsWith(TargetPrefix, StringComparison.OrdinalIgnoreCase)
        && Enum.TryParse<WidgetKind>(target[TargetPrefix.Length..], ignoreCase: true, out var kind) && Enum.IsDefined(kind)
        && !int.TryParse(target[TargetPrefix.Length..], out _) ? kind : null;

    /// <summary>
    /// A widget target of a kind this NeoFences does not know (M28, M25 review minor): still a widget — never checked or
    /// opened through Windows — shown Missing so it can be removed.
    /// </summary>
    public static bool IsUnknown(string target) => target.StartsWith(TargetPrefix, StringComparison.OrdinalIgnoreCase) && Of(target) is null;

    /// <summary>A widget target's name: its kind's, or "Unknown widget".</summary>
    public static string NameOfTarget(string target) => Of(target) is { } kind ? NameOf(kind) : "Unknown widget";

    public static string NameOf(WidgetKind kind) => kind switch
    {
        WidgetKind.Clock => "Clock",
        WidgetKind.Date => "Date",
        _ => "System stats",
    };

    /// <summary>Clock 2×1; Date and System stats 2×2.</summary>
    public static GridSpan DefaultSpan(WidgetKind kind) => kind == WidgetKind.Clock ? new GridSpan(2, 1) : new GridSpan(2, 2);

    /// <summary>
    /// The stats widget's tiles (M31, spec 2026-10-06-modern-widgets-design §2): CPU, CPU TEMP, GPU, GPU TEMP, RAM (wide). A
    /// missing value shows "—"; CPU TEMP then says what it needs. Temperature bars run 0–100 °C whatever the unit.
    /// </summary>
    public static IReadOnlyList<StatTile> Tiles(StatsSample? sample, bool fahrenheit, CultureInfo culture) =>
    [
        Percent("CPU", sample?.Cpu),
        Temperature("CPU TEMP", sample?.CpuTemp, fahrenheit, hint: "needs Afterburner or HWiNFO"),
        Percent("GPU", sample?.Gpu),
        Temperature("GPU TEMP", sample?.GpuTemp, fahrenheit, hint: ""),
        Ram(sample?.RamUsedGb, sample?.RamTotalGb, culture),
    ];

    private const string Dash = "—";

    private static StatTile Percent(string label, double? percent)
    {
        if (percent is not { } value || double.IsNaN(value)) return new StatTile(label, 0, Dash, "");
        var clamped = Math.Clamp(value, 0, 100);
        return new StatTile(label, clamped, Whole(clamped), "%");
    }

    private static StatTile Temperature(string label, double? celsius, bool fahrenheit, string hint)
    {
        if (celsius is not { } value || double.IsNaN(value)) return new StatTile(label, 0, Dash, "", hint);
        return new StatTile(label, Math.Clamp(value, 0, 100), Whole(fahrenheit ? Fahrenheit(value) : value), fahrenheit ? "°F" : "°C");
    }

    private static StatTile Ram(double? usedGb, double? totalGb, CultureInfo culture)
    {
        if (usedGb is not { } used || totalGb is not { } total || total <= 0) return new StatTile("RAM", 0, Dash, "", Wide: true);
        return new StatTile("RAM", Math.Clamp(100 * used / total, 0, 100), used.ToString("0.0", culture),
            $"/ {Math.Round(total, MidpointRounding.AwayFromZero):0} GB", Wide: true);
    }

    private static string Whole(double value) => Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    public static double Fahrenheit(double celsius) => celsius * 9 / 5 + 32;

    /// <summary>
    /// The clock's date line (M31): the weekday and the day with its month — "Monday, 5 October"; where the culture's long
    /// date puts the weekday after the month (ja, zh, ko), after it: "10月5日 月曜日" (M32). Windows' own regional formats
    /// may leave the weekday out (ja-JP, zh-CN): the culture's default pattern decides then (M32 final review I2).
    /// </summary>
    public static string ClockDateLine(DateTime date, CultureInfo culture)
    {
        var weekday = culture.DateTimeFormat.GetDayName(date.DayOfWeek);
        var monthDay = date.ToString(culture.DateTimeFormat.MonthDayPattern, culture);
        var pattern = culture.DateTimeFormat.LongDatePattern;
        if (!pattern.Contains("dddd", StringComparison.Ordinal)) pattern = CultureInfo.GetCultureInfo(culture.Name).DateTimeFormat.LongDatePattern;
        var bare = System.Text.RegularExpressions.Regex.Replace(pattern, "'[^']*'", ""); // quoted literals are not fields
        var weekdayAt = bare.IndexOf("dddd", StringComparison.Ordinal);
        var monthAt = bare.IndexOf('M');
        return weekdayAt >= 0 && monthAt >= 0 && weekdayAt > monthAt ? $"{monthDay} {weekday}" : $"{weekday}, {monthDay}";
    }

    public static DatePage Page(DateTime date, CultureInfo culture) =>
        new(culture.DateTimeFormat.GetDayName(date.DayOfWeek), date.Day.ToString(culture), date.ToString(culture.DateTimeFormat.YearMonthPattern, culture)); // M28: the culture's own order (ja, zh, ko, hu)

    /// <summary>
    /// GPU use like Task Manager's (M28, M25 review minor): the 3D engines' readings summed per adapter (the instance's
    /// "luid_…" part), the busiest adapter, capped at 100. An instance without a luid counts as one adapter.
    /// </summary>
    public static double GpuPercent(IReadOnlyList<(string Instance, double Value)> engines) => BusiestGpu(engines).Percent;

    /// <summary>
    /// The main graphics card when no monitor names it (M31 final review I3): the adapter with the most dedicated video
    /// memory in use (an integrated GPU has little), with its use; without memory readings, the busiest one as before.
    /// </summary>
    public static (string Adapter, double Percent) MainGpu(IReadOnlyList<(string Instance, double Value)> engines,
        IReadOnlyList<(string Instance, double Bytes)> dedicated, string? previousAdapter = null)
    {
        if (dedicated.Count == 0) return BusiestGpu(engines);
        var adapters = dedicated.GroupBy(memory => AdapterOf(memory.Instance), StringComparer.OrdinalIgnoreCase)
            .Select(adapter => (adapter.Key, MemoryMb: adapter.Sum(memory => memory.Bytes) / (1024.0 * 1024))).ToList();
        var main = StickyGpu(previousAdapter, adapters)!; // M32: no flip on a hybrid laptop
        var percent = engines.Where(engine => string.Equals(AdapterOf(engine.Instance), main, StringComparison.OrdinalIgnoreCase)).Sum(engine => engine.Value);
        return (main, Math.Min(100, percent));
    }

    /// <summary>How much more video memory another GPU must use before the choice moves to it (M32): twice and this many MB.</summary>
    public const double SwitchGpuMb = 1024;

    /// <summary>
    /// The main graphics card, sticky (M32): the previous choice stays unless another GPU uses at least twice its video
    /// memory and <see cref="SwitchGpuMb"/> more — a hybrid laptop's card idling at 0 MB beside a built-in GPU holding a few
    /// hundred does not flip. Without a previous choice (or when it is gone), the one using the most.
    /// </summary>
    public static string? StickyGpu(string? previous, IReadOnlyList<(string Key, double MemoryMb)> candidates)
    {
        if (candidates.Count == 0) return null;
        var best = candidates.MaxBy(candidate => candidate.MemoryMb);
        var kept = candidates.FirstOrDefault(candidate => string.Equals(candidate.Key, previous, StringComparison.OrdinalIgnoreCase));
        if (previous is null || kept.Key is null) return best.Key;
        return best.MemoryMb >= 2 * kept.MemoryMb && best.MemoryMb >= kept.MemoryMb + SwitchGpuMb ? best.Key : kept.Key;
    }

    /// <summary>The busiest adapter (its "luid_…" key) and its use (M31: its temperature is read from the same adapter).</summary>
    public static (string Adapter, double Percent) BusiestGpu(IReadOnlyList<(string Instance, double Value)> engines)
    {
        if (engines.Count == 0) return ("", 0);
        var busiest = engines.GroupBy(engine => AdapterOf(engine.Instance), StringComparer.OrdinalIgnoreCase)
            .Select(adapter => (Adapter: adapter.Key, Percent: adapter.Sum(engine => engine.Value))).MaxBy(adapter => adapter.Percent);
        return (busiest.Adapter, Math.Min(100, busiest.Percent));
    }

    /// <summary>An adapter's LUID from a counter instance or key ("luid_0x00000000_0x0000D1F2…"), or null.</summary>
    public static (int High, uint Low)? LuidOf(string instance)
    {
        var parts = AdapterOf(instance).Split('_');
        return parts.Length >= 3
               && int.TryParse(Hex(parts[1]), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high)
               && uint.TryParse(Hex(parts[2]), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low)
            ? (high, low) : null;

        static string Hex(string part) => part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? part[2..] : part;
    }

    private static string AdapterOf(string instance)
    {
        var start = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return "";
        var end = instance.IndexOf("_phys", start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? instance[start..] : instance[start..end];
    }

    /// <summary>The time in the culture's short time format, or its long one with seconds (12/24-hour follows Windows).</summary>
    public static string ClockText(DateTime now, bool seconds, CultureInfo culture) =>
        now.ToString(seconds ? culture.DateTimeFormat.LongTimePattern : culture.DateTimeFormat.ShortTimePattern, culture);

    /// <summary>The next whole second, or the next whole minute: when a clock shows something new.</summary>
    public static DateTime NextTick(DateTime now, bool seconds)
    {
        var unit = seconds ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
        return new DateTime(now.Ticks - now.Ticks % unit + unit, now.Kind);
    }
}
