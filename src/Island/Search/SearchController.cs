using Island.Core.Config;
using Island.Core.Layout;
using Island.Core.Models;
using Island.Core.Search;
using Island.Interop;
using Island.Services;

namespace Island.Search;

/// <summary>Owns the search bar: opening/closing, what is searchable, and what Enter does.</summary>
public sealed class SearchController
{
    private readonly AppServices _s;
    private readonly IslandController _island;
    private readonly SearchHistory _history;
    private readonly AppCatalog _apps = new();
    private readonly SearchViewModel _vm;
    private readonly SearchWindow _window;
    private Func<SpotifyTrack, Task>? _playTrack;

    public SearchController(AppServices services, IslandController island)
    {
        _s = services;
        _island = island;
        _history = SearchHistory.Load(Path.Combine(services.DataDirectory, "search-history.json"));
        _vm = new SearchViewModel(_history)
        {
            FileProvider = (q, ct) => FileSearch.QueryAsync(q, SearchViewModel.MaxFiles, ct),
            FileResultFactory = FileResult,
            CalculationFactory = CalculationResult,
        };
        _window = new SearchWindow(_vm);
        _window.ResultInvoked += (_, e) => _ = InvokeAsync(e.Result, e.Reveal);
        _window.Dismissed += (_, _) => Close(restoreFocus: true);

        _apps.Changed += (_, _) =>
        {
            _s.Dispatcher.BeginInvoke(RebuildLocalItems);
            WarmIcons(_apps.Apps);
        };
        _island.SearchRequested += (_, _) => Toggle();
    }

    public void Start()
    {
        _window.Prewarm();
        _apps.RefreshInBackground();
    }

    /// <summary>Connects Spotify song search and playback (Settings → Services).</summary>
    public void AttachSpotify(Func<string, CancellationToken, Task<IReadOnlyList<SpotifyTrack>>> search, Func<SpotifyTrack, Task> play)
    {
        _vm.SongProvider = search;
        _vm.SongResultFactory = SongResult;
        _playTrack = play;
    }

    public void Toggle()
    {
        if (_window.IsVisible) Close(restoreFocus: true);
        else Open();
    }

    public void Open()
    {
        RebuildLocalItems();
        _vm.Reset();

        var cursor = WindowHelper.CursorPosition();
        var monitors = MonitorService.GetMonitors();
        var mon = monitors.FirstOrDefault(m => m.Bounds.Contains(cursor.X, cursor.Y)) ?? monitors.First(m => m.IsPrimary);
        var wa = mon.WorkArea;

        // Move onto the target monitor first so WPF lays out at that monitor's DPI, then center.
        WindowHelper.MoveTo(_window, wa.X + wa.Width / 4, wa.Y + wa.Height / 4);
        _window.Show();
        _window.UpdateLayout();
        var r = WindowHelper.GetRect(_window);
        int pad = (int)Math.Round(16 * WindowHelper.GetScale(_window));
        WindowHelper.MoveTo(_window, wa.X + (wa.Width - r.Width) / 2, wa.Y + (int)(wa.Height * 0.28) - pad);

        _window.Activate();
        if (!_window.IsActive) NativeMethods.SetForegroundWindow(WindowHelper.Handle(_window));
        _window.FocusQuery();
        _window.PlayOpenAnimation();
        _apps.RefreshInBackground();
    }

    public void Close(bool restoreFocus)
    {
        if (!_window.IsVisible) return;
        _window.Hide();
        _vm.Reset();
        if (restoreFocus && _s.Foreground.LastExternalWindowIfAlive is var prev && prev != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(prev);
    }

    // ------------------------------------------------------------------ what is searchable

    private void RebuildLocalItems()
    {
        var items = new List<LocalItem>();

        foreach (var app in _apps.Apps)
            items.Add(new LocalItem(new SearchCandidate("app:" + app.LaunchPath, app.Name, null, SearchResultKind.App),
                IconSpec.App(app.LaunchPath), () => Launch(app.LaunchPath)));

        foreach (var b in ConfigSerializer.Flatten(_s.Config.Current.Buttons))
        {
            var button = b;
            var subtitle = button.Action is GroupAction ? "Island group" : ActionFactory.DisplayName(button.Action.Type);
            items.Add(new LocalItem(new SearchCandidate("button:" + button.Id, button.Label, subtitle, SearchResultKind.IslandButton),
                button.Icon, () => RunButton(button), Bias: 30, OpensIslandWindow: button.Action is OpenSettingsAction));
        }

        foreach (var (title, glyph, run) in Commands())
            items.Add(new LocalItem(new SearchCandidate("command:" + title, title, "Island", SearchResultKind.IslandCommand),
                IconSpec.Glyph(glyph), run, Bias: 10));

        _vm.LocalItems = items;
    }

    private IEnumerable<(string Title, string Glyph, Func<Task> Run)> Commands()
    {
        yield return ("Island settings", "E713", Do(_island.OpenSettings));
        yield return (_island.IsIslandVisible ? "Hide island" : "Show island", "E890", Do(_island.ToggleIslandVisibility));
        yield return ("Script run log", "E81C", Do(_island.ShowRunLog));
        yield return ("Screenshot (snip)", "E722", () => RunTransient("Screenshot", new ScreenshotAction { Mode = ScreenshotMode.Snip }));
        yield return ("Screenshot (full screen)", "E740", () => RunTransient("Screenshot", new ScreenshotAction { Mode = ScreenshotMode.FullScreen }));

        static Func<Task> Do(Action a) => () =>
        {
            a();
            return Task.CompletedTask;
        };
    }

    // ------------------------------------------------------------------ result rows

    private SearchResultViewModel CalculationResult(string formatted) =>
        new(new SearchCandidate("calc", "= " + formatted, "Enter copies the result", SearchResultKind.Calculation),
            IconSpec.Glyph("E8EF"), () =>
            {
                ClipboardHelper.SetText(formatted);
                _s.Toasts.Success($"Copied {formatted}");
                return Task.CompletedTask;
            });

    private SearchResultViewModel FileResult(FileHit hit) =>
        new(new SearchCandidate("path:" + hit.Path, hit.Name, hit.Path, hit.IsFolder ? SearchResultKind.Folder : SearchResultKind.File),
            hit.IsFolder ? IconSpec.Glyph("E8B7") : IconSpec.App(SearchPolicy.FileIconKey(hit.Path)),
            () => Launch(hit.Path, isPath: true),
            () => ShellLauncher.RevealFile(hit.Path));

    private SearchResultViewModel SongResult(SpotifyTrack t) =>
        new(new SearchCandidate("song:" + t.Uri, t.Name, string.IsNullOrEmpty(t.Album) ? t.Artists : $"{t.Artists} · {t.Album}", SearchResultKind.Song),
            IconSpec.Glyph("E768"), () => _playTrack?.Invoke(t) ?? Task.CompletedTask)
        {
            Image = SearchResultViewModel.LoadRemoteImage(t.ImageUrl),
        };

    // ------------------------------------------------------------------ running

    private async Task InvokeAsync(SearchResultViewModel r, bool reveal)
    {
        // Close first so the bar isn't in screenshots. Focus goes back to the app the user came
        // from (Claude Code here and active-window screenshots depend on that), except for results
        // that open an Island window, which need the focus themselves.
        Close(restoreFocus: SearchPolicy.RestoreFocusBeforeRunning(r.Kind, r.OpensIslandWindow));
        if (r.Kind != SearchResultKind.Calculation)
        {
            _vm.RecordUse(r);
            _ = Task.Run(_history.Save);
        }
        try
        {
            if (reveal && r.Reveal is not null) r.Reveal();
            else await r.Run();
        }
        catch (Exception ex)
        {
            _s.Toasts.Error(ex.Message, r.Title);
        }
    }

    private Task Launch(string target, bool isPath = false)
    {
        if (isPath) ShellLauncher.OpenPath(target);
        else ShellLauncher.Open(target);
        return Task.CompletedTask;
    }

    private Task RunButton(ButtonItem button)
    {
        if (button.Action is GroupAction)
        {
            _island.OpenGroup(button.Id);
            return Task.CompletedTask;
        }
        return _s.Actions.RunAsync(button);
    }

    private Task RunTransient(string label, ActionBase action) =>
        _s.Actions.RunAsync(ButtonItem.Create(label, IconSpec.Glyph("E722"), action));

    /// <summary>Loads app icons in the background so rows don't extract icons while you type.</summary>
    private static void WarmIcons(IReadOnlyList<AppEntry> apps)
    {
        var t = new Thread(() =>
        {
            foreach (var a in apps) IconService.GetFileIcon(a.LaunchPath);
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "AppIconWarmup",
        };
        t.Start();
    }
}
