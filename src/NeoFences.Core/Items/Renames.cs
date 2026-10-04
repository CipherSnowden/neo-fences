namespace NeoFences.Core.Items;

/// <summary>
/// Renames seen in watched folders, settled before any item follows them (final review C1). Editors (Word, Excel,
/// JetBrains, atomic savers) save by renaming the original away and a temp file onto its name; following the first
/// rename at once would point the item at a temp file that is deleted a moment later.
/// </summary>
public static class Renames
{
    /// <summary>
    /// The renames items should follow: chains joined (a → b, b → c is a → c), and only those whose old path is gone and
    /// whose new path exists now. A save leaves the original's path in place, so it is not followed.
    /// </summary>
    /// <param name="exists">True when a file or folder is at the path now (asked off the UI thread).</param>
    public static IReadOnlyList<(string OldPath, string NewPath)> Settle(IReadOnlyList<(string OldPath, string NewPath)> seen, Func<string, bool> exists)
    {
        var chains = new List<(string Origin, string Current)>();
        foreach (var (oldPath, newPath) in seen)
        {
            var index = chains.FindIndex(chain => ItemKinds.Comparer.Equals(chain.Current, oldPath));
            if (index >= 0) chains[index] = (chains[index].Origin, newPath);
            else chains.Add((oldPath, newPath));
        }
        return chains.Where(chain => !ItemKinds.Comparer.Equals(chain.Origin, chain.Current) && !exists(chain.Origin) && exists(chain.Current))
            .Select(chain => (chain.Origin, chain.Current)).ToList();
    }
}
