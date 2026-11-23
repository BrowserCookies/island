using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media.Imaging;
using Island.Core.Layout;
using static Island.Interop.NativeMethods;

namespace Island.Services;

public static class ScreenshotService
{
    /// <summary>Captures all monitors (the whole virtual screen).</summary>
    public static Bitmap CaptureFullScreen()
    {
        var mons = MonitorService.GetMonitors();
        int l = mons.Min(m => m.Bounds.Left), t = mons.Min(m => m.Bounds.Top);
        int r = mons.Max(m => m.Bounds.Right), b = mons.Max(m => m.Bounds.Bottom);
        return Capture(PixelRect.FromLTRB(l, t, r, b));
    }

    /// <summary>Captures a window's visible frame (without the invisible resize borders / shadow).</summary>
    public static Bitmap CaptureWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) throw new InvalidOperationException("There is no active window to capture.");
        if (IsIconic(hwnd)) throw new InvalidOperationException("The active window is minimized.");

        PixelRect rect;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0)
            rect = PixelRect.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        else
        {
            GetWindowRect(hwnd, out var wr);
            rect = PixelRect.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
        }
        if (rect.Width <= 0 || rect.Height <= 0) throw new InvalidOperationException("The active window has no visible area.");
        return Capture(rect);
    }

    private static Bitmap Capture(PixelRect rect)
    {
        var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(rect.Width, rect.Height), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static string Save(Bitmap bmp, string folder)
    {
        Directory.CreateDirectory(folder);
        var baseName = $"Screenshot {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
        var path = Path.Combine(folder, baseName + ".png");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{baseName} ({i}).png");
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var h = bmp.GetHbitmap();
        try
        {
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            DeleteObject(h);
        }
    }
}
