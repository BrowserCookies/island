using System.Windows;
using System.Windows.Threading;
using Island.Core.Config;
using Island.Core.Scripts;
using Island.Services;
using WinForms = System.Windows.Forms;

namespace Island;

public partial class App : Application
{
    private const string MutexName = "Island.SingleInstance.{6C1C2B1E-0E55-4B0B-9C38-7C3A0D5B2E11}";
    private const string ShowEventName = "Island.ShowExisting.{6C1C2B1E-0E55-4B0B-9C38-7C3A0D5B2E11}";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private WinForms.NotifyIcon? _tray;
    private IslandController? _controller;
    private AppServices? _services;
    private ForegroundTracker? _tracker;
    private HotkeyService? _hotkeys;
    private ConfigService? _config;

    protected override void OnStartup(StartupEventArgs e)
    {
        // ---- single instance: a second launch just pokes the first one and exits
        var user = Environment.UserName.Replace('\\', '_');
        _mutex = new Mutex(true, MutexName + "." + user, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ShowEventName + "." + user);
                ev.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => args.SetObserved();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName + "." + user);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
            Dispatcher.BeginInvoke(() => _controller?.Flash()), null, Timeout.Infinite, executeOnlyOnce: false);

        // ---- services
        _config = new ConfigService(ConfigService.DefaultDataDirectory, Dispatcher);
        var load = _config.Load();

        var theme = new ThemeService();
        theme.Apply(_config.Current.General);
        var toasts = new ToastService(Dispatcher);
        _tracker = new ForegroundTracker();
        _hotkeys = new HotkeyService();

        _services = new AppServices
        {
            Dispatcher = Dispatcher,
            Config = _config,
            Toasts = toasts,
            Foreground = _tracker,
            CurrentFolder = new CurrentFolderService(_tracker),
            Theme = theme,
            RunLog = new RunLog(_config.Store.RunLogPath),
            Host = null!, // set by IslandController
            Hotkeys = _hotkeys,
        };
        _ = new ActionRunner(_services);

        _controller = new IslandController(_services);
        _controller.Start();
        CreateTrayIcon();

        // ---- config problems are reported, never silently swallowed
        if (load.Error is not null)
        {
            var source = load.Source == ConfigLoadSource.Backup ? "the backup (config.backup.json)" : "the default settings";
            var preserved = load.PreservedBrokenFile is null ? "" : $"\n\nYour broken file was kept as:\n{load.PreservedBrokenFile}";
            MessageBox.Show(
                $"Island couldn't read config.json:\n\n{load.Error}\n\nIsland is using {source} for now. config.json will only be overwritten when you save settings.{preserved}",
                "Island — configuration problem", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (load.Source == ConfigLoadSource.FirstRun)
        {
            toasts.Info("Click the island to open it, drag it to any corner. Right-click for more.", "Welcome to Island");
        }

        _config.ReloadFailed += (_, msg) => toasts.Error(msg, "config.json was not reloaded",
            new ToastAction("Open file", () => ShellLauncher.Open("notepad.exe", Core.Scripts.CommandLine.Quote(_config.Store.ConfigPath))));
        _config.ConfigChanged += (_, _) =>
        {
            if (_config.Current.General.StartWithWindows != StartupService.IsEnabled) ApplyStartup(_config.Current.General.StartWithWindows);
        };
        if (_config.Current.General.StartWithWindows && !StartupService.IsEnabled) ApplyStartup(true);
        _config.StartWatching();
    }

    private void ApplyStartup(bool enable)
    {
        try { StartupService.Apply(enable); }
        catch (Exception ex) { _services?.Toasts.Error(ex.Message, "Start with Windows"); }
    }

    private void CreateTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        var toggle = new WinForms.ToolStripMenuItem("Hide island");
        toggle.Click += (_, _) => _controller!.ToggleIslandVisibility();
        menu.Items.Add(toggle);
        menu.Items.Add("Reset position", null, (_, _) => _controller!.ResetPosition());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => _controller!.OpenSettings());
        menu.Items.Add("Script run log…", null, (_, _) => _controller!.ShowRunLog());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        menu.Opening += (_, _) => toggle.Text = _controller!.IsIslandVisible ? "Hide island" : "Show island";

        System.Drawing.Icon icon;
        try
        {
            var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/island.ico"))?.Stream;
            icon = stream is not null ? new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize) : System.Drawing.SystemIcons.Application;
        }
        catch (Exception)
        {
            icon = System.Drawing.SystemIcons.Application;
        }

        _tray = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "Island",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => _controller!.OpenSettings();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the island alive; report the problem instead of crashing.
        e.Handled = true;
        try
        {
            var log = Path.Combine(ConfigService.DefaultDataDirectory, "error.log");
            File.AppendAllText(log, $"[{DateTime.Now:O}] {e.Exception}\n\n");
            _services?.Toasts.Error(e.Exception.Message, "Something went wrong",
                new ToastAction("Open error log", () => ShellLauncher.Open(log)));
        }
        catch (Exception)
        {
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _hotkeys?.Dispose();
        _tracker?.Dispose();
        _config?.Dispose();
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
