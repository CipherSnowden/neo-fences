namespace NeoFences.Core.Membership;

/// <summary>A change to the desktop reported by NeoFences.Shell's watcher, applied with <see cref="FenceMembership.Apply"/>.</summary>
public abstract record DesktopChange
{
    public sealed record Created(string ItemRef) : DesktopChange;

    public sealed record Deleted(string ItemRef) : DesktopChange;

    public sealed record Renamed(string OldRef, string NewRef) : DesktopChange;
}
