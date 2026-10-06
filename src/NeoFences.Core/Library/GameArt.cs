using System.Text;
using NeoFences.Core.Items;

namespace NeoFences.Core.Library;

/// <summary>A tile's art (M12, M34): a 2:3 poster fills the tile; a logo is centred like an icon.</summary>
public sealed record CoverArt(string Path, bool IsPoster);

/// <summary>
/// Where a game's art comes from (M34, spec 2026-10-06-icons-and-game-tiles-design §1–2, ADR-055): the user's choice, the
/// launcher's cover on disk, a cover found online, the launcher's logo, else none (the glow tile). Strict names for the
/// online lookup and when to look up again. Pure.
/// </summary>
public static class GameArt
{
    /// <summary>A miss is asked again after this long (or at once when the game's name changes).</summary>
    public static readonly TimeSpan MissRetry = TimeSpan.FromDays(30);

    private static readonly string[] PictureExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    /// <param name="chosen">"Choose cover…" (a file in NeoFences' covers folder).</param>
    /// <param name="poster">The launcher's 2:3 cover on disk (Steam).</param>
    /// <param name="online">A cover found online (the covers index).</param>
    /// <param name="logo">The launcher's logo (an Xbox package's); a program's path is no picture.</param>
    public static CoverArt? Choose(string? chosen, string? poster, string? online, string? logo)
    {
        // ponytail: a logo comes after an online cover (ruling at planning: a real cover beats a centred square logo).
        if (chosen is not null) return new CoverArt(chosen, IsPoster: true);
        if (poster is not null) return new CoverArt(poster, IsPoster: true);
        if (online is not null) return new CoverArt(online, IsPoster: true);
        return logo is not null && PictureExtensions.Contains(Path.GetExtension(logo).ToLowerInvariant()) ? new CoverArt(logo, IsPoster: false) : null;
    }

    /// <summary>The online lookup runs for games without a chosen cover or a cover on disk (a logo still looks up).</summary>
    public static bool NeedsLookup(string? chosen, string? poster) => chosen is null && poster is null;

    /// <summary>Strict: equal after ignoring case, punctuation, spacing and ™ ® ©; an empty name never matches.</summary>
    public static bool SameName(string ours, string theirs)
    {
        var normal = Normalize(ours);
        return normal.Length > 0 && normal == Normalize(theirs);
    }

    /// <summary>The name for a store search: punctuation as spaces (Steam finds nothing for "Clair Obscur - Expedition 33").</summary>
    public static string SearchTerm(string name)
    {
        var term = new StringBuilder(name.Length);
        foreach (var character in name) term.Append(char.IsLetterOrDigit(character) ? character : ' ');
        return string.Join(' ', term.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Never asked: yes. Found: no. A miss: after <see cref="MissRetry"/>, or at once when the name changed.</summary>
    public static bool ShouldLookUp(CoverRecord? record, string name, DateTimeOffset now) =>
        record is null
        || record.File is null && (!string.Equals(record.Name, name, StringComparison.Ordinal) || now - record.Checked > MissRetry);

    private static string Normalize(string name)
    {
        var normal = new StringBuilder(name.Length);
        foreach (var character in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character)) normal.Append(character);
        }
        return normal.ToString();
    }
}

/// <summary>
/// Game cover sizes (M34, owner's choice at planning): Normal (1×2, the default) and Large (2×4, twice). A cover is
/// 2:3 and fills its cells; an older size maps to the nearest: one column Normal, two or more Large.
/// </summary>
public static class CoverSizes
{
    public static GridSpan Normal { get; } = new(1, 2);
    public static GridSpan Large { get; } = new(2, 4);

    public static GridSpan SpanOf(GridSpan? size) => size is { Columns: >= 2 } ? Large : Normal;
}
