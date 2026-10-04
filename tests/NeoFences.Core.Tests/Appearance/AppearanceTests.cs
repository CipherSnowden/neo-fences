using NeoFences.Core.Appearance;
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Appearance;

/// <summary>M14 (v1.7, spec 2026-10-04-appearance-design): the look resolver, the wallpaper accent and the config.</summary>
public class AppearanceTests
{
    private static readonly AppearanceSettings Defaults = new();
    private static readonly Fence Plain = Fence.Create("Games");
    private static readonly Argb Red = Argb.FromHex("#E84855")!.Value;

    // ---------- §3 the defaults are v1.6's look ----------

    [Fact]
    public void Defaults_GiveTheV16LookInBothTones()
    {
        var dark = FenceLook.Resolve(Defaults, Plain, light: false, wallpaperAccent: null);
        Assert.Equal(0, dark.Veil.A);                                   // Clear: no veil
        Assert.Equal(new Argb(0x40, 0xFF, 0xFF, 0xFF), dark.Border);
        Assert.Equal(new Argb(0xFF, 0xFF, 0xFF, 0xFF), dark.TitleText);
        Assert.Null(dark.TitleStrip);
        Assert.Null(dark.Bar);

        var light = FenceLook.Resolve(Defaults, Plain, light: true, wallpaperAccent: null);
        Assert.Equal(new Argb(0xB8, 0xF2, 0xF2, 0xF2), light.Veil);     // today's light veil
        Assert.Equal(new Argb(0x33, 0x00, 0x00, 0x00), light.Border);
        Assert.Equal(new Argb(0xE6, 0x00, 0x00, 0x00), light.TitleText);

        Assert.Equal(("Segoe UI", 14, TitleWeight.SemiBold, 30.0), (dark.Font.Family, dark.Font.Size, dark.Font.Weight, dark.TitleHeight));
    }

    [Theory]
    [InlineData(false, 40, 0x66)]
    [InlineData(true, 0, 0x00)]
    [InlineData(true, 85, 0xD9)]
    public void Strength_SetsTheVeilOfTheCurrentTone(bool light, int strength, byte alpha)
    {
        var appearance = light ? Defaults with { StrengthLight = strength } : Defaults with { StrengthDark = strength };
        Assert.Equal(alpha, FenceLook.Resolve(appearance, Plain, light, wallpaperAccent: null).Veil.A);
    }

    // ---------- §3.1 which colour ----------

    [Fact]
    public void Colour_CustomBeatsSwatchBeatsAccent_AndTheAccentNeedsItsSwitch()
    {
        var accent = new Argb(0xFF, 0x20, 0x90, 0x40);
        var swatch = Plain with { TabColor = TabColor.Blue };
        var custom = swatch with { CustomColor = "#E84855" };

        Assert.Equal(Red, FenceLook.Resolve(Defaults, custom, light: false, accent).Colour);
        Assert.Equal(FenceLook.Swatches[TabColor.Blue], FenceLook.Resolve(Defaults, swatch, light: false, accent).Colour);
        Assert.Null(FenceLook.Resolve(Defaults, Plain, light: false, accent).Colour);                                   // switch off
        Assert.Equal(accent, FenceLook.Resolve(Defaults with { WallpaperAccent = true }, Plain, light: false, accent).Colour);
    }

    // ---------- §3.2 the three styles ----------

    [Fact]
    public void AccentEdge_ColoursBorderBarAndTitle_KeepsTheVeil()
    {
        var look = FenceLook.Resolve(Defaults, Plain with { CustomColor = "#E84855" }, light: false, wallpaperAccent: null);
        Assert.Equal(0, look.Veil.A);
        Assert.Equal(Red, look.Border);
        Assert.Equal(Red, look.Bar);
        Assert.True(look.TitleText.R > Red.R || look.TitleText.G > Red.G); // lighter than the colour: readable on dark
        Assert.Null(look.TitleStrip);
    }

    [Fact]
    public void TintedGlass_ShowsAtClear_AndBorderTakesTheColour()
    {
        var look = FenceLook.Resolve(Defaults with { ColourStyle = ColourStyle.TintedGlass }, Plain with { CustomColor = "#E84855" }, light: false, wallpaperAccent: null);
        Assert.True(look.Veil.A >= 56);             // at least 22 %
        Assert.True(look.Veil.R > look.Veil.G);     // reddish
        Assert.Equal(Red with { A = 0xB3 }, look.Border);
        Assert.Null(look.Bar);
    }

    [Theory]
    [InlineData("#0078D4", 0xFF)] // dark blue strip: white title
    [InlineData("#FFB900", 0x00)] // yellow strip: dark title
    [InlineData("#16C60C", 0x00)] // green strip (luminance 0.41): dark reads better than white (final review I4)
    [InlineData("#00B7C3", 0x00)] // teal, same
    [InlineData("#E81123", 0xFF)] // deep red: white
    public void TitleStrip_ColoursOnlyTheTitleBar_WithReadableText(string colour, byte titleRed)
    {
        var look = FenceLook.Resolve(Defaults with { ColourStyle = ColourStyle.TitleStrip }, Plain with { CustomColor = colour }, light: false, wallpaperAccent: null);
        Assert.Equal(Argb.FromHex(colour)!.Value with { A = 0xBF }, look.TitleStrip);
        Assert.Equal(titleRed, look.TitleText.R);
        Assert.Equal(new Argb(0x40, 0xFF, 0xFF, 0xFF), look.Border);
    }

    // ---------- §3.4 fonts ----------

    [Fact]
    public void Font_OneForAllFences_AndTheTitleBarGrows() // per-fence fonts removed in v1.7.1 (ADR-038)
    {
        var appearance = Defaults with { TitleFont = new TitleFont("Bahnschrift", 20, TitleWeight.Bold) };
        var look = FenceLook.Resolve(appearance, Plain, light: false, wallpaperAccent: null);
        Assert.Equal(("Bahnschrift", 20, TitleWeight.Bold, 38.0), (look.Font.Family, look.Font.Size, look.Font.Weight, look.TitleHeight));
        Assert.Equal(26.0, FenceLook.TitleHeightFor(12));
    }

    [Fact]
    public void Font_AnOldPerFenceFontIsIgnored_AndDroppedOnSave()
    {
        var config = ConfigNormalizer.Normalize(ConfigJson.Deserialize(
            """{ "fences": [ { "id": "a", "title": "A", "isInbox": true, "titleFont": { "family": "Bahnschrift", "size": 20 } } ] }"""));
        var look = FenceLook.Resolve(config.Settings.Appearance, config.Fences[0], light: false, wallpaperAccent: null);
        Assert.Equal(("Segoe UI", 14), (look.Font.Family, look.Font.Size));
        Assert.DoesNotContain("Bahnschrift", ConfigJson.Serialize(config));
    }

    // ---------- §4 the wallpaper's colour ----------

    private static byte[] Image(params (byte R, byte G, byte B, int Count)[] runs) =>
        [.. runs.SelectMany(run => Enumerable.Repeat(new[] { run.B, run.G, run.R, (byte)255 }, run.Count).SelectMany(pixel => pixel))];

    [Fact]
    public void Accent_TheStrongestVividHueWins_DarkAndGreyPixelsAreIgnored()
    {
        // mostly near-black and grey, some vivid red, a little blue
        var accent = AccentColor.FromPixels(Image((5, 5, 8, 600), (128, 128, 128, 300), (220, 40, 50, 80), (30, 60, 220, 20)));
        Assert.NotNull(accent);
        Assert.True(accent.Value.R > 150 && accent.Value.G < 110 && accent.Value.B < 120);
        Assert.Equal(0xFF, accent.Value.A);
    }

    [Fact]
    public void Accent_AGreyImageHasNone_AndColoursAreClampedToReadableLightness()
    {
        Assert.Null(AccentColor.FromPixels(Image((10, 10, 10, 500), (200, 200, 200, 500))));
        var deep = AccentColor.FromPixels(Image((60, 0, 0, 1000)))!.Value;   // very dark red
        var lightness = (Math.Max(deep.R, Math.Max(deep.G, deep.B)) + Math.Min(deep.R, Math.Min(deep.G, deep.B))) / 2.0 / 255;
        Assert.InRange(lightness, 0.39, 0.66);

        // a muted slate (the user's WE wallpaper gave #566376): the same hue, vivid enough to read as an accent
        var muted = AccentColor.FromPixels(Image((86, 99, 118, 1000)))!.Value;
        double max = Math.Max(muted.R, Math.Max(muted.G, muted.B)) / 255.0, min = Math.Min(muted.R, Math.Min(muted.G, muted.B)) / 255.0;
        Assert.True((max - min) / (1 - Math.Abs(max + min - 1)) >= 0.44, $"{muted.ToHex()} is too grey");
        Assert.True(muted.B > muted.G && muted.G > muted.R); // still blue
    }

    // ---------- §4 Wallpaper Engine's files ----------

    [Fact]
    public void WallpaperEngine_SelectedWallpapersAndPreviewAreRead()
    {
        const string config = """
            { "cipher": { "general": { "wallpaperconfig": { "selectedwallpapers": {
                "Monitor0": { "file": "C:/Steam/steamapps/workshop/content/431960/1405736695/scene.pkg" },
                "Monitor1": { "file": "D:/WE/projects/myproject/index.html" } } } } },
              "?installdirectory": "C:/Steam" }
            """;
        var selected = WallpaperEngineFiles.SelectedWallpapers(config);
        Assert.Equal(@"C:\Steam\steamapps\workshop\content\431960\1405736695\scene.pkg", selected[0]);
        Assert.Equal(@"D:\WE\projects\myproject\index.html", selected[1]);
        Assert.Equal("preview.gif", WallpaperEngineFiles.PreviewName("""{ "title": "Kara", "preview": "preview.gif", "type": "scene" }"""));
        Assert.Equal("Kara", WallpaperEngineFiles.TitleOf("""{ "title": " Kara ", "preview": "preview.gif" }"""));
        Assert.Empty(WallpaperEngineFiles.SelectedWallpapers("{ not json"));
        Assert.Empty(WallpaperEngineFiles.SelectedWallpapers("""{ "cipher": { "general": {} } }"""));
        Assert.Null(WallpaperEngineFiles.PreviewName("""{ "preview": "../../escape.jpg" }""")); // stays inside the wallpaper's folder
    }

    // ---------- §2 config ----------

    [Fact]
    public void Config_SchemaThree_AndBrokenAppearanceIsRepaired()
    {
        const string json = """
            { "schemaVersion": 2,
              "settings": { "appearance": { "strengthDark": 300, "strengthLight": -5, "colourStyle": "neon", "titleFont": { "family": " ", "size": 13, "weight": "heavy" } } },
              "fences": [ { "id": "a", "title": "A", "isInbox": true, "customColor": "red", "titleFont": { "size": 99, "family": "Bahnschrift" } },
                          { "id": "b", "title": "B", "customColor": "#e84855" } ] }
            """;
        var config = ConfigNormalizer.Normalize(ConfigJson.Deserialize(json));

        Assert.Equal(NeoFencesConfig.CurrentSchemaVersion, config.SchemaVersion); // 3 in v1.7, 4 since v1.8 (M17)
        var appearance = config.Settings.Appearance;
        Assert.Equal((85, 0, ColourStyle.AccentEdge), (appearance.StrengthDark, appearance.StrengthLight, appearance.ColourStyle));
        Assert.Equal(new TitleFont("Segoe UI", 14, TitleWeight.SemiBold), appearance.TitleFont);
        Assert.Null(config.Fences[0].CustomColor);
        Assert.Equal("#E84855", config.Fences[1].CustomColor);
    }

    [Fact]
    public void Config_AnOldFileGetsTheDefaults_AndAppearanceRoundTrips()
    {
        var old = ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "schemaVersion": 2, "fences": [ { "id": "a", "title": "A", "isInbox": true } ] }"""));
        Assert.Equal(new AppearanceSettings(), old.Settings.Appearance);

        var styled = old with { Settings = old.Settings with { Appearance = new AppearanceSettings { ColourStyle = ColourStyle.TitleStrip, WallpaperAccent = true, StrengthDark = 40 } } };
        var again = ConfigNormalizer.Normalize(ConfigJson.Deserialize(ConfigJson.Serialize(styled)));
        Assert.Equal(styled.Settings.Appearance, again.Settings.Appearance);
        Assert.Contains("\"colourStyle\": \"titleStrip\"", ConfigJson.Serialize(styled));
    }

    // ---------- fence menu edits ----------

    [Fact]
    public void Edits_ASwatchClearsTheCustomColour()
    {
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox"), Plain] };

        var custom = FenceEdits.SetCustomColor(config, Plain.Id, "#e84855");
        Assert.Equal("#E84855", custom.Fences[1].CustomColor);
        var swatch = FenceTabs.SetColor(custom, Plain.Id, TabColor.Teal);
        Assert.Null(swatch.Fences[1].CustomColor);
        Assert.Equal(TabColor.Teal, swatch.Fences[1].TabColor);
        Assert.Same(swatch, FenceTabs.SetColor(swatch, Plain.Id, TabColor.Teal));

    }
}
