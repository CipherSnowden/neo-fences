using System.Windows;
using System.Windows.Controls;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>
/// The recipe confirmations (M41b, spec §4, recipes.html): one fence — one question; all fences — two steps, the second with
/// "Only fences that hold games" and the snapshot taken first. Built in code in the message dialog's look.
/// </summary>
public static class RecipeDialogs
{
    /// <summary>"Apply “Game shelf” to Games?" with the changes; Undo with Ctrl+Z.</summary>
    public static bool ConfirmOne(Window owner, Recipe recipe, string fenceTitle) =>
        MessageDialog.Ask(owner, $"Apply “{recipe.Name}” to {fenceTitle}?",
            string.Join("\n", Recipes.Changes(recipe).Select(line => "• " + line)) + "\n\nOnly this fence changes. Undo with Ctrl+Z.", "Apply", "Cancel");

    /// <returns>Null: cancelled; else whether only the fences that hold games change.</returns>
    public static bool? ConfirmAll(Window owner, Recipe recipe, int fences, int gameFences)
    {
        if (!MessageDialog.Ask(owner, $"Apply “{recipe.Name}” to all {fences} fences?",
                "This changes how every fence looks and behaves:\n" + string.Join("\n", Recipes.Changes(recipe).Select(line => "• " + line)) +
                "\n\nYour items, folders and fence places stay as they are.", "Continue…", "Cancel")) return null;
        var onlyGames = new CheckBox
        {
            Content = $"Only fences that hold games ({gameFences} of {fences})", Margin = new Thickness(0, 12, 0, 0),
            Visibility = gameFences > 0 && gameFences < fences ? Visibility.Visible : Visibility.Collapsed,
        };
        var apply = new Button { Content = "Apply to all fences", IsDefault = true, MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        var back = new Button { Content = "Back", IsCancel = true, MinWidth = 88 };
#pragma warning disable WPF0001 // ThemeMode: the Fluent look every other window sets in XAML (where WPF does not flag it)
        var dialog = new Window
        {
            Title = "NeoFences", SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner.IsVisible ? owner : null, ThemeMode = ThemeMode.System,
        };
#pragma warning restore WPF0001
        apply.Click += (_, _) => dialog.DialogResult = true;
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24), MaxWidth = 460,
            Children =
            {
                new TextBlock { Text = "Are you sure?", FontSize = 20, FontWeight = FontWeights.SemiBold },
                new TextBlock { Text = $"A snapshot “Before {recipe.Name}” is taken first. Tray → Restore snapshot brings everything back.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) },
                onlyGames,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0), Children = { apply, back } },
            },
        };
        return dialog.ShowDialog() == true ? onlyGames.IsChecked == true : null;
    }
}
