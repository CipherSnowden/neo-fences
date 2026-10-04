namespace NeoFences.Core.Model;

/// <summary>Checks a name typed in a fence's rename box before Windows renames anything (M3a review I2).</summary>
public static class FileNames
{
    // Windows' forbidden file-name characters, spelled out: Core must not depend on the platform it runs on.
    private static readonly char[] Forbidden = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// False for anything that is not a plain name in the same folder: path separators (IFileOperation.RenameItem would
    /// move the file elsewhere, out of the Desktop), Windows' forbidden or control characters, ".", "..", or blank.
    /// </summary>
    public static bool IsValidNewName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name is not ("." or "..")
        && name.IndexOfAny(Forbidden) < 0
        && !name.Any(char.IsControl);
}
