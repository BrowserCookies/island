using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Island.Interop;

namespace Island.Search;

/// <summary>
/// The pill-shaped search bar. Unlike the island it takes focus (you type into it), but it stays a
/// topmost tool window: not in the taskbar or Alt+Tab. Created once at startup and only shown/hidden.
/// </summary>
public partial class SearchWindow : Window
{
    private readonly SearchViewModel _vm;

    public SearchWindow(SearchViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        SourceInitialized += (_, _) => WindowHelper.MakeFloating(this, noActivate: false);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchViewModel.SelectedIndex) && vm.Selected is { } sel)
                ResultsList.ScrollIntoView(sel);
        };
    }

    /// <summary>Run the selected row. The bool is true for "show in folder" (Ctrl/Shift+Enter).</summary>
    public event EventHandler<(SearchResultViewModel Result, bool Reveal)>? ResultInvoked;

    /// <summary>Esc, or the window lost focus.</summary>
    public event EventHandler? Dismissed;

    /// <summary>Creates the HWND and lays out the visual tree off-screen so the first open is instant.</summary>
    public void Prewarm()
    {
        Opacity = 0;
        Show();
        Hide();
        Opacity = 1;
    }

    public void FocusQuery()
    {
        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
        QueryBox.SelectAll();
    }

    public void PlayOpenAnimation()
    {
        var d = TimeSpan.FromMilliseconds(110);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, d) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, d) { EasingFunction = ease });
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (IsVisible) Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Dismissed?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Key.Down:
                _vm.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                _vm.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                _vm.MoveSelection(8);
                e.Handled = true;
                break;
            case Key.PageUp:
                _vm.MoveSelection(-8);
                e.Handled = true;
                break;
            case Key.Enter:
                if (_vm.Selected is { } r)
                {
                    bool reveal = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
                    ResultInvoked?.Invoke(this, (r, reveal));
                }
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d &&
            ItemsControl.ContainerFromElement(ResultsList, d) is ListBoxItem { DataContext: SearchResultViewModel r })
        {
            ResultInvoked?.Invoke(this, (r, false));
        }
    }
}
