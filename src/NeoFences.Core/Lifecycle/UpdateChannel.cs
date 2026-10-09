namespace NeoFences.Core.Lifecycle;

/// <summary>
/// Which releases an installed copy updates to (1.0.0-rc, ADR-062): release candidates are GitHub pre-releases. A stable
/// copy never sees them; a release candidate follows them (rc.1 → rc.2) and then the stable release that ends them. Pure.
/// </summary>
public static class UpdateChannel
{
    /// <summary>True when this copy's own version is a pre-release ("1.0.0-rc.1", with or without "+commit" after it).</summary>
    public static bool FollowsPrereleases(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        var plus = version.IndexOf('+');
        return (plus < 0 ? version : version[..plus]).Contains('-');
    }
}
