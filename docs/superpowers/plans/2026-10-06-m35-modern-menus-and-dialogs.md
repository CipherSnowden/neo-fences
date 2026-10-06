# M35 — Modern menus and dialogs (0.22.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One Windows 11 menu style with icons for the fence, item and tray menus; a 12-line grouped fence menu; Settings in eight sections with toggle switches; colour swatches in the menu and a modern custom-colour picker; one message dialog instead of MessageBox.

**Architecture:** Core gains the picker's maths (test-first): `Hsv` (from/to `Argb`, hue wrap, clamping) and `Argb.FromUserHex` (what people type). The App gets one shared style (`Menus.xaml`, merged into the application, colours from `MenuTheme` that follow light/dark) and `MenuGlyph` (icons from Segoe Fluent Icons, red destructive entries, `MenuGlyph.Entry` for menus built in code); the fence menu is regrouped in XAML; item, game, panel, widget and tray menus use the helper; the tray menu is `TrayMenuView` (a WPF menu at the pointer, Shell only brings NeoFences forward); `ColourWindow` and `MessageDialog` are small Fluent windows; Settings becomes a section list plus pages with a `Switch` checkbox style. Windows' `ChooseColor` and the native tray popup go.

**Tech Stack:** .NET 10, C#, WPF (the Fluent `ThemeMode` already used by NeoFences' windows; menus styled by NeoFences), xUnit; PowerShell for the live check.

**Spec:** `docs/superpowers/specs/2026-10-06-modern-menus-and-dialogs-design.md` (approved 2026-10-06). Decision: ADR-056 — added by Task 3.

## How this plan is written

Built in a scratch worktree (`m35-proto`), probed on a copy of the owner's data (owner's OK: the fence menu with Add ▸,
View ▸ and Colour ▸, the colour picker, an item and a game menu, the tray menu with Restore snapshot ▸, Settings General /
Games / Appearance; screenshots sent), fixed after the probe (the Delete entry's long text, the Settings selection, two
menu paths in texts), and replay-verified on a fresh worktree of `main` at `98776e6`: the tests patch alone fails to build
(8 error lines — the RED), the Core patch makes 773 tests pass, the App patch builds with 0 warnings and 773 pass, and the
tree is identical to the prototype. Tasks 1–2 are **patches to apply** (`git apply --whitespace=nowarn <file>`; if one does
not apply, stop). The App patch deletes `src/NeoFences.Shell/ColorPicker.cs`.

**Calls made while prototyping (ledger them as rulings at Task 2):** "Delete fence" is the whole entry; the old reassurance
("your files are not touched") is its tooltip; a missing item's menu keeps Locate… first and moves Remove to the end like
every other menu; a folder panel's menu starts with Open folder; "Use my accent colour" stores the accent of that moment (it
does not follow later accent changes); the swatches are None plus the eight fence colours; the tray menu is placed with the
first fence window's DPI (a ponytail for per-monitor DPI); the delete-without-snapshot question now says why it asks; the
Settings window is 820 × 680 (the section list needs the room); Game mode's icon is the lightning glyph; `PathPicker` lost
an unused `using` that only `ChooseColor`'s bindings made compile.

## Global Constraints

- Hard rules stand: no new NuGet dependency; Win32 only in `NeoFences.Shell` (the tray's foreground call stays there); the
  fences' own drawing (items, tiles, panels, widgets) is not restyled; user files never touched.
- Copy, exactly: fence menu "Add", "Item…", "From desktop…", "Games…", "Folder panel…", "Widget", "View", "Icon size",
  "Labels", "Sort by", "Layout", "Auto-collect…", "Rename" (F2; "Rename tab" on tabs), "Colour", "Use my accent colour",
  "Custom colour…", "Lock position", "Refresh", "New fence", "Empty", "Folder panel…", "Settings…", "Detach tab", "Delete
  fence"; Settings sections "General", "Fences", "Appearance", "Games", "Game mode", "Snapshots", "Updates", "About";
  switches "On" / "Off"; picker window "Custom colour", "OK", "Cancel".
- "Start with Windows" only in Settings → General; "Exit NeoFences" only in the tray.
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer**; secret scan before each commit.
- Version stays `0.21.0` until the release step; the release is 0.22.0.

## Review Focus

1. **The tray menu**: opens at the pointer near the taskbar (100 / 125 / 150 %), closes on a click elsewhere or Esc, never
   stays stuck open or hides behind the taskbar; every entry still acts (Restore snapshot ▸, Leave safe mode, Undo delete,
   Restart to update, Exit); it works while paused and in safe mode.
2. **Light mode and a live switch** of Windows' app mode: menu text, hover, check marks and separators stay readable; open
   sub-menus follow; the Fluent windows' own text-box menus (cut / copy / paste) use the new style without breaking.
3. **Colour**: a swatch click sets the colour and the ring follows the fence and its tabs; Use my accent colour; Custom
   colour… Cancel / Esc changes nothing; odd hex input never throws; the swatch row works by keyboard (Left / Right / Enter).
4. **Settings after the move to pages**: every switch and control still raises its change (nothing lost in the move); the
   banner shows on every page; "More in Settings…" opens Snapshots; arrows in the section list, Tab into the page.
5. **Menus by keyboard and odd states**: arrows / Enter / Esc in every menu and sub-menu; the Size grid's keys still work;
   disabled entries (paused, a missing item's actions) look disabled and do nothing; menus built for items that vanish
   while the menu is open never crash.

---

### Task 0: Worktree and baseline

- [ ] `git worktree add -b m35-menus ..\neo_fences-m35 main` (main at `98776e6` or later docs-only commits).
- [ ] `dotnet build` → 0 warnings; `dotnet test` → 747 passed.

### Task 1: Core — the colour picker's maths

**Files:**
- Create: `tests/NeoFences.Core.Tests/Appearance/ColourPickerTests.cs`, `src/NeoFences.Core/Appearance/Hsv.cs`
- Modify: `src/NeoFences.Core/Appearance/Argb.cs` (`FromUserHex`)

**Interfaces:**
- Produces: `Hsv(double H, double S, double V)`, `Hsv.FromArgb(Argb)`, `Hsv.ToArgb()`; `Argb.FromUserHex(string?) → Argb?`.

- [ ] **Step 1: Write the failing tests.** Write this patch to `m35-1a-tests.patch` and apply it:

```diff
diff --git a/tests/NeoFences.Core.Tests/Appearance/ColourPickerTests.cs b/tests/NeoFences.Core.Tests/Appearance/ColourPickerTests.cs
new file mode 100644
index 0000000..118b44e
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Appearance/ColourPickerTests.cs
@@ -0,0 +1,62 @@
+using NeoFences.Core.Appearance;
+
+namespace NeoFences.Core.Tests.Appearance;
+
+/// <summary>M35 (spec 2026-10-06-modern-menus-and-dialogs-design §3): the colour picker's maths and what people type as hex.</summary>
+public class ColourPickerTests
+{
+    [Theory]
+    [InlineData(0xFF, 0x00, 0x00, 0, 1, 1)]
+    [InlineData(0x00, 0xFF, 0x00, 120, 1, 1)]
+    [InlineData(0x00, 0x00, 0xFF, 240, 1, 1)]
+    [InlineData(0xFF, 0xFF, 0xFF, 0, 0, 1)]
+    [InlineData(0x00, 0x00, 0x00, 0, 0, 0)]
+    [InlineData(0x80, 0x80, 0x80, 0, 0, 0.502)]
+    public void Hsv_FromArgb_KnownColours(byte red, byte green, byte blue, double hue, double saturation, double value)
+    {
+        var hsv = Hsv.FromArgb(new Argb(0xFF, red, green, blue));
+        Assert.Equal(hue, hsv.H, 1);
+        Assert.Equal(saturation, hsv.S, 3);
+        Assert.Equal(value, hsv.V, 3);
+    }
+
+    [Theory]
+    [InlineData("#E23A50")]
+    [InlineData("#E8C13D")]
+    [InlineData("#3A7BE2")]
+    [InlineData("#000000")]
+    [InlineData("#FFFFFF")]
+    [InlineData("#808080")]
+    [InlineData("#16C60C")]
+    public void Hsv_RoundTrips_EveryByte(string hex)
+    {
+        var colour = Argb.FromHex(hex)!.Value;
+        Assert.Equal(colour, Hsv.FromArgb(colour).ToArgb());
+    }
+
+    [Fact]
+    public void Hsv_OutOfRange_IsClamped_HueWraps()
+    {
+        Assert.Equal(new Argb(0xFF, 0xFF, 0x00, 0x00), new Hsv(360, 1.5, 2).ToArgb()); // 360° is red again; S and V at most 1
+        Assert.Equal(new Argb(0xFF, 0x00, 0x00, 0x00), new Hsv(-30, -1, -1).ToArgb());
+        Assert.Equal(new Hsv(330, 1, 1).ToArgb(), new Hsv(-30, 1, 1).ToArgb());
+    }
+
+    [Theory]
+    [InlineData("#e23a50", "#E23A50")]
+    [InlineData("E23A50", "#E23A50")]
+    [InlineData("  #E23A50  ", "#E23A50")]
+    [InlineData("#abc", "#AABBCC")]
+    [InlineData("abc", "#AABBCC")]
+    public void FromUserHex_AcceptsWhatPeopleType(string typed, string expected) => Assert.Equal(expected, Argb.FromUserHex(typed)!.Value.ToHex());
+
+    [Theory]
+    [InlineData(null)]
+    [InlineData("")]
+    [InlineData("#12345")]
+    [InlineData("#12345G")]
+    [InlineData("red")]
+    [InlineData("#E23A5011")]
+    [InlineData("+E23A50")]
+    public void FromUserHex_RejectsTheRest(string? typed) => Assert.Null(Argb.FromUserHex(typed));
+}
```

- [ ] **Step 2: Run them.** `dotnet test tests/NeoFences.Core.Tests` → Expected: build fails, 8 `error CS` lines (`Hsv`, `FromUserHex` do not exist).

- [ ] **Step 3: Implement.** Write this patch to `m35-1b-core.patch` and apply it:

```diff
diff --git a/src/NeoFences.Core/Appearance/Argb.cs b/src/NeoFences.Core/Appearance/Argb.cs
index 3516f01..9751789 100644
--- a/src/NeoFences.Core/Appearance/Argb.cs
+++ b/src/NeoFences.Core/Appearance/Argb.cs
@@ -13,6 +13,15 @@ public readonly record struct Argb(byte A, byte R, byte G, byte B)
         return new Argb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
     }
 
+    /// <summary>What people type in the picker's hex box (M35): "#E23A50", "e23a50", "#abc" (shorthand), spaces around; else null.</summary>
+    public static Argb? FromUserHex(string? text)
+    {
+        var hex = text?.Trim() ?? "";
+        if (hex.StartsWith('#')) hex = hex[1..];
+        if (hex.Length == 3) hex = string.Concat(hex.Select(digit => new string(digit, 2)));
+        return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? FromHex("#" + hex) : null;
+    }
+
     public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
 
     /// <summary>This colour moved <paramref name="amount"/> (0–1) of the way to <paramref name="other"/>; alpha stays.</summary>
diff --git a/src/NeoFences.Core/Appearance/Hsv.cs b/src/NeoFences.Core/Appearance/Hsv.cs
new file mode 100644
index 0000000..d41bd57
--- /dev/null
+++ b/src/NeoFences.Core/Appearance/Hsv.cs
@@ -0,0 +1,43 @@
+namespace NeoFences.Core.Appearance;
+
+/// <summary>
+/// Hue (degrees, wraps), saturation and value (0–1, clamped) for the custom colour picker (M35, spec §3): the field is
+/// saturation × value at the chosen hue. Pure.
+/// </summary>
+public readonly record struct Hsv(double H, double S, double V)
+{
+    public static Hsv FromArgb(Argb colour)
+    {
+        double red = colour.R / 255.0, green = colour.G / 255.0, blue = colour.B / 255.0;
+        var max = Math.Max(red, Math.Max(green, blue));
+        var min = Math.Min(red, Math.Min(green, blue));
+        var range = max - min;
+        var hue = range == 0 ? 0
+            : max == red ? 60 * ((green - blue) / range % 6)
+            : max == green ? 60 * ((blue - red) / range + 2)
+            : 60 * ((red - green) / range + 4);
+        return new Hsv(hue < 0 ? hue + 360 : hue, max == 0 ? 0 : range / max, max);
+    }
+
+    /// <summary>An opaque colour; the hue wraps (−30° = 330°), saturation and value are clamped to 0–1.</summary>
+    public Argb ToArgb()
+    {
+        var hue = (H % 360 + 360) % 360;
+        double saturation = Math.Clamp(S, 0, 1), value = Math.Clamp(V, 0, 1);
+        var chroma = value * saturation;
+        var second = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
+        var match = value - chroma;
+        var (red, green, blue) = (int)(hue / 60) switch
+        {
+            0 => (chroma, second, 0.0),
+            1 => (second, chroma, 0.0),
+            2 => (0.0, chroma, second),
+            3 => (0.0, second, chroma),
+            4 => (second, 0.0, chroma),
+            _ => (chroma, 0.0, second),
+        };
+        return new Argb(0xFF, Byte(red + match), Byte(green + match), Byte(blue + match));
+    }
+
+    private static byte Byte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);
+}
```

- [ ] **Step 4: Run.** `dotnet test tests/NeoFences.Core.Tests` → Expected: `Passed: 773`.

- [ ] **Step 5: Commit.** `git add -A && git commit -m "feat: added the colour picker's HSV maths and typed hex colours in Core"`

### Task 2: Shell + App — menus, tray, Settings pages, colour, dialogs

**Files:**
- Create: `src/NeoFences.App/Menus.xaml`, `Menus.cs` (`MenuGlyph`, `MenuTheme`, `TrayMenuView`), `ColourWindow.xaml(.cs)`,
  `MessageDialog.xaml(.cs)`
- Delete: `src/NeoFences.Shell/ColorPicker.cs`
- Modify: `src/NeoFences.Shell/TrayIcon.cs` (`TrayMenu.BringForward` instead of the native popup), `NativeMethods.txt`
  (`ChooseColor` out), `PathPicker.cs` (unused using); `src/NeoFences.App/App.cs` (merge the menu style), `FenceWindow.xaml(.cs)`
  (the 12-line menu, the colour panel, Start with Windows and Exit out, tooltips), `FenceHost.cs` (theme, accent colour, the tray
  menu, the delete dialog), `FenceHost.Items.cs` / `GameItems.cs` / `FolderPanels.cs` / `Widgets.cs` / `Grid.cs` / `Covers.cs`
  (menus with icons; the Choose cover note), `FenceHost.Appearance.cs` (the new picker), `FenceHost.Collect.cs` (the dialog),
  `FenceHost.Library.cs` (text), `ItemPropertiesWindow.xaml.cs` (the dialog), `SettingsWindow.xaml(.cs)` (sections, pages,
  switches), `SettingsWindow.Library.cs` (comments), `AddGamesWindow.xaml(.cs)`, `DesktopFillWindow.xaml`, `OnlineArtWindow.xaml`,
  `ChooseCoverWindow.xaml.cs` ("Settings → Games")

**Interfaces:**
- Consumes: `Hsv`, `Argb.FromUserHex`.
- Produces: `MenuGlyph.Glyph` / `MenuGlyph.Danger` (attached), `MenuGlyph.Entry(header, glyph, click?, isChecked?, danger, enabled)`,
  `MenuGlyph.<names>`; `MenuTheme.Apply(resources, light)`; `TrayMenuView.Show(items, at, glyphOf, chosen)`; `TrayMenu.BringForward(handle)`;
  `ColourWindow.Pick(owner, initial) → string?`; `MessageDialog.Ask(owner, heading, text, primary, secondary) → bool`,
  `MessageDialog.Tell(owner, heading, text)`; `FenceWindow.AccentColorRequested`; `SettingsWindow.ShowPage(name)`.

- [ ] **Step 1: Implement.** Write this patch to `m35-2-app.patch` and apply it:

```diff
diff --git a/src/NeoFences.App/AddGamesWindow.xaml b/src/NeoFences.App/AddGamesWindow.xaml
index f0bad57..36a43db 100644
--- a/src/NeoFences.App/AddGamesWindow.xaml
+++ b/src/NeoFences.App/AddGamesWindow.xaml
@@ -11,7 +11,7 @@
             <RowDefinition Height="Auto" />
         </Grid.RowDefinitions>
         <TextBlock TextWrapping="Wrap" Margin="0,0,0,12" Foreground="{DynamicResource TextFillColorSecondaryBrush}"
-                   Text="Each ticked game becomes an item in this fence. Games already here are not ticked. Missing a game? Add your games folder in Settings → Game Library." />
+                   Text="Each ticked game becomes an item in this fence. Games already here are not ticked. Missing a game? Add your games folder in Settings → Games." />
         <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
             <StackPanel x:Name="GamesPanel" />
         </ScrollViewer>
diff --git a/src/NeoFences.App/AddGamesWindow.xaml.cs b/src/NeoFences.App/AddGamesWindow.xaml.cs
index bc82b3b..9c421b2 100644
--- a/src/NeoFences.App/AddGamesWindow.xaml.cs
+++ b/src/NeoFences.App/AddGamesWindow.xaml.cs
@@ -58,7 +58,7 @@ public partial class AddGamesWindow : Window
             GamesPanel.Children.Add(box);
             _rows.Add((box, row.Game));
         }
-        ListStatus.Text = rows.Count > 0 ? "" : scanning ? "Looking for games…" : "No games found — add your games folder in Settings → Game Library.";
+        ListStatus.Text = rows.Count > 0 ? "" : scanning ? "Looking for games…" : "No games found — add your games folder in Settings → Games.";
         ListStatus.Visibility = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
         UpdateAddButton();
     }
diff --git a/src/NeoFences.App/App.cs b/src/NeoFences.App/App.cs
index df6c377..482f033 100644
--- a/src/NeoFences.App/App.cs
+++ b/src/NeoFences.App/App.cs
@@ -84,6 +84,9 @@ public sealed class App : Application
         _exitSignal = ExitSignal();
         _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => Dispatcher.BeginInvoke(Shutdown), null, Timeout.Infinite, executeOnlyOnce: true);
 
+        // M35 (ADR-056): one menu style for every menu, light or dark like the fences.
+        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/NeoFences;component/menus.xaml", UriKind.Relative) });
+        MenuTheme.Apply(Resources, light: SystemTheme.AppsUseLightTheme());
         _host = new FenceHost { StartMode = _start };
         _host.ExitRequested += Shutdown;
         _host.Start();
diff --git a/src/NeoFences.App/ChooseCoverWindow.xaml.cs b/src/NeoFences.App/ChooseCoverWindow.xaml.cs
index d3ee86c..6d5855b 100644
--- a/src/NeoFences.App/ChooseCoverWindow.xaml.cs
+++ b/src/NeoFences.App/ChooseCoverWindow.xaml.cs
@@ -40,7 +40,7 @@ public partial class ChooseCoverWindow : Window
             Close();
         };
         if (online) Loaded += async (_, _) => await ShowResultsAsync(gameName);
-        else StatusText.Text = "Turn on \"Find covers and website icons online\" in Settings → Game Library to see covers from the Steam store.";
+        else StatusText.Text = "Turn on \"Find covers and website icons online\" in Settings → Games to see covers from the Steam store.";
     }
 
     private async Task ShowResultsAsync(string gameName)
diff --git a/src/NeoFences.App/ColourWindow.xaml b/src/NeoFences.App/ColourWindow.xaml
new file mode 100644
index 0000000..7d62bec
--- /dev/null
+++ b/src/NeoFences.App/ColourWindow.xaml
@@ -0,0 +1,61 @@
+<Window x:Class="NeoFences.App.ColourWindow"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="Custom colour" SizeToContent="WidthAndHeight" ResizeMode="NoResize"
+        WindowStartupLocation="CenterScreen" ShowInTaskbar="False" ThemeMode="System">
+    <!-- M35 (spec §3): a saturation/brightness field at the chosen hue, a hue bar, a hex box and a preview. -->
+    <StackPanel Margin="20,16,20,18" Width="280">
+        <Grid x:Name="Field" Height="160" Cursor="Cross" ClipToBounds="True" AutomationProperties.Name="Saturation and brightness">
+            <Border x:Name="HueFill" CornerRadius="6" Background="Red" />
+            <Border CornerRadius="6">
+                <Border.Background>
+                    <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
+                        <GradientStop Color="White" Offset="0" />
+                        <GradientStop Color="#00FFFFFF" Offset="1" />
+                    </LinearGradientBrush>
+                </Border.Background>
+            </Border>
+            <Border CornerRadius="6">
+                <Border.Background>
+                    <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
+                        <GradientStop Color="#00000000" Offset="0" />
+                        <GradientStop Color="Black" Offset="1" />
+                    </LinearGradientBrush>
+                </Border.Background>
+            </Border>
+            <Canvas>
+                <Ellipse x:Name="FieldThumb" Width="14" Height="14" Stroke="White" StrokeThickness="2" IsHitTestVisible="False">
+                    <Ellipse.Effect>
+                        <DropShadowEffect BlurRadius="3" ShadowDepth="0" Opacity="0.8" />
+                    </Ellipse.Effect>
+                </Ellipse>
+            </Canvas>
+        </Grid>
+        <Grid Margin="0,12,0,0" Height="20">
+            <Border CornerRadius="6" Height="12" VerticalAlignment="Center">
+                <Border.Background>
+                    <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
+                        <GradientStop Color="#FF0000" Offset="0" />
+                        <GradientStop Color="#FFFF00" Offset="0.1667" />
+                        <GradientStop Color="#00FF00" Offset="0.3333" />
+                        <GradientStop Color="#00FFFF" Offset="0.5" />
+                        <GradientStop Color="#0000FF" Offset="0.6667" />
+                        <GradientStop Color="#FF00FF" Offset="0.8333" />
+                        <GradientStop Color="#FF0000" Offset="1" />
+                    </LinearGradientBrush>
+                </Border.Background>
+            </Border>
+            <Slider x:Name="HueSlider" Minimum="0" Maximum="359" Opacity="0.85" VerticalAlignment="Center" AutomationProperties.Name="Hue"
+                    SmallChange="1" LargeChange="15" IsMoveToPointEnabled="True" />
+        </Grid>
+        <DockPanel Margin="0,14,0,0">
+            <Border x:Name="Preview" DockPanel.Dock="Left" Width="44" Height="32" CornerRadius="4" Margin="0,0,10,0"
+                    BorderBrush="{DynamicResource CardStrokeColorDefaultBrush}" BorderThickness="1" />
+            <TextBox x:Name="HexBox" VerticalContentAlignment="Center" AutomationProperties.Name="Colour as hex, like #E23A50" MaxLength="9" />
+        </DockPanel>
+        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,18,0,0">
+            <Button x:Name="OkButton" Content="OK" IsDefault="True" Style="{DynamicResource AccentButtonStyle}" MinWidth="80" Margin="0,0,8,0" />
+            <Button Content="Cancel" IsCancel="True" MinWidth="80" />
+        </StackPanel>
+    </StackPanel>
+</Window>
diff --git a/src/NeoFences.App/ColourWindow.xaml.cs b/src/NeoFences.App/ColourWindow.xaml.cs
new file mode 100644
index 0000000..5fafe43
--- /dev/null
+++ b/src/NeoFences.App/ColourWindow.xaml.cs
@@ -0,0 +1,73 @@
+using System.Windows;
+using System.Windows.Controls;
+using System.Windows.Input;
+using System.Windows.Media;
+using NeoFences.Core.Appearance;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Colour ▸ Custom colour… (M35, spec §3): replaces Windows' 1995 colour dialog. Drag in the field (saturation × brightness),
+/// slide the hue, or type a hex colour ("#E23A50", "e23a50", "#abc"); OK takes it, Cancel or Esc changes nothing.
+/// </summary>
+public partial class ColourWindow : Window
+{
+    private Hsv _colour;
+    private bool _updating;
+
+    private ColourWindow(Argb initial)
+    {
+        InitializeComponent();
+        _colour = Hsv.FromArgb(initial);
+        Field.MouseLeftButtonDown += (_, press) => { Field.CaptureMouse(); FromField(press.GetPosition(Field)); };
+        Field.MouseMove += (_, move) => { if (Field.IsMouseCaptured) FromField(move.GetPosition(Field)); };
+        Field.MouseLeftButtonUp += (_, _) => Field.ReleaseMouseCapture();
+        Field.SizeChanged += (_, _) => Show(updateHex: false);
+        HueSlider.ValueChanged += (_, _) =>
+        {
+            if (_updating) return;
+            _colour = _colour with { H = HueSlider.Value };
+            Show(updateHex: true);
+        };
+        HexBox.TextChanged += (_, _) =>
+        {
+            if (_updating || Argb.FromUserHex(HexBox.Text) is not { } typed) return;
+            _colour = Hsv.FromArgb(typed);
+            Show(updateHex: false);
+        };
+        OkButton.Click += (_, _) => DialogResult = true;
+        Loaded += (_, _) => { Show(updateHex: true); HexBox.Focus(); HexBox.SelectAll(); };
+    }
+
+    /// <summary>The picked colour as "#RRGGBB", or null when cancelled.</summary>
+    public static string? Pick(Window? owner, Argb? initial)
+    {
+        var window = new ColourWindow(initial ?? new Argb(0xFF, 0xE2, 0x3A, 0x50)) { Owner = owner };
+        if (owner is null) window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
+        return window.ShowDialog() == true ? window._colour.ToArgb().ToHex() : null;
+    }
+
+    private void FromField(Point point)
+    {
+        _colour = _colour with
+        {
+            S = Math.Clamp(point.X / Math.Max(1, Field.ActualWidth), 0, 1),
+            V = 1 - Math.Clamp(point.Y / Math.Max(1, Field.ActualHeight), 0, 1),
+        };
+        Show(updateHex: true);
+    }
+
+    private void Show(bool updateHex)
+    {
+        _updating = true;
+        var pure = new Hsv(_colour.H, 1, 1).ToArgb();
+        HueFill.Background = new SolidColorBrush(Color.FromRgb(pure.R, pure.G, pure.B));
+        var colour = _colour.ToArgb();
+        Preview.Background = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
+        HueSlider.Value = _colour.H;
+        Canvas.SetLeft(FieldThumb, _colour.S * Field.ActualWidth - FieldThumb.Width / 2);
+        Canvas.SetTop(FieldThumb, (1 - _colour.V) * Field.ActualHeight - FieldThumb.Height / 2);
+        if (updateHex) HexBox.Text = colour.ToHex();
+        _updating = false;
+    }
+}
diff --git a/src/NeoFences.App/DesktopFillWindow.xaml b/src/NeoFences.App/DesktopFillWindow.xaml
index b51997f..1f8cd1e 100644
--- a/src/NeoFences.App/DesktopFillWindow.xaml
+++ b/src/NeoFences.App/DesktopFillWindow.xaml
@@ -12,7 +12,7 @@
             <RowDefinition Height="Auto" />
         </Grid.RowDefinitions>
         <TextBlock TextWrapping="Wrap" Margin="0,0,0,12" Foreground="{DynamicResource TextFillColorSecondaryBrush}"
-                   Text="Each ticked item becomes a link in the fence its group goes to. Nothing on your desktop is moved or changed. Games missing? Add your games folder in Settings → Game Library, then open this again." />
+                   Text="Each ticked item becomes a link in the fence its group goes to. Nothing on your desktop is moved or changed. Games missing? Add your games folder in Settings → Games, then open this again." />
         <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
             <StackPanel x:Name="GroupsPanel" />
         </ScrollViewer>
diff --git a/src/NeoFences.App/FenceHost.Appearance.cs b/src/NeoFences.App/FenceHost.Appearance.cs
index 53554ef..c5010db 100644
--- a/src/NeoFences.App/FenceHost.Appearance.cs
+++ b/src/NeoFences.App/FenceHost.Appearance.cs
@@ -184,7 +184,7 @@ public sealed partial class FenceHost
         return new AppearanceView(Appearance, _lightTheme, accent?.ToHex(), origin);
     }
 
-    /// <summary>Fence menu → Colour → Custom… (M14): Windows' colour picker; Cancel changes nothing.</summary>
+    /// <summary>Fence menu → Colour → Custom colour… (M14, M35): the colour picker; Cancel changes nothing.</summary>
     private void PickCustomColour(FenceWindow window)
     {
         var fenceId = window.FenceId; // the dialog is modal to this fence only: the tab or the fence may change meanwhile (final review M4)
@@ -194,7 +194,7 @@ public sealed partial class FenceHost
         string? picked;
         try
         {
-            picked = ColorPicker.TryPick(window.Handle, current);
+            picked = ColourWindow.Pick(window, current); // M35: NeoFences' own picker (Windows' 1995 dialog is gone)
         }
         catch (Exception failure) when (failure is not OutOfMemoryException)
         {
diff --git a/src/NeoFences.App/FenceHost.Collect.cs b/src/NeoFences.App/FenceHost.Collect.cs
index b92b75a..15c4461 100644
--- a/src/NeoFences.App/FenceHost.Collect.cs
+++ b/src/NeoFences.App/FenceHost.Collect.cs
@@ -200,9 +200,11 @@ public sealed partial class FenceHost
         var lone = _config with { Fences = [.. _config.Fences.Where(fence => fence.Id == fenceId).Select(fence => fence with { Collect = [rule] })] };
         var plan = CollectRules.Plan(lone, _items, rule.Source, entries);
         if (plan.Count == 0) return;
-        var answer = MessageBox.Show(window, $"{plan.Count}{(plan.Count == CollectRules.MaxPerBurst ? " (the first)" : "")} item{(plan.Count == 1 ? "" : "s")} in {CollectRules.Summary(rule).Split(" · ")[0]} match this rule already.\n\nAdd these {plan.Count} too?\n\nYour files are not moved: the fence only shows them.",
-            "NeoFences — auto-collect", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
-        if (answer != MessageBoxResult.Yes) return;
+        // M35: the one message dialog instead of MessageBox.
+        var first = plan.Count == CollectRules.MaxPerBurst ? " (the first)" : "";
+        if (!MessageDialog.Ask(window, $"Add {plan.Count}{first} item{(plan.Count == 1 ? "" : "s")} already there?",
+                $"{plan.Count} item{(plan.Count == 1 ? "" : "s")} in {CollectRules.Summary(rule).Split(" · ")[0]} match this rule already. Your files are not moved: the fence only shows them.",
+                primary: "Add", secondary: "Not now")) return;
         _items = ItemEdits.Add(_items, fenceId, [.. plan.Select(entry => VirtualItem.Create(entry.Path))]).Document;
         Log.Information("auto-collect: {Count} existing item(s) added to fence {FenceId} by a new rule", plan.Count, fenceId);
         ItemsChanged(checkTargets: [.. plan.Select(entry => entry.Path)]);
diff --git a/src/NeoFences.App/FenceHost.Covers.cs b/src/NeoFences.App/FenceHost.Covers.cs
index c0206ca..8f8d7f3 100644
--- a/src/NeoFences.App/FenceHost.Covers.cs
+++ b/src/NeoFences.App/FenceHost.Covers.cs
@@ -183,7 +183,11 @@ public sealed partial class FenceHost
             .ContinueWith(download =>
             {
                 if (download.IsCompletedSuccessfully && download.Result) SetCoverChoice(game, choiceFile + ".jpg");
-                else Log.Warning("online art: the chosen cover for {Game} could not be downloaded", game.Name);
+                else
+                {
+                    Log.Warning("online art: the chosen cover for {Game} could not be downloaded", game.Name);
+                    MessageDialog.Tell(null, "The cover could not be downloaded", "The Steam store did not answer. Try again later, or choose a picture of your own."); // M35 (M34 minor M8)
+                }
             }, TaskScheduler.FromCurrentSynchronizationContext());
         window.FileChosen += picture =>
         {
@@ -240,7 +244,7 @@ public sealed partial class FenceHost
     /// <summary>Size ▸ for covers (owner's choice at planning): Normal (1×2) or Large (2×4, twice).</summary>
     private MenuItem CoverSizeMenu(IReadOnlyList<VirtualItem> items)
     {
-        var size = new MenuItem { Header = "Size" };
+        var size = MenuGlyph.Entry("Size", MenuGlyph.Size); // M35
         var spans = items.Select(FenceGrid.SpanOf).Distinct().ToList();
         foreach (var (name, span) in new[] { ("Normal", CoverSizes.Normal), ("Large (twice as big)", CoverSizes.Large) })
         {
diff --git a/src/NeoFences.App/FenceHost.FolderPanels.cs b/src/NeoFences.App/FenceHost.FolderPanels.cs
index 696191c..cbb2545 100644
--- a/src/NeoFences.App/FenceHost.FolderPanels.cs
+++ b/src/NeoFences.App/FenceHost.FolderPanels.cs
@@ -199,34 +199,30 @@ public sealed partial class FenceHost
     {
         var panel = item.Panel!;
         var menu = new ContextMenu();
-        MenuItem Command(ItemsControl parent, string header, Action run, bool? isChecked = null, bool enabled = true)
-        {
-            var command = new MenuItem { Header = header, IsChecked = isChecked == true, IsEnabled = enabled };
-            command.Click += (_, _) => run();
-            parent.Items.Add(command);
-            return command;
-        }
+        // M35 (spec §1): icons; Open folder first, the panel's own settings, Remove last in red.
+        void Command(ItemsControl parent, string header, string? glyph, Action run, bool? isChecked = null, bool enabled = true, bool danger = false) =>
+            parent.Items.Add(MenuGlyph.Entry(header, glyph, run, isChecked, danger, enabled));
         if (CheckOf(item.Target).State != TargetState.Ok)
         {
-            Command(menu, "Locate…", () => Locate(window, item.Id));
+            Command(menu, "Locate…", MenuGlyph.Missing, () => Locate(window, item.Id));
             menu.Items.Add(new Separator());
         }
-        var look = new MenuItem { Header = "Look" };
+        Command(menu, "Open folder", MenuGlyph.OpenFolder, () => OpenItem(ShownFolder(item), ownerHandle: window.Handle));
+        var look = MenuGlyph.Entry("Look", MenuGlyph.View);
         foreach (var (choice, name) in new[] { (PanelLook.Details, "Details"), (PanelLook.List, "List"), (PanelLook.Icons, "Icons") })
-            Command(look, name, () => SetPanel(item.Id, panel with { Look = choice }), isChecked: panel.Look == choice);
+            Command(look, name, null, () => SetPanel(item.Id, panel with { Look = choice }), isChecked: panel.Look == choice);
         menu.Items.Add(look);
-        var sort = new MenuItem { Header = "Sort by" };
+        var sort = MenuGlyph.Entry("Sort by", MenuGlyph.Sort);
         foreach (var (choice, name) in new[] { (PanelSort.Name, "Name"), (PanelSort.Date, "Date modified"), (PanelSort.Type, "Type"), (PanelSort.Size, "Size") })
-            Command(sort, name, () => SetPanel(item.Id, FolderPanels.HeaderSort(panel, choice)), isChecked: panel.Sort == choice);
+            Command(sort, name, null, () => SetPanel(item.Id, FolderPanels.HeaderSort(panel, choice)), isChecked: panel.Sort == choice);
         menu.Items.Add(sort);
-        Command(menu, "Panel settings…", () => EditPanel(window, item.Id));
-        Command(menu, "Open folder", () => OpenItem(ShownFolder(item), ownerHandle: window.Handle));
-        menu.Items.Add(new Separator());
+        Command(menu, "Panel settings…", MenuGlyph.Settings, () => EditPanel(window, item.Id));
         menu.Items.Add(SizeMenu(menu, [item]));
         var alone = _items.Of(window.FenceId) is [_];
-        Command(menu, "Fill fence", () => SetFill(item.Id, !item.Fill), isChecked: item.Fill && alone, enabled: alone);
-        Command(menu, "Show as icon", () => SetPanelShown(item.Id, null));
-        Command(menu, "Remove from fence", () => RemoveItems(window, [item.Id]));
+        Command(menu, "Fill fence", MenuGlyph.Layout, () => SetFill(item.Id, !item.Fill), isChecked: item.Fill && alone, enabled: alone);
+        Command(menu, "Show as icon", MenuGlyph.ShowAs, () => SetPanelShown(item.Id, null));
+        menu.Items.Add(new Separator());
+        Command(menu, "Remove from fence", MenuGlyph.Remove, () => RemoveItems(window, [item.Id]), danger: true);
         menu.Items.Add(new Separator());
         menu.Items.Add(new MenuItem { Header = "Shift+right-click on entries: Windows' menu", IsEnabled = false });
         window.ShowItemMenu(menu, fromKeyboard);
@@ -332,18 +328,13 @@ public sealed partial class FenceHost
             return;
         }
         var menu = new ContextMenu();
-        void Command(ItemsControl parent, string header, Action run)
-        {
-            var command = new MenuItem { Header = header };
-            command.Click += (_, _) => run();
-            parent.Items.Add(command);
-        }
-        Command(menu, "Open", () => { foreach (var path in paths) OpenItem(path, ownerHandle: window.Handle); });
-        if (paths.Count == 1) Command(menu, "Open file location", () => ShowInFolder(paths[0]));
-        Command(menu, paths.Count == 1 ? "Copy path" : "Copy paths", () => CopyText(string.Join(Environment.NewLine, paths)));
+        void Command(ItemsControl parent, string header, string? glyph, Action run) => parent.Items.Add(MenuGlyph.Entry(header, glyph, run)); // M35
+        Command(menu, "Open", MenuGlyph.Open, () => { foreach (var path in paths) OpenItem(path, ownerHandle: window.Handle); });
+        if (paths.Count == 1) Command(menu, "Open file location", MenuGlyph.OpenFolder, () => ShowInFolder(paths[0]));
+        Command(menu, paths.Count == 1 ? "Copy path" : "Copy paths", MenuGlyph.Copy, () => CopyText(string.Join(Environment.NewLine, paths)));
         var itemFences = _config.Fences.Where(fence => fence.Kind == FenceKind.Items).ToList();
-        var addTo = new MenuItem { Header = "Add to fence", IsEnabled = itemFences.Count > 0 };
-        foreach (var fence in itemFences) Command(addTo, fence.Title.Length > 0 ? fence.Title : "(untitled fence)", () => AddToFence(fence.Id, paths));
+        var addTo = MenuGlyph.Entry("Add to fence", MenuGlyph.Add, enabled: itemFences.Count > 0);
+        foreach (var fence in itemFences) Command(addTo, fence.Title.Length > 0 ? fence.Title : "(untitled fence)", null, () => AddToFence(fence.Id, paths));
         menu.Items.Add(addTo);
         menu.Items.Add(new Separator());
         menu.Items.Add(new MenuItem { Header = "Shift+right-click: Windows' menu", IsEnabled = false });
diff --git a/src/NeoFences.App/FenceHost.GameItems.cs b/src/NeoFences.App/FenceHost.GameItems.cs
index 4f34c8b..b7ecaf5 100644
--- a/src/NeoFences.App/FenceHost.GameItems.cs
+++ b/src/NeoFences.App/FenceHost.GameItems.cs
@@ -115,32 +115,23 @@ public sealed partial class FenceHost
     private void ShowGameItemMenu(FenceWindow window, VirtualItem item, bool fromKeyboard)
     {
         var menu = new ContextMenu();
-        void Command(ItemsControl parent, string header, Action run, bool? isChecked = null)
-        {
-            var command = new MenuItem { Header = header, IsChecked = isChecked == true };
-            command.Click += (_, _) => run();
-            parent.Items.Add(command);
-        }
+        // M35 (spec §1): icons; Open first, the game's own actions, Properties…, Remove last in red.
+        void Command(ItemsControl parent, string header, string? glyph, Action run, bool? isChecked = null, bool danger = false) =>
+            parent.Items.Add(MenuGlyph.Entry(header, glyph, run, isChecked, danger));
         var installed = CheckOf(item.Target).State == TargetState.Ok;
-        if (!installed)
-        {
-            Command(menu, "Remove from fence", () => RemoveItems(window, [item.Id]));
-            menu.Items.Add(new Separator());
-        }
-        Command(menu, "Open", () => OpenVirtualItem(window, item, runAsAdmin: false));
-        var showAs = new MenuItem { Header = "Show as" };
-        Command(showAs, "Cover tile", () => SetShowAs(item.Id, ItemShow.Cover), isChecked: GameItems.ShowsCover(item));
-        Command(showAs, "Icon", () => SetShowAs(item.Id, ItemShow.Icon), isChecked: !GameItems.ShowsCover(item));
+        Command(menu, "Open", MenuGlyph.Run, () => OpenVirtualItem(window, item, runAsAdmin: false));
+        var showAs = MenuGlyph.Entry("Show as", MenuGlyph.ShowAs);
+        Command(showAs, "Cover tile", null, () => SetShowAs(item.Id, ItemShow.Cover), isChecked: GameItems.ShowsCover(item));
+        Command(showAs, "Icon", null, () => SetShowAs(item.Id, ItemShow.Icon), isChecked: !GameItems.ShowsCover(item));
         menu.Items.Add(showAs);
-        if (GameItems.ShowsCover(item)) Command(menu, "Choose cover…", () => ChooseCover(item)); // M34
+        if (GameItems.ShowsCover(item)) Command(menu, "Choose cover…", MenuGlyph.Cover, () => ChooseCover(item)); // M34
         menu.Items.Add(GameItems.ShowsCover(item) ? CoverSizeMenu([item]) : SizeMenu(menu, [item])); // M24; M34: Normal / Large covers
-        var openFolder = new MenuItem { Header = "Open install folder", IsEnabled = installed && LibraryItemOf(item.Target)?.Game.InstallFolder is not null }; // M23
-        openFolder.Click += (_, _) => OpenInstallFolder(window, item.Target);
-        menu.Items.Add(openFolder);
-        Command(menu, "Copy path", () => CopyText(item.Target));
+        menu.Items.Add(MenuGlyph.Entry("Open install folder", MenuGlyph.OpenFolder, () => OpenInstallFolder(window, item.Target),
+            enabled: installed && LibraryItemOf(item.Target)?.Game.InstallFolder is not null)); // M23
+        Command(menu, "Copy path", MenuGlyph.Copy, () => CopyText(item.Target));
         menu.Items.Add(new Separator());
-        Command(menu, "Properties…", () => ShowProperties(window, item.Id, focusName: false));
-        if (installed) Command(menu, "Remove from fence", () => RemoveItems(window, [item.Id]));
+        Command(menu, "Properties…", MenuGlyph.Properties, () => ShowProperties(window, item.Id, focusName: false));
+        Command(menu, "Remove from fence", MenuGlyph.Remove, () => RemoveItems(window, [item.Id]), danger: true);
         menu.Items.Add(new Separator());
         menu.Items.Add(new MenuItem { Header = "Shift+right-click: Windows' menu", IsEnabled = false });
         window.ShowItemMenu(menu, fromKeyboard);
diff --git a/src/NeoFences.App/FenceHost.Grid.cs b/src/NeoFences.App/FenceHost.Grid.cs
index 952b815..4153cc1 100644
--- a/src/NeoFences.App/FenceHost.Grid.cs
+++ b/src/NeoFences.App/FenceHost.Grid.cs
@@ -13,8 +13,12 @@ namespace NeoFences.App;
 public sealed partial class FenceHost
 {
     /// <summary>The Size ▸ entry for these items (an items fence only).</summary>
-    private MenuItem SizeMenu(ContextMenu menu, IReadOnlyList<VirtualItem> items) =>
-        SizePicker.Create(menu, FenceGrid.CommonSize([.. items.Select(item => item.Size)]), size => SetSize([.. items.Select(item => item.Id)], size)); // M28: mixed sizes check nothing
+    private MenuItem SizeMenu(ContextMenu menu, IReadOnlyList<VirtualItem> items)
+    {
+        var size = SizePicker.Create(menu, FenceGrid.CommonSize([.. items.Select(item => item.Size)]), size => SetSize([.. items.Select(item => item.Id)], size)); // M28: mixed sizes check nothing
+        MenuGlyph.SetGlyph(size, MenuGlyph.Size); // M35
+        return size;
+    }
 
     private void SetSize(IReadOnlyList<string> itemIds, GridSpan? size)
     {
diff --git a/src/NeoFences.App/FenceHost.Items.cs b/src/NeoFences.App/FenceHost.Items.cs
index abcba08..ef7c966 100644
--- a/src/NeoFences.App/FenceHost.Items.cs
+++ b/src/NeoFences.App/FenceHost.Items.cs
@@ -100,17 +100,14 @@ public sealed partial class FenceHost
             return;
         }
         var menu = new ContextMenu();
-        void Command(string header, Action run)
-        {
-            var command = new MenuItem { Header = header };
-            command.Click += (_, _) => run();
-            menu.Items.Add(command);
-        }
+        // M35 (spec §1): icons; Open first, then the item's own actions, Properties…, Remove last in red.
+        void Command(string header, string glyph, Action run, bool danger = false) => menu.Items.Add(MenuGlyph.Entry(header, glyph, run, danger: danger));
         if (items.Count > 1)
         {
-            Command("Open", () => { foreach (var item in items) OpenKey(window, item.Id); }); // a widget opens its own app, never ShellExecute on its target (M25 review I2)
+            Command("Open", MenuGlyph.Open, () => { foreach (var item in items) OpenKey(window, item.Id); }); // a widget opens its own app, never ShellExecute on its target (M25 review I2)
             menu.Items.Add(items.All(GameItems.ShowsCover) ? CoverSizeMenu(items) : SizeMenu(menu, items)); // M24; M34: covers' own sizes
-            Command($"Remove {items.Count} items from fence", () => RemoveItems(window, [.. items.Select(item => item.Id)]));
+            menu.Items.Add(new Separator());
+            Command($"Remove {items.Count} items from fence", MenuGlyph.Remove, () => RemoveItems(window, [.. items.Select(item => item.Id)]), danger: true);
         }
         else if (FolderPanels.IsPanel(items[0]))
         {
@@ -134,19 +131,18 @@ public sealed partial class FenceHost
             var onDisk = item.Kind == ItemKind.Path && TargetChecks.RootOf(item.Target) is not null;
             if (check.State != TargetState.Ok)
             {
-                Command("Locate…", () => Locate(window, item.Id));
-                Command("Remove from fence", () => RemoveItems(window, [item.Id]));
+                Command("Locate…", MenuGlyph.Missing, () => Locate(window, item.Id)); // a missing item's first step
                 menu.Items.Add(new Separator());
             }
-            Command("Open", () => OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin));
-            if (onDisk && !check.IsFolder) Command("Run as administrator", () => OpenVirtualItem(window, item, runAsAdmin: true));
-            if (onDisk) Command("Open file location", () => ShowInFolder(item.Target));
-            if (onDisk && check.IsFolder && check.State == TargetState.Ok) Command("Show as folder panel", () => SetPanelShown(item.Id, FolderPanels.Create(item.Target, BusyFolders).Panel)); // M26
-            Command("Copy path", () => CopyText(item.Target));
-            menu.Items.Add(new Separator());
+            Command("Open", MenuGlyph.Open, () => OpenVirtualItem(window, item, runAsAdmin: item.RunAsAdmin));
+            if (onDisk && !check.IsFolder) Command("Run as administrator", MenuGlyph.Admin, () => OpenVirtualItem(window, item, runAsAdmin: true));
+            if (onDisk) Command("Open file location", MenuGlyph.OpenFolder, () => ShowInFolder(item.Target));
+            if (onDisk && check.IsFolder && check.State == TargetState.Ok) Command("Show as folder panel", MenuGlyph.Folder, () => SetPanelShown(item.Id, FolderPanels.Create(item.Target, BusyFolders).Panel)); // M26
+            Command("Copy path", MenuGlyph.Copy, () => CopyText(item.Target));
             menu.Items.Add(SizeMenu(menu, [item])); // M24
-            Command("Properties…", () => ShowProperties(window, item.Id, focusName: false));
-            if (check.State == TargetState.Ok) Command("Remove from fence", () => RemoveItems(window, [item.Id]));
+            menu.Items.Add(new Separator());
+            Command("Properties…", MenuGlyph.Properties, () => ShowProperties(window, item.Id, focusName: false));
+            Command("Remove from fence", MenuGlyph.Remove, () => RemoveItems(window, [item.Id]), danger: true);
             menu.Items.Add(new Separator());
             menu.Items.Add(new MenuItem { Header = "Shift+right-click: Windows' menu", IsEnabled = false });
         }
@@ -382,7 +378,7 @@ public sealed partial class FenceHost
         catch (Exception failure) when (failure is not OutOfMemoryException)
         {
             Log.Warning(failure, "picture {Picture} could not be used as an icon", picture);
-            MessageBox.Show(owner, $"NeoFences could not use this picture as the icon:\n{picture}", "NeoFences", MessageBoxButton.OK, MessageBoxImage.Warning);
+            MessageDialog.Tell(owner, "This picture cannot be used as an icon", picture); // M35
             return item;
         }
     }
diff --git a/src/NeoFences.App/FenceHost.Library.cs b/src/NeoFences.App/FenceHost.Library.cs
index 1d552d2..a3d4234 100644
--- a/src/NeoFences.App/FenceHost.Library.cs
+++ b/src/NeoFences.App/FenceHost.Library.cs
@@ -314,7 +314,7 @@ public sealed partial class FenceHost
         Folders: _config.Library.Folders,
         Sources: _config.Library.Sources,
         Hidden: HiddenGamesForSettings(),
-        Status: LibraryWanted ? _libraryStatus : "No games in any fence yet: fence menu → Add games…, or choose where new games go.",
+        Status: LibraryWanted ? _libraryStatus : "No games in any fence yet: fence menu → Add → Games…, or choose where new games go.",
         Fences: [.. _config.Fences.Where(fence => fence.Kind == FenceKind.Items).Select(fence => (fence.Id, fence.Title))],
         NewGamesFence: _config.Library.NewGamesFence,
         OnlineArt: _config.Library.OnlineArt == true); // M34
diff --git a/src/NeoFences.App/FenceHost.Widgets.cs b/src/NeoFences.App/FenceHost.Widgets.cs
index 1789609..6864185 100644
--- a/src/NeoFences.App/FenceHost.Widgets.cs
+++ b/src/NeoFences.App/FenceHost.Widgets.cs
@@ -123,28 +123,25 @@ public sealed partial class FenceHost
     private void ShowWidgetMenu(FenceWindow window, VirtualItem item, bool fromKeyboard)
     {
         var menu = new ContextMenu();
-        void Command(string header, Action run, bool? isChecked = null)
-        {
-            var command = new MenuItem { Header = header, IsChecked = isChecked == true };
-            command.Click += (_, _) => run();
-            menu.Items.Add(command);
-        }
+        void Command(string header, string? glyph, Action run, bool? isChecked = null, bool danger = false) =>
+            menu.Items.Add(MenuGlyph.Entry(header, glyph, run, isChecked, danger)); // M35
         if (Widgets.Of(item.Target) == WidgetKind.Clock)
         {
             var options = item.Widget ?? new WidgetOptions();
-            Command("Show seconds", () => SetWidgetOptions(item.Id, options with { Seconds = !options.Seconds }), isChecked: options.Seconds);
-            Command("Show date", () => SetWidgetOptions(item.Id, options with { Date = !options.Date }), isChecked: options.Date);
+            Command("Show seconds", null, () => SetWidgetOptions(item.Id, options with { Seconds = !options.Seconds }), isChecked: options.Seconds);
+            Command("Show date", null, () => SetWidgetOptions(item.Id, options with { Date = !options.Date }), isChecked: options.Date);
             menu.Items.Add(new Separator());
         }
         if (Widgets.Of(item.Target) == WidgetKind.Stats) // M31
         {
             var options = item.Widget ?? new WidgetOptions();
-            Command("Temperature in °F", () => SetWidgetOptions(item.Id, options with { Fahrenheit = !options.Fahrenheit }), isChecked: options.Fahrenheit);
+            Command("Temperature in °F", null, () => SetWidgetOptions(item.Id, options with { Fahrenheit = !options.Fahrenheit }), isChecked: options.Fahrenheit);
             menu.Items.Add(new Separator());
         }
         menu.Items.Add(SizeMenu(menu, [item]));
-        Command("Properties…", () => ShowProperties(window, item.Id, focusName: false));
-        Command("Remove from fence", () => RemoveItems(window, [item.Id]));
+        menu.Items.Add(new Separator());
+        Command("Properties…", MenuGlyph.Properties, () => ShowProperties(window, item.Id, focusName: false));
+        Command("Remove from fence", MenuGlyph.Remove, () => RemoveItems(window, [item.Id]), danger: true);
         window.ShowItemMenu(menu, fromKeyboard);
     }
 
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index 0ad9db3..27fff7a 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -290,6 +290,13 @@ public sealed partial class FenceHost
             ScheduleSave();
         };
         window.CustomColorRequested += () => PickCustomColour(window); // M14
+        window.AccentColorRequested += () => // M35: the fence takes Windows' current accent as its colour
+        {
+            var accent = System.Windows.SystemColors.AccentColor;
+            _config = FenceEdits.SetCustomColor(_config, window.FenceId, new NeoFences.Core.Appearance.Argb(0xFF, accent.R, accent.G, accent.B).ToHex());
+            RefreshTabs(window);
+            ScheduleSave();
+        };
         window.DetachTabRequested += () => DetachTab(window, window.FenceId, dropPoint: null);
         window.TabCycleRequested += step => CycleTab(window, step);
         window.SetTabs(FenceTabs.TabsOf(_config, box.Id), shown.Id);
@@ -301,7 +308,6 @@ public sealed partial class FenceHost
         window.LockToggled += locked => SetFenceLocked(window, locked);
         window.DeleteRequested += () => DeleteFence(window);
         window.NewFenceRequested += CreateFence;
-        window.ExitRequested += () => ExitRequested?.Invoke();
         window.OpenRequested += key => OpenKey(window, key);
         window.OpenManyRequested += keys =>
         {
@@ -325,11 +331,9 @@ public sealed partial class FenceHost
         window.LayoutRequested += layout => SetFenceLayout(window, layout); // M24
         window.AddWidgetRequested += kind => AddWidget(window, kind); // M25
         window.ItemsShownChanged += OnWidgetTick; // a rolled-up fence opened: its widgets show the right time at once
-        window.StartupToggled += SetStartWithWindows;
         window.SettingsRequested += OpenSettings;
         window.LabelModeRequested += labels => SetFenceLabels(window, labels);
         window.SetShortcutArrows(_config.Settings.ShowShortcutArrows);
-        window.SetStartupChecked(_config.Settings.StartWithWindows);
         window.SortRequested += sort => SortFence(window, sort);
         window.DragRequested += keys => DragItems(window, keys);
         window.RollUpToggled += () => ToggleRollUp(window);
@@ -553,9 +557,8 @@ public sealed partial class FenceHost
         var now = DateTimeOffset.Now;
         var saved = _snapshots.Save(Snapshots.Take(_config, _items, name: $"Before deleting {fence.Title} ({now:d MMM HH:mm})", now: now)) is not null;
         if (!saved) Log.Warning(_snapshots.LastFailure, "the snapshot before deleting a fence could not be saved; asking instead");
-        if (!saved && count > 0 && MessageBox.Show(window,
-                $"Delete \"{fence.Title}\" and its {count} item{(count == 1 ? "" : "s")}?\n\nYour files, folders and apps are not touched.",
-                "NeoFences", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
+        if (!saved && count > 0 && !MessageDialog.Ask(window, $"Delete \"{fence.Title}\" and its {count} item{(count == 1 ? "" : "s")}?", // M35
+                "The snapshot that lets you undo this could not be saved. Your files, folders and apps are not touched.", primary: "Delete", secondary: "Cancel")) return;
         _undo = Undo.ForDeletion(_config, _items, fence.Id); // M33
         _undoDeleteUntil = now.AddMinutes(2);
         _trayIcon?.ShowBalloon("Fence deleted", $"\"{fence.Title}\" is gone. Press Ctrl+Z in a fence, or tray → Undo delete, to bring it back."); // M33
@@ -584,6 +587,7 @@ public sealed partial class FenceHost
         _lightTheme = light;
         Log.Information("Windows app mode changed; light: {Light}", light);
         foreach (var window in _windows.Values) window.ApplyTheme(light);
+        MenuTheme.Apply(System.Windows.Application.Current.Resources, light); // M35: the menus follow too
         RestyleAll(); // M14: the tone's strength and ink
         RefreshSettings();
     }
@@ -734,7 +738,6 @@ public sealed partial class FenceHost
     {
         _config = _config with { Settings = _config.Settings with { StartWithWindows = startWithWindows } };
         ApplyStartup();
-        foreach (var window in _windows.Values) window.SetStartupChecked(startWithWindows);
         SaveNow();
         RefreshSettings();
     }
@@ -1298,7 +1301,9 @@ public sealed partial class FenceHost
         }
         if (restoreItems.Count > 0) restoreItems.Add(TrayMenuItem.Separator);
         restoreItems.Add(new TrayMenuItem(TraySnapshotsSettings, "More in Settings…"));
-        var chosen = TrayMenu.Show(_messages.Handle,
+        // M35 (ADR-056): NeoFences' own menu, like the fence and item menus; foreground first so a click elsewhere closes it.
+        TrayMenu.BringForward(_messages.Handle);
+        TrayMenuView.Show(
         [
             .. SafetyTrayItems(), // M33: "Leave safe mode", "Undo delete" first while they apply
             .. UpdateTrayItems(), // M17: "Restart to update to v…" first while an update waits
@@ -1316,7 +1321,10 @@ public sealed partial class FenceHost
             new TrayMenuItem(TrayPause, "Pause NeoFences", Checked: _paused),
             TrayMenuItem.Separator,
             new TrayMenuItem(TrayExit, "Exit NeoFences"),
-        ], screenX, screenY);
+        ], TrayMenuPoint(screenX, screenY), TrayGlyph, OnTrayChosen);
+
+        void OnTrayChosen(int chosen)
+        {
         switch (chosen)
         {
             case TrayNewFence:
@@ -1349,6 +1357,24 @@ public sealed partial class FenceHost
                 break;
         }
     }
+    }
+
+    /// <summary>The pointer in WPF units (M35): screen pixels over the fences' scale (one scale is enough for the tray's monitor here).</summary>
+    private Point TrayMenuPoint(int screenX, int screenY)
+    {
+        // ponytail: the first fence's DPI; a tray on a monitor with another scale lands a little off, per-monitor if it matters.
+        var scale = _windows.Values.FirstOrDefault() is { } window ? System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX : 1.0;
+        return new Point(screenX / scale, screenY / scale);
+    }
+
+    private static string? TrayGlyph(int id) => id switch
+    {
+        TrayNewFence => MenuGlyph.NewFence, TrayNewFolderPanel => MenuGlyph.Folder, TrayAddFromDesktop => MenuGlyph.Desktop,
+        TrayQuickHide => MenuGlyph.Hide, TrayPeek => MenuGlyph.Peek, TrayTakeSnapshot => MenuGlyph.Snapshot, TrayRestoreMenu => MenuGlyph.Restore,
+        TrayHelp => MenuGlyph.Help, TraySettings => MenuGlyph.Settings, TrayPause => MenuGlyph.Pause, TrayExit => MenuGlyph.Exit,
+        TrayUndoDelete => MenuGlyph.Undo, TrayLeaveSafeMode => MenuGlyph.SafeMode, TrayRestartToUpdate => MenuGlyph.Refresh,
+        _ => null,
+    };
 
     private void OnDesktopGesture(DesktopGesture gesture, int screenX, int screenY)
     {
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index 4bc4260..2d0bba4 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -216,46 +216,52 @@
                      BorderBrush="{DynamicResource FenceBorder}" CaretBrush="{DynamicResource FenceText}" />
             <Border x:Name="Body" Grid.Row="2" BorderBrush="{DynamicResource FenceDivider}" BorderThickness="0,1,0,0" Background="#01000000">
                 <Border.ContextMenu>
+                    <!-- M35 (spec §1): 12 lines, grouped, with Windows 11 icons; Start with Windows lives in Settings, Exit in the tray. -->
                     <ContextMenu x:Name="BodyContextMenu">
-                        <MenuItem x:Name="AddItemItem" Header="Add item…" />
-                        <MenuItem x:Name="AddFromDesktopItem" Header="Add from desktop…" />
-                        <MenuItem x:Name="AddGamesItem" Header="Add games…" />
-                        <MenuItem x:Name="AddWidgetItem" Header="Add widget">
-                            <MenuItem x:Name="AddClockItem" Header="Clock" />
-                            <MenuItem x:Name="AddDateItem" Header="Date" />
-                            <MenuItem x:Name="AddStatsItem" Header="System stats" />
+                        <MenuItem x:Name="AddMenu" Header="Add" local:MenuGlyph.Glyph="&#xE710;">
+                            <MenuItem x:Name="AddItemItem" Header="Item…" local:MenuGlyph.Glyph="&#xE8E5;" />
+                            <MenuItem x:Name="AddFromDesktopItem" Header="From desktop…" local:MenuGlyph.Glyph="&#xE8FC;" />
+                            <MenuItem x:Name="AddGamesItem" Header="Games…" local:MenuGlyph.Glyph="&#xE7FC;" />
+                            <!-- M26: a folder inside the fence, beside its other elements. -->
+                            <MenuItem x:Name="AddFolderPanelItem" Header="Folder panel…" local:MenuGlyph.Glyph="&#xE8B7;" />
+                            <MenuItem x:Name="AddWidgetItem" Header="Widget" local:MenuGlyph.Glyph="&#xE9F9;">
+                                <MenuItem x:Name="AddClockItem" Header="Clock" />
+                                <MenuItem x:Name="AddDateItem" Header="Date" />
+                                <MenuItem x:Name="AddStatsItem" Header="System stats" />
+                            </MenuItem>
+                        </MenuItem>
+                        <MenuItem x:Name="ViewMenu" Header="View" local:MenuGlyph.Glyph="&#xE8B3;">
+                            <MenuItem x:Name="IconSizeItem" Header="Icon size" local:MenuGlyph.Glyph="&#xE740;" />
+                            <MenuItem x:Name="LabelsItem" Header="Labels" local:MenuGlyph.Glyph="&#xE8D2;">
+                                <MenuItem x:Name="LabelsAlwaysItem" Header="Always" IsCheckable="True" />
+                                <MenuItem x:Name="LabelsOnHoverItem" Header="On hover (icons only)" IsCheckable="True" />
+                            </MenuItem>
+                            <MenuItem x:Name="SortItem" Header="Sort by" local:MenuGlyph.Glyph="&#xE8CB;" />
+                            <MenuItem x:Name="LayoutItem" Header="Layout" local:MenuGlyph.Glyph="&#xE80A;">
+                                <MenuItem x:Name="LayoutFlowItem" Header="Flow (packed)" IsCheckable="True" />
+                                <MenuItem x:Name="LayoutFreeItem" Header="Free (fixed positions)" IsCheckable="True" />
+                            </MenuItem>
                         </MenuItem>
-                        <!-- M26: a folder inside the fence, beside its other elements. -->
-                        <MenuItem x:Name="AddFolderPanelItem" Header="Add folder panel…" />
                         <!-- M27: new matching files of a folder become items here. -->
-                        <MenuItem x:Name="AutoCollectItem" Header="Auto-collect…" />
-                        <MenuItem x:Name="RefreshItem" Header="Refresh" />
+                        <MenuItem x:Name="AutoCollectItem" Header="Auto-collect…" local:MenuGlyph.Glyph="&#xE895;" />
                         <Separator />
-                        <MenuItem x:Name="NewFenceItem" Header="New fence" />
-                        <!-- M22: games are items in any fence; the library fence kind stays in the code, not in the menu. -->
-                        <MenuItem x:Name="NewLibraryItem" Header="New Game Library fence" Visibility="Collapsed" />
-                        <!-- M26: a new fence holding one panel that fills it (folder views became panels). -->
-                        <MenuItem x:Name="NewFolderPanelItem" Header="New folder panel…" />
-                        <MenuItem x:Name="RenameItem" Header="Rename fence" />
-                        <MenuItem x:Name="IconSizeItem" Header="Icon size" />
-                        <MenuItem x:Name="LabelsItem" Header="Labels">
-                            <MenuItem x:Name="LabelsAlwaysItem" Header="Always" IsCheckable="True" />
-                            <MenuItem x:Name="LabelsOnHoverItem" Header="On hover (icons only)" IsCheckable="True" />
-                        </MenuItem>
-                        <MenuItem x:Name="SortItem" Header="Sort by" />
-                        <MenuItem x:Name="LayoutItem" Header="Layout">
-                            <MenuItem x:Name="LayoutFlowItem" Header="Flow (packed)" IsCheckable="True" />
-                            <MenuItem x:Name="LayoutFreeItem" Header="Free (fixed positions)" IsCheckable="True" />
-                        </MenuItem>
-                        <MenuItem x:Name="TabColorItem" Header="Colour" />
-                        <MenuItem x:Name="DetachTabItem" Header="Detach tab" Visibility="Collapsed" />
-                        <MenuItem x:Name="LockItem" Header="Lock position" IsCheckable="True" />
-                        <MenuItem x:Name="DeleteItem" Header="Delete fence (your files are not touched)" />
+                        <MenuItem x:Name="RenameItem" Header="Rename" InputGestureText="F2" local:MenuGlyph.Glyph="&#xE8AC;" />
+                        <!-- M35 (spec §3): swatches inside the menu, the accent, a custom colour (built in code). -->
+                        <MenuItem x:Name="TabColorItem" Header="Colour" local:MenuGlyph.Glyph="&#xE790;" />
+                        <MenuItem x:Name="LockItem" Header="Lock position" IsCheckable="True" local:MenuGlyph.Glyph="&#xE72E;" />
+                        <MenuItem x:Name="RefreshItem" Header="Refresh" local:MenuGlyph.Glyph="&#xE72C;" />
                         <Separator />
-                        <MenuItem x:Name="StartupItem" Header="Start with Windows" IsCheckable="True" />
-                        <MenuItem x:Name="SettingsItem" Header="Settings…" />
+                        <MenuItem x:Name="NewFenceMenu" Header="New fence" local:MenuGlyph.Glyph="&#xE8A0;">
+                            <MenuItem x:Name="NewFenceItem" Header="Empty" />
+                            <!-- M26: a new fence holding one panel that fills it (folder views became panels). -->
+                            <MenuItem x:Name="NewFolderPanelItem" Header="Folder panel…" />
+                            <!-- M22: games are items in any fence; the library fence kind stays in the code, not in the menu. -->
+                            <MenuItem x:Name="NewLibraryItem" Header="Game Library fence" Visibility="Collapsed" />
+                        </MenuItem>
+                        <MenuItem x:Name="SettingsItem" Header="Settings…" local:MenuGlyph.Glyph="&#xE713;" />
                         <Separator />
-                        <MenuItem x:Name="ExitItem" Header="Exit NeoFences" />
+                        <MenuItem x:Name="DetachTabItem" Header="Detach tab" Visibility="Collapsed" local:MenuGlyph.Glyph="&#xE8A7;" />
+                        <MenuItem x:Name="DeleteItem" Header="Delete fence" local:MenuGlyph.Glyph="&#xE74D;" local:MenuGlyph.Danger="True" />
                     </ContextMenu>
                 </Border.ContextMenu>
                 <Grid>
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index 424ed23..1fe3ac8 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -45,7 +45,7 @@ public partial class FenceWindow : Window
     private bool _renaming;
     private string _title = "";
     private FenceKind _kind; // the shown tab: items, the Game Library (M12: tiles, its own menu) or a folder view (M21: read-only)
-    private const string ItemsHint = "Drop files, folders or links here — or right-click → Add item…";
+    private const string ItemsHint = "Drop files, folders or links here — or right-click → Add → Item…";
     private bool _welcome; // M30: the first fence of a fresh start (ADR-051)
     private readonly System.Windows.Threading.DispatcherTimer _undoTimer = new() { Interval = TimeSpan.FromSeconds(10) }; // M33
     private IReadOnlyDictionary<string, (string Path, bool IsPoster)> _libraryArt = new Dictionary<string, (string, bool)>();
@@ -79,6 +79,8 @@ public partial class FenceWindow : Window
     public event Action<TabColor?>? TabColorRequested;
     /// <summary>Fence menu → Colour → Custom… (M14): the host opens Windows' colour picker.</summary>
     public event Action? CustomColorRequested;
+    /// <summary>Colour ▸ Use my accent colour (M35): the fence takes Windows' current accent as its colour.</summary>
+    public event Action? AccentColorRequested;
     public event Action? DetachTabRequested;
     /// <summary>Ctrl+Tab (+1) / Ctrl+Shift+Tab (-1).</summary>
     public event Action<int>? TabCycleRequested;
@@ -124,7 +126,6 @@ public partial class FenceWindow : Window
     public event Action<FenceWindow, PixelRect>? MovedByUser;
 
     public event Action? NewFenceRequested;
-    public event Action? ExitRequested;
     /// <summary>Double-click or Enter on one item (its key).</summary>
     public event Action<string>? OpenRequested;
     /// <summary>Enter with several items selected: open each.</summary>
@@ -170,8 +171,6 @@ public partial class FenceWindow : Window
     public event Action<string, PanelCommand>? PanelCommandRequested;
     /// <summary>A drive arrived or was removed (Windows tells top-level windows): missing and unavailable items are checked again.</summary>
     public event Action? DrivesChanged;
-    /// <summary>The "Start with Windows" toggle changed (ADR-019).</summary>
-    public event Action<bool>? StartupToggled;
     /// <summary>"Settings…" in the fence menu (M6b).</summary>
     public event Action? SettingsRequested;
     /// <summary>Double-click on the title: roll up to the title bar, or back down (M5).</summary>
@@ -202,20 +201,7 @@ public partial class FenceWindow : Window
             sortItem.Click += (_, _) => SortRequested?.Invoke(sort);
             SortItem.Items.Add(sortItem);
         }
-        var noColor = new MenuItem { Header = "None", IsCheckable = true };
-        noColor.Click += (_, _) => TabColorRequested?.Invoke(null);
-        TabColorItem.Items.Add(noColor);
-        foreach (var (color, value) in TabColors)
-        {
-            var colorItem = new MenuItem
-            {
-                Header = color.ToString(), Tag = color, IsCheckable = true,
-                Icon = new Rectangle { Width = 12, Height = 12, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(value) },
-            };
-            colorItem.Click += (_, _) => TabColorRequested?.Invoke(color);
-            TabColorItem.Items.Add(colorItem);
-        }
-        BuildCustomColourMenu();
+        BuildColourMenu(); // M35
         DetachTabItem.Click += (_, _) => DetachTabRequested?.Invoke();
         TitleBar.SizeChanged += (_, _) => UpdateTabStripWidth();
         PreviewKeyDown += OnTabKeys;
@@ -248,8 +234,6 @@ public partial class FenceWindow : Window
         RenameItem.Click += (_, _) => BeginRename();
         LockItem.Click += (_, _) => LockToggled?.Invoke(LockItem.IsChecked);
         DeleteItem.Click += (_, _) => DeleteRequested?.Invoke();
-        ExitItem.Click += (_, _) => ExitRequested?.Invoke();
-        StartupItem.Click += (_, _) => StartupToggled?.Invoke(StartupItem.IsChecked);
         SettingsItem.Click += (_, _) => SettingsRequested?.Invoke();
         LabelsAlwaysItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.Always);
         LabelsOnHoverItem.Click += (_, _) => LabelModeRequested?.Invoke(LabelMode.OnHover);
@@ -309,8 +293,6 @@ public partial class FenceWindow : Window
         };
     }
 
-    public void SetStartupChecked(bool startWithWindows) => StartupItem.IsChecked = startWithWindows;
-
     /// <summary>Everything the window shows of one fence: title, menu state, icon size, labels.</summary>
     private void ApplyFence(Fence fence)
     {
@@ -336,11 +318,11 @@ public partial class FenceWindow : Window
         AutoCollectItem.Header = fence.Collect.Count switch { 0 => "Auto-collect…", 1 => "Auto-collect… (1 rule)", var count => $"Auto-collect… ({count} rules)" }; // M27
         SortItem.Visibility = _kind == FenceKind.Library ? Visibility.Collapsed : Visibility.Visible;
         foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = view && Equals(sortItem.Tag, fence.View!.Sort);
-        DeleteItem.Header = _kind switch
+        DeleteItem.ToolTip = _kind switch // M35: the short entry; the reassurance as its tooltip
         {
-            FenceKind.Library => "Delete fence (your games are not touched)",
-            FenceKind.View => "Delete fence (the folder is not touched)",
-            _ => "Delete fence (your files are not touched)",
+            FenceKind.Library => "Your games are not touched",
+            FenceKind.View => "The folder is not touched",
+            _ => "Your files are not touched",
         };
         UpdateEmptyHint();
         _labelMode = fence.Labels;
@@ -433,12 +415,9 @@ public partial class FenceWindow : Window
         if (many) foreach (var tab in _tabs) TabStrip.Children.Add(BuildTabHeader(tab, isActive: tab.Id == active.Id));
         ShowTitleOrTabs();
         DetachTabItem.Visibility = many ? Visibility.Visible : Visibility.Collapsed;
-        RenameItem.Header = many ? "Rename tab" : "Rename fence";
+        RenameItem.Header = many ? "Rename tab" : "Rename";
         // The bar under a single fence's title comes from its look (ApplyStyle, M14); the menu shows the fence's choice.
-        foreach (var colorItem in TabColorItem.Items.OfType<MenuItem>())
-        {
-            colorItem.IsChecked = active.CustomColor is not null ? Equals(colorItem.Tag, CustomColourTag) : Equals(colorItem.Tag, active.TabColor);
-        }
+        ShowColourChoice(active); // M35: the ring on the current swatch, the tick on Custom colour…
         if (_style is { } style) ApplyStyle(style); // headers were rebuilt: their font and the bar follow the look again
         UpdateTabStripWidth();
     }
@@ -1249,7 +1228,6 @@ public partial class FenceWindow : Window
     }
 
     private FenceStyle? _style;
-    private const string CustomColourTag = "custom";
 
     /// <summary>
     /// The fence's look (M14, spec §3): veil, outline, title ink, title strip, colour bar, title font and the title row's
@@ -1306,12 +1284,85 @@ public partial class FenceWindow : Window
     };
 
     /// <summary>Colour → Custom… (M14): Windows' colour picker.</summary>
-    private void BuildCustomColourMenu()
+    private readonly List<(TabColor? Colour, Border Ring)> _swatches = [];
+    private MenuItem? _customColourItem;
+
+    /// <summary>
+    /// Colour ▸ (M35, spec §3): round swatches inside the menu — None and the fence colours, the current one ringed — set with
+    /// one click (or Left / Right and Enter); "Use my accent colour"; "Custom colour…".
+    /// </summary>
+    private void BuildColourMenu()
     {
+        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4), MaxWidth = 6 * 32 };
+        var keyIndex = 0;
+        void Pick(TabColor? colour)
+        {
+            BodyContextMenu.IsOpen = false;
+            TabColorRequested?.Invoke(colour);
+        }
+        void Highlight(int index)
+        {
+            for (var swatch = 0; swatch < _swatches.Count; swatch++) _swatches[swatch].Ring.Opacity = swatch == index || Equals(_swatches[swatch].Ring.Tag, true) ? 1 : 0;
+        }
+        foreach (var colour in new TabColor?[] { null }.Concat(TabColors.Keys.Select(key => (TabColor?)key)))
+        {
+            var dot = new Border
+            {
+                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1),
+                BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80)),
+                Background = colour is { } swatch ? new SolidColorBrush(TabColors[swatch]) : Brushes.Transparent,
+                Child = colour is null ? new TextBlock { Text = MenuGlyph.Remove, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10,
+                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 } : null,
+            };
+            var ring = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2), Opacity = 0 };
+            ring.SetResourceReference(Border.BorderBrushProperty, "MenuText");
+            var cell = new Grid { Width = 30, Height = 30, Margin = new Thickness(1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
+            cell.Children.Add(dot);
+            cell.Children.Add(ring);
+            System.Windows.Automation.AutomationProperties.SetName(cell, colour?.ToString() ?? "No colour");
+            cell.ToolTip = colour?.ToString() ?? "No colour";
+            var index = _swatches.Count;
+            cell.MouseEnter += (_, _) => { keyIndex = index; Highlight(index); };
+            cell.MouseLeftButtonUp += (_, click) => { click.Handled = true; Pick(colour); };
+            _swatches.Add((colour, ring));
+            row.Children.Add(cell);
+        }
+        row.MouseLeave += (_, _) => Highlight(-1);
+        var swatches = new MenuItem { Header = row, StaysOpenOnClick = true };
+        System.Windows.Automation.AutomationProperties.SetName(swatches, "Fence colour: Left and Right choose, Enter sets");
+        swatches.GotKeyboardFocus += (_, _) => Highlight(keyIndex);
+        swatches.PreviewKeyDown += (_, key) =>
+        {
+            switch (key.Key)
+            {
+                case Key.Left when keyIndex > 0: keyIndex--; break;
+                case Key.Right when keyIndex < _swatches.Count - 1: keyIndex++; break;
+                case Key.Enter:
+                    key.Handled = true;
+                    Pick(_swatches[keyIndex].Colour);
+                    return;
+                default: return;
+            }
+            key.Handled = true;
+            Highlight(keyIndex);
+        };
+        TabColorItem.Items.Add(swatches);
         TabColorItem.Items.Add(new Separator());
-        var custom = new MenuItem { Header = "Custom…", Tag = CustomColourTag, IsCheckable = true };
-        custom.Click += (_, _) => CustomColorRequested?.Invoke();
-        TabColorItem.Items.Add(custom);
+        TabColorItem.Items.Add(MenuGlyph.Entry("Use my accent colour", MenuGlyph.Colour, () => AccentColorRequested?.Invoke()));
+        _customColourItem = MenuGlyph.Entry("Custom colour…", MenuGlyph.Add, () => CustomColorRequested?.Invoke());
+        TabColorItem.Items.Add(_customColourItem);
+    }
+
+    /// <summary>The fence's colour in the menu: a ringed swatch (or None), or a tick on Custom colour… for any other colour.</summary>
+    private void ShowColourChoice(Fence fence)
+    {
+        foreach (var (colour, ring) in _swatches)
+        {
+            var current = fence.CustomColor is null && Equals(colour, fence.TabColor);
+            ring.Tag = current;
+            ring.Opacity = current ? 1 : 0;
+        }
+        if (_customColourItem is not null) _customColourItem.IsChecked = fence.CustomColor is not null;
     }
 
     /// <summary>The Recycle Bin turned full or empty, or another special icon changed (M8c).</summary>
diff --git a/src/NeoFences.App/ItemPropertiesWindow.xaml.cs b/src/NeoFences.App/ItemPropertiesWindow.xaml.cs
index a268b74..cd1f801 100644
--- a/src/NeoFences.App/ItemPropertiesWindow.xaml.cs
+++ b/src/NeoFences.App/ItemPropertiesWindow.xaml.cs
@@ -188,7 +188,7 @@ public partial class ItemPropertiesWindow : Window
             {
                 Log.Warning(failure, "picture {Picture} cannot be shown", _picture);
                 _picture = null;
-                MessageBox.Show(this, "This picture cannot be used as an icon.", "NeoFences", MessageBoxButton.OK, MessageBoxImage.Warning);
+                MessageDialog.Tell(this, "This picture cannot be used as an icon", ""); // M35
             }
             return;
         }
diff --git a/src/NeoFences.App/Menus.cs b/src/NeoFences.App/Menus.cs
new file mode 100644
index 0000000..7ad21fb
--- /dev/null
+++ b/src/NeoFences.App/Menus.cs
@@ -0,0 +1,107 @@
+using System.Windows;
+using System.Windows.Controls;
+using System.Windows.Media;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// Menu icons and destructive entries (M35, spec §1, ADR-056): a glyph from Windows' icon font (Segoe Fluent Icons, else
+/// Segoe MDL2 Assets) as a menu entry's icon, and a red ink for Delete / Remove. Set in XAML
+/// (<c>local:MenuGlyph.Glyph="&amp;#xE710;"</c>) or with <see cref="Entry"/> for menus built in code.
+/// </summary>
+public static class MenuGlyph
+{
+    public const string Add = "", View = "", Collect = "", Rename = "", Colour = "", Lock = "",
+        Refresh = "", NewFence = "", Settings = "", Delete = "", Item = "", Desktop = "",
+        Games = "", Folder = "", Widget = "", Size = "", Labels = "", Sort = "", Layout = "",
+        Open = "", OpenFolder = "", Copy = "", Properties = "", Remove = "", ShowAs = "",
+        Cover = "", Detach = "", Pause = "", Snapshot = "", Restore = "", Help = "",
+        Exit = "", Undo = "", SafeMode = "", Peek = "", Hide = "", Run = "", Admin = "",
+        Missing = "", GameMode = "";
+
+    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
+
+    public static readonly DependencyProperty GlyphProperty =
+        DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(MenuGlyph), new PropertyMetadata(null, OnGlyphChanged));
+
+    public static string? GetGlyph(DependencyObject element) => (string?)element.GetValue(GlyphProperty);
+    public static void SetGlyph(DependencyObject element, string? glyph) => element.SetValue(GlyphProperty, glyph);
+
+    /// <summary>A destructive entry (Delete fence, Remove …): shown in the menu's red ink.</summary>
+    public static readonly DependencyProperty DangerProperty =
+        DependencyProperty.RegisterAttached("Danger", typeof(bool), typeof(MenuGlyph), new PropertyMetadata(false));
+
+    public static bool GetDanger(DependencyObject element) => (bool)element.GetValue(DangerProperty);
+    public static void SetDanger(DependencyObject element, bool danger) => element.SetValue(DangerProperty, danger);
+
+    private static void OnGlyphChanged(DependencyObject element, DependencyPropertyChangedEventArgs change)
+    {
+        if (element is MenuItem item) item.Icon = change.NewValue is string { Length: > 0 } glyph ? Icon(glyph) : null;
+    }
+
+    private static TextBlock Icon(string glyph) =>
+        new() { Text = glyph, FontFamily = IconFont, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
+
+    /// <summary>A menu entry built in code: its icon, its click, checked or not, red when destructive.</summary>
+    public static MenuItem Entry(string header, string? glyph, Action? click = null, bool? isChecked = null, bool danger = false, bool enabled = true)
+    {
+        var entry = new MenuItem { Header = header, IsChecked = isChecked == true, IsEnabled = enabled };
+        if (glyph is not null) entry.Icon = Icon(glyph);
+        if (danger) SetDanger(entry, true);
+        if (click is not null) entry.Click += (_, _) => click();
+        return entry;
+    }
+}
+
+/// <summary>The menus' colours (M35): light or dark like the fences; set on the application, so every menu follows at once.</summary>
+public static class MenuTheme
+{
+    public static void Apply(ResourceDictionary resources, bool light)
+    {
+        resources["MenuBackground"] = Frozen(light ? Color.FromArgb(0xF7, 0xF9, 0xF9, 0xF9) : Color.FromArgb(0xF7, 0x2C, 0x2C, 0x30));
+        resources["MenuBorder"] = Frozen(light ? Color.FromArgb(0x1F, 0, 0, 0) : Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
+        resources["MenuText"] = Frozen(light ? Color.FromArgb(0xE6, 0, 0, 0) : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
+        resources["MenuHover"] = Frozen(light ? Color.FromArgb(0x0F, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
+        resources["MenuSeparator"] = Frozen(light ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
+        resources["MenuDanger"] = Frozen(light ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0xFF, 0x99, 0xA4));
+    }
+
+    private static SolidColorBrush Frozen(Color colour)
+    {
+        var brush = new SolidColorBrush(colour);
+        brush.Freeze();
+        return brush;
+    }
+}
+
+/// <summary>
+/// The tray menu (M35, spec §1): the host's entries as NeoFences' own menu at the pointer, with icons; a click elsewhere or
+/// Esc closes it; the chosen entry's id goes to <c>chosen</c> (submenus' own ids never do).
+/// </summary>
+public static class TrayMenuView
+{
+    public static void Show(IReadOnlyList<NeoFences.Shell.TrayMenuItem> items, Point at, Func<int, string?> glyphOf, Action<int> chosen)
+    {
+        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint, HorizontalOffset = at.X, VerticalOffset = at.Y };
+        Fill(menu, items, glyphOf, chosen);
+        menu.IsOpen = true;
+    }
+
+    private static void Fill(ItemsControl parent, IReadOnlyList<NeoFences.Shell.TrayMenuItem> items, Func<int, string?> glyphOf, Action<int> chosen)
+    {
+        foreach (var item in items)
+        {
+            if (item.Id == 0)
+            {
+                parent.Items.Add(new Separator());
+                continue;
+            }
+            var parts = item.Text.Split('\t'); // "Peek\tCtrl+Alt+Space": the keys at the right
+            var entry = MenuGlyph.Entry(parts[0], glyphOf(item.Id), item.Children is { Count: > 0 } ? null : () => chosen(item.Id),
+                isChecked: item.Checked, danger: false, enabled: item.Enabled);
+            if (parts.Length > 1) entry.InputGestureText = parts[1];
+            if (item.Children is { Count: > 0 } children) Fill(entry, children, glyphOf, chosen);
+            parent.Items.Add(entry);
+        }
+    }
+}
diff --git a/src/NeoFences.App/Menus.xaml b/src/NeoFences.App/Menus.xaml
new file mode 100644
index 0000000..05c5083
--- /dev/null
+++ b/src/NeoFences.App/Menus.xaml
@@ -0,0 +1,107 @@
+<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+                    xmlns:local="clr-namespace:NeoFences.App">
+    <!-- M35 (spec 2026-10-06-modern-menus-and-dialogs-design §1, ADR-056): one menu style for fences, items and the tray —
+         rounded, a soft shadow, 30 px rows, a soft hover, light or dark like the fences (brushes from MenuTheme). -->
+    <FontFamily x:Key="MenuIconFont">Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
+
+    <ControlTemplate x:Key="MenuChrome" TargetType="ContentControl">
+        <!-- The margin leaves room for the shadow inside the transparent popup. -->
+        <Border Margin="2,2,12,14" Background="{DynamicResource MenuBackground}" BorderBrush="{DynamicResource MenuBorder}" BorderThickness="1"
+                CornerRadius="8" Padding="4">
+            <Border.Effect>
+                <DropShadowEffect BlurRadius="16" ShadowDepth="4" Direction="270" Opacity="0.32" />
+            </Border.Effect>
+            <ContentPresenter />
+        </Border>
+    </ControlTemplate>
+
+    <Style TargetType="ContextMenu">
+        <Setter Property="OverridesDefaultStyle" Value="True" />
+        <Setter Property="SnapsToDevicePixels" Value="True" />
+        <Setter Property="UseLayoutRounding" Value="True" />
+        <Setter Property="HasDropShadow" Value="False" />
+        <Setter Property="Foreground" Value="{DynamicResource MenuText}" />
+        <Setter Property="FontFamily" Value="Segoe UI Variable Text, Segoe UI" />
+        <Setter Property="FontSize" Value="13" />
+        <Setter Property="MinWidth" Value="200" />
+        <Setter Property="Grid.IsSharedSizeScope" Value="True" />
+        <Setter Property="Template">
+            <Setter.Value>
+                <ControlTemplate TargetType="ContextMenu">
+                    <ContentControl Template="{StaticResource MenuChrome}">
+                        <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle" />
+                    </ContentControl>
+                </ControlTemplate>
+            </Setter.Value>
+        </Setter>
+    </Style>
+
+    <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
+        <Setter Property="OverridesDefaultStyle" Value="True" />
+        <Setter Property="Focusable" Value="False" />
+        <Setter Property="Template">
+            <Setter.Value>
+                <ControlTemplate TargetType="Separator">
+                    <Border Height="1" Margin="8,4" Background="{DynamicResource MenuSeparator}" />
+                </ControlTemplate>
+            </Setter.Value>
+        </Setter>
+    </Style>
+
+    <Style TargetType="MenuItem">
+        <Setter Property="OverridesDefaultStyle" Value="True" />
+        <Setter Property="Foreground" Value="{DynamicResource MenuText}" />
+        <Setter Property="Template">
+            <Setter.Value>
+                <ControlTemplate TargetType="MenuItem">
+                    <Grid>
+                        <Border x:Name="Row" MinHeight="30" CornerRadius="4" Background="Transparent" Padding="8,0,8,0">
+                            <Grid>
+                                <Grid.ColumnDefinitions>
+                                    <ColumnDefinition Width="28" />
+                                    <ColumnDefinition Width="*" />
+                                    <ColumnDefinition Width="Auto" SharedSizeGroup="MenuGesture" />
+                                    <ColumnDefinition Width="18" />
+                                </Grid.ColumnDefinitions>
+                                <ContentPresenter x:Name="IconHost" Content="{TemplateBinding Icon}" HorizontalAlignment="Left" VerticalAlignment="Center" />
+                                <TextBlock x:Name="Check" Text="&#xE73E;" FontFamily="{StaticResource MenuIconFont}" FontSize="12" VerticalAlignment="Center"
+                                           Visibility="Collapsed" />
+                                <ContentPresenter Grid.Column="1" ContentSource="Header" RecognizesAccessKey="True" VerticalAlignment="Center" Margin="0,5" />
+                                <TextBlock Grid.Column="2" Text="{TemplateBinding InputGestureText}" Opacity="0.6" FontSize="12" Margin="24,0,0,0"
+                                           VerticalAlignment="Center" />
+                                <TextBlock x:Name="Arrow" Grid.Column="3" Text="&#xE76C;" FontFamily="{StaticResource MenuIconFont}" FontSize="10"
+                                           HorizontalAlignment="Right" VerticalAlignment="Center" Opacity="0.7" Visibility="Collapsed" />
+                            </Grid>
+                        </Border>
+                        <Popup x:Name="PART_Popup" Placement="Right" HorizontalOffset="-2" VerticalOffset="-7" AllowsTransparency="True"
+                               Focusable="False" PopupAnimation="Fade" IsOpen="{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}">
+                            <ContentControl Template="{StaticResource MenuChrome}" Foreground="{DynamicResource MenuText}"
+                                            FontFamily="Segoe UI Variable Text, Segoe UI" FontSize="13" MinWidth="200" Grid.IsSharedSizeScope="True">
+                                <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle" />
+                            </ContentControl>
+                        </Popup>
+                    </Grid>
+                    <ControlTemplate.Triggers>
+                        <Trigger Property="Role" Value="SubmenuHeader">
+                            <Setter TargetName="Arrow" Property="Visibility" Value="Visible" />
+                        </Trigger>
+                        <Trigger Property="IsHighlighted" Value="True">
+                            <Setter TargetName="Row" Property="Background" Value="{DynamicResource MenuHover}" />
+                        </Trigger>
+                        <Trigger Property="IsChecked" Value="True">
+                            <Setter TargetName="Check" Property="Visibility" Value="Visible" />
+                            <Setter TargetName="IconHost" Property="Visibility" Value="Hidden" />
+                        </Trigger>
+                        <Trigger Property="IsEnabled" Value="False">
+                            <Setter Property="Opacity" Value="0.45" />
+                        </Trigger>
+                        <Trigger Property="local:MenuGlyph.Danger" Value="True">
+                            <Setter Property="Foreground" Value="{DynamicResource MenuDanger}" />
+                        </Trigger>
+                    </ControlTemplate.Triggers>
+                </ControlTemplate>
+            </Setter.Value>
+        </Setter>
+    </Style>
+</ResourceDictionary>
diff --git a/src/NeoFences.App/MessageDialog.xaml b/src/NeoFences.App/MessageDialog.xaml
new file mode 100644
index 0000000..78900a2
--- /dev/null
+++ b/src/NeoFences.App/MessageDialog.xaml
@@ -0,0 +1,15 @@
+<Window x:Class="NeoFences.App.MessageDialog"
+        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
+        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
+        Title="NeoFences" Width="440" SizeToContent="Height" ResizeMode="NoResize"
+        WindowStartupLocation="CenterOwner" ShowInTaskbar="False" ThemeMode="System">
+    <!-- M35 (spec §3): the one message dialog — a question with two answers, or a note with OK — instead of MessageBox. -->
+    <StackPanel Margin="24,18,24,20">
+        <TextBlock x:Name="HeadingText" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" Margin="0,0,0,8" />
+        <TextBlock x:Name="BodyText" TextWrapping="Wrap" Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,18" />
+        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
+            <Button x:Name="PrimaryButton" IsDefault="True" Style="{DynamicResource AccentButtonStyle}" MinWidth="90" Margin="0,0,8,0" />
+            <Button x:Name="SecondaryButton" IsCancel="True" MinWidth="90" />
+        </StackPanel>
+    </StackPanel>
+</Window>
diff --git a/src/NeoFences.App/MessageDialog.xaml.cs b/src/NeoFences.App/MessageDialog.xaml.cs
new file mode 100644
index 0000000..1d5711b
--- /dev/null
+++ b/src/NeoFences.App/MessageDialog.xaml.cs
@@ -0,0 +1,40 @@
+using System.Windows;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// The one message dialog (M35, spec §3) in Windows 11's style: <see cref="Ask"/> a question with two answers (Enter for the
+/// first, Esc for the second), or <see cref="Tell"/> a note with OK.
+/// </summary>
+public partial class MessageDialog : Window
+{
+    private MessageDialog(string heading, string text, string primary, string? secondary)
+    {
+        InitializeComponent();
+        HeadingText.Text = heading;
+        BodyText.Text = text;
+        BodyText.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
+        PrimaryButton.Content = primary;
+        PrimaryButton.Click += (_, _) => DialogResult = true;
+        if (secondary is null)
+        {
+            SecondaryButton.Visibility = Visibility.Collapsed;
+            PrimaryButton.IsCancel = true; // Esc closes a note too
+            PrimaryButton.Margin = new Thickness(0);
+        }
+        else SecondaryButton.Content = secondary;
+    }
+
+    /// <returns>True for the first answer; false for the second, Esc or closing the window.</returns>
+    public static bool Ask(Window? owner, string heading, string text, string primary, string secondary) =>
+        Show(owner, new MessageDialog(heading, text, primary, secondary)) == true;
+
+    public static void Tell(Window? owner, string heading, string text) => Show(owner, new MessageDialog(heading, text, "OK", secondary: null));
+
+    private static bool? Show(Window? owner, MessageDialog dialog)
+    {
+        if (owner is { IsVisible: true }) dialog.Owner = owner;
+        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
+        return dialog.ShowDialog();
+    }
+}
diff --git a/src/NeoFences.App/OnlineArtWindow.xaml b/src/NeoFences.App/OnlineArtWindow.xaml
index a4d9dce..1108a86 100644
--- a/src/NeoFences.App/OnlineArtWindow.xaml
+++ b/src/NeoFences.App/OnlineArtWindow.xaml
@@ -8,7 +8,7 @@
         <TextBlock Text="Find covers online?" FontSize="16" FontWeight="SemiBold" TextWrapping="Wrap" Margin="0,0,0,8" />
         <TextBlock x:Name="CountText" TextWrapping="Wrap" Margin="0,0,0,6" />
         <TextBlock TextWrapping="Wrap" FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,18"
-                   Text="NeoFences sends the names of games without a cover to the Steam store, and asks your web links' sites for their icons. Nothing else leaves your PC. You can change this in Settings → Game Library." />
+                   Text="NeoFences sends the names of games without a cover to the Steam store, and asks your web links' sites for their icons. Nothing else leaves your PC. You can change this in Settings → Games." />
         <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
             <Button x:Name="YesButton" Content="Find covers" IsDefault="True" Style="{DynamicResource AccentButtonStyle}" MinWidth="100" Margin="0,0,8,0" />
             <Button x:Name="NoButton" Content="Not now" IsCancel="True" MinWidth="90" />
diff --git a/src/NeoFences.App/SettingsWindow.Library.cs b/src/NeoFences.App/SettingsWindow.Library.cs
index d5aa00a..0228476 100644
--- a/src/NeoFences.App/SettingsWindow.Library.cs
+++ b/src/NeoFences.App/SettingsWindow.Library.cs
@@ -14,13 +14,13 @@ public sealed record HiddenGame(string Id, string Name, IReadOnlyList<string> Al
     public override string ToString() => Name;
 }
 
-/// <summary>What Settings → Game Library shows (M12; M22: the fences new games can go to, and the chosen one).</summary>
+/// <summary>What Settings → Games shows (M12; M22: the fences new games can go to, and the chosen one).</summary>
 /// <param name="HasFence">The scan runs (games are wanted somewhere).</param>
 public sealed record LibraryView(bool HasFence, IReadOnlyList<string> Folders, LibrarySources Sources, IReadOnlyList<HiddenGame> Hidden, string Status,
     IReadOnlyList<(string Id, string Title)> Fences, string? NewGamesFence, bool OnlineArt = false);
 
 /// <summary>
-/// Settings → Game Library (M12, spec §4): game folders, a checkbox per source, hidden games with "Show again", and
+/// Settings → Games (M12; M35: the Games page, spec §4): game folders, a checkbox per source, hidden games with "Show again", and
 /// "Refresh library now". Every change goes to the host, which saves, rescans and shows it back.
 /// </summary>
 public partial class SettingsWindow
diff --git a/src/NeoFences.App/SettingsWindow.xaml b/src/NeoFences.App/SettingsWindow.xaml
index 9c6b33c..71f6c74 100644
--- a/src/NeoFences.App/SettingsWindow.xaml
+++ b/src/NeoFences.App/SettingsWindow.xaml
@@ -1,7 +1,7 @@
 <Window x:Class="NeoFences.App.SettingsWindow"
         xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
-        Title="NeoFences settings" Width="600" Height="680" MinWidth="460" MinHeight="400"
+        Title="NeoFences settings" Width="820" Height="680" MinWidth="640" MinHeight="420"
         WindowStartupLocation="CenterScreen" ThemeMode="System">
     <!-- Fluent (WPF's built-in theme, ThemeMode="System"): follows Windows light/dark and the accent colour (spec §6).
          Set only on this window: the fences keep their own look. Changes apply at once; there is no OK button. -->
@@ -25,10 +25,79 @@
             <Setter Property="TextWrapping" Value="Wrap" />
             <Setter Property="Margin" Value="0,2,0,0" />
         </Style>
+        <Style x:Key="Switch" TargetType="CheckBox">
+            <Setter Property="Cursor" Value="Hand" />
+            <Setter Property="Template">
+                <Setter.Value>
+                    <ControlTemplate TargetType="CheckBox">
+                        <StackPanel Orientation="Horizontal" Background="Transparent">
+                            <TextBlock x:Name="State" Text="Off" Width="28" VerticalAlignment="Center" Foreground="{DynamicResource TextFillColorPrimaryBrush}" />
+                            <Border x:Name="Track" Width="40" Height="20" CornerRadius="10" BorderThickness="1" Background="Transparent"
+                                    BorderBrush="{DynamicResource TextFillColorSecondaryBrush}">
+                                <Ellipse x:Name="Knob" Width="12" Height="12" HorizontalAlignment="Left" Margin="4,0,0,0" Fill="{DynamicResource TextFillColorSecondaryBrush}" />
+                            </Border>
+                        </StackPanel>
+                        <ControlTemplate.Triggers>
+                            <Trigger Property="IsChecked" Value="True">
+                                <Setter TargetName="State" Property="Text" Value="On" />
+                                <Setter TargetName="Track" Property="Background" Value="{DynamicResource AccentFillColorDefaultBrush}" />
+                                <Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource AccentFillColorDefaultBrush}" />
+                                <Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right" />
+                                <Setter TargetName="Knob" Property="Margin" Value="0,0,4,0" />
+                                <Setter TargetName="Knob" Property="Fill" Value="{DynamicResource TextOnAccentFillColorPrimaryBrush}" />
+                            </Trigger>
+                            <Trigger Property="IsMouseOver" Value="True">
+                                <Setter TargetName="Knob" Property="Width" Value="14" />
+                                <Setter TargetName="Knob" Property="Height" Value="14" />
+                            </Trigger>
+                            <Trigger Property="IsEnabled" Value="False">
+                                <Setter Property="Opacity" Value="0.45" />
+                            </Trigger>
+                        </ControlTemplate.Triggers>
+                    </ControlTemplate>
+                </Setter.Value>
+            </Setter>
+        </Style>
     </Window.Resources>
-    <ScrollViewer VerticalScrollBarVisibility="Auto">
-        <StackPanel Margin="28,16,28,28">
-            <TextBlock Text="Settings" FontSize="28" FontWeight="SemiBold" />
+    <!-- M35 (spec §2): a section list on the left, the open section's page on the right. -->
+    <Grid>
+        <Grid.ColumnDefinitions>
+            <ColumnDefinition Width="200" />
+            <ColumnDefinition />
+        </Grid.ColumnDefinitions>
+        <ListBox x:Name="SectionList" Margin="8,16,4,12" Background="Transparent" BorderThickness="0" AutomationProperties.Name="Settings sections">
+            <!-- M35: like Windows Settings — a subtle highlight and a short accent pill on the open section. -->
+            <ListBox.ItemContainerStyle>
+                <Style TargetType="ListBoxItem">
+                    <Setter Property="Margin" Value="0,1" />
+                    <Setter Property="Template">
+                        <Setter.Value>
+                            <ControlTemplate TargetType="ListBoxItem">
+                                <Border x:Name="Row" CornerRadius="4" Background="Transparent" Padding="{TemplateBinding Padding}">
+                                    <Grid>
+                                        <Border x:Name="Pill" Width="3" Height="16" CornerRadius="1.5" HorizontalAlignment="Left" Margin="-8,0,0,0"
+                                                Background="{DynamicResource AccentFillColorDefaultBrush}" Visibility="Collapsed" />
+                                        <ContentPresenter />
+                                    </Grid>
+                                </Border>
+                                <ControlTemplate.Triggers>
+                                    <Trigger Property="IsMouseOver" Value="True">
+                                        <Setter TargetName="Row" Property="Background" Value="{DynamicResource SubtleFillColorSecondaryBrush}" />
+                                    </Trigger>
+                                    <Trigger Property="IsSelected" Value="True">
+                                        <Setter TargetName="Row" Property="Background" Value="{DynamicResource SubtleFillColorSecondaryBrush}" />
+                                        <Setter TargetName="Pill" Property="Visibility" Value="Visible" />
+                                    </Trigger>
+                                </ControlTemplate.Triggers>
+                            </ControlTemplate>
+                        </Setter.Value>
+                    </Setter>
+                </Style>
+            </ListBox.ItemContainerStyle>
+        </ListBox>
+        <ScrollViewer x:Name="PageScroller" Grid.Column="1" VerticalScrollBarVisibility="Auto">
+        <StackPanel Margin="20,16,28,28">
+            <TextBlock x:Name="PageTitle" Text="General" FontSize="28" FontWeight="SemiBold" />
             <!-- M33: a problem on top (safe mode, changes not saved), with the logs one click away. -->
             <Border x:Name="Banner" Visibility="Collapsed" Margin="0,12,0,0" Padding="14,10" CornerRadius="6"
                     Background="{DynamicResource SystemFillColorCautionBackgroundBrush}" BorderBrush="{DynamicResource CardStrokeColorDefaultBrush}" BorderThickness="1">
@@ -38,10 +107,10 @@
                 </DockPanel>
             </Border>
 
-            <TextBlock Text="General" Style="{StaticResource SectionHeader}" />
+            <StackPanel x:Name="GeneralPage" Margin="0,16,0,0" Visibility="Collapsed">
             <Border Style="{StaticResource Card}">
                 <DockPanel>
-                    <CheckBox x:Name="StartupBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Start with Windows" />
+                    <CheckBox x:Name="StartupBox" DockPanel.Dock="Right" Style="{StaticResource Switch}" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Start with Windows" />
                     <StackPanel>
                         <TextBlock Text="Start with Windows" TextWrapping="Wrap" />
                         <TextBlock x:Name="StartupDescription" Style="{StaticResource Description}" Text="Your fences come back by themselves after a restart or a power cut." />
@@ -50,7 +119,7 @@
             </Border>
             <Border Style="{StaticResource Card}">
                 <DockPanel>
-                    <CheckBox x:Name="HideIconsBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Hide desktop icons while NeoFences runs" />
+                    <CheckBox x:Name="HideIconsBox" DockPanel.Dock="Right" Style="{StaticResource Switch}" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Hide desktop icons while NeoFences runs" />
                     <StackPanel>
                         <TextBlock Text="Hide desktop icons while NeoFences runs" TextWrapping="Wrap" />
                         <TextBlock x:Name="HideIconsDescription" Style="{StaticResource Description}" Text="Windows' own desktop icons go away while NeoFences runs and come back when it is paused, closed, or stops unexpectedly. Your files stay where they are." />
@@ -59,7 +128,7 @@
             </Border>
             <Border Style="{StaticResource Card}">
                 <DockPanel>
-                    <CheckBox x:Name="QuickHideGestureBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Double-click the desktop to quick-hide" />
+                    <CheckBox x:Name="QuickHideGestureBox" DockPanel.Dock="Right" Style="{StaticResource Switch}" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Double-click the desktop to quick-hide" />
                     <StackPanel>
                         <TextBlock Text="Double-click the desktop to quick-hide" TextWrapping="Wrap" />
                         <TextBlock x:Name="QuickHideGestureDescription" Style="{StaticResource Description}" Text="Double-click empty desktop to hide all fences (and the icons); again to bring them back. Quick-hide stays in the tray menu." />
@@ -68,7 +137,7 @@
             </Border>
             <Border Style="{StaticResource Card}">
                 <DockPanel>
-                    <CheckBox x:Name="DrawGestureBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Right-drag on the desktop to draw a fence" />
+                    <CheckBox x:Name="DrawGestureBox" DockPanel.Dock="Right" Style="{StaticResource Switch}" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Right-drag on the desktop to draw a fence" />
                     <StackPanel>
                         <TextBlock Text="Right-drag on the desktop to draw a fence" TextWrapping="Wrap" />
                         <TextBlock x:Name="DrawGestureDescription" Style="{StaticResource Description}" Text="Hold the right mouse button on empty desktop and draw a rectangle. With both desktop gestures off, NeoFences watches no mouse clicks at all." />
@@ -91,7 +160,8 @@
                 </StackPanel>
             </Border>
 
-            <TextBlock Text="Fences" Style="{StaticResource SectionHeader}" />
+            </StackPanel>
+            <StackPanel x:Name="FencesPage" Margin="0,16,0,0" Visibility="Collapsed">
             <Border Style="{StaticResource Card}">
                 <StackPanel>
                     <DockPanel>
@@ -109,7 +179,7 @@
             </Border>
             <Border Style="{StaticResource Card}">
                 <DockPanel>
-                    <CheckBox x:Name="ArrowsBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Show shortcut arrows" />
+                    <CheckBox x:Name="ArrowsBox" DockPanel.Dock="Right" Style="{StaticResource Switch}" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Show shortcut arrows" />
                     <StackPanel>
                         <TextBlock Text="Show shortcut arrows" TextWrapping="Wrap" />
                         <TextBlock x:Name="ArrowsDescription" Style="{StaticResource Description}" Text="The small arrow Windows draws on shortcut icons." />
@@ -129,7 +199,8 @@
                 </DockPanel>
             </Border>
 
-            <TextBlock Text="Appearance" Style="{StaticResource SectionHeader}" />
+            </StackPanel>
+            <StackPanel x:Name="AppearancePage" Margin="0,16,0,0" Visibility="Collapsed">
             <Border Style="{StaticResource Card}">
                 <StackPanel>
                     <TextBlock Text="Colour style" />
@@ -174,7 +245,7 @@
             </Border>
             <Border Style="{StaticResource Card}">
                 <DockPanel>
-                    <CheckBox x:Name="AccentBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Colour fences from the wallpaper" />
+                    <CheckBox x:Name="AccentBox" DockPanel.Dock="Right" Style="{StaticResource Switch}" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Colour fences from the wallpaper" />
                     <StackPanel>
                         <TextBlock Text="Colour fences from the wallpaper" TextWrapping="Wrap" />
                         <TextBlock Style="{StaticResource Description}" Text="Fences without a colour of their own take the wallpaper's colour (also Wallpaper Engine's)." />
@@ -217,11 +288,12 @@
                 </StackPanel>
             </Border>
 
-            <TextBlock Text="Game mode" Style="{StaticResource SectionHeader}" />
+            </StackPanel>
+            <StackPanel x:Name="GameModePage" Margin="0,16,0,0" Visibility="Collapsed">
             <Border Style="{StaticResource Card}">
                 <StackPanel>
                     <DockPanel>
-                        <CheckBox x:Name="GameModeBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Go idle while a full-screen game runs" />
+                        <CheckBox x:Name="GameModeBox" DockPanel.Dock="Right" Style="{StaticResource Switch}" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Go idle while a full-screen game runs" />
                         <StackPanel>
                             <TextBlock Text="Go idle while a full-screen game runs" TextWrapping="Wrap" />
                             <TextBlock x:Name="GameModeDescription" Style="{StaticResource Description}" Text="NeoFences removes its mouse hook and waits with background work, so games get every bit of input. Fences stay where they are." />
@@ -231,7 +303,8 @@
                 </StackPanel>
             </Border>
 
-            <TextBlock Text="Snapshots" Style="{StaticResource SectionHeader}" />
+            </StackPanel>
+            <StackPanel x:Name="SnapshotsPage" Margin="0,16,0,0" Visibility="Collapsed">
             <Border x:Name="SnapshotsCard" Style="{StaticResource Card}">
                 <StackPanel>
                     <TextBlock x:Name="SnapshotsDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
@@ -263,14 +336,15 @@
                 </StackPanel>
             </Border>
 
-            <TextBlock Text="Game Library" Style="{StaticResource SectionHeader}" />
+            </StackPanel>
+            <StackPanel x:Name="GamesPage" Margin="0,16,0,0" Visibility="Collapsed">
             <Border x:Name="LibraryCard" Style="{StaticResource Card}">
                 <StackPanel>
                     <TextBlock x:Name="LibraryDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
-                               Text="Finds the games installed on this PC. Put them in any fence with fence menu → Add games…; newly installed games go to the fence chosen below. Launcher games start through their launcher. Nothing is downloaded unless you turn on finding covers below, and your games and files are never changed." />
+                               Text="Finds the games installed on this PC. Put them in any fence with fence menu → Add → Games…; newly installed games go to the fence chosen below. Launcher games start through their launcher. Nothing is downloaded unless you turn on finding covers below, and your games and files are never changed." />
                     <!-- M34 (ADR-055): online art, off until the owner says yes (asked once). -->
                     <DockPanel Margin="0,4,0,10">
-                        <CheckBox x:Name="OnlineArtBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Find covers and website icons online" />
+                        <CheckBox x:Name="OnlineArtBox" DockPanel.Dock="Right" Style="{StaticResource Switch}" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Find covers and website icons online" />
                         <StackPanel>
                             <TextBlock Text="Find covers and website icons online" TextWrapping="Wrap" />
                             <TextBlock Style="{StaticResource Description}" Text="Sends the names of games without a cover to the Steam store, and asks your web links' sites for their icons. Nothing else leaves your PC. Off: covers already found stay." />
@@ -309,11 +383,12 @@
                 </StackPanel>
             </Border>
 
-            <TextBlock Text="Updates" Style="{StaticResource SectionHeader}" />
+            </StackPanel>
+            <StackPanel x:Name="UpdatesPage" Margin="0,16,0,0" Visibility="Collapsed">
             <Border Style="{StaticResource Card}">
                 <StackPanel>
                     <DockPanel>
-                        <CheckBox x:Name="AutoUpdateBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Download updates automatically" />
+                        <CheckBox x:Name="AutoUpdateBox" DockPanel.Dock="Right" Style="{StaticResource Switch}" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Download updates automatically" />
                         <StackPanel>
                             <TextBlock Text="Download updates automatically" TextWrapping="Wrap" />
                             <TextBlock Style="{StaticResource Description}" Text="Checks GitHub at start and once a day, never during a game. Off: no update checks." />
@@ -327,7 +402,8 @@
                 </StackPanel>
             </Border>
 
-            <TextBlock Text="About and logs" Style="{StaticResource SectionHeader}" />
+            </StackPanel>
+            <StackPanel x:Name="AboutPage" Margin="0,16,0,0" Visibility="Collapsed">
             <Border Style="{StaticResource Card}">
                 <StackPanel>
                     <TextBlock x:Name="VersionText" />
@@ -340,6 +416,8 @@
                     </StackPanel>
                 </StackPanel>
             </Border>
+            </StackPanel>
         </StackPanel>
-    </ScrollViewer>
+        </ScrollViewer>
+    </Grid>
 </Window>
diff --git a/src/NeoFences.App/SettingsWindow.xaml.cs b/src/NeoFences.App/SettingsWindow.xaml.cs
index a931421..e7784f0 100644
--- a/src/NeoFences.App/SettingsWindow.xaml.cs
+++ b/src/NeoFences.App/SettingsWindow.xaml.cs
@@ -62,6 +62,7 @@ public partial class SettingsWindow : Window
     public SettingsWindow()
     {
         InitializeComponent();
+        BuildSections(); // M35
         // Checked/Unchecked, not Click: UI Automation (Narrator, Toggle) changes the box without a click (M6b smoke).
         OnToggled(StartupBox, isChecked => StartWithWindowsChanged?.Invoke(isChecked));
         OnToggled(HideIconsBox, isChecked => HideDesktopIconsChanged?.Invoke(isChecked));
@@ -198,8 +199,44 @@ public partial class SettingsWindow : Window
     }
 
     /// <summary>Tray → Restore snapshot → "More in Settings…": the Snapshots card in view (M13c).</summary>
-    public void ShowSnapshotsCard() =>
-        Dispatcher.BeginInvoke(() => SnapshotsCard.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
+    public void ShowSnapshotsCard() => ShowPage("Snapshots"); // M35: its own page
+
+    /// <summary>
+    /// The section list (M35, spec §2): General, Fences, Appearance, Games, Game mode, Snapshots, Updates, About — the open one
+    /// marked, only its page shown; the arrow keys move through the list, Tab goes into the page.
+    /// </summary>
+    private (string Name, string Glyph, FrameworkElement Page)[] Sections =>
+    [
+        ("General", MenuGlyph.Settings, GeneralPage), ("Fences", MenuGlyph.NewFence, FencesPage), ("Appearance", MenuGlyph.Colour, AppearancePage),
+        ("Games", MenuGlyph.Games, GamesPage), ("Game mode", MenuGlyph.GameMode, GameModePage), ("Snapshots", MenuGlyph.Snapshot, SnapshotsPage),
+        ("Updates", MenuGlyph.Refresh, UpdatesPage), ("About", MenuGlyph.Properties, AboutPage),
+    ];
+
+    private void BuildSections()
+    {
+        foreach (var (name, glyph, _) in Sections)
+        {
+            var row = new StackPanel { Orientation = Orientation.Horizontal };
+            row.Children.Add(new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 15,
+                Width = 22, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
+            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
+            var item = new ListBoxItem { Content = row, Tag = name, Padding = new Thickness(10, 7, 10, 7) };
+            System.Windows.Automation.AutomationProperties.SetName(item, name);
+            SectionList.Items.Add(item);
+        }
+        SectionList.SelectionChanged += (_, _) =>
+        {
+            if (SectionList.SelectedItem is not ListBoxItem { Tag: string name }) return;
+            foreach (var (section, _, page) in Sections) page.Visibility = section == name ? Visibility.Visible : Visibility.Collapsed;
+            PageTitle.Text = name;
+            PageScroller.ScrollToTop();
+        };
+        SectionList.SelectedIndex = 0;
+    }
+
+    /// <summary>Opens a section by name ("Snapshots", "Games", …).</summary>
+    public void ShowPage(string name) =>
+        SectionList.SelectedItem = SectionList.Items.OfType<ListBoxItem>().FirstOrDefault(item => Equals(item.Tag, name)) ?? SectionList.SelectedItem;
 
     /// <summary>The snapshot list, keeping the selection where the same file is still listed.</summary>
     private void ShowSnapshots(IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> snapshots)
diff --git a/src/NeoFences.Shell/ColorPicker.cs b/src/NeoFences.Shell/ColorPicker.cs
deleted file mode 100644
index b252249..0000000
--- a/src/NeoFences.Shell/ColorPicker.cs
+++ /dev/null
@@ -1,31 +0,0 @@
-using NeoFences.Core.Appearance;
-using Windows.Win32;
-using Windows.Win32.Foundation;
-using Windows.Win32.UI.Controls.Dialogs;
-
-namespace NeoFences.Shell;
-
-/// <summary>Windows' colour dialog (M14, fence menu → Colour → Custom…): any colour, no new dependency.</summary>
-public static class ColorPicker
-{
-    private static readonly uint[] CustomColors = new uint[16]; // the dialog's 16 "custom colours" for this run
-
-    /// <returns>"#RRGGBB", or null when the user cancelled.</returns>
-    public static unsafe string? TryPick(nint ownerHandle, Argb? initial)
-    {
-        fixed (uint* custom = CustomColors)
-        {
-            var dialog = new CHOOSECOLORW
-            {
-                lStructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<CHOOSECOLORW>(),
-                hwndOwner = (HWND)ownerHandle,
-                rgbResult = initial is { } start ? (COLORREF)(uint)(start.R | start.G << 8 | start.B << 16) : default,
-                lpCustColors = (COLORREF*)custom,
-                Flags = CHOOSECOLOR_FLAGS.CC_RGBINIT | CHOOSECOLOR_FLAGS.CC_FULLOPEN | CHOOSECOLOR_FLAGS.CC_ANYCOLOR,
-            };
-            if (!PInvoke.ChooseColor(ref dialog)) return null;
-            var rgb = dialog.rgbResult.Value;
-            return new Argb(0xFF, (byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16)).ToHex();
-        }
-    }
-}
diff --git a/src/NeoFences.Shell/NativeMethods.txt b/src/NeoFences.Shell/NativeMethods.txt
index 3473792..45b01a2 100644
--- a/src/NeoFences.Shell/NativeMethods.txt
+++ b/src/NeoFences.Shell/NativeMethods.txt
@@ -179,8 +179,6 @@ SHLoadIndirectString
 MapVirtualKey
 IDesktopWallpaper
 DesktopWallpaper
-ChooseColor
-CHOOSECOLOR_FLAGS
 PickIconDlg
 SHDefExtractIcon
 GetIconInfo
diff --git a/src/NeoFences.Shell/PathPicker.cs b/src/NeoFences.Shell/PathPicker.cs
index a69bf1e..4ecadd2 100644
--- a/src/NeoFences.Shell/PathPicker.cs
+++ b/src/NeoFences.Shell/PathPicker.cs
@@ -1,7 +1,6 @@
 using System.Runtime.InteropServices;
 using Windows.Win32;
 using Windows.Win32.Foundation;
-using Windows.Win32.UI.Controls.Dialogs;
 using Windows.Win32.UI.Shell;
 using Windows.Win32.UI.Shell.Common;
 
diff --git a/src/NeoFences.Shell/TrayIcon.cs b/src/NeoFences.Shell/TrayIcon.cs
index 6cf2031..25bc136 100644
--- a/src/NeoFences.Shell/TrayIcon.cs
+++ b/src/NeoFences.Shell/TrayIcon.cs
@@ -133,53 +133,13 @@ public sealed record TrayMenuItem(int Id, string Text = "", bool Checked = false
     public IReadOnlyList<TrayMenuItem>? Children { get; init; }
 }
 
-/// <summary>The tray menu: Windows' own popup menu (it closes reliably when the user clicks elsewhere).</summary>
+/// <summary>
+/// The tray menu (M35, ADR-056): NeoFences draws it like its other menus; Windows' part is only the documented dance —
+/// NeoFences comes to the foreground first, so a click anywhere else closes the menu.
+/// </summary>
 public static class TrayMenu
 {
-    /// <returns>The chosen item's id, or 0 when the menu was dismissed.</returns>
-    public static unsafe int Show(nint ownerHandle, IReadOnlyList<TrayMenuItem> items, int screenX, int screenY)
-    {
-        var menu = PInvoke.CreatePopupMenu();
-        if (menu.IsNull) return 0;
-        try
-        {
-            Fill(menu, items);
-            // The documented tray-menu dance: foreground first, so a click elsewhere closes the menu; WM_NULL after.
-            PInvoke.SetForegroundWindow((HWND)ownerHandle);
-            var chosen = PInvoke.TrackPopupMenuEx(menu,
-                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN),
-                screenX, screenY, (HWND)ownerHandle, null);
-            PInvoke.PostMessage((HWND)ownerHandle, PInvoke.WM_NULL, 0, 0);
-            return chosen.Value;
-        }
-        finally
-        {
-            PInvoke.DestroyMenu(menu); // and its submenus
-        }
-    }
-
-    private static unsafe void Fill(HMENU menu, IReadOnlyList<TrayMenuItem> items)
-    {
-        foreach (var item in items)
-        {
-            if (item.Id == 0)
-            {
-                PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);
-                continue;
-            }
-            var flags = MENU_ITEM_FLAGS.MF_STRING | (item.Checked ? MENU_ITEM_FLAGS.MF_CHECKED : 0) | (item.Enabled ? 0 : MENU_ITEM_FLAGS.MF_GRAYED);
-            var id = (nuint)item.Id;
-            if (item.Children is { Count: > 0 } children && item.Enabled)
-            {
-                var submenu = PInvoke.CreatePopupMenu();
-                if (submenu.IsNull) continue;
-                Fill(submenu, children);
-                flags |= MENU_ITEM_FLAGS.MF_POPUP;
-                id = (nuint)(nint)submenu.Value;
-            }
-            fixed (char* text = item.Text) PInvoke.AppendMenu(menu, flags, id, text);
-        }
-    }
+    public static void BringForward(nint ownerHandle) => PInvoke.SetForegroundWindow((HWND)ownerHandle);
 }
 
 /// <summary>Session lock/unlock notices (WM_WTSSESSION_CHANGE) for a window: the mouse hook is re-installed on unlock.</summary>
```

- [ ] **Step 2: Build and test.** `dotnet build` → `0 Warning(s)`; `dotnet test` → `Passed: 773`.

- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: added one modern menu style with icons, the grouped fence menu, the tray menu, Settings sections with switches, colour swatches, the colour picker and the message dialog"`

### Task 3: Docs

**Files:**
- Modify: `docs/DECISIONS.md` (ADR-056), `docs/ARCHITECTURE.md` (0.22.0 paragraph), `docs/GUIDE.md` and `README.md` (every menu path —
  see below), `docs/FEATURES.md` (rows), `docs/TEST-CHECKLIST.md` (section AU)
- Create: `docs/research/m35-modern-menus-and-dialogs.md`

- [ ] **Step 1: Menu paths in GUIDE and README** (ADR-050). Search each and rewrite: fence menu "Add item…" → "Add → Item…";
  "Add from desktop…" (fence menu) → "Add → From desktop…" (the tray keeps "Add from desktop…"); "Add games…" → "Add → Games…";
  "Add widget" → "Add → Widget"; "Add folder panel…" → "Add → Folder panel…"; fence menu "New folder panel…" → "New fence →
  Folder panel…" (the tray keeps "New folder panel…"); fence menu "New fence" → "New fence → Empty"; "Icon size", "Labels",
  "Sort by", "Layout" → "View → …"; "Rename fence" → "Rename"; "Colour" → swatches, "Use my accent colour", "Custom colour…";
  "Delete fence (your files are not touched)" → "Delete fence"; "Start with Windows" in the fence menu → Settings → General;
  "Settings → Game Library" → "Settings → Games"; Settings' description (§12) as eight sections with switches.
- [ ] **Step 2: The rest.** ADR-056 (one menu style drawn by NeoFences for fences, items and the tray; why not WPF's Fluent theme
  app-wide or native dark menus; the tray dance stays in Shell); ARCHITECTURE 0.22.0; FEATURES row; research note (probe, rulings).
  AU: AU1 the fence menu, 12 lines with icons, dark and light; AU2 Add ▸ / View ▸ / New fence ▸ entries act; AU3 Delete fence in
  red with its tooltip, then Ctrl+Z / tray Undo delete still work; AU4 item, game, folder panel, widget and several-items menus:
  Open first, Remove last in red, the Windows' menu hint; AU5 the tray menu at the pointer, closes on a click elsewhere and Esc,
  Restore snapshot ▸ and Exit act; AU6 Colour ▸ swatches (ring follows), Use my accent colour, Custom colour… (field, hue, hex,
  Cancel); AU7 Settings: eight sections, switches show On / Off and act, the banner on every page, tray "More in Settings…" opens
  Snapshots; AU8 the auto-collect question and the bad-picture note in the new dialog; AU9 Windows switched to light mode while
  NeoFences runs: menus follow; AU10 Start with Windows only in Settings, Exit only in the tray.
- [ ] **Step 3: Check** the strings against the code; secret scan of `git diff main`.
- [ ] **Step 4: Commit.** `git add -A && git commit -m "docs: described modern menus and dialogs in ADR-056, the guide's menu paths, architecture, features, checklist AU and the M35 note"`

### Task 4: Final review and fix pass

- [ ] Whole-branch review on the most capable model (Review Focus above); findings re-graded by effect; Critical/Important
  fixed in one pass, each with a failing test first where Core-testable.

### Task 5: Live check (asked first)

- [ ] On a backed-up copy of the owner's data (`m35-switch.ps1`, branch Release build): AU1–AU10 as far as scriptable
  (screenshots sent as they are taken); light mode only with the owner's OK (it changes a Windows setting; restore it);
  restore the data and the Run value; start the installed copy; refocus Terminal; results into the research note; commit
  `docs: added the M35 live check results`.
