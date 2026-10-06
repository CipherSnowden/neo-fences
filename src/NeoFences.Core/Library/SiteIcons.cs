using System.Text.RegularExpressions;

namespace NeoFences.Core.Library;

/// <summary>
/// Website icons (M34, ADR-055): the page's own declared icon (the largest), else <c>/favicon.ico</c>; without one, a
/// coloured letter badge instead of the browser's icon. Pure: the fetching is NeoFences.Shell's.
/// </summary>
public static partial class SiteIcons
{
    /// <summary>The badge colours (ARGB): Windows 11-like, readable with white text.</summary>
    public static IReadOnlyList<uint> BadgeColors { get; } = [0xFFC4302B, 0xFF0F6CBD, 0xFF107C10, 0xFF8764B8, 0xFFCA5010, 0xFF038387, 0xFFB146C2, 0xFF5C6970];

    /// <summary>The largest <c>&lt;link rel="icon" | "apple-touch-icon"&gt;</c> of a page, resolved against it; SVG skipped (WPF cannot draw it).</summary>
    public static Uri? DeclaredIcon(string html, Uri page)
    {
        Uri? best = null;
        var bestSize = -1;
        foreach (Match tag in LinkTag().Matches(html))
        {
            var attributes = Attributes(tag.Value);
            if (!attributes.TryGetValue("rel", out var rel) || !attributes.TryGetValue("href", out var href) || href.Length == 0) continue;
            var rels = rel.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var touch = rels.Contains("apple-touch-icon") || rels.Contains("apple-touch-icon-precomposed");
            if (!touch && !rels.Contains("icon")) continue;
            if (attributes.TryGetValue("type", out var type) && type.Contains("svg", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(page, href, out var url) || url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
            // An apple-touch icon without sizes is 180 px by convention; a plain icon without sizes is a small favicon.
            var size = attributes.TryGetValue("sizes", out var sizes) && SizeOf(sizes) is { } declared ? declared : touch ? 180 : 16;
            if (size <= bestSize) continue;
            (best, bestSize) = (url, size);
        }
        return best;
    }

    public static Uri FallbackIcon(Uri page) => new(page, "/favicon.ico");

    /// <summary>The first letter of the site's name (the host without "www.") and a colour that stays the same for it.</summary>
    public static (char Letter, uint Color) Badge(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Host.Length == 0) return ('?', BadgeColors[^1]);
        var host = parsed.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? parsed.Host[4..] : parsed.Host;
        var letter = host.FirstOrDefault(char.IsLetterOrDigit);
        // FNV-1a: string.GetHashCode differs per run.
        var hash = 2166136261u;
        foreach (var character in host.ToLowerInvariant()) hash = (hash ^ character) * 16777619u;
        return (letter == default ? '?' : char.ToUpperInvariant(letter), BadgeColors[(int)(hash % (uint)BadgeColors.Count)]);
    }

    /// <summary>Never fetched: yes. Found: no. A miss: after 30 days.</summary>
    public static bool ShouldFetch(SiteRecord? record, DateTimeOffset now) =>
        record is null || record.File is null && now - record.Checked > GameArt.MissRetry;

    private static int? SizeOf(string sizes)
    {
        var largest = (int?)null;
        foreach (var size in sizes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = size.ToLowerInvariant().Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out var width) && (largest is null || width > largest)) largest = width;
        }
        return largest;
    }

    private static Dictionary<string, string> Attributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match attribute in Attribute().Matches(tag))
        {
            attributes.TryAdd(attribute.Groups["name"].Value, System.Net.WebUtility.HtmlDecode(attribute.Groups["value"].Value.Trim()));
        }
        return attributes;
    }

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex("""(?<name>[a-zA-Z-]+)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s>]+))""")]
    private static partial Regex Attribute();
}
