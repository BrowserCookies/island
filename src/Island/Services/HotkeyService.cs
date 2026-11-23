using System.Windows.Input;
using System.Windows.Interop;
using Island.Core.Util;
using static Island.Interop.NativeMethods;

namespace Island.Services;

/// <summary>Global hotkeys via RegisterHotKey on a hidden message-only window.</summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 0x1000;

    public HotkeyService()
    {
        var p = new HwndSourceParameters("IslandHotkeys") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */, Width = 0, Height = 0, WindowStyle = 0 };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers a hotkey. Returns an id (for <see cref="Unregister"/>) or null if the combination is taken.</summary>
    public int? Register(HotkeyGesture gesture, Action handler, out string? error)
    {
        error = null;
        if (!TryGetVirtualKey(gesture.Key, out var vk))
        {
            error = $"Unknown key \"{gesture.Key}\".";
            return null;
        }
        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, (uint)gesture.Modifiers | MOD_NOREPEAT, vk))
        {
            int code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            error = code == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"{gesture} is already used by another application."
                : $"Couldn't register {gesture} (error {code}).";
            return null;
        }
        _handlers[id] = handler;
        return id;
    }

    /// <summary>Registers a raw virtual-key hotkey (used for Esc while the panel is open).</summary>
    public int? RegisterVirtualKey(uint modifiers, uint vk, Action handler)
    {
        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, modifiers | MOD_NOREPEAT, vk)) return null;
        _handlers[id] = handler;
        return id;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id)) UnregisterHotKey(_source.Handle, id);
    }

    /// <summary>Checks whether a hotkey could be registered right now (registers and immediately releases it).</summary>
    public bool IsAvailable(HotkeyGesture gesture, out string? error)
    {
        var id = Register(gesture, () => { }, out error);
        if (id is null) return false;
        Unregister(id.Value);
        return true;
    }

    public static bool TryGetVirtualKey(string keyName, out uint vk)
    {
        vk = 0;
        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
        {
            // Allow digits written as "1" instead of "D1".
            if (keyName.Length == 1 && char.IsDigit(keyName[0]) && Enum.TryParse("D" + keyName, out key)) { }
            else return false;
        }
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var h))
        {
            handled = true;
            h();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys.ToList()) Unregister(id);
        _source.Dispose();
    }
}
