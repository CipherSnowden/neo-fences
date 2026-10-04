using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace NeoFences.Spikes.M0;

public partial class LabWindow : Window
{
    private static readonly uint TaskbarCreatedMessage = Windows.Win32.PInvoke.RegisterWindowMessage("TaskbarCreated");

    /// <summary>Raised on the UI thread when Explorer restarts (TaskbarCreated broadcast).</summary>
    public event Action? ExplorerRestarted;

    public LabWindow()
    {
        InitializeComponent();
        Lab.Logged += line => Dispatcher.BeginInvoke(() =>
        {
            LogList.Items.Add(line);
            LogList.ScrollIntoView(line);
        });
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(OnWindowMessage);
    }

    public void AddButton(string label, Action onClick)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4) };
        button.Click += (_, _) =>
        {
            try { onClick(); }
            catch (Exception failure) { Lab.Log($"'{label}' failed: {failure.GetType().Name}: {failure.Message}"); }
        };
        ActionsPanel.Children.Add(button);
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message == TaskbarCreatedMessage)
        {
            Lab.Log("TaskbarCreated received (Explorer restarted)");
            ExplorerRestarted?.Invoke();
        }
        return 0;
    }
}
