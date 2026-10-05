using System.IO.Enumeration;
using NeoFences.Core.Model;

namespace NeoFences.Core.Items;

/// <summary>What a rule collects (M27), by file name; several may be chosen.</summary>
[Flags]
public enum CollectKinds { None = 0, Apps = 1, Documents = 2, Pictures = 4, Archives = 8, Installers = 16, Anything = 32 }

/// <summary>
/// An auto-collect rule of a fence (M27, spec 2026-10-05-auto-collect-design §1): new entries of <see cref="Source"/> that
/// match become items in the fence. Never a file operation: an item only points at the file.
/// </summary>
public sealed record CollectRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>A full folder path, or <see cref="CollectRules.DesktopSource"/> (the user's and the Public Desktop).</summary>
    public string Source { get; init; } = CollectRules.DesktopSource;

    public CollectKinds Kinds { get; init; }

    /// <summary>File-name patterns that also match ("*.iso;*.pdf"), the folder-panel syntax.</summary>
    public string Patterns { get; init; } = "";

    /// <summary>When the rule last looked at its folder: entries created after it are new at the next start; null: never.</summary>
    public DateTimeOffset? Watermark { get; init; }
}

/// <param name="RuleId">The rule that collected it (logs).</param>
public sealed record CollectPlan(string FenceId, string RuleId, string Path);

/// <summary>The auto-collect rules (M27, ADR-049): kinds by name, matching, routing new arrivals to fences. Pure.</summary>
public static class CollectRules
{
    public const string DesktopSource = "desktop";

    /// <summary>At most this many items per rule from one burst (an unzip into a watched folder).</summary>
    public const int MaxPerBurst = 200;

    private static readonly HashSet<string> AppExtensions = new([".lnk", ".url", ".appref-ms", ".exe"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> InstallerExtensions = new([".msi", ".msix", ".msixbundle", ".appx", ".appxbundle"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DocumentExtensions = new([".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".rtf", ".txt", ".md", ".csv"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PictureExtensions = new([".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".svg"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ArchiveExtensions = new([".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz"], StringComparer.OrdinalIgnoreCase);

    private static readonly (CollectKinds Kind, string Name)[] KindNames =
        [(CollectKinds.Apps, "Apps and shortcuts"), (CollectKinds.Documents, "Documents"), (CollectKinds.Pictures, "Pictures"),
         (CollectKinds.Archives, "Archives"), (CollectKinds.Installers, "Installers"), (CollectKinds.Anything, "Anything")];

    private const CollectKinds AllKinds = CollectKinds.Apps | CollectKinds.Documents | CollectKinds.Pictures | CollectKinds.Archives | CollectKinds.Installers | CollectKinds.Anything;

    /// <summary>A file's kind by its name: an installer before an app (SteamSetup.exe), None for folders and anything else.</summary>
    public static CollectKinds KindOf(string name, bool isFolder)
    {
        if (isFolder) return CollectKinds.None;
        var extension = Path.GetExtension(name);
        if (InstallerExtensions.Contains(extension)) return CollectKinds.Installers;
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            && (name.Contains("setup", StringComparison.OrdinalIgnoreCase) || name.Contains("install", StringComparison.OrdinalIgnoreCase)))
            return CollectKinds.Installers;
        return AppExtensions.Contains(extension) ? CollectKinds.Apps
            : DocumentExtensions.Contains(extension) ? CollectKinds.Documents
            : PictureExtensions.Contains(extension) ? CollectKinds.Pictures
            : ArchiveExtensions.Contains(extension) ? CollectKinds.Archives
            : CollectKinds.None;
    }

    /// <summary>The rule's kinds or patterns match; a folder only by Anything or a pattern.</summary>
    public static bool Matches(CollectRule rule, string name, bool isFolder) =>
        rule.Kinds.HasFlag(CollectKinds.Anything)
        || (rule.Kinds & KindOf(name, isFolder)) != CollectKinds.None
        || (FolderViews.ParsePatterns(rule.Patterns) ?? []).Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true));

    public static bool SameSource(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase) || FolderViews.SameFolder(left, right);

    /// <summary>
    /// Where new arrivals of one source go (spec §2): for each, the first matching rule of that source in fence order (items
    /// fences only); nothing that any fence already holds (an item the user moved elsewhere stays there), nothing twice, at
    /// most <see cref="MaxPerBurst"/> per rule.
    /// </summary>
    public static IReadOnlyList<CollectPlan> Plan(NeoFencesConfig config, ItemsDocument items, string source, IReadOnlyList<ItemInfo> arrivals)
    {
        var held = items.Fences.Values.SelectMany(list => list).Select(item => item.Target).ToHashSet(ItemKinds.Comparer);
        var rules = config.Fences.Where(fence => fence.Kind == FenceKind.Items)
            .SelectMany(fence => fence.Collect.Where(rule => SameSource(rule.Source, source)).Select(rule => (FenceId: fence.Id, Rule: rule))).ToList();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var plan = new List<CollectPlan>();
        foreach (var arrival in arrivals)
        {
            if (!held.Add(arrival.ItemRef)) continue; // held already, or twice in this burst
            if (rules.FirstOrDefault(candidate => Matches(candidate.Rule, arrival.Name, arrival.IsFolder)) is not { Rule: { } rule } match) continue;
            var count = counts.GetValueOrDefault(rule.Id);
            if (count >= MaxPerBurst) continue;
            counts[rule.Id] = count + 1;
            plan.Add(new CollectPlan(match.FenceId, rule.Id, arrival.ItemRef));
        }
        return plan;
    }

    /// <summary>
    /// The entries that arrived: those not in the previous listing, or — with no previous listing (start, a drive back) —
    /// those created after the rule last looked. Never looked (no watermark): none.
    /// </summary>
    public static IReadOnlyList<ItemInfo> Arrivals(IReadOnlyList<ItemInfo>? before, IReadOnlyList<ItemInfo> now, DateTimeOffset? watermark)
    {
        if (before is not null)
        {
            var known = before.Select(entry => entry.ItemRef).ToHashSet(ItemKinds.Comparer);
            return [.. now.Where(entry => !known.Contains(entry.ItemRef))];
        }
        return watermark is { } since ? [.. now.Where(entry => entry.Created > since)] : [];
    }

    /// <summary>A hand-edited list repaired: known kinds, valid patterns, a desktop or full-path source (else the rule goes), unique ids.</summary>
    public static IReadOnlyList<CollectRule> Normalize(IReadOnlyList<CollectRule>? rules)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var repaired = new List<CollectRule>();
        foreach (var rule in rules ?? [])
        {
            if (rule is null) continue;
            var source = string.Equals(rule.Source?.Trim(), DesktopSource, StringComparison.OrdinalIgnoreCase) ? DesktopSource : FolderViews.FolderPath(rule.Source ?? "");
            if (source is null) continue;
            repaired.Add(rule with
            {
                Id = string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id) ? new CollectRule().Id : rule.Id,
                Source = source,
                Kinds = rule.Kinds & AllKinds,
                Patterns = FolderViews.ParsePatterns(rule.Patterns) is not null ? rule.Patterns ?? "" : "",
            });
            ids.Add(repaired[^1].Id);
        }
        return repaired;
    }

    /// <summary>"Desktop · Apps and shortcuts, Installers": the rule list's line.</summary>
    public static string Summary(CollectRule rule)
    {
        var source = rule.Source == DesktopSource ? "Desktop" : FolderViews.NameOf(rule.Source);
        List<string> parts = [.. KindNames.Where(kind => rule.Kinds.HasFlag(kind.Kind)).Select(kind => kind.Name), .. FolderViews.ParsePatterns(rule.Patterns) ?? []];
        return $"{source} · {(parts.Count == 0 ? "nothing chosen" : string.Join(", ", parts))}";
    }
}
