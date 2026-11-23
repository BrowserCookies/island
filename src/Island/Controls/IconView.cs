using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Island.Core.Models;
using Island.Services;

namespace Island.Controls;

/// <summary>Renders an <see cref="IconSpec"/> (glyph, app icon, image file or emoji) at a given size.</summary>
public sealed class IconView : ContentControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(IconSpec), typeof(IconView), new PropertyMetadata(null, (d, e) => ((IconView)d).OnIconChanged(e)));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(IconView), new PropertyMetadata(24.0, (d, _) => ((IconView)d).Rebuild()));

    public IconSpec? Icon
    {
        get => (IconSpec?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public IconView()
    {
        Focusable = false;
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
    }

    private void OnIconChanged(DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is IconSpec old) old.PropertyChanged -= OnSpecChanged;
        if (e.NewValue is IconSpec spec) spec.PropertyChanged += OnSpecChanged;
        Rebuild();
    }

    private void OnSpecChanged(object? sender, PropertyChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        var spec = Icon;
        double size = IconSize;
        if (spec is null)
        {
            Content = null;
            return;
        }

        Brush? tint = ThemeService.ParseColor(spec.TintColor) is { } c ? new SolidColorBrush(c) : null;

        switch (spec.Kind)
        {
            case IconKind.Glyph:
            {
                var tb = new TextBlock
                {
                    Text = GlyphInfo.GlyphText(spec.Value),
                    FontFamily = GlyphCatalog.IconFont,
                    FontSize = size * 0.8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (tint is not null) tb.Foreground = tint;
                Content = tb;
                break;
            }
            case IconKind.Emoji:
            {
                var tb = new TextBlock
                {
                    Text = string.IsNullOrEmpty(spec.Value) ? "🙂" : spec.Value,
                    FontFamily = new FontFamily("Segoe UI Emoji"),
                    FontSize = size * 0.78,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (tint is not null) tb.Foreground = tint;
                Content = tb;
                break;
            }
            case IconKind.AppIcon:
            case IconKind.Image:
            {
                var src = spec.Kind == IconKind.AppIcon ? IconService.GetFileIcon(spec.Value) : IconService.LoadImageFile(spec.Value);
                if (src is null)
                {
                    Content = new TextBlock
                    {
                        Text = GlyphInfo.GlyphText("E7AC"),
                        FontFamily = GlyphCatalog.IconFont,
                        FontSize = size * 0.75,
                        Opacity = 0.6,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    break;
                }
                if (tint is not null && spec.Kind == IconKind.Image)
                {
                    // Tinted image: paint the tint through the image's alpha channel.
                    Content = new Rectangle
                    {
                        Width = size,
                        Height = size,
                        Fill = tint,
                        OpacityMask = new ImageBrush(src) { Stretch = Stretch.Uniform },
                    };
                }
                else
                {
                    var img = new Image { Source = src, Width = size, Height = size, Stretch = Stretch.Uniform };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    Content = img;
                }
                break;
            }
        }
    }
}

/// <summary>Small indeterminate progress ring.</summary>
public sealed class Spinner : ContentControl
{
    private readonly RotateTransform _rotate = new();
    private readonly Storyboard _story = new();

    public Spinner()
    {
        IsHitTestVisible = false;
        Width = Height = 18;
        var arc = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 9,1.5 A 7.5,7.5 0 1 1 1.5,9"),
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.None,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _rotate,
            Width = 18,
            Height = 18,
        };
        arc.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        Content = new Viewbox { Child = arc };

        var anim = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.9))) { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(anim, arc);
        Storyboard.SetTargetProperty(anim, new PropertyPath("RenderTransform.Angle"));
        _story.Children.Add(anim);

        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _story.Begin(this, true);
            else _story.Stop(this);
        };
    }
}
