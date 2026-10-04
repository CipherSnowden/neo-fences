using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace NeoFences.App;

/// <summary>One desktop item as a fence shows it. Label and icon start as placeholders and fill in from <see cref="IconLoader"/>.</summary>
public sealed class FenceItemView(string itemRef) : INotifyPropertyChanged
{
    public string ItemRef { get; } = itemRef;

    /// <summary>A shortcut (.lnk, .url, .pif): gets the arrow overlay when Settings shows shortcut arrows (M8b).</summary>
    public bool IsShortcut { get; } = Path.GetExtension(itemRef).ToLowerInvariant() is ".lnk" or ".url" or ".pif";

    public string Label
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); }
    } = itemRef.StartsWith("::", StringComparison.Ordinal) ? "" : Path.GetFileNameWithoutExtension(itemRef);

    /// <summary>The icon size last requested (UI thread): a slower, older load of another size never wins (final review I4).</summary>
    public int WantedSizePx { get; set; }

    public ImageSource? Icon
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
    }

    /// <summary>The label is being renamed in place (F2 or the item menu's Rename).</summary>
    public bool IsEditing
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing))); }
    }

    /// <summary>Text in the rename box.</summary>
    public string EditName
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditName))); }
    } = "";

    /// <summary>A Game Library tile (M12): a 2:3 tile with a poster, a logo or the icon centred.</summary>
    public bool IsTile
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTile))); }
    }

    /// <summary>The tile's poster or logo, once loaded.</summary>
    public ImageSource? Art
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Art))); }
    }

    /// <summary>"Poster" (fills the tile), "Logo" (centred) or "None" (the icon).</summary>
    public string ArtKind
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ArtKind))); }
    } = "None";

    /// <summary>The art file requested last (UI thread): a slower, older load never wins.</summary>
    public string? ArtPath { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
