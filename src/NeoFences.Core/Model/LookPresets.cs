using NeoFences.Core.Config;

namespace NeoFences.Core.Model;

/// <summary>
/// A look preset (M36): an own look and, where the preset needs them, an icon size and labels. Applied, it is copied onto
/// the fence (a copy, not a link); the fence keeps its colour.
/// </summary>
public sealed record LookPreset
{
    public string Name { get; init; } = "";

    public OwnLook Look { get; init; } = new();

    public int? IconSize { get; init; }

    public LabelMode? Labels { get; init; }
}

/// <summary>The built-in presets and the owner's own (M36, spec §2). Pure: each returns a new config.</summary>
public static class LookPresets
{
    public const int MaxNameLength = 40;

    /// <summary>Glass, Minimal, Title strip, Solid, Compact (the owner picked all five).</summary>
    public static IReadOnlyList<LookPreset> BuiltIn { get; } =
    [
        new() { Name = "Glass", Look = new OwnLook { ColourStyle = ColourStyle.TintedGlass, Strength = 25 }, Labels = LabelMode.Always },
        new() { Name = "Minimal", Look = new OwnLook { ColourStyle = ColourStyle.AccentEdge, Strength = 4, TitleOnHover = true }, Labels = LabelMode.OnHover },
        new() { Name = "Title strip", Look = new OwnLook { ColourStyle = ColourStyle.TitleStrip, Strength = 60 } },
        new() { Name = "Solid", Look = new OwnLook { ColourStyle = ColourStyle.AccentEdge, Strength = AppearanceSettings.MaxStrength, Spacing = Spacing.Roomy } },
        new() { Name = "Compact", Look = new OwnLook { Spacing = Spacing.Compact }, IconSize = 32, Labels = LabelMode.OnHover },
    ];

    /// <summary>The built-ins, then the owner's own.</summary>
    public static IReadOnlyList<LookPreset> All(NeoFencesConfig config) => [.. BuiltIn, .. config.Presets];

    /// <summary>The preset's look copied onto the fence; icon size and labels only where the preset has them.</summary>
    /// <exception cref="ArgumentException">No fence with that id.</exception>
    public static NeoFencesConfig Apply(NeoFencesConfig config, string fenceId, LookPreset preset)
    {
        var fence = Require(config, fenceId);
        return config.WithFence(fence with
        {
            Look = ConfigNormalizer.NormalizeLook(preset.Look),
            IconSize = preset.IconSize ?? fence.IconSize,
            Labels = preset.Labels ?? fence.Labels,
        });
    }

    /// <summary>"Like all fences": the fence's own look goes; icon size, labels and layout stay.</summary>
    public static NeoFencesConfig LikeAllFences(NeoFencesConfig config, string fenceId) =>
        config.WithFence(Require(config, fenceId) with { Look = null });

    /// <summary>Why a name cannot be a new preset's, or null when it can.</summary>
    public static string? NameProblem(string name)
    {
        var clean = Clean(name);
        if (clean.Length == 0) return "Type a name.";
        return BuiltIn.FirstOrDefault(preset => string.Equals(preset.Name, clean, StringComparison.OrdinalIgnoreCase)) is { } builtIn
            ? $"“{builtIn.Name}” is a built-in preset: choose another name."
            : null;
    }

    /// <summary>
    /// "Save this look as a preset…": the fence's own look, icon size and labels under <paramref name="name"/>. An own preset
    /// of the same name (any case) is replaced in its place; a name with a <see cref="NameProblem"/> changes nothing.
    /// </summary>
    public static NeoFencesConfig Save(NeoFencesConfig config, string fenceId, string name)
    {
        if (NameProblem(name) is not null) return config;
        var fence = Require(config, fenceId);
        var preset = new LookPreset { Name = Clean(name), Look = fence.Look ?? new OwnLook(), IconSize = fence.IconSize, Labels = fence.Labels };
        var presets = config.Presets.ToList();
        var index = presets.FindIndex(own => string.Equals(own.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) presets[index] = preset;
        else presets.Add(preset);
        return config with { Presets = presets };
    }

    /// <summary>Deletes an own preset (any case); built-ins cannot be deleted. Fences that used it keep their look.</summary>
    public static NeoFencesConfig Delete(NeoFencesConfig config, string name)
    {
        var kept = config.Presets.Where(own => !string.Equals(own.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return kept.Count == config.Presets.Count ? config : config with { Presets = kept };
    }

    /// <summary>
    /// The preset the fence looks like now (its chip is lit), or null: like all fences (no own look), or a look of its own
    /// that no preset has.
    /// </summary>
    public static string? Matching(NeoFencesConfig config, Fence fence)
    {
        if (ConfigNormalizer.NormalizeLook(fence.Look) is not { } look) return null;
        return All(config).FirstOrDefault(preset => ConfigNormalizer.NormalizeLook(preset.Look) == look
                                                    && (preset.IconSize ?? fence.IconSize) == fence.IconSize
                                                    && (preset.Labels ?? fence.Labels) == fence.Labels)?.Name;
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
        Settings = new Settings(),
        Library = config.Library with { Sources = new LibrarySources(), OnlineArt = null, NewGamesFence = null },
    };
}
