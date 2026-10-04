namespace NeoFences.Core.Input;

/// <summary>
/// A global hotkey from settings text ("Ctrl+Alt+Space", spec §4.6 Peek). At least one modifier is required, so a
/// plain key is never stolen from games or apps. <see cref="Key"/> is a key name (WPF's <c>Key</c> names: "Space", "P").
/// </summary>
public sealed record Hotkey(bool Ctrl, bool Alt, bool Shift, bool Win, string Key)
{
    public static bool TryParse(string text, out Hotkey hotkey)
    {
        hotkey = new Hotkey(false, false, false, false, "");
        var parts = text.Split('+').Select(part => part.Trim()).ToList();
        if (parts.Count < 2 || parts.Any(part => part.Length == 0)) return false;

        bool ctrl = false, alt = false, shift = false, win = false;
        string? key = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win" or "windows": win = true; break;
                default:
                    if (key is not null) return false; // two keys
                    // A digit is the top-row key (WPF "D1"); any other number would parse as a raw key code (M5 review M4).
                    if (part.All(char.IsAsciiDigit))
                    {
                        if (part.Length != 1) return false;
                        key = "D" + part;
                        break;
                    }
                    key = part.Length == 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
                    break;
            }
        }
        if (key is null || !(ctrl || alt || shift || win)) return false;
        hotkey = new Hotkey(ctrl, alt, shift, win, key);
        return true;
    }

    private static readonly HashSet<string> SystemCombinations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alt+F4", "Alt+Tab", "Alt+Shift+Tab", "Alt+Escape", "Alt+Space", "Ctrl+Escape",
    };

    /// <summary>
    /// Whether the settings recorder may save this (M6b review I1). A global hotkey swallows its combination in every
    /// app, so it needs Alt or Win (Ctrl/Shift + key are everyday shortcuts and capitals), or an F-key; Windows' own
    /// combinations are refused; and Ctrl+Alt alone with a letter, digit or punctuation key types AltGr characters on
    /// many layouts. config.json stays lenient: this only guards what the user records.
    /// </summary>
    public bool IsSafeToRecord
    {
        get
        {
            if (SystemCombinations.Contains(ToString())) return false;
            var isFunctionKey = Key.Length >= 2 && Key[0] == 'F' && Key[1..].All(char.IsAsciiDigit);
            if (isFunctionKey) return true;
            if (!Alt && !Win) return false;
            var typesAltGr = Ctrl && Alt && !Shift && !Win
                && (Key.Length == 1 || (Key.Length == 2 && Key[0] == 'D' && char.IsAsciiDigit(Key[1])) || Key.StartsWith("Oem", StringComparison.OrdinalIgnoreCase));
            return !typesAltGr;
        }
    }

    private static readonly Dictionary<string, string> KeyCaps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OemPlus"] = "=", ["OemMinus"] = "-", ["OemComma"] = ",", ["OemPeriod"] = ".",
        ["Oem1"] = ";", ["OemSemicolon"] = ";", ["Oem2"] = "/", ["OemQuestion"] = "/", ["Oem3"] = "`", ["OemTilde"] = "`",
        ["Oem4"] = "[", ["OemOpenBrackets"] = "[", ["Oem5"] = "\\", ["OemPipe"] = "\\", ["Oem6"] = "]", ["OemCloseBrackets"] = "]",
        ["Oem7"] = "'", ["OemQuotes"] = "'", ["Add"] = "Num +", ["Subtract"] = "Num -", ["Multiply"] = "Num *", ["Divide"] = "Num /",
        ["Decimal"] = "Num .", ["Return"] = "Enter", ["Back"] = "Backspace", ["Escape"] = "Esc", ["Next"] = "Page Down",
        ["Prior"] = "Page Up", ["Capital"] = "Caps Lock", ["Snapshot"] = "Print Screen",
    };

    /// <summary>
    /// What a person reads (Settings, the tray menu): key caps instead of WPF key names, "Ctrl+Shift+=" for
    /// "Ctrl+Shift+OemPlus" (M6b review carry-over). Storage keeps <see cref="ToString"/>.
    /// </summary>
    public string DisplayText => DisplayTextWith(_ => null);

    /// <summary>
    /// The key caps as this keyboard shows them (M13c): <paramref name="layoutCap"/> gives the character the user's layout puts
    /// on a key ("+" for OemPlus on a German keyboard, not "="), or null to keep the US name.
    /// </summary>
    public string DisplayTextWith(Func<string, string?> layoutCap) =>
        string.Join("+", new[] { Ctrl ? "Ctrl" : null, Alt ? "Alt" : null, Shift ? "Shift" : null, Win ? "Win" : null, layoutCap(Key) ?? KeyCap }.OfType<string>());

    private string KeyCap =>
        KeyCaps.TryGetValue(Key, out var cap) ? cap
        : Key.Length == 2 && Key[0] is 'D' or 'd' && char.IsAsciiDigit(Key[1]) ? Key[1..]
        : Key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && Key.Length == 7 ? "Num " + Key[6..]
        : Key;

    public override string ToString() =>
        string.Join("+", new[] { Ctrl ? "Ctrl" : null, Alt ? "Alt" : null, Shift ? "Shift" : null, Win ? "Win" : null, Key }.OfType<string>());
}
