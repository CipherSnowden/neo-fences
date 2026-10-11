using NeoFences.Core.Items;
using Windows.Media.Control;

namespace NeoFences.Shell;

/// <summary>A player's session as Now playing shows it (M41a, spec 2026-10-11-m41a-feel-design §6.6).</summary>
/// <param name="AppId">Windows' id of the player ("Spotify.exe", a Store app's AUMID): the key to switch and command it.</param>
/// <param name="Updated">When Windows last updated <paramref name="Position"/> (it runs on from there while playing).</param>
/// <param name="CoverKey">Changes with the track: the cover is read again only then.</param>
public sealed record MediaSessionInfo(
    string AppId, MediaTrack Track, bool Playing, TimeSpan Position, TimeSpan? End, DateTimeOffset Updated,
    bool CanPlayPause, bool CanNext, bool CanPrevious, bool CanSeek, bool? Shuffle, MediaRepeat? Repeat, string CoverKey);

public enum MediaRepeat { None, Track, List }

public enum MediaCommand { PlayPause, Next, Previous, Shuffle, Repeat, Seek }

/// <summary>
/// Windows' media sessions (M41a, ADR-065): whatever plays with Windows' media controls — Spotify, browsers, Media Player.
/// Read on demand (the widget's tick), never kept open beyond the manager; every failure is logged and reads as nothing
/// playing (hard rule 7). Covers are kept in memory only, never written to disk. WinRT through the Windows SDK projection,
/// not CsWin32: these are WinRT APIs, which CsWin32 does not cover (hard rule 5's exception, ADR-065).
/// </summary>
public sealed class MediaSessions(Action<string> log)
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private readonly object _gate = new(); // reads, covers and commands run on pool threads at once (final review)
    private readonly HashSet<string> _failuresLogged = new(StringComparer.Ordinal);

    /// <summary>Final review I6: a player that never answers must not hold Now playing up — every WinRT wait is bounded.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(3);

    private async Task<GlobalSystemMediaTransportControlsSessionManager?> ManagerAsync()
    {
        lock (_gate) if (_manager is { } known) return known;
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().WaitAsync(Patience);
            lock (_gate) return _manager ??= manager;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) { Once("the media sessions API is not available", failure); return null; }
    }

    /// <summary>Every session, Windows' current one first; empty when nothing plays or Windows refuses.</summary>
    public async Task<IReadOnlyList<MediaSessionInfo>> ReadAsync()
    {
        try { return await ReadSessionsAsync(); }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Once("the media sessions could not be read", failure);
            lock (_gate) _manager = null; // a broken manager is asked for again next time
            return [];
        }
    }

    private async Task<IReadOnlyList<MediaSessionInfo>> ReadSessionsAsync()
    {
        if (await ManagerAsync() is not { } manager) return [];
        var current = manager.GetCurrentSession()?.SourceAppUserModelId;
        var shown = new List<MediaSessionInfo>();
        foreach (var session in manager.GetSessions())
        {
            try
            {
                var properties = await session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(Patience);
                var playback = session.GetPlaybackInfo();
                var timeline = session.GetTimelineProperties();
                var controls = playback.Controls;
                var title = properties?.Title ?? "";
                if (title.Length == 0 && playback.PlaybackStatus is not GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) continue; // an idle, empty session
                var end = timeline.EndTime - timeline.StartTime;
                var track = new MediaTrack(title, properties?.Artist ?? "", properties?.AlbumTitle ?? "", NowPlaying.AppName(session.SourceAppUserModelId));
                shown.Add(new MediaSessionInfo(session.SourceAppUserModelId, track,
                    playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    timeline.Position - timeline.StartTime, end > TimeSpan.Zero ? end : null, timeline.LastUpdatedTime,
                    controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled, controls.IsNextEnabled, controls.IsPreviousEnabled,
                    controls.IsPlaybackPositionEnabled && end > TimeSpan.Zero,
                    controls.IsShuffleEnabled ? playback.IsShuffleActive == true : null,
                    controls.IsRepeatEnabled ? (MediaRepeat)(int)(playback.AutoRepeatMode ?? Windows.Media.MediaPlaybackAutoRepeatMode.None) : null,
                    $"{session.SourceAppUserModelId}|{title}|{properties?.Artist}|{properties?.AlbumTitle}"));
            }
            catch (Exception failure) when (failure is not OutOfMemoryException) { Once($"a player's session could not be read ({session.SourceAppUserModelId})", failure); }
        }
        return [.. shown.OrderBy(info => info.AppId == current ? 0 : 1)];
    }

    /// <summary>The cover of a session's track (encoded image bytes, for the UI to decode), or null.</summary>
    public async Task<byte[]?> CoverAsync(string appId)
    {
        try
        {
            if (await ManagerAsync() is not { } manager || Session(manager, appId) is not { } session) return null;
            if ((await session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(Patience))?.Thumbnail is not { } thumbnail) return null;
            using var stream = await thumbnail.OpenReadAsync().AsTask().WaitAsync(Patience);
            if (stream.Size is 0 or > 8 * 1024 * 1024) return null; // a broken or absurd cover
            using var copy = new MemoryStream();
            await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(copy);
            return copy.ToArray();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) { Once($"a cover could not be read ({appId})", failure); return null; }
    }

    /// <summary>Play/pause, next, previous, shuffle on/off, the next repeat mode, or a seek: true when the player took it.</summary>
    public async Task<bool> CommandAsync(string appId, MediaCommand command, TimeSpan? seekTo = null)
    {
        try
        {
            if (await ManagerAsync() is not { } manager || Session(manager, appId) is not { } session) return false;
            var playback = session.GetPlaybackInfo();
            var asked = command switch
            {
                MediaCommand.PlayPause => session.TryTogglePlayPauseAsync(),
                MediaCommand.Next => session.TrySkipNextAsync(),
                MediaCommand.Previous => session.TrySkipPreviousAsync(),
                MediaCommand.Shuffle => session.TryChangeShuffleActiveAsync(playback.IsShuffleActive != true),
                MediaCommand.Repeat => session.TryChangeAutoRepeatModeAsync(playback.AutoRepeatMode switch
                {
                    Windows.Media.MediaPlaybackAutoRepeatMode.None => Windows.Media.MediaPlaybackAutoRepeatMode.List,
                    Windows.Media.MediaPlaybackAutoRepeatMode.List => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                    _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
                }),
                MediaCommand.Seek when seekTo is { } at => session.TryChangePlaybackPositionAsync((session.GetTimelineProperties().StartTime + at).Ticks),
                _ => null,
            };
            return asked is not null && await asked.AsTask().WaitAsync(Patience);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) { Once($"the player refused {command} ({appId})", failure); return false; }
    }

    private static GlobalSystemMediaTransportControlsSession? Session(GlobalSystemMediaTransportControlsSessionManager manager, string appId) =>
        manager.GetSessions().FirstOrDefault(session => string.Equals(session.SourceAppUserModelId, appId, StringComparison.OrdinalIgnoreCase));

    private void Once(string what, Exception failure)
    {
        bool first;
        lock (_gate) first = _failuresLogged.Add(what);
        if (first) log($"now playing: {what}: {failure.GetType().Name}: {failure.Message}");
    }
}
