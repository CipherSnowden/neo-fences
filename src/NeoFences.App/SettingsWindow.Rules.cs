using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>A desktop fence a rule can put items in (M11), by title.</summary>
public sealed record RuleFence(string Id, string Title)
{
    public override string ToString() => Title; // what Narrator reads for the item
}

/// <summary>One row of the Rules list (M11): "Images → Pictures", greyed when off or its fence is gone.</summary>
public sealed record RuleRow(string Id, bool Enabled, string Text, bool FenceMissing)
{
    public string Spoken => Enabled ? Text : $"{Text}, off";
    public string ToggleName => $"Use the rule {Text}";
    public double Opacity => Enabled && !FenceMissing ? 1 : 0.55;
}

/// <summary>
/// Settings → Rules (M11, spec §4): an ordered list (first match wins) and an editor under it. Every change goes to the
/// host as the whole new list; the host saves and shows it back.
/// </summary>
public partial class SettingsWindow
{
    public event Action<IReadOnlyList<Rule>>? RulesChanged;
    public event Action? ApplyRulesRequested;

    private IReadOnlyList<Rule> _rules = [];
    private IReadOnlyList<RuleFence> _ruleFences = [];
    private bool _ruleEditorOpen;
    private string? _editingRuleId; // null while adding
    private string? _selectRuleAfterUpdate;

    private void InitializeRules()
    {
        AddRuleButton.Click += (_, _) => BeginRuleEdit(rule: null, fenceId: null);
        EditRuleButton.Click += (_, _) => { if (SelectedRule is { } rule) BeginRuleEdit(rule, rule.FenceId); };
        DeleteRuleButton.Click += (_, _) => { if (SelectedRule is { } rule) ReportRules([.. _rules.Where(candidate => candidate.Id != rule.Id)]); };
        MoveRuleUpButton.Click += (_, _) => MoveSelectedRule(-1);
        MoveRuleDownButton.Click += (_, _) => MoveSelectedRule(+1);
        ApplyRulesButton.Click += (_, _) =>
        {
            ShowRulesResult("Reading your Desktop items…"); // the button stays off until the host answers
            ApplyRulesRequested?.Invoke();
        };
        RuleList.SelectionChanged += (_, _) => UpdateRuleButtons();
        RuleKindBox.SelectionChanged += (_, _) => ShowConditionFields(condition: null);
        RuleChoiceBox.SelectionChanged += (_, _) => UpdateTextField();
        SaveRuleButton.Click += (_, _) => SaveRule();
        CancelRuleButton.Click += (_, _) => EndRuleEdit();
        RuleBrowseButton.Click += (_, _) =>
        {
            var folder = FolderPicker.TryPick(new WindowInteropHelper(this).Handle, "Choose the folder your games are in",
                logFailure: failure => Log.Warning(failure, "rules: the folder picker failed"));
            if (folder is not null) RuleTextBox.Text = folder;
        };
        RuleEditor.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) SaveRule();
            else if (key.Key == Key.Escape) EndRuleEdit();
            else return;
            key.Handled = true;
        };
        AutomationProperties.SetHelpText(RuleList, RulesDescription.Text);
    }

    private Rule? SelectedRule => RuleList.SelectedItem is RuleRow row ? _rules.FirstOrDefault(rule => rule.Id == row.Id) : null;

    private RuleKind SelectedKind => RuleKindBox.SelectedItem is ComboBoxItem { Tag: string kind } ? Enum.Parse<RuleKind>(kind) : RuleKind.Type;

    private object? SelectedChoice => (RuleChoiceBox.SelectedItem as ComboBoxItem)?.Tag;

    /// <summary>Fence menu → "Rules for this fence…": the editor opens with a new rule for that fence.</summary>
    public void BeginNewRule(string fenceId)
    {
        if (_ruleEditorOpen)
        {
            // A rule is being edited: keep it (M13b); the user finishes or cancels it first.
            ShowRuleProblem("Finish or cancel the rule you are editing first.");
            Dispatcher.BeginInvoke(() => RulesCard.BringIntoView(), DispatcherPriority.Loaded);
            return;
        }
        BeginRuleEdit(rule: null, fenceId);
        Dispatcher.BeginInvoke(() => RulesCard.BringIntoView(), DispatcherPriority.Loaded); // after the first layout
    }

    /// <summary>The host's answer to "Apply rules now" (also read out by Narrator). A message ending in "…" is still working.</summary>
    public void ShowRulesResult(string message)
    {
        ApplyRulesButton.IsEnabled = !message.EndsWith('…');
        RulesStatus.Text = message;
        RulesStatus.Visibility = Visibility.Visible;
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(RulesStatus)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    private IReadOnlyList<string> _ruleLines = [];

    private void ShowRules(SettingsView view)
    {
        // A refresh with nothing new (a scan finished, game mode changed) leaves the list alone: row focus and an open
        // fence dropdown stay as they are (M13c).
        var unchanged = _rules.SequenceEqual(view.Rules) && _ruleLines.SequenceEqual(view.RuleLines) && _ruleFences.SequenceEqual(view.RuleFences)
                        && _selectRuleAfterUpdate is null && _focusRuleId is null && RuleList.ItemsSource is not null;
        _rules = view.Rules;
        _ruleLines = view.RuleLines;
        _ruleFences = view.RuleFences;
        if (unchanged) return;
        var selectedId = _selectRuleAfterUpdate ?? (RuleList.SelectedItem as RuleRow)?.Id;
        _selectRuleAfterUpdate = null;
        var fenceIds = view.RuleFences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        var rows = view.Rules.Select((rule, index) => new RuleRow(rule.Id, rule.Enabled, view.RuleLines[index], !fenceIds.Contains(rule.FenceId))).ToList();
        RuleList.ItemsSource = rows;
        RuleList.SelectedItem = rows.FirstOrDefault(row => row.Id == selectedId);
        UpdateRuleButtons();
        if (_ruleEditorOpen)
        {
            // Fences renamed or deleted meanwhile: the editor offers what exists now, keeping the choice (M13b).
            var chosen = (RuleFenceBox.SelectedItem as RuleFence)?.Id;
            RuleFenceBox.ItemsSource = _ruleFences;
            RuleFenceBox.SelectedItem = _ruleFences.FirstOrDefault(fence => fence.Id == chosen);
        }
        if (_focusRuleId is { } focusId)
        {
            // The list was rebuilt under the ticked box: give the keyboard back to it (M13b).
            _focusRuleId = null;
            Dispatcher.BeginInvoke(() =>
            {
                if (rows.FirstOrDefault(row => row.Id == focusId) is { } row
                    && RuleList.ItemContainerGenerator.ContainerFromItem(row) is DependencyObject container && FindCheckBox(container) is { } box) box.Focus();
            }, DispatcherPriority.Loaded);
        }
    }

    private string? _focusRuleId;

    private static CheckBox? FindCheckBox(DependencyObject parent)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if ((child as CheckBox ?? FindCheckBox(child)) is { } box) return box;
        }
        return null;
    }

    /// <summary>A row's checkbox. Refilling the list sets the boxes too: only a real change is reported.</summary>
    private void OnRuleToggled(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { Tag: string id } box) return;
        var enabled = box.IsChecked == true;
        if (_rules.FirstOrDefault(rule => rule.Id == id) is not { } toggled || toggled.Enabled == enabled) return;
        if (box.IsKeyboardFocused) _focusRuleId = id;
        ReportRules([.. _rules.Select(rule => rule.Id == id ? rule with { Enabled = enabled } : rule)]);
    }

    private void UpdateRuleButtons()
    {
        // While a rule is being edited the list stays as it is: deleting or moving its rule would lose the edit (M13b).
        var index = _ruleEditorOpen ? -1 : RuleList.SelectedIndex;
        AddRuleButton.IsEnabled = !_ruleEditorOpen;
        EditRuleButton.IsEnabled = index >= 0;
        DeleteRuleButton.IsEnabled = index >= 0;
        MoveRuleUpButton.IsEnabled = index > 0;
        MoveRuleDownButton.IsEnabled = index >= 0 && index < _rules.Count - 1;
    }

    private void MoveSelectedRule(int offset)
    {
        var index = RuleList.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _rules.Count) return;
        var rules = _rules.ToList();
        (rules[index], rules[target]) = (rules[target], rules[index]);
        _selectRuleAfterUpdate = rules[target].Id;
        ReportRules(rules);
    }

    private void ReportRules(IReadOnlyList<Rule> rules) => RulesChanged?.Invoke(rules);

    private void BeginRuleEdit(Rule? rule, string? fenceId)
    {
        _ruleEditorOpen = true;
        _editingRuleId = rule?.Id;
        RuleFenceBox.ItemsSource = _ruleFences;
        RuleFenceBox.SelectedItem = _ruleFences.FirstOrDefault(fence => fence.Id == fenceId);
        var kind = rule?.Condition.Kind is { } existing && Enum.IsDefined(existing) ? existing : RuleKind.Type;
        RuleKindBox.SelectedIndex = (int)kind; // the items are in RuleKind order
        ShowConditionFields(rule?.Condition);
        SaveRuleButton.Content = rule is null ? "Add rule" : "Save rule";
        RuleEditor.Visibility = Visibility.Visible;
        UpdateRuleButtons();
        RuleKindBox.Focus();
    }

    private void EndRuleEdit()
    {
        if (!_ruleEditorOpen) return;
        _ruleEditorOpen = false;
        _editingRuleId = null;
        RuleEditor.Visibility = Visibility.Collapsed;
        UpdateRuleButtons();
        (RuleList.ItemContainerGenerator.ContainerFromItem(RuleList.SelectedItem) as UIElement ?? AddRuleButton).Focus();
    }

    /// <summary>The fields of the chosen condition kind, filled from <paramref name="condition"/> (or defaults).</summary>
    private void ShowConditionFields(RuleCondition? condition)
    {
        var kind = SelectedKind;
        (string Label, object Value)[] choices = kind switch
        {
            RuleKind.Type => [.. Enum.GetValues<TypeGroup>().Select(group => (Rules.GroupName(group), (object)group)), ("Other extensions…", "extensions")],
            RuleKind.Game => [.. Enum.GetValues<GameLauncher>().Select(launcher => (launcher switch
            {
                GameLauncher.Any => "Any launcher",
                GameLauncher.Folder => "In a folder of mine…",
                _ => Rules.LauncherName(launcher),
            }, (object)launcher))],
            RuleKind.Age => [("Older than", RuleCompare.OlderThan), ("Newer than", RuleCompare.NewerThan)],
            RuleKind.Size => [("Bigger than", RuleCompare.BiggerThan), ("Smaller than", RuleCompare.SmallerThan)],
            _ => [],
        };
        var items = choices.Select(choice => new ComboBoxItem { Content = choice.Label, Tag = choice.Value }).ToList();
        RuleChoiceBox.ItemsSource = items;
        RuleChoiceBox.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(RuleChoiceBox, kind switch { RuleKind.Type => "File type", RuleKind.Game => "Launcher", _ => "Comparison" });
        object? chosen = condition is null ? null : kind switch
        {
            RuleKind.Type => condition.Group is { } group ? group : "extensions",
            RuleKind.Game => condition.Launcher ?? GameLauncher.Any,
            RuleKind.Age or RuleKind.Size => condition.Compare,
            _ => null,
        };
        RuleChoiceBox.SelectedItem = items.FirstOrDefault(item => Equals(item.Tag, chosen)) ?? items.FirstOrDefault();
        RuleTextBox.Text = condition is null ? "" : kind switch
        {
            RuleKind.Type => condition.Extensions ?? "",
            RuleKind.Game => condition.Folder ?? "",
            RuleKind.Name => condition.Pattern ?? "",
            RuleKind.Age => condition.Days?.ToString(CultureInfo.CurrentCulture) ?? "",
            RuleKind.Size => condition.Megabytes?.ToString(CultureInfo.CurrentCulture) ?? "",
            _ => "",
        };
        UpdateTextField();
    }

    private void UpdateTextField()
    {
        var kind = SelectedKind;
        var customExtensions = kind == RuleKind.Type && SelectedChoice is "extensions";
        var gameFolder = kind == RuleKind.Game && SelectedChoice is GameLauncher.Folder;
        var showText = kind is RuleKind.Name or RuleKind.Age or RuleKind.Size || customExtensions || gameFolder;
        RuleTextBox.Visibility = showText ? Visibility.Visible : Visibility.Collapsed;
        RuleBrowseButton.Visibility = gameFolder ? Visibility.Visible : Visibility.Collapsed;
        RuleUnitText.Text = kind switch { RuleKind.Age => "days", RuleKind.Size => "MB", _ => "" };
        var (name, hint) = kind switch
        {
            RuleKind.Type when customExtensions => ("Extensions", "Custom extensions are separated by spaces, like .iso .torrent"),
            RuleKind.Type => ("", "By the file's extension; Folders matches folders."),
            RuleKind.Game when gameFolder => ("Game folder", @"Shortcuts that point inside this folder, like D:\GameLibrary"),
            RuleKind.Game => ("", "Shortcuts made by Steam, Epic, Ubisoft Connect, the EA app, Battle.net or GOG Galaxy."),
            RuleKind.Name => ("Name pattern", "* stands for any text, ? for one character; plain text matches names that contain it."),
            RuleKind.Age => ("Number of days", "By the date the item was last changed."),
            _ => ("Size in megabytes", "Folders never match a size rule."),
        };
        AutomationProperties.SetName(RuleTextBox, name);
        RuleHint.Text = hint;
        RuleHint.Foreground = SecondaryText;
    }

    private void SaveRule()
    {
        if (BuildCondition(out var problem) is not { } condition)
        {
            ShowRuleProblem(problem);
            return;
        }
        if (RuleFenceBox.SelectedItem is not RuleFence fence)
        {
            ShowRuleProblem("Choose the fence the items go to.");
            return;
        }
        // The edited rule may have gone meanwhile (a snapshot restore): the edit is kept as a new rule, not dropped (M13c).
        List<Rule> rules = _editingRuleId is { } id && _rules.Any(rule => rule.Id == id)
            ? [.. _rules.Select(rule => rule.Id == id ? rule with { Condition = condition, FenceId = fence.Id } : rule)]
            : [.. _rules, Rule.Create(condition, fence.Id)];
        _selectRuleAfterUpdate = _editingRuleId is { } kept && rules.Any(rule => rule.Id == kept) ? kept : rules[^1].Id;
        EndRuleEdit();
        ReportRules(rules);
    }

    private void ShowRuleProblem(string problem)
    {
        RuleHint.Text = problem;
        RuleHint.Foreground = System.Windows.Media.Brushes.IndianRed;
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(RuleHint)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    /// <summary>The editor's condition, or null with what is missing.</summary>
    private RuleCondition? BuildCondition(out string problem)
    {
        problem = "";
        var kind = SelectedKind;
        var text = RuleTextBox.Text.Trim();
        switch (kind)
        {
            case RuleKind.Type when SelectedChoice is TypeGroup group:
                return new RuleCondition { Kind = kind, Group = group };
            case RuleKind.Type when text.Length == 0:
                problem = "Type at least one extension, like .iso";
                return null;
            case RuleKind.Type:
                return new RuleCondition { Kind = kind, Extensions = text };
            case RuleKind.Game when SelectedChoice is GameLauncher.Folder && text.Length == 0:
                problem = "Choose the folder your games are in.";
                return null;
            case RuleKind.Game when SelectedChoice is GameLauncher.Folder:
                return new RuleCondition { Kind = kind, Launcher = GameLauncher.Folder, Folder = text };
            case RuleKind.Game:
                return new RuleCondition { Kind = kind, Launcher = SelectedChoice as GameLauncher? ?? GameLauncher.Any };
            case RuleKind.Name when text.Length == 0:
                problem = "Type a name or a pattern, like invoice*";
                return null;
            case RuleKind.Name:
                return new RuleCondition { Kind = kind, Pattern = text };
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || !double.IsFinite(number) || number < 0)
        {
            problem = kind == RuleKind.Age ? "Type a number of days (0 or more)." : "Type a size in MB (0 or more).";
            return null;
        }
        var compare = SelectedChoice as RuleCompare?;
        return kind == RuleKind.Age
            ? new RuleCondition { Kind = kind, Compare = compare ?? RuleCompare.OlderThan, Days = number }
            : new RuleCondition { Kind = kind, Compare = compare ?? RuleCompare.BiggerThan, Megabytes = number };
    }
}
