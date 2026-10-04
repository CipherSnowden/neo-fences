using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NeoFences.Core.Items;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Loads display names and icons on two background STA threads (shell extensions expect STA; thumbnails of big
/// files are slow) and hands them to the UI thread. An item's own name and icon win (M18); a website shows the default
/// browser's icon. A failed load leaves the placeholder, never throws.
/// </summary>
public sealed class IconLoader : IDisposable
{
    private sealed record LoadRequest(FenceItemView View, int Number, int SizePx, string Target, bool WantsName, ItemIcon? OwnIcon);

    private readonly BlockingCollection<LoadRequest> _requests = new();
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

    /// <summary>Called on the UI thread. With two workers an older request can finish last: only the newest is applied.</summary>
    public void Request(FenceItemView view, int sizePx)
    {
        view.IconRequest++;
        _requests.TryAdd(new LoadRequest(view, view.IconRequest, sizePx, view.Target, WantsName: view.OwnName is null, view.OwnIcon));
    }

    private void Work()
    {
        foreach (var request in _requests.GetConsumingEnumerable())
        {
            try
            {
                var kind = ItemKinds.Of(request.Target);
                // A share is asked first, at most 2 s (M19 R7): a dead one would hold this worker in the shell for minutes.
                var reachable = kind != ItemKind.Path || !TargetChecks.IsNetworkPath(request.Target)
                                || TargetProbe.Check(request.Target).State == TargetState.Ok;
                var label = !request.WantsName ? null
                    : kind == ItemKind.Website ? ItemKinds.WebsiteName(request.Target)
                    : reachable ? ShellItems.TryGetDisplayName(request.Target) : null;
                var icon = OwnIcon(request) ?? (reachable ? TargetIcon(request, kind) : null) ?? GenericIcon(request, kind);
                _uiDispatcher.BeginInvoke(() =>
                {
                    if (request.Number != request.View.IconRequest) return; // a newer request (size, target, icon) is on its way
                    if (label is not null && request.View.OwnName is null) request.View.Label = label;
                    if (icon is not null) request.View.Icon = icon;
                });
            }
            catch (Exception failure)
            {
                // One bad item (odd bitmap, huge thumbnail) must not kill the app and crash-loop it (M2b review I5).
                Log.Warning(failure, "could not load the icon of {Target}", request.Target);
            }
        }
    }

    /// <summary>The item's own icon: one in a file, or a picture in NeoFences' icons folder. Null when it cannot be read (then the target's).</summary>
    private static BitmapSource? OwnIcon(LoadRequest request)
    {
        if (request.OwnIcon is { File: { Length: > 0 } file } icon) return Frozen(ShellItems.TryGetFileIcon(file, icon.Index, request.SizePx));
        if (request.OwnIcon is not { Image: { Length: > 0 } image }) return null;
        try
        {
            var picture = new BitmapImage();
            picture.BeginInit();
            picture.CacheOption = BitmapCacheOption.OnLoad; // the file is not kept open
            picture.DecodePixelWidth = request.SizePx;
            picture.UriSource = new Uri(Path.Combine(AppPaths.IconsDirectory, Path.GetFileName(image)));
            picture.EndInit();
            picture.Freeze();
            return picture;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "item picture {Image} could not be read; the target's icon shows", image);
            return null;
        }
    }

    private static BitmapSource? TargetIcon(LoadRequest request, ItemKind kind) =>
        kind == ItemKind.Website
            ? ShellItems.DefaultBrowserPath() is { } browser ? Frozen(ShellItems.TryGetImage(browser, request.SizePx)) : null
            : Frozen(ShellItems.TryGetImage(request.Target, request.SizePx));

    private static bool _websiteIconFailureLogged;

    /// <summary>
    /// No icon from the shell (M19 R8): a file or folder (missing, unreachable) or an app (uninstalled) gets Windows' icon for
    /// its type, by name only; a website's failed browser icon is logged once (the unreproduced blank icon of the M18 check).
    /// </summary>
    private static BitmapSource? GenericIcon(LoadRequest request, ItemKind kind)
    {
        if (kind == ItemKind.Website)
        {
            if (!_websiteIconFailureLogged) Log.Information("no browser icon for website {Target}; it shows without one", request.Target);
            _websiteIconFailureLogged = true;
            return null;
        }
        if (kind != ItemKind.Path && !ItemKinds.IsApp(request.Target)) return null; // a special item without an icon stays as it is
        var looksLikeFolder = kind == ItemKind.Path && !Path.HasExtension(request.Target.TrimEnd('\\')); // ponytail: by its name; the check knows better
        return Frozen(ShellItems.TryGetGenericImage(request.Target, looksLikeFolder));
    }

    private static BitmapSource? Frozen(ShellImage? image)
    {
        if (image is null) return null;
        var icon = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Width * 4);
        icon.Freeze(); // frozen: usable from the UI thread
        return icon;
    }

    public void Dispose() => _requests.CompleteAdding();
}
