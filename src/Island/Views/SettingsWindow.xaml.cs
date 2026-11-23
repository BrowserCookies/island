using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Island.Core.Config;
using Island.Core.Models;
using Island.Core.Util;
using Island.Interop;
using Island.Services;
using Island.ViewModels;
using Microsoft.Win32;

namespace Island.Views;

public partial class SettingsWindow : Window
{
    private const string DragFormat = "Island.ButtonItem";
    private readonly AppServices _s;
    private readonly SettingsViewModel _vm;

    // drag & drop state
    private Point _dragStart;
    private ButtonItem? _dragCandidate;
    private DropIndicatorAdorner? _adorner;

    public SettingsWindow(AppServices services)
    {
        _s = services;
        _vm = new SettingsViewModel(services);
        _vm.ConfirmUngroup = item => MessageBox.Show(this,
            $"\"{item.Label}\" contains {item.Children?.Count} button(s). Changing its type moves them out of the group, right after it.\n\nContinue?",
            "Change action type", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
        DataContext = _vm;
        InitializeComponent();
        RunLog.Attach(services);

        SourceInitialized += (_, _) => WindowHelper.SetDarkTitleBar(this, _s.Theme.IsDark);
        Loaded += (_, _) =>
        {
            if (_vm.Working.Buttons.FirstOrDefault() is { } first) _vm.Select(first);
            _vm.SetHotkey(HotkeyGesture.TryParse(_vm.Working.General.Hotkey, out var g) ? g : null);
            _vm.SetSearchHotkey(HotkeyGesture.TryParse(_vm.Working.General.SearchHotkey, out var sg) ? sg : null);
        };
        Closed += (_, _) => _vm.Stop();

        InputBindings.Add(new KeyBinding(_vm.SaveCommand, Key.S, ModifierKeys.Control));
    }

    // ------------------------------------------------------------------ closing / save / cancel

    private bool _closingConfirmed;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closingConfirmed) return;
        _vm.Refresh();
        if (!_vm.IsDirty) return;

        var r = MessageBox.Show(this, "You have unsaved changes. Save them before closing?", "Island settings",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) e.Cancel = true;
        else if (r == MessageBoxResult.Yes) _vm.Save();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _closingConfirmed = true;
        Close();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Replace all buttons and settings with the defaults?\n\nNothing is saved until you click Save.",
                "Reset to defaults", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        _vm.ResetToDefaults();
        if (_vm.Working.Buttons.FirstOrDefault() is { } first) _vm.Select(first);
    }

    // ------------------------------------------------------------------ import / export

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Island config (*.json)|*.json|All files (*.*)|*.*", Title = "Import Island config" };
        if (dlg.ShowDialog(this) != true) return;

        IslandConfig imported;
        try
        {
            imported = ConfigSerializer.Deserialize(File.ReadAllText(dlg.FileName));
        }
        catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Scripts run with your permissions: list them and ask before enabling.
        var scripts = ConfigSerializer.Flatten(imported.Buttons).Where(b => b.Action is RunScriptAction).ToList();
        if (scripts.Count > 0)
        {
            var lines = scripts.Select(b =>
            {
                var rs = (RunScriptAction)b.Action;
                var what = rs.Source == ScriptSource.File
                    ? rs.ScriptPath
                    : $"inline {rs.InlineExtension}: {(rs.InlineScript ?? "").Split('\n').FirstOrDefault()?.Trim()}";
                return $"• {b.Label} — {Truncate(what ?? "", 90)}";
            });
            var answer = MessageBox.Show(this,
                $"This config contains {scripts.Count} script button(s). Scripts run with your user permissions:\n\n" +
                string.Join("\n", lines.Take(20)) + (scripts.Count > 20 ? $"\n… and {scripts.Count - 20} more" : "") +
                "\n\nYes = import and enable the scripts\nNo = import with the scripts disabled (you can enable each one later)\nCancel = don't import",
                "Imported config contains scripts", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Cancel) return;
            foreach (var b in scripts) ((RunScriptAction)b.Action).Disabled = answer != MessageBoxResult.Yes;
        }

        _vm.ReplaceWorking(imported);
        if (_vm.Working.Buttons.FirstOrDefault() is { } first) _vm.Select(first);
        _s.Toasts.Info("Config imported. Review it, then click Save to apply.", "Import");
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Island config (*.json)|*.json",
            FileName = $"island-config-{DateTime.Now:yyyy-MM-dd}.json",
            Title = "Export Island config",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, _vm.ExportJson());
            _s.Toasts.Success($"Exported to {Path.GetFileName(dlg.FileName)}", "Export",
                new ToastAction("Show in folder", () => ShellLauncher.RevealFile(dlg.FileName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => ShellLauncher.OpenFolder(_s.DataDirectory);

    private void OpenConfigFile_Click(object sender, RoutedEventArgs e) =>
        ShellLauncher.Open("notepad.exe", Core.Scripts.CommandLine.Quote(_s.Config.Store.ConfigPath));

    private void OpenScriptsFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_s.Config.Store.ScriptsDirectory);
        ShellLauncher.OpenFolder(_s.Config.Store.ScriptsDirectory);
    }

    // ------------------------------------------------------------------ add menu

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var t in SettingsViewModel.ActionTypes)
        {
            var mi = new MenuItem
            {
                Header = ActionFactory.DisplayName(t),
                Icon = new TextBlock { Text = GlyphInfo.GlyphText(ActionFactory.DefaultGlyph(t)), FontFamily = GlyphCatalog.IconFont, FontSize = 14 },
                Command = _vm.AddCommand,
                CommandParameter = t,
            };
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------ icon picker

    private void ChangeIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected is not { } item) return;
        var dlg = new IconPickerWindow(item.Icon.Clone(), _s, item.Action) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result is { } icon) item.Icon = icon;
    }

    // ------------------------------------------------------------------ browse buttons

    private static void SetProperty(object? target, object? tag, string value)
    {
        if (target is null || tag is not string name) return;
        var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        prop?.SetValue(target, value);
    }

    private static string? InitialDir(object? target, object? tag)
    {
        if (target is null || tag is not string name) return null;
        var v = target.GetType().GetProperty(name)?.GetValue(target) as string;
        if (string.IsNullOrWhiteSpace(v)) return null;
        var p = Environment.ExpandEnvironmentVariables(v);
        if (Directory.Exists(p)) return p;
        try
        {
            var d = Path.GetDirectoryName(p);
            return d is not null && Directory.Exists(d) ? d : null;
        }
        catch (ArgumentException) { return null; }
    }

    private void BrowseFileCore(object sender, string filter, string title)
    {
        var fe = (FrameworkElement)sender;
        var dlg = new OpenFileDialog { Filter = filter, Title = title, DereferenceLinks = false };
        if (InitialDir(fe.DataContext, fe.Tag) is { } dir) dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(this) == true) SetProperty(fe.DataContext, fe.Tag, dlg.FileName);
    }

    private void BrowseApp_Click(object sender, RoutedEventArgs e) =>
        BrowseFileCore(sender, "Programs and shortcuts (*.exe;*.lnk;*.bat;*.cmd;*.appref-ms)|*.exe;*.lnk;*.bat;*.cmd;*.appref-ms|All files (*.*)|*.*", "Choose an app");

    private void BrowseFile_Click(object sender, RoutedEventArgs e) =>
        BrowseFileCore(sender, "All files (*.*)|*.*", "Choose a file");

    private void BrowseScript_Click(object sender, RoutedEventArgs e) =>
        BrowseFileCore(sender, "Scripts (*.ps1;*.py;*.js;*.mjs;*.bat;*.cmd;*.exe)|*.ps1;*.py;*.pyw;*.js;*.mjs;*.cjs;*.bat;*.cmd;*.exe|All files (*.*)|*.*", "Choose a script");

    private void BrowseExe_Click(object sender, RoutedEventArgs e) =>
        BrowseFileCore(sender, "Programs (*.exe)|*.exe|All files (*.*)|*.*", "Choose an interpreter");

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var fe = (FrameworkElement)sender;
        var dlg = new OpenFolderDialog { Title = "Choose a folder" };
        if (InitialDir(fe.DataContext, fe.Tag) is { } dir) dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(this) == true) SetProperty(fe.DataContext, fe.Tag, dlg.FolderName);
    }

    private void BrowseGeneralFolder_Click(object sender, RoutedEventArgs e)
    {
        var fe = (FrameworkElement)sender;
        var dlg = new OpenFolderDialog { Title = "Choose a folder" };
        if (InitialDir(_vm.Working.General, fe.Tag) is { } dir) dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(this) == true) SetProperty(_vm.Working.General, fe.Tag, dlg.FolderName);
    }

    // ------------------------------------------------------------------ hotkey capture

    private bool IsSearchBox(object sender) => sender is FrameworkElement { Tag: "search" };

    private void ApplyHotkey(object sender, HotkeyGesture? g)
    {
        if (IsSearchBox(sender)) _vm.SetSearchHotkey(g);
        else _vm.SetHotkey(g);
    }

    private void SetHotkeyHint(object sender, string text, bool isError)
    {
        if (IsSearchBox(sender))
        {
            _vm.SearchHotkeyStatus = text;
            _vm.SearchHotkeyStatusIsError = isError;
        }
        else
        {
            _vm.HotkeyStatus = text;
            _vm.HotkeyStatusIsError = isError;
        }
    }

    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        SetHotkeyHint(sender, "Press the new key combination (e.g. Ctrl+Alt+Space). Esc cancels.", false);

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            var current = IsSearchBox(sender) ? _vm.Working.General.SearchHotkey : _vm.Working.General.Hotkey;
            ApplyHotkey(sender, HotkeyGesture.TryParse(current, out var cur) ? cur : null);
            return;
        }
        if (key is Key.Back or Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            ApplyHotkey(sender, null);
            return;
        }
        // Wait for a non-modifier key.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.Tab or Key.ImeProcessed or Key.DeadCharProcessed)
            return;

        var mods = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= HotkeyModifiers.Win;

        var text = new HotkeyGesture(mods, key.ToString()).ToString();
        if (HotkeyGesture.TryParse(text, out var g)) ApplyHotkey(sender, g);
        else SetHotkeyHint(sender, "Use at least one modifier (Ctrl, Alt, Shift or Win), or an F-key.", true);
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e) => ApplyHotkey(sender, null);

    private void CopyRedirectUri_Click(object sender, RoutedEventArgs e)
    {
        ClipboardHelper.SetText(_vm.SpotifyRedirectUri);
        _vm.Services.Toasts.Success("Redirect URI copied.");
    }

    private void OpenSpotifyDashboard_Click(object sender, RoutedEventArgs e) =>
        ShellLauncher.Open("https://developer.spotify.com/dashboard");

    // ------------------------------------------------------------------ tree: selection & keys

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ButtonItem b) _vm.Selected = b;
    }

    private void Tree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (alt && key == Key.Up)
        {
            _vm.MoveUpCommand.Execute(null);
            e.Handled = true;
        }
        else if (alt && key == Key.Down)
        {
            _vm.MoveDownCommand.Execute(null);
            e.Handled = true;
        }
        else if (key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            _vm.DeleteCommand.Execute(null);
            e.Handled = true;
        }
        else if (key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _vm.DuplicateCommand.Execute(null);
            e.Handled = true;
        }
        else if (key == Key.F2)
        {
            LabelBox.Focus();
            LabelBox.SelectAll();
            e.Handled = true;
        }
        if (e.Handled) FocusSelectedTreeItem();
    }

    private void FocusSelectedTreeItem()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_vm.Selected is { } sel && FindContainer(Tree, sel) is { } tvi) tvi.Focus();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private static TreeViewItem? FindContainer(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct) return direct;
        foreach (var child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem tvi)
            {
                var found = FindContainer(tvi, item);
                if (found is not null) return found;
            }
        }
        return null;
    }

    // ------------------------------------------------------------------ tree: drag & drop

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Tree);
        _dragCandidate = (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext) as ButtonItem;
        // Clicking the expander toggle shouldn't start a drag.
        if (FindAncestor<System.Windows.Controls.Primitives.ToggleButton>(e.OriginalSource as DependencyObject) is not null) _dragCandidate = null;
    }

    private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null) return;
        var pos = e.GetPosition(Tree);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var item = _dragCandidate;
        _dragCandidate = null;
        try
        {
            DragDrop.DoDragDrop(Tree, new DataObject(DragFormat, item), DragDropEffects.Move);
        }
        finally
        {
            RemoveAdorner();
        }
    }

    private enum DropZone { Before, After, Into }

    private sealed record DropTarget(ObservableCollection<ButtonItem> Collection, int Index, TreeViewItem? Container, DropZone Zone);

    private DropTarget ComputeDropTarget(DragEventArgs e)
    {
        var tvi = FindAncestor<TreeViewItem>(Tree.InputHitTest(e.GetPosition(Tree)) as DependencyObject);
        if (tvi?.DataContext is not ButtonItem target)
            return new DropTarget(_vm.Working.Buttons, _vm.Working.Buttons.Count, null, DropZone.After);

        // Measure against the header row only (not the expanded children).
        var header = (tvi.Template.FindName("PART_Header", tvi) as FrameworkElement) ?? tvi;
        double y = e.GetPosition(header).Y;
        double h = Math.Max(1, header.ActualHeight);
        var list = ButtonTree.FindParentCollection(_vm.Working.Buttons, target) ?? _vm.Working.Buttons;
        int idx = list.IndexOf(target);

        if (target.IsGroup && y > h * 0.25 && y < h * 0.75)
            return new DropTarget(target.Children!, target.Children!.Count, tvi, DropZone.Into);
        if (y < h / 2)
            return new DropTarget(list, idx, tvi, DropZone.Before);
        if (target.IsGroup && target.IsExpanded && target.Children is { Count: > 0 })
            return new DropTarget(target.Children, 0, tvi, DropZone.Into);
        return new DropTarget(list, idx + 1, tvi, DropZone.After);
    }

    private void Tree_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        bool isButton = e.Data.GetDataPresent(DragFormat);
        bool isFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        if (!isButton && !isFiles)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        var t = ComputeDropTarget(e);
        if (isButton && e.Data.GetData(DragFormat) is ButtonItem dragged &&
            (ReferenceEquals(t.Container?.DataContext, dragged) || IsInside(dragged, t.Collection)))
        {
            e.Effects = DragDropEffects.None;
            RemoveAdorner();
            return;
        }

        e.Effects = isButton ? DragDropEffects.Move : DragDropEffects.Copy;
        ShowAdorner(t);
    }

    private static bool IsInside(ButtonItem dragged, ObservableCollection<ButtonItem> target) =>
        dragged.Children is not null && (ReferenceEquals(dragged.Children, target) ||
                                         dragged.Children.Any(c => IsInside(c, target)));

    private void Tree_DragLeave(object sender, DragEventArgs e) => RemoveAdorner();

    private void Tree_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        RemoveAdorner();
        var t = ComputeDropTarget(e);

        if (e.Data.GetData(DragFormat) is ButtonItem dragged)
        {
            if (ButtonTree.Move(_vm.Working.Buttons, dragged, t.Collection, t.Index))
            {
                if (t.Zone == DropZone.Into && t.Container?.DataContext is ButtonItem g) g.IsExpanded = true;
                _vm.Select(dragged);
            }
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var items = files.Select(f => DropFactory.Create(f, Directory.Exists(f))).ToList();
            _vm.AddButtons(items, t.Collection, t.Index);
            if (t.Zone == DropZone.Into && t.Container?.DataContext is ButtonItem g) g.IsExpanded = true;
            Activate();
        }
    }

    private void ShowAdorner(DropTarget t)
    {
        var layer = AdornerLayer.GetAdornerLayer(Tree);
        if (layer is null) return;
        if (_adorner is null)
        {
            _adorner = new DropIndicatorAdorner(Tree);
            layer.Add(_adorner);
        }

        Rect rect;
        if (t.Container is null)
        {
            // End of the list.
            double bottom = 0;
            if (Tree.Items.Count > 0 && Tree.ItemContainerGenerator.ContainerFromIndex(Tree.Items.Count - 1) is FrameworkElement last)
                bottom = last.TranslatePoint(new Point(0, last.ActualHeight), Tree).Y;
            rect = new Rect(4, bottom, Tree.ActualWidth - 8, 0);
            _adorner.Update(rect, isLine: true);
            return;
        }

        var header = (t.Container.Template.FindName("PART_Header", t.Container) as FrameworkElement) ?? t.Container;
        var topLeft = header.TranslatePoint(new Point(0, 0), Tree);
        double width = Math.Max(40, Tree.ActualWidth - topLeft.X - 8);
        rect = t.Zone switch
        {
            DropZone.Before => new Rect(topLeft.X, topLeft.Y, width, 0),
            DropZone.After => new Rect(topLeft.X, topLeft.Y + header.ActualHeight, width, 0),
            _ => new Rect(topLeft.X - 2, topLeft.Y, width + 2, header.ActualHeight),
        };
        _adorner.Update(rect, isLine: t.Zone != DropZone.Into);
    }

    private void RemoveAdorner()
    {
        if (_adorner is null) return;
        AdornerLayer.GetAdornerLayer(Tree)?.Remove(_adorner);
        _adorner = null;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>Draws the insertion line / "drop into group" frame during drag &amp; drop.</summary>
    private sealed class DropIndicatorAdorner : Adorner
    {
        private Rect _rect;
        private bool _isLine;

        public DropIndicatorAdorner(UIElement adorned) : base(adorned) => IsHitTestVisible = false;

        public void Update(Rect rect, bool isLine)
        {
            _rect = rect;
            _isLine = isLine;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var brush = (Brush)(TryFindResource("AccentBrush") ?? Brushes.DodgerBlue);
            if (_isLine)
            {
                var pen = new Pen(brush, 2);
                dc.DrawLine(pen, _rect.TopLeft, _rect.TopRight);
                dc.DrawEllipse(brush, null, _rect.TopLeft, 3, 3);
            }
            else
            {
                dc.DrawRoundedRectangle(null, new Pen(brush, 2), _rect, 4, 4);
            }
        }
    }
}
