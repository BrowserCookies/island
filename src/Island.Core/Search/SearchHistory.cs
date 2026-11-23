using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Island.Core.Search;

/// <summary>
/// How often and how recently each search result was used, so frequent picks rise to the top.
/// Persisted as JSON; keeps the 500 most recently used entries.
/// </summary>
public sealed class SearchHistory
{
    public const int MaxEntries = 500;

    private sealed record Entry(int Count, DateTime LastUsed);

    private readonly string? _path;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public SearchHistory(string? path) => _path = path;

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public static SearchHistory Load(string path)
    {
        var h = new SearchHistory(path);
        try
        {
            if (!File.Exists(path)) return h;
            var data = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path));
            if (data is not null)
                foreach (var (k, v) in data) h._entries[k] = v;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A broken history only costs ranking hints; start fresh.
        }
        return h;
    }

    public void Record(string key, DateTime now)
    {
        lock (_lock)
        {
            _entries[key] = _entries.TryGetValue(key, out var e) ? new Entry(e.Count + 1, now) : new Entry(1, now);
        }
    }

    /// <summary>Bounded ranking bonus: up to 200 for use count, +50 if used in the last 24 hours.</summary>
    public int Boost(string key, DateTime now)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var e)) return 0;
            int boost = Math.Min(e.Count, 20) * 10;
            if (now - e.LastUsed < TimeSpan.FromHours(24)) boost += 50;
            return boost;
        }
    }

    public void Save()
    {
        if (_path is null) return;
        Dictionary<string, Entry> keep;
        lock (_lock)
        {
            foreach (var old in _entries.OrderByDescending(kv => kv.Value.LastUsed).Skip(MaxEntries).Select(kv => kv.Key).ToList())
                _entries.Remove(old);
            keep = new Dictionary<string, Entry>(_entries, StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(keep));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
