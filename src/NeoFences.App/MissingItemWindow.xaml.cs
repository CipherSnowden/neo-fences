using System.Windows;
using NeoFences.Core.Items;

namespace NeoFences.App;

public enum MissingItemChoice { None, Locate, Remove }

/// <summary>An opened item's target is missing or on a drive that is not there (M18 spec §3): Locate… / Remove / Cancel.</summary>
public partial class MissingItemWindow : Window
{
    public MissingItemChoice Choice { get; private set; }

    public MissingItemWindow(string name, string target, TargetState state)
    {
        InitializeComponent();
        Headline.Text = state == TargetState.Unavailable ? $"\"{name}\" is not available right now" : $"\"{name}\" is missing";
        Explanation.Text = state == TargetState.Unavailable
            ? TargetChecks.IsNetworkPath(target)
                ? "Its network location does not answer. It comes back by itself when it does."
                : $"It is on drive {TargetChecks.RootOf(target)?.TrimEnd('\\')}, which is not connected. Plug it in: the item comes back by itself."
            : "Nothing is at its place any more: it was deleted, moved or renamed. Point the item at its new place, or remove the item.";
        TargetText.Text = target;
        LocateButton.Click += (_, _) => Choose(MissingItemChoice.Locate);
        RemoveButton.Click += (_, _) => Choose(MissingItemChoice.Remove);
    }

    private void Choose(MissingItemChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
