using System.Runtime.InteropServices;
using NeoFences.Core.Model;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>
/// What a rule needs to know about desktop items (M11): name, type, size, date and a shortcut's target. Call on the shell
/// worker (an STA thread; a shortcut to an offline share can be slow). An item that cannot be read keeps only its name and
/// extension, so only name/type rules can match it; special items (the Recycle Bin, ::{…}) are left out.
/// </summary>
public static class ItemFactsReader
{
    public static IReadOnlyList<ItemFacts> Read(IReadOnlyList<string> itemRefs, Action<string, Exception> logFailure)
    {
        var facts = new List<ItemFacts>(itemRefs.Count);
        foreach (var itemRef in itemRefs.Where(itemRef => !itemRef.StartsWith("::", StringComparison.Ordinal)))
        {
            var name = Path.GetFileName(itemRef);
            var extension = Path.GetExtension(itemRef);
            try
            {
                if (Directory.Exists(itemRef))
                {
                    facts.Add(new ItemFacts(itemRef, name, "", IsFolder: true, SizeBytes: null, Directory.GetLastWriteTime(itemRef), ShortcutTarget: null));
                    continue;
                }
                var file = new FileInfo(itemRef);
                if (!file.Exists) continue; // gone meanwhile: nothing to file
                facts.Add(new ItemFacts(itemRef, name, extension, IsFolder: false, file.Length, file.LastWriteTime, ShortcutTarget(itemRef, extension)));
            }
            catch (Exception failure) when (failure is not OutOfMemoryException) // a bad shortcut must never stop the rest (hard rule 7)
            {
                logFailure(itemRef, failure);
                facts.Add(new ItemFacts(itemRef, name, extension, IsFolder: false, SizeBytes: null, Modified: null, ShortcutTarget: null));
            }
        }
        return facts;
    }

    /// <summary>A .url's URL= line, or a .lnk's target path and arguments; null for other files.</summary>
    private static string? ShortcutTarget(string path, string extension)
    {
        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            return ShellLinks.UrlOf(path); // bounded read (M13c)
        }
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? LinkTarget(path) : null;
    }

    private static unsafe string? LinkTarget(string path)
    {
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(ShellLink).GUID)!)!;
            fixed (char* file = path) ((IPersistFile)link).Load(file, STGM.STGM_READ);
            var buffer = new char[1024];
            fixed (char* text = buffer)
            {
                link.GetPath(text, buffer.Length, null, 0x4 /* SLGP_RAWPATH: no resolving, never a network wait */);
                var target = Environment.ExpandEnvironmentVariables(new string(text)); // raw paths keep %ProgramFiles% (M13b)
                link.GetArguments(text, buffer.Length);
                var arguments = new string(text);
                return arguments.Length == 0 ? target : $"{target} {arguments}";
            }
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }
}
