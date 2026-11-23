using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Island.Core.Models;

public partial class IslandConfig : ObservableObject
{
    public const int CurrentSchemaVersion = 1;

    [ObservableProperty] private int _schemaVersion = CurrentSchemaVersion;
    [ObservableProperty] private GeneralSettings _general = new();
    [ObservableProperty] private ObservableCollection<ButtonItem> _buttons = new();
    [ObservableProperty] private ServicesSettings _services = new();
}

/// <summary>Connections to outside services (Settings → Services). Secrets are not stored here.</summary>
public partial class ServicesSettings : ObservableObject
{
    [ObservableProperty] private SpotifySettings _spotify = new();
}

public partial class SpotifySettings : ObservableObject
{
    /// <summary>Client ID of the user's own Spotify developer app (not a secret; PKCE needs no client secret).</summary>
    [ObservableProperty] private string? _clientId;
}

public enum ExpandLayout { Row, Column, Grid }
public enum ThemeMode { System, Dark, Light }
public enum PowerShellKind { WindowsPowerShell, Pwsh }
public enum HotkeyAction { ToggleExpand, ToggleVisibility }

public partial class GeneralSettings : ObservableObject
{
    // Startup & hotkey
    [ObservableProperty] private bool _startWithWindows;
    /// <summary>Global hotkey, e.g. "Ctrl+Alt+Space". Empty = none. The same hotkey + Shift opens settings.</summary>
    [ObservableProperty] private string? _hotkey = "Ctrl+Alt+Space";
    [ObservableProperty] private HotkeyAction _hotkeyAction = HotkeyAction.ToggleExpand;
    /// <summary>Global hotkey that opens the search bar. Empty = none.</summary>
    [ObservableProperty] private string? _searchHotkey = "Win+Shift+F";

    // Island
    [ObservableProperty] private bool _snapEnabled = true;
    [ObservableProperty] private int _snapMargin = 16;
    [ObservableProperty] private int _islandSize = 56;
    [ObservableProperty] private double _opacity = 1.0;
    [ObservableProperty] private bool _hoverEffect = true;

    // Expanded panel
    [ObservableProperty] private ExpandLayout _layout = ExpandLayout.Row;
    [ObservableProperty] private int _gridColumns = 4;
    [ObservableProperty] private bool _showLabels;
    [ObservableProperty] private bool _collapseAfterAction = true;

    // Look
    [ObservableProperty] private ThemeMode _theme = ThemeMode.System;
    /// <summary>"#RRGGBB". Empty = Windows accent color.</summary>
    [ObservableProperty] private string? _accentColor;

    // Defaults for actions
    [ObservableProperty] private string _claudeCommand = "claude";
    [ObservableProperty] private string _fallbackFolder = "%USERPROFILE%";
    [ObservableProperty] private string _screenshotFolder = @"%USERPROFILE%\Pictures\Screenshots";
    [ObservableProperty] private PowerShellKind _powerShell = PowerShellKind.WindowsPowerShell;
    [ObservableProperty] private string _pythonCommand = "python";
    [ObservableProperty] private string _nodeCommand = "node";
}
