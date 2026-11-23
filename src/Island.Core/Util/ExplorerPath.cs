using System;

namespace Island.Core.Util;

public static class ExplorerPath
{
    /// <summary>
    /// Converts an Explorer <c>LocationURL</c> (e.g. <c>file:///C:/Users/me/My%20Docs</c> or
    /// <c>file://server/share/dir</c>) into a Windows path. Returns null for anything that isn't
    /// a file system location (e.g. "This PC", search results, shell: namespaces).
    /// Implemented as plain string handling so it behaves identically on every OS.
    /// </summary>
    public static string? FromLocationUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        const string prefix = "file:";
        if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var rest = url.Substring(prefix.Length);
        string path;
        if (rest.StartsWith("///", StringComparison.Ordinal))
        {
            // file:///C:/dir  -> C:\dir
            path = rest.Substring(3);
        }
        else if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            // file://server/share -> \\server\share ; file://localhost/C:/x -> C:\x
            var hostAndPath = rest.Substring(2);
            if (hostAndPath.StartsWith("localhost/", StringComparison.OrdinalIgnoreCase))
                path = hostAndPath.Substring("localhost/".Length);
            else
                path = "//" + hostAndPath;
        }
        else
        {
            return null;
        }

        try
        {
            path = Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return null;
        }

        path = path.Replace('/', '\\');
        if (path.Length == 2 && path[1] == ':') path += "\\";   // "C:" -> "C:\"
        if (path.Length > 3 && path.EndsWith('\\')) path = path.TrimEnd('\\');
        return path.Length == 0 ? null : path;
    }

    /// <summary>Shell namespace paths ("::{GUID}", "shell:...") aren't usable as working directories.</summary>
    public static bool IsFileSystemPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && !path.StartsWith("::", StringComparison.Ordinal) &&
        !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) &&
        (path.Length >= 2 && path[1] == ':' || path.StartsWith(@"\\", StringComparison.Ordinal));
}
