namespace NeoFences.Core.Model;

/// <summary>Item refs are Windows shell parsing names: paths compare case-insensitively.</summary>
public static class ItemRef
{
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;
}
