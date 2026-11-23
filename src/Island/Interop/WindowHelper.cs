using System.Windows;
using System.Windows.Interop;
using Island.Core.Layout;
using static Island.Interop.NativeMethods;

namespace Island.Interop;

/// <summary>Physical-pixel window placement helpers and the "floating tool window" setup.</summary>
internal static class WindowHelper
{
    public static IntPtr Handle(Window w) => new WindowInteropHelper(w).EnsureHandle();

    /// <summary>
    /// Makes a window a non-activating, topmost tool window: not in the taskbar or Alt+Tab,
    /// and clicking it doesn't steal focus from the app the user was in.
    /// </summary>
    public static void MakeFloating(Window w, bool noActivate = true)
    {
        var hwnd = Handle(w);
        int add = WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
        if (noActivate) add |= WS_EX_NOACTIVATE;
        AddExStyle(hwnd, add);
        RemoveExStyle(hwnd, WS_EX_APPWINDOW);
        if (noActivate)
        {
            HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == WM_MOUSEACTIVATE)
                {
                    handled = true;
                    return new IntPtr(MA_NOACTIVATE);
                }
                return IntPtr.Zero;
            });
        }
    }

    public static PixelRect GetRect(Window w)
    {
        GetWindowRect(Handle(w), out var r);
        return PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static PixelRect GetRect(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static void MoveTo(Window w, int x, int y)
    {
        SetWindowPos(Handle(w), HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public static void AssertTopmost(Window w)
    {
        SetWindowPos(Handle(w), HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public static PixelPoint CursorPosition()
    {
        GetCursorPos(out var p);
        return new PixelPoint(p.X, p.Y);
    }

    /// <summary>DPI scale (1.0 = 96 DPI) of the monitor the window is on.</summary>
    public static double GetScale(Window w)
    {
        try
        {
            uint dpi = GetDpiForWindow(Handle(w));
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch (EntryPointNotFoundException)
        {
            return 1.0;
        }
    }

    /// <summary>Dark title bar for normal (settings) windows on Windows 10 20H1+ / 11.</summary>
    public static void SetDarkTitleBar(Window w, bool dark)
    {
        try
        {
            int v = dark ? 1 : 0;
            DwmSetWindowAttribute(Handle(w), DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
