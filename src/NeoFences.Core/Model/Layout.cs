namespace NeoFences.Core.Model;

/// <summary>A fence's rectangle in DIPs, relative to the top-left of its monitor's work area.</summary>
public sealed record FenceRect(string Monitor, double X, double Y, double W, double H);

/// <summary>Work-area size (DIPs) a layout was saved for, so it can be scaled to another monitor.</summary>
public sealed record MonitorArea(double WorkWidth, double WorkHeight)
{
    /// <summary>False for zero, negative, NaN or infinite sizes (a bad monitor query or a hand edit).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsUsable => double.IsFinite(WorkWidth) && double.IsFinite(WorkHeight) && WorkWidth > 0 && WorkHeight > 0;
}

/// <summary>Fence positions for one display configuration (see <c>DisplayFingerprint</c>).</summary>
public sealed record Layout
{
    public IReadOnlyDictionary<string, MonitorArea> Monitors { get; init; } = new Dictionary<string, MonitorArea>();
    public IReadOnlyDictionary<string, FenceRect> Fences { get; init; } = new Dictionary<string, FenceRect>();
}
