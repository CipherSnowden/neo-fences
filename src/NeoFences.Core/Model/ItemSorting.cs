namespace NeoFences.Core.Model;

/// <summary>What sorting needs to know about one item (filled in by NeoFences.Shell).</summary>
public sealed record ItemInfo(string ItemRef, string Name, bool IsFolder, string TypeName, DateTimeOffset Modified);

/// <summary>
/// Item order for "Sort by" (one time) and the library's listing. Name and Type put folders first, like
/// Explorer; Date is newest first with folders mixed in, so the latest file is always on top (user choice 2026-10-03).
/// </summary>
public static class ItemSorting
{
    public static IReadOnlyList<string> Order(IEnumerable<ItemInfo> items, FenceSort sort) => (sort switch
    {
        FenceSort.Name => items.OrderBy(item => !item.IsFolder).ThenBy(item => item.Name, NaturalComparer.Instance),
        FenceSort.Type => items.OrderBy(item => !item.IsFolder).ThenBy(item => item.TypeName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, NaturalComparer.Instance),
        FenceSort.Date => items.OrderByDescending(item => item.Modified).ThenBy(item => item.Name, NaturalComparer.Instance),
        _ => items,
    }).Select(item => item.ItemRef).ToList();

    /// <summary>"setup2" before "setup10": runs of digits compare by value, the rest ignoring case (like Explorer).</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        public static NaturalComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (left is null || right is null) return string.Compare(left, right, StringComparison.Ordinal);
            var leftIndex = 0;
            var rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
                {
                    var leftEnd = leftIndex;
                    while (leftEnd < left.Length && char.IsDigit(left[leftEnd])) leftEnd++;
                    var rightEnd = rightIndex;
                    while (rightEnd < right.Length && char.IsDigit(right[rightEnd])) rightEnd++;
                    var leftDigits = left[leftIndex..leftEnd].TrimStart('0');
                    var rightDigits = right[rightIndex..rightEnd].TrimStart('0');
                    var byValue = leftDigits.Length != rightDigits.Length
                        ? leftDigits.Length.CompareTo(rightDigits.Length)
                        : string.CompareOrdinal(leftDigits, rightDigits);
                    if (byValue != 0) return byValue;
                    leftIndex = leftEnd;
                    rightIndex = rightEnd;
                    continue;
                }
                var byText = string.Compare(left[leftIndex].ToString(), right[rightIndex].ToString(), StringComparison.CurrentCultureIgnoreCase);
                if (byText != 0) return byText;
                leftIndex++;
                rightIndex++;
            }
            return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        }
    }
}
