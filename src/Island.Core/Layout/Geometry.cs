using System;

namespace Island.Core.Layout;

public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

public static class CornerExtensions
{
    public static bool IsRight(this Corner c) => c is Corner.TopRight or Corner.BottomRight;
    public static bool IsBottom(this Corner c) => c is Corner.BottomLeft or Corner.BottomRight;
}

/// <summary>Integer rectangle in physical (device) pixels, like a Win32 RECT.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;

    public static PixelRect FromLTRB(int l, int t, int r, int b) => new(l, t, r - l, b - t);

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };
    public PixelRect At(int x, int y) => this with { X = x, Y = y };

    public bool Contains(int px, int py) => px >= Left && px < Right && py >= Top && py < Bottom;

    public long IntersectionArea(PixelRect o)
    {
        long w = Math.Min(Right, o.Right) - Math.Max(Left, o.Left);
        long h = Math.Min(Bottom, o.Bottom) - Math.Max(Top, o.Top);
        return w > 0 && h > 0 ? w * h : 0;
    }

    /// <summary>Squared distance from a point to this rectangle (0 if inside).</summary>
    public long DistanceSquaredTo(int px, int py)
    {
        long dx = px < Left ? Left - px : px >= Right ? px - (Right - 1) : 0;
        long dy = py < Top ? Top - py : py >= Bottom ? py - (Bottom - 1) : 0;
        return dx * dx + dy * dy;
    }
}

public readonly record struct PixelPoint(int X, int Y);

/// <summary>A display as seen by the layout logic (all values in physical pixels).</summary>
public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary, double Scale = 1.0);

public static class Easing
{
    /// <summary>Cubic ease-out: fast start, gentle stop. t in [0,1].</summary>
    public static double CubicOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        double f = 1 - t;
        return 1 - f * f * f;
    }

    public static int Lerp(int from, int to, double t) => (int)Math.Round(from + (to - from) * t);
}
