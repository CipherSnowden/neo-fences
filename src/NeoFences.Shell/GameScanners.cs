using System.Xml.Linq;
using Windows.Win32;
using Microsoft.Win32;
using NeoFences.Core.Library;
using NeoFences.Core.Model;

namespace NeoFences.Shell;

/// <summary>
/// Finds installed games for the Game Library (M12, spec §2): Steam, Epic, GOG, Ubisoft Connect, EA app, Battle.net,
/// Xbox / Microsoft Store, the user's game folders and game shortcuts on the Desktop. Read-only: nothing is changed or
/// started. Each scan is isolated — a failure makes only that scan "unreadable" (its games are kept). Call on an STA
/// thread (shortcuts are read through the shell).
/// </summary>
public static class GameScanners
{
    private static readonly EnumerationOptions ProgramSearch = new()
    {
        RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <param name="previous">The last scan's games: a program found then is reused while it is still there, instead of searching
    /// every game folder again (M13b: 300+ games).</param>
    public static IReadOnlyList<SourceScan> ScanAll(LibrarySettings settings, Action<string, Exception> logFailure, IReadOnlyList<GameEntry>? previous = null)
    {
        var remembered = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in previous ?? []) remembered.TryAdd(game.Id, game);
        var scans = new List<SourceScan>();
        void Run(string scanKey, Func<IEnumerable<SourceScan>> scan)
        {
            try
            {
                scans.AddRange(scan());
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(scanKey, failure);
                scans.Add(new SourceScan(scanKey, false, []));
            }
        }
        var sources = settings.Sources;
        if (sources.Steam) Run("steam", () => ScanSteam(remembered));
        if (sources.Epic) Run("epic", () => [ScanEpic(logFailure)]);
        if (sources.Gog) Run("gog", () => [ScanGog()]);
        if (sources.Ubisoft) Run("ubisoft", () => [ScanUbisoft(remembered)]);
        if (sources.Ea) Run("ea", () => [ScanUninstallEntries("ea", GameSource.Ea, "Electronic Arts", ["EA app", "Origin", "EA Desktop"], remembered)]);
        if (sources.BattleNet) Run("battlenet", () => [ScanUninstallEntries("battlenet", GameSource.BattleNet, "Blizzard Entertainment", ["Battle.net"], remembered)]);
        if (sources.Xbox) Run("xbox", () => [ScanXbox(logFailure)]);
        if (sources.Folders)
        {
            foreach (var folder in settings.Folders) Run(FolderKey(folder), () => [ScanGameFolder(folder, remembered)]);
        }
        if (sources.DesktopShortcuts) Run("desktop", () => [ScanDesktopShortcuts(settings.Folders, logFailure)]);
        return scans;
    }

    /// <summary>Folders whose changes mean "installed or removed a game": Steam's library folders, Epic's manifests, game folders.</summary>
    public static IReadOnlyList<string> WatchFolders(LibrarySettings settings)
    {
        var folders = new List<string>();
        try
        {
            if (settings.Sources.Steam && SteamRoot() is { } steam)
                folders.AddRange(SteamLibraries(steam).Select(library => Path.Combine(library, "steamapps")).Where(Directory.Exists));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or FormatException)
        {
            // no watching for a broken Steam install; scans still say why
        }
        if (settings.Sources.Epic && Directory.Exists(EpicManifests)) folders.Add(EpicManifests);
        if (settings.Sources.Folders) folders.AddRange(settings.Folders.Where(Directory.Exists));
        return folders;
    }

    /// <summary>A file an earlier scan found for this game (its program or icon), while it is still there (M13b).</summary>
    private static string? StillThere(IReadOnlyDictionary<string, GameEntry> remembered, string id, Func<GameEntry, string?> pick) =>
        remembered.TryGetValue(id, out var game) && pick(game) is { Length: > 0 } path && File.Exists(path) ? path : null;

    public static string FolderKey(string folder) => "folder:" + folder.Trim().TrimEnd('\\', '/').ToLowerInvariant();

    // --- Steam -------------------------------------------------------------------------------------------------------

    private static IEnumerable<SourceScan> ScanSteam(IReadOnlyDictionary<string, GameEntry> remembered)
    {
        var steamPath = SteamPath();
        if (steamPath is null) return [new SourceScan("steam", true, [])]; // Steam not installed
        if (!Directory.Exists(steamPath)) return [new SourceScan("steam", false, [])]; // its drive is not there (yet): keep its games (final review I1)
        var steam = Path.GetFullPath(steamPath);
        var scans = new List<SourceScan>();
        foreach (var library in SteamLibraries(steam))
        {
            var key = "steam:" + library.TrimEnd('\\').ToLowerInvariant();
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps))
            {
                scans.Add(new SourceScan(key, false, [])); // a library on a drive that is not there: keep its games
                continue;
            }
            try
            {
                var games = new List<GameEntry>();
                foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    if (SteamFiles.ParseManifest(File.ReadAllText(manifest)) is not { } app) continue;
                    var installFolder = Path.Combine(steamapps, "common", app.InstallDir);
                    games.Add(new GameEntry($"steam:{app.AppId}", app.Name, GameSource.Steam, key, new GameLaunch(SteamFiles.LaunchUri(app.AppId)),
                        installFolder, SteamPoster(steam, app.AppId), StillThere(remembered, $"steam:{app.AppId}", game => game.IconPath) ?? ProgramIn(installFolder)));
                }
                scans.Add(new SourceScan(key, true, games));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // A manifest Steam is rewriting right now (the watcher fires then): this library keeps its games this time.
                scans.Add(new SourceScan(key, false, []));
            }
        }
        return scans;
    }

    /// <summary>Steam's folder as the registry names it (it may be on a drive that is not there), or null when Steam is not installed.</summary>
    private static string? SteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") is string { Length: > 0 } path ? path : null;
    }

    private static string? SteamRoot() => SteamPath() is { } path && Directory.Exists(path) ? Path.GetFullPath(path) : null;

    /// <summary>Wallpaper Engine is a Steam app (M14): its folder is found the way games are.</summary>
    internal static string? SteamRootForWallpaper() => SteamRoot();

    internal static IReadOnlyList<string> SteamLibrariesForWallpaper(string steam) => SteamLibraries(steam);

    /// <summary>
    /// An install folder that is gone while its drive is there: an uninstalled game left in a launcher's list (skip it).
    /// A drive that is not there: unknown (the caller keeps the source's games). Final review I3.
    /// </summary>
    private static bool? InstalledAt(string folder)
    {
        if (Directory.Exists(folder)) return true;
        return Path.GetPathRoot(folder) is { Length: > 0 } root && Directory.Exists(root) ? false : null;
    }

    private static IReadOnlyList<string> SteamLibraries(string steam)
    {
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        var libraries = File.Exists(vdf) ? SteamFiles.LibraryFolders(File.ReadAllText(vdf)).ToList() : [];
        if (!libraries.Any(library => string.Equals(library.TrimEnd('\\'), steam.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) libraries.Insert(0, steam);
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Steam's cached 2:3 cover: <c>librarycache\&lt;appid&gt;\library_600x900.jpg</c> or, newer, <c>…\&lt;hash&gt;\library_capsule.jpg</c>.</summary>
    private static string? SteamPoster(string steam, string appId)
    {
        var cache = Path.Combine(steam, "appcache", "librarycache");
        var folder = Path.Combine(cache, appId);
        string[] direct = [Path.Combine(folder, "library_600x900.jpg"), Path.Combine(cache, $"{appId}_library_600x900.jpg")];
        if (direct.FirstOrDefault(File.Exists) is { } poster) return poster;
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "library_capsule.jpg", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1 }).FirstOrDefault()
            : null;
    }

    // --- Epic --------------------------------------------------------------------------------------------------------

    private static string EpicManifests =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    private static SourceScan ScanEpic(Action<string, Exception> logFailure)
    {
        if (!Directory.Exists(EpicManifests)) return new SourceScan("epic", true, []);
        var games = new List<GameEntry>();
        var readable = true;
        foreach (var manifest in Directory.EnumerateFiles(EpicManifests, "*.item"))
        {
            try
            {
                if (EpicManifest.Parse(File.ReadAllText(manifest)) is not { } game) continue;
                var installed = InstalledAt(game.InstallLocation);
                readable &= installed is not null;
                if (installed != true) continue;
                var program = game.LaunchExecutable is { } executable ? Path.Combine(game.InstallLocation, executable) : ProgramIn(game.InstallLocation);
                games.Add(new GameEntry($"epic:{game.AppName}", game.DisplayName, GameSource.Epic, "epic", new GameLaunch(game.LaunchUri),
                    game.InstallLocation, Poster: null, IconPath: program is not null && File.Exists(program) ? program : null));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // A manifest Epic is rewriting (an update) or a folder going away mid-scan: Epic keeps its games this time, like
                // a Steam library (M13a review M2) — no tile flickers away and back.
                logFailure(manifest, failure);
                readable = false;
            }
        }
        return new SourceScan("epic", readable, games);
    }

    // --- GOG ---------------------------------------------------------------------------------------------------------

    private static SourceScan ScanGog()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var games = machine.OpenSubKey(@"SOFTWARE\GOG.com\Games");
        if (games is null) return new SourceScan("gog", true, []);
        using var galaxyPaths = machine.OpenSubKey(@"SOFTWARE\GOG.com\GalaxyClient\paths");
        var galaxy = galaxyPaths?.GetValue("client") is string clientFolder ? Path.Combine(clientFolder, "GalaxyClient.exe") : null;
        if (galaxy is not null && !File.Exists(galaxy)) galaxy = null;
        var found = new List<GameEntry>();
        var readable = true;
        foreach (var id in games.GetSubKeyNames())
        {
            using var game = games.OpenSubKey(id);
            if (game?.GetValue("gameName") is not string name || game.GetValue("path") is not string folder || game.GetValue("exe") is not string exe) continue;
            var installed = InstalledAt(folder);
            readable &= installed is not null;
            if (installed != true) continue;
            var launch = galaxy is not null
                ? new GameLaunch(galaxy, $"/command=runGame /gameId={id} /path=\"{folder}\"", Path.GetDirectoryName(galaxy))
                : new GameLaunch(exe, game.GetValue("launchParam") as string is { Length: > 0 } parameters ? parameters : null,
                    game.GetValue("workingDir") as string is { Length: > 0 } working ? working : Path.GetDirectoryName(exe));
            found.Add(new GameEntry($"gog:{id}", name, GameSource.Gog, "gog", launch, folder, Poster: null, IconPath: File.Exists(exe) ? exe : null));
        }
        return new SourceScan("gog", readable, found);
    }

    // --- Ubisoft Connect ---------------------------------------------------------------------------------------------

    private static SourceScan ScanUbisoft(IReadOnlyDictionary<string, GameEntry> remembered)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var installs = machine.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
        if (installs is null) return new SourceScan("ubisoft", true, []);
        var found = new List<GameEntry>();
        foreach (var id in installs.GetSubKeyNames())
        {
            using var install = installs.OpenSubKey(id);
            if (install?.GetValue("InstallDir") is not string raw) continue;
            var folder = Path.GetFullPath(raw.Replace('/', '\\')).TrimEnd('\\');
            if (!Directory.Exists(folder)) continue;
            found.Add(new GameEntry($"ubisoft:{id}", Path.GetFileName(folder), GameSource.Ubisoft, "ubisoft", new GameLaunch($"uplay://launch/{id}/0"),
                folder, Poster: null, IconPath: StillThere(remembered, $"ubisoft:{id}", game => game.IconPath) ?? ProgramIn(folder)));
        }
        return new SourceScan("ubisoft", true, found);
    }

    // --- EA app, Battle.net (their games' uninstall entries) ---------------------------------------------------------

    private static SourceScan ScanUninstallEntries(string scanKey, GameSource source, string publisher, string[] launcherNames, IReadOnlyDictionary<string, GameEntry> remembered)
    {
        var found = new List<GameEntry>();
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                if (entry?.GetValue("Publisher") is not string entryPublisher || !entryPublisher.Contains(publisher, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.GetValue("DisplayName") is not string displayName || launcherNames.Any(launcher => displayName.StartsWith(launcher, StringComparison.OrdinalIgnoreCase))) continue;
                var id = $"{scanKey}:{name.ToLowerInvariant()}";
                if (entry.GetValue("InstallLocation") is not string folder || !Directory.Exists(folder)
                    || (StillThere(remembered, id, game => game.Launch.Target) ?? ProgramIn(folder)) is not { } program) continue;
                found.Add(new GameEntry(id, displayName, source, scanKey, new GameLaunch(program, WorkingFolder: Path.GetDirectoryName(program)),
                    folder.TrimEnd('\\'), Poster: null, IconPath: program));
            }
        }
        return new SourceScan(scanKey, true, found);
    }

    // --- Xbox / Microsoft Store --------------------------------------------------------------------------------------

    private static SourceScan ScanXbox(Action<string, Exception> logFailure)
    {
        using var packages = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
        if (packages is null) return new SourceScan("xbox", true, []);
        var found = new List<GameEntry>();
        foreach (var fullName in packages.GetSubKeyNames())
        {
            try
            {
                if (XboxGame(packages, fullName) is { } game) found.Add(game);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(fullName, failure); // one broken package (a damaged manifest): that package only (M13a)
            }
        }
        return new SourceScan("xbox", true, found);
    }

    private static GameEntry? XboxGame(RegistryKey packages, string fullName)
    {
            using var package = packages.OpenSubKey(fullName);
            if (package?.GetValue("PackageRootFolder") is not string folder) return null;
            var gameConfig = Path.Combine(folder, "MicrosoftGame.config");
            if (!File.Exists(gameConfig)) return null; // only game packages have one
            var visuals = XDocument.Load(gameConfig).Descendants().FirstOrDefault(element => element.Name.LocalName == "ShellVisuals");
            var appId = XDocument.Load(Path.Combine(folder, "AppxManifest.xml")).Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Application")?.Attribute("Id")?.Value;
            if (appId is null) return null;
            var name = DisplayName(visuals?.Attribute("DefaultDisplayName")?.Value, fullName)
                       ?? DisplayName(package.GetValue("DisplayName") as string, fullName)
                       ?? fullName.Split('_')[0];
            var parts = fullName.Split('_');
            var familyName = $"{parts[0]}_{parts[^1]}";
            var logo = (visuals?.Attribute("Square480x480Logo") ?? visuals?.Attribute("Square150x150Logo") ?? visuals?.Attribute("StoreLogo"))?.Value;
            return new GameEntry($"xbox:{familyName.ToLowerInvariant()}", name, GameSource.Xbox, "xbox",
                new GameLaunch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"shell:AppsFolder\\{familyName}!{appId}"),
                folder, Poster: null, IconPath: logo is null ? null : ScaledAsset(folder, logo));
    }

    /// <summary>
    /// A package's name as people see it: plain text as is; "ms-resource:…" and "@{…}" looked up in the package's own
    /// resources (Game Pass titles carry only those; M13b), so the library never shows "Microsoft.624F8B84B80".
    /// </summary>
    private static unsafe string? DisplayName(string? value, string packageFullName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string indirect;
        if (value.StartsWith('@')) indirect = value;
        else if (value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            var resource = value["ms-resource:".Length..];
            var packageName = packageFullName.Split('_')[0];
            var uri = resource.StartsWith("//", StringComparison.Ordinal) ? "ms-resource:" + resource
                : resource.StartsWith('/') ? $"ms-resource://{packageName}{resource}"
                : resource.Contains('/') ? $"ms-resource://{packageName}/{resource}"
                : $"ms-resource://{packageName}/Resources/{resource}";
            indirect = $"@{{{packageFullName}?{uri}}}";
        }
        else return value;
        var buffer = new char[512];
        fixed (char* source = indirect)
        fixed (char* output = buffer)
        {
            if (PInvoke.SHLoadIndirectString(source, output, (uint)buffer.Length).Failed) return null;
            var text = new string(output);
            return text.Length > 0 && !text.StartsWith("ms-resource", StringComparison.OrdinalIgnoreCase) ? text : null;
        }
    }

    /// <summary>A package image: the file itself, or its largest ".scale-NNN" variant.</summary>
    private static string? ScaledAsset(string packageFolder, string relative)
    {
        var path = Path.Combine(packageFolder, relative.Replace('/', '\\'));
        if (File.Exists(path)) return path;
        var folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder)) return null;
        return Directory.EnumerateFiles(folder, $"{Path.GetFileNameWithoutExtension(path)}.scale-*{Path.GetExtension(path)}")
            .OrderByDescending(file => new FileInfo(file).Length).FirstOrDefault();
    }

    // --- My game folders ---------------------------------------------------------------------------------------------

    private static SourceScan ScanGameFolder(string folder, IReadOnlyDictionary<string, GameEntry> remembered)
    {
        var key = FolderKey(folder);
        if (!Directory.Exists(folder)) return new SourceScan(key, false, []); // a drive that is not there: keep its games
        var found = new List<GameEntry>();
        foreach (var game in Directory.EnumerateDirectories(folder))
        {
            var id = "folder:" + game.ToLowerInvariant();
            if ((StillThere(remembered, id, entry => entry.Launch.Target) ?? ProgramIn(game)) is not { } program) continue; // no program found: not listed
            found.Add(new GameEntry(id, Path.GetFileName(game), GameSource.Folder, key,
                new GameLaunch(program, WorkingFolder: Path.GetDirectoryName(program)), game, Poster: null, IconPath: program));
        }
        return new SourceScan(key, true, found);
    }

    /// <summary>The program that starts a game in this folder (<see cref="ProgramPicker"/>), or null.</summary>
    private static string? ProgramIn(string? folder)
    {
        if (folder is null || !Directory.Exists(folder)) return null;
        var programs = new DirectoryInfo(folder).EnumerateFiles("*.exe", ProgramSearch)
            .Select(file => (Path.GetRelativePath(folder, file.FullName), file.Length));
        return ProgramPicker.Pick(programs) is { } relative ? Path.Combine(folder, relative) : null;
    }

    // --- Game shortcuts on the Desktop -------------------------------------------------------------------------------

    private static SourceScan ScanDesktopShortcuts(IReadOnlyList<string> gameFolders, Action<string, Exception> logFailure)
    {
        var listing = DesktopItems.Enumerate();
        var found = new List<GameEntry>();
        foreach (var itemRef in listing.ItemRefs.Where(itemRef => Path.GetExtension(itemRef).ToLowerInvariant() is ".lnk" or ".url"))
        {
            GameLaunch? launch;
            try
            {
                launch = ShellLinks.Read(itemRef);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                logFailure(itemRef, failure);
                continue;
            }
            if (launch is null) continue;
            var gameFolder = gameFolders.Select(root => GameFolderOf(root, launch.Target)).FirstOrDefault(found => found is not null);
            var asText = launch.Arguments is null ? launch.Target : $"{launch.Target} {launch.Arguments}";
            if (gameFolder is null && GameLaunchers.LauncherOf(asText) is null) continue;
            // The target's folder only when the target is a game itself (under a game library): steam.exe, Battle.net.exe or
            // GalaxyClient.exe started with a game argument have no folder of their own game (final review I2).
            var installFolder = gameFolder ?? (launch.IsLink || GameLaunchers.LauncherOf(launch.Target) is null ? null : Path.GetDirectoryName(launch.Target));
            found.Add(new GameEntry("desktop:" + itemRef.ToLowerInvariant(), Path.GetFileNameWithoutExtension(itemRef), GameSource.DesktopShortcut, "desktop",
                launch, installFolder, Poster: null, IconPath: launch.IsLink ? null : launch.Target) { ShortcutFile = itemRef });
        }
        // A Desktop folder that could not be listed: keep the shortcuts found before.
        return new SourceScan("desktop", listing.UnavailableFolders.Count == 0, found);
    }

    /// <summary>The game sub-folder of a game folder that holds this target, or null.</summary>
    private static string? GameFolderOf(string root, string target)
    {
        var prefix = root.TrimEnd('\\') + "\\";
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var slash = target.IndexOf('\\', prefix.Length);
        return slash < 0 ? null : target[..slash];
    }
}
