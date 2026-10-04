using System.Text.Json;

namespace NeoFences.Core.Library;

/// <summary>One installed Steam app from its <c>appmanifest_&lt;appid&gt;.acf</c>.</summary>
public sealed record SteamApp(string AppId, string Name, string InstallDir);

/// <summary>Steam's library list and app manifests (M12). Parsing only; the Shell reads the files.</summary>
public static class SteamFiles
{
    /// <summary>Every library folder in <c>steamapps\libraryfolders.vdf</c>, in file order.</summary>
    /// <exception cref="FormatException">The file is damaged.</exception>
    public static IReadOnlyList<string> LibraryFolders(string vdfText)
    {
        var root = ValveKeyValues.Parse(vdfText);
        if (!root.TryGetValue("libraryfolders", out var foldersValue) || foldersValue is not IReadOnlyDictionary<string, object> folders) return [];
        return folders.Values.OfType<IReadOnlyDictionary<string, object>>()
            .Select(folder => folder.TryGetValue("path", out var path) ? path as string : null)
            .OfType<string>().Where(path => path.Length > 0).ToList();
    }

    /// <summary>The app in an <c>appmanifest_*.acf</c>, or null when it lacks an id, a name or an install folder (or is damaged).</summary>
    public static SteamApp? ParseManifest(string acfText)
    {
        try
        {
            if (ValveKeyValues.Parse(acfText).TryGetValue("AppState", out var stateValue) && stateValue is IReadOnlyDictionary<string, object> state
                && Text(state, "appid") is { } appId && Text(state, "name") is { } name && Text(state, "installdir") is { } installDir)
            {
                // Bit 4 = fully installed (also while updating); a queued download has a manifest too (final review I3).
                if (Text(state, "StateFlags") is { } flags && int.TryParse(flags, out var stateFlags) && (stateFlags & 4) == 0) return null;
                return new SteamApp(appId, name, installDir);
            }
        }
        catch (FormatException)
        {
            // a half-written manifest (Steam is installing): skipped this time
        }
        return null;
    }

    public static string LaunchUri(string appId) => $"steam://rungameid/{appId}";

    private static string? Text(IReadOnlyDictionary<string, object> block, string key) =>
        block.TryGetValue(key, out var value) && value is string { Length: > 0 } text ? text : null;
}

/// <summary>An installed Epic game from its launcher manifest (<c>Manifests\*.item</c>).</summary>
public sealed record EpicGame(string DisplayName, string InstallLocation, string? LaunchExecutable, string Namespace, string ItemId, string AppName)
{
    /// <summary>Starts the game through the Epic launcher (ownership, updates and cloud saves stay Epic's).</summary>
    public string LaunchUri => $"com.epicgames.launcher://apps/{Namespace}%3A{ItemId}%3A{AppName}?action=launch&silent=true";
}

public static class EpicManifest
{
    /// <summary>The game, or null for a damaged file, an unfinished install or missing fields.</summary>
    public static EpicGame? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return null;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
            if (Text("DisplayName") is not { } name || Text("InstallLocation") is not { } location || Text("AppName") is not { } appName) return null;
            // Only games: an add-on names its main game; Unreal Engine and other tools carry no "games" category (M13b).
            if (Text("MainGameAppName") is { } mainGame && !string.Equals(mainGame, appName, StringComparison.OrdinalIgnoreCase)) return null;
            if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array && categories.GetArrayLength() > 0
                && !categories.EnumerateArray().Any(category => category.ValueKind == JsonValueKind.String && category.GetString() == "games")) return null;
            return new EpicGame(name, location, Text("LaunchExecutable"), Text("CatalogNamespace") ?? "", Text("CatalogItemId") ?? "", appName);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
