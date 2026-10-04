using NeoFences.Core.Library;

namespace NeoFences.Core.Tests.Library;

/// <summary>Which launcher a Desktop game shortcut starts (the Game Library, M12; moved out of Rules in M18).</summary>
public class GameLaunchersTests
{
    [Theory]
    [InlineData("steam://rungameid/1091500", GameLauncher.Steam)]
    [InlineData("com.epicgames.launcher://apps/Fortnite?action=launch", GameLauncher.Epic)]
    [InlineData("uplay://launch/635/0", GameLauncher.Ubisoft)]
    [InlineData("origin2://game/launch?offerIds=1", GameLauncher.Ea)]
    [InlineData("battlenet://WoW", GameLauncher.BattleNet)]
    [InlineData("goggalaxy://openGameView/1207658924", GameLauncher.Gog)]
    [InlineData(@"D:\SteamLibrary\steamapps\common\Hades\Hades.exe", GameLauncher.Steam)]
    [InlineData(@"C:\Program Files\Epic Games\Fortnite\FortniteLauncher.exe", GameLauncher.Epic)]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe -applaunch 570", GameLauncher.Steam)]
    [InlineData(@"C:\Program Files (x86)\Battle.net\Battle.net.exe --exec=""launch WoW""", GameLauncher.BattleNet)]
    [InlineData(@"C:\Program Files (x86)\GOG Galaxy\Games\Witcher 3\witcher3.exe", GameLauncher.Gog)]
    public void LauncherOf_KnowsTheLaunchers(string target, GameLauncher expected) => Assert.Equal(expected, GameLaunchers.LauncherOf(target));

    [Theory]
    [InlineData(null)]
    [InlineData(@"C:\Windows\notepad.exe")]
    // The launchers themselves are not games (probe on the user's desktop, 2026-10-03)
    [InlineData(@"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe")]
    [InlineData(@"C:\Program Files (x86)\Steam\Steam.exe")]
    [InlineData(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\UbisoftConnect.exe")]
    public void LauncherOf_NotAGame(string? target) => Assert.Null(GameLaunchers.LauncherOf(target));
}
