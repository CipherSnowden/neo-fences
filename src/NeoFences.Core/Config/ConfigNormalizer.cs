using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>
/// Repairs a loaded (possibly hand-edited) config: unique fence ids, no nulls, supported icon sizes and enum values, at
/// most one Game Library fence, consistent tabs, and layouts free of null or non-finite entries. Items are items.json's
/// (ItemEdits.Repair).
/// </summary>
public static class ConfigNormalizer
{
    public static IReadOnlyList<int> IconSizes { get; } = [32, 48, 64, 96];

    public static NeoFencesConfig Normalize(NeoFencesConfig config)
    {
        var defaults = new Settings();
        var settings = config.Settings ?? defaults;
        settings = settings with
        {
            PeekHotkey = string.IsNullOrWhiteSpace(settings.PeekHotkey) ? defaults.PeekHotkey : settings.PeekHotkey,
            RollupExpand = Enum.IsDefined(settings.RollupExpand) ? settings.RollupExpand : defaults.RollupExpand,
            DefaultLabels = Enum.IsDefined(settings.DefaultLabels) ? settings.DefaultLabels : defaults.DefaultLabels, // M8b review
            Appearance = NormalizeAppearance(settings.Appearance),
        };

        var seenFenceIds = new HashSet<string>(StringComparer.Ordinal);
        var libraryFound = false; // M12: at most one Game Library fence
        var fences = new List<Fence>();

        foreach (var loadedFence in config.Fences ?? [])
        {
            if (loadedFence is null) continue;
            var fence = loadedFence with
            {
                Id = string.IsNullOrWhiteSpace(loadedFence.Id) || !seenFenceIds.Add(loadedFence.Id) ? Fence.NewId() : loadedFence.Id,
                Title = loadedFence.Title ?? "",
                IsLibrary = loadedFence.IsLibrary && !libraryFound,
                Labels = Enum.IsDefined(loadedFence.Labels) ? loadedFence.Labels : LabelMode.Always, // a hand-edited number (M8b review)
                Tabs = loadedFence.Tabs ?? [], // a hand-edited "tabs": null (M9 final review)
                IconSize = IconSizes.Contains(loadedFence.IconSize) ? loadedFence.IconSize : 48,
                CustomColor = Appearance.Argb.FromHex(loadedFence.CustomColor)?.ToHex(), // M14: a broken colour is none
                Layout = Enum.IsDefined(loadedFence.Layout) ? loadedFence.Layout : Items.FenceLayout.Flow, // M24: a typo is Flow
            };
            fence = fence with { View = fence.IsLibrary ? null : Items.FolderViews.Normalize(loadedFence.View) }; // M21: never on the Library
            seenFenceIds.Add(fence.Id);
            libraryFound |= fence.IsLibrary;
            fences.Add(fence);
        }

        return config with
        {
            SchemaVersion = NeoFencesConfig.CurrentSchemaVersion,
            Settings = settings,
            Fences = FenceTabs.Repair(fences), // M9: one consistent box per tab
            Layouts = NormalizeLayouts(config.Layouts),
            Library = NormalizeLibrary(config.Library) with
            {
                // M22: only a fence that holds items (not a folder view, not gone)
                NewGamesFence = fences.Any(fence => fence.Id == config.Library?.NewGamesFence && fence.View is null) ? config.Library!.NewGamesFence : null,
            },
        };
    }

    /// <summary>M14: strengths in range, a known style, a whole global title font (spec §2).</summary>
    private static AppearanceSettings NormalizeAppearance(AppearanceSettings? appearance)
    {
        var defaults = new AppearanceSettings();
        if (appearance is null) return defaults;
        var font = appearance.TitleFont;
        return appearance with
        {
            StrengthDark = Math.Clamp(appearance.StrengthDark, 0, AppearanceSettings.MaxStrength),
            StrengthLight = Math.Clamp(appearance.StrengthLight, 0, AppearanceSettings.MaxStrength),
            ColourStyle = Enum.IsDefined(appearance.ColourStyle) ? appearance.ColourStyle : defaults.ColourStyle,
            TitleFont = new TitleFont(
                string.IsNullOrWhiteSpace(font?.Family) ? defaults.TitleFont.Family : font.Family.Trim(),
                font?.Size is { } size && TitleFont.Sizes.Contains(size) ? size : defaults.TitleFont.Size,
                font?.Weight is { } weight && Enum.IsDefined(weight) ? weight : defaults.TitleFont.Weight),
        };
    }

    private static LibrarySettings NormalizeLibrary(LibrarySettings? library) => new()
    {
        Folders = (library?.Folders ?? []).Where(folder => !string.IsNullOrWhiteSpace(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        Sources = library?.Sources ?? new LibrarySources(),
        Hidden = (library?.Hidden ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
    };

    private static Dictionary<string, Layout> NormalizeLayouts(IReadOnlyDictionary<string, Layout>? loadedLayouts)
    {
        var layouts = new Dictionary<string, Layout>();
        foreach (var (fingerprint, layout) in loadedLayouts ?? new Dictionary<string, Layout>())
        {
            if (layout is null) continue;
            layouts[fingerprint] = new Layout
            {
                Monitors = (layout.Monitors ?? new Dictionary<string, MonitorArea>())
                    .Where(entry => entry.Value is { IsUsable: true })
                    .ToDictionary(entry => entry.Key, entry => entry.Value),
                Fences = (layout.Fences ?? new Dictionary<string, FenceRect>())
                    .Where(entry => entry.Value is { } rect && !string.IsNullOrWhiteSpace(rect.Monitor)
                                    && double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.W) && double.IsFinite(rect.H))
                    .ToDictionary(entry => entry.Key, entry => entry.Value),
            };
        }
        return layouts;
    }
}
