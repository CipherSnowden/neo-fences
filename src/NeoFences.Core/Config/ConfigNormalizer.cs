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
            OpenItemsWith = Enum.IsDefined(settings.OpenItemsWith) ? settings.OpenItemsWith : defaults.OpenItemsWith, // M41a
            FenceMenu = Enum.IsDefined(settings.FenceMenu) ? settings.FenceMenu : defaults.FenceMenu,
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
                Labels = loadedFence.Labels is { } labels && Enum.IsDefined(labels) ? labels : null, // a hand-edited number follows All fences (M8b review, M41b)
                Tabs = loadedFence.Tabs ?? [], // a hand-edited "tabs": null (M9 final review)
                IconSize = loadedFence.IconSize is { } iconSize && IconSizes.Contains(iconSize) ? iconSize : null, // M41b: else All fences'
                CustomColor = Appearance.Argb.FromHex(loadedFence.CustomColor)?.ToHex(), // M14: a broken colour is none
                Layout = Enum.IsDefined(loadedFence.Layout) ? loadedFence.Layout : Items.FenceLayout.Flow, // M24: a typo is Flow
                Collect = Items.CollectRules.Normalize(loadedFence.Collect), // M27
                Look = NormalizeLook(loadedFence.Look), // M36
                OpenWith = loadedFence.OpenWith is { } openWith && Enum.IsDefined(openWith) ? openWith : null, // M41a
                Emoji = Emoji.Normalize(loadedFence.Emoji),
            };
            fence = fence with { View = fence.IsLibrary ? null : Items.FolderViews.Normalize(loadedFence.View) }; // M21: never on the Library
            seenFenceIds.Add(fence.Id);
            libraryFound |= fence.IsLibrary;
            fences.Add(fence);
        }

        return AllFences.Introduce(config with // M41b: once, after the repair (an odd own size already follows)
        {
            SchemaVersion = NeoFencesConfig.CurrentSchemaVersion,
            Settings = settings,
            Fences = FenceTabs.Repair(fences), // M9: one consistent box per tab
            Layouts = NormalizeLayouts(config.Layouts),
            Presets = NormalizePresets(config.Presets), // M36
            Library = NormalizeLibrary(config.Library) with
            {
                // M22: only a fence that holds items (not a folder view, not gone)
                NewGamesFence = fences.Any(fence => fence.Id == config.Library?.NewGamesFence && fence.View is null) ? config.Library!.NewGamesFence : null,
            },
        });
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
            Theme = Enum.IsDefined(appearance.Theme) ? appearance.Theme : defaults.Theme, // M41a
            TitleStyle = Enum.IsDefined(appearance.TitleStyle) ? appearance.TitleStyle : defaults.TitleStyle,
            NameStyle = Enum.IsDefined(appearance.NameStyle) ? appearance.NameStyle : defaults.NameStyle,
            Motion = Enum.IsDefined(appearance.Motion) ? appearance.Motion : defaults.Motion,
            IconSize = IconSizes.Contains(appearance.IconSize) ? appearance.IconSize : defaults.IconSize, // M41b
            Labels = Enum.IsDefined(appearance.Labels) ? appearance.Labels : defaults.Labels,
            TitleAlign = Enum.IsDefined(appearance.TitleAlign) ? appearance.TitleAlign : defaults.TitleAlign,
            Spacing = Enum.IsDefined(appearance.Spacing) ? appearance.Spacing : defaults.Spacing,
            TitleFont = new TitleFont(
                string.IsNullOrWhiteSpace(font?.Family) ? defaults.TitleFont.Family : font.Family.Trim(),
                font?.Size is { } size && TitleFont.Sizes.Contains(size) ? size : defaults.TitleFont.Size,
                font?.Weight is { } weight && Enum.IsDefined(weight) ? weight : defaults.TitleFont.Weight),
        };
    }

    /// <summary>
    /// M36: a fence's own look with odd parts dropped (they become "like all fences"); strength in range; a look with nothing
    /// left is none (null).
    /// </summary>
    public static OwnLook? NormalizeLook(OwnLook? look)
    {
        if (look is null) return null;
        var font = look.TitleFont is { } own
            ? new TitleFont(
                string.IsNullOrWhiteSpace(own.Family) ? null : own.Family.Trim(),
                own.Size is { } size && TitleFont.Sizes.Contains(size) ? size : null,
                own.Weight is { } weight && Enum.IsDefined(weight) ? weight : null)
            : null;
        var clean = new OwnLook
        {
            ColourStyle = look.ColourStyle is { } style && Enum.IsDefined(style) ? style : null,
            Strength = look.Strength is { } strength ? Math.Clamp(strength, 0, AppearanceSettings.MaxStrength) : null,
            TitleFont = font == new TitleFont(null, null, null) ? null : font,
            TitleAlign = look.TitleAlign is { } align && Enum.IsDefined(align) ? align : null,
            TitleOnHover = look.TitleOnHover == true ? true : null, // false is the same as like all fences: one way to say it
            Spacing = look.Spacing is { } spacing && Enum.IsDefined(spacing) ? spacing : null,
            Theme = look.Theme is { } theme && Enum.IsDefined(theme) ? theme : null, // M41a
            TitleStyle = look.TitleStyle is { } titleStyle && Enum.IsDefined(titleStyle) ? titleStyle : null,
        };
        return clean == new OwnLook() ? null : clean;
    }

    /// <summary>M36: own presets with a usable name (tidied, not blank, not a built-in's, the first of the same name); odd values dropped.</summary>
    private static List<Recipe> NormalizePresets(IReadOnlyList<Recipe>? presets)
    {
        var names = new HashSet<string>(Recipes.BuiltIn.Select(preset => preset.Name), StringComparer.OrdinalIgnoreCase);
        var kept = new List<Recipe>();
        foreach (var preset in presets ?? [])
        {
            if (preset is null) continue;
            var name = Recipes.Clean(preset.Name);
            if (name.Length == 0 || !names.Add(name)) continue;
            kept.Add(new Recipe
            {
                Name = name,
                Look = NormalizeLook(preset.Look) ?? new OwnLook(),
                IconSize = preset.IconSize is { } iconSize && IconSizes.Contains(iconSize) ? iconSize : null,
                Labels = preset.Labels is { } labels && Enum.IsDefined(labels) ? labels : null,
                Emoji = Emoji.Normalize(preset.Emoji), // M41b: recipes
                Layout = preset.Layout is { } layout && Enum.IsDefined(layout) ? layout : null,
                OpenWith = preset.OpenWith is { } openWith && Enum.IsDefined(openWith) ? openWith : null,
                Inbox = preset.Inbox,
            });
        }
        return kept;
    }

    private static LibrarySettings NormalizeLibrary(LibrarySettings? library) => new()
    {
        Folders = (library?.Folders ?? []).Where(folder => !string.IsNullOrWhiteSpace(folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        Sources = library?.Sources ?? new LibrarySources(),
        Hidden = (library?.Hidden ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        OnlineArt = library?.OnlineArt, // M34
        // M34: only a file name in NeoFences' covers folder counts (a hand-edited path never points elsewhere).
        CoverChoices = (library?.CoverChoices ?? new Dictionary<string, string>())
            .Where(choice => !string.IsNullOrWhiteSpace(choice.Key) && !string.IsNullOrWhiteSpace(WindowsPath.FileName(choice.Value ?? "")))
            .GroupBy(choice => choice.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => WindowsPath.FileName(group.First().Value), StringComparer.OrdinalIgnoreCase),
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
