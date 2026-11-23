using Island.Interop;
using static Island.Interop.NativeMethods;

namespace Island.Services;

/// <summary>
/// Remembers the last foreground window that isn't one of Island's own, so actions like
/// "Claude Code here" know where the user was before clicking the island.
/// </summary>
public sealed class ForegroundTracker : IDisposable
{
    private readonly WinEventProc _proc; // must stay referenced while the hook is installed
    private IntPtr _hook;

    public ForegroundTracker()
    {
        _proc = OnForegroundChanged;
        var fg = GetForegroundWindow();
        if (!IsOwnWindow(fg)) LastExternalWindow = fg;
        // Out-of-context hooks are delivered on this (UI) thread's message loop.
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>Last foreground window outside Island (may have been closed since; check with IsWindow).</summary>
    public IntPtr LastExternalWindow { get; private set; }

    public IntPtr LastExternalWindowIfAlive =>
        LastExternalWindow != IntPtr.Zero && IsWindow(LastExternalWindow) ? LastExternalWindow : IntPtr.Zero;

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd)) return;
        LastExternalWindow = hwnd;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
