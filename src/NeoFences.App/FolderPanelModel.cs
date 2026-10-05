using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using NeoFences.Core.Items;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>What a folder panel asks its fence to do (M26): the host acts on the panel's item.</summary>
public abstract record PanelCommand;

/// <summary>Double-click or Enter on entries: files open; one folder is browsed into.</summary>
public sealed record PanelOpen(IReadOnlyList<string> Paths) : PanelCommand;

public enum PanelMove { Back, Up, Home }

public sealed record PanelNavigate(PanelMove Move) : PanelCommand;

/// <summary>A Details column header clicked.</summary>
public sealed record PanelSortBy(PanelSort Sort) : PanelCommand;

/// <summary>Right-click on entries (Shift: Windows' menu), at a screen point in physical pixels.</summary>
public sealed record PanelEntryMenu(IReadOnlyList<string> Paths, bool Extended, int ScreenX, int ScreenY, bool FromKeyboard) : PanelCommand;

/// <summary>Entries dragged out of the panel (copies to other apps; items in fences).</summary>
public sealed record PanelDrag(IReadOnlyList<string> Paths) : PanelCommand;

/// <summary>The "+ N more — Open folder" line.</summary>
public sealed record PanelOpenFolder : PanelCommand;

/// <summary>What the host shows in a panel (M26): the header, the browse buttons, the entries or the line instead of them.</summary>
/// <param name="ShowHeader">False for a panel filling a fence titled like it, at its own folder: the title already says it.</param>
public sealed record PanelContent(string Header, bool ShowHeader, bool CanGoBack, bool CanGoUp, bool CanGoHome, IReadOnlyList<ItemInfo> Entries, string? Status, string? More);

/// <summary>
/// One entry of a panel: its facts as text for the columns, and an item view for its icon and name. A file that changes
/// (a download growing) keeps its entry: icon and selection stay, only the facts are updated (final review I4).
/// </summary>
public sealed class PanelEntry : INotifyPropertyChanged
{
    public PanelEntry(ItemInfo info, CultureInfo culture)
    {
        Info = info;
        Item = new FenceItemView(new ShownItem(info.ItemRef, info.ItemRef, Name: info.Name));
        Update(info, culture);
    }

    public ItemInfo Info { get; private set; }
    public string Path => Info.ItemRef;
    public FenceItemView Item { get; }
    public string DateText { get; private set { field = value; Changed(); } } = "";
    public string TypeText { get; private set { field = value; Changed(); } } = "";
    public string SizeText { get; private set { field = value; Changed(); } } = "";

    /// <summary>The same path's new facts (its size or date changed; the name too, when only its case changed).</summary>
    public void Update(ItemInfo info, CultureInfo culture)
    {
        Info = info;
        if (Item.OwnName != info.Name) Item.Update(new ShownItem(info.ItemRef, info.ItemRef, Name: info.Name));
        DateText = info.Modified == DateTimeOffset.MinValue ? "" : info.Modified.LocalDateTime.ToString("g", culture);
        TypeText = info.IsFolder ? "Folder" : info.TypeName.TrimStart('.');
        SizeText = info.IsFolder ? "" : FolderPanels.SizeText(info.Size, culture);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>The icon size (physical pixels) last asked for: a new look or DPI asks again.</summary>
    public int RequestedPx { get; set; }
}

/// <summary>
/// A folder panel as its fence shows it (M26): the item's look and sort, and the host's latest content. Entries that stay
/// keep their icon and selection; the window wires <see cref="Commands"/> and <see cref="IconWanted"/>.
/// </summary>
public sealed class FolderPanelModel : INotifyPropertyChanged
{
    public FolderPanelModel(string itemKey) => ItemKey = itemKey;

    public string ItemKey { get; }

    public ObservableCollection<PanelEntry> Entries { get; } = [];

    /// <summary>Set by the window: the panel's requests go to the host.</summary>
    public Action<PanelCommand>? Commands { get; set; }

    /// <summary>Set by the window: a row came into view and needs its icon.</summary>
    public Action<PanelEntry>? IconWanted { get; set; }

    public PanelLook Look { get; private set { if (field == value) return; field = value; Changed(); LookChanged?.Invoke(); } }

    /// <summary>The look changed: the control swaps its view and rows.</summary>
    public event Action? LookChanged;

    public PanelSort Sort { get; private set { field = value; Changed(); } }
    public bool Descending { get; private set { field = value; Changed(); } }


    public string Header { get; private set { field = value; Changed(); } } = "";
    public bool ShowHeader { get; private set { field = value; Changed(); } } = true;
    public bool CanGoBack { get; private set { field = value; Changed(); Changed(nameof(Browsing)); } }
    public bool CanGoUp { get; private set { field = value; Changed(); Changed(nameof(Browsing)); } }
    public bool CanGoHome { get; private set { field = value; Changed(); Changed(nameof(Browsing)); } }

    /// <summary>The browse buttons show: away from home, or with somewhere to go back to.</summary>
    public bool Browsing => CanGoBack || CanGoUp || CanGoHome;

    /// <summary>A line instead of entries: not available, empty, nothing matching; null while there are entries (or the first listing is on its way).</summary>
    public string? Status { get; private set { field = value; Changed(); } }

    /// <summary>"+ N more — Open folder", or null.</summary>
    public string? More { get; private set { field = value; Changed(); } }

    public void Apply(FolderPanel panel)
    {
        Look = panel.Look;
        Sort = panel.Sort;
        Descending = panel.Descending;
    }

    /// <summary>The host's latest listing for this panel, in place: entries that stay keep their icon, selection and scroll.</summary>
    public void SetContent(PanelContent content, CultureInfo culture)
    {
        (Header, ShowHeader) = (content.Header, content.ShowHeader);
        (CanGoBack, CanGoUp, CanGoHome) = (content.CanGoBack, content.CanGoUp, content.CanGoHome);
        (Status, More) = (content.Status, content.More);
        var kept = Entries.GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var wanted = content.Entries.Select(info =>
        {
            if (!kept.TryGetValue(info.ItemRef, out var entry)) return new PanelEntry(info, culture);
            if (entry.Info != info) entry.Update(info, culture);
            return entry;
        }).ToList();
        var wantedSet = wanted.ToHashSet();
        for (var index = Entries.Count - 1; index >= 0; index--)
        {
            if (!wantedSet.Contains(Entries[index])) Entries.RemoveAt(index);
        }
        // ponytail: O(n²) moves in the worst case (a re-sort of 500 entries); a keyed diff when that shows up.
        for (var index = 0; index < wanted.Count; index++)
        {
            var at = index < Entries.Count && ReferenceEquals(Entries[index], wanted[index]) ? index : Entries.IndexOf(wanted[index]);
            if (at < 0) Entries.Insert(index, wanted[index]);
            else if (at != index) Entries.Move(at, index);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
