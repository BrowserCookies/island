using System;
using System.IO;
using Island.Core.Models;

namespace Island.Core.Scripts;

public sealed record ResolvedCommand(string FileName, string Arguments);

/// <summary>Turns a script path + RunScript settings into an executable and an argument string.</summary>
public static class InterpreterResolver
{
    public static bool IsKnownExtension(string? ext) =>
        (ext ?? "").ToLowerInvariant() is ".ps1" or ".py" or ".pyw" or ".js" or ".mjs" or ".cjs" or ".bat" or ".cmd" or ".exe";

    /// <param name="scriptPath">Full path to the script file.</param>
    /// <param name="expandedArgs">Script arguments, already placeholder-expanded.</param>
    /// <param name="action">The RunScript action (interpreter mode / custom template).</param>
    /// <param name="settings">For PowerShell / Python / Node preferences.</param>
    /// <param name="pwshAvailable">Whether pwsh.exe was found (only used if Pwsh is preferred).</param>
    public static ResolvedCommand Resolve(string scriptPath, string expandedArgs, RunScriptAction action, GeneralSettings settings, bool pwshAvailable)
    {
        expandedArgs = expandedArgs?.Trim() ?? "";

        if (action.Interpreter == InterpreterMode.Custom)
        {
            var exe = (action.CustomInterpreterPath ?? "").Trim().Trim('"');
            if (exe.Length == 0) throw new InvalidOperationException("No custom interpreter is set.");
            var args = (action.CustomArgumentTemplate ?? "\"{script}\" {args}")
                .Replace("{script}", scriptPath, StringComparison.Ordinal)
                .Replace("{args}", expandedArgs, StringComparison.Ordinal);
            return new ResolvedCommand(exe, args.Trim());
        }

        var ext = Path.GetExtension(scriptPath).ToLowerInvariant();
        string quoted = "\"" + scriptPath + "\"";
        string Tail(string s) => expandedArgs.Length == 0 ? s : s + " " + expandedArgs;

        switch (ext)
        {
            case ".ps1":
            {
                var exe = settings.PowerShell == PowerShellKind.Pwsh && pwshAvailable ? "pwsh.exe" : "powershell.exe";
                return new ResolvedCommand(exe, Tail($"-NoProfile -ExecutionPolicy Bypass -File {quoted}"));
            }
            case ".py":
            case ".pyw":
            {
                var (exe, pre) = CommandLine.SplitExecutable(string.IsNullOrWhiteSpace(settings.PythonCommand) ? "python" : settings.PythonCommand);
                var head = pre.Length > 0 ? $"{pre} {quoted}" : quoted;
                return new ResolvedCommand(exe, Tail(head));
            }
            case ".js":
            case ".mjs":
            case ".cjs":
            {
                var (exe, pre) = CommandLine.SplitExecutable(string.IsNullOrWhiteSpace(settings.NodeCommand) ? "node" : settings.NodeCommand);
                var head = pre.Length > 0 ? $"{pre} {quoted}" : quoted;
                return new ResolvedCommand(exe, Tail(head));
            }
            case ".bat":
            case ".cmd":
                // cmd strips the outer quotes with /s, keeping the quoted script path intact.
                return new ResolvedCommand("cmd.exe", $"/d /s /c \"{Tail(quoted)}\"");
            case ".exe":
                return new ResolvedCommand(scriptPath, expandedArgs);
            default:
                throw new InvalidOperationException(
                    $"No built-in interpreter for \"{ext}\" files. Set a custom interpreter for this button.");
        }
    }
}
