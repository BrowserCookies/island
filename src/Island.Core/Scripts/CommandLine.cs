using System;
using System.Collections.Generic;
using System.Text;

namespace Island.Core.Scripts;

/// <summary>Windows command-line helpers (CommandLineToArgvW-compatible quoting).</summary>
public static class CommandLine
{
    /// <summary>Quotes one argument so that the MSVC runtime parses it back unchanged.</summary>
    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return arg;
        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
            }
            else
            {
                sb.Append('\\', backslashes);
                sb.Append(c);
            }
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Splits a command line into tokens. Double quotes group, and are removed.
    /// Good enough for user-entered commands like <c>py -3</c> or <c>"C:\Program Files\x.exe" --flag</c>.
    /// </summary>
    public static List<string> Split(string? commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return result;
        var cur = new StringBuilder();
        bool inQuotes = false, any = false;
        foreach (char c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                any = true;
                continue;
            }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (any || cur.Length > 0) result.Add(cur.ToString());
                cur.Clear();
                any = false;
                continue;
            }
            cur.Append(c);
        }
        if (any || cur.Length > 0) result.Add(cur.ToString());
        return result;
    }

    /// <summary>Splits "exe rest of args" into the executable and the remaining raw argument text.</summary>
    public static (string Exe, string Args) SplitExecutable(string commandLine)
    {
        var s = commandLine.Trim();
        if (s.Length == 0) return ("", "");
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            if (end < 0) return (s.Trim('"'), "");
            return (s.Substring(1, end - 1), s[(end + 1)..].Trim());
        }
        int sp = s.IndexOfAny(new[] { ' ', '\t' });
        return sp < 0 ? (s, "") : (s[..sp], s[(sp + 1)..].Trim());
    }

    /// <summary>
    /// Extracts the executable from a registry "shell\open\command" value such as
    /// <c>"C:\Program Files\Google\Chrome\Application\chrome.exe" --single-argument %1</c>.
    /// </summary>
    public static string? ExtractExecutable(string? registryCommand)
    {
        if (string.IsNullOrWhiteSpace(registryCommand)) return null;
        var s = registryCommand.Trim();
        if (s.StartsWith('"')) return SplitExecutable(s).Exe;
        // Unquoted path with spaces: take up to ".exe".
        int exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return s[..(exe + 4)];
        return SplitExecutable(s).Exe;
    }
}
