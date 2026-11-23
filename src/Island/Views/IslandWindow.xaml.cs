using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Island.Core.Layout;
using Island.Core.Models;
using Island.Interop;
using Island.Services;
using Microsoft.Win32;

namespace Island.Views;

/// <summary>
/// The collapsed island: a borderless, transparent, topmost, non-activating pill that can be
/// dragged (with corner snapping) or clicked. All positioning is done in physical pixels.
/// </summary>
public partial class IslandWindow : Window
{
    /// <summary>Transparent padding (DIPs) around the pill, so its shadow isn't clipped.</summary>
    public const double ShadowPad = 10;
    private const double DragThresholdDip = 5;
    private static readonly TimeSpan SnapDuration = TimeSpan.FromMilliseconds(150);

    private readonly DispatcherTimer _topmostTimer;
    private readonly DispatcherTimer _displayChangeDebounce;
    private GeneralSettings _settings = new();
    private IslandState _state = new();

    // drag state
    private bool _pressed;
    private bool _dragging;
    private PixelPoint _pressCursor;
    private PixelRect _pressWindow;

    // snap animation
    private DispatcherTimer? _animTimer;
    private readonly Stopwatch _animClock = new();
    private PixelPoint _animFrom, _animTo;

    private bool _expanded;
    private bool _hovered;

    public IslandWindow()
    {
        InitializeComponent();

        _topmostTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (_, _) => ReassertTopmost(), Dispatcher);
        _displayChangeDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Background, (_, _) =>
        {
            _displayChangeDebounce!.Stop();
            RestorePosition();
            ReassertTopmost();
        }, Dispatcher);
        _displayChangeDebounce.Stop();

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => RestorePosition();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _topmostTimer.Stop();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (!_dragging && !IsAnimating) RestorePosition(); }, DispatcherPriority.Background);
    }

    // ------------------------------------------------------------------ events for the controller

    /// <summary>A click (press + release without moving).</summary>
    public event EventHandler? Clicked;
    /// <summary>A drag started (the controller collapses the panel).</summary>
    public event EventHandler? DragStarted;
    /// <summary>The island settled at a new position (after snapping or a display change).</summary>
    public event EventHandler? Moved;
    public event EventHandler<MouseButtonEventArgs>? RightClicked;
    /// <summary>Persist the state (position).</summary>
    public event EventHandler<IslandState>? PositionStateChanged;

    /// <summary>The corner the island is at right now (may differ from the saved one after a monitor fallback).</summary>
    public Corner Corner => _effectiveCorner;
    private Corner _effectiveCorner = Corner.BottomRight;
    public bool IsAnimating => _animTimer?.IsEnabled == true;

    /// <summary>The visible pill rectangle in physical pixels (window minus the shadow padding).</summary>
    public PixelRect VisibleRect
    {
        get
        {
            var r = WindowHelper.GetRect(this);
            int inset = InsetPx;
            return new PixelRect(r.X + inset, r.Y + inset, Math.Max(0, r.Width - 2 * inset), Math.Max(0, r.Height - 2 * inset));
        }
    }

    public int InsetPx => (int)Math.Round(ShadowPad * WindowHelper.GetScale(this));

    public MonitorInfo CurrentMonitor => SnapCalculator.FindMonitor(WindowHelper.GetRect(this), MonitorService.GetMonitors());

    // ------------------------------------------------------------------ setup

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowHelper.MakeFloating(this);
        HwndSource.FromHwnd(WindowHelper.Handle(this))?.AddHook(WndProc);
        _topmostTimer.Start();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_DISPLAYCHANGE ||
            (msg == NativeMethods.WM_SETTINGCHANGE && wParam.ToInt32() == NativeMethods.SPI_SETWORKAREA))
        {
            ScheduleDisplayRefresh();
        }
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ScheduleDisplayRefresh);

    private void ScheduleDisplayRefresh()
    {
        _displayChangeDebounce.Stop();
        _displayChangeDebounce.Start();
    }

    public void ApplySettings(GeneralSettings settings, IslandState state)
    {
        _settings = settings;
        _state = state;
        double size = settings.IslandSize;
        Width = Height = size + 2 * ShadowPad;
        PillBorder.CornerRadius = new CornerRadius(size / 2);
        Dot.Width = Dot.Height = Math.Round(size * 0.34);
        CloseGlyph.FontSize = Math.Round(size * 0.3);
        UpdateOpacity();
        if (IsLoaded) Dispatcher.BeginInvoke(RestorePosition, DispatcherPriority.Background);
    }

    private void UpdateOpacity()
    {
        Opacity = _hovered || _expanded ? 1.0 : _settings.Opacity;
    }

    // ------------------------------------------------------------------ positioning

    /// <summary>Places the island from the saved state (monitor + corner, or exact position).</summary>
    public void RestorePosition()
    {
        if (!IsLoaded) return;
        StopAnimation();
        var r = WindowHelper.GetRect(this);
        var (pos, corner, mon) = SnapCalculator.ResolveSaved(_state, MonitorService.GetMonitors(), r.Width, r.Height,
            _settings.SnapEnabled, Scaled(_settings.SnapMargin), InsetPx);
        WindowHelper.MoveTo(this, pos.X, pos.Y);
        // Don't overwrite the saved monitor/corner here: if the saved monitor is temporarily
        // missing we fall back to the primary one, and want to return once it is reconnected.
        _effectiveCorner = corner;
        Moved?.Invoke(this, EventArgs.Empty);
    }

    public void ResetPosition()
    {
        _state.Monitor = null;
        _state.Exact = false;
        _state.Corner = Corner.BottomRight;
        RestorePosition();
        PositionStateChanged?.Invoke(this, _state);
    }

    private int Scaled(double dip) => (int)Math.Round(dip * WindowHelper.GetScale(this));

    private void UpdateState(MonitorInfo mon, Corner corner, PixelPoint pos)
    {
        _effectiveCorner = corner;
        _state.Monitor = mon.DeviceName;
        _state.Corner = corner;
        _state.Exact = !_settings.SnapEnabled;
        _state.OffsetX = pos.X - mon.WorkArea.X;
        _state.OffsetY = pos.Y - mon.WorkArea.Y;
    }

    private void SnapAfterDrag()
    {
        var r = WindowHelper.GetRect(this);
        var (pos, corner, mon) = SnapCalculator.ResolveDrop(r, MonitorService.GetMonitors(), _settings.SnapEnabled,
            Scaled(_settings.SnapMargin), InsetPx);
        UpdateState(mon, corner, pos);
        PositionStateChanged?.Invoke(this, _state);
        AnimateTo(pos);
    }

    private void AnimateTo(PixelPoint target)
    {
        StopAnimation();
        var r = WindowHelper.GetRect(this);
        _animFrom = new PixelPoint(r.X, r.Y);
        _animTo = target;
        if (_animFrom == _animTo)
        {
            Moved?.Invoke(this, EventArgs.Empty);
            return;
        }
        _animClock.Restart();
        _animTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, (_, _) => AnimStep(), Dispatcher);
        _animTimer.Start();
    }

    private void AnimStep()
    {
        double t = _animClock.Elapsed.TotalMilliseconds / SnapDuration.TotalMilliseconds;
        double e = Core.Layout.Easing.CubicOut(t);
        WindowHelper.MoveTo(this, Core.Layout.Easing.Lerp(_animFrom.X, _animTo.X, e), Core.Layout.Easing.Lerp(_animFrom.Y, _animTo.Y, e));
        if (t >= 1)
        {
            StopAnimation();
            // If the drop crossed onto a monitor with a different DPI the window size changed
            // during the move; settle precisely using the final size.
            Dispatcher.BeginInvoke(() =>
            {
                RestorePosition();
            }, DispatcherPriority.Background);
        }
    }

    private void StopAnimation()
    {
        if (_animTimer is null) return;
        _animTimer.Stop();
        _animTimer = null;
    }

    public void ReassertTopmost()
    {
        if (!IsVisible || _dragging) return;
        WindowHelper.AssertTopmost(this);
    }

    // ------------------------------------------------------------------ mouse: drag vs click

    private void Pill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StopAnimation();
        _pressed = true;
        _dragging = false;
        _pressCursor = WindowHelper.CursorPosition();
        _pressWindow = WindowHelper.GetRect(this);
        Pill.CaptureMouse();
        e.Handled = true;
    }

    private void Pill_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        var c = WindowHelper.CursorPosition();
        int dx = c.X - _pressCursor.X, dy = c.Y - _pressCursor.Y;
        if (!_dragging)
        {
            double threshold = DragThresholdDip * WindowHelper.GetScale(this);
            if (dx * dx + dy * dy < threshold * threshold) return;
            _dragging = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
        }
        WindowHelper.MoveTo(this, _pressWindow.X + dx, _pressWindow.Y + dy);
    }

    private void Pill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        bool wasDragging = _dragging;
        _pressed = false;
        _dragging = false;
        Pill.ReleaseMouseCapture();
        e.Handled = true;
        if (wasDragging) SnapAfterDrag();
        else Clicked?.Invoke(this, EventArgs.Empty);
    }

    private void Pill_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        bool wasDragging = _dragging;
        _pressed = false;
        _dragging = false;
        if (wasDragging) SnapAfterDrag();
    }

    private void Pill_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        RightClicked?.Invoke(this, e);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ visuals

    private void Pill_MouseEnter(object sender, MouseEventArgs e)
    {
        _hovered = true;
        UpdateOpacity();
        if (_settings.HoverEffect) AnimateScale(PillScale, 1.07, 120);
    }

    private void Pill_MouseLeave(object sender, MouseEventArgs e)
    {
        _hovered = false;
        UpdateOpacity();
        AnimateScale(PillScale, 1.0, 120);
    }

    public void SetExpanded(bool expanded)
    {
        _expanded = expanded;
        UpdateOpacity();
        var d = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        CloseGlyph.BeginAnimation(OpacityProperty, new DoubleAnimation(expanded ? 1 : 0, d) { EasingFunction = ease });
        CloseRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(expanded ? 0 : -90, d) { EasingFunction = ease });
        Dot.BeginAnimation(OpacityProperty, new DoubleAnimation(expanded ? 0 : 1, d) { EasingFunction = ease });
        AnimateScale(DotScale, expanded ? 0.4 : 1.0, 160);
    }

    public void SetBusy(bool busy)
    {
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Attention pulse (used when Island is launched a second time).</summary>
    public void Flash()
    {
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(900) };
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.25, KeyTime.FromPercent(0.2), new SineEase()));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0.45), new SineEase()));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.18, KeyTime.FromPercent(0.65), new SineEase()));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), new SineEase()));
        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private static void AnimateScale(ScaleTransform t, double to, int ms)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        t.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        t.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }
}
