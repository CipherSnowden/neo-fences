namespace NeoFences.Core.Lifecycle;

/// <summary>
/// Log lines without the Windows user name (M33): the user profile path is written as <c>%USERPROFILE%</c>, so a log shared
/// in an issue does not carry it. Only a whole path segment matches (C:\Users\Alexander is not C:\Users\Alex).
/// </summary>
public static class LogPrivacy
{
    public const string Placeholder = "%USERPROFILE%";

    public static string Mask(string text, string profilePath)
    {
        if (string.IsNullOrEmpty(profilePath) || string.IsNullOrEmpty(text)) return text;
        var profile = profilePath.TrimEnd('\\', '/');
        var result = new System.Text.StringBuilder(text.Length);
        var start = 0;
        while (true)
        {
            var at = text.IndexOf(profile, start, StringComparison.OrdinalIgnoreCase);
            if (at < 0) break;
            var end = at + profile.Length;
            var wholeSegment = end == text.Length || text[end] is '\\' or '/' or '"' or '\'' or ' ' or ')' or ',' or ';' or ']';
            result.Append(text, start, at - start).Append(wholeSegment ? Placeholder : text.Substring(at, profile.Length));
            start = end;
        }
        return result.Append(text, start, text.Length - start).ToString();
    }
}
