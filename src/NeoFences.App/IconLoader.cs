using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Loads display names and icons on two background STA threads (shell extensions expect STA; thumbnails of big
/// files are slow) and hands them to the UI thread. An item's own name and icon win (M18); a website shows its own icon
/// when NeoFences found one, else a letter badge (M34). A failed load leaves the placeholder, never throws.
/// </summary>
public sealed class IconLoader : IDisposable
{
    private sealed record LoadRequest(FenceItemView View, int Number, int SizePx, string Target, bool WantsName, ItemIcon? OwnIcon, bool Fresh);

    // M37 (spec §4): what can be seen first, the rest after (TakeFromAny takes from the first queue that has something).
    private readonly BlockingCollection<LoadRequest> _urgent = new(), _later = new();
    private readonly Dispatcher _uiDispatcher;

    // M37 (spec §3): what was loaded fresh this run — a key is asked of the shell once per run, until its stamp changes or a
    // refresh asks again — and the cache on disk, read before the shell.
    private readonly ConcurrentDictionary<string, (BitmapSource? Icon, string? Name, IconStamp? Stamp)> _session = new(StringComparer.Ordinal);
    private readonly IconDiskCache? _disk;
    private readonly ManualResetEventSlim _diskReady = new(false);

    public IconLoader(Dispatcher uiDispatcher, IconDiskCache? disk = null)
    {
        _uiDispatcher = uiDispatcher;
        _disk = disk;
        // Two STA workers: one slow thumbnail (a big video, a network shortcut) no longer holds up every other icon (M2b review).
        for (var workerNumber = 1; workerNumber <= 2; workerNumber++)
        {
            var first = workerNumber == 1;
            var worker = new Thread(() =>
            {
                if (first)
                {
                    try
                    {
                        _disk?.LoadAndPrune(); // M37: the index first (a few ms), so the first icons can come from it
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        Log.Warning(failure, "icon cache unavailable; icons load fresh");
                    }
                    _diskReady.Set();
                }
                _diskReady.Wait();
                Work();
            }) { IsBackground = true, Name = $"NeoFences icon loader {workerNumber}" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }
    }

    /// <summary>
    /// M37: raised on the UI thread each time no request is waiting any more, with the requests so far and how many the cache
    /// answered (the "icons settled" timing mark).
    /// </summary>
    public event Action<int, int>? Settled;

    private int _pending, _requested; // UI thread only

    /// <summary>Requests still waiting (UI thread; M37: "icons settled" before the fences were shown).</summary>
    public int Pending => _pending;

    /// <summary>Requests so far (UI thread).</summary>
    public int Requested => _requested;

    /// <summary>Requests answered from the icon cache on disk so far (M37).</summary>
    public int FromCache { get; private set; }

    /// <summary>Called on the UI thread. With two workers an older request can finish last: only the newest is applied.</summary>
    /// <param name="urgent">The item can be seen now (M37): before the others.</param>
    /// <param name="fresh">Ask the shell even when the icon is known (Refresh, the Recycle Bin's full / empty icon).</param>
    public void Request(FenceItemView view, int sizePx, bool urgent = true, bool fresh = false)
    {
        view.IconRequest++;
        view.IconPending = true;
        _pending++;
        _requested++;
        (urgent ? _urgent : _later).TryAdd(new LoadRequest(view, view.IconRequest, sizePx, view.Target, WantsName: view.OwnName is null, view.OwnIcon, fresh));
    }

    private void Work()
    {
        while (true)
        {
            LoadRequest request;
            try
            {
                BlockingCollection<LoadRequest>.TakeFromAny([_urgent, _later], out request!);
            }
            catch (Exception ended) when (ended is ArgumentException or InvalidOperationException or ObjectDisposedException)
            {
                return; // exit: the queues were completed
            }
            try
            {
                Load(request);
            }
            catch (Exception failure)
            {
                // One bad item (odd bitmap, huge thumbnail) must not kill the app and crash-loop it (M2b review I5).
                Log.Warning(failure, "could not load the icon of {Target}", request.Target);
                _uiDispatcher.BeginInvoke(() => Finish(request));
            }
        }
    }

    /// <summary>
    /// This run's icon, else the cached one (shown at once), else — or behind a cached one whose source may have changed (M37
    /// spec §3) — the shell's. A fresh icon that looks the same as the cached one is not applied again.
    /// </summary>
    private void Load(LoadRequest request)
    {
        var key = IconCache.KeyOf(request.Target, request.OwnIcon, request.SizePx);
        var stamp = StampOf(request);
        if (!request.Fresh && _session.TryGetValue(key, out var known) && known.Stamp == stamp)
        {
            Apply(request, known.Name, known.Icon, final: true, fromDisk: false);
            return;
        }
        BitmapSource? shown = null;
        if (!request.Fresh && _disk?.TryGet(key) is var (cachedIcon, entry))
        {
            shown = cachedIcon;
            var stale = IconCache.NeedsFreshLoad(entry, stamp);
            Apply(request, entry.Name, cachedIcon, final: !stale, fromDisk: true);
            if (!stale)
            {
                _session[key] = (cachedIcon, entry.Name, stamp);
                return;
            }
        }
        var kind = ItemKinds.Of(request.Target);
        // A share or mapped network drive is asked first, at most 2 s (M19 R7, review I2): a dead one would hold this
        // worker in the shell for minutes.
        var reachable = kind != ItemKind.Path || !TargetProbe.MayHang(request.Target)
                        || TargetProbe.Check(request.Target).State == TargetState.Ok;
        // M37: the name is kept with the icon, so it is asked even for an item with its own (another item may want it).
        var label = kind == ItemKind.Website ? ItemKinds.WebsiteName(request.Target)
            : reachable ? ShellItems.TryGetDisplayName(request.Target) : null;
        var icon = OwnIcon(request) ?? (reachable ? TargetIcon(request, kind) : null) ?? GenericIcon(request, kind);
        _session[key] = (icon, label, stamp);
        if (icon is not null && reachable) _disk?.Put(key, icon, label, stamp); // an unreachable target's generic icon is not kept
        Apply(request, label, shown is not null && icon is not null && SamePixels(shown, icon) ? null : icon, final: true, fromDisk: false);
    }

    private void Apply(LoadRequest request, string? label, BitmapSource? icon, bool final, bool fromDisk)
    {
        _uiDispatcher.BeginInvoke(() =>
        {
            if (fromDisk) FromCache++;
            if (request.Number == request.View.IconRequest) // a newer request (size, target, icon) is on its way otherwise
            {
                if (label is not null && request.WantsName && request.View.OwnName is null) request.View.Label = label;
                if (icon is not null) request.View.Icon = icon;
            }
            if (final) Finish(request);
        });
    }

    /// <summary>One request finished (UI thread): when none waits any more, <see cref="Settled"/> and the cache index is saved.</summary>
    private void Finish(LoadRequest request)
    {
        if (request.Number == request.View.IconRequest) request.View.IconPending = false;
        if (--_pending != 0) return;
        Settled?.Invoke(_requested, FromCache);
        if (_disk is { } disk) ThreadPool.QueueUserWorkItem(_ => disk.SaveIfChanged());
    }

    /// <summary>
    /// What the icon comes from, as a stamp (M37): the item's own icon file or picture, else the target file or folder; null
    /// for Start apps, shell items, websites, missing files and network paths (no stamp is read where the share may hang).
    /// </summary>
    private static IconStamp? StampOf(LoadRequest request)
    {
        var file = request.OwnIcon is { File: { Length: > 0 } iconFile } ? iconFile
            : request.OwnIcon is { Image: { Length: > 0 } image } ? Path.Combine(AppPaths.IconsDirectory, Path.GetFileName(image))
            : ItemKinds.Of(request.Target) == ItemKind.Path ? request.Target : null;
        if (file is null || TargetProbe.MayHang(file)) return null;
        try
        {
            var info = new FileInfo(file);
            if (info.Exists) return new IconStamp(info.LastWriteTimeUtc.Ticks, info.Length);
            var folder = new DirectoryInfo(file);
            return folder.Exists ? new IconStamp(folder.LastWriteTimeUtc.Ticks, 0) : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The same picture, give or take the rounding a PNG round trip brings to soft edges (M37).</summary>
    private static bool SamePixels(BitmapSource first, BitmapSource second)
    {
        if (first.PixelWidth != second.PixelWidth || first.PixelHeight != second.PixelHeight) return false;
        static byte[] Pixels(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(bytes, converted.PixelWidth * 4, 0);
            return bytes;
        }
        var (a, b) = (Pixels(first), Pixels(second));
        for (var index = 0; index < a.Length; index++)
        {
            if (Math.Abs(a[index] - b[index]) > 3) return false;
        }
        return true;
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

    /// <summary>
    /// Website icons found online (M34, ADR-055): host → icon file, kept by the host (set on the UI thread, read here). A website
    /// without one asks the host once per load (<see cref="SiteIconWanted"/>, on the UI thread) and shows its letter badge.
    /// </summary>
    public volatile IReadOnlyDictionary<string, string> SiteIconFiles = new Dictionary<string, string>();

    public event Action<string>? SiteIconWanted;

    private BitmapSource? TargetIcon(LoadRequest request, ItemKind kind)
    {
        if (kind == ItemKind.Website) return SiteIcon(request);
        // M34: a shortcut that starts a Store app through Explorer (Minecraft Launcher's) shows the app's icon, not Explorer's.
        if (ShellLinks.AppsFolderOf(request.Target) is { } app && Frozen(ShellItems.TryGetImage(app, request.SizePx)) is { } appIcon) return appIcon;
        return Frozen(ShellItems.TryGetImage(request.Target, request.SizePx));
    }

    /// <summary>The site's own icon when NeoFences found one, else its letter badge (never the browser's icon, M34).</summary>
    private BitmapSource SiteIcon(LoadRequest request)
    {
        var host = Uri.TryCreate(request.Target, UriKind.Absolute, out var url) ? url.Host : "";
        if (host.Length > 0 && SiteIconFiles.TryGetValue(host, out var file))
        {
            try
            {
                var decoder = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                // An .ico holds several sizes: the largest frame, scaled to the item.
                var frame = decoder.Frames.OrderByDescending(candidate => candidate.PixelWidth).First();
                var scale = (double)request.SizePx / Math.Max(frame.PixelWidth, frame.PixelHeight);
                BitmapSource sized = Math.Abs(scale - 1) < 0.01 ? frame : new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                sized.Freeze();
                return sized;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                Log.Warning(failure, "website icon {File} could not be read; its letter shows", file);
            }
        }
        else if (host.Length > 0) _uiDispatcher.BeginInvoke(() => SiteIconWanted?.Invoke(request.Target));
        return Badge(request.Target, request.SizePx);
    }

    /// <summary>The letter badge (M34): the site's first letter in white on its colour, a rounded square like an app icon.</summary>
    private static BitmapSource Badge(string url, int sizePx)
    {
        var (letter, argb) = SiteIcons.Badge(url);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var fill = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            fill.Freeze();
            var corner = sizePx * 0.22;
            drawing.DrawRoundedRectangle(fill, null, new System.Windows.Rect(0, 0, sizePx, sizePx), corner, corner);
            var text = new FormattedText(letter.ToString(), System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), System.Windows.FontStyles.Normal, System.Windows.FontWeights.SemiBold,
                    System.Windows.FontStretches.Normal), sizePx * 0.52, Brushes.White, 1.0);
            drawing.DrawText(text, new System.Windows.Point((sizePx - text.Width) / 2, (sizePx - text.Height) / 2));
        }
        var bitmap = new RenderTargetBitmap(sizePx, sizePx, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

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
        return Frozen(ShellItems.TryGetGenericImage(request.Target, looksLikeFolder, request.SizePx)); // M34: at its real size, not 32 px stretched
    }

    private static BitmapSource? Frozen(ShellImage? image)
    {
        if (image is null) return null;
        var icon = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Width * 4);
        icon.Freeze(); // frozen: usable from the UI thread
        return icon;
    }

    public void Dispose()
    {
        _urgent.CompleteAdding();
        _later.CompleteAdding();
        _disk?.SaveIfChanged(); // M37: what this run learned
    }
}
