using System.Windows;
using NeoFences.Core.Items;

namespace NeoFences.App;

/// <summary>The bulk fix's question (M19 spec §2): "Fix N more items?" with up to five names. Fix = DialogResult true.</summary>
public partial class RelocateWindow : Window
{
    private const int NamesShown = 5;

    public RelocateWindow(int count, Relocation.Bases bases, IReadOnlyList<string> names)
    {
        InitializeComponent();
        Headline.Text = count == 1 ? "Fix 1 more item?" : $"Fix {count} more items?";
        Explanation.Text = $"{(count == 1 ? "It was" : "They were")} in {bases.OldBase} and {(count == 1 ? "is" : "are")} now in {bases.NewBase}.";
        ItemNames.Text = string.Join(", ", names.Take(NamesShown)) + (names.Count > NamesShown ? $" … and {names.Count - NamesShown} more" : "");
        FixButton.Click += (_, _) => DialogResult = true;
    }
}
