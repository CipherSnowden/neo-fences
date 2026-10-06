using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Game covers and website icons (M34, spec 2026-10-06-icons-and-game-tiles-design, ADR-055): the art order, the ask-once
/// question, the online lookup after each library scan (only after a yes), website icons from the sites themselves,
/// "Choose cover…" and the cover sizes. Only NeoFences' own files change (the covers folder and its index).
/// </summary>
public sealed partial class FenceHost
{
    private CoversIndex _coversIndex = CoversIndex.Empty;
    private bool _coverLookupRunning, _onlineArtAskPending;
    private OnlineArtWindow? _onlineArtWindow;
    private readonly HashSet<string> _siteIconsFetching = new(StringComparer.OrdinalIgnoreCase);

    private static string CoversIndexPath => Path.Combine(AppPaths.CoversDirectory, "index.json");

    private void LoadCovers()
    {
        _coversIndex = CoversIndex.Load(CoversIndexPath);
        PublishSiteIcons();
        _iconLoader.SiteIconWanted += OnSiteIconWanted;
    }

    /// <summary>The art a game shows: the user's choice, the cover on disk, a cover found online, the logo; null: the glow tile.</summary>
    private CoverArt? ArtOf(GameEntry game) =>
        GameArt.Choose(chosen: ChosenCover(game), poster: game.Poster, online: FoundCover(game), logo: game.IconPath);

    private string? ChosenCover(GameEntry game) =>
        _config.Library.CoverChoices.TryGetValue(game.Id, out var file) && Path.Combine(AppPaths.CoversDirectory, file) is var path && File.Exists(path) ? path : null;

    /// <summary>A cover found online stays when the switch is turned off later (spec §2: what was found is kept).</summary>
    private string? FoundCover(GameEntry game) =>
        _coversIndex.Games.TryGetValue(game.Id, out var record) && record.File is { } file && Path.Combine(AppPaths.CoversDirectory, file) is var path && File.Exists(path) ? path : null;

    private List<GameEntry> GamesWithoutCover() =>
        [.. _library.Items.Select(item => item.Game).Where(game => GameArt.NeedsLookup(ChosenCover(game), game.Poster))];

    /// <summary>
    /// Asked once (spec §2): the first time a scan leaves games without a cover. Held while a game runs; closing the window
    /// without an answer asks again at the next scan. Never in safe mode or a read-only session.
    /// </summary>
    private void MaybeAskOnlineArt()
    {
        if (_config.Library.OnlineArt is not null || SafeMode || _configReadOnly || _onlineArtWindow is not null) return;
        var without = GamesWithoutCover();
        if (without.Count == 0) return;
        if (_gameMode)
        {
            _onlineArtAskPending = true; // CheckGameMode asks once the game ends
            return;
        }
        _onlineArtAskPending = false;
        _onlineArtWindow = new OnlineArtWindow(without.Count);
        _onlineArtWindow.Answered += SetOnlineArt;
        _onlineArtWindow.Closed += (_, _) => _onlineArtWindow = null;
        _onlineArtWindow.Show();
        Log.Information("online art: asked ({Count} games without a cover)", without.Count);
    }

    private void SetOnlineArt(bool on)
    {
        if (_config.Library.OnlineArt == on) return;
        _config = _config with { Library = _config.Library with { OnlineArt = on } };
        Log.Information("online art: {State}", on ? "on" : "off");
        SaveNow();
        RefreshSettings();
        if (!on) return;
        LookUpCovers();
        foreach (var window in _windows.Values) window.ReloadIcons(); // websites ask for their icons
    }

    /// <summary>
    /// After a library scan (spec §2): games still without a cover are looked up on the Steam store, one at a time, in the
    /// background; strict names only. A miss is remembered (30 days, or until the name changes); offline records nothing.
    /// </summary>
    private void LookUpCovers()
    {
        if (_config.Library.OnlineArt != true || _coverLookupRunning || !Current.ExtrasWanted || _gameMode || _libraryStopped) return;
        var now = DateTimeOffset.Now;
        var index = _coversIndex;
        var wanted = GamesWithoutCover().Where(game => GameArt.ShouldLookUp(index.Games.GetValueOrDefault(game.Id), game.Name, now)).ToList();
        if (wanted.Count == 0) return;
        _coverLookupRunning = true;
        Log.Information("online art: looking up {Count} game(s)", wanted.Count);
        Task.Run(async () =>
        {
            var matched = new Dictionary<GameEntry, int>();
            var missed = new List<GameEntry>();
            foreach (var game in wanted)
            {
                if (await OnlineArt.SearchAsync(game.Name) is not { } results) break; // offline: the rest waits for the next scan
                if (results.FirstOrDefault(result => GameArt.SameName(game.Name, result.Name)) is { } match) matched[game] = match.AppId;
                else missed.Add(game);
                await Task.Delay(300); // one request at a time, gently
            }
            var urls = await OnlineArt.CoverUrlsAsync([.. matched.Values.Distinct()]);
            var found = new Dictionary<GameEntry, (int AppId, string File)>();
            foreach (var (game, appId) in matched)
            {
                var file = $"steam-{appId}.jpg";
                if (File.Exists(Path.Combine(AppPaths.CoversDirectory, file))
                    || urls.TryGetValue(appId, out var url) && await OnlineArt.DownloadImageAsync(url, Path.Combine(AppPaths.CoversDirectory, file)))
                {
                    found[game] = (appId, file);
                }
            }
            return (Found: found, Missed: missed);
        }).ContinueWith(lookup =>
        {
            _coverLookupRunning = false;
            if (lookup.IsFaulted)
            {
                Log.Warning(lookup.Exception, "online art: the lookup failed"); // hard rule 7: the glow tiles stay
                return;
            }
            var (found, missed) = lookup.Result;
            var checkedAt = DateTimeOffset.Now;
            foreach (var (game, cover) in found) _coversIndex = _coversIndex.WithGame(game.Id, new CoverRecord(game.Name, checkedAt, cover.AppId, cover.File));
            foreach (var game in missed) _coversIndex = _coversIndex.WithGame(game.Id, new CoverRecord(game.Name, checkedAt));
            SaveCoversIndex();
            Log.Information("online art: {Found} cover(s) found, {Missed} not on the Steam store", found.Count, missed.Count);
            if (found.Count == 0) return;
            _libraryLister?.Refresh();
            RefreshWindows();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void SaveCoversIndex()
    {
        try
        {
            _coversIndex.Save(CoversIndexPath);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Warning(failure, "online art: the covers index could not be saved; the art is looked up again next time");
        }
    }

    /// <summary>The icon files found so far, for the icon loader's threads (a new dictionary each time: they only read it).</summary>
    private void PublishSiteIcons() =>
        _iconLoader.SiteIconFiles = _coversIndex.Sites.Where(site => site.Value.File is not null)
            .ToDictionary(site => site.Key, site => Path.Combine(AppPaths.SiteIconsDirectory, site.Value.File!), StringComparer.OrdinalIgnoreCase);

    /// <summary>A website showed its letter badge: with online art on, its own icon is fetched once (a miss waits 30 days).</summary>
    private void OnSiteIconWanted(string url)
    {
        if (_config.Library.OnlineArt != true || !Current.ExtrasWanted || !Uri.TryCreate(url, UriKind.Absolute, out var page)
            || page.Scheme is not ("http" or "https") || !SiteIcons.ShouldFetch(_coversIndex.Sites.GetValueOrDefault(page.Host), DateTimeOffset.Now)
            || !_siteIconsFetching.Add(page.Host)) return;
        Task.Run(() => OnlineArt.FetchSiteIconAsync(page, AppPaths.SiteIconsDirectory)).ContinueWith(fetch =>
        {
            _siteIconsFetching.Remove(page.Host);
            var file = fetch.IsCompletedSuccessfully ? fetch.Result : null;
            _coversIndex = _coversIndex.WithSite(page.Host, new SiteRecord(DateTimeOffset.Now, file));
            SaveCoversIndex();
            Log.Information("online art: website icon for {Host}: {Result}", page.Host, file ?? "none");
            if (file is null) return;
            PublishSiteIcons();
            foreach (var window in _windows.Values) window.ReloadIcons();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>"Choose cover…" (spec §3): Steam's results, a picture of the user's own, or back to automatic.</summary>
    private void ChooseCover(VirtualItem item)
    {
        if (LibraryItemOf(item.Target)?.Game is not { } game) return;
        var window = new ChooseCoverWindow(game.Name, online: _config.Library.OnlineArt == true && Current.ExtrasWanted,
            hasChoice: _config.Library.CoverChoices.ContainsKey(game.Id));
        var choiceFile = $"choice-{FileNameOf(game.Id)}";
        window.StoreCoverChosen += cover => Task.Run(() => OnlineArt.DownloadImageAsync(cover, Path.Combine(AppPaths.CoversDirectory, choiceFile + ".jpg")))
            .ContinueWith(download =>
            {
                if (download.IsCompletedSuccessfully && download.Result) SetCoverChoice(game, choiceFile + ".jpg");
                else Log.Warning("online art: the chosen cover for {Game} could not be downloaded", game.Name);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        window.FileChosen += picture =>
        {
            var file = choiceFile + Path.GetExtension(picture).ToLowerInvariant();
            try
            {
                Directory.CreateDirectory(AppPaths.CoversDirectory);
                File.Copy(picture, Path.Combine(AppPaths.CoversDirectory, file), overwrite: true); // a copy: the user's file is never moved or changed
                SetCoverChoice(game, file);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Log.Warning(failure, "the cover picture {Picture} could not be copied", picture);
            }
        };
        window.ResetRequested += () => SetCoverChoice(game, file: null);
        window.Show();
    }

    private void SetCoverChoice(GameEntry game, string? file)
    {
        var choices = new Dictionary<string, string>(_config.Library.CoverChoices, StringComparer.OrdinalIgnoreCase);
        if (file is null)
        {
            if (choices.Remove(game.Id, out var old)) TryDeleteCover(old);
        }
        else choices[game.Id] = file;
        _config = _config with { Library = _config.Library with { CoverChoices = choices } };
        Log.Information("cover of {Game}: {Choice}", game.Name, file ?? "automatic");
        SaveNow();
        _libraryLister?.Refresh();
        RefreshWindows();
    }

    private static void TryDeleteCover(string file)
    {
        try
        {
            File.Delete(Path.Combine(AppPaths.CoversDirectory, Path.GetFileName(file))); // NeoFences' own copy only
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Warning(failure, "an old cover copy could not be removed");
        }
    }

    private static string FileNameOf(string gameId) =>
        string.Concat(gameId.Take(80).Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-'));

    /// <summary>Size ▸ for covers (owner's choice at planning): Normal (1×2) or Large (2×4, twice).</summary>
    private MenuItem CoverSizeMenu(IReadOnlyList<VirtualItem> items)
    {
        var size = new MenuItem { Header = "Size" };
        var spans = items.Select(FenceGrid.SpanOf).Distinct().ToList();
        foreach (var (name, span) in new[] { ("Normal", CoverSizes.Normal), ("Large (twice as big)", CoverSizes.Large) })
        {
            var entry = new MenuItem { Header = name, IsCheckable = true, IsChecked = spans.Count == 1 && spans[0] == span };
            entry.Click += (_, _) => SetSize([.. items.Select(item => item.Id)], span == CoverSizes.Normal ? null : span);
            size.Items.Add(entry);
        }
        return size;
    }
}
