using System.Data.OleDb;
using Island.Core.Search;

namespace Island.Search;

public sealed record FileHit(string Path, string Name, bool IsFolder);

/// <summary>
/// File and folder name search through the Windows Search index (the index Windows already keeps;
/// Island stores no file list of its own). Returns nothing if the Windows Search service is off.
/// </summary>
public static class FileSearch
{
    private const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';";

    public static Task<IReadOnlyList<FileHit>> QueryAsync(string query, int top, CancellationToken ct) =>
        Task.Run(() => Query(query, top, ct), ct);

    private static IReadOnlyList<FileHit> Query(string query, int top, CancellationToken ct)
    {
        // Ask for more than we show: the index's word-breaking also matches inside names
        // ("count_documents"), so results are filtered and re-ranked by how well the name matches.
        var sql = WindowsSearchQuery.Build(query, Math.Max(top * 5, 40));
        if (sql is null || ct.IsCancellationRequested) return Array.Empty<FileHit>();
        try
        {
            using var conn = new OleDbConnection(ConnectionString);
            conn.Open();
            using var cmd = new OleDbCommand(sql, conn);
            using var reader = cmd.ExecuteReader();
            var hits = new List<FileHit>();
            while (reader.Read())
            {
                if (ct.IsCancellationRequested) break;
                var path = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
                if (string.IsNullOrEmpty(path) || WindowsSearchQuery.IsNoisePath(path)) continue;
                var name = reader.IsDBNull(1) ? System.IO.Path.GetFileName(path) : reader.GetValue(1)?.ToString() ?? path;
                var type = reader.IsDBNull(2) ? "" : reader.GetValue(2)?.ToString() ?? "";
                hits.Add(new FileHit(path, name, type.Equals("Directory", StringComparison.OrdinalIgnoreCase)));
            }
            return hits
                .Select(h => (Hit: h, Score: SearchMatcher.Score(query, h.Name) + (h.IsFolder ? 5 : 0)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(top)
                .Select(x => x.Hit)
                .ToList();
        }
        catch (Exception)
        {
            // Search service disabled, provider missing, or a malformed query: no file results.
            return Array.Empty<FileHit>();
        }
    }
}
