using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>32-bit premultiplied BGRA pixels, top-down rows (ready for WPF's Pbgra32 BitmapSource).</summary>
public sealed record ShellImage(int Width, int Height, byte[] Pixels);

/// <summary>Display names, icons/thumbnails and opening of desktop items, by item ref (parsing name).</summary>
public static class ShellItems
{
    /// <summary>The name Explorer shows ("Crysis 2", not "Crysis 2.lnk"; "Recycle Bin" in the user's language).</summary>
    public static unsafe string? TryGetDisplayName(string itemRef)
    {
        IShellItem? item = null;
        try
        {
            item = Create(itemRef);
            item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, out var name);
            try { return name.ToString(); }
            finally { Marshal.FreeCoTaskMem((nint)name.Value); }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            // Released now, not by the finalizer: a fence of hundreds of items keeps no shell objects alive (M8b).
            if (item is not null) Marshal.ReleaseComObject(item);
        }
    }

    /// <summary>The item's file-system path (SIGDN_FILESYSPATH), or null for virtual items (This PC, a phone, …).</summary>
    internal static unsafe string? FileSystemPath(IShellItem item)
    {
        try
        {
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);
            try { return path.ToString(); }
            finally { Marshal.FreeCoTaskMem((nint)path.Value); }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// The thumbnail (images, videos) or icon at <paramref name="sizePx"/>. Call from an STA thread that is not the
    /// UI thread (slow for big files). Null when the shell has nothing (the item vanished, a broken handler).
    /// </summary>
    public static unsafe ShellImage? TryGetImage(string itemRef, int sizePx)
    {
        HBITMAP bitmap = default;
        IShellItem? item = null;
        try
        {
            item = Create(itemRef);
            ((IShellItemImageFactory)item).GetImage(new SIZE(sizePx, sizePx), SIIGBF.SIIGBF_RESIZETOFIT, &bitmap);
            return ReadPixels(bitmap);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (!bitmap.IsNull) PInvoke.DeleteObject(bitmap);
            if (item is not null) Marshal.ReleaseComObject(item);
        }
    }

    /// <summary>
    /// Opens with the default verb, like a double-click in Explorer. If it cannot, Windows shows its own message
    /// (broken shortcut, no app for this file type) owned by <paramref name="ownerHandle"/> (M2b finding H5).
    /// </summary>
    /// <returns>False when nothing could open it (no associated app, the user cancelled a UAC prompt, the item is gone).</returns>
    public static bool TryOpen(string itemRef, nint ownerHandle)
    {
        try
        {
            var startInfo = itemRef.StartsWith("::", StringComparison.Ordinal)
                ? new ProcessStartInfo("explorer.exe", "shell:" + itemRef) { UseShellExecute = true }
                : new ProcessStartInfo(itemRef) { UseShellExecute = true };
            startInfo.ErrorDialog = true;
            startInfo.ErrorDialogParentHandle = ownerHandle;
            // The user just clicked our (never-activated) fence: let the opened window come to the front.
            PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
            Process.Start(startInfo)?.Dispose();
            return true;
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    private static IShellItem Create(string itemRef)
    {
        PInvoke.SHCreateItemFromParsingName(itemRef, null, out IShellItem item).ThrowOnFailure();
        return item;
    }

    private static unsafe ShellImage? ReadPixels(HBITMAP bitmap)
    {
        BITMAP header;
        // Any bit depth: GetDIBits converts to 32-bit; a 24-bit (or palette) image comes back with alpha 0 and is made
        // opaque below. Such icons used to be dropped, leaving the placeholder (M2b review carry-over).
        if (PInvoke.GetObject(bitmap, sizeof(BITMAP), &header) == 0) return null;

        var width = header.bmWidth;
        var height = Math.Abs(header.bmHeight);
        var pixels = new byte[width * height * 4];
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // negative: top-down rows
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,  // BI_RGB
            },
        };
        var screenDc = PInvoke.GetDC(HWND.Null);
        try
        {
            fixed (byte* pixelBuffer = pixels)
            {
                if (PInvoke.GetDIBits(screenDc, bitmap, 0, (uint)height, pixelBuffer, &info, DIB_USAGE.DIB_RGB_COLORS) == 0) return null;
            }
        }
        finally
        {
            PInvoke.ReleaseDC(HWND.Null, screenDc);
        }
        // Some thumbnail handlers return opaque images with every alpha byte 0: show them opaque, not invisible.
        var hasAlpha = false;
        for (var alphaIndex = 3; alphaIndex < pixels.Length && !hasAlpha; alphaIndex += 4) hasAlpha = pixels[alphaIndex] != 0;
        if (!hasAlpha) for (var alphaIndex = 3; alphaIndex < pixels.Length; alphaIndex += 4) pixels[alphaIndex] = 255;
        return new ShellImage(width, height, pixels);
    }
}
