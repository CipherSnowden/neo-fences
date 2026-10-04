using NeoFences.Core.Items;

namespace NeoFences.Core.Tests.Items;

/// <summary>M20 (0.10.1): the deferred M19 review minors.</summary>
public class SmallFixesTests
{
    [Theory]
    [InlineData(1, "Before fixing 1 item (5 Oct 12:00)")]
    [InlineData(7, "Before fixing 7 items (5 Oct 12:00)")]
    public void UndoName_CountsItemsInPlainEnglish(int count, string expected) =>
        Assert.Equal(expected, Relocation.UndoName(count, "5 Oct 12:00"));

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Microsoft.WindowsCalculator")]
    [InlineData("308046B0AF4A39CB", "308046B0AF4A39CB")]
    [InlineData(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", "EpicGamesLauncher")]
    [InlineData(@"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\MSI Afterburner\SDK\Doc\Localization reference.pdf", "Localization reference")]
    public void AppName_IsReadable_ForStoreAppsAndPrograms(string appId, string expected) =>
        Assert.Equal(expected, ItemKinds.AppName(appId));

    [Fact]
    public void UsableGameFolders_DropDriveRootsAndSystemFolders()
    {
        var folders = DesktopSorting.UsableGameFolders([
            @"D:\GameLibrary", @"C:\Program Files (x86)\Steam\steamapps\common\Hades", @"C:\Program Files (x86)",
            @"C:\Program Files\", @"D:\", @"E:", @"C:\Windows", @"c:\programdata", @"C:\Program Files\EA Games\Battlefield 6",
        ]);
        Assert.Equal([@"D:\GameLibrary", @"C:\Program Files (x86)\Steam\steamapps\common\Hades", @"C:\Program Files\EA Games\Battlefield 6"], folders);
    }

    [Fact]
    public void ShortcutsToWindowsPlaces_AreFoldersAndFiles_StoreAppShortcutsAreApps()
    {
        DesktopGroup Group(string? linkTarget) =>
            DesktopSorting.GroupOf(new DesktopEntry(@"C:\Users\c\Desktop\x.lnk", IsFolder: false, linkTarget, LinkArguments: null), []);
        Assert.Equal(DesktopGroup.FoldersAndFiles, Group("::{26EE0668-A00A-44D7-9371-BEB064C98683}\\0\\::{BB06C0E4-D293-4F75-8A90-CB05B6477EEE}")); // Control Panel → System
        Assert.Equal(DesktopGroup.FoldersAndFiles, Group("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")); // This PC
        Assert.Equal(DesktopGroup.Apps, Group(@"shell:AppsFolder\5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App")); // a Store app's shortcut
        Assert.Equal(DesktopGroup.FoldersAndFiles, Group("file:///C:/Users/c/Documents/notes.html")); // a file:// .url
    }
}
