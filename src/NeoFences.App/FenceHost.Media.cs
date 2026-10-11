using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NeoFences.Core.Items;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Now playing (M41a, spec 2026-10-11-m41a-feel-design §6.6, ADR-065): Windows' media sessions are read on the widget tick
/// while a Now playing widget can be seen (one read at a time, off the UI thread); covers are read once per track and kept
/// in memory only. Nothing is read in a game, paused, hidden or in safe mode (the tick does not run then).
/// </summary>
public sealed partial class FenceHost
{
    private MediaSessions? _mediaSessions;
    private bool _mediaInFlight;
    private IReadOnlyList<MediaSessionInfo> _sessions = [];
    private MediaTrack? _lastTrack;
    private readonly Dictionary<string, string> _mediaChoice = new(StringComparer.Ordinal); // a widget's chosen player (this run)
    private readonly Dictionary<string, (string Key, ImageSource? Cover, ImageSource? Glow)> _covers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>From the widget tick: a fresh reading when a Now playing widget is shown, then every such widget updated.</summary>
    private void TickMedia(IReadOnlyList<FenceWindow> windows)
    {
        var showing = windows.Where(window => window.ShowsWidget(WidgetKind.NowPlaying)).ToList();
        if (showing.Count == 0) return;
        foreach (var window in showing) window.ShowMedia(MediaFor); // the position runs on between readings
        if (_mediaInFlight) return;
        _mediaInFlight = true;
        _mediaSessions ??= new MediaSessions(message => Log.Warning("{Message}", message));
        var sessions = _mediaSessions;
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Task.Run(sessions.ReadAsync).ContinueWith(read => dispatcher.BeginInvoke(() =>
        {
            _mediaInFlight = false;
            _sessions = read.IsCompletedSuccessfully ? read.Result : [];
            if (_sessions.FirstOrDefault()?.Track is { Title.Length: > 0 } playing) _lastTrack = playing;
            foreach (var session in _sessions.Where(session => !_covers.TryGetValue(session.AppId, out var known) || known.Key != session.CoverKey)) ReadCover(session);
            foreach (var window in WidgetWindows()) window.ShowMedia(MediaFor);
        }));
    }

    private NowPlayingData MediaFor(string widgetKey)
    {
        var shown = _mediaChoice.TryGetValue(widgetKey, out var chosen) && _sessions.FirstOrDefault(session => session.AppId == chosen) is { } picked
            ? picked : _sessions.FirstOrDefault();
        var face = NowPlaying.Face(current: shown?.Track, last: _lastTrack);
        var art = shown is not null && _covers.TryGetValue(shown.AppId, out var cover) ? cover : default;
        return new NowPlayingData(_sessions, shown, face, art.Cover, art.Glow);
    }

    /// <summary>The track's cover, once per track: decoded for the sleeve, and a few pixels wide for the glow.</summary>
    private void ReadCover(MediaSessionInfo session)
    {
        _covers[session.AppId] = (session.CoverKey, _covers.GetValueOrDefault(session.AppId).Cover, _covers.GetValueOrDefault(session.AppId).Glow); // asked once
        var sessions = _mediaSessions!;
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Task.Run(() => sessions.CoverAsync(session.AppId)).ContinueWith(read => dispatcher.BeginInvoke(() =>
        {
            if (!read.IsCompletedSuccessfully || read.Result is not { } bytes) { _covers[session.AppId] = (session.CoverKey, null, null); return; }
            _covers[session.AppId] = (session.CoverKey, Decode(bytes, width: 300), Decode(bytes, width: 6));
            foreach (var window in WidgetWindows()) window.ShowMedia(MediaFor);
        }));
    }

    private static ImageSource? Decode(byte[] bytes, int width)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(bytes);
            image.DecodePixelWidth = width;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning("now playing: a cover could not be decoded ({Message})", failure.Message);
            return null;
        }
    }

    /// <summary>A widget's button: the command to the player (then a fresh reading), or (no command) the player to the front.</summary>
    private void OnMediaRequested(string appId, MediaCommand? command, TimeSpan? at)
    {
        if (command is not { } asked)
        {
            PlayerWindows.BringToFront(appId, log: message => Log.Information("{Message}", message));
            return;
        }
        var sessions = _mediaSessions;
        if (sessions is null) return;
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Task.Run(() => sessions.CommandAsync(appId, asked, at)).ContinueWith(_ => dispatcher.BeginInvoke(() => TickMedia(WidgetWindows())));
    }

    private void OnMediaPlayerChosen(string widgetKey, string appId)
    {
        _mediaChoice[widgetKey] = appId;
        foreach (var window in WidgetWindows()) window.ShowMedia(MediaFor);
    }
}
