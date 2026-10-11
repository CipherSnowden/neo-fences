namespace NeoFences.Core.Model;

/// <summary>How items open (M41a, ADR-064): one click (the default) or Windows' double click.</summary>
public enum OpenWith { OneClick, DoubleClick }

/// <summary>The fence menu's shape (M41a, spec §5): sections with headings (the default), or an icon row and a short list.</summary>
public enum FenceMenuStyle { Guided, Compact }

public static class Clicks
{
    /// <summary>A fence's own "Open items with" wins over Settings' (null: like all fences).</summary>
    public static bool OneClickOpens(Fence fence, Settings settings) => (fence.OpenWith ?? settings.OpenItemsWith) == OpenWith.OneClick;
}

/// <summary>What a click on empty wallpaper does (M41a, final review I5).</summary>
public enum RevealStep { None, Reveal, Restore }

/// <summary>
/// "Click wallpaper to reveal desktop" decides from what the desktop shows at the click, not from a remembered toggle (the
/// user may have brought windows back meanwhile): app windows show → move them aside; none since our reveal → bring them back.
/// </summary>
public static class RevealPolicy
{
    public static RevealStep Next(bool windowsShown, bool revealedByUs) =>
        windowsShown ? RevealStep.Reveal : revealedByUs ? RevealStep.Restore : RevealStep.None;
}
