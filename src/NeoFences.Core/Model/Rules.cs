using System.IO.Enumeration;
using NeoFences.Core.Membership;

namespace NeoFences.Core.Model;

public enum RuleKind { Type, Game, Name, Age, Size }

public enum TypeGroup { ShortcutsApps, Images, Videos, Documents, Archives, Folders }

/// <summary>Folder: shortcuts into a folder of the user's own (games outside any launcher, user choice 2026-10-03).</summary>
public enum GameLauncher { Any, Steam, Epic, Ubisoft, Ea, BattleNet, Gog, Folder }

public enum RuleCompare { OlderThan, NewerThan, BiggerThan, SmallerThan }

/// <summary>What a rule looks at (M11). Only the fields of its <see cref="Kind"/> matter.</summary>
public sealed record RuleCondition
{
    public RuleKind Kind { get; init; }
    /// <summary>Type: a ready-made group, or <see cref="Extensions"/> instead.</summary>
    public TypeGroup? Group { get; init; }
    /// <summary>Type: custom extensions, ".iso .torrent" (dots optional; spaces or commas between).</summary>
    public string? Extensions { get; init; }
    public GameLauncher? Launcher { get; init; }
    /// <summary>Game with <see cref="GameLauncher.Folder"/>: the folder the shortcuts point into, like D:\GameLibrary.</summary>
    public string? Folder { get; init; }
    /// <summary>Name: * and ? wildcards; plain text means "contains". Case-insensitive.</summary>
    public string? Pattern { get; init; }
    public RuleCompare? Compare { get; init; }
    public double? Days { get; init; }
    public double? Megabytes { get; init; }
}

/// <summary>"When the condition matches, put the item in this fence" (M11). Rules are ordered; the first match wins.</summary>
public sealed record Rule
{
    public string Id { get; init; } = ""; // not required: a hand-edited rule without one is repaired, not a corrupt file
    public bool Enabled { get; init; } = true;
    public RuleCondition Condition { get; init; } = new();
    public string FenceId { get; init; } = "";

    public static Rule Create(RuleCondition condition, string fenceId) =>
        new() { Id = Guid.NewGuid().ToString("N"), Condition = condition, FenceId = fenceId };
}

/// <summary>What NeoFences knows about a desktop item, read by the Shell layer (M11).</summary>
/// <param name="ShortcutTarget">For .lnk: target path and arguments; for .url: the URL. Null otherwise or when unreadable.</param>
public sealed record ItemFacts(string ItemRef, string Name, string Extension, bool IsFolder, long? SizeBytes, DateTimeOffset? Modified, string? ShortcutTarget);

/// <summary>
/// Rules auto-sort (M11, spec 2026-10-03-rules-design): which fence a desktop item belongs in. Pure: the App reads the
/// facts and saves the result. Membership only; nothing touches files.
/// </summary>
public static class Rules
{
    public static readonly IReadOnlyDictionary<TypeGroup, string[]> Groups = new Dictionary<TypeGroup, string[]>
    {
        [TypeGroup.ShortcutsApps] = [".lnk", ".url", ".exe", ".appref-ms", ".msi", ".bat", ".cmd"],
        [TypeGroup.Images] = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".svg", ".ico"],
        [TypeGroup.Videos] = [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v"],
        [TypeGroup.Documents] = [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".md", ".csv"],
        [TypeGroup.Archives] = [".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".cab"],
        [TypeGroup.Folders] = [],
    };

    private static readonly (string Prefix, GameLauncher Launcher)[] LauncherUrls =
    [
        ("steam://", GameLauncher.Steam), ("com.epicgames.launcher://", GameLauncher.Epic), ("uplay://", GameLauncher.Ubisoft),
        ("origin://", GameLauncher.Ea), ("origin2://", GameLauncher.Ea), ("ea://", GameLauncher.Ea), ("link2ea://", GameLauncher.Ea),
        ("battlenet://", GameLauncher.BattleNet), ("goggalaxy://", GameLauncher.Gog),
    ];

    private static readonly (string Part, GameLauncher Launcher)[] LibraryFolders =
    [
        (@"\steamapps\common\", GameLauncher.Steam), (@"\Epic Games\", GameLauncher.Epic),
        (@"\Ubisoft Game Launcher\games\", GameLauncher.Ubisoft), (@"\EA Games\", GameLauncher.Ea),
        (@"\GOG Galaxy\Games\", GameLauncher.Gog), (@"\GOG Games\", GameLauncher.Gog),
    ];

    /// <summary>The game launcher a shortcut belongs to (its URL scheme, library folder or launcher arguments), or null.</summary>
    public static GameLauncher? LauncherOf(string? shortcutTarget)
    {
        if (string.IsNullOrWhiteSpace(shortcutTarget)) return null;
        var target = shortcutTarget.Trim();
        foreach (var (prefix, launcher) in LauncherUrls)
        {
            // The link itself, or the link handed to the launcher as an argument (M13b): "steam.exe steam://rungameid/570".
            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || target.Contains(" " + prefix, StringComparison.OrdinalIgnoreCase) || target.Contains("\"" + prefix, StringComparison.OrdinalIgnoreCase)) return launcher;
        }
        // The Epic launcher itself lives under "\Epic Games\Launcher\": not a game.
        foreach (var (part, launcher) in LibraryFolders)
        {
            if (target.Contains(part, StringComparison.OrdinalIgnoreCase) && !target.Contains(@"\Epic Games\Launcher\", StringComparison.OrdinalIgnoreCase)) return launcher;
        }
        if (target.Contains("steam.exe", StringComparison.OrdinalIgnoreCase) && target.Contains("-applaunch", StringComparison.OrdinalIgnoreCase)) return GameLauncher.Steam;
        if (target.Contains("battle.net.exe", StringComparison.OrdinalIgnoreCase) && target.Contains("--exec", StringComparison.OrdinalIgnoreCase)) return GameLauncher.BattleNet;
        return null;
    }

    private static readonly string[] DownloadExtensions = [".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp"];

    /// <summary>
    /// A rename from a download or temp name (Chrome's .crdownload, Firefox's .part, save-then-rename .tmp) is a new item
    /// for rules; a rename the user makes is not (final review I2).
    /// </summary>
    public static bool IsDownloadRename(string oldItemRef) =>
        DownloadExtensions.Contains(Path.GetExtension(oldItemRef), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether one condition matches an item (a broken condition never matches).</summary>
    public static bool Matches(RuleCondition condition, ItemFacts facts, DateTimeOffset now) => condition.Kind switch
    {
        RuleKind.Type when condition.Group == TypeGroup.Folders => facts.IsFolder,
        RuleKind.Type when condition.Group is { } group && Groups.TryGetValue(group, out var extensions) =>
            !facts.IsFolder && extensions.Contains(facts.Extension, StringComparer.OrdinalIgnoreCase),
        RuleKind.Type when condition.Group is null => !facts.IsFolder && ExtensionsOf(condition.Extensions).Contains(facts.Extension, StringComparer.OrdinalIgnoreCase),
        RuleKind.Game when condition.Launcher == GameLauncher.Folder => InFolder(facts.ShortcutTarget, condition.Folder),
        RuleKind.Game => LauncherOf(facts.ShortcutTarget) is { } launcher && (condition.Launcher is GameLauncher.Any or null || condition.Launcher == launcher),
        RuleKind.Name when !string.IsNullOrWhiteSpace(condition.Pattern) => NameMatches(condition.Pattern.Trim(), facts.Name),
        RuleKind.Age when facts.Modified is { } modified && condition.Days is { } days => condition.Compare switch
        {
            RuleCompare.OlderThan => (now - modified).TotalDays > days, // no TimeSpan: "99999999 days" must not overflow (final review C1)
            RuleCompare.NewerThan => (now - modified).TotalDays < days,
            _ => false,
        },
        RuleKind.Size when !facts.IsFolder && facts.SizeBytes is { } size && condition.Megabytes is { } megabytes => condition.Compare switch
        {
            RuleCompare.BiggerThan => size > megabytes * 1024 * 1024,
            RuleCompare.SmallerThan => size < megabytes * 1024 * 1024,
            _ => false,
        },
        _ => false,
    };

    /// <summary>The fence of the first enabled rule that matches and names an existing desktop fence; null = stays put.</summary>
    public static string? Match(ItemFacts facts, NeoFencesConfig config, DateTimeOffset now)
    {
        var desktopFenceIds = config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        return config.Rules.FirstOrDefault(rule => rule.Enabled && desktopFenceIds.Contains(rule.FenceId) && Matches(rule.Condition, facts, now))?.FenceId;
    }

    /// <summary>Moves every matched item to the end of its rule's fence, in the order given; the same config when nothing moves.</summary>
    public static NeoFencesConfig File(NeoFencesConfig config, IReadOnlyList<ItemFacts> facts, DateTimeOffset now)
    {
        foreach (var item in facts)
        {
            if (Match(item, config, now) is not { } fenceId) continue;
            var owner = config.Fences.FirstOrDefault(fence => fence.Items.Contains(item.ItemRef, ItemRef.Comparer));
            if (owner is null || owner.Id == fenceId) continue; // gone meanwhile, or already there
            config = FenceMembership.MoveItem(config, item.ItemRef, fenceId);
        }
        return config;
    }

    /// <summary>How many of these items <see cref="File"/> would move (the "N items moved" notice).</summary>
    public static int CountMoves(NeoFencesConfig config, IReadOnlyList<ItemFacts> facts, DateTimeOffset now) =>
        facts.Count(item => Match(item, config, now) is { } fenceId
                            && config.Fences.FirstOrDefault(fence => fence.Items.Contains(item.ItemRef, ItemRef.Comparer)) is { } owner
                            && owner.Id != fenceId);

    /// <summary>A rule as one line for Settings: "Images → Pictures".</summary>
    public static string Describe(Rule rule, NeoFencesConfig config)
    {
        var condition = rule.Condition;
        var what = condition.Kind switch
        {
            RuleKind.Type when condition.Group is { } group => GroupName(group),
            RuleKind.Type => $"Extensions {string.Join(" ", ExtensionsOf(condition.Extensions))}",
            RuleKind.Game when condition.Launcher == GameLauncher.Folder => $"Game shortcuts in {condition.Folder}",
            RuleKind.Game => condition.Launcher is GameLauncher.Any or null ? "Game shortcuts (any launcher)" : $"{LauncherName(condition.Launcher.Value)} game shortcuts",
            RuleKind.Name => $"Name like \"{condition.Pattern}\"",
            RuleKind.Age => $"{(condition.Compare == RuleCompare.NewerThan ? "Newer" : "Older")} than {condition.Days:0.##} days",
            RuleKind.Size => $"{(condition.Compare == RuleCompare.SmallerThan ? "Smaller" : "Bigger")} than {condition.Megabytes:0.##} MB",
            _ => "(broken rule)",
        };
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == rule.FenceId && candidate.Source.Kind == FenceSourceKind.Desktop);
        return $"{what} → {(fence is null ? "(fence missing)" : fence.Title)}";
    }

    public static string GroupName(TypeGroup group) => group == TypeGroup.ShortcutsApps ? "Shortcuts and apps" : group.ToString();

    public static string LauncherName(GameLauncher launcher) => launcher switch
    {
        GameLauncher.Ea => "EA",
        GameLauncher.BattleNet => "Battle.net",
        GameLauncher.Gog => "GOG",
        _ => launcher.ToString(),
    };

    /// <summary>Load-time repair: a rule whose condition cannot work is kept but disabled (M11 spec §2).</summary>
    public static Rule Repair(Rule rule)
    {
        var condition = rule.Condition ?? new RuleCondition { Kind = (RuleKind)(-1) };
        var valid = condition.Kind switch
        {
            RuleKind.Type => condition.Group is { } group ? Enum.IsDefined(group) : ExtensionsOf(condition.Extensions).Count > 0,
            RuleKind.Game when condition.Launcher == GameLauncher.Folder => !string.IsNullOrWhiteSpace(condition.Folder),
            RuleKind.Game => condition.Launcher is null || Enum.IsDefined(condition.Launcher.Value),
            RuleKind.Name => !string.IsNullOrWhiteSpace(condition.Pattern),
            RuleKind.Age => condition.Days is >= 0 && condition.Compare is RuleCompare.OlderThan or RuleCompare.NewerThan,
            RuleKind.Size => condition.Megabytes is >= 0 && condition.Compare is RuleCompare.BiggerThan or RuleCompare.SmallerThan,
            _ => false,
        };
        var repaired = rule with
        {
            Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id,
            FenceId = rule.FenceId ?? "",
            Condition = condition,
        };
        return valid ? repaired : repaired with { Enabled = false };
    }

    /// <summary>Whether a shortcut points inside a folder (the folder itself, not a name starting the same).</summary>
    private static bool InFolder(string? shortcutTarget, string? folder) =>
        !string.IsNullOrWhiteSpace(shortcutTarget) && !string.IsNullOrWhiteSpace(folder)
        && shortcutTarget.Trim().StartsWith(folder.Trim().TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase);

    private static List<string> ExtensionsOf(string? text) =>
        (text ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(extension => extension.StartsWith('.') ? extension : "." + extension).ToList();

    private static bool NameMatches(string pattern, string name) =>
        pattern.Contains('*') || pattern.Contains('?')
            ? FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true)
            : name.Contains(pattern, StringComparison.OrdinalIgnoreCase);
}
