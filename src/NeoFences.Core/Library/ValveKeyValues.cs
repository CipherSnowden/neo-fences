using System.Text;

namespace NeoFences.Core.Library;

/// <summary>
/// Valve's text KeyValues format (Steam's <c>libraryfolders.vdf</c>, <c>appmanifest_*.acf</c>; M12): quoted keys with a
/// quoted value or a nested <c>{ … }</c> block, <c>//</c> comments, backslash escapes. Keys compare case-insensitively.
/// A small reader instead of a dependency (hard rule 6).
/// </summary>
public static class ValveKeyValues
{
    /// <summary>The top-level keys; values are <see cref="string"/> or a nested dictionary.</summary>
    /// <exception cref="FormatException">The text is not well-formed (unterminated string or block).</exception>
    public static IReadOnlyDictionary<string, object> Parse(string text)
    {
        var position = 0;
        var root = ReadBlock(text, ref position, nested: false);
        return root;
    }

    private static Dictionary<string, object> ReadBlock(string text, ref int position, bool nested)
    {
        var block = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            SkipSpaceAndComments(text, ref position);
            if (position >= text.Length)
            {
                if (nested) throw new FormatException("unterminated block");
                return block;
            }
            if (text[position] == '}')
            {
                if (!nested) throw new FormatException("unexpected '}'");
                position++;
                return block;
            }
            var key = ReadString(text, ref position);
            SkipSpaceAndComments(text, ref position);
            if (position >= text.Length) throw new FormatException($"no value for \"{key}\"");
            if (text[position] == '{')
            {
                position++;
                block[key] = ReadBlock(text, ref position, nested: true);
            }
            else
            {
                block[key] = ReadString(text, ref position);
            }
        }
    }

    private static string ReadString(string text, ref int position)
    {
        if (text[position] != '"')
        {
            // Unquoted token (allowed by the format, rare in Steam's files): up to whitespace or a brace.
            var start = position;
            while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not ('{' or '}' or '"')) position++;
            if (position == start) throw new FormatException($"unexpected '{text[position]}'");
            return text[start..position];
        }
        position++;
        var value = new StringBuilder();
        while (position < text.Length)
        {
            var current = text[position++];
            if (current == '"') return value.ToString();
            if (current == '\\' && position < text.Length)
            {
                var escaped = text[position++];
                value.Append(escaped switch { 'n' => '\n', 't' => '\t', _ => escaped });
                continue;
            }
            value.Append(current);
        }
        throw new FormatException("unterminated string");
    }

    private static void SkipSpaceAndComments(string text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position])) position++;
            else if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n') position++;
            }
            else return;
        }
    }
}
