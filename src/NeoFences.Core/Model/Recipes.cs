using NeoFences.Core.Config;

namespace NeoFences.Core.Model;

/// <summary>
/// A recipe (M41b, spec 2026-10-11-m41b-settings-design §4; was M36's look preset): a fence's look and behaviour — never its
/// items, place or size. Applied, it is copied onto the fence (a copy, not a link); the fence keeps its colour and emoji.
/// </summary>
public sealed record Recipe
{
    public string Name { get; init; } = "";

    /// <summary>The card's emoji (not applied to the fence).</summary>
    public string? Emoji { get; init; }

    public OwnLook Look { get; init; } = new();

    public int? IconSize { get; init; }

    public LabelMode? Labels { get; init; }

    public Items.FenceLayout? Layout { get; init; }

    public OpenWith? OpenWith { get; init; }

    /// <summary>Downloads inbox: a folder panel in the fence shows Details, newest first (the App applies it to items.json).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Inbox { get; init; }
}

/// <summary>The built-in recipes and the owner's own (M41b, ADR-067; JSON key "presets" kept). Pure: each returns a new config.</summary>
public static class Recipes
{
    public const int MaxNameLength = 40;

    /// <summary>Game shelf, Work desk, Downloads inbox, Minimal (the owner's picks, recipes.html).</summary>
    public static IReadOnlyList<Recipe> BuiltIn { get; } =
    [
        new() { Name = "Game shelf", Emoji = "🎮", Look = new OwnLook { Theme = FenceTheme.Hud, TitleStyle = TitleStyle.Caps, Strength = 60 }, IconSize = 96, Labels = LabelMode.OnHover, OpenWith = Model.OpenWith.OneClick },
        new() { Name = "Work desk", Emoji = "💼", Look = new OwnLook { Theme = FenceTheme.LightMica, TitleStyle = TitleStyle.Clean, Spacing = Spacing.Compact }, IconSize = 32, Labels = LabelMode.Always, OpenWith = Model.OpenWith.DoubleClick },
        new() { Name = "Downloads inbox", Emoji = "📥", Look = new OwnLook { Theme = FenceTheme.Aurora, TitleStyle = TitleStyle.Clean }, Labels = LabelMode.Always, OpenWith = Model.OpenWith.DoubleClick, Inbox = true },
        new() { Name = "Minimal", Emoji = "◻", Look = new OwnLook { Theme = FenceTheme.Aurora, TitleStyle = TitleStyle.Caps, Strength = 10, TitleOnHover = true }, Labels = LabelMode.OnHover },
    ];

    /// <summary>The built-ins, then the owner's own.</summary>
    public static IReadOnlyList<Recipe> All(NeoFencesConfig config) => [.. BuiltIn, .. config.Presets];

    /// <summary>One fence: the recipe's parts become the fence's own; parts the recipe has not stay.</summary>
    /// <exception cref="ArgumentException">No fence with that id.</exception>
    public static NeoFencesConfig Apply(NeoFencesConfig config, string fenceId, Recipe recipe)
    {
        var fence = Require(config, fenceId);
        var look = recipe.Look;
        var mine = fence.Look ?? new OwnLook();
        return config.WithFence(fence with
        {
            // Part by part (1.0.1): a part the recipe has not keeps the fence's own value.
            Look = ConfigNormalizer.NormalizeLook(new OwnLook
            {
                Theme = look.Theme ?? mine.Theme, TitleStyle = look.TitleStyle ?? mine.TitleStyle, ColourStyle = look.ColourStyle ?? mine.ColourStyle,
                Strength = look.Strength ?? mine.Strength, TitleFont = look.TitleFont ?? mine.TitleFont, TitleAlign = look.TitleAlign ?? mine.TitleAlign,
                TitleOnHover = look.TitleOnHover ?? mine.TitleOnHover, Spacing = look.Spacing ?? mine.Spacing,
            }),
            IconSize = recipe.IconSize ?? fence.IconSize,
            Labels = recipe.Labels ?? fence.Labels,
            OpenWith = recipe.OpenWith ?? fence.OpenWith,
            // A recipe's Layout is the App's to apply (final review): to Free, a fence first pins its cells where they show, and
            // only a fence of items has a layout.
        });
    }

    /// <summary>
    /// Every fence (null): the recipe becomes All fences' values and every fence follows them; some fences: each gets the
    /// recipe as its own (the others keep following). Places, sizes, items and layouts are never touched (the App applies a
    /// recipe's Layout per fence, pinning cells first).
    /// </summary>
    public static NeoFencesConfig ApplyToAll(NeoFencesConfig config, Recipe recipe, IReadOnlyCollection<string>? onlyFenceIds)
    {
        if (onlyFenceIds is not null) return onlyFenceIds.Aggregate(config, (changed, fenceId) => Apply(changed, fenceId, recipe));
        var look = recipe.Look;
        var appearance = config.Settings.Appearance;
        appearance = appearance with
        {
            Theme = look.Theme ?? appearance.Theme,
            TitleStyle = look.TitleStyle ?? appearance.TitleStyle,
            ColourStyle = look.ColourStyle ?? appearance.ColourStyle,
            TitleAlign = look.TitleAlign ?? appearance.TitleAlign,
            TitleOnHover = look.TitleOnHover ?? appearance.TitleOnHover,
            Spacing = look.Spacing ?? appearance.Spacing,
            IconSize = recipe.IconSize ?? appearance.IconSize,
            Labels = recipe.Labels ?? appearance.Labels,
            TitleFont = look.TitleFont is { } font
                ? new TitleFont(font.Family ?? appearance.TitleFont.Family, font.Size ?? appearance.TitleFont.Size, font.Weight ?? appearance.TitleFont.Weight)
                : appearance.TitleFont,
        };
        if (look.Strength is { } strength) appearance = appearance.WithStrength(strength);
        var settings = config.Settings with { Appearance = appearance, OpenItemsWith = recipe.OpenWith ?? config.Settings.OpenItemsWith };
        var follow = Enum.GetValues<LookSetting>();
        return config with
        {
            Settings = settings,
            Fences = config.Fences.Select(fence => follow.Aggregate(fence, AllFences.ClearOwn)).ToList(),
        };
    }

    /// <summary>What a recipe changes, one plain line each (the confirmations list them).</summary>
    public static IReadOnlyList<string> Changes(Recipe recipe)
    {
        var lines = new List<string>();
        var look = recipe.Look;
        if (look.Theme is { } theme) lines.Add("Theme → " + ThemeName(theme));
        if (look.Strength is { } strength) lines.Add($"Transparency → {strength} %");
        if (look.TitleStyle is { } style) lines.Add("Title style → " + style);
        if (look.ColourStyle is { } colourStyle) lines.Add("Colour style → " + ColourStyleName(colourStyle));
        if (look.TitleFont is { } font) lines.Add("Title font → " + FontName(font));
        if (look.TitleAlign is { } align) lines.Add("Title alignment → " + align);
        if (look.TitleOnHover is { } onHover) lines.Add(onHover ? "Title bar → only on hover" : "Title bar → always shown");
        if (look.Spacing is { } spacing) lines.Add("Spacing → " + spacing);
        if (recipe.IconSize is { } size) lines.Add($"Icon size → {size} px");
        if (recipe.Labels is { } labels) lines.Add(labels == LabelMode.Always ? "Names → always" : "Names → on hover");
        if (recipe.Layout is { } layout) lines.Add(layout == Items.FenceLayout.Free ? "Layout → Free" : "Layout → Grid");
        if (recipe.OpenWith is { } openWith) lines.Add(openWith == Model.OpenWith.OneClick ? "Open with → one click" : "Open with → double click");
        if (recipe.Inbox) lines.Add("A folder panel → Details, newest first");
        return lines;
    }

    public static string ThemeName(FenceTheme theme) => theme switch
    {
        FenceTheme.Aurora => "Aurora glass", FenceTheme.Hud => "Game HUD", FenceTheme.LightMica => "Light Mica", _ => theme.ToString(),
    };

    private static string ColourStyleName(ColourStyle style) => style switch
    {
        ColourStyle.TintedGlass => "Tinted glass", ColourStyle.TitleStrip => "Title strip", _ => "Accent edge",
    };

    /// <summary>"Consolas, Large, Bold": the parts the font has (sizes by their Fence settings names).</summary>
    private static string FontName(TitleFont font)
    {
        string[] sizeNames = ["Small", "Normal", "Large", "Huge"];
        var size = font.Size is { } points ? TitleFont.Sizes.ToList().IndexOf(points) is var index and >= 0 ? sizeNames[index] : $"{points} pt" : null;
        var weight = font.Weight is { } chosen ? chosen == TitleWeight.SemiBold ? "Semibold" : chosen.ToString() : null;
        return string.Join(", ", new[] { font.Family, size, weight }.OfType<string>());
    }

    /// <summary>Why a name cannot be a new recipe's, or null when it can.</summary>
    public static string? NameProblem(string name)
    {
        var clean = Clean(name);
        if (clean.Length == 0) return "Type a name.";
        return BuiltIn.FirstOrDefault(recipe => string.Equals(recipe.Name, clean, StringComparison.OrdinalIgnoreCase)) is { } builtIn
            ? $"“{builtIn.Name}” is a built-in recipe: choose another name."
            : null;
    }

    /// <summary>
    /// "Save as recipe…": what the fence shows now (its own values and what it follows), under a name and emoji. An own recipe
    /// of the same name (any case) is replaced in its place; a name with a <see cref="NameProblem"/> changes nothing.
    /// </summary>
    public static NeoFencesConfig Save(NeoFencesConfig config, string fenceId, string name, string? emoji, bool windowsLight)
    {
        if (NameProblem(name) is not null) return config;
        var fence = Require(config, fenceId);
        var all = config.Settings.Appearance;
        var recipe = new Recipe
        {
            Name = Clean(name),
            Emoji = Model.Emoji.Normalize(emoji),
            Look = Shown(all, fence, windowsLight),
            IconSize = fence.IconSize ?? all.IconSize,
            Labels = fence.Labels ?? all.Labels,
            Layout = fence.Layout,
            OpenWith = fence.OpenWith ?? config.Settings.OpenItemsWith,
        };
        var recipes = config.Presets.ToList();
        var index = recipes.FindIndex(existing => string.Equals(existing.Name, recipe.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) recipes[index] = recipe;
        else recipes.Add(recipe);
        return config with { Presets = recipes };
    }

    /// <summary>Deletes an own recipe (any case); built-ins cannot be deleted. Fences that used it keep their look.</summary>
    public static NeoFencesConfig Delete(NeoFencesConfig config, string name)
    {
        var kept = config.Presets.Where(own => !string.Equals(own.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return kept.Count == config.Presets.Count ? config : config with { Presets = kept };
    }

    /// <summary>
    /// The recipe the fence looks like now (its card is lit), or null. A recipe matches when every part it has is what the
    /// fence shows (own value, else All fences').
    /// </summary>
    public static string? Matching(NeoFencesConfig config, Fence fence, bool windowsLight)
    {
        var all = config.Settings.Appearance;
        var shown = Shown(all, fence, windowsLight);
        return All(config).FirstOrDefault(recipe => Matches(recipe, shown, fence.IconSize ?? all.IconSize, fence.Labels ?? all.Labels,
            fence.Layout, fence.OpenWith ?? config.Settings.OpenItemsWith))?.Name;
    }

    /// <summary>
    /// The look the fence shows: its own parts, else All fences'. The transparency is the tone's (1.0.1): Light Mica is light,
    /// the other themes dark, Classic follows Windows (<paramref name="windowsLight"/>).
    /// </summary>
    private static OwnLook Shown(AppearanceSettings all, Fence fence, bool windowsLight)
    {
        var own = fence.Look ?? new OwnLook();
        var theme = own.Theme ?? all.Theme;
        var light = theme == FenceTheme.LightMica || theme == FenceTheme.Classic && windowsLight;
        return new OwnLook
        {
            Theme = theme, TitleStyle = own.TitleStyle ?? all.TitleStyle, ColourStyle = own.ColourStyle ?? all.ColourStyle,
            Strength = own.Strength ?? (light ? all.StrengthLight : all.StrengthDark), TitleFont = own.TitleFont, TitleAlign = own.TitleAlign ?? all.TitleAlign,
            TitleOnHover = own.TitleOnHover ?? all.TitleOnHover, Spacing = own.Spacing ?? all.Spacing,
        };
    }

    private static bool Matches(Recipe recipe, OwnLook shown, int iconSize, LabelMode labels, Items.FenceLayout layout, OpenWith openWith)
    {
        var look = recipe.Look;
        return (look.Theme ?? shown.Theme) == shown.Theme && (look.TitleStyle ?? shown.TitleStyle) == shown.TitleStyle
            && (look.ColourStyle ?? shown.ColourStyle) == shown.ColourStyle && (look.Strength ?? shown.Strength) == shown.Strength
            && (look.TitleFont ?? shown.TitleFont) == shown.TitleFont && (look.TitleAlign ?? shown.TitleAlign) == shown.TitleAlign
            && (look.TitleOnHover ?? shown.TitleOnHover) == shown.TitleOnHover && (look.Spacing ?? shown.Spacing) == shown.Spacing
            && (recipe.IconSize ?? iconSize) == iconSize && (recipe.Labels ?? labels) == labels
            && (recipe.Layout ?? layout) == layout && (recipe.OpenWith ?? openWith) == openWith;
    }

    /// <summary>A name as one tidy line: control characters are spaces, runs of spaces one, cut at <see cref="MaxNameLength"/>.</summary>
    internal static string Clean(string? name)
    {
        var clean = string.Join(' ', new string((name ?? "").Select(character => char.IsControl(character) ? ' ' : character).ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length <= MaxNameLength) return clean;
        // Never between the halves of a surrogate pair (an emoji): one character less instead.
        return clean[..(char.IsHighSurrogate(clean[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength)].TrimEnd();
    }

    private static Fence Require(NeoFencesConfig config, string fenceId) =>
        config.Fences.FirstOrDefault(fence => fence.Id == fenceId) ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
}

/// <summary>Settings → About → Reset settings to defaults… (M36, spec §3). Pure.</summary>
public static class SettingsReset
{
    /// <summary>
    /// Every Settings page back to its defaults (the Games page's switches too); the fences and their looks, the own presets
    /// and the Games page's data (folders, hidden games, chosen covers) stay. Items are items.json's and are not touched.
    /// </summary>
    public static NeoFencesConfig Apply(NeoFencesConfig config) => config with
    {
        Settings = Settings.NewInstall, // M41a: as NeoFences comes now (Aurora glass, Clean titles)
        Library = config.Library with { Sources = new LibrarySources(), OnlineArt = null, NewGamesFence = null },
    };
}
