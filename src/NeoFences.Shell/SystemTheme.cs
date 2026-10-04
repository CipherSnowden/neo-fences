using Microsoft.Win32;

namespace NeoFences.Shell;

/// <summary>The Windows "app mode" (Settings → Personalization → Colors), which fences follow (M2c, user choice).</summary>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True for light mode. Missing value (older builds, policy-stripped profiles): light, like Windows.</summary>
    public static bool AppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
}
