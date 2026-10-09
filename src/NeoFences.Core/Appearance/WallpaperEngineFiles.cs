using System.Text.Json;

namespace NeoFences.Core.Appearance;

/// <summary>
/// Wallpaper Engine's own files (M14, spec §4), read only: <c>config.json</c> names each monitor's wallpaper, and the
/// wallpaper folder's <c>project.json</c> names its preview image. Pure: the Shell reads the files.
/// </summary>
public static class WallpaperEngineFiles
{
    /// <summary>
    /// Monitor index → the selected wallpaper's file, from <c>&lt;user&gt;.general.wallpaperconfig.selectedwallpapers</c>
    /// ("Monitor0", "Monitor1", …). The first user with a selection wins; anything unreadable gives none.
    /// </summary>
    public static IReadOnlyDictionary<int, string> SelectedWallpapers(string configJson)
    {
        var selected = new Dictionary<int, string>();
        try
        {
            using var document = JsonDocument.Parse(configJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return selected;
            foreach (var user in document.RootElement.EnumerateObject())
            {
                if (user.Value.ValueKind != JsonValueKind.Object
                    || !user.Value.TryGetProperty("general", out var general) || general.ValueKind != JsonValueKind.Object
                    || !general.TryGetProperty("wallpaperconfig", out var wallpaperConfig) || wallpaperConfig.ValueKind != JsonValueKind.Object
                    || !wallpaperConfig.TryGetProperty("selectedwallpapers", out var wallpapers) || wallpapers.ValueKind != JsonValueKind.Object) continue;
                foreach (var monitor in wallpapers.EnumerateObject())
                {
                    if (!monitor.Name.StartsWith("Monitor", StringComparison.OrdinalIgnoreCase)
                        || !int.TryParse(monitor.Name.AsSpan(7), out var index) || index < 0
                        || monitor.Value.ValueKind != JsonValueKind.Object
                        || !monitor.Value.TryGetProperty("file", out var file) || file.GetString() is not { Length: > 0 } path) continue;
                    selected[index] = path.Replace('/', '\\');
                }
                if (selected.Count > 0) return selected;
            }
        }
        catch (JsonException)
        {
            // a file WE is writing right now, or damaged: no selection (the next source is used)
        }
        return selected;
    }

    /// <summary>The wallpaper's title from <c>project.json</c> (shown in Settings), or null.</summary>
    public static string? TitleOf(string projectJson)
    {
        try
        {
            using var document = JsonDocument.Parse(projectJson);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("title", out var title)
                   && title.ValueKind == JsonValueKind.String && title.GetString() is { Length: > 0 } text ? text.Trim() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The preview's file name from <c>project.json</c>; null unless it is a plain name inside the wallpaper's folder.</summary>
    public static string? PreviewName(string projectJson)
    {
        try
        {
            using var document = JsonDocument.Parse(projectJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("preview", out var preview) || preview.ValueKind != JsonValueKind.String) return null;
            var name = preview.GetString();
            return name is { Length: > 0 } && WindowsPath.IsPlainFileName(name) ? name : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
