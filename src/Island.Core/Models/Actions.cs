using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Island.Core.Models;

/// <summary>
/// Every button has exactly one action. The concrete type is stored in JSON
/// using a "type" discriminator (e.g. <c>"type": "LaunchApp"</c>).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type",
    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(LaunchAppAction), "LaunchApp")]
[JsonDerivedType(typeof(OpenFolderAction), "OpenFolder")]
[JsonDerivedType(typeof(OpenFileAction), "OpenFile")]
[JsonDerivedType(typeof(OpenUrlAction), "OpenUrl")]
[JsonDerivedType(typeof(GroupAction), "Group")]
[JsonDerivedType(typeof(ClaudeCodeHereAction), "ClaudeCodeHere")]
[JsonDerivedType(typeof(ScreenshotAction), "Screenshot")]
[JsonDerivedType(typeof(OpenSettingsAction), "OpenSettings")]
[JsonDerivedType(typeof(RunScriptAction), "RunScript")]
public abstract partial class ActionBase : ObservableObject
{
    [JsonIgnore]
    public ActionType Type => GetActionType();

    protected abstract ActionType GetActionType();
}

public enum ActionType
{
    LaunchApp,
    OpenFolder,
    OpenFile,
    OpenUrl,
    Group,
    ClaudeCodeHere,
    Screenshot,
    OpenSettings,
    RunScript,
}

public static class ActionFactory
{
    public static ActionBase Create(ActionType type) => type switch
    {
        ActionType.LaunchApp => new LaunchAppAction(),
        ActionType.OpenFolder => new OpenFolderAction(),
        ActionType.OpenFile => new OpenFileAction(),
        ActionType.OpenUrl => new OpenUrlAction(),
        ActionType.Group => new GroupAction(),
        ActionType.ClaudeCodeHere => new ClaudeCodeHereAction(),
        ActionType.Screenshot => new ScreenshotAction(),
        ActionType.OpenSettings => new OpenSettingsAction(),
        ActionType.RunScript => new RunScriptAction(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static string DisplayName(ActionType type) => type switch
    {
        ActionType.LaunchApp => "Launch app",
        ActionType.OpenFolder => "Open folder",
        ActionType.OpenFile => "Open file",
        ActionType.OpenUrl => "Open URL",
        ActionType.Group => "Group (folder)",
        ActionType.ClaudeCodeHere => "Claude Code here",
        ActionType.Screenshot => "Screenshot",
        ActionType.OpenSettings => "Open settings",
        ActionType.RunScript => "Run script",
        _ => type.ToString(),
    };

    /// <summary>Default glyph (Segoe Fluent / MDL2 code point) for a new button of this type.</summary>
    public static string DefaultGlyph(ActionType type) => type switch
    {
        ActionType.LaunchApp => "E7AC",
        ActionType.OpenFolder => "E8B7",
        ActionType.OpenFile => "E8A5",
        ActionType.OpenUrl => "E774",
        ActionType.Group => "E8F1",
        ActionType.ClaudeCodeHere => "E756",
        ActionType.Screenshot => "E722",
        ActionType.OpenSettings => "E713",
        ActionType.RunScript => "E943",
        _ => "E710",
    };
}

public partial class LaunchAppAction : ActionBase
{
    protected override ActionType GetActionType() => ActionType.LaunchApp;
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string? _arguments;
    [ObservableProperty] private string? _workingDirectory;
    [ObservableProperty] private bool _runAsAdmin;
}

public partial class OpenFolderAction : ActionBase
{
    protected override ActionType GetActionType() => ActionType.OpenFolder;
    [ObservableProperty] private string _path = "";
}

public partial class OpenFileAction : ActionBase
{
    protected override ActionType GetActionType() => ActionType.OpenFile;
    [ObservableProperty] private string _path = "";
}

public partial class OpenUrlAction : ActionBase
{
    protected override ActionType GetActionType() => ActionType.OpenUrl;
    /// <summary>Optional. Empty = open the default browser's home page.</summary>
    [ObservableProperty] private string? _url;
}

/// <summary>Opens a sub-panel with the button's <see cref="ButtonItem.Children"/>.</summary>
public partial class GroupAction : ActionBase
{
    protected override ActionType GetActionType() => ActionType.Group;
}

public partial class ClaudeCodeHereAction : ActionBase
{
    protected override ActionType GetActionType() => ActionType.ClaudeCodeHere;
    /// <summary>Command to run in the terminal. Empty = General.ClaudeCommand (default "claude").</summary>
    [ObservableProperty] private string? _command;
    /// <summary>Folder used when the last active window isn't Explorer. Empty = General.FallbackFolder.</summary>
    [ObservableProperty] private string? _fallbackFolder;
}

public enum ScreenshotMode { Snip, FullScreen, ActiveWindow }

public partial class ScreenshotAction : ActionBase
{
    protected override ActionType GetActionType() => ActionType.Screenshot;
    [ObservableProperty] private ScreenshotMode _mode = ScreenshotMode.Snip;
    /// <summary>Empty = General.ScreenshotFolder.</summary>
    [ObservableProperty] private string? _saveFolder;
    [ObservableProperty] private bool _copyToClipboard = true;
}

public partial class OpenSettingsAction : ActionBase
{
    protected override ActionType GetActionType() => ActionType.OpenSettings;
}

public enum ScriptSource { File, Inline }
public enum InterpreterMode { Auto, Custom }
public enum OutputHandling { Ignore, Toast, CopyToClipboard, ShowInPopup, JsonProtocol }

public partial class RunScriptAction : ActionBase
{
    protected override ActionType GetActionType() => ActionType.RunScript;

    [ObservableProperty] private ScriptSource _source = ScriptSource.Inline;
    /// <summary>Used when <see cref="Source"/> is File.</summary>
    [ObservableProperty] private string? _scriptPath;
    /// <summary>Used when <see cref="Source"/> is Inline. Saved to %APPDATA%\Island\scripts\&lt;buttonId&gt;&lt;ext&gt;.</summary>
    [ObservableProperty] private string? _inlineScript;
    /// <summary>File extension for inline scripts (decides the interpreter), e.g. ".ps1".</summary>
    [ObservableProperty] private string _inlineExtension = ".ps1";

    [ObservableProperty] private InterpreterMode _interpreter = InterpreterMode.Auto;
    [ObservableProperty] private string? _customInterpreterPath;
    /// <summary>Argument template for a custom interpreter. {script} and {args} are substituted.</summary>
    [ObservableProperty] private string _customArgumentTemplate = "\"{script}\" {args}";

    /// <summary>Arguments passed to the script. Supports placeholders like {currentFolder}.</summary>
    [ObservableProperty] private string? _arguments;
    /// <summary>Working directory. Supports placeholders. Empty = the script's folder.</summary>
    [ObservableProperty] private string? _workingDirectory;
    [ObservableProperty] private bool _showConsole;
    /// <summary>Seconds before the script is killed. 0 = no timeout.</summary>
    [ObservableProperty] private int _timeoutSeconds = 60;
    [ObservableProperty] private OutputHandling _output = OutputHandling.Toast;

    /// <summary>
    /// Set when a config containing scripts was imported and the user chose not to
    /// enable them yet. Disabled scripts never run.
    /// </summary>
    [ObservableProperty] private bool _disabled;
}
