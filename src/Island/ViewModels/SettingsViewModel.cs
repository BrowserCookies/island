using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Island.Core.Config;
using Island.Core.Models;
using Island.Core.Util;
using Island.Services;

namespace Island.ViewModels;

/// <summary>
/// Settings editor. Works on a deep copy of the live config ("working copy"); nothing reaches
/// the island until Save.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly DispatcherTimer _tick;
    private readonly ButtonValidator _validator = new();
    private string _baseline;
    private UndoInfo? _undo;
    private readonly DispatcherTimer _undoTimer;
    private ActionBase? _watchedAction;

    public SettingsViewModel(AppServices services)
    {
        _s = services;
        _working = ConfigSerializer.Clone(services.Config.Current);
        _baseline = ConfigSerializer.Serialize(_working);

        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => Refresh(), services.Dispatcher);
        _tick.Start();
        _undoTimer = new DispatcherTimer(TimeSpan.FromSeconds(8), DispatcherPriority.Background, (_, _) => DismissUndo(), services.Dispatcher);
        _undoTimer.Stop();
        Refresh();
    }

    public static IReadOnlyList<ActionType> ActionTypes { get; } = Enum.GetValues<ActionType>();
    public static IReadOnlyList<OutputHandling> OutputModes { get; } = Enum.GetValues<OutputHandling>();
    public static IReadOnlyList<string> ScriptLanguages { get; } = new[] { ".ps1", ".py", ".js", ".mjs", ".bat", ".cmd" };

    public AppServices Services => _s;

    [ObservableProperty] private IslandConfig _working;
    [ObservableProperty] private ButtonItem? _selected;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _undoMessage;
    [ObservableProperty] private string? _hotkeyStatus;
    [ObservableProperty] private bool _hotkeyStatusIsError;

    public ObservableCollection<string> SelectedErrors { get; } = new();

    public bool HasSelection => Selected is not null;

    public ActionType? SelectedActionType
    {
        get => Selected?.Action.Type;
        set
        {
            if (Selected is null || value is null || value == Selected.Action.Type) return;
            ChangeActionType(Selected, value.Value);
            OnPropertyChanged();
        }
    }

    partial void OnSelectedChanged(ButtonItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedActionType));
        WatchAction(value?.Action);
        if (value is not null) value.PropertyChanged += OnSelectedItemChanged;
        Refresh();
    }

    partial void OnSelectedChanging(ButtonItem? value)
    {
        if (Selected is not null) Selected.PropertyChanged -= OnSelectedItemChanged;
    }

    private void OnSelectedItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ButtonItem.Action))
        {
            WatchAction(Selected?.Action);
            OnPropertyChanged(nameof(SelectedActionType));
        }
    }

    private void WatchAction(ActionBase? action)
    {
        if (_watchedAction is not null) _watchedAction.PropertyChanged -= OnActionPropertyChanged;
        _watchedAction = action;
        if (action is not null) action.PropertyChanged += OnActionPropertyChanged;
    }

    /// <summary>LaunchApp: picking a path fills in the icon and an empty label automatically.</summary>
    private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Selected is null) return;
        string? path = sender switch
        {
            LaunchAppAction la when e.PropertyName == nameof(LaunchAppAction.Path) => la.Path,
            OpenFileAction of when e.PropertyName == nameof(OpenFileAction.Path) => of.Path,
            OpenFolderAction od when e.PropertyName == nameof(OpenFolderAction.Path) => od.Path,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(path)) return;

        var icon = Selected.Icon;
        bool iconIsDefault = icon.Kind == IconKind.AppIcon ||
                             (icon.Kind == IconKind.Glyph && icon.Value == ActionFactory.DefaultGlyph(Selected.Action.Type));
        if (iconIsDefault)
        {
            icon.Kind = IconKind.AppIcon;
            icon.Value = path.Trim().Trim('"');
        }
        if (string.IsNullOrWhiteSpace(Selected.Label) || Selected.Label == "New button")
            Selected.Label = DropFactory.FileNameWithoutExtension(path.Trim().Trim('"'));
    }

    // ------------------------------------------------------------------ periodic refresh

    /// <summary>Recomputes dirty state and validation (cheap: configs are small).</summary>
    public void Refresh()
    {
        IsDirty = ConfigSerializer.Serialize(Working) != _baseline;

        foreach (var b in ConfigSerializer.Flatten(Working.Buttons))
        {
            var errs = _validator.Validate(b);
            b.Warning = errs.Count == 0 ? null : string.Join("\n", errs);
        }

        var selErrors = Selected is null ? (IReadOnlyList<string>)Array.Empty<string>() : _validator.Validate(Selected);
        if (!selErrors.SequenceEqual(SelectedErrors))
        {
            SelectedErrors.Clear();
            foreach (var e in selErrors) SelectedErrors.Add(e);
        }
    }

    public void Stop()
    {
        _tick.Stop();
        _undoTimer.Stop();
        WatchAction(null);
    }

    // ------------------------------------------------------------------ tree editing

    private ObservableCollection<ButtonItem> CollectionFor(ButtonItem? anchor, out int insertIndex)
    {
        if (anchor is null)
        {
            insertIndex = Working.Buttons.Count;
            return Working.Buttons;
        }
        var list = ButtonTree.FindParentCollection(Working.Buttons, anchor) ?? Working.Buttons;
        insertIndex = list.IndexOf(anchor) + 1;
        return list;
    }

    [RelayCommand]
    private void Add(ActionType type)
    {
        var item = new ButtonItem
        {
            Label = type switch
            {
                ActionType.Group => "New group",
                ActionType.ClaudeCodeHere => "Claude Code here",
                ActionType.Screenshot => "Screenshot",
                ActionType.OpenSettings => "Settings",
                _ => "New button",
            },
            Icon = IconSpec.Glyph(ActionFactory.DefaultGlyph(type)),
            Action = ActionFactory.Create(type),
        };
        if (item.Action is RunScriptAction rs)
            rs.InlineScript = "# Your PowerShell script.\n# Print a line to show it as a toast, or JSON for the JSON protocol.\nWrite-Output \"Hello from Island!\"\n";

        // New buttons go below the selection (inside an open group if a group is selected and expanded).
        ObservableCollection<ButtonItem> list;
        int index;
        if (Selected is { IsGroup: true, IsExpanded: true, Children: { } kids } && type != ActionType.Group)
        {
            list = kids;
            index = kids.Count;
        }
        else
        {
            list = CollectionFor(Selected, out index);
        }
        list.Insert(Math.Min(index, list.Count), item);
        Select(item);
    }

    public void AddButtons(IEnumerable<ButtonItem> items, ObservableCollection<ButtonItem> target, int index)
    {
        ButtonItem? last = null;
        foreach (var item in items)
        {
            target.Insert(Math.Clamp(index++, 0, target.Count), item);
            last = item;
        }
        if (last is not null) Select(last);
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is null) return;
        var copy = ConfigSerializer.CloneButton(Selected, newIds: true);
        copy.Label = Selected.Label + " (copy)";
        var list = CollectionFor(Selected, out int index);
        list.Insert(index, copy);
        Select(copy);
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        var item = Selected;
        var list = ButtonTree.FindParentCollection(Working.Buttons, item);
        if (list is null) return;
        var parentButton = ButtonTree.FindParentButton(Working.Buttons, item);
        int index = list.IndexOf(item);
        list.RemoveAt(index);
        item.IsSelected = false;

        _undo = new UndoInfo(item, list, index);
        UndoMessage = $"Deleted \"{(string.IsNullOrWhiteSpace(item.Label) ? "button" : item.Label)}\"";
        _undoTimer.Stop();
        _undoTimer.Start();

        // Select a neighbour.
        if (list.Count > 0) Select(list[Math.Min(index, list.Count - 1)]);
        else if (parentButton is not null) Select(parentButton);
        else Selected = null;
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undo is null) return;
        var (item, list, index) = _undo;
        list.Insert(Math.Min(index, list.Count), item);
        Select(item);
        DismissUndo();
    }

    [RelayCommand]
    private void DismissUndo()
    {
        _undoTimer.Stop();
        _undo = null;
        UndoMessage = null;
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (Selected is null) return;
        var item = Selected;
        if (ButtonTree.MoveUp(Working.Buttons, item)) Select(item);
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (Selected is null) return;
        var item = Selected;
        if (ButtonTree.MoveDown(Working.Buttons, item)) Select(item);
    }

    public void Select(ButtonItem item)
    {
        // Expand ancestors so the item is visible in the tree.
        var parent = ButtonTree.FindParentButton(Working.Buttons, item);
        while (parent is not null)
        {
            parent.IsExpanded = true;
            parent = ButtonTree.FindParentButton(Working.Buttons, parent);
        }
        foreach (var b in ConfigSerializer.Flatten(Working.Buttons)) if (!ReferenceEquals(b, item)) b.IsSelected = false;
        item.IsSelected = true;
        Selected = item;
    }

    /// <summary>Raised to ask the view whether a group's children may be moved out (returns true = proceed).</summary>
    public Func<ButtonItem, bool>? ConfirmUngroup { get; set; }

    private void ChangeActionType(ButtonItem item, ActionType type)
    {
        if (item.Action is GroupAction && item.Children is { Count: > 0 } kids && type != ActionType.Group)
        {
            if (ConfirmUngroup is not null && !ConfirmUngroup(item)) return;
            var list = ButtonTree.FindParentCollection(Working.Buttons, item)!;
            int at = list.IndexOf(item) + 1;
            foreach (var k in kids.ToList()) list.Insert(at++, k);
        }

        bool defaultIcon = item.Icon.Kind == IconKind.Glyph && item.Icon.Value == ActionFactory.DefaultGlyph(item.Action.Type);
        item.Action = ActionFactory.Create(type);
        if (type == ActionType.Group) item.Children ??= new ObservableCollection<ButtonItem>();
        else item.Children = null;
        if (defaultIcon) item.Icon.Value = ActionFactory.DefaultGlyph(type);
        if (item.Action is RunScriptAction rs) rs.InlineScript = "Write-Output \"Hello from Island!\"\n";
    }

    // ------------------------------------------------------------------ save / revert / reset

    [RelayCommand]
    public void Save()
    {
        // Validation never blocks saving; invalid buttons just carry a warning badge.
        var toSave = ConfigSerializer.Clone(Working);
        _s.Config.Save(toSave);
        _baseline = ConfigSerializer.Serialize(Working);
        Refresh();
    }

    public void Revert()
    {
        var selectedId = Selected?.Id;
        Working = ConfigSerializer.Clone(_s.Config.Current);
        _baseline = ConfigSerializer.Serialize(Working);
        Selected = null;
        var again = ConfigSerializer.Flatten(Working.Buttons).FirstOrDefault(b => b.Id == selectedId);
        if (again is not null) Select(again);
        Refresh();
    }

    public void ResetToDefaults()
    {
        Working = DefaultConfig.Create();
        Selected = null;
        Refresh();
    }

    public void ReplaceWorking(IslandConfig config)
    {
        Working = config;
        Selected = null;
        Refresh();
    }

    public string ExportJson() => ConfigSerializer.Serialize(Working);

    [RelayCommand]
    private void Test()
    {
        if (Selected is null) return;
        if (Selected.Action is OpenSettingsAction)
        {
            _s.Toasts.Info("This button opens the settings window.", Selected.Label);
            return;
        }
        _ = _s.Actions.RunAsync(Selected, isTest: true);
    }

    // ------------------------------------------------------------------ hotkey

    public void SetHotkey(HotkeyGesture? gesture)
    {
        if (gesture is null)
        {
            Working.General.Hotkey = null;
            HotkeyStatus = "No global hotkey.";
            HotkeyStatusIsError = false;
            return;
        }

        var g = gesture.Value;
        Working.General.Hotkey = g.ToString();
        bool isCurrent = HotkeyGesture.TryParse(_s.Config.Current.General.Hotkey, out var current) && current == g;
        if (isCurrent || _s.Hotkeys is null)
        {
            HotkeyStatus = $"{g} toggles the island; {g.WithShift()} opens settings.";
            HotkeyStatusIsError = false;
        }
        else if (_s.Hotkeys.IsAvailable(g, out var err))
        {
            HotkeyStatus = g.Modifiers.HasFlag(HotkeyModifiers.Shift)
                ? $"{g} is available. (It already includes Shift, so there is no settings shortcut.)"
                : $"{g} is available. {g.WithShift()} will open settings.";
            HotkeyStatusIsError = false;
        }
        else
        {
            HotkeyStatus = err ?? $"{g} is not available.";
            HotkeyStatusIsError = true;
        }
    }

    private sealed record UndoInfo(ButtonItem Item, ObservableCollection<ButtonItem> List, int Index);
}
