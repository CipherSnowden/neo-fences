using Microsoft.Win32;
using NeoFences.Core.Model;

namespace NeoFences.Shell;

/// <param name="UnavailableFolders">Desktop folders that could not be listed (missing or unreadable): their items are unknown, not gone.</param>
public sealed record DesktopListing(IReadOnlyList<string> ItemRefs, IReadOnlyList<string> UnavailableFolders);

/// <summary>
/// What Explorer shows on the desktop, as item refs (ADR-002): the visible files and folders of the user's Desktop
/// (OneDrive-redirected or not) and the Public Desktop, plus the special icons the user enabled in "Desktop icon
/// settings" (Recycle Bin, This PC, ...) as "::{CLSID}".
/// </summary>
public static class DesktopItems
{
    private const string HideDesktopIconsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    /// <summary>Special desktop icons and whether Windows shows each one when the user never changed the setting.</summary>
    private static readonly (string Clsid, bool ShownByDefault)[] SpecialIcons =
    [
        ("{645FF040-5081-101B-9F08-00AA002F954E}", true),  // Recycle Bin
        ("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false), // This PC
        ("{59031a47-3f72-44a7-89c5-5595fe6b30ee}", false), // User's files
        ("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", false), // Network
        ("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", false), // Control Panel
    ];

    // DoNotVerify: a missing (offline, unmounted) folder still has a path, so its items are recognised as unknown, not gone.
    public static string UserDesktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);

    public static string PublicDesktop => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>The special icons the user shows in "Desktop icon settings", as "::{CLSID}" (one registry read).</summary>
    public static IReadOnlyList<string> SpecialIconRefs()
    {
        var itemRefs = new List<string>();
        using var hideKey = Registry.CurrentUser.OpenSubKey(HideDesktopIconsKey);
        foreach (var (clsid, shownByDefault) in SpecialIcons)
        {
            var hidden = hideKey?.GetValue(clsid) is int setting ? setting != 0 : !shownByDefault;
            if (!hidden) itemRefs.Add("::" + clsid);
        }
        return itemRefs;
    }

    /// <summary>Special icons first (as Explorer arranges them), then files and folders by name.</summary>
    public static DesktopListing Enumerate()
    {
        var itemRefs = SpecialIconRefs().ToList();

        var files = new List<string>();
        var unavailable = new List<string>();
        foreach (var directory in new[] { UserDesktop, PublicDesktop }.Where(path => path.Length > 0).Distinct(ItemRef.Comparer))
        {
            try
            {
                // ToList first: a folder that fails halfway counts as unavailable, with none of its entries listed.
                files.AddRange(new DirectoryInfo(directory).EnumerateFileSystemInfos()
                    .Where(IsVisibleOnDesktop)
                    .Select(entry => entry.FullName)
                    .ToList());
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                unavailable.Add(directory); // missing (DirectoryNotFoundException is an IOException) or unreadable
            }
        }
        itemRefs.AddRange(files.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase));
        return new DesktopListing(itemRefs, unavailable);
    }

    /// <summary>Hidden and system entries (desktop.ini, Office ~$ lock files) are not shown by Explorer either.</summary>
    public static bool IsVisibleOnDesktop(FileSystemInfo entry) =>
        (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
}
