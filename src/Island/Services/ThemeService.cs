using System.Windows;
using System.Windows.Media;
using Island.Core.Models;
using Microsoft.Win32;

namespace Island.Services;

/// <summary>
/// Builds the color palette (dark / light + accent) and swaps it into the application
/// resources. All views use DynamicResource, so a theme change applies immediately.
/// </summary>
public sealed class ThemeService
{
    private ResourceDictionary? _current;
    private GeneralSettings? _settings;

    public bool IsDark { get; private set; }
    public Color Accent { get; private set; }

    public event EventHandler? ThemeChanged;

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle
                && _settings is not null)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(_settings));
        };
    }

    public void Apply(GeneralSettings settings)
    {
        _settings = settings;
        IsDark = settings.Theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => SystemUsesDarkTheme(),
        };
        Accent = ParseColor(settings.AccentColor) ?? SystemAccent() ?? Color.FromRgb(0x00, 0x78, 0xD4);

        var d = Build(IsDark, Accent);
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        // Later merged dictionaries win, so the palette goes last (after Controls.xaml's fallbacks).
        merged.Add(d);
        _current = d;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static ResourceDictionary Build(bool dark, Color accent)
    {
        var d = new ResourceDictionary();
        void B(string key, Color c) { var b = new SolidColorBrush(c); b.Freeze(); d[key] = b; d[key + "Color"] = c; }

        var accentFg = Luminance(accent) > 0.55 ? Colors.Black : Colors.White;
        B("AccentBrush", accent);
        B("AccentForegroundBrush", accentFg);
        B("AccentSoftBrush", Color.FromArgb(0x40, accent.R, accent.G, accent.B));

        if (dark)
        {
            B("IslandBackgroundBrush", Color.FromRgb(0x20, 0x20, 0x24));
            B("IslandBorderBrush", Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            B("IslandForegroundBrush", Color.FromRgb(0xF2, 0xF2, 0xF2));
            B("PanelBackgroundBrush", Color.FromArgb(0xF2, 0x26, 0x26, 0x2B));
            B("TileBackgroundBrush", Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            B("TileHoverBrush", Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
            B("TilePressedBrush", Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            B("WindowBackgroundBrush", Color.FromRgb(0x1F, 0x1F, 0x23));
            B("SurfaceBrush", Color.FromRgb(0x2A, 0x2A, 0x30));
            B("ControlBackgroundBrush", Color.FromRgb(0x2D, 0x2D, 0x33));
            B("ControlHoverBrush", Color.FromRgb(0x38, 0x38, 0x40));
            B("ControlBorderBrush", Color.FromRgb(0x48, 0x48, 0x52));
            B("ForegroundBrush", Color.FromRgb(0xF2, 0xF2, 0xF2));
            B("SubtleForegroundBrush", Color.FromRgb(0xA8, 0xA8, 0xB0));
            B("DisabledForegroundBrush", Color.FromRgb(0x70, 0x70, 0x78));
            B("SelectionBrush", Color.FromArgb(0x66, accent.R, accent.G, accent.B));
            B("ToastBackgroundBrush", Color.FromArgb(0xF5, 0x2B, 0x2B, 0x31));
            B("ErrorBrush", Color.FromRgb(0xFF, 0x6B, 0x6B));
            B("WarningBrush", Color.FromRgb(0xFF, 0xC1, 0x4D));
            B("SuccessBrush", Color.FromRgb(0x5F, 0xD0, 0x8A));
            B("ShadowColorBrush", Colors.Black);
        }
        else
        {
            B("IslandBackgroundBrush", Color.FromRgb(0xFB, 0xFB, 0xFC));
            B("IslandBorderBrush", Color.FromArgb(0x30, 0x00, 0x00, 0x00));
            B("IslandForegroundBrush", Color.FromRgb(0x1B, 0x1B, 0x1F));
            B("PanelBackgroundBrush", Color.FromArgb(0xF5, 0xF7, 0xF7, 0xF9));
            B("TileBackgroundBrush", Color.FromArgb(0x0C, 0x00, 0x00, 0x00));
            B("TileHoverBrush", Color.FromArgb(0x1C, 0x00, 0x00, 0x00));
            B("TilePressedBrush", Color.FromArgb(0x28, 0x00, 0x00, 0x00));
            B("WindowBackgroundBrush", Color.FromRgb(0xF3, 0xF3, 0xF5));
            B("SurfaceBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
            B("ControlBackgroundBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
            B("ControlHoverBrush", Color.FromRgb(0xEB, 0xEB, 0xEF));
            B("ControlBorderBrush", Color.FromRgb(0xCC, 0xCC, 0xD2));
            B("ForegroundBrush", Color.FromRgb(0x1B, 0x1B, 0x1F));
            B("SubtleForegroundBrush", Color.FromRgb(0x5F, 0x5F, 0x68));
            B("DisabledForegroundBrush", Color.FromRgb(0xA0, 0xA0, 0xA8));
            B("SelectionBrush", Color.FromArgb(0x40, accent.R, accent.G, accent.B));
            B("ToastBackgroundBrush", Color.FromArgb(0xF8, 0xFF, 0xFF, 0xFF));
            B("ErrorBrush", Color.FromRgb(0xC4, 0x2B, 0x1C));
            B("WarningBrush", Color.FromRgb(0x9D, 0x5D, 0x00));
            B("SuccessBrush", Color.FromRgb(0x0F, 0x7B, 0x0F));
            B("ShadowColorBrush", Color.FromRgb(0x40, 0x40, 0x40));
        }

        // Make the stock controls (TreeView selection, menus) follow the palette.
        d[SystemColors.HighlightBrushKey] = d["SelectionBrush"];
        d[SystemColors.HighlightTextBrushKey] = d["ForegroundBrush"];
        d[SystemColors.InactiveSelectionHighlightBrushKey] = d["SelectionBrush"];
        d[SystemColors.InactiveSelectionHighlightTextBrushKey] = d["ForegroundBrush"];
        d[SystemColors.ControlTextBrushKey] = d["ForegroundBrush"];
        d[SystemColors.WindowTextBrushKey] = d["ForegroundBrush"];
        d[SystemColors.WindowBrushKey] = d["ControlBackgroundBrush"];
        d[SystemColors.MenuBrushKey] = d["SurfaceBrush"];
        d[SystemColors.MenuTextBrushKey] = d["ForegroundBrush"];
        d[SystemColors.GrayTextBrushKey] = d["DisabledForegroundBrush"];
        return d;
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception) { return false; }
    }

    public static Color? SystemAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                var u = unchecked((uint)abgr);
                return Color.FromRgb((byte)(u & 0xFF), (byte)((u >> 8) & 0xFF), (byte)((u >> 16) & 0xFF));
            }
        }
        catch (Exception) { }
        return null;
    }

    public static Color? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var t = text.Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            return (Color)ColorConverter.ConvertFromString(t);
        }
        catch (FormatException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
}
