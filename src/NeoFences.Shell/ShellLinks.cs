using System.Runtime.InteropServices;
using NeoFences.Core.Library;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>
/// Reads and writes shortcut files for the Game Library (M12): <c>.lnk</c> through the shell's own link object,
/// <c>.url</c> as the small INI text Windows uses. Call on an STA thread.
/// </summary>
public static class ShellLinks
{
    private const uint RawPath = 0x4; // SLGP_RAWPATH: never resolves the link (no network wait)

    /// <summary>A shortcut's launch: a .url's URL, or a .lnk's target, arguments and working folder; null when unreadable or empty.</summary>
    public static unsafe GameLaunch? Read(string path)
    {
        if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            return UrlOf(path) is { } url ? new GameLaunch(url) : null;
        }
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(ShellLink).GUID)!)!;
            fixed (char* file = path) ((IPersistFile)link).Load(file, STGM.STGM_READ);
            var buffer = new char[1024];
            fixed (char* text = buffer)
            {
                link.GetPath(text, buffer.Length, null, RawPath);
                var target = new string(text);
                link.GetArguments(text, buffer.Length);
                var arguments = new string(text);
                link.GetWorkingDirectory(text, buffer.Length);
                var folder = new string(text);
                return target.Length == 0 ? null : new GameLaunch(Environment.ExpandEnvironmentVariables(target),
                    arguments.Length == 0 ? null : arguments, folder.Length == 0 ? null : Environment.ExpandEnvironmentVariables(folder));
            }
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>
    /// A .url file's URL= line, reading at most the first 64 KB (a huge or binary file named .url never fills memory,
    /// also without line breaks; M13c). Null when there is none.
    /// </summary>
    public static string? UrlOf(string path)
    {
        using var reader = new StreamReader(path);
        var buffer = System.Buffers.ArrayPool<char>.Shared.Rent(64 * 1024); // no 128 KB allocation per read (M16)
        try
        {
            var read = reader.ReadBlock(buffer, 0, 64 * 1024);
            var line = new string(buffer, 0, read).Split('\n').Select(text => text.TrimEnd('\r'))
                .FirstOrDefault(text => text.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
            return line is { Length: > 4 } ? line[4..].Trim() : null;
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>Writes a shortcut for a launch (a .url for links, a .lnk for programs), with an icon when given.</summary>
    public static unsafe void Write(string path, GameLaunch launch, string? iconPath)
    {
        if (launch.IsLink)
        {
            var lines = new List<string> { "[InternetShortcut]", $"URL={launch.Target}" };
            if (iconPath is not null) lines.AddRange([$"IconFile={iconPath}", "IconIndex=0"]);
            File.WriteAllLines(path, lines);
            return;
        }
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(ShellLink).GUID)!)!;
            fixed (char* target = launch.Target) link.SetPath(target);
            fixed (char* arguments = launch.Arguments ?? "") link.SetArguments(arguments);
            fixed (char* folder = launch.WorkingFolder ?? Path.GetDirectoryName(launch.Target) ?? "") link.SetWorkingDirectory(folder);
            if (iconPath is not null)
            {
                fixed (char* icon = iconPath) link.SetIconLocation(icon, 0);
            }
            fixed (char* file = path) ((IPersistFile)link).Save(file, true);
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }
}
