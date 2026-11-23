using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Island.Core.Models;
using Island.Core.Search;

namespace Island.Search;

/// <summary>One row in the search results.</summary>
public sealed class SearchResultViewModel
{
    public SearchResultViewModel(SearchCandidate candidate, IconSpec? icon, Func<Task> run, Action? reveal = null)
    {
        Candidate = candidate;
        Icon = icon;
        Run = run;
        Reveal = reveal;
    }

    public SearchCandidate Candidate { get; }
    public string Title => Candidate.Title;
    public string? Subtitle => Candidate.Subtitle;
    public SearchResultKind Kind => Candidate.Kind;
    public IconSpec? Icon { get; }
    /// <summary>Remote artwork (Spotify album covers); shown instead of <see cref="Icon"/> when set.</summary>
    public ImageSource? Image { get; init; }
    public Func<Task> Run { get; }
    /// <summary>Opens one of Island's own windows (e.g. a Settings button), so focus stays with Island.</summary>
    public bool OpensIslandWindow { get; init; }
    /// <summary>"Show in folder" for files and folders (Ctrl/Shift+Enter).</summary>
    public Action? Reveal { get; }

    public string KindLabel => Kind switch
    {
        SearchResultKind.Calculation => "Copy",
        SearchResultKind.App => "App",
        SearchResultKind.IslandButton => "Island",
        SearchResultKind.IslandCommand => "Island",
        SearchResultKind.Folder => "Folder",
        SearchResultKind.File => "File",
        SearchResultKind.Song => "Spotify",
        _ => "",
    };

    public static ImageSource? LoadRemoteImage(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(url, UriKind.Absolute);
            bmp.DecodePixelWidth = 64;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit(); // downloads asynchronously; the row shows it when it arrives
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>A local (in-memory) search candidate and how to turn it into a result row.</summary>
public sealed record LocalItem(SearchCandidate Candidate, IconSpec? Icon, Func<Task> Run, int Bias = 0, bool OpensIslandWindow = false);

/// <summary>
/// Query → results. Local items (apps, Island buttons and commands) and the calculator are ranked
/// synchronously on every keystroke. Files and songs arrive asynchronously, each in its own block
/// below the local results, and are dropped if the query changed in the meantime.
/// </summary>
public partial class SearchViewModel : ObservableObject
{
    public const int MaxLocal = 6;
    public const int MaxFiles = 5;
    public const int MaxSongs = 5;
    private const int MinAsyncQueryLength = 2;
    private static readonly TimeSpan SongDebounce = TimeSpan.FromMilliseconds(250);

    private readonly SearchHistory _history;
    private readonly SearchResultSet _set = new();
    private CancellationTokenSource? _cts;

    private List<SearchResultViewModel> _local = new();
    private List<SearchResultViewModel> _files = new();
    private List<SearchResultViewModel> _songs = new();

    public SearchViewModel(SearchHistory history) => _history = history;

    /// <summary>Everything searchable in memory. Rebuilt by the controller when apps or config change.</summary>
    public IReadOnlyList<LocalItem> LocalItems { get; set; } = Array.Empty<LocalItem>();

    public Func<string, CancellationToken, Task<IReadOnlyList<FileHit>>>? FileProvider { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<SpotifyTrack>>>? SongProvider { get; set; }
    public Func<FileHit, SearchResultViewModel>? FileResultFactory { get; set; }
    public Func<SpotifyTrack, SearchResultViewModel>? SongResultFactory { get; set; }
    public Func<string, SearchResultViewModel>? CalculationFactory { get; set; }

    public ObservableCollection<SearchResultViewModel> Results { get; } = new();

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _selectedIndex = -1;
    [ObservableProperty] private bool _hasResults;

    public SearchResultViewModel? Selected =>
        SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    partial void OnQueryChanged(string value) => Search(value);

    public void Reset()
    {
        _cts?.Cancel();
        _set.Begin("");
        Query = "";
        Search("");
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Results.Count - 1);
    }

    public void RecordUse(SearchResultViewModel r) => _history.Record(r.Candidate.Key, DateTime.UtcNow);

    private void Search(string query)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        int gen = _set.Begin(query);
        var q = query.Trim();

        _local = RankLocal(q);
        _files = new();
        _songs = new();
        Publish(resetSelection: true);

        if (q.Length < MinAsyncQueryLength) return;
        if (FileProvider is not null && FileResultFactory is not null) _ = LoadFilesAsync(q, gen, ct);
        if (SongProvider is not null && SongResultFactory is not null) _ = LoadSongsAsync(q, gen, ct);
    }

    private List<SearchResultViewModel> RankLocal(string q)
    {
        var list = new List<SearchResultViewModel>();
        if (q.Length == 0) return list;

        if (CalculationFactory is not null && Calculator.TryEvaluate(q, out var value))
            list.Add(CalculationFactory(Calculator.Format(value)));

        var now = DateTime.UtcNow;
        var ranked = LocalItems
            .Select(i => (Item: i, Score: SearchMatcher.Score(q, i.Candidate.Title)))
            .Where(x => x.Score > 0)
            .Select(x => (x.Item, Score: x.Score + x.Item.Bias + _history.Boost(x.Item.Candidate.Key, now)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Candidate.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxLocal);
        foreach (var (item, _) in ranked)
            list.Add(new SearchResultViewModel(item.Candidate, item.Icon, item.Run) { OpensIslandWindow = item.OpensIslandWindow });
        return list;
    }

    private async Task LoadFilesAsync(string q, int gen, CancellationToken ct)
    {
        try
        {
            var hits = await FileProvider!(q, ct);
            if (!_set.Accept(gen) || ct.IsCancellationRequested) return;
            _files = hits.Take(MaxFiles).Select(FileResultFactory!).ToList();
            Publish(resetSelection: false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadSongsAsync(string q, int gen, CancellationToken ct)
    {
        try
        {
            await Task.Delay(SongDebounce, ct);
            var tracks = await SongProvider!(q, ct);
            if (!_set.Accept(gen) || ct.IsCancellationRequested) return;
            _songs = tracks.Take(MaxSongs).Select(SongResultFactory!).ToList();
            Publish(resetSelection: false);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Rebuilds the list in fixed block order, keeping the selected row if it is still there.</summary>
    private void Publish(bool resetSelection)
    {
        var selected = resetSelection ? null : Selected;
        Results.Clear();
        foreach (var r in _local.Concat(_files).Concat(_songs)) Results.Add(r);
        HasResults = Results.Count > 0;
        int idx = selected is null ? -1 : Results.IndexOf(selected);
        SelectedIndex = idx >= 0 ? idx : Results.Count > 0 ? 0 : -1;
        OnPropertyChanged(nameof(Selected));
    }

    partial void OnSelectedIndexChanged(int value) => OnPropertyChanged(nameof(Selected));
}
