using System.Collections.Concurrent;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// One STA thread for shell operations that can block or show Windows' dialogs (open, recycle, rename): the fences keep
/// responding meanwhile, and shell handlers get the apartment they expect (M3a review). Operations run in order; a
/// failure is logged, never thrown.
/// </summary>
public sealed class ShellWorker : IDisposable
{
    private readonly BlockingCollection<Action> _operations = new();

    public ShellWorker()
    {
        var worker = new Thread(Work) { IsBackground = true, Name = "NeoFences shell worker" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    /// <summary>One operation on its own STA thread: for opens, which may wait long on their own and must not queue.</summary>
    public static void RunAlone(Action operation, string name)
    {
        var worker = new Thread(() => RunLogged(operation)) { IsBackground = true, Name = name };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public void Run(Action operation)
    {
        if (!_operations.IsAddingCompleted) _operations.TryAdd(operation);
    }

    private void Work()
    {
        foreach (var operation in _operations.GetConsumingEnumerable()) RunLogged(operation);
    }

    private static void RunLogged(Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "a shell operation failed");
        }
    }

    public void Dispose() => _operations.CompleteAdding();
}
