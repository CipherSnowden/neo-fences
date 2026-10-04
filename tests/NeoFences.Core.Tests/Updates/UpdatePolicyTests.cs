using NeoFences.Core.Config;
using NeoFences.Core.Model;
using NeoFences.Core.Updates;

namespace NeoFences.Core.Tests.Updates;

/// <summary>M17 (v1.8, spec 2026-10-04-public-releases-design §4): when NeoFences looks for an update, and the setting.</summary>
public class UpdatePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(5.5));

    private static bool Check(UpdateState state, bool enabled = true, bool installed = true, bool gameMode = false) =>
        UpdatePolicy.ShouldCheck(Now, state, enabled: enabled, installed: installed, gameMode: gameMode);

    [Fact]
    public void FirstCheckAtStart_ThenOnceADay()
    {
        Assert.True(Check(new UpdateState(LastCheck: null, LastFailed: false)));
        Assert.False(Check(new UpdateState(Now.AddHours(-23), LastFailed: false)));
        Assert.True(Check(new UpdateState(Now.AddHours(-24), LastFailed: false)));
    }

    [Fact]
    public void AfterAFailure_AgainIn6Hours()
    {
        Assert.False(Check(new UpdateState(Now.AddHours(-5), LastFailed: true)));
        Assert.True(Check(new UpdateState(Now.AddHours(-6), LastFailed: true)));
    }

    [Theory]
    [InlineData(false, true, false)]  // switched off: no network at all
    [InlineData(true, false, false)]  // a developer build (not installed by Setup): never updates
    [InlineData(true, true, true)]    // a game in front: never
    public void Never_WhenOffNotInstalledOrGaming(bool enabled, bool installed, bool gameMode) =>
        Assert.False(Check(new UpdateState(LastCheck: null, LastFailed: false), enabled, installed, gameMode));

    [Fact]
    public void NextCheck_IsWhenTheScheduleSays_OrAtOnce()
    {
        Assert.Equal(TimeSpan.Zero, UpdatePolicy.NextCheckIn(Now, new UpdateState(null, false)));
        Assert.Equal(TimeSpan.FromHours(4), UpdatePolicy.NextCheckIn(Now, new UpdateState(Now.AddHours(-20), false)));
        Assert.Equal(TimeSpan.FromHours(1), UpdatePolicy.NextCheckIn(Now, new UpdateState(Now.AddHours(-5), true)));
        Assert.Equal(TimeSpan.Zero, UpdatePolicy.NextCheckIn(Now, new UpdateState(Now.AddDays(-3), false)));
    }

    [Fact]
    public void Config_AutoUpdateIsOnByDefault_SchemaFour_AndSurvivesARoundTrip()
    {
        Assert.Equal(4, NeoFencesConfig.CurrentSchemaVersion);
        Assert.True(new Settings().AutoUpdate);
        var old = ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "schemaVersion": 3, "fences": [] }"""));
        Assert.True(old.Settings.AutoUpdate);
        Assert.Equal(4, old.SchemaVersion);
        var off = old with { Settings = old.Settings with { AutoUpdate = false } };
        Assert.False(ConfigNormalizer.Normalize(ConfigJson.Deserialize(ConfigJson.Serialize(off))).Settings.AutoUpdate);
    }
}
