using Island.Core.Models;
using Island.Core.Scripts;

namespace Island.Services.Handlers;

/// <summary>
/// Runs a user script asynchronously: resolves the interpreter, expands placeholders, enforces the
/// timeout, logs the run and handles the output (toast / clipboard / popup / JSON protocol).
/// </summary>
public sealed class RunScriptHandler : ActionHandler<RunScriptAction>
{
    protected override async Task ExecuteAsync(RunScriptAction a, ActionContext ctx, CancellationToken ct)
    {
        var s = ctx.Services;
        var label = string.IsNullOrWhiteSpace(ctx.Button.Label) ? "Script" : ctx.Button.Label;

        if (a.Disabled)
            throw new ActionException("This script came from an imported config and is disabled. Enable it in Settings to run it.");

        // ---- script file
        string scriptPath;
        if (a.Source == ScriptSource.File)
        {
            scriptPath = PathUtil.Expand(a.ScriptPath);
            if (scriptPath.Length == 0) throw new ActionException("No script file is set.");
            if (!File.Exists(scriptPath)) throw new ActionException($"Script not found: {scriptPath}");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(a.InlineScript)) throw new ActionException("The inline script is empty.");
            scriptPath = ScriptFiles.EnsureInlineFile(ctx.Button.Id, a, s.Config.Store.ScriptsDirectory);
        }

        // ---- placeholders (resolved up front: the clipboard must be read on the UI thread,
        //      and the Explorer lookup must not block it). Both are also passed as environment
        //      variables, which is safer than command-line arguments for arbitrary text.
        string currentFolder = await Task.Run(() => s.CurrentFolder.Resolve(s.Settings.FallbackFolder), ct);
        string clipboard = ClipboardHelper.GetText();

        var expander = PlaceholderExpander.CreateStandard(() => currentFolder, () => clipboard, s.DataDirectory);
        var args = expander.Expand(a.Arguments);
        var wd = PathUtil.Expand(expander.Expand(a.WorkingDirectory));
        if (wd.Length == 0)
            wd = a.Source == ScriptSource.File
                ? Path.GetDirectoryName(scriptPath) ?? ""
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(wd)) throw new ActionException($"Working directory not found: {wd}");

        // ---- interpreter
        bool pwsh = s.Settings.PowerShell == PowerShellKind.Pwsh && ShellLauncher.FindOnPath("pwsh.exe") is not null;
        ResolvedCommand cmd;
        try
        {
            cmd = InterpreterResolver.Resolve(scriptPath, args, a, s.Settings, pwsh);
            if (a.Interpreter == InterpreterMode.Custom)
                cmd = cmd with { FileName = PathUtil.Expand(cmd.FileName), Arguments = expander.Expand(cmd.Arguments) };
        }
        catch (InvalidOperationException ex)
        {
            throw new ActionException(ex.Message);
        }

        var env = new Dictionary<string, string>
        {
            ["PYTHONIOENCODING"] = "utf-8",
            ["PYTHONUTF8"] = "1",
            ["ISLAND_DIR"] = s.DataDirectory,
            ["ISLAND_BUTTON"] = label,
            ["ISLAND_CURRENT_FOLDER"] = currentFolder,
            // A single environment variable is limited to 32,767 characters.
            ["ISLAND_CLIPBOARD"] = clipboard.Length > 30000 ? clipboard[..30000] : clipboard,
        };

        // ---- run
        var timeout = a.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(a.TimeoutSeconds) : (TimeSpan?)null;
        var result = await ProcessRunner.RunAsync(new ProcessSpec(cmd.FileName, cmd.Arguments, wd, a.ShowConsole, env), timeout, ct);

        var entry = new RunLogEntry
        {
            Time = DateTime.Now,
            ButtonId = ctx.Button.Id,
            ButtonLabel = label,
            Command = $"{cmd.FileName} {cmd.Arguments}".Trim(),
            ExitCode = result.ExitCode,
            Outcome = result.Outcome,
            DurationMs = result.Duration.TotalMilliseconds,
            Stdout = result.Stdout,
            Stderr = result.Stderr,
            Error = result.StartError,
        };
        s.RunLog.Add(entry);

        var details = new ToastAction("View details", () => s.Host.ShowOutputPopup($"{label} — output", entry.FullText));

        switch (result.Outcome)
        {
            case ProcessOutcome.FailedToStart:
                s.Toasts.Error(result.StartError ?? "The script could not be started.", label, details);
                return;
            case ProcessOutcome.TimedOut:
                s.Toasts.Error($"Timed out after {a.TimeoutSeconds} s and was stopped.", label, details);
                return;
            case ProcessOutcome.Cancelled:
                s.Toasts.Info("Cancelled.", label);
                return;
        }

        if (result.ExitCode != 0)
        {
            var firstErr = ScriptOutputParser.FirstNonEmptyLine(result.Stderr) ?? ScriptOutputParser.LastNonEmptyLine(result.Stdout);
            var msg = $"Exit code {result.ExitCode}" + (firstErr is null ? "" : $": {ScriptOutputParser.Truncate(firstErr, 200)}");
            s.Toasts.Error(msg, label, details);
            return;
        }

        HandleOutput(a.Output, result.Stdout, label, s, details);
    }

    internal static void HandleOutput(OutputHandling mode, string stdout, string label, AppServices s, ToastAction details)
    {
        switch (mode)
        {
            case OutputHandling.Ignore:
                break;

            case OutputHandling.CopyToClipboard:
            {
                var text = stdout.TrimEnd('\r', '\n');
                ClipboardHelper.SetText(text);
                s.Toasts.Success(text.Length == 0 ? "Output was empty; clipboard cleared." : "Output copied to the clipboard.", label);
                break;
            }

            case OutputHandling.ShowInPopup:
                s.Host.ShowOutputPopup(label, stdout.Length == 0 ? "(no output)" : stdout);
                break;

            case OutputHandling.JsonProtocol:
            {
                var reply = ScriptOutputParser.ParseJsonProtocol(stdout);
                if (reply is null)
                {
                    ToastLastLine(stdout, label, s, details);
                    break;
                }
                ApplyReply(reply, label, s);
                break;
            }

            default: // Toast
                ToastLastLine(stdout, label, s, details);
                break;
        }
    }

    private static void ToastLastLine(string stdout, string label, AppServices s, ToastAction details)
    {
        var line = ScriptOutputParser.LastNonEmptyLine(stdout);
        if (line is null) s.Toasts.Success("Finished.", label);
        else s.Toasts.Info(ScriptOutputParser.Truncate(line), label, details);
    }

    internal static void ApplyReply(ScriptReply r, string label, AppServices s)
    {
        if (r.Clipboard is not null) ClipboardHelper.SetText(r.Clipboard);
        if (!string.IsNullOrWhiteSpace(r.OpenUrl))
        {
            try { ShellLauncher.Open(r.OpenUrl!); }
            catch (Exception ex) { s.Toasts.Error($"Couldn't open {r.OpenUrl}: {ex.Message}", label); }
        }
        if (!string.IsNullOrWhiteSpace(r.OpenPath))
        {
            try { ShellLauncher.OpenPath(r.OpenPath!); }
            catch (Exception ex) { s.Toasts.Error($"Couldn't open {r.OpenPath}: {ex.Message}", label); }
        }
        if (r.Popup is not null) s.Host.ShowOutputPopup(label, r.Popup);
        if (!string.IsNullOrWhiteSpace(r.Error)) s.Toasts.Error(ScriptOutputParser.Truncate(r.Error!), label);
        if (!string.IsNullOrWhiteSpace(r.Notify)) s.Toasts.Info(ScriptOutputParser.Truncate(r.Notify!), label);
        else if (r.Clipboard is not null && r.Error is null) s.Toasts.Success("Copied to the clipboard.", label);
    }
}
