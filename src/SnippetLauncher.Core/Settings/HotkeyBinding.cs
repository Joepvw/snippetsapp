namespace SnippetLauncher.Core.Settings;

/// <summary>
/// Represents a global hotkey binding as modifiers + virtual key code.
/// Serialized/displayed as e.g. "Ctrl+Shift+Space".
/// </summary>
public readonly record struct HotkeyBinding(uint Modifiers, uint VirtualKey)
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    public static HotkeyBinding? TryParse(string? binding)
    {
        if (string.IsNullOrWhiteSpace(binding)) return null;

        var parts = binding.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        uint mods = 0;
        uint vk = 0;

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": mods |= MOD_CTRL; break;
                case "ALT": mods |= MOD_ALT; break;
                case "SHIFT": mods |= MOD_SHIFT; break;
                case "WIN": mods |= MOD_WIN; break;
                default:
                    var candidate = ParseVirtualKey(part);
                    if (candidate == 0) return null;
                    vk = candidate;
                    break;
            }
        }

        if (vk == 0) return null;
        return new HotkeyBinding(mods, vk);
    }

    private static uint ParseVirtualKey(string key) => key.ToUpperInvariant() switch
    {
        "SPACE" => 0x20,
        "ENTER" => 0x0D,
        "TAB" => 0x09,
        "ESC" or "ESCAPE" => 0x1B,
        "F1" => 0x70,
        "F2" => 0x71,
        "F3" => 0x72,
        "F4" => 0x73,
        "F5" => 0x74,
        "F6" => 0x75,
        "F7" => 0x76,
        "F8" => 0x77,
        "F9" => 0x78,
        "F10" => 0x79,
        "F11" => 0x7A,
        "F12" => 0x7B,
        // Letters A–Z
        var s when s.Length == 1 && s[0] >= 'A' && s[0] <= 'Z' => (uint)s[0],
        // Digits 0–9
        var s when s.Length == 1 && s[0] >= '0' && s[0] <= '9' => (uint)s[0],
        _ => 0,
    };

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & MOD_CTRL) != 0) parts.Add("Ctrl");
        if ((Modifiers & MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Modifiers & MOD_WIN) != 0) parts.Add("Win");
        parts.Add(VkToName(VirtualKey));
        return string.Join("+", parts);
    }

    private static string VkToName(uint vk) => vk switch
    {
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x1B => "Escape",
        >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
        >= (uint)'A' and <= (uint)'Z' => ((char)vk).ToString(),
        >= (uint)'0' and <= (uint)'9' => ((char)vk).ToString(),
        _ => $"0x{vk:X2}",
    };
}
