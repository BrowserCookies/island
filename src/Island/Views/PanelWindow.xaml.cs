using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Island.Core.Layout;
using Island.Interop;
using Island.ViewModels;

namespace Island.Views;

/// <summary>The expanded menu. Non-activating like the island, so the user's app keeps focus.</summary>
public partial class PanelWindow : Window
{
    /// <summary>Transparent padding (DIPs) around the panel for its shadow.</summary>
    public const double ShadowPad = 12;

    public PanelWindow(PanelViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        ViewModel = vm;
        SourceInitialized += (_, _) => WindowHelper.MakeFloating(this);
    }

    public PanelViewModel ViewModel { get; }

    public event EventHandler<TileViewModel>? TileClicked;

    public int InsetPx => (int)Math.Round(ShadowPad * WindowHelper.GetScale(this));

    /// <summary>Puts the hover caption on the side away from the island and sets the grow origin.</summary>
    public void SetCorner(Corner corner)
    {
        DockPanel.SetDock(CaptionText, corner.IsBottom() ? Dock.Top : Dock.Bottom);
        Root.RenderTransformOrigin = new Point(corner.IsRight() ? 1 : 0, corner.IsBottom() ? 1 : 0);
        CaptionText.TextAlignment = corner.IsRight() ? TextAlignment.Right : TextAlignment.Left;
    }

    public void PlayOpenAnimation()
    {
        var d = TimeSpan.FromMilliseconds(140);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.92, 1, d) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, d) { EasingFunction = ease });
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TileViewModel tile })
            TileClicked?.Invoke(this, tile);
    }

    private void Tile_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TileViewModel tile })
            ViewModel.Caption = tile.ToolTipText;
    }

    private void Tile_MouseLeave(object sender, MouseEventArgs e)
    {
        ViewModel.Caption = null;
    }
}
