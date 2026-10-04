using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NeoFences.Core.Items;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// An item's Properties (M18 spec §3), also "Add item…" (spec §2): name, target, arguments, run as administrator, icon
/// and note. OK gives <see cref="Result"/> (and a picture to copy in); nothing is applied here.
/// </summary>
public partial class ItemPropertiesWindow : Window
{
    private readonly VirtualItem _original;
    private readonly FenceItemView _preview;
    private readonly IconLoader _iconLoader;
    private readonly DispatcherTimer _checkDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private ItemIcon? _icon;
    private string? _picture;    // chosen now: copied into NeoFences' icons folder on OK (the host does it)
    private bool _isFolder;      // from the last check of the target
    private int _checkNumber;    // only the newest check is shown

    /// <summary>The item with the fields as typed (OK), or null.</summary>
    public VirtualItem? Result { get; private set; }

    /// <summary>A picture chosen as the icon, still at its own place; null when none was chosen.</summary>
    public string? PictureToCopy => _picture;

    /// <param name="adding">"Add item…": an empty item, the title and button say so, the target box has the keyboard.</param>
    /// <param name="focusName">F2: the name box has the keyboard, all selected.</param>
    public ItemPropertiesWindow(VirtualItem item, bool adding, IconLoader iconLoader, bool focusName)
    {
        InitializeComponent();
        _original = item;
        _iconLoader = iconLoader;
        _icon = item.Icon;
        Title = adding ? "Add item" : $"{item.OwnName ?? "Item"} — properties";
        OkButton.Content = adding ? "Add" : "OK";
        NameBox.Text = item.Name ?? "";
        TargetBox.Text = item.Target;
        ArgumentsBox.Text = item.Arguments ?? "";
        AdminBox.IsChecked = item.RunAsAdmin;
        NoteBox.Text = item.Note ?? "";
        _preview = new FenceItemView(new ShownItem("preview", item.Target, item.Name, item.Icon));
        _preview.PropertyChanged += OnPreviewChanged;

        BrowseButton.Click += (_, _) => OpenMenu(BrowseButton);
        ChangeIconButton.Click += (_, _) => OpenMenu(ChangeIconButton);
        BrowseFileItem.Click += (_, _) => Browse(folder: false);
        BrowseFolderItem.Click += (_, _) => Browse(folder: true);
        BrowseAppItem.Click += (_, _) => PickApp();
        IconFromFileItem.Click += (_, _) => PickIconFromFile();
        IconFromPictureItem.Click += (_, _) => PickPicture();
        ResetIconItem.Click += (_, _) => SetIcon(icon: null, picture: null);
        TargetBox.TextChanged += (_, _) =>
        {
            _checkDelay.Stop(); // typed: checked once the typing pauses
            _checkDelay.Start();
            OkButton.IsEnabled = false; // until this target's check says file or folder (M19 R6)
        };
        _checkDelay.Tick += (_, _) =>
        {
            _checkDelay.Stop();
            CheckTarget();
        };
        OkButton.Click += (_, _) => Accept();
        Loaded += (_, _) =>
        {
            CheckTarget();
            var box = adding ? TargetBox : focusName ? NameBox : null;
            if (box is null) return;
            box.Focus();
            box.SelectAll();
        };
        Closed += (_, _) => _checkDelay.Stop();
    }

    private nint Handle => new WindowInteropHelper(this).Handle;

    private static void OpenMenu(Button button)
    {
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private string? CurrentTarget => ItemKinds.Clean(TargetBox.Text);

    private void Browse(bool folder)
    {
        var start = CurrentTarget is { } target && ItemKinds.Of(target) == ItemKind.Path ? Path.GetDirectoryName(target.TrimEnd('\\')) : null;
        WhenReachable(start, reachable =>
        {
            void LogFailure(Exception failure) => Log.Warning(failure, "Browse dialog failed");
            var picked = folder
                ? PathPicker.TryPickFolder(Handle, "Choose a folder", LogFailure, reachable)
                : PathPicker.TryPickFile(Handle, "Choose a file or program", LogFailure, reachable);
            if (picked is not null) TargetBox.Text = picked; // checked by TextChanged
        });
    }

    /// <summary>"An app…" (M19 §1): Start's All apps; the name box takes the app's name when it is empty.</summary>
    private void PickApp()
    {
        var picker = new AppPickerWindow(_iconLoader) { Owner = this };
        if (picker.ShowDialog() != true || picker.Chosen is not { } app) return;
        TargetBox.Text = ItemKinds.AppTarget(app.AppId);
        if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = app.Name;
    }

    /// <summary>Windows' icon picker, opening at the item's icon file, else its target (an .exe or .dll offers its own icons).</summary>
    private void PickIconFromFile()
    {
        var target = CurrentTarget;
        var start = _icon?.File ?? (target is not null && Path.GetExtension(target).ToLowerInvariant() is ".exe" or ".dll" or ".ico" ? target : null);
        WhenReachable(start, reachable =>
        {
            if (IconPicker.TryPick(Handle, reachable, _icon?.Index ?? 0) is not { } chosen) return;
            SetIcon(new ItemIcon { File = chosen.File, Index = chosen.Index }, picture: null);
        });
    }

    /// <summary>
    /// A dialog's start place is checked off the UI thread first (M19 R2): Windows' pickers parse it on the UI thread, and a
    /// dead share would freeze the window. Not reachable within 2 s: the dialog opens at Windows' default place.
    /// </summary>
    private void WhenReachable(string? path, Action<string?> open)
    {
        if (path is null || TargetChecks.RootOf(path) is null)
        {
            open(path);
            return;
        }
        IsEnabled = false; // no second click while the check runs (at most 2 s)
        Task.Run(() => TargetProbe.Check(path).State == TargetState.Ok).ContinueWith(checking =>
        {
            IsEnabled = true;
            if (!checking.Result) Log.Information("{Path} did not answer; the dialog opens at its default place", path);
            open(checking.Result ? path : null);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void PickPicture()
    {
        var picked = PathPicker.TryPickFile(Handle, "Choose a picture for the icon", failure => Log.Warning(failure, "picture dialog failed"),
            filters: [("Pictures", "*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.gif"), ("All files", "*.*")]);
        if (picked is not null) SetIcon(icon: null, picture: picked);
    }

    /// <summary>A new icon choice: a file's icon, a picture (shown from where it is until it is copied in), or none.</summary>
    private void SetIcon(ItemIcon? icon, string? picture)
    {
        _icon = icon;
        _picture = picture;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_picture is not null)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 96;
                image.UriSource = new Uri(_picture);
                image.EndInit();
                IconPreview.Source = image;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                Log.Warning(failure, "picture {Picture} cannot be shown", _picture);
                _picture = null;
                MessageBox.Show(this, "This picture cannot be used as an icon.", "NeoFences", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }
        if (_preview.Update(new ShownItem("preview", CurrentTarget ?? "", NameBox.Text, _icon)) || _preview.Icon is null) _iconLoader.Request(_preview, 48);
        IconPreview.Source = _preview.Icon;
    }

    private void OnPreviewChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(FenceItemView.Icon) && _picture is null) IconPreview.Source = _preview.Icon;
    }

    /// <summary>Found / Missing / Drive not connected (spec §3), off the UI thread; Arguments and admin greyed where they do not apply.</summary>
    private void CheckTarget()
    {
        var target = CurrentTarget;
        RefreshPreview();
        if (target is null)
        {
            ShowStatus("Type a path or a web address, or use Browse.", takesArguments: false);
            return;
        }
        var kind = ItemKinds.Of(target);
        var isApp = ItemKinds.IsApp(target);
        if (kind != ItemKind.Path && !isApp)
        {
            _checkNumber++; // an older path check still running must not overwrite this
            ShowStatus(kind == ItemKind.Website ? "A website: opens in your browser." : "A Windows item.", takesArguments: false);
            return;
        }
        var number = ++_checkNumber;
        ShowStatus("Checking…", takesArguments: !isApp, checking: true);
        Task.Run(() => TargetProbe.Check(target)).ContinueWith(checking =>
        {
            if (number != _checkNumber) return;
            var check = checking.Result;
            _isFolder = check.IsFolder;
            ShowStatus(check.State switch
            {
                TargetState.Missing when isApp => "● Not installed: Windows no longer has this app.",
                TargetState.Missing => "● Missing: nothing is there now.",
                TargetState.Unavailable => TargetChecks.IsNetworkPath(target) ? "● Network location not reachable." : "● Drive not connected.",
                _ when isApp => "● Found (an app).",
                _ when TargetChecks.RootOf(target) is null => "Opens through Windows.",
                _ => check.IsFolder ? "● Found (a folder)." : "● Found.",
            }, takesArguments: ItemKinds.TakesArguments(kind, check.IsFolder));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <param name="checking">A check is running: OK waits for it (M19 R6), at most 2 s, so the folder answer it saves is this target's.</param>
    private void ShowStatus(string text, bool takesArguments, bool checking = false)
    {
        TargetStatus.Text = text;
        ArgumentsBox.IsEnabled = takesArguments;
        AdminBox.IsEnabled = takesArguments;
        OkButton.IsEnabled = !checking;
    }

    private void Accept()
    {
        if (CurrentTarget is not { } target)
        {
            ShowStatus("Type a path or a web address, or use Browse.", takesArguments: false);
            TargetBox.Focus();
            return;
        }
        var takesArguments = ItemKinds.TakesArguments(ItemKinds.Of(target), _isFolder);
        var note = NoteBox.Text.Trim();
        Result = _original with
        {
            Target = target,
            Name = NameBox.Text.Trim() is { Length: > 0 } name ? name : null,
            Arguments = takesArguments && ArgumentsBox.Text.Trim() is { Length: > 0 } arguments ? arguments : null,
            RunAsAdmin = takesArguments && AdminBox.IsChecked == true,
            Note = note.Length > 0 ? note : null,
            Icon = _icon,
        };
        DialogResult = true;
    }
}
