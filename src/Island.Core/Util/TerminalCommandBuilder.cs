using Island.Core.Scripts;

namespace Island.Core.Util;

/// <summary>Builds the command line for "Claude Code here".</summary>
public static class TerminalCommandBuilder
{
    /// <summary>
    /// Windows Terminal: <c>wt.exe -d "&lt;folder&gt;" &lt;command&gt;</c>.
    /// Semicolons are escaped because wt treats a bare ";" as "new tab".
    /// If the command resolves to a .cmd/.bat shim (e.g. an npm-installed CLI) it is wrapped
    /// in <c>cmd /k</c>, because Windows Terminal can only start real executables directly.
    /// </summary>
    public static ResolvedCommand ForWindowsTerminal(string wtPath, string folder, string command, bool commandIsBatchShim)
    {
        var cmd = command.Trim().Replace(";", "\\;");
        if (commandIsBatchShim) cmd = "cmd /k " + cmd;
        return new ResolvedCommand(wtPath, $"-d {CommandLine.Quote(folder)} {cmd}");
    }

    /// <summary>Fallback: <c>powershell.exe -NoExit -Command "Set-Location -LiteralPath '&lt;folder&gt;'; &lt;command&gt;"</c>.</summary>
    public static ResolvedCommand ForPowerShell(string powershellExe, string folder, string command)
    {
        var script = $"Set-Location -LiteralPath '{folder.Replace("'", "''")}'; {command.Trim()}";
        return new ResolvedCommand(powershellExe, "-NoExit -Command " + CommandLine.Quote(script));
    }
}
