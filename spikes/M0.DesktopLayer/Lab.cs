using System.IO;

namespace NeoFences.Spikes.M0;

/// <summary>Throwaway spike logger: appends to the Lab window and to %LOCALAPPDATA%\NeoFences\spike-m0\lab.log.</summary>
public static class Lab
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoFences", "spike-m0");

    private static readonly Lock FileLock = new();

    public static event Action<string>? Logged;

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{Environment.ProcessId}] {message}";
        lock (FileLock)
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(Path.Combine(DataDir, "lab.log"), line + Environment.NewLine);
        }
        Logged?.Invoke(line);
    }
}
