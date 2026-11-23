using System;
using System.IO;

namespace Island.Core.Search;

public static class SearchPolicy
{
    /// <summary>
    /// The path to take a file result's icon from. Most files share their type's icon, so they use a
    /// stand-in name per extension: the icon cache then grows with the number of file types, not with
    /// every file ever shown. Executables, shortcuts and icons have their own icons.
    /// </summary>
    public static string FileIconKey(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is "" or ".exe" or ".lnk" or ".ico" or ".url" or ".appref-ms" or ".msc" or ".cpl"
            ? path
            : "file" + ext;
    }

    /// <summary>
    /// Whether to hand focus back to the previous app before running a result. Results that open one
    /// of Island's own windows (Settings, run log) keep it, or that window could open without focus.
    /// </summary>
    public static bool RestoreFocusBeforeRunning(SearchResultKind kind, bool opensIslandWindow) =>
        kind != SearchResultKind.IslandCommand && !opensIslandWindow;
}
