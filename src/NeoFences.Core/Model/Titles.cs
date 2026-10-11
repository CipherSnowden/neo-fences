using System.Globalization;
using NeoFences.Core.Items;

namespace NeoFences.Core.Model;

/// <summary>A fence's emoji (M41a, spec §2): one grapheme — what a person sees as one character — or none.</summary>
public static class Emoji
{
    public static string? Normalize(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;
        var first = StringInfo.GetNextTextElement(typed.Trim());
        return first.Length is 0 or > 16 ? null : first; // 16 UTF-16 units: the longest family or flag sequences fit
    }
}

/// <summary>What a fence's title shows besides its name (M41a, spec §2).</summary>
public static class Titles
{
    /// <summary>The count beside the title: the fence's items by their targets, widgets left out; empty when there are none.</summary>
    public static string CountText(IEnumerable<string> targets, CultureInfo culture)
    {
        var count = targets.Count(target => !target.StartsWith(Widgets.TargetPrefix, StringComparison.OrdinalIgnoreCase));
        return count == 0 ? "" : count.ToString(culture);
    }
}
