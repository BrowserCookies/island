using System;
using System.Collections.Generic;
using System.IO;
using Island.Core.Models;
using Island.Core.Scripts;

namespace Island.Core.Config;

/// <summary>Validation used by the settings editor. Invalid buttons still save; they just get a warning badge.</summary>
public sealed class ButtonValidator
{
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _dirExists;
    private readonly Func<string, string> _expand;

    public ButtonValidator(Func<string, bool>? fileExists = null, Func<string, bool>? dirExists = null, Func<string, string>? expand = null)
    {
        _fileExists = fileExists ?? File.Exists;
        _dirExists = dirExists ?? Directory.Exists;
        _expand = expand ?? Environment.ExpandEnvironmentVariables;
    }

    public IReadOnlyList<string> Validate(ButtonItem item)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(item.Label))
            errors.Add("Label is empty.");

        switch (item.Action)
        {
            case LaunchAppAction a:
                RequireFile(a.Path, "App path", errors, allowBareName: true);
                OptionalDir(a.WorkingDirectory, "Working directory", errors);
                break;
            case OpenFolderAction a:
                if (string.IsNullOrWhiteSpace(a.Path)) errors.Add("Folder path is required.");
                else if (!_dirExists(_expand(a.Path))) errors.Add($"Folder not found: {a.Path}");
                break;
            case OpenFileAction a:
                RequireFile(a.Path, "File path", errors, allowBareName: false);
                break;
            case OpenUrlAction a:
                if (!string.IsNullOrWhiteSpace(a.Url) && !IsValidUrl(a.Url))
                    errors.Add($"Invalid URL: {a.Url} (include the scheme, e.g. https://)");
                break;
            case GroupAction:
                if (item.Children is null || item.Children.Count == 0) errors.Add("Group has no buttons.");
                break;
            case ClaudeCodeHereAction a:
                OptionalDir(a.FallbackFolder, "Fallback folder", errors);
                break;
            case ScreenshotAction:
                break;
            case RunScriptAction a:
                ValidateScript(a, errors);
                break;
        }
        return errors;
    }

    private void ValidateScript(RunScriptAction a, List<string> errors)
    {
        if (a.Disabled)
            errors.Add("Script is disabled (imported config). Untick \"Disabled\" to allow it to run.");

        string ext;
        if (a.Source == ScriptSource.File)
        {
            RequireFile(a.ScriptPath, "Script path", errors, allowBareName: false);
            ext = Path.GetExtension(a.ScriptPath ?? "");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(a.InlineScript)) errors.Add("Inline script is empty.");
            ext = a.InlineExtension;
        }

        if (a.Interpreter == InterpreterMode.Custom)
        {
            if (string.IsNullOrWhiteSpace(a.CustomInterpreterPath)) errors.Add("Custom interpreter path is required.");
            else if (!a.CustomArgumentTemplate.Contains("{script}", StringComparison.Ordinal))
                errors.Add("Custom argument template should contain {script}.");
        }
        else if (!InterpreterResolver.IsKnownExtension(ext))
        {
            errors.Add($"No built-in interpreter for \"{ext}\". Choose a custom interpreter.");
        }

        if (a.TimeoutSeconds < 0) errors.Add("Timeout can't be negative.");
    }

    private void RequireFile(string? path, string what, List<string> errors, bool allowBareName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"{what} is required.");
            return;
        }
        var p = _expand(path.Trim().Trim('"'));
        // Bare names like "notepad" or "code" are resolved via PATH / App Paths at launch time.
        if (allowBareName && !p.Contains('\\') && !p.Contains('/')) return;
        if (!_fileExists(p) && !_dirExists(p)) errors.Add($"{what} not found: {path}");
    }

    private void OptionalDir(string? path, string what, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path) || PlaceholderExpander.ContainsPlaceholder(path)) return;
        if (!_dirExists(_expand(path))) errors.Add($"{what} not found: {path}");
    }

    /// <summary>Absolute URI with a scheme; http(s) URLs also need a host. Other schemes (mailto:, ms-settings:) are allowed.</summary>
    public static bool IsValidUrl(string url)
    {
        var s = url.Trim();
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return false;
        if (u.Scheme is "http" or "https") return !string.IsNullOrEmpty(u.Host);
        // On Unix "/foo" parses as file:///foo; require an explicit scheme in the text.
        return s.Contains(':') && !s.StartsWith('/');
    }
}
