using System.Diagnostics;
using System.Runtime.InteropServices;
using NeoFences.Core.Items;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>32-bit premultiplied BGRA pixels, top-down rows (ready for WPF's Pbgra32 BitmapSource).</summary>
public sealed record ShellImage(int Width, int Height, byte[] Pixels);

/// <summary>Display names, icons/thumbnails and opening of item targets (parsing names: paths, <c>::{GUID}</c>, URLs).</summary>
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

    /// <summary>A special item's ref ("::{645FF040-…}" for the Recycle Bin), or null for anything else.</summary>
    internal static unsafe string? SpecialItemRef(IShellItem item)
    {
        try
        {
            item.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, out var name);
            try { return name.ToString() is { } parsed && parsed.StartsWith("::", StringComparison.Ordinal) && !parsed.Contains('\\') ? parsed : null; }
            finally { Marshal.FreeCoTaskMem((nint)name.Value); }
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
    /// Opens like a double-click in Explorer (websites in the default browser), with the item's own arguments, "run as
    /// administrator", and the target's folder as the working folder, as a shortcut would. If it cannot, Windows shows its
    /// own message (no app for this file type) owned by <paramref name="ownerHandle"/> (M2b finding H5).
    /// </summary>
    /// <returns>False when nothing could open it (no associated app, the user declined the UAC prompt, the item is gone).</returns>
    public static bool TryOpen(string target, nint ownerHandle, string? arguments = null, bool runAsAdmin = false)
    {
        try
        {
            ProcessStartInfo startInfo;
            // Quoted (M19 review): a program's app id holds spaces and commas, where Explorer would split it.
            if (target.StartsWith("::", StringComparison.Ordinal) || target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                startInfo = new ProcessStartInfo("explorer.exe", ItemKinds.ExplorerArgument(target));
            else
            {
                startInfo = new ProcessStartInfo(target) { Arguments = arguments ?? "" };
                if (runAsAdmin) startInfo.Verb = "runas";
                if (Path.IsPathFullyQualified(target) && Path.GetDirectoryName(target) is { Length: > 0 } folder) startInfo.WorkingDirectory = folder;
            }
            startInfo.UseShellExecute = true;
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

    /// <summary>Explorer with the target selected in its folder (item menu → Open file location). Read-only: nothing changes.</summary>
    public static bool TryShowInFolder(string target)
    {
        try
        {
            PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static string? _defaultBrowser;
    private static bool _defaultBrowserRead;

    /// <summary>The program that opens http links for this user (a website item shows its icon), or null. Read once per run.</summary>
    public static string? DefaultBrowserPath()
    {
        if (_defaultBrowserRead) return _defaultBrowser;
        _defaultBrowserRead = true;
        const uint AssocfIsProtocol = 0x1000; // ASSOCF_IS_PROTOCOL: honour the user's choice (UserChoice), not HKCR\http
        var buffer = new char[1024];
        var length = (uint)buffer.Length;
        try
        {
            if (PInvoke.AssocQueryString((ASSOCF)AssocfIsProtocol, ASSOCSTR.ASSOCSTR_EXECUTABLE, "http", "open", buffer, ref length).Succeeded)
            {
                var end = Array.IndexOf(buffer, '\0');
                _defaultBrowser = new string(buffer, 0, end >= 0 ? end : buffer.Length);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            _defaultBrowser = null; // websites then show no icon
        }
        return _defaultBrowser;
    }

    /// <summary>
    /// The icon at <paramref name="index"/> in an .ico, .exe or .dll (an item's own icon, Properties → From a file…), at
    /// <paramref name="sizePx"/>. Null when the file or the icon is not there.
    /// </summary>
    public static ShellImage? TryGetFileIcon(string file, int index, int sizePx)
    {
        DestroyIconSafeHandle? large = null, small = null;
        ICONINFO info = default;
        try
        {
            if (PInvoke.SHDefExtractIcon(file, index, 0, out large, out small, (uint)sizePx).Failed || large.IsInvalid) return null;
            if (!PInvoke.GetIconInfo(large, out info)) return null;
            // ponytail: icons without an alpha channel (old 24-bit ones) come out with an opaque background; apply the mask if users pick those.
            return ReadPixels(info.hbmColor, premultiply: true);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (!info.hbmColor.IsNull) PInvoke.DeleteObject(info.hbmColor);
            if (!info.hbmMask.IsNull) PInvoke.DeleteObject(info.hbmMask);
            large?.Dispose();
            small?.Dispose();
        }
    }

    /// <summary>
    /// Windows' icon for this kind of item, by its name only (M19 R8): a folder icon, or the icon of its file type ("a .txt
    /// file"). Never touches the disk, so a missing or unreachable target still gets one. Null when Windows has none.
    /// </summary>
    public static unsafe ShellImage? TryGetGenericImage(string target, bool isFolder)
    {
        var info = new SHFILEINFOW();
        ICONINFO iconInfo = default;
        try
        {
            // ponytail: the 32 px "large" icon, scaled by WPF for bigger sizes; the system image list's jumbo icons if it looks soft.
            var attributes = isFolder ? FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_DIRECTORY : FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL;
            var flags = SHGFI_FLAGS.SHGFI_ICON | SHGFI_FLAGS.SHGFI_LARGEICON | SHGFI_FLAGS.SHGFI_USEFILEATTRIBUTES;
            nuint found;
            fixed (char* name = target)
            {
                found = PInvoke.SHGetFileInfo(name, attributes, &info, (uint)sizeof(SHFILEINFOW), flags);
            }
            if (found == 0 || info.hIcon.IsNull) return null;
            if (!PInvoke.GetIconInfo(info.hIcon, &iconInfo)) return null;
            return ReadPixels(iconInfo.hbmColor, premultiply: true);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (!iconInfo.hbmColor.IsNull) PInvoke.DeleteObject(iconInfo.hbmColor);
            if (!iconInfo.hbmMask.IsNull) PInvoke.DeleteObject(iconInfo.hbmMask);
            if (!info.hIcon.IsNull) PInvoke.DestroyIcon(info.hIcon);
        }
    }

    private static IShellItem Create(string itemRef)
    {
        PInvoke.SHCreateItemFromParsingName(itemRef, null, out IShellItem item).ThrowOnFailure();
        return item;
    }

    /// <param name="premultiply">Icon bitmaps carry straight alpha; WPF's Pbgra32 wants it premultiplied.</param>
    private static unsafe ShellImage? ReadPixels(HBITMAP bitmap, bool premultiply = false)
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
        else if (premultiply)
        {
            for (var pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                var alpha = pixels[pixel + 3];
                for (var channel = 0; channel < 3; channel++) pixels[pixel + channel] = (byte)(pixels[pixel + channel] * alpha / 255);
            }
        }
        return new ShellImage(width, height, pixels);
    }
}
