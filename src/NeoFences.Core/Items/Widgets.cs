using System.Globalization;

namespace NeoFences.Core.Items;

/// <summary>The widgets a fence can hold (M25, spec 2026-10-05-widgets-design).</summary>
public enum WidgetKind { Clock, Date, Stats }

/// <summary>A clock's options (M25): seconds, and a date line under the time.</summary>
public sealed record WidgetOptions
{
    public bool Seconds { get; init; }
    public bool Date { get; init; }
}

/// <summary>One row of the System stats widget: its label, the bar's percent (0–100) and the text shown.</summary>
public sealed record StatRow(string Label, double Percent, string Text);

/// <summary>The Date widget's page: weekday (upper case), day number, month and year.</summary>
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

    /// <summary>A stats row: a whole percent within 0–100, or "—" when Windows gave no value.</summary>
    public static StatRow Row(string label, double? percent)
    {
        if (percent is not { } value || double.IsNaN(value)) return new StatRow(label, 0, "—");
        var clamped = Math.Clamp(value, 0, 100);
        return new StatRow(label, clamped, $"{Math.Round(clamped, MidpointRounding.AwayFromZero):0}%");
    }

    public static DatePage Page(DateTime date, CultureInfo culture) =>
        new(culture.DateTimeFormat.GetDayName(date.DayOfWeek).ToUpper(culture), date.Day.ToString(culture), date.ToString(culture.DateTimeFormat.YearMonthPattern, culture)); // M28: the culture's own order (ja, zh, ko, hu)

    /// <summary>
    /// GPU use like Task Manager's (M28, M25 review minor): the 3D engines' readings summed per adapter (the instance's
    /// "luid_…" part), the busiest adapter, capped at 100. An instance without a luid counts as one adapter.
    /// </summary>
    public static double GpuPercent(IReadOnlyList<(string Instance, double Value)> engines) =>
        engines.Count == 0 ? 0
            : Math.Min(100, engines.GroupBy(engine => AdapterOf(engine.Instance), StringComparer.OrdinalIgnoreCase).Max(adapter => adapter.Sum(engine => engine.Value)));

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
