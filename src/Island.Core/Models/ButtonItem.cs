using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Island.Core.Models;

public partial class ButtonItem : ObservableObject
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string? _tooltip;
    [ObservableProperty] private IconSpec _icon = new();
    [ObservableProperty] private ActionBase _action = new OpenSettingsAction();
    /// <summary>Child buttons; only meaningful when <see cref="Action"/> is a <see cref="GroupAction"/>.</summary>
    [ObservableProperty] private ObservableCollection<ButtonItem>? _children;

    // ---- UI-only state (never serialized) ----
    [ObservableProperty]
    [property: JsonIgnore] private bool _isSelected;
    [ObservableProperty]
    [property: JsonIgnore] private bool _isExpanded = true;
    /// <summary>Validation message(s) shown as a warning badge in the settings tree.</summary>
    [ObservableProperty]
    [property: JsonIgnore] private string? _warning;

    [JsonIgnore] public bool IsGroup => Action is GroupAction;

    partial void OnActionChanged(ActionBase value)
    {
        if (value is GroupAction && Children is null)
            Children = new ObservableCollection<ButtonItem>();
        OnPropertyChanged(nameof(IsGroup));
    }

    [JsonIgnore]
    public string DisplayTooltip => string.IsNullOrWhiteSpace(Tooltip) ? Label : Tooltip!;

    partial void OnTooltipChanged(string? value) => OnPropertyChanged(nameof(DisplayTooltip));
    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(DisplayTooltip));

    public static ButtonItem Create(string label, IconSpec icon, ActionBase action, params ButtonItem[] children)
    {
        var b = new ButtonItem { Label = label, Icon = icon, Action = action };
        if (action is GroupAction || children.Length > 0)
            b.Children = new ObservableCollection<ButtonItem>(children);
        return b;
    }
}

public enum IconKind { Glyph, AppIcon, Image, Emoji }

public partial class IconSpec : ObservableObject
{
    [ObservableProperty] private IconKind _kind = IconKind.Glyph;
    /// <summary>
    /// Glyph: hex code point (e.g. "E713"). AppIcon: path to exe/lnk/file.
    /// Image: path to a PNG/ICO (copied into %APPDATA%\Island\icons). Emoji: the emoji text.
    /// </summary>
    [ObservableProperty] private string _value = "E713";
    /// <summary>Optional "#RRGGBB" / "#AARRGGBB" tint for glyphs, emoji and images.</summary>
    [ObservableProperty] private string? _tintColor;
    /// <summary>Optional "#RRGGBB" / "#AARRGGBB" button background.</summary>
    [ObservableProperty] private string? _background;

    public static IconSpec Glyph(string codepoint) => new() { Kind = IconKind.Glyph, Value = codepoint };
    public static IconSpec App(string path) => new() { Kind = IconKind.AppIcon, Value = path };
    public static IconSpec Emoji(string emoji) => new() { Kind = IconKind.Emoji, Value = emoji };
    public static IconSpec Image(string path) => new() { Kind = IconKind.Image, Value = path };

    public IconSpec Clone() => new() { Kind = Kind, Value = Value, TintColor = TintColor, Background = Background };
}
