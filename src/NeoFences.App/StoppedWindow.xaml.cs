using System.Diagnostics;
using System.IO;
using System.Windows;
using NeoFences.Core.Config;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// "NeoFences stopped after repeated crashes" (M33, ADR-053): shown by a <c>--stopped</c> start when safe mode crashed too.
/// Open logs, Start from a backup (a snapshot of the current setup first, then the newest daily backups, then safe mode),
/// or Close. Nothing else of NeoFences runs while it is open.
/// </summary>
public partial class StoppedWindow : Window
{
    public StoppedWindow()
    {
        InitializeComponent();
        OpenLogsButton.Click += (_, _) => OpenLogs();
        BackupButton.Click += (_, _) => StartFromBackup();
    }

    private static void OpenLogs()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogsDirectory}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "could not open the logs folder");
        }
    }

    private void StartFromBackup()
    {
        var data = AppPaths.DataDirectory;
        var configStore = new ConfigStore(data);
        var itemStore = new ItemStore(data);
        var now = DateTimeOffset.Now;
        var current = configStore.Load();
        var currentItems = itemStore.Load();
        var snapshot = Snapshots.Take(current.Config, currentItems.Document, name: $"Before starting from a backup ({now:d MMM HH:mm})", now: now);
        if (new SnapshotStore(Path.Combine(data, "snapshots")).Save(snapshot) is null)
        {
            Show("Nothing changed: NeoFences could not save your current setup as a snapshot first (see the log).");
            return;
        }
        var config = Newest(configStore.BackupsDirectory, "config-*.json");
        var items = Newest(configStore.BackupsDirectory, "items-*.json");
        if (config is null && items is null)
        {
            Show("There is no daily backup yet. Your current setup is kept; NeoFences starts in safe mode.");
        }
        try
        {
            if (config is not null) File.Copy(config, configStore.ConfigPath, overwrite: true);
            if (items is not null) File.Copy(items, itemStore.ItemsPath, overwrite: true);
            Log.Information("starting from a backup: {Config}, {Items} (snapshot saved first)", config, items);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "could not put the backup in place");
            Show("The backup could not be put in place (see the log). Your current setup is kept; NeoFences starts in safe mode.");
        }
        new Watchdog(data, message => Log.Information("{Message}", message)).ClearRestarts();
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Watchdog.SafeModeArgument) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "could not start NeoFences in safe mode");
        }
        Close();
    }

    private static string? Newest(string directory, string pattern) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, pattern).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault() : null;

    private void Show(string text)
    {
        ResultText.Text = text;
        ResultText.Visibility = Visibility.Visible;
    }
}
