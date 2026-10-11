using System.Globalization;

namespace NeoFences.Core.Items;

/// <summary>A widget's size (M41a, spec 2026-10-11-m41a-feel-design §6.2): three designed layouts, a bigger one shows more.</summary>
public enum WidgetSize { Small, Medium, Large }

/// <summary>Widget sizes as grid spans; old free spans map to the nearest size (nothing is rewritten until a resize).</summary>
public static class WidgetSizes
{
    public static GridSpan SpanOf(WidgetSize size) => size switch
    {
        WidgetSize.Small => new GridSpan(2, 1),
        WidgetSize.Large => new GridSpan(4, 2),
        _ => new GridSpan(2, 2),
    };

    /// <summary>A stored span's size: three columns or more is Large; else one row Small, more Medium. None: the kind's default.</summary>
    public static WidgetSize Of(GridSpan? stored, WidgetKind kind) =>
        stored is { } span ? span.Columns >= 3 ? WidgetSize.Large : span.Rows >= 2 ? WidgetSize.Medium : WidgetSize.Small
        : kind == WidgetKind.Clock ? WidgetSize.Small : WidgetSize.Medium;

    /// <summary>
    /// The design a widget shows in the space it gets (rc.5): the chosen size, or the biggest smaller one that fits — a
    /// Large widget in a three-column fence is trimmed to three columns and shows Medium instead of a cut-off Large.
    /// </summary>
    public static WidgetSize Fitting(WidgetSize chosen, GridSpan space)
    {
        var fits = space.Columns >= 4 && space.Rows >= 2 ? WidgetSize.Large : space.Columns >= 2 && space.Rows >= 2 ? WidgetSize.Medium : WidgetSize.Small;
        return fits < chosen ? fits : chosen;
    }

    /// <summary>A corner drag (in cells, fractions included) snaps to the size it is nearest.</summary>
    public static WidgetSize Snap(double columns, double rows) =>
        columns >= 3 ? WidgetSize.Large : rows >= 1.5 ? WidgetSize.Medium : WidgetSize.Small;
}

/// <summary>One day of the calendar widget's month.</summary>
public sealed record CalendarDay(int Day, bool InMonth, bool Today, bool Weekend);

/// <summary>The calendar widget's month (M41a): whole weeks from the week's first day, as many as the month needs.</summary>
public sealed record CalendarMonth(IReadOnlyList<CalendarDay> Days)
{
    public static CalendarMonth Build(DateOnly anyDayOfMonth, DayOfWeek firstDayOfWeek, DateOnly today)
    {
        var first = new DateOnly(anyDayOfMonth.Year, anyDayOfMonth.Month, 1);
        var start = first.AddDays(-(((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7));
        var last = first.AddMonths(1).AddDays(-1);
        var days = new List<CalendarDay>();
        for (var day = start; day <= last || days.Count % 7 != 0; day = day.AddDays(1))
            days.Add(new CalendarDay(day.Day, day.Month == first.Month, day == today, day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
        return new CalendarMonth(days);
    }

    /// <summary>The weekday letters from the week's first day, in the culture's language.</summary>
    public static IReadOnlyList<string> Headers(CultureInfo culture, DayOfWeek firstDayOfWeek) =>
        [.. Enumerable.Range(0, 7).Select(offset => StringInfo.GetNextTextElement(culture.DateTimeFormat.ShortestDayNames[((int)firstDayOfWeek + offset) % 7]))];

    public static int WeekOf(DateOnly date) => ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));
}

/// <summary>A city for the clock's world clocks (M41a): its time zone (IANA id) and the name shown.</summary>
public sealed record WorldCity(string Id, string Name);

public static class WorldClocks
{
    public const int Max = 2;

    /// <summary>The cities offered on the clock's back (a short, well-known list; any IANA zone stored by hand works too).</summary>
    public static IReadOnlyList<WorldCity> Cities { get; } =
    [
        new("America/Los_Angeles", "Los Angeles"), new("America/Chicago", "Chicago"), new("America/New_York", "New York"),
        new("America/Sao_Paulo", "São Paulo"), new("Europe/London", "London"), new("Europe/Paris", "Paris"), new("Europe/Berlin", "Berlin"),
        new("Europe/Moscow", "Moscow"), new("Asia/Dubai", "Dubai"), new("Asia/Kolkata", "Kolkata"), new("Asia/Singapore", "Singapore"),
        new("Asia/Shanghai", "Shanghai"), new("Asia/Seoul", "Seoul"), new("Asia/Tokyo", "Tokyo"), new("Australia/Sydney", "Sydney"),
        new("Pacific/Auckland", "Auckland"), new("UTC", "UTC"),
    ];

    /// <summary>Known zones only, no repeats, at most <see cref="Max"/>.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? ids) =>
        [.. (ids ?? []).Where(Known).Distinct(StringComparer.OrdinalIgnoreCase).Take(Max)];

    /// <summary>The city's name and its time now, in the culture's short time format.</summary>
    public static (string Name, string Time) Show(string id, DateTimeOffset now, CultureInfo culture)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
        var name = Cities.FirstOrDefault(city => string.Equals(city.Id, id, StringComparison.OrdinalIgnoreCase))?.Name ?? id[(id.LastIndexOf('/') + 1)..].Replace('_', ' ');
        return (name, TimeZoneInfo.ConvertTime(now, zone).ToString(culture.DateTimeFormat.ShortTimePattern, culture));
    }

    private static bool Known(string id)
    {
        try { return !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.FindSystemTimeZoneById(id) is not null; }
        catch (Exception failure) when (failure is TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
    }
}

/// <summary>What plays now (M41a, spec §6.6): read from Windows' media sessions by the Shell.</summary>
public sealed record MediaTrack(string Title, string Artist, string Album, string App);

/// <summary>What the widget's face says: the track (or the last one, dimmed), its title and the line under it.</summary>
public sealed record NowPlayingFace(MediaTrack? Track, bool Idle, string Title, string Subtitle);

public static class NowPlaying
{
    private const char Minus = '−';

    /// <summary>Elapsed and left ("1:41", "−2:22"); a stream with no end is live (no times).</summary>
    public static (string Elapsed, string Left, bool Live) Timeline(TimeSpan position, TimeSpan? end)
    {
        if (end is not { } length || length <= TimeSpan.Zero) return ("", "", true);
        var at = position < TimeSpan.Zero ? TimeSpan.Zero : position > length ? length : position;
        return (Clock(at), $"{Minus}{Clock(length - at)}", false);
    }

    /// <summary>Nothing playing: the last song dimmed ("Last: …"), or just "Nothing playing".</summary>
    public static NowPlayingFace Face(MediaTrack? current, MediaTrack? last) =>
        current is { } playing ? new(playing, Idle: false, playing.Title is { Length: > 0 } title ? title : AppName(playing.App), playing.Artist) // M41b: untitled → the app
        : last is { } earlier ? new(earlier, Idle: true, "Nothing playing", $"Last: {earlier.Title} · {earlier.Artist}")
        : new(null, Idle: true, "Nothing playing", "");

    /// <summary>Windows updates a player's timeline now and then: while playing, the position runs on from that update (to the end).</summary>
    public static TimeSpan LivePosition(TimeSpan position, DateTimeOffset updated, DateTimeOffset now, bool playing, TimeSpan? end)
    {
        var live = playing && now > updated ? position + (now - updated) : position;
        return end is { } length && length > TimeSpan.Zero && live > length ? length : live;
    }

    private static readonly (string Key, string Name)[] KnownApps =
    [
        ("spotify", "Spotify"), ("msedge", "Edge"), ("chrome", "Chrome"), ("firefox", "Firefox"), ("zunemusic", "Media Player"),
        ("vlc", "VLC"), ("brave", "Brave"), ("opera", "Opera"), ("foobar2000", "foobar2000"), ("musicbee", "MusicBee"), ("itunes", "iTunes"),
        ("applemusic", "Apple Music"), ("tidal", "TIDAL"), ("deezer", "Deezer"), ("amazonmusic", "Amazon Music"), ("youtubemusic", "YouTube Music"),
    ];

    /// <summary>
    /// A player's name from the app id Windows gives its media session ("Spotify.exe", a Store app's "Publisher.App_…!App"):
    /// a known player's name, else the id's last part without ".exe".
    /// </summary>
    public static string AppName(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return "A player";
        var lower = appId.ToLowerInvariant();
        foreach (var (key, name) in KnownApps)
            if (lower.Contains(key, StringComparison.Ordinal)) return name;
        var last = appId[(appId.LastIndexOfAny(['!', '\\', '/']) + 1)..];
        return last.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? last[..^4] : last;
    }

    private static string Clock(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";
}

/// <summary>M41a (spec §6.5): fences that hold only widgets may float.</summary>
/// <summary>M41b (spec §7): touchpads send small wheel deltas; one volume step per 120 units (Windows' notch), the rest kept.</summary>
public static class VolumeWheel
{
    public static (int Steps, int Remainder) Add(int remainder, int delta)
    {
        var total = (Math.Sign(remainder) == -Math.Sign(delta) ? 0 : remainder) + delta; // turned back: the leftover goes (1.0.1)
        return (total / 120, total % 120);
    }
}

public static class WidgetBoards
{
    /// <summary>The fence holds widgets and nothing else (by its items' targets).</summary>
    public static bool OnlyWidgets(IReadOnlyCollection<string> targets) =>
        targets.Count > 0 && targets.All(target => target.StartsWith(Widgets.TargetPrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>"Let them float?" is offered once: when the fence becomes widgets only, not on every widget added after.</summary>
    public static bool OfferFloat(IReadOnlyCollection<string> before, IReadOnlyCollection<string> after) => OnlyWidgets(after) && !OnlyWidgets(before);
}
