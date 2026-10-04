namespace NeoFences.Core.Membership;

/// <summary>
/// Where an item should go if it (re)appears before <paramref name="ExpiresAt"/>: a fenced item that just disappeared
/// (editor safe-save, M3a: <see cref="FenceMembership.SafeSaveWindow"/>) or a file just dropped onto a fence from Explorer
/// (M3b: <see cref="FenceMembership.ArrivalWindow"/>, as Windows may still be copying it).
/// </summary>
public sealed record RememberedPlacement(string ItemRef, string FenceId, int Index, DateTimeOffset ExpiresAt);
