using System;
using System.Collections.Generic;
using System.Linq;

namespace Island.Core.Util;

/// <summary>Same bit values as the Win32 MOD_* flags used by RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A hotkey like "Ctrl+Alt+Space". <see cref="Key"/> is a WPF <c>System.Windows.Input.Key</c> name.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, HotkeyModifiers> ModNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = HotkeyModifiers.Ctrl,
        ["Control"] = HotkeyModifiers.Ctrl,
        ["Alt"] = HotkeyModifiers.Alt,
        ["Shift"] = HotkeyModifiers.Shift,
        ["Win"] = HotkeyModifiers.Win,
        ["Windows"] = HotkeyModifiers.Win,
    };

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (var p in parts)
        {
            if (ModNames.TryGetValue(p, out var m))
            {
                mods |= m;
            }
            else
            {
                if (key is not null) return false; // two non-modifier keys
                key = p;
            }
        }
        if (key is null) return false;
        // Global hotkeys without any modifier (other than F-keys) would swallow normal typing.
        if (mods == HotkeyModifiers.None && !(key.Length >= 2 && key[0] is 'F' or 'f' && key.Skip(1).All(char.IsDigit)))
            return false;
        gesture = new HotkeyGesture(mods, NormalizeKey(key));
        return true;
    }

    private static string NormalizeKey(string key) => key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key[1..];

    public HotkeyGesture WithShift() => this with { Modifiers = Modifiers | HotkeyModifiers.Shift };

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}
