using System;
using System.Text.Json;

namespace Island.Core.Scripts;

/// <summary>
/// Fields a script can send back with the JSON protocol (last non-empty stdout line).
/// All are optional; unknown fields are ignored.
/// </summary>
public sealed record ScriptReply(string? Notify, string? Clipboard, string? OpenUrl, string? OpenPath, string? Popup, string? Error)
{
    public bool IsEmpty => Notify is null && Clipboard is null && OpenUrl is null && OpenPath is null && Popup is null && Error is null;
}

public static class ScriptOutputParser
{
    public static string? LastNonEmptyLine(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var lines = text.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var l = lines[i].Trim('\r', ' ', '\t', '﻿');
            if (l.Length > 0) return l;
        }
        return null;
    }

    public static string? FirstNonEmptyLine(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var raw in text.Split('\n'))
        {
            var l = raw.Trim('\r', ' ', '\t', '﻿');
            if (l.Length > 0) return l;
        }
        return null;
    }

    /// <summary>
    /// Parses the last non-empty stdout line as a JSON object. Returns null if it isn't one
    /// (the caller then falls back to Toast behaviour).
    /// </summary>
    public static ScriptReply? ParseJsonProtocol(string? stdout)
    {
        var line = LastNonEmptyLine(stdout);
        if (line is null || !line.StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var root = doc.RootElement;
            return new ScriptReply(
                Get(root, "notify"),
                Get(root, "clipboard"),
                Get(root, "openUrl"),
                Get(root, "openPath"),
                Get(root, "popup"),
                Get(root, "error"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Get(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            return p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => p.Value.GetRawText(),
            };
        }
        return null;
    }

    /// <summary>Shortens text for a toast.</summary>
    public static string Truncate(string text, int max = 240) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
