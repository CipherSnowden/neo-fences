namespace NeoFences.Core.Items;

/// <summary>
/// The bulk fix after one Locate… (M19, spec 2026-10-05 §2, ADR-042): where the located item moved tells where its
/// neighbours went, and the other missing items from the same old place are offered at once.
/// </summary>
public static class Relocation
{
    /// <summary>The differing front of the old and new path: everything under <see cref="OldBase"/> is now under <see cref="NewBase"/>.</summary>
    public sealed record Bases(string OldBase, string NewBase);

    /// <summary>One item's proposed new target.</summary>
    public sealed record Move(string ItemId, string OldTarget, string NewTarget);

    /// <summary>The undo snapshot's name: "Before fixing 1 item (…)", "Before fixing 7 items (…)".</summary>
    public static string UndoName(int count, string when) => $"Before fixing {count} item{(count == 1 ? "" : "s")} ({when})";

    /// <summary>
    /// Compares the two paths segment by segment from the end (ignoring case and a trailing "\"): what they share at the
    /// end stayed, the rest moved. Null when the last segment (the file or folder name) differs, nothing moved, or
    /// either path has no drive or share root.
    /// </summary>
    public static Bases? Find(string oldTarget, string newTarget)
    {
        if (Split(oldTarget) is not { } old || Split(newTarget) is not { } found) return null;
        var shared = 0;
        while (shared < old.Parts.Length && shared < found.Parts.Length
               && ItemKinds.Comparer.Equals(old.Parts[^(shared + 1)], found.Parts[^(shared + 1)]))
        {
            shared++;
        }
        if (shared == 0) return null;
        var oldBase = Join(old.Root, old.Parts[..^shared]);
        var newBase = Join(found.Root, found.Parts[..^shared]);
        return ItemKinds.Comparer.Equals(oldBase, newBase) ? null : new Bases(oldBase, newBase);
    }

    /// <summary>
    /// The other items (not <paramref name="exceptItemId"/>), in every fence and in fence order, that point at a file or
    /// folder under <see cref="Bases.OldBase"/> and are Missing or Unavailable, with their target under the new base.
    /// Websites, apps, special items and items that are fine are never moved.
    /// </summary>
    public static IReadOnlyList<Move> Candidates(ItemsDocument document, Func<string, TargetState> stateOf, Bases bases, string exceptItemId)
    {
        var oldPrefix = bases.OldBase.TrimEnd('\\') + "\\";
        var newPrefix = bases.NewBase.TrimEnd('\\') + "\\";
        var moves = new List<Move>();
        foreach (var item in document.Fences.Values.SelectMany(items => items))
        {
            if (item.Id == exceptItemId || item.Kind != ItemKind.Path) continue;
            var target = item.Target.TrimEnd('\\');
            string? moved = ItemKinds.Comparer.Equals(target, bases.OldBase.TrimEnd('\\')) ? bases.NewBase
                : target.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase) ? newPrefix + target[oldPrefix.Length..]
                : null;
            if (moved is null || stateOf(item.Target) is not (TargetState.Missing or TargetState.Unavailable)) continue;
            moves.Add(new Move(item.Id, item.Target, moved));
        }
        return moves;
    }

    /// <summary>"D:\" + ["Games", "X"] or "\\nas\share\" + [...]; null without a root.</summary>
    private static (string Root, string[] Parts)? Split(string path)
    {
        var trimmed = path.TrimEnd('\\');
        if (TargetChecks.RootOf(trimmed + "\\") is not { } root) return null;
        var rest = trimmed.Length > root.Length ? trimmed[root.Length..] : "";
        return (root, rest.Split('\\', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>A drive root keeps its "\" ("D:\"); anything longer has none ("D:\Games", "\\nas\share").</summary>
    private static string Join(string root, string[] parts)
    {
        var joined = root + string.Join('\\', parts);
        return joined.Length == 3 && joined[1] == ':' ? joined : joined.TrimEnd('\\');
    }
}
