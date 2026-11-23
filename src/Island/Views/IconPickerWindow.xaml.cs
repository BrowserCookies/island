using System.Windows;
using System.Windows.Controls;
using Island.Core.Models;
using Island.Interop;
using Island.Services;
using Microsoft.Win32;

namespace Island.Views;

public partial class IconPickerWindow : Window
{
    private static readonly string[] Emojis =
    {
        "😀", "😎", "🤓", "🤖", "👾", "🧠", "👀", "👍", "🙏", "💪", "🔥", "✨", "⭐", "🌟", "⚡", "💡",
        "🚀", "🛠️", "🔧", "🔨", "⚙️", "🧪", "🧰", "📦", "📁", "📂", "🗂️", "📄", "📝", "📋", "📌", "📎",
        "🔗", "🔒", "🔑", "🗝️", "💾", "💻", "🖥️", "⌨️", "🖱️", "🖨️", "📱", "📷", "🎥", "🎧", "🎵", "🎮",
        "🌐", "🌍", "☁️", "🌙", "☀️", "🌈", "❄️", "🌊", "🏝️", "🌴", "🍀", "🌸", "🍕", "☕", "🍺", "🎂",
        "✅", "❌", "⚠️", "❓", "❗", "💬", "📣", "🔔", "⏰", "⏱️", "📅", "📈", "📊", "💰", "🛒", "🏠",
        "🏢", "✈️", "🚗", "🚲", "❤️", "🧡", "💛", "💚", "💙", "💜", "🖤", "🤍",
    };

    private readonly AppServices _s;

    public IconPickerWindow(IconSpec initial, AppServices services, ActionBase? action)
    {
        _s = services;
        Current = initial;
        InitializeComponent();
        DataContext = Current;
        SourceInitialized += (_, _) => WindowHelper.SetDarkTitleBar(this, _s.Theme.IsDark);

        GlyphList.ItemsSource = GlyphCatalog.All;
        EmojiList.ItemsSource = Emojis;

        // Pre-fill "From app" with the button's own target when that makes sense.
        AppPathBox.Text = initial.Kind == IconKind.AppIcon ? initial.Value : action switch
        {
            LaunchAppAction la => la.Path,
            OpenFileAction of => of.Path,
            OpenFolderAction od => od.Path,
            _ => "",
        };
        if (initial.Kind == IconKind.Glyph) CodePointBox.Text = initial.Value;
        if (initial.Kind == IconKind.Emoji) EmojiBox.Text = initial.Value;
        if (initial.Kind == IconKind.Image) ImagePathText.Text = initial.Value;

        Tabs.SelectedIndex = initial.Kind switch
        {
            IconKind.AppIcon => 1,
            IconKind.Image => 2,
            IconKind.Emoji => 3,
            _ => 0,
        };
        Loaded += (_, _) => SearchBox.Focus();
    }

    public IconSpec Current { get; }
    public IconSpec? Result { get; private set; }

    private void Set(IconKind kind, string value)
    {
        Current.Kind = kind;
        Current.Value = value;
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        GlyphList.ItemsSource = q.Length == 0
            ? GlyphCatalog.All
            : GlyphCatalog.All.Where(g =>
                g.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                g.Tags.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                g.Code.Equals(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void Glyph_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GlyphInfo g })
        {
            Set(IconKind.Glyph, g.Code);
            CodePointBox.Text = g.Code;
        }
    }

    private void UseCodePoint_Click(object sender, RoutedEventArgs e)
    {
        var code = CodePointBox.Text.Trim().TrimStart('\\', 'u', 'U', '+').ToUpperInvariant();
        if (GlyphInfo.GlyphText(code) == "?")
        {
            MessageBox.Show(this, "Enter a hexadecimal code point, e.g. E713.", "Island", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Set(IconKind.Glyph, code);
    }

    private void BrowseApp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Programs, shortcuts and icons (*.exe;*.lnk;*.ico;*.dll)|*.exe;*.lnk;*.ico;*.dll|All files (*.*)|*.*",
            DereferenceLinks = false,
        };
        if (dlg.ShowDialog(this) == true)
        {
            AppPathBox.Text = dlg.FileName;
            UseAppIcon_Click(sender, e);
        }
    }

    private void UseAppIcon_Click(object sender, RoutedEventArgs e)
    {
        var p = AppPathBox.Text.Trim().Trim('"');
        if (p.Length == 0) return;
        IconService.Invalidate(p);
        Set(IconKind.AppIcon, p);
    }

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Images (*.png;*.ico)|*.png;*.ico|All images (*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var copy = IconService.ImportImage(dlg.FileName, _s.Config.Store.IconsDirectory);
            ImagePathText.Text = copy;
            Set(IconKind.Image, copy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Couldn't copy the image", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string emoji })
        {
            EmojiBox.Text = emoji;
            Set(IconKind.Emoji, emoji);
        }
    }

    private void UseEmoji_Click(object sender, RoutedEventArgs e)
    {
        var t = EmojiBox.Text.Trim();
        if (t.Length > 0) Set(IconKind.Emoji, t);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Current.TintColor)) Current.TintColor = null;
        if (string.IsNullOrWhiteSpace(Current.Background)) Current.Background = null;
        Result = Current;
        DialogResult = true;
    }
}
