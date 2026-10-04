using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Loads display names and icons on two background STA threads (shell extensions expect STA; thumbnails of big
/// files are slow) and hands them to the UI thread. A failed load leaves the placeholder, never throws.
/// </summary>
public sealed class IconLoader : IDisposable
{
    private readonly BlockingCollection<(FenceItemView View, int SizePx)> _requests = new();
    private readonly Dispatcher _uiDispatcher;

    public IconLoader(Dispatcher uiDispatcher)
    {
        _uiDispatcher = uiDispatcher;
        // Two STA workers: one slow thumbnail (a big video, a network shortcut) no longer holds up every other icon (M2b review).
        for (var workerNumber = 1; workerNumber <= 2; workerNumber++)
        {
            var worker = new Thread(Work) { IsBackground = true, Name = $"NeoFences icon loader {workerNumber}" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }
    }

    /// <summary>Called on the UI thread. With two workers an older request can finish last: only the wanted size is applied.</summary>
    public void Request(FenceItemView view, int sizePx)
    {
        view.WantedSizePx = sizePx;
        _requests.TryAdd((view, sizePx));
    }

    private void Work()
    {
        foreach (var (view, sizePx) in _requests.GetConsumingEnumerable())
        {
            try
            {
                var label = ShellItems.TryGetDisplayName(view.ItemRef);
                var image = ShellItems.TryGetImage(view.ItemRef, sizePx);
                BitmapSource? icon = null;
                if (image is not null)
                {
                    icon = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Width * 4);
                    icon.Freeze(); // frozen: usable from the UI thread
                }
                _uiDispatcher.BeginInvoke(() =>
                {
                    if (label is not null) view.Label = label;
                    if (icon is not null && sizePx == view.WantedSizePx) view.Icon = icon;
                });
            }
            catch (Exception failure)
            {
                // One bad item (odd bitmap, huge thumbnail) must not kill the app and crash-loop it (M2b review I5).
                Log.Warning(failure, "could not load the icon of {ItemRef}", view.ItemRef);
            }
        }
    }

    public void Dispose() => _requests.CompleteAdding();
}
