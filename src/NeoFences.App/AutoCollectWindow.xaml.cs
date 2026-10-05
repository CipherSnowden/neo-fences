using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using NeoFences.Core.Items;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// "Auto-collect…" (M27 spec §3): a fence's rules — the list, and the selected rule's folder, kinds and patterns with a live
/// "N items here match now". OK gives <see cref="Result"/>; <see cref="Listings"/> are the folders it listed (by source),
/// so a new rule's "Add these N too?" needs no second listing.
/// </summary>
public partial class AutoCollectWindow : Window
{
    private const string ChooseTag = "choose";
    private readonly List<CollectRule> _rules;
    private readonly string? _downloads;
    private readonly Dictionary<string, IReadOnlyList<ItemInfo>?> _listings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _listing = new(StringComparer.OrdinalIgnoreCase); // folders being listed now
    private readonly string _patternsHint;
    private bool _showing; // the editor is being filled from a rule: its change events are not edits
    private object? _lastSource;

    public IReadOnlyList<CollectRule>? Result { get; private set; }

    public IReadOnlyDictionary<string, IReadOnlyList<ItemInfo>?> Listings => _listings;

    private readonly (CheckBox Box, CollectKinds Kind)[] _kinds;

    public AutoCollectWindow(string fenceTitle, IReadOnlyList<CollectRule> rules, string? downloads)
    {
        InitializeComponent();
        _rules = [.. rules];
        _downloads = downloads;
        _patternsHint = PatternsHint.Text;
        Heading.Text = $"Auto-collect into \"{fenceTitle}\"";
        _kinds = [(AppsCheck, CollectKinds.Apps), (InstallersCheck, CollectKinds.Installers), (DocumentsCheck, CollectKinds.Documents),
                  (PicturesCheck, CollectKinds.Pictures), (ArchivesCheck, CollectKinds.Archives), (AnythingCheck, CollectKinds.Anything)];
        foreach (var (box, _) in _kinds) box.Click += (_, _) => Edit(rule => rule with { Kinds = KindsChecked() });
        PatternsBox.TextChanged += (_, _) => Edit(rule => rule with { Patterns = PatternsBox.Text.Trim() });
        SourceBox.SelectionChanged += (_, _) => OnSourceChosen();
        RuleList.SelectionChanged += (_, _) => ShowRule();
        NewButton.Click += (_, _) =>
        {
            _rules.Add(new CollectRule { Kinds = CollectKinds.Apps | CollectKinds.Installers });
            FillList(select: _rules.Count - 1);
        };
        RemoveButton.Click += (_, _) =>
        {
            if (RuleList.SelectedIndex < 0) return;
            _rules.RemoveAt(RuleList.SelectedIndex);
            FillList(select: Math.Min(RuleList.SelectedIndex, _rules.Count - 1));
        };
        OkButton.Click += (_, _) =>
        {
            Result = [.. _rules];
            DialogResult = true;
        };
        FillList(select: _rules.Count > 0 ? 0 : -1);
    }

    private CollectRule? Selected => RuleList.SelectedIndex >= 0 && RuleList.SelectedIndex < _rules.Count ? _rules[RuleList.SelectedIndex] : null;

    private void FillList(int select)
    {
        RuleList.Items.Clear();
        foreach (var rule in _rules) RuleList.Items.Add(new ListBoxItem { Content = CollectRules.Summary(rule) });
        RuleList.SelectedIndex = select;
        ShowRule();
    }

    /// <summary>The selected rule in the editor (or an empty, disabled editor).</summary>
    private void ShowRule()
    {
        _showing = true;
        var rule = Selected;
        Editor.IsEnabled = rule is not null;
        RemoveButton.IsEnabled = rule is not null;
        FillSources(rule?.Source);
        foreach (var (box, kind) in _kinds) box.IsChecked = rule?.Kinds.HasFlag(kind) == true;
        PatternsBox.Text = rule?.Patterns ?? "";
        _showing = false;
        Validate();
    }

    /// <summary>Desktop, Downloads, the rule's own folder, and "Choose folder…".</summary>
    private void FillSources(string? source)
    {
        SourceBox.Items.Clear();
        SourceBox.Items.Add(new ComboBoxItem { Content = "Desktop", Tag = CollectRules.DesktopSource });
        if (_downloads is not null) SourceBox.Items.Add(new ComboBoxItem { Content = $"Downloads ({_downloads})", Tag = _downloads });
        if (source is not null && source != CollectRules.DesktopSource && (_downloads is null || !FolderViews.SameFolder(source, _downloads)))
            SourceBox.Items.Add(new ComboBoxItem { Content = source, Tag = source });
        SourceBox.Items.Add(new ComboBoxItem { Content = "Choose folder…", Tag = ChooseTag });
        SourceBox.SelectedItem = SourceBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => source is not null && item.Tag is string tag && tag != ChooseTag && CollectRules.SameSource(tag, source));
        _lastSource = SourceBox.SelectedItem;
    }

    private void OnSourceChosen()
    {
        if (_showing || SourceBox.SelectedItem is not ComboBoxItem { Tag: string tag } chosen) return;
        if (tag != ChooseTag)
        {
            _lastSource = chosen;
            Edit(rule => rule with { Source = tag });
            return;
        }
        var picked = PathPicker.TryPickFolder(new WindowInteropHelper(this).Handle, "Choose a folder to watch",
            failure => Serilog.Log.Warning(failure, "auto-collect: the folder dialog failed"));
        if (picked is null)
        {
            _showing = true;
            SourceBox.SelectedItem = _lastSource; // cancelled: the folder it had
            _showing = false;
            return;
        }
        Edit(rule => rule with { Source = picked });
        ShowRule();
    }

    private CollectKinds KindsChecked() => _kinds.Where(entry => entry.Box.IsChecked == true).Aggregate(CollectKinds.None, (kinds, entry) => kinds | entry.Kind);

    /// <summary>The selected rule takes the editor's change; its line in the list follows.</summary>
    private void Edit(Func<CollectRule, CollectRule> change)
    {
        if (_showing || Selected is not { } rule) return;
        var index = RuleList.SelectedIndex;
        _rules[index] = change(rule);
        if (RuleList.Items[index] is ListBoxItem line) line.Content = CollectRules.Summary(_rules[index]);
        Validate();
    }

    private void Validate()
    {
        var patternsOk = FolderViews.ParsePatterns(PatternsBox.Text) is not null;
        PatternsHint.Text = patternsOk ? _patternsHint : "Use file name patterns like *.iso;*.torrent — no paths, and none of \\ / : \" < > |";
        PatternsHint.Foreground = patternsOk ? (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush") : System.Windows.Media.Brushes.IndianRed;
        OkButton.IsEnabled = _rules.All(rule => FolderViews.ParsePatterns(rule.Patterns) is not null);
        ShowMatches();
    }

    /// <summary>"N items here match now", from a listing made off the UI thread (a dead share must not freeze the dialog).</summary>
    private void ShowMatches()
    {
        if (Selected is not { } rule)
        {
            MatchText.Text = "";
            return;
        }
        if (!_listings.TryGetValue(rule.Source, out var entries))
        {
            MatchText.Text = "Looking…";
            if (!_listing.Add(rule.Source)) return;
            var source = rule.Source;
            Task.Run(() => ListSource(source)).ContinueWith(listed =>
            {
                _listing.Remove(source);
                _listings[source] = listed.IsFaulted ? null : listed.Result;
                ShowMatches();
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return;
        }
        if (entries is null)
        {
            MatchText.Text = "This folder is not available now; the rule waits for it.";
            return;
        }
        var count = entries.Count(entry => CollectRules.Matches(rule, entry.Name, entry.IsFolder));
        MatchText.Text = count == 1 ? "1 item here matches now." : $"{count:N0} items here match now.";
    }

    /// <summary>The desktop is the user's and the Public Desktop; null when the folder (or both desktops) cannot be read.</summary>
    private static IReadOnlyList<ItemInfo>? ListSource(string source)
    {
        if (source != CollectRules.DesktopSource) return FolderItems.TryList(source);
        var user = FolderItems.TryList(DesktopItems.UserDesktop);
        var common = FolderItems.TryList(DesktopItems.PublicDesktop);
        return user is null && common is null ? null : [.. user ?? [], .. common ?? []];
    }
}
