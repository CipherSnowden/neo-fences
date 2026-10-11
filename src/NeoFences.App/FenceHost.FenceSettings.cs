using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NeoFences.Core.Appearance;
using NeoFences.Core.Items;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Fence settings (M41b, spec 2026-10-11-m41b-settings-design §1–2, ADR-067): one window per fence (a tab has its own) and
/// one for All fences only. Each window edits this fence or All fences, keeps its own undo, and shows a live preview. Every
/// change applies to the fences at once and is saved; the windows show the result back.
/// </summary>
public sealed partial class FenceHost
{
    private const string AllFencesKey = "*";
    private readonly Dictionary<string, FenceSettingsSession> _fenceSettings = new(StringComparer.Ordinal);
    private IReadOnlyList<WallpaperImage>? _previewWallpapers;
    /// <summary>Each wallpaper's picture, decoded once per read (final review: not on the UI thread at every refresh).</summary>
    private IReadOnlyDictionary<string, BitmapSource> _previewPictures = new Dictionary<string, BitmapSource>();

    private sealed class FenceSettingsSession(FenceSettingsWindow window, string? fenceId, NeoFencesConfig opened)
    {
        public FenceSettingsWindow Window { get; } = window;
        public string? FenceId { get; } = fenceId;
        public bool AllFences { get; set; } = fenceId is null;
        public Stack<SettingsUndoStep> Undo { get; } = new();
        /// <summary>The config when the window opened: Hold to compare shows it.</summary>
        public NeoFencesConfig Opened { get; } = opened;
    }

    /// <summary>One Ctrl+Z step: the config before, and the folder panels a Downloads inbox recipe changed (1.0.1: items.json's).</summary>
    private sealed record SettingsUndoStep(NeoFencesConfig Config, IReadOnlyList<(string ItemId, FolderPanel Panel)>? PanelsBefore = null);

    /// <param name="fenceId">The fence, or null: the window for All fences (Settings → Change the look of all fences…, search).</param>
    /// <param name="focusSettingId">A <see cref="SettingsCatalog"/> id to bring into view (Settings' search).</param>
    private void OpenFenceSettings(string? fenceId, string? focusSettingId = null)
    {
        var key = fenceId ?? AllFencesKey;
        if (_fenceSettings.TryGetValue(key, out var open))
        {
            if (open.Window.WindowState == WindowState.Minimized) open.Window.WindowState = WindowState.Normal;
            open.Window.Activate();
            if (focusSettingId is not null) open.Window.FocusSetting(focusSettingId);
            return;
        }
        if (fenceId is not null && _config.Fences.All(fence => fence.Id != fenceId)) return;
        var window = new FenceSettingsWindow(fenceId);
        var session = new FenceSettingsSession(window, fenceId, _config);
        window.SettingChanged += (id, value) => ChangeSetting(session, id, value);
        window.ModeChanged += allFences =>
        {
            session.AllFences = allFences || session.FenceId is null;
            RefreshFenceSettings(key);
        };
        window.ColourChanged += (swatch, hex, windowsAccent) => EditInSettings(session, config => FenceEdits.SetColour(config, session.FenceId!, swatch, hex, windowsAccent), "colour");
        window.CustomColourRequested += () => PickColourInSettings(session);
        window.WallpaperColourChanged += on =>
        {
            session.Undo.Push(new(_config));
            SetAppearance(Appearance with { WallpaperAccent = on });
        };
        window.MakeFollowRequested += setting => EditInSettings(session, config => AllFences.MakeFollow(config, setting), $"{setting}: every fence follows");
        window.UseAllDefaultsRequested += () => EditInSettings(session, config => AllFences.UseAllDefaults(config, session.FenceId!), "all defaults");
        window.UndoRequested += () => UndoInSettings(session);
        window.Renamed += name =>
        {
            if (_config.Fences.All(fence => fence.Id != session.FenceId)) return; // gone meanwhile (final review)
            session.Undo.Push(new(_config));
            _config = FenceEdits.Rename(_config, session.FenceId!, name);
            if (WindowShowing(session.FenceId!) is { } shown) shown.SetTitle(_config.Fences.First(fence => fence.Id == shown.FenceId).Title);
            foreach (var fenceWindow in _windows.Values) RefreshTabs(fenceWindow);
            ScheduleSave();
            RefreshAllFenceSettings();
        };
        window.EmojiChanged += emoji => EditInSettings(session, config => FenceEdits.SetEmoji(config, session.FenceId!, emoji), "emoji");
        window.CollectRequested += () =>
        {
            if (session.FenceId is { } id && WindowShowing(id) is { } shown) EditCollectRules(shown);
            RefreshFenceSettings(key);
        };
        window.RecipeChosen += recipe => ApplyRecipe(session, recipe);
        window.RecipeSaved += (name, emoji) =>
        {
            if (_config.Fences.All(fence => fence.Id != session.FenceId)) return; // gone meanwhile (final review)
            _config = Recipes.Save(_config, session.FenceId!, name, emoji, windowsLight: _lightTheme);
            Log.Information("recipe saved: {Name}", name);
            ScheduleSave();
            RefreshAllFenceSettings(); // every open window lists the recipes
        };
        window.RecipeDeleted += name =>
        {
            _config = Recipes.Delete(_config, name);
            Log.Information("recipe deleted: {Name}", name);
            ScheduleSave();
            RefreshAllFenceSettings();
        };
        window.CompareHeld += held => Compare(session, held);
        window.Activated += (_, _) => RefreshFenceSettings(key); // the fence menu may have changed the fence meanwhile
        window.Closed += (_, _) =>
        {
            Compare(session, held: false);
            _fenceSettings.Remove(key);
        };
        _fenceSettings[key] = session;
        PlaceBesideFence(window, fenceId);
        RefreshFenceSettings(key);
        window.Show();
        window.Activate();
        if (focusSettingId is not null) window.FocusSetting(focusSettingId);
        LoadPreviewWallpapers();
    }

    /// <summary>One setting from a Fence settings window: this fence's own (null follows again), or All fences'.</summary>
    private void ChangeSetting(FenceSettingsSession session, string id, object? value)
    {
        var scope = session.AllFences ? "All fences" : "this fence";
        if (id == "fence.layout" && value is FenceLayout layout && session.FenceId is { } fenceId)
        {
            session.Undo.Push(new(_config));
            // The shown tab takes the menu's path (a Free layout pins the cells where the items are now).
            if (WindowShowing(fenceId) is { } shown) SetFenceLayout(shown, layout);
            else _config = FenceEdits.SetLayout(_config, fenceId, layout);
            AfterSettingsChange(session, $"{id} = {value} ({scope})");
            return;
        }
        if (id == "fence.rollUp" && value is RollupExpand mode)
        {
            session.Undo.Push(new(_config));
            SetRollupExpand(mode);
            AfterSettingsChange(session, $"{id} = {value} (All fences)");
            return;
        }
        EditInSettings(session, config => SettingEdits.Set(config, session.AllFences ? null : session.FenceId, id, value), $"{id} = {value?.ToString() ?? "follow"} ({scope})");
    }

    /// <summary>A change from Fence settings: undo kept, applied to every fence window, saved, shown back.</summary>
    private void EditInSettings(FenceSettingsSession session, Func<NeoFencesConfig, NeoFencesConfig> change, string what)
    {
        if (session.FenceId is { } fenceId && _config.Fences.All(fence => fence.Id != fenceId)) return; // deleted meanwhile
        var before = _config;
        _config = change(_config);
        if (ReferenceEquals(before, _config)) return;
        session.Undo.Push(new(before));
        AfterSettingsChange(session, what);
    }

    private void AfterSettingsChange(FenceSettingsSession session, string what)
    {
        Log.Information("fence settings: {Change}", what);
        if (session.FenceId is { } fenceId && WindowShowing(fenceId) is { } shown) shown.Refresh(_config.Fences.First(fence => fence.Id == fenceId));
        foreach (var fenceWindow in _windows.Values) RefreshTabs(fenceWindow); // colour and emoji show on tabs too
        RestyleAll(); // icon size, names, clicks and the look of every following fence
        ScheduleSave();
        RefreshAllFenceSettings();
    }

    /// <summary>The M41a "Float" tip and other one-off changes to a fence from outside Fence settings.</summary>
    private void ChangeFence(string fenceId, Func<NeoFencesConfig, NeoFencesConfig> change)
    {
        if (_config.Fences.All(fence => fence.Id != fenceId)) return; // deleted meanwhile
        _config = change(_config);
        if (WindowShowing(fenceId) is { } window)
        {
            window.Refresh(_config.Fences.First(fence => fence.Id == fenceId));
            ApplyStyle(window);
        }
        ScheduleSave();
        RefreshAllFenceSettings();
    }

    private void UndoInSettings(FenceSettingsSession session)
    {
        if (!session.Undo.TryPop(out var step)) return;
        var rollup = _config.Settings.RollupExpand;
        var wallpaperAccent = Appearance.WallpaperAccent;
        _config = SettingEdits.UndoTo(_config, step.Config);
        foreach (var (itemId, panel) in step.PanelsBefore ?? []) SetPanel(itemId, panel);
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } fence) continue;
            window.Refresh(fence);
            window.SetTitle(fence.Title);
            RefreshTabs(window);
        }
        if (_config.Settings.RollupExpand != rollup)
        {
            foreach (var window in _windows.Values) window.SetRollupExpand(_config.Settings.RollupExpand);
        }
        if (Appearance.WallpaperAccent != wallpaperAccent) UpdateAccents();
        RestyleAll();
        ScheduleSave();
        RefreshAllFenceSettings();
        Log.Information("fence settings: undo");
    }

    /// <summary>Custom colour… in Fence settings: NeoFences' colour picker over the settings window; Cancel changes nothing.</summary>
    private void PickColourInSettings(FenceSettingsSession session)
    {
        if (session.FenceId is not { } fenceId || _config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { } fence) return;
        var current = Argb.FromHex(fence.CustomColor) ?? (fence.TabColor is { } swatch ? FenceLook.Swatches[swatch] : null);
        string? picked;
        try
        {
            picked = ColourWindow.Pick(session.Window, current);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "colour picker failed"); // hard rule 7: no custom colour this time
            return;
        }
        if (picked is not null) EditInSettings(session, config => FenceEdits.SetColour(config, fenceId, null, picked, false), "custom colour");
        else RefreshFenceSettings(fenceId);
    }

    /// <summary>A recipe (spec §4): one fence — one confirmation; All fences — two, a snapshot first, maybe only the games fences.</summary>
    private void ApplyRecipe(FenceSettingsSession session, Recipe recipe)
    {
        if (!session.AllFences && session.FenceId is { } fenceId)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { } fence || !RecipeDialogs.ConfirmOne(session.Window, recipe, fence.Title)) return;
            var steps = session.Undo.Count;
            EditInSettings(session, config => Recipes.Apply(config, fenceId, recipe), $"recipe {recipe.Name} (this fence)");
            ApplyRecipeLayout(recipe, [fenceId]);
            if (recipe.Inbox) MakeInboxPanels(session, [fenceId], steps);
            return;
        }
        var gameFences = _config.Fences.Where(fence => _items.Of(fence.Id).Any(item => item.GameId is not null)).Select(fence => fence.Id).ToList();
        if (RecipeDialogs.ConfirmAll(session.Window, recipe, _config.Fences.Count, gameFences.Count) is not { } onlyGames) return;
        var now = DateTimeOffset.Now;
        // The whole setup (final review): the recipe changes All fences' values in Settings, so restoring must bring those back too.
        if (_snapshots.Save(Snapshots.TakeWhole(_config, _items, name: $"Before {recipe.Name} ({now:d MMM HH:mm})", now: now)) is null)
        {
            Log.Warning(_snapshots.LastFailure, "recipe {Recipe}: the snapshot could not be saved; nothing changed", recipe.Name);
            MessageDialog.Tell(session.Window, "Nothing changed", "NeoFences could not save the snapshot to go back to, so the recipe was not applied (see the log).");
            return;
        }
        var undoSteps = session.Undo.Count;
        EditInSettings(session, config => Recipes.ApplyToAll(config, recipe, onlyGames ? gameFences : null), $"recipe {recipe.Name} ({(onlyGames ? "fences with games" : "all fences")})");
        var changed = onlyGames ? gameFences : [.. _config.Fences.Select(fence => fence.Id)];
        ApplyRecipeLayout(recipe, changed);
        if (recipe.Inbox) MakeInboxPanels(session, changed, undoSteps);
    }

    /// <summary>
    /// A recipe's Grid / Free (final review): only fences of items have a layout; a shown one takes the menu's path (to Free, its
    /// cells are pinned where the items show now).
    /// </summary>
    private void ApplyRecipeLayout(Recipe recipe, IReadOnlyCollection<string> fenceIds)
    {
        if (recipe.Layout is not { } layout) return;
        foreach (var fenceId in fenceIds)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { Kind: FenceKind.Items } fence || fence.Layout == layout) continue;
            if (WindowShowing(fenceId) is { } shown) SetFenceLayout(shown, layout);
            else _config = FenceEdits.SetLayout(_config, fenceId, layout);
        }
        ScheduleSave();
    }

    /// <summary>
    /// Downloads inbox: every folder panel in these fences shows Details, newest first. Ctrl+Z puts the panels back with the
    /// recipe's step (1.0.1); <paramref name="undoSteps"/> is the undo count before the recipe (a recipe that changed nothing
    /// else gets a step of its own).
    /// </summary>
    private void MakeInboxPanels(FenceSettingsSession session, IReadOnlyCollection<string> fenceIds, int undoSteps)
    {
        var before = fenceIds.SelectMany(fenceId => _items.Of(fenceId)).Where(item => item.Panel is not null).Select(item => (item.Id, item.Panel!)).ToList();
        if (before.Count == 0) return;
        foreach (var (itemId, panel) in before) SetPanel(itemId, panel with { Look = PanelLook.Details, Sort = PanelSort.Date, Descending = true });
        var step = session.Undo.Count > undoSteps ? session.Undo.Pop() : new SettingsUndoStep(_config);
        session.Undo.Push(step with { PanelsBefore = before });
        RefreshFenceSettings(session.FenceId ?? AllFencesKey); // Undo may have become available
    }

    /// <summary>Hold to compare: the fence (or every fence, in All fences mode) as it was when the window opened.</summary>
    private void Compare(FenceSettingsSession session, bool held)
    {
        if (!held)
        {
            RestyleAll();
            return;
        }
        foreach (var window in _windows.Values)
        {
            if (!session.AllFences && window.FenceId != session.FenceId) continue;
            if (session.Opened.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } was) continue;
            window.ApplyStyle(FenceLook.Resolve(session.Opened.Settings.Appearance, was, _lightTheme, AccentAt(window), WindowsAccentNow()));
        }
    }

    private static Argb WindowsAccentNow()
    {
        var accent = SystemColors.AccentColor;
        return new Argb(0xFF, accent.R, accent.G, accent.B);
    }

    /// <summary>The fence window whose shown tab is this fence, or null (another tab of its box is in front).</summary>
    private FenceWindow? WindowShowing(string fenceId) => _windows.Values.FirstOrDefault(window => window.FenceId == fenceId);

    /// <summary>Shows the fence (or All fences) as it is now; a fence that is gone (deleted, a snapshot restored) closes its window.</summary>
    private void RefreshFenceSettings(string key)
    {
        if (!_fenceSettings.TryGetValue(key, out var session)) return;
        var fence = session.FenceId is { } fenceId ? _config.Fences.FirstOrDefault(candidate => candidate.Id == fenceId) : null;
        if (session.FenceId is not null && fence is null)
        {
            session.Window.Close();
            return;
        }
        var items = fence is null ? [] : _items.Of(fence.Id);
        session.Window.Show(new FenceSettingsView(fence, _config, _lightTheme, session.AllFences, session.Undo.Count, items.Count,
            OnlyWidgets: items.Count > 0 && items.All(item => item.Kind == ItemKind.Widget)));
        UpdatePreview(session);
    }

    private void RefreshAllFenceSettings()
    {
        foreach (var key in _fenceSettings.Keys.ToList()) RefreshFenceSettings(key);
    }

    /// <summary>The preview: the fence's window drawing (All fences: the first fence that follows everything) over its wallpaper.</summary>
    private void UpdatePreview(FenceSettingsSession session)
    {
        var fenceId = session.FenceId
                      ?? _config.Fences.FirstOrDefault(fence => !Enum.GetValues<LookSetting>().Any(setting => AllFences.HasOwn(fence, setting)))?.Id
                      ?? _config.Fences.FirstOrDefault()?.Id;
        var window = fenceId is null ? null : WindowShowing(fenceId);
        if (window is null || window.Handle == 0)
        {
            session.Window.SetPreview(null, 300, 200, 30, null, new Rect(0, 0, 1, 1), "This fence is not shown right now.");
            return;
        }
        var style = FenceLook.Resolve(Appearance, _config.Fences.First(fence => fence.Id == fenceId), _lightTheme, null);
        var rect = FenceWindowChrome.GetPixelRect(window.Handle);
        var (centerX, centerY) = (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var monitor = _previewWallpapers?.FirstOrDefault(image => centerX >= image.Left && centerX < image.Right && centerY >= image.Top && centerY < image.Bottom);
        ImageSource? picture = null;
        var crop = new Rect(0, 0, 1, 1);
        string? note = null;
        if (monitor?.ImagePath is { } path && _previewPictures.TryGetValue(path, out var image))
        {
            picture = image;
            // ponytail: the picture is taken as filling its monitor (Windows' Fill); Fit / Tile wallpapers are cropped a little off.
            var (width, height) = ((double)(monitor.Right - monitor.Left), (double)(monitor.Bottom - monitor.Top));
            crop = new Rect((rect.X - monitor.Left) / width, (rect.Y - monitor.Top) / height, rect.Width / width, rect.Height / height);
            note = monitor.Origin;
        }
        note ??= _previewWallpapers is null ? "Reading your wallpaper…"
            : monitor?.Origin == WallpaperSources.PlaylistOrigin ? "Wallpaper Engine plays a playlist — showing a plain background"
            : "Your wallpaper isn't a picture — showing a plain background";
        session.Window.SetPreview(window.Content as Visual, window.ActualWidth, window.ActualHeight, style.TitleHeight, picture, crop, note);
    }

    /// <summary>
    /// Every monitor's wallpaper picture, read on the shell worker (Wallpaper Engine's preview included) when a settings window
    /// opens and when the wallpaper changes (1.0.1: once per run before); the old pictures show until the new ones are read.
    /// </summary>
    private void LoadPreviewWallpapers()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var images = WallpaperSources.Read((source, failure) => Log.Information(failure, "fence settings preview: {Source} could not be read", source));
            var pictures = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in images.Select(image => image.ImagePath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    // BeginInit/EndInit: OnLoad releases the file, the decode width keeps it small, IgnoreImageCache reads a file
                    // written again under the same name (Spotlight); frozen, it crosses to the UI thread. A corrupt picture throws.
                    var picture = new BitmapImage();
                    picture.BeginInit();
                    picture.UriSource = new Uri(path);
                    picture.CacheOption = BitmapCacheOption.OnLoad;
                    picture.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    picture.DecodePixelWidth = 1280;
                    picture.EndInit();
                    picture.Freeze();
                    pictures[path] = picture;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException) // hard rule 7: no picture, a plain background
                {
                    Log.Information(failure, "fence settings preview: the wallpaper picture could not be read");
                }
            }
            dispatcher.BeginInvoke(() =>
            {
                _previewWallpapers = images;
                _previewPictures = pictures;
                foreach (var session in _fenceSettings.Values) UpdatePreview(session);
            });
        });
    }

    /// <summary>The window opens beside its fence (left if there is room, else right), inside the work area.</summary>
    private void PlaceBesideFence(FenceSettingsWindow window, string? fenceId)
    {
        var work = SystemParameters.WorkArea;
        var fenceWindow = fenceId is null ? null : WindowShowing(fenceId);
        if (fenceWindow is null)
        {
            window.Left = work.Left + (work.Width - window.Width) / 2;
            window.Top = work.Top + (work.Height - window.Height) / 2;
            return;
        }
        var left = fenceWindow.Left - window.Width - 12;
        if (left < work.Left) left = fenceWindow.Left + fenceWindow.ActualWidth + 12;
        window.Left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - window.Width));
        window.Top = Math.Clamp(fenceWindow.Top, work.Top, Math.Max(work.Top, work.Bottom - window.Height));
    }
}
