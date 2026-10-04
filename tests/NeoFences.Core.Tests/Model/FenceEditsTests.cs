using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

public class FenceEditsTests
{
    private static (NeoFencesConfig Config, Fence Games) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true };
        var games = Fence.Create("Games");
        return (new NeoFencesConfig { Fences = [inbox, games] }, games);
    }

    private static Fence FenceOf(NeoFencesConfig config, string fenceId) => config.Fences.Single(fence => fence.Id == fenceId);

    [Fact]
    public void Rename_TrimsTheNewTitle()
    {
        var (config, games) = Sample();

        var renamed = FenceEdits.Rename(config, games.Id, "  Shooters  ");

        Assert.Equal("Shooters", FenceOf(renamed, games.Id).Title);
    }

    [Fact]
    public void Rename_BlankTitle_KeepsTheOldOne()
    {
        // Enter on an emptied title box must not leave a nameless fence.
        var (config, games) = Sample();

        Assert.Same(config, FenceEdits.Rename(config, games.Id, "   "));
    }

    [Fact]
    public void Rename_LongTitle_IsCut()
    {
        var (config, games) = Sample();

        var renamed = FenceEdits.Rename(config, games.Id, new string('x', 200));

        Assert.Equal(FenceEdits.MaxTitleLength, FenceOf(renamed, games.Id).Title.Length);
    }

    [Fact]
    public void Rename_UnknownFence_Throws()
    {
        var (config, _) = Sample();

        Assert.Throws<ArgumentException>(() => FenceEdits.Rename(config, "missing", "Name"));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(96)]
    public void SetIconSize_SupportedSize_IsStored(int iconSize)
    {
        var (config, games) = Sample();

        Assert.Equal(iconSize, FenceOf(FenceEdits.SetIconSize(config, games.Id, iconSize), games.Id).IconSize);
    }

    [Fact]
    public void SetIconSize_UnsupportedSize_Throws()
    {
        var (config, games) = Sample();

        Assert.Throws<ArgumentOutOfRangeException>(() => FenceEdits.SetIconSize(config, games.Id, 50));
    }

    [Fact]
    public void SetLocked_IsStored_AndUnlockRestores()
    {
        var (config, games) = Sample();

        var locked = FenceEdits.SetLocked(config, games.Id, locked: true);

        Assert.True(FenceOf(locked, games.Id).Locked);
        Assert.False(FenceOf(FenceEdits.SetLocked(locked, games.Id, locked: false), games.Id).Locked);
    }
}
