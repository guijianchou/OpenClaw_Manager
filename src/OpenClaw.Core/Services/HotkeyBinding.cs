// Copyright (c) Lanstack @openclaw. All rights reserved.

namespace OpenClaw.Services;

/// <summary>
/// Represents a parsed global hotkey binding (modifier keys + a single virtual key).
/// Supports parsing from and serializing to a human-readable string like "Ctrl+Alt+Space".
/// </summary>
public sealed class HotkeyBinding
{
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }
    public bool Win { get; init; }
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Parses a hotkey string like "Ctrl+Alt+Space" or "Shift+F12".
    /// Returns null if the input is empty, whitespace, or has no valid key component.
    /// </summary>
    public static HotkeyBinding? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var parts = input.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var ctrl = false;
        var alt = false;
        var shift = false;
        var win = false;
        string? key = null;

        foreach (var part in parts)
        {
            var normalized = part.ToLowerInvariant();
            switch (normalized)
            {
                case "ctrl" or "control":
                    ctrl = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "win" or "windows" or "super" or "meta":
                    win = true;
                    break;
                default:
                    // Last non-modifier part is the key
                    key = part;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return new HotkeyBinding
        {
            Ctrl = ctrl,
            Alt = alt,
            Shift = shift,
            Win = win,
            Key = key,
        };
    }

    /// <summary>
    /// Serializes the binding back to a human-readable string like "Ctrl+Alt+Space".
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        if (Win)
        {
            parts.Add("Win");
        }

        parts.Add(Key);
        return string.Join('+', parts);
    }

    /// <summary>
    /// Gets the Win32 modifier flags for use with RegisterHotKey.
    /// </summary>
    public int GetWin32Modifiers()
    {
        int modifiers = 0;
        if (Alt)
        {
            modifiers |= 0x0001;   // MOD_ALT
        }

        if (Ctrl)
        {
            modifiers |= 0x0002;  // MOD_CONTROL
        }

        if (Shift)
        {
            modifiers |= 0x0004; // MOD_SHIFT
        }

        if (Win)
        {
            modifiers |= 0x0008;   // MOD_WIN
        }

        modifiers |= 0x4000;            // MOD_NOREPEAT
        return modifiers;
    }

    /// <summary>
    /// Gets the Win32 virtual key code for the key component.
    /// Returns 0 if the key cannot be mapped.
    /// </summary>
    public int GetVirtualKeyCode()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            return 0;
        }

        var normalized = Key.ToUpperInvariant();

        // Function keys F1-F24
        if (normalized.StartsWith('F') && int.TryParse(normalized.AsSpan(1), out var fNum) && fNum >= 1 && fNum <= 24)
        {
            return 0x70 + (fNum - 1); // VK_F1 = 0x70
        }

        // Single character A-Z, 0-9
        if (normalized.Length == 1)
        {
            var ch = normalized[0];
            if (ch is >= 'A' and <= 'Z')
            {
                return ch;
            }

            if (ch is >= '0' and <= '9')
            {
                return ch;
            }
        }

        // Named keys
        return normalized switch
        {
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" or "ESC" => 0x1B,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "PAUSE" => 0x13,
            "CAPSLOCK" => 0x14,
            "NUMLOCK" => 0x90,
            "SCROLLLOCK" => 0x91,
            "PRINTSCREEN" or "PRTSC" => 0x2C,
            _ => 0,
        };
    }
}
