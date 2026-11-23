using System;
using System.Collections.Generic;
using System.Text;

namespace Island.Core.Scripts;

/// <summary>
/// Expands {name} placeholders. Values are produced lazily (so the clipboard or the
/// Explorer folder is only queried when the template actually uses them).
/// Unknown placeholders are left untouched; "{{" and "}}" produce literal braces.
/// </summary>
public sealed class PlaceholderExpander
{
    public static readonly string[] KnownNames = { "currentFolder", "clipboard", "date", "time", "islandDir" };

    private readonly Dictionary<string, Func<string>> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public PlaceholderExpander Add(string name, Func<string> provider)
    {
        _providers[name] = provider;
        return this;
    }

    /// <summary>Standard set: {date} (yyyy-MM-dd), {time} (HH-mm-ss, filename-safe), plus the given providers.</summary>
    public static PlaceholderExpander CreateStandard(Func<string> currentFolder, Func<string> clipboard, string islandDir, Func<DateTime>? now = null)
    {
        now ??= () => DateTime.Now;
        return new PlaceholderExpander()
            .Add("currentFolder", currentFolder)
            .Add("clipboard", clipboard)
            .Add("date", () => now().ToString("yyyy-MM-dd"))
            .Add("time", () => now().ToString("HH-mm-ss"))
            .Add("islandDir", () => islandDir);
    }

    public static bool ContainsPlaceholder(string? template)
    {
        if (string.IsNullOrEmpty(template)) return false;
        foreach (var n in KnownNames)
            if (template.Contains("{" + n + "}", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public string Expand(string? template)
    {
        if (string.IsNullOrEmpty(template)) return template ?? "";
        var sb = new StringBuilder(template.Length);
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
            {
                sb.Append('{');
                i += 2;
                continue;
            }
            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                sb.Append('}');
                i += 2;
                continue;
            }
            if (c == '{')
            {
                int end = template.IndexOf('}', i + 1);
                if (end > i + 1)
                {
                    var name = template.Substring(i + 1, end - i - 1);
                    if (TryGet(name, out var value))
                    {
                        sb.Append(value);
                        i = end + 1;
                        continue;
                    }
                }
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private bool TryGet(string name, out string value)
    {
        if (_cache.TryGetValue(name, out value!)) return true;
        if (!_providers.TryGetValue(name, out var p)) return false;
        string v;
        try { v = p() ?? ""; }
        catch (Exception) { v = ""; }
        _cache[name] = v;
        value = v;
        return true;
    }
}
