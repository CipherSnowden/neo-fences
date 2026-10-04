namespace NeoFences.Core.Library;

public enum GameLauncher { Steam, Epic, Ubisoft, Ea, BattleNet, Gog }

/// <summary>Which game launcher a shortcut starts (the Game Library's Desktop shortcuts, M12; from Rules before M18).</summary>
public static class GameLaunchers
{
    private static readonly (string Prefix, GameLauncher Launcher)[] LauncherUrls =
    [
        ("steam://", GameLauncher.Steam), ("com.epicgames.launcher://", GameLauncher.Epic), ("uplay://", GameLauncher.Ubisoft),
        ("origin://", GameLauncher.Ea), ("origin2://", GameLauncher.Ea), ("ea://", GameLauncher.Ea), ("link2ea://", GameLauncher.Ea),
        ("battlenet://", GameLauncher.BattleNet), ("goggalaxy://", GameLauncher.Gog),
    ];

    private static readonly (string Part, GameLauncher Launcher)[] LibraryFolders =
    [
        (@"\steamapps\common\", GameLauncher.Steam), (@"\Epic Games\", GameLauncher.Epic),
        (@"\Ubisoft Game Launcher\games\", GameLauncher.Ubisoft), (@"\EA Games\", GameLauncher.Ea),
        (@"\GOG Galaxy\Games\", GameLauncher.Gog), (@"\GOG Games\", GameLauncher.Gog),
    ];

    /// <summary>The game launcher a shortcut belongs to (its URL scheme, library folder or launcher arguments), or null.</summary>
    public static GameLauncher? LauncherOf(string? shortcutTarget)
    {
        if (string.IsNullOrWhiteSpace(shortcutTarget)) return null;
        var target = shortcutTarget.Trim();
        foreach (var (prefix, launcher) in LauncherUrls)
        {
            // The link itself, or the link handed to the launcher as an argument (M13b): "steam.exe steam://rungameid/570".
            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || target.Contains(" " + prefix, StringComparison.OrdinalIgnoreCase) || target.Contains("\"" + prefix, StringComparison.OrdinalIgnoreCase)) return launcher;
        }
        // The Epic launcher itself lives under "\Epic Games\Launcher\": not a game.
        foreach (var (part, launcher) in LibraryFolders)
        {
            if (target.Contains(part, StringComparison.OrdinalIgnoreCase) && !target.Contains(@"\Epic Games\Launcher\", StringComparison.OrdinalIgnoreCase)) return launcher;
        }
        if (target.Contains("steam.exe", StringComparison.OrdinalIgnoreCase) && target.Contains("-applaunch", StringComparison.OrdinalIgnoreCase)) return GameLauncher.Steam;
        if (target.Contains("battle.net.exe", StringComparison.OrdinalIgnoreCase) && target.Contains("--exec", StringComparison.OrdinalIgnoreCase)) return GameLauncher.BattleNet;
        return null;
    }
}
