using System;
using System.Collections.Generic;
using System.Linq;
using Island.Core.Models;

namespace Island.Core.Layout;

/// <summary>
/// Pure snapping / placement math. Everything is in physical pixels.
/// "Window" rectangles include any transparent shadow padding (the <c>inset</c>):
/// the visible pill sits <c>inset</c> pixels inside the window on every side, and
/// margins are measured from the visible pill, not the window.
/// </summary>
public static class SnapCalculator
{
    /// <summary>The monitor that contains most of <paramref name="rect"/>, else the closest one.</summary>
    public static MonitorInfo FindMonitor(PixelRect rect, IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("No monitors.", nameof(monitors));
        var best = monitors.MaxBy(m => m.Bounds.IntersectionArea(rect))!;
        if (best.Bounds.IntersectionArea(rect) > 0) return best;
        return monitors.MinBy(m => m.Bounds.DistanceSquaredTo(rect.CenterX, rect.CenterY))!;
    }

    public static MonitorInfo Primary(IReadOnlyList<MonitorInfo> monitors) =>
        monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

    /// <summary>The work-area corner closest to the centre of <paramref name="rect"/>.</summary>
    public static Corner NearestCorner(PixelRect rect, PixelRect workArea)
    {
        bool right = rect.CenterX >= workArea.CenterX;
        bool bottom = rect.CenterY >= workArea.CenterY;
        return (right, bottom) switch
        {
            (false, false) => Corner.TopLeft,
            (true, false) => Corner.TopRight,
            (false, true) => Corner.BottomLeft,
            _ => Corner.BottomRight,
        };
    }

    /// <summary>Top-left window position that puts the visible pill <paramref name="margin"/> px from the corner.</summary>
    public static PixelPoint CornerPosition(Corner corner, PixelRect workArea, int windowWidth, int windowHeight, int margin, int inset = 0)
    {
        int x = corner.IsRight()
            ? workArea.Right - margin - windowWidth + inset
            : workArea.Left + margin - inset;
        int y = corner.IsBottom()
            ? workArea.Bottom - margin - windowHeight + inset
            : workArea.Top + margin - inset;
        return new PixelPoint(x, y);
    }

    /// <summary>Keeps the visible pill fully inside <paramref name="area"/>.</summary>
    public static PixelPoint Clamp(PixelRect window, PixelRect area, int inset = 0)
    {
        int minX = area.Left - inset, maxX = area.Right - window.Width + inset;
        int minY = area.Top - inset, maxY = area.Bottom - window.Height + inset;
        int x = maxX < minX ? minX : Math.Clamp(window.X, minX, maxX);
        int y = maxY < minY ? minY : Math.Clamp(window.Y, minY, maxY);
        return new PixelPoint(x, y);
    }

    /// <summary>Where a dragged island should end up when released.</summary>
    public static (PixelPoint Position, Corner Corner, MonitorInfo Monitor) ResolveDrop(
        PixelRect window, IReadOnlyList<MonitorInfo> monitors, bool snap, int margin, int inset = 0)
    {
        var mon = FindMonitor(window, monitors);
        var corner = NearestCorner(window, mon.WorkArea);
        var pos = snap
            ? CornerPosition(corner, mon.WorkArea, window.Width, window.Height, margin, inset)
            : Clamp(window, mon.WorkArea, inset);
        return (pos, corner, mon);
    }

    /// <summary>
    /// Restores a saved position. If the saved monitor no longer exists, falls back to
    /// the primary monitor's bottom-right corner.
    /// </summary>
    public static (PixelPoint Position, Corner Corner, MonitorInfo Monitor) ResolveSaved(
        IslandState? state, IReadOnlyList<MonitorInfo> monitors, int windowWidth, int windowHeight,
        bool snap, int margin, int inset = 0)
    {
        var mon = state?.Monitor is { } name
            ? monitors.FirstOrDefault(m => string.Equals(m.DeviceName, name, StringComparison.OrdinalIgnoreCase))
            : null;

        if (mon is null || state is null)
        {
            var primary = Primary(monitors);
            return (CornerPosition(Corner.BottomRight, primary.WorkArea, windowWidth, windowHeight, margin, inset),
                    Corner.BottomRight, primary);
        }

        if (state.Exact && !snap)
        {
            var r = new PixelRect(mon.WorkArea.X + state.OffsetX, mon.WorkArea.Y + state.OffsetY, windowWidth, windowHeight);
            var p = Clamp(r, mon.WorkArea, inset);
            var corner = NearestCorner(r.At(p.X, p.Y), mon.WorkArea);
            return (p, corner, mon);
        }

        return (CornerPosition(state.Corner, mon.WorkArea, windowWidth, windowHeight, margin, inset), state.Corner, mon);
    }
}

/// <summary>Decides where the expanded panel goes so it opens away from the island's corner.</summary>
public static class PanelPlacement
{
    /// <param name="island">Visible island rectangle (without shadow padding).</param>
    /// <param name="panelWidth">Panel window width.</param>
    /// <param name="panelHeight">Panel window height.</param>
    /// <param name="corner">Corner the island is snapped to (or nearest corner).</param>
    /// <param name="layout">Row panels open sideways; Column and Grid panels open vertically.</param>
    /// <param name="workArea">Work area of the island's monitor.</param>
    /// <param name="gap">Space between the island and the panel.</param>
    /// <param name="inset">Transparent padding around the panel's visible content.</param>
    public static PixelPoint Compute(PixelRect island, int panelWidth, int panelHeight, Corner corner,
        ExpandLayout layout, PixelRect workArea, int gap, int inset = 0)
    {
        int x, y;
        if (layout == ExpandLayout.Row)
        {
            x = corner.IsRight() ? island.Left - gap - panelWidth + inset : island.Right + gap - inset;
            y = corner.IsBottom() ? island.Bottom - panelHeight + inset : island.Top - inset;
        }
        else
        {
            y = corner.IsBottom() ? island.Top - gap - panelHeight + inset : island.Bottom + gap - inset;
            x = corner.IsRight() ? island.Right - panelWidth + inset : island.Left - inset;
        }

        var clamped = SnapCalculator.Clamp(new PixelRect(x, y, panelWidth, panelHeight), workArea, inset);
        return clamped;
    }
}
