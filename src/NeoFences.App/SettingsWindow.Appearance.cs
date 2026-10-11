using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>What Settings → Look &amp; feel shows (M14, M41b): the settings and the tone now.</summary>
public sealed record AppearanceView(AppearanceSettings Settings, bool LightTheme);

/// <summary>
/// Settings → Look &amp; feel (M41b, spec §3): animations and item names — what is not about one fence (theme, colours, the
/// wallpaper colour since 1.0.1, titles, transparency and fonts live in Fence settings, ADR-067). Changes apply live: every change goes to the
/// host, which restyles, saves and shows it back.
/// </summary>
public partial class SettingsWindow
{
    public event Action<AppearanceSettings>? AppearanceChanged;

    private static readonly Motion[] Motions = [Motion.Lively, Motion.Calm, Motion.Off];
    private static readonly NameStyle[] NameStyles = [NameStyle.Crisp, NameStyle.Chips, NameStyle.Plain];

    private AppearanceView? _appearance;
    private Action<int>? _selectMotion;
    private Action<int>? _selectNames;

    private void InitializeAppearance()
    {
        _selectMotion = Segmented(MotionChoice, ["Lively", "Calm", "Off"], index => ReportAppearance(current => current with { Motion = Motions[index] }));
        _selectNames = Segmented(NameStyleChoice, ["Crisp", "Chips", "Plain"], index => ReportAppearance(current => current with { NameStyle = NameStyles[index] }));
    }

    private void ShowAppearance(AppearanceView view)
    {
        _appearance = view;
        var settings = view.Settings;
        _selectMotion?.Invoke(Array.IndexOf(Motions, settings.Motion));
        _selectNames?.Invoke(Array.IndexOf(NameStyles, settings.NameStyle));
    }

    private void ReportAppearance(Func<AppearanceSettings, AppearanceSettings> change)
    {
        if (_updating || _appearance is not { } shown) return;
        var changed = change(shown.Settings);
        if (changed == shown.Settings) return;
        _appearance = shown with { Settings = changed };
        AppearanceChanged?.Invoke(changed);
    }
}
