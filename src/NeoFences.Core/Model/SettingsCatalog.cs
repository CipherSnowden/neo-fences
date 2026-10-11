using System.Globalization;
using System.Text;

namespace NeoFences.Core.Model;

/// <summary>Where a setting lives (M41b): a Settings page, or a Fence settings section.</summary>
public enum SettingsPlace { LookAndFeel, Desktop, Games, PeekAndKeys, Snapshots, Updates, Help, FenceLook, FenceTitle, FenceItems, FenceBehaviour }

/// <summary>One setting in plain words (M41b, spec §2–3): what it does, its default, one tip, and extra words search finds it by.</summary>
public sealed record SettingInfo(string Id, string Label, SettingsPlace Place, string What, string Default, string Tip, string Words = "");

/// <summary>
/// Every setting a person sees (M41b): the explanations in Fence settings and Settings come from here, and the search box
/// finds a setting by its label, its words or its explanation. Pure.
/// </summary>
public static class SettingsCatalog
{
    public static IReadOnlyList<SettingInfo> All { get; } =
    [
        new("fence.theme", "Theme", SettingsPlace.FenceLook, "The whole look of the fence: its glass, light and lines.", "Aurora glass (Classic for setups from before rc.3)", "Game HUD and Neon suit a games fence.", "style skin aurora hud neon mica classic"),
        new("fence.accent", "Accent colour", SettingsPlace.FenceLook, "The fence's colour: its edge, underline and glow.", "None (the theme's own, or Windows' accent)", "\"Windows accent\" changes with Windows.", "colour color swatch tint"),
        new("fence.transparency", "Transparency", SettingsPlace.FenceLook, "How much of the wallpaper shows through.", "30 %", "Lower is more see-through.", "opacity see-through solid glass strength"),
        new("fence.colourStyle", "Colour style", SettingsPlace.FenceLook, "How a Classic fence shows its colour.", "Accent edge", "Only for the Classic theme.", "edge tinted strip"),
        new("fence.titleStyle", "Title style", SettingsPlace.FenceTitle, "How the title is drawn.", "Clean", "Pill shows the emoji and the count in a pill.", "simple clean pill caps"),
        new("fence.titleFont", "Title font", SettingsPlace.FenceTitle, "The title's font, size and weight.", "Segoe UI, Normal, Semibold", "Game HUD uses a mono font unless you pick one.", "font size weight bold"),
        new("fence.titleAlign", "Title alignment", SettingsPlace.FenceTitle, "Where the title sits.", "Left", "Pill and Caps titles sit left.", "left centre center right"),
        new("fence.titleBar", "Show the title bar", SettingsPlace.FenceTitle, "Off: the title shows only while the mouse is over the fence.", "On", "You can still move and rename it on hover.", "hide title hover"),
        new("fence.layout", "Layout", SettingsPlace.FenceItems, "Grid packs items in order; Free keeps each where you drop it.", "Grid", "Free is good for a hand-made arrangement.", "grid free flow packed positions"),
        new("fence.iconSize", "Icon size", SettingsPlace.FenceItems, "How big icons and covers are.", "Medium", "A games fence looks good Large.", "small medium large big"),
        new("fence.labels", "Names", SettingsPlace.FenceItems, "Names under icons: always, or only when you point at one.", "Always", "On hover gives a tight grid.", "labels names text"),
        new("fence.spacing", "Spacing", SettingsPlace.FenceItems, "Room between items.", "Normal", "Only for the Grid layout.", "gap room compact roomy"),
        new("fence.widgetsFloat", "Widgets float", SettingsPlace.FenceItems, "A fence with only widgets drops its glass; each widget is its own card.", "Off", "Point at the fence to move it.", "board cards glass widget"),
        new("fence.openWith", "Open items with", SettingsPlace.FenceBehaviour, "One click or a double click opens an item.", "One click", "Ctrl+click still selects several.", "click double single launch"),
        new("fence.rollUp", "Rolled-up fences open", SettingsPlace.FenceBehaviour, "How a rolled-up fence opens: when the mouse rests on it, or on a click on its title. Double-click a title to roll it up.", "When the mouse rests", "", "roll up collapse title"),
        new("fence.lock", "Lock position", SettingsPlace.FenceBehaviour, "Stops moving and resizing the fence.", "Off", "", "lock pin move"),
        new("fence.collect", "Auto-collect", SettingsPlace.FenceBehaviour, "New files from a folder show up here by themselves (links, never moved).", "Off", "Watch Downloads for installers.", "rules watch downloads automatic"),
        new("look.motion", "Animations", SettingsPlace.LookAndFeel, "Items lift under the mouse, themes' light moves.", "Lively", "Always off in games and when Windows' animations are off.", "motion animation lively calm off"),
        new("look.names", "Item names", SettingsPlace.LookAndFeel, "Crisp text, or on soft chips readable over any wallpaper.", "Crisp", "Chips help on busy wallpapers.", "text chips crisp plain readable"),
        new("look.menu", "Menu style", SettingsPlace.LookAndFeel, "The fence menu: Guided (with headings) or Compact (an icon row).", "Guided", "", "menu compact guided"),
        new("look.wallpaperColour", "Colour fences from the wallpaper", SettingsPlace.LookAndFeel, "Fences without a colour take one from your wallpaper.", "Off", "", "accent wallpaper colour color"),
        new("desktop.hideIcons", "Hide desktop icons while NeoFences runs", SettingsPlace.Desktop, "Hides Windows' desktop icons; they always come back when NeoFences exits.", "Off", "", "icons hide desktop"),
        new("desktop.quickHide", "Double-click empty desktop hides all fences", SettingsPlace.Desktop, "Then the desktop is fully yours; double-click again to bring them back.", "On", "", "double click quick-hide gesture"),
        new("desktop.reveal", "Click wallpaper to reveal the desktop", SettingsPlace.Desktop, "One click on empty wallpaper moves your windows aside; click again for them back.", "Off", "A double-click still hides fences.", "single click reveal show desktop windows aside"),
        new("desktop.draw", "Right-drag draws a new fence", SettingsPlace.Desktop, "Draw a rectangle on the desktop with the right mouse button.", "On", "", "right drag draw new fence gesture"),
        new("desktop.startWithWindows", "Start with Windows", SettingsPlace.Desktop, "NeoFences starts when you sign in.", "On", "", "startup boot login"),
        new("desktop.shortcutArrows", "Show shortcut arrows", SettingsPlace.Desktop, "The small arrow Windows draws on shortcut icons.", "Off", "", "arrow overlay shortcut"),
        new("games.sources", "Look for games in", SettingsPlace.Games, "Which launchers and folders are searched for games.", "All", "", "steam epic gog ubisoft ea battle.net xbox launchers"),
        new("games.folders", "Game folders", SettingsPlace.Games, "Folders whose sub-folders are games.", "None", "Turn on \"My game folders\" in the list above.", "folders library"),
        new("games.newGames", "New games go to", SettingsPlace.Games, "The fence that gets newly installed games.", "Nowhere", "", "new games fence"),
        new("games.onlineArt", "Find covers and website icons online", SettingsPlace.Games, "Looks up game covers on Steam and web links' icons.", "Asked once", "Only names and addresses leave the PC.", "covers art online steam icons privacy"),
        new("games.gameMode", "Go idle while a full-screen game runs", SettingsPlace.Games, "NeoFences pauses everything while you game.", "On", "", "game mode fullscreen performance idle"),
        new("keys.peek", "Peek hotkey", SettingsPlace.PeekAndKeys, "Shows your fences above all windows; the keyboard works in them.", "Ctrl+Alt+Space", "", "hotkey shortcut peek keyboard"),
        new("safety.snapshots", "Snapshots", SettingsPlace.Snapshots, "Saved arrangements of your fences you can go back to.", "One a day and before big changes", "", "backup restore undo"),
        new("safety.export", "Export / Import setup", SettingsPlace.Snapshots, "Your whole setup as one file, to move it or keep it.", "Nothing exported", "", "backup file move pc"),
        new("safety.reset", "Reset settings", SettingsPlace.Snapshots, "Every setting back to how NeoFences comes; fences and items stay.", "As NeoFences comes", "", "defaults factory"),
        new("updates.auto", "Download updates automatically", SettingsPlace.Updates, "Checks GitHub once a day and installs on the next start.", "On", "Off: NeoFences makes no network calls of its own.", "update network privacy"),
        new("help.guide", "Help", SettingsPlace.Help, "Opens the guide with every feature.", "The online guide", "", "guide manual docs"),
    ];

    /// <exception cref="KeyNotFoundException">No setting with that id.</exception>
    public static SettingInfo Find(string id) =>
        All.FirstOrDefault(setting => setting.Id == id) ?? throw new KeyNotFoundException($"No setting {id}.");

    /// <summary>A Fence settings section (else a Settings page).</summary>
    public static bool InFenceSettings(SettingsPlace place) => place >= SettingsPlace.FenceLook;

    /// <summary>
    /// Every setting whose label, words or explanation hold each word of the query (any case, accents ignored); the label's
    /// matches first. An empty query finds nothing.
    /// </summary>
    public static IReadOnlyList<SettingInfo> Search(string? query)
    {
        var words = Fold(query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        var whole = string.Join(' ', words);
        return All
            .Select((setting, index) => (setting, index, label: Fold(setting.Label), text: Fold($"{setting.Label} {setting.Words} {setting.What}")))
            .Where(entry => words.All(word => entry.text.Contains(word, StringComparison.Ordinal)))
            .OrderBy(entry => entry.label.StartsWith(whole, StringComparison.Ordinal) ? 0 : entry.label.Contains(whole, StringComparison.Ordinal) ? 1 : 2)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.setting)
            .ToList();
    }

    /// <summary>Lower case without accents ("Transparéncy" → "transparency").</summary>
    private static string Fold(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var kept = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) kept.Append(character);
        }
        return kept.ToString().Normalize(NormalizationForm.FormC);
    }
}
