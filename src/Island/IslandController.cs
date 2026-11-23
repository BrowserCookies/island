using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Island.Core.Layout;
using Island.Core.Models;
using Island.Core.Util;
using Island.Interop;
using Island.Services;
using Island.ViewModels;
using Island.Views;
using static Island.Interop.NativeMethods;

namespace Island;

/// <summary>
/// Orchestrates the island, its expanded panel, global hotkeys, the tray menu and the
/// secondary windows (settings, run log, output popups).
/// </summary>
public sealed class IslandController : IIslandHost, IDisposable
{
    private const uint VK_ESCAPE = 0x1B;

    private readonly AppServices _s;
    private readonly IslandWindow _island;
    private readonly PanelWindow _panel;
    private readonly PanelViewModel _panelVm = new();
    private IslandState _state;

    private SettingsWindow? _settingsWindow;
    private RunLogWindow? _runLogWindow;

    private readonly List<int> _hotkeyIds = new();
    private int? _escHotkey;
    private IntPtr _mouseHook;
    private readonly LowLevelProc _mouseProc;

    public IslandController(AppServices services)
    {
        _s = services;
        _s.Host = this;
        _mouseProc = MouseHookProc;
        _state = _s.Config.Store.LoadState();

        _island = new IslandWindow();
        _island.Clicked += (_, _) => Toggle();
        _island.DragStarted += (_, _) => Collapse();
        _island.Moved += (_, _) =>
        {
            if (IsExpanded) PositionPanel();
        };
        _island.PositionStateChanged += (_, st) => _s.Config.Store.SaveState(st);
        _island.RightClicked += (_, _) => ShowIslandContextMenu();

        _panel = new PanelWindow(_panelVm);
        _panel.TileClicked += (_, tile) => OnTileClicked(tile);
        _panel.SizeChanged += (_, _) =>
        {
            if (IsExpanded) PositionPanel();
        };

        _s.Actions.RunningChanged += (_, id) =>
        {
            bool running = _s.Actions.IsRunning(id);
            _panelVm.SetRunning(id, running);
            _island.SetBusy(_s.Actions.AnyRunning);
        };

        _s.Toasts.AnchorProvider = () =>
        {
            if (!_island.IsVisible) return null;
            var mon = _island.CurrentMonitor;
            return (_island.VisibleRect, _island.Corner, mon.WorkArea);
        };

        _s.Config.ConfigChanged += (_, _) => ApplyConfig();
        _s.Theme.ThemeChanged += (_, _) => UpdateSettingsWindowChrome();
    }

    public bool IsExpanded { get; private set; }
    public bool IsIslandVisible => _island.IsVisible;

    public void Start()
    {
        ApplyConfig();
        if (!_state.Hidden) ShowIsland();
    }

    private void ApplyConfig()
    {
        var cfg = _s.Config.Current;
        _s.Theme.Apply(cfg.General);
        _island.ApplySettings(cfg.General, _state);
        RegisterHotkeys(cfg.General);
        if (IsExpanded) Collapse();
    }

    // ------------------------------------------------------------------ island visibility

    public void ShowIsland()
    {
        if (!_island.IsVisible)
        {
            _island.Show();
            _island.RestorePosition();
        }
        _state.Hidden = false;
        _s.Config.Store.SaveState(_state);
        _island.ReassertTopmost();
    }

    public void HideIsland()
    {
        Collapse();
        _island.Hide();
        _state.Hidden = true;
        _s.Config.Store.SaveState(_state);
    }

    public void ToggleIslandVisibility()
    {
        if (_island.IsVisible) HideIsland();
        else ShowIsland();
    }

    public void ResetPosition()
    {
        ShowIsland();
        _island.ResetPosition();
    }

    /// <summary>Called when Island is launched a second time.</summary>
    public void Flash()
    {
        ShowIsland();
        _island.Flash();
    }

    // ------------------------------------------------------------------ expand / collapse

    public void Toggle()
    {
        if (IsExpanded) Collapse();
        else Expand();
    }

    public void Expand()
    {
        if (IsExpanded) return;
        if (!_island.IsVisible) ShowIsland();
        var cfg = _s.Config.Current;
        _panelVm.Load(cfg.Buttons, cfg.General, _island.Corner);
        foreach (var t in _panelVm.AllTiles) t.IsRunning = _s.Actions.IsRunning(t.Item.Id);
        _panel.SetCorner(_island.Corner);

        IsExpanded = true;
        if (!_panel.IsVisible)
        {
            // Start on the island's monitor so the panel is measured at the right DPI.
            var r = _island.VisibleRect;
            _panel.Show();
            WindowHelper.MoveTo(_panel, r.X, r.Y);
        }
        _panel.Dispatcher.BeginInvoke(() =>
        {
            PositionPanel();
            _panel.PlayOpenAnimation();
        }, DispatcherPriority.Loaded);

        _island.SetExpanded(true);
        InstallCollapseTriggers();
    }

    public void Collapse()
    {
        if (!IsExpanded) return;
        IsExpanded = false;
        _panel.Hide();
        _panelVm.Caption = null;
        _island.SetExpanded(false);
        RemoveCollapseTriggers();
    }

    private void PositionPanel()
    {
        if (!_panel.IsVisible) return;
        var panelRect = WindowHelper.GetRect(_panel);
        var island = _island.VisibleRect;
        var mon = SnapCalculator.FindMonitor(island, MonitorService.GetMonitors());
        int gap = (int)Math.Round(8 * WindowHelper.GetScale(_island));
        var corner = _s.Config.Current.General.SnapEnabled ? _island.Corner : SnapCalculator.NearestCorner(island, mon.WorkArea);
        _panelVm.SetCorner(corner);
        _panel.SetCorner(corner);
        var p = PanelPlacement.Compute(island, panelRect.Width, panelRect.Height, corner, _s.Config.Current.General.Layout,
            mon.WorkArea, gap, _panel.InsetPx);
        WindowHelper.MoveTo(_panel, p.X, p.Y);
        _island.ReassertTopmost();
    }

    private void OnTileClicked(TileViewModel tile)
    {
        var item = tile.Item;
        if (item.Action is GroupAction)
        {
            _panelVm.ToggleGroup(tile);
            return;
        }

        if (_s.Actions.IsRunning(item.Id))
        {
            _s.Actions.Cancel(item.Id);
            return;
        }

        if (_s.Config.Current.General.CollapseAfterAction) Collapse();
        _ = _s.Actions.RunAsync(item);
    }

    // Esc (temporary hotkey) and click-outside (low-level mouse hook) while the panel is open.
    private void InstallCollapseTriggers()
    {
        if (_s.Hotkeys is not null && _escHotkey is null)
            _escHotkey = _s.Hotkeys.RegisterVirtualKey(0, VK_ESCAPE, () => Collapse());
        if (_mouseHook == IntPtr.Zero)
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
    }

    private void RemoveCollapseTriggers()
    {
        if (_escHotkey is { } id)
        {
            _s.Hotkeys?.Unregister(id);
            _escHotkey = null;
        }
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var hwnd = GetAncestor(WindowFromPoint(data.pt), GA_ROOT);
                if (!IsOwnWindow(hwnd))
                    _island.Dispatcher.BeginInvoke(Collapse);
            }
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    // ------------------------------------------------------------------ hotkeys

    private void RegisterHotkeys(GeneralSettings g)
    {
        if (_s.Hotkeys is null) return;
        foreach (var id in _hotkeyIds) _s.Hotkeys.Unregister(id);
        _hotkeyIds.Clear();

        if (!HotkeyGesture.TryParse(g.Hotkey, out var gesture)) return;

        var main = _s.Hotkeys.Register(gesture, OnHotkey, out var err);
        if (main is { } m) _hotkeyIds.Add(m);
        else _s.Toasts.Error(err ?? $"Couldn't register {gesture}.", "Global hotkey");

        if (!gesture.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            var withShift = _s.Hotkeys.Register(gesture.WithShift(), OpenSettings, out _);
            if (withShift is { } w) _hotkeyIds.Add(w);
        }
    }

    private void OnHotkey()
    {
        if (_s.Config.Current.General.HotkeyAction == HotkeyAction.ToggleVisibility)
        {
            ToggleIslandVisibility();
            return;
        }
        if (!_island.IsVisible) ShowIsland();
        Toggle();
    }

    // ------------------------------------------------------------------ context menu on the island

    private void ShowIslandContextMenu()
    {
        Collapse();
        var menu = new ContextMenu();
        void Add(string header, Action a)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => a();
            menu.Items.Add(mi);
        }
        Add("Settings…", OpenSettings);
        Add("Script run log…", ShowRunLog);
        Add("Reset position", ResetPosition);
        menu.Items.Add(new Separator());
        Add("Hide island", HideIsland);
        Add("Exit Island", () => Application.Current.Shutdown());
        menu.PlacementTarget = _island;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------ IIslandHost

    public void OpenSettings()
    {
        Collapse();
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_s);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        }
        _settingsWindow.Activate();
        SetForegroundWindow(WindowHelper.Handle(_settingsWindow));
        UpdateSettingsWindowChrome();
    }

    private void UpdateSettingsWindowChrome()
    {
        if (_settingsWindow is not null) WindowHelper.SetDarkTitleBar(_settingsWindow, _s.Theme.IsDark);
        if (_runLogWindow is not null) WindowHelper.SetDarkTitleBar(_runLogWindow, _s.Theme.IsDark);
    }

    public void ShowRunLog()
    {
        Collapse();
        if (_runLogWindow is null)
        {
            _runLogWindow = new RunLogWindow(_s);
            _runLogWindow.Closed += (_, _) => _runLogWindow = null;
            _runLogWindow.Show();
        }
        _runLogWindow.Activate();
        SetForegroundWindow(WindowHelper.Handle(_runLogWindow));
        UpdateSettingsWindowChrome();
    }

    public void ShowOutputPopup(string title, string text)
    {
        var w = new OutputPopupWindow(title, text);
        w.SourceInitialized += (_, _) => WindowHelper.SetDarkTitleBar(w, _s.Theme.IsDark);
        w.Show();
        w.Activate();
        SetForegroundWindow(WindowHelper.Handle(w));
    }

    public async Task<T> RunHiddenAsync<T>(Func<T> capture)
    {
        Collapse();
        double oldOpacity = _island.Opacity;
        _island.Opacity = 0;
        try
        {
            await Task.Delay(180); // let DWM present a frame without the island
            return capture();
        }
        finally
        {
            _island.Opacity = oldOpacity;
        }
    }

    public void Dispose()
    {
        RemoveCollapseTriggers();
        if (_s.Hotkeys is not null)
            foreach (var id in _hotkeyIds) _s.Hotkeys.Unregister(id);
        _s.Config.Store.SaveState(_state);
    }
}
