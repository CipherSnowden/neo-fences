using System.Windows;

namespace NeoFences.App;

/// <summary>
/// The one message dialog (M35, spec §3) in Windows 11's style: <see cref="Ask"/> a question with two answers (Enter for the
/// first, Esc for the second), or <see cref="Tell"/> a note with OK.
/// </summary>
public partial class MessageDialog : Window
{
    private MessageDialog(string heading, string text, string primary, string? secondary)
    {
        InitializeComponent();
        HeadingText.Text = heading;
        BodyText.Text = text;
        BodyText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = primary;
        PrimaryButton.Click += (_, _) => DialogResult = true;
        if (secondary is null)
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
            PrimaryButton.IsCancel = true; // Esc closes a note too
            PrimaryButton.Margin = new Thickness(0);
        }
        else SecondaryButton.Content = secondary;
    }

    /// <returns>True for the first answer; false for the second, Esc or closing the window.</returns>
    public static bool Ask(Window? owner, string heading, string text, string primary, string secondary) =>
        Show(owner, new MessageDialog(heading, text, primary, secondary)) == true;

    public static void Tell(Window? owner, string heading, string text) => Show(owner, new MessageDialog(heading, text, "OK", secondary: null));

    private static bool? Show(Window? owner, MessageDialog dialog)
    {
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog();
    }
}
