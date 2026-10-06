using System.Windows;

namespace NeoFences.App;

/// <summary>"Find covers online?" (M34, ADR-055): Find covers turns it on; Not now (or closing) asks again at the next start.</summary>
public partial class OnlineArtWindow : Window
{
    /// <summary>The answer: true to find covers and website icons online, false for no.</summary>
    public event Action<bool>? Answered;

    public OnlineArtWindow(int gamesWithoutCover)
    {
        InitializeComponent();
        CountText.Text = gamesWithoutCover == 1 ? "1 of your games has no cover on this PC." : $"{gamesWithoutCover} of your games have no cover on this PC.";
        YesButton.Click += (_, _) => Answer(yes: true);
        NoButton.Click += (_, _) => Close(); // "Not now": nothing stored, asked again at the next start (review I1); IsCancel alone would not close it
    }

    private void Answer(bool yes)
    {
        Answered?.Invoke(yes);
        Close();
    }
}
