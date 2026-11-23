using System.Windows;
using System.Windows.Threading;
using Island.Core.Layout;
using Island.Interop;
using Island.Views;

namespace Island.Services;

public enum ToastKind { Info, Success, Error }

public sealed record ToastAction(string Text, Action Callback);

/// <summary>Island-styled toasts that stack next to the island (no focus stealing, no Windows notification setup needed).</summary>
public sealed class ToastService
{
    private readonly Dispatcher _dispatcher;
    private readonly List<ToastWindow> _open = new();

    /// <summary>Returns the visible island rect, its corner and its monitor's work area (physical pixels).</summary>
    public Func<(PixelRect Island, Corner Corner, PixelRect WorkArea)?>? AnchorProvider { get; set; }

    public ToastService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Info(string message, string? title = null, params ToastAction[] actions) => Show(title, message, ToastKind.Info, actions);
    public void Success(string message, string? title = null, params ToastAction[] actions) => Show(title, message, ToastKind.Success, actions);
    public void Error(string message, string? title = null, params ToastAction[] actions) => Show(title, message, ToastKind.Error, actions);

    public void Show(string? title, string message, ToastKind kind, IReadOnlyList<ToastAction>? actions = null)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => Show(title, message, kind, actions));
            return;
        }

        var duration = TimeSpan.FromSeconds(kind == ToastKind.Error ? 8 : 4.5);
        var w = new ToastWindow(title, message, kind, actions ?? Array.Empty<ToastAction>(), duration);
        w.Closed += (_, _) =>
        {
            _open.Remove(w);
            Relayout();
        };
        // Show off-screen first so it is measured at the right DPI, then place it.
        w.Left = -10000;
        w.Top = -10000;
        _open.Add(w);
        w.Show();
        w.Dispatcher.BeginInvoke(Relayout, DispatcherPriority.Loaded);

        foreach (var old in _open.Take(Math.Max(0, _open.Count - 4)).ToList()) old.CloseAnimated();
    }

    private void Relayout()
    {
        var anchor = AnchorProvider?.Invoke();
        PixelRect work;
        Corner corner;
        PixelRect island;
        if (anchor is { } a)
        {
            (island, corner, work) = a;
        }
        else
        {
            var mon = SnapCalculator.Primary(MonitorService.GetMonitors());
            work = mon.WorkArea;
            corner = Corner.BottomRight;
            island = new PixelRect(work.Right - 16, work.Bottom - 16, 0, 0);
        }

        // Toasts stack vertically away from the island's screen edge.
        int edge = corner.IsBottom() ? island.Top : island.Bottom;
        for (int i = _open.Count - 1; i >= 0; i--)
        {
            var w = _open[i];
            if (!w.IsLoaded) continue;
            var r = WindowHelper.GetRect(w);
            double scale = WindowHelper.GetScale(w);
            int pad = (int)Math.Round(12 * scale);   // the toast's transparent shadow margin
            int gap = (int)Math.Round(8 * scale);
            int x = corner.IsRight() ? island.Right - r.Width + pad : island.Left - pad;
            int y = corner.IsBottom() ? edge - gap - r.Height + pad : edge + gap - pad;
            var p = SnapCalculator.Clamp(new PixelRect(x, y, r.Width, r.Height), work, pad);
            WindowHelper.MoveTo(w, p.X, p.Y);
            edge = corner.IsBottom() ? p.Y + pad : p.Y + r.Height - pad;
        }
    }
}
