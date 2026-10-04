using System.Text.Json.Serialization;

namespace NeoFences.Core.Items;

/// <summary>An item's own icon (M18): an icon in a file (Windows' icon picker), or a picture copied into NeoFences' data.</summary>
public sealed record ItemIcon
{
    /// <summary>An .ico, .exe or .dll; <see cref="Index"/> is the icon's place in it.</summary>
    public string? File { get; init; }

    public int Index { get; init; }

    /// <summary>A picture's file name in NeoFences' <c>icons\</c> folder (PNG, copied in when chosen).</summary>
    public string? Image { get; init; }
}

/// <summary>What an item points at. A path may be a file or a folder: that is known only when it is checked.</summary>
public enum ItemKind { Path, Website, Special }

/// <summary>
/// A virtual item (ADR-040, spec 2026-10-04-virtual-items-design §1): NeoFences' own record of a target with its own
/// name, icon, arguments and note. The same target may be in many items; nothing NeoFences does touches the target.
/// </summary>
public sealed record VirtualItem
{
    public required string Id { get; init; }

    /// <summary>A file or folder path, a <c>::{GUID}</c> or <c>shell:</c> special item, or an http(s) URL.</summary>
    public required string Target { get; init; }

    /// <summary>Null or empty: Windows' display name for the target.</summary>
    public string? Name { get; init; }

    /// <summary>Null: the target's icon.</summary>
    public ItemIcon? Icon { get; init; }

    /// <summary>Files and apps only.</summary>
    public string? Arguments { get; init; }

    /// <summary>Files and apps only.</summary>
    public bool RunAsAdmin { get; init; }

    /// <summary>Shown as the item's tooltip.</summary>
    public string? Note { get; init; }

    [JsonIgnore]
    public ItemKind Kind => ItemKinds.Of(Target);

    /// <summary>The name the user gave it, or null when Windows' name is shown.</summary>
    [JsonIgnore]
    public string? OwnName => string.IsNullOrWhiteSpace(Name) ? null : Name;

    public static VirtualItem Create(string target) => new() { Id = NewId(), Target = target };

    public static string NewId() => Guid.NewGuid().ToString("N");
}

/// <summary>Target kinds, and what the Add item… box accepts.</summary>
public static class ItemKinds
{
    /// <summary>Targets compare ignoring case: Windows paths do, and a URL's host does.</summary>
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    public static ItemKind Of(string target) =>
        target.StartsWith("::", StringComparison.Ordinal) || target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? ItemKind.Special
        : IsWebsite(target) ? ItemKind.Website
        : ItemKind.Path;

    private const string AppsFolderPrefix = @"shell:AppsFolder\";

    /// <summary>
    /// What Explorer is given to open a special item or app: quoted, because a program's app id holds spaces and Explorer
    /// splits its command line at spaces and commas (M19 review). A <c>::{GUID}</c> becomes <c>shell:::{GUID}</c>.
    /// </summary>
    public static string ExplorerArgument(string target) =>
        $"\"{(target.StartsWith("::", StringComparison.Ordinal) ? "shell:" + target : target)}\"";

    /// <summary>An entry of Start's All apps (M19, ADR-042): <c>shell:AppsFolder\&lt;AppUserModelID&gt;</c>, a Store app or a program.</summary>
    public static bool IsApp(string target) => AppIdOf(target) is not null;

    /// <summary>
    /// A readable name for an app until (or when, uninstalled, never) Windows names it: a program's file name without its
    /// extension (its id is <c>{GUID}\folder\x.exe</c>), else the front of a Store app's id ("Microsoft.WindowsCalculator").
    /// </summary>
    public static string AppName(string appId) =>
        appId.Contains('\\') ? Path.GetFileNameWithoutExtension(appId) : appId.Split('_', '!')[0];

    /// <summary>The app's id (AppUserModelID), or null when the target is not an app.</summary>
    public static string? AppIdOf(string target) =>
        target.Length > AppsFolderPrefix.Length && target.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase)
            ? target[AppsFolderPrefix.Length..] : null;

    public static string AppTarget(string appId) => AppsFolderPrefix + appId;

    public static bool IsWebsite(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// A target as typed or pasted: trimmed, without the quotes of Explorer's "Copy as path", and "www.…" made a website.
    /// Null when nothing is left.
    /// </summary>
    public static string? Clean(string? typed)
    {
        var text = (typed ?? "").Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1].Trim();
        if (text.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) text = "https://" + text;
        return text.Length == 0 ? null : text;
    }

    /// <summary>Arguments and "Run as administrator" apply to files and apps, not folders, websites or special items.</summary>
    public static bool TakesArguments(ItemKind kind, bool isFolder) => kind == ItemKind.Path && !isFolder;

    /// <summary>A website's name before the user gives it one: its host without "www." ("github.com").</summary>
    public static string WebsiteName(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host)
            : url;
}
