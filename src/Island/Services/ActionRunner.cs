using System.Collections.Concurrent;
using System.Windows.Threading;
using Island.Core.Models;
using Island.Core.Scripts;

namespace Island.Services;

/// <summary>Things actions need from the UI layer.</summary>
public interface IIslandHost
{
    void OpenSettings();
    void ShowRunLog();
    void ShowOutputPopup(string title, string text);
    /// <summary>Hides Island's windows while <paramref name="capture"/> runs (for screenshots).</summary>
    Task<T> RunHiddenAsync<T>(Func<T> capture);
}

/// <summary>Shared services handed to action handlers.</summary>
public sealed class AppServices
{
    public required Dispatcher Dispatcher { get; init; }
    public required ConfigService Config { get; init; }
    public required ToastService Toasts { get; init; }
    public required ForegroundTracker Foreground { get; init; }
    public required CurrentFolderService CurrentFolder { get; init; }
    public required ThemeService Theme { get; init; }
    public required RunLog RunLog { get; init; }
    public required IIslandHost Host { get; set; }
    public ActionRunner Actions { get; set; } = null!;
    public HotkeyService? Hotkeys { get; set; }

    public GeneralSettings Settings => Config.Current.General;
    public string DataDirectory => Config.DataDirectory;
}

public sealed record ActionContext(ButtonItem Button, AppServices Services, bool IsTest)
{
    public GeneralSettings Settings => Services.Settings;
}

/// <summary>One implementation per action type, registered in <see cref="ActionRunner"/>.</summary>
public interface IActionHandler
{
    Task ExecuteAsync(ActionBase action, ActionContext ctx, CancellationToken ct);
}

/// <summary>Handler base with a typed action.</summary>
public abstract class ActionHandler<T> : IActionHandler where T : ActionBase
{
    public Task ExecuteAsync(ActionBase action, ActionContext ctx, CancellationToken ct) => ExecuteAsync((T)action, ctx, ct);
    protected abstract Task ExecuteAsync(T action, ActionContext ctx, CancellationToken ct);
}

/// <summary>Thrown by handlers for expected, user-facing failures (shown as an error toast).</summary>
public sealed class ActionException : Exception
{
    public ActionException(string message) : base(message) { }
}

public sealed class ActionRunner
{
    private readonly AppServices _services;
    private readonly Dictionary<Type, IActionHandler> _handlers = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public ActionRunner(AppServices services)
    {
        _services = services;
        services.Actions = this;
        Register<LaunchAppAction>(new Handlers.LaunchAppHandler());
        Register<OpenFolderAction>(new Handlers.OpenFolderHandler());
        Register<OpenFileAction>(new Handlers.OpenFileHandler());
        Register<OpenUrlAction>(new Handlers.OpenUrlHandler());
        Register<GroupAction>(new Handlers.GroupHandler());
        Register<ClaudeCodeHereAction>(new Handlers.ClaudeCodeHereHandler());
        Register<ScreenshotAction>(new Handlers.ScreenshotHandler());
        Register<OpenSettingsAction>(new Handlers.OpenSettingsHandler());
        Register<RunScriptAction>(new Handlers.RunScriptHandler());
    }

    /// <summary>Raised (on the UI thread) when a button starts or stops running.</summary>
    public event EventHandler<Guid>? RunningChanged;

    public void Register<T>(IActionHandler handler) where T : ActionBase => _handlers[typeof(T)] = handler;

    public bool IsRunning(Guid buttonId) => _running.ContainsKey(buttonId);
    public bool AnyRunning => !_running.IsEmpty;

    public void Cancel(Guid buttonId)
    {
        if (_running.TryGetValue(buttonId, out var cts)) cts.Cancel();
    }

    /// <summary>Runs a button's action. Errors are reported as toasts, never thrown.</summary>
    public async Task RunAsync(ButtonItem button, bool isTest = false)
    {
        if (!_handlers.TryGetValue(button.Action.GetType(), out var handler))
        {
            _services.Toasts.Error($"No handler for action type {button.Action.Type}.");
            return;
        }

        if (_running.ContainsKey(button.Id)) return; // already running; the UI offers Cancel instead

        using var cts = new CancellationTokenSource();
        _running[button.Id] = cts;
        RunningChanged?.Invoke(this, button.Id);
        try
        {
            await handler.ExecuteAsync(button.Action, new ActionContext(button, _services, isTest), cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ActionException ex)
        {
            _services.Toasts.Error(ex.Message, button.Label);
        }
        catch (Exception ex)
        {
            _services.Toasts.Error(ex.Message, $"{button.Label} failed");
        }
        finally
        {
            _running.TryRemove(button.Id, out _);
            RunningChanged?.Invoke(this, button.Id);
        }
    }
}
