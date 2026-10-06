using System.Windows;

namespace NeoFences.App;

/// <summary>"Find covers online?" (M34, ADR-055): asked once; closing it without an answer asks again at the next scan.</summary>
public partial class OnlineArtWindow : Window
{
    /// <summary>The answer: true to find covers and website icons online, false for no.</summary>
    public event Action<bool>? Answered;

    public OnlineArtWindow(int gamesWithoutCover)
    {
        InitializeComponent();
        CountText.Text = gamesWithoutCover == 1 ? "1 of your games has no cover on this PC." : $"{gamesWithoutCover} of your games have no cover on this PC.";
        YesButton.Click += (_, _) => Answer(yes: true);
        NoButton.Click += (_, _) => Answer(yes: false); // IsCancel closes only a dialog: this window is modeless (M33 lesson)
    }

    private void Answer(bool yes)
    {
        Answered?.Invoke(yes);
        Close();
    }
}
