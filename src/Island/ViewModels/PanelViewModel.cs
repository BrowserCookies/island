using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Island.Core.Layout;
using Island.Core.Models;

namespace Island.ViewModels;

/// <summary>A button as shown in the expanded panel (adds running / open state to the model).</summary>
public partial class TileViewModel : ObservableObject
{
    public TileViewModel(ButtonItem item, double tileSize, bool showLabel)
    {
        Item = item;
        TileSize = tileSize;
        ShowLabel = showLabel;
    }

    public ButtonItem Item { get; }
    public double TileSize { get; }
    public double IconSize => Math.Round(TileSize * 0.52);
    public bool ShowLabel { get; }
    public bool IsGroup => Item.Action is GroupAction;
    public bool IsDisabled => Item.Action is RunScriptAction { Disabled: true };

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isOpen;

    public string ToolTipText => IsRunning ? $"{Item.DisplayTooltip} — running (click to cancel)" : Item.DisplayTooltip;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(ToolTipText));
}

/// <summary>One row/column of tiles: the top level or an opened group.</summary>
public sealed class PanelLevel
{
    public PanelLevel(ButtonItem? group, IReadOnlyList<ButtonItem> source, IEnumerable<TileViewModel> tiles)
    {
        Group = group;
        Source = source;
        Tiles = new ObservableCollection<TileViewModel>(tiles);
    }

    public ButtonItem? Group { get; }
    /// <summary>The buttons in island order (tiles may be displayed reversed).</summary>
    public IReadOnlyList<ButtonItem> Source { get; }
    public ObservableCollection<TileViewModel> Tiles { get; }
}

public partial class PanelViewModel : ObservableObject
{
    private GeneralSettings _settings = new();
    private Corner _corner = Corner.BottomRight;

    /// <summary>Levels in logical order: [0] = top level, then each opened group.</summary>
    private readonly List<PanelLevel> _levels = new();

    /// <summary>Levels in display order (the level nearest the island comes first in the stack direction).</summary>
    public ObservableCollection<PanelLevel> DisplayLevels { get; } = new();

    [ObservableProperty] private string? _caption;
    /// <summary>Levels (e.g. an opened group) hug the island's side of the panel.</summary>
    [ObservableProperty] private System.Windows.HorizontalAlignment _levelHorizontalAlignment = System.Windows.HorizontalAlignment.Right;
    [ObservableProperty] private System.Windows.VerticalAlignment _levelVerticalAlignment = System.Windows.VerticalAlignment.Bottom;
    [ObservableProperty] private ExpandLayout _layout;
    [ObservableProperty] private int _gridColumns = 4;

    public double TileSize => Math.Clamp(Math.Round(_settings.IslandSize * 0.8), 32, 80);

    /// <summary>Levels stack vertically for Row/Grid and horizontally for Column layouts.</summary>
    public bool StackLevelsHorizontally => Layout == ExpandLayout.Column;

    public IEnumerable<TileViewModel> AllTiles => _levels.SelectMany(l => l.Tiles);

    public void Load(IEnumerable<ButtonItem> buttons, GeneralSettings settings, Corner corner)
    {
        _settings = settings;
        _corner = corner;
        Layout = settings.Layout;
        GridColumns = settings.GridColumns;
        OnPropertyChanged(nameof(TileSize));
        OnPropertyChanged(nameof(StackLevelsHorizontally));
        _levels.Clear();
        _levels.Add(MakeLevel(null, buttons));
        Caption = null;
        RebuildDisplay();
    }

    public void SetCorner(Corner corner)
    {
        if (_corner == corner) return;
        _corner = corner;
        // Rebuild tiles so their order follows the new corner.
        foreach (var i in Enumerable.Range(0, _levels.Count))
        {
            var l = _levels[i];
            var openIds = l.Tiles.Where(t => t.IsOpen).Select(t => t.Item.Id).ToHashSet();
            _levels[i] = MakeLevel(l.Group, l.Source);
            foreach (var t in _levels[i].Tiles) t.IsOpen = openIds.Contains(t.Item.Id);
        }
        RebuildDisplay();
    }

    private PanelLevel MakeLevel(ButtonItem? group, IEnumerable<ButtonItem> items)
    {
        var source = items.ToList();
        var tiles = source.Select(b => new TileViewModel(b, TileSize, _settings.ShowLabels)).ToList();
        // Keep the first button nearest the island: reverse when the panel grows leftwards (row)
        // or upwards (column).
        bool reverse = Layout switch
        {
            ExpandLayout.Row => _corner.IsRight(),
            ExpandLayout.Column => _corner.IsBottom(),
            _ => false,
        };
        if (reverse) tiles.Reverse();
        return new PanelLevel(group, source, tiles);
    }

    /// <summary>Opens (or toggles closed) a group from the given level.</summary>
    public void ToggleGroup(TileViewModel tile)
    {
        int levelIndex = _levels.FindIndex(l => l.Tiles.Contains(tile));
        if (levelIndex < 0) return;

        bool wasOpen = tile.IsOpen;
        // Close everything deeper than this level.
        for (int i = _levels.Count - 1; i > levelIndex; i--) _levels.RemoveAt(i);
        foreach (var t in _levels[levelIndex].Tiles) t.IsOpen = false;

        if (!wasOpen)
        {
            tile.IsOpen = true;
            _levels.Add(MakeLevel(tile.Item, tile.Item.Children ?? new ObservableCollection<ButtonItem>()));
        }
        RebuildDisplay();
    }

    private void RebuildDisplay()
    {
        LevelHorizontalAlignment = _corner.IsRight() ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left;
        LevelVerticalAlignment = _corner.IsBottom() ? System.Windows.VerticalAlignment.Bottom : System.Windows.VerticalAlignment.Top;
        DisplayLevels.Clear();
        // Levels stack away from the island: for bottom corners (row/grid) the top level is
        // at the bottom; for right corners (column) it is at the right.
        bool reverse = Layout == ExpandLayout.Column ? _corner.IsRight() : _corner.IsBottom();
        IEnumerable<PanelLevel> seq = reverse ? Enumerable.Reverse(_levels) : _levels;
        foreach (var l in seq) DisplayLevels.Add(l);
    }

    public void SetRunning(Guid id, bool running)
    {
        foreach (var t in AllTiles.Where(t => t.Item.Id == id)) t.IsRunning = running;
    }
}
