using Windows.Media;
using Windows.Storage;
using Windows.Storage.Streams;

// FakePlayer <log file> [cover.png]: a hidden window whose media controls say "Midnight City · M83" is playing (4:03 long);
// play / pause / next / previous from Windows (a Now playing widget) are logged one per line and change what it shows.
var log = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "fakeplayer.log");
var cover = args.Length > 1 ? args[1] : null;
ApplicationConfiguration.Initialize();
using var form = new Form { ShowInTaskbar = false, Opacity = 0, Text = "FakePlayer" };
form.Load += async (_, _) =>
{
    var controls = SystemMediaTransportControlsInterop.GetForWindow(form.Handle);
    controls.IsEnabled = controls.IsPlayEnabled = controls.IsPauseEnabled = controls.IsNextEnabled = controls.IsPreviousEnabled = true;
    string[] titles = ["Midnight City", "Wait", "Outro"];
    var track = 0;
    async Task Show()
    {
        var updater = controls.DisplayUpdater;
        updater.Type = MediaPlaybackType.Music;
        updater.MusicProperties.Title = titles[track % titles.Length];
        updater.MusicProperties.Artist = "M83";
        updater.MusicProperties.AlbumTitle = "Hurry Up, We're Dreaming";
        if (cover is not null && File.Exists(cover)) updater.Thumbnail = RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(cover));
        updater.Update();
        controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(243), MinSeekTime = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromSeconds(243), Position = TimeSpan.FromSeconds(101),
        });
    }
    controls.ButtonPressed += (_, pressed) => form.BeginInvoke(async () =>
    {
        File.AppendAllText(log, pressed.Button + Environment.NewLine);
        switch (pressed.Button)
        {
            case SystemMediaTransportControlsButton.Pause: controls.PlaybackStatus = MediaPlaybackStatus.Paused; break;
            case SystemMediaTransportControlsButton.Play: controls.PlaybackStatus = MediaPlaybackStatus.Playing; break;
            case SystemMediaTransportControlsButton.Next: track++; await Show(); break;
            case SystemMediaTransportControlsButton.Previous: track = Math.Max(0, track - 1); await Show(); break;
        }
    });
    controls.PlaybackStatus = MediaPlaybackStatus.Playing;
    await Show();
    File.AppendAllText(log, "ready" + Environment.NewLine);
};
Application.Run(form);
