using System.Windows;
using System.Windows.Interop;
using NeoFences.Core.Items;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// "Folder view settings" (M21 spec §2): the folder, what it shows, the types, the sort and "only the newest N". OK gives
/// <see cref="Result"/>; an invalid pattern or count marks its box and disables OK.
/// </summary>
public partial class FolderViewWindow : Window
{
    private static readonly FenceSort[] Sorts = [FenceSort.Name, FenceSort.Type, FenceSort.Date];
    private readonly string _patternsHint;

    public FolderView? Result { get; private set; }

    public FolderViewWindow(FolderView view)
    {
        InitializeComponent();
        _patternsHint = PatternsHint.Text;
        FolderBox.Text = view.Path;
        ShowBox.SelectedIndex = (int)view.Show;
        PatternsBox.Text = view.Patterns;
        SortBox.SelectedIndex = Math.Max(0, Array.IndexOf(Sorts, view.Sort));
        NewestCheck.IsChecked = view.Newest is not null;
        NewestBox.Text = (view.Newest ?? FolderViews.BusyFolderNewest).ToString();
        FolderBox.TextChanged += (_, _) => Validate();
        PatternsBox.TextChanged += (_, _) => Validate();
        NewestBox.TextChanged += (_, _) => Validate();
        NewestCheck.Click += (_, _) => Validate();
        BrowseButton.Click += (_, _) => Browse(FolderBox.Text.Trim());
        OkButton.Click += (_, _) =>
        {
            if (Read() is not { } read) return;
            Result = read;
            DialogResult = true;
        };
        Loaded += (_, _) => FolderBox.Focus();
        Validate();
    }

    /// <summary>
    /// Windows' folder dialog at the folder typed, checked off the UI thread first (M19 R2, final review I3): the dialog parses
    /// its start folder on the UI thread, and a dead share would freeze every fence. Not answering within 2 s: Windows'
    /// default place.
    /// </summary>
    private void Browse(string start)
    {
        IsEnabled = false; // no second click while the check runs (at most 2 s)
        Task.Run(() => start.Length > 0 && TargetChecks.RootOf(start) is not null && TargetProbe.Check(start).State == TargetState.Ok).ContinueWith(checking =>
        {
            IsEnabled = true;
            if (checking.IsFaulted) Serilog.Log.Warning(checking.Exception, "folder view: {Folder} could not be checked; the dialog opens at its default place", start);
            var reachable = !checking.IsFaulted && checking.Result;
            var picked = PathPicker.TryPickFolder(new WindowInteropHelper(this).Handle, "Choose the folder to show",
                failure => Serilog.Log.Warning(failure, "folder view: the folder dialog failed"), startFolder: reachable ? start : null);
            if (picked is not null) FolderBox.Text = picked;
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Validate()
    {
        var patternsOk = FolderViews.ParsePatterns(PatternsBox.Text) is not null;
        PatternsHint.Text = patternsOk ? _patternsHint : "Use file name patterns like *.png;*.jpg — no paths, and none of \\ / : \" < > |";
        PatternsHint.Foreground = patternsOk ? (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush") : System.Windows.Media.Brushes.IndianRed;
        NewestBox.IsEnabled = NewestCheck.IsChecked == true;
        // Say why OK is greyed out (M23); an empty folder box needs no words.
        var problem = FolderBox.Text.Trim().Length > 0 && FolderViews.FolderPath(FolderBox.Text) is null
            ? "Type a full folder path, like C:\\Users\\you\\Downloads (or use Browse…)."
            : NewestCheck.IsChecked == true && !(int.TryParse(NewestBox.Text.Trim(), out var count) && count is >= 1 and <= FolderViews.MaxNewest)
                ? $"\"Only the newest\" takes a number from 1 to {FolderViews.MaxNewest}."
                : null;
        ProblemText.Text = problem ?? "";
        ProblemText.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        OkButton.IsEnabled = Read() is not null;
    }

    /// <summary>The view as entered, or null while something is not valid.</summary>
    private FolderView? Read()
    {
        // A full path only, variables expanded (M23): a relative one would be read against NeoFences' own folder.
        if (FolderViews.FolderPath(FolderBox.Text) is not { } folder || FolderViews.ParsePatterns(PatternsBox.Text) is null) return null;
        int? newest = null;
        if (NewestCheck.IsChecked == true)
        {
            if (!int.TryParse(NewestBox.Text.Trim(), out var count) || count < 1 || count > FolderViews.MaxNewest) return null;
            newest = count;
        }
        return new FolderView
        {
            Path = folder,
            Show = (ViewShow)Math.Max(0, ShowBox.SelectedIndex),
            Patterns = PatternsBox.Text.Trim(),
            Sort = Sorts[Math.Max(0, SortBox.SelectedIndex)],
            Newest = newest,
        };
    }
}
