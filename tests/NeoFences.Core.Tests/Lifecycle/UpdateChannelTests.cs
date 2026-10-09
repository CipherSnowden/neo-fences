using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

/// <summary>
/// 1.0.0-rc (ADR-062): a release candidate's update check follows pre-releases (rc.2, then 1.0.0); a stable copy never
/// sees them. The version is the app's informational version: SemVer, with "+commit" build metadata after it.
/// </summary>
public class UpdateChannelTests
{
    [Theory]
    [InlineData("1.0.0-rc.1+5f631b2620970e4516e9a7c3ef68fa3c6de52e45", true)]
    [InlineData("1.0.0-rc.2", true)]
    [InlineData("0.26.0+5f631b2620970e4516e9a7c3ef68fa3c6de52e45", false)]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.0+build-7", false)] // a hyphen in the build metadata is not a pre-release
    [InlineData("", false)]
    [InlineData(null, false)]
    public void FollowsPrereleases_OnlyWhenThisVersionIsOne(string? version, bool expected) =>
        Assert.Equal(expected, UpdateChannel.FollowsPrereleases(version));
}
