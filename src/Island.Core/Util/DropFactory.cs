using System;
using System.IO;
using Island.Core.Models;

namespace Island.Core.Util;

/// <summary>Creates buttons from files/folders dropped from Explorer.</summary>
public static class DropFactory
{
    /// <param name="path">Dropped path.</param>
    /// <param name="isDirectory">Whether the path is a folder.</param>
    /// <param name="readUrlFile">Reads a .url file's text (null = use File.ReadAllText).</param>
    public static ButtonItem Create(string path, bool isDirectory, Func<string, string>? readUrlFile = null)
    {
        var name = FileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) name = path;

        if (isDirectory)
        {
            var dirName = path.TrimEnd('\\', '/');
            dirName = dirName[(dirName.LastIndexOfAny(new[] { '\\', '/' }) + 1)..];
            return ButtonItem.Create(dirName.Length > 0 ? dirName : path, IconSpec.App(path), new OpenFolderAction { Path = path });
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".exe":
            case ".lnk":
            case ".appref-ms":
                return ButtonItem.Create(name, IconSpec.App(path), new LaunchAppAction { Path = path });
            case ".ps1":
            case ".py":
            case ".pyw":
            case ".js":
            case ".mjs":
            case ".cjs":
            case ".bat":
            case ".cmd":
                return ButtonItem.Create(name, IconSpec.Glyph("E943"), new RunScriptAction
                {
                    Source = ScriptSource.File,
                    ScriptPath = path,
                    Output = OutputHandling.Toast,
                });
            case ".url":
            {
                string? url = null;
                try { url = ParseUrlFile((readUrlFile ?? File.ReadAllText)(path)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                return ButtonItem.Create(name, IconSpec.Glyph("E774"), new OpenUrlAction { Url = url });
            }
            default:
                return ButtonItem.Create(name, IconSpec.App(path), new OpenFileAction { Path = path });
        }
    }

    /// <summary>Windows-style file name (handles '\\' separators on every OS).</summary>
    public static string FileNameWithoutExtension(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        int slash = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        var file = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        int dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    /// <summary>Reads the URL= line of an Internet Shortcut (.url) file.</summary>
    public static string? ParseUrlFile(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                return line.Substring(4).Trim();
        }
        return null;
    }
}
