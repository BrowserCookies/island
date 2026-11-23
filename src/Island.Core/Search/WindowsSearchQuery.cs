namespace Island.Core.Search;

/// <summary>Builds the Windows Search (SystemIndex) SQL for a file/folder name prefix search.</summary>
public static class WindowsSearchQuery
{
    private static readonly string[] NoiseSegments =
    {
        @"\node_modules\", @"\.git\", @"\AppData\", @"\obj\", @"\$Recycle.Bin\", @"\.vs\", @"\__pycache__\",
    };

    /// <summary>Build output, package caches and app data: indexed, but never what someone searches for.</summary>
    public static bool IsNoisePath(string path)
    {
        foreach (var seg in NoiseSegments)
            if (path.Contains(seg, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <returns>The SQL text, or null if nothing searchable is left after escaping.</returns>
    public static string? Build(string query, int top)
    {
        // Double quotes would end the CONTAINS phrase; single quotes are SQL-escaped by doubling.
        var term = query.Replace("\"", "").Replace("*", "").Trim();
        if (term.Length == 0) return null;
        term = term.Replace("'", "''");
        return $"SELECT TOP {top} System.ItemPathDisplay, System.ItemNameDisplay, System.ItemType FROM SystemIndex " +
               $"WHERE CONTAINS(System.FileName, '\"{term}*\"') AND SCOPE='file:' " +
               "ORDER BY System.Search.Rank DESC, System.DateModified DESC";
    }
}
