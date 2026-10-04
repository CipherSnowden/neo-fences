namespace NeoFences.Core.Model;

/// <summary>Game Library settings (M12, spec §4), in <c>config.json</c>.</summary>
public sealed record LibrarySettings
{
    /// <summary>Folders whose sub-folders are games (e.g. D:GameLibrary).</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    public LibrarySources Sources { get; init; } = new();

    /// <summary>Game ids the user hid ("steam:431960").</summary>
    public IReadOnlyList<string> Hidden { get; init; } = [];
}

/// <summary>Which sources the library reads; all on by default.</summary>
public sealed record LibrarySources
{
    public bool Steam { get; init; } = true;
    public bool Epic { get; init; } = true;
    public bool Gog { get; init; } = true;
    public bool Ubisoft { get; init; } = true;
    public bool Ea { get; init; } = true;
    public bool BattleNet { get; init; } = true;
    public bool Xbox { get; init; } = true;
    public bool Folders { get; init; } = true;
    public bool DesktopShortcuts { get; init; } = true;
}
