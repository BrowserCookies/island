using Island.Core.Layout;
using Island.Core.Models;

namespace Island.Tests;

public class SnapTests
{
    // Primary 1920x1080 with a 40px taskbar at the bottom; secondary 2560x1440 to the right at 150%.
    private static readonly MonitorInfo Primary = new(@"\\.\DISPLAY1",
        new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), true, 1.0);
    private static readonly MonitorInfo Secondary = new(@"\\.\DISPLAY2",
        new PixelRect(1920, 0, 2560, 1440), new PixelRect(1920, 0, 2560, 1400), false, 1.5);
    private static readonly IReadOnlyList<MonitorInfo> Monitors = new[] { Primary, Secondary };

    [Theory]
    [InlineData(10, 10, Corner.TopLeft)]
    [InlineData(1800, 20, Corner.TopRight)]
    [InlineData(50, 900, Corner.BottomLeft)]
    [InlineData(1500, 700, Corner.BottomRight)]
    public void NearestCorner_UsesQuadrant(int x, int y, Corner expected)
    {
        Assert.Equal(expected, SnapCalculator.NearestCorner(new PixelRect(x, y, 56, 56), Primary.WorkArea));
    }

    [Fact]
    public void CornerPosition_RespectsMarginAndTaskbar()
    {
        var p = SnapCalculator.CornerPosition(Corner.BottomRight, Primary.WorkArea, 56, 56, 16);
        Assert.Equal(new PixelPoint(1920 - 16 - 56, 1040 - 16 - 56), p);
        var tl = SnapCalculator.CornerPosition(Corner.TopLeft, Primary.WorkArea, 56, 56, 16);
        Assert.Equal(new PixelPoint(16, 16), tl);
    }

    [Fact]
    public void CornerPosition_AccountsForShadowInset()
    {
        // 56px pill inside a 72px window (8px shadow padding each side).
        var p = SnapCalculator.CornerPosition(Corner.BottomRight, Primary.WorkArea, 72, 72, 16, inset: 8);
        // Visible pill right edge = p.X + 72 - 8 must be 16px from the work area edge.
        Assert.Equal(1920 - 16, p.X + 72 - 8);
        Assert.Equal(1040 - 16, p.Y + 72 - 8);
    }

    [Fact]
    public void FindMonitor_PicksLargestOverlap()
    {
        var straddling = new PixelRect(1900, 100, 56, 56); // mostly on secondary
        Assert.Equal(Secondary, SnapCalculator.FindMonitor(straddling, Monitors));
    }

    [Fact]
    public void FindMonitor_OffScreen_PicksClosest()
    {
        Assert.Equal(Secondary, SnapCalculator.FindMonitor(new PixelRect(5000, 200, 56, 56), Monitors));
        Assert.Equal(Primary, SnapCalculator.FindMonitor(new PixelRect(-500, 200, 56, 56), Monitors));
    }

    [Fact]
    public void ResolveDrop_Snaps_OnSecondaryMonitor()
    {
        var (pos, corner, mon) = SnapCalculator.ResolveDrop(new PixelRect(4300, 1300, 84, 84), Monitors, snap: true, margin: 24);
        Assert.Equal(Secondary, mon);
        Assert.Equal(Corner.BottomRight, corner);
        Assert.Equal(new PixelPoint(1920 + 2560 - 24 - 84, 1400 - 24 - 84), pos);
    }

    [Fact]
    public void ResolveDrop_NoSnap_ClampsOnScreen()
    {
        var (pos, _, _) = SnapCalculator.ResolveDrop(new PixelRect(1890, 1030, 56, 56), new[] { Primary }, snap: false, margin: 16);
        Assert.Equal(new PixelPoint(1920 - 56, 1040 - 56), pos);
    }

    [Fact]
    public void ResolveDrop_NoSnap_KeepsPositionInside()
    {
        var (pos, _, _) = SnapCalculator.ResolveDrop(new PixelRect(500, 400, 56, 56), new[] { Primary }, snap: false, margin: 16);
        Assert.Equal(new PixelPoint(500, 400), pos);
    }

    [Fact]
    public void ResolveSaved_MissingMonitor_FallsBackToPrimaryBottomRight()
    {
        var state = new IslandState { Monitor = @"\\.\DISPLAY9", Corner = Corner.TopLeft };
        var (pos, corner, mon) = SnapCalculator.ResolveSaved(state, Monitors, 56, 56, snap: true, margin: 16);
        Assert.Equal(Primary, mon);
        Assert.Equal(Corner.BottomRight, corner);
        Assert.Equal(new PixelPoint(1920 - 72, 1040 - 72), pos);
    }

    [Fact]
    public void ResolveSaved_NoState_FallsBackToPrimaryBottomRight()
    {
        var (_, corner, mon) = SnapCalculator.ResolveSaved(null, Monitors, 56, 56, true, 16);
        Assert.Equal(Primary, mon);
        Assert.Equal(Corner.BottomRight, corner);
    }

    [Fact]
    public void ResolveSaved_RestoresCornerOnSavedMonitor()
    {
        var state = new IslandState { Monitor = @"\\.\display2", Corner = Corner.TopLeft };
        var (pos, corner, mon) = SnapCalculator.ResolveSaved(state, Monitors, 84, 84, true, 16);
        Assert.Equal(Secondary, mon);
        Assert.Equal(Corner.TopLeft, corner);
        Assert.Equal(new PixelPoint(1920 + 16, 16), pos);
    }

    [Fact]
    public void ResolveSaved_ExactPosition_WhenSnapOff()
    {
        var state = new IslandState { Monitor = @"\\.\DISPLAY1", Exact = true, OffsetX = 300, OffsetY = 200 };
        var (pos, _, _) = SnapCalculator.ResolveSaved(state, Monitors, 56, 56, snap: false, margin: 16);
        Assert.Equal(new PixelPoint(300, 200), pos);
    }

    [Fact]
    public void Easing_CubicOut_Bounds()
    {
        Assert.Equal(0, Easing.CubicOut(0));
        Assert.Equal(1, Easing.CubicOut(1));
        Assert.True(Easing.CubicOut(0.5) > 0.5); // ease-out is ahead of linear
        Assert.Equal(1, Easing.CubicOut(2));
        Assert.Equal(150, Easing.Lerp(100, 200, 0.5));
    }

    [Fact]
    public void Panel_BottomRight_Row_OpensLeft()
    {
        var island = new PixelRect(1848, 968, 56, 56);
        var p = PanelPlacement.Compute(island, 300, 64, Corner.BottomRight, ExpandLayout.Row, Primary.WorkArea, gap: 8);
        Assert.Equal(1848 - 8 - 300, p.X);
        Assert.Equal(968 + 56 - 64, p.Y); // bottom-aligned with island
    }

    [Fact]
    public void Panel_BottomRight_Column_OpensUp()
    {
        var island = new PixelRect(1848, 968, 56, 56);
        var p = PanelPlacement.Compute(island, 64, 300, Corner.BottomRight, ExpandLayout.Column, Primary.WorkArea, gap: 8);
        Assert.Equal(968 - 8 - 300, p.Y);
        Assert.Equal(1848 + 56 - 64, p.X); // right-aligned with island
    }

    [Fact]
    public void Panel_TopLeft_Grid_OpensDown()
    {
        var island = new PixelRect(16, 16, 56, 56);
        var p = PanelPlacement.Compute(island, 250, 200, Corner.TopLeft, ExpandLayout.Grid, Primary.WorkArea, gap: 8);
        Assert.Equal(16 + 56 + 8, p.Y);
        Assert.Equal(16, p.X);
    }

    [Fact]
    public void Panel_IsClampedIntoWorkArea()
    {
        var island = new PixelRect(16, 968, 56, 56);
        // Bottom-left, column, but a panel taller than the space above.
        var p = PanelPlacement.Compute(island, 64, 2000, Corner.BottomLeft, ExpandLayout.Column, Primary.WorkArea, gap: 8);
        Assert.Equal(0, p.Y);
    }
}
