using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>Where Windows keeps the user's busy folders (M21): a new folder view of one of them starts newest first.</summary>
public static class KnownFolders
{
    /// <summary>Downloads and Screenshots, where Windows has them (moved ones included); a failure is logged and skipped.</summary>
    public static IReadOnlyList<string> BusyFolders(Action<Exception> logFailure) =>
        [.. new[] { PInvoke.FOLDERID_Downloads, PInvoke.FOLDERID_Screenshots }.Select(folderId => TryGetPath(folderId, logFailure)).OfType<string>()];

    /// <summary>Downloads, where Windows has it (M27: a rule's source); null when it cannot be told (logged).</summary>
    public static string? Downloads(Action<Exception> logFailure) => TryGetPath(PInvoke.FOLDERID_Downloads, logFailure);

    private static unsafe string? TryGetPath(Guid folderId, Action<Exception> logFailure)
    {
        PWSTR path = default;
        try
        {
            PInvoke.SHGetKnownFolderPath(&folderId, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, default, &path).ThrowOnFailure();
            return path.ToString();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) // E_INVALIDARG (an id this Windows lacks) is an ArgumentException (hard rule 7)
        {
            logFailure(failure); // no such folder on this PC (Screenshots before the first one): no busy-folder default
            return null;
        }
        finally
        {
            if (path.Value is not null) PInvoke.CoTaskMemFree(path.Value);
        }
    }
}
