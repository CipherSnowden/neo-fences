namespace NeoFences.Core;

/// <summary>
/// Windows paths as Core stores them — items, folders, game shortcuts, picture names — read by Windows' rules on every OS
/// (M39, ADR-061): System.IO.Path follows the OS it runs on, so on Linux a backslash is not a separator and Core's tests
/// (and a later port) would read Windows paths wrongly. Pure text; NeoFences' own files are still opened with
/// System.IO.Path. Both separators count, so a name with "..\" or "../" never leaves its folder.
/// </summary>
public static class WindowsPath
{
    private static readonly char[] Separators = ['\\', '/'];
    private static readonly char[] Reserved = ['<', '>', ':', '"', '|', '?', '*'];

    /// <summary>What follows the last separator ("" for a path that ends in one).</summary>
    public static string FileName(string path) => path[(path.LastIndexOfAny(Separators) + 1)..];

    /// <summary>The file name's last ".xyz", with its dot ("" without one).</summary>
    public static string Extension(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..];
    }

    public static string FileNameWithoutExtension(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[..dot];
    }

    /// <summary>The parent folder; null for a drive's root, a share's root, or a bare name.</summary>
    public static string? DirectoryName(string path)
    {
        var trimmed = TrimEndingSeparator(path);
        var cut = trimmed.LastIndexOfAny(Separators);
        if (cut < 0 || trimmed.Length <= RootLength(trimmed)) return null;
        var parent = trimmed[..cut];
        if (IsUnc(trimmed) && parent.Length <= 2) return null; // \\server: not a folder
        return parent.Length == 2 && parent[1] == ':' ? parent + '\\' : parent; // D: → D:\
    }

    /// <summary>A folder and a name with one backslash between.</summary>
    public static string Combine(string folder, string name) =>
        folder.Length == 0 || folder[^1] is '\\' or '/' ? folder + name : folder + '\\' + name;

    /// <summary>Without its trailing separator — except a drive's root ("D:\"), which needs it.</summary>
    public static string TrimEndingSeparator(string path) =>
        path.Length > 1 && path[^1] is '\\' or '/' && !(path.Length == 3 && path[1] == ':') ? path[..^1] : path;

    /// <summary>A drive with its separator ("D:\...") or a share ("\\server\share..."); not "D:x", "\x" or "x\y".</summary>
    public static bool IsFullyQualified(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'
        || IsUnc(path) && path.Length > 2 && path[2] is not ('\\' or '/');

    /// <summary>The path below the folder (case-insensitive, as on Windows); "." for the folder itself; else the path unchanged.</summary>
    public static string RelativePath(string folder, string path)
    {
        var from = TrimEndingSeparator(folder);
        var to = TrimEndingSeparator(path);
        if (to.Equals(from, StringComparison.OrdinalIgnoreCase)) return ".";
        var prefix = from[^1] is '\\' or '/' ? from : from + '\\';
        return to.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? to[prefix.Length..] : path;
    }

    /// <summary>A name that is only a file name on Windows: no separators, no reserved characters, not "." or "..".</summary>
    public static bool IsPlainFileName(string name) =>
        name.Length > 0 && name is not ("." or "..") && name.IndexOfAny(Separators) < 0 && name.IndexOfAny(Reserved) < 0
        && name.All(character => character >= ' ');

    private static bool IsUnc(string path) => path.Length >= 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/';

    private static int RootLength(string path)
    {
        if (path.Length >= 2 && path[1] == ':') return path.Length >= 3 && path[2] is '\\' or '/' ? 3 : 2;
        if (!IsUnc(path)) return 0;
        // \\server\share: the root is both names
        var server = path.IndexOfAny(Separators, 2);
        if (server < 0) return path.Length;
        var share = path.IndexOfAny(Separators, server + 1);
        return share < 0 ? path.Length : share;
    }
}
