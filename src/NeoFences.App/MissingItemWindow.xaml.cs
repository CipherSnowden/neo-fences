using System.Windows;
using NeoFences.Core.Items;

namespace NeoFences.App;

public enum MissingItemChoice { None, Locate, Remove }

/// <summary>An opened item's target is missing or on a drive that is not there (M18 spec §3): Locate… / Remove / Cancel.</summary>
public partial class MissingItemWindow : Window
{
    public MissingItemChoice Choice { get; private set; }

    /// <param name="game">A game item (M22): "not installed", no Locate….</param>
    public MissingItemWindow(string name, string target, TargetState state, bool game = false)
    {
        InitializeComponent();
        if (game && state == TargetState.Missing)
        {
            Headline.Text = $"\"{name}\" is not installed";
            Explanation.Text = "The game was uninstalled, or its launcher no longer lists it. Reinstall it and the item comes back by itself.";
            TargetText.Text = "";
            LocateButton.Visibility = Visibility.Collapsed;
            RemoveButton.IsDefault = true;
            RemoveButton.Click += (_, _) => Choose(MissingItemChoice.Remove);
            return;
        }
        Headline.Text = state == TargetState.Unavailable ? $"\"{name}\" is not available right now" : $"\"{name}\" is missing";
        Explanation.Text = state == TargetState.Unavailable
            ? TargetChecks.IsNetworkPath(target)
                ? "Its network location does not answer. It comes back by itself when it does."
                : $"It is on drive {TargetChecks.RootOf(target)?.TrimEnd('\\')}, which is not connected. Plug it in: the item comes back by itself."
            : ItemKinds.IsApp(target)
                ? "Windows no longer has this app: it was uninstalled. Point the item at another app, or remove the item."
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
