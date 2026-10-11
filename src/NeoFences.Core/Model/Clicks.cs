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
