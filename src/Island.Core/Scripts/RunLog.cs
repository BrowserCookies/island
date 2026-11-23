using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Island.Core.Config;

namespace Island.Core.Scripts;

public sealed class RunLogEntry
{
    public DateTime Time { get; set; }
    public Guid ButtonId { get; set; }
    public string ButtonLabel { get; set; } = "";
    public string Command { get; set; } = "";
    public int ExitCode { get; set; }
    public ProcessOutcome Outcome { get; set; }
    public double DurationMs { get; set; }
    public string Stdout { get; set; } = "";
    public string Stderr { get; set; } = "";
    public string? Error { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary => Outcome switch
    {
        ProcessOutcome.Completed => ExitCode == 0 ? "OK" : $"Exit {ExitCode}",
        ProcessOutcome.TimedOut => "Timed out",
        ProcessOutcome.Cancelled => "Cancelled",
        ProcessOutcome.FailedToStart => "Failed to start",
        _ => Outcome.ToString(),
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string DurationText => DurationMs < 1000 ? $"{DurationMs:0} ms" : $"{DurationMs / 1000:0.0} s";

    [System.Text.Json.Serialization.JsonIgnore]
    public string FullText =>
        $"{Time:yyyy-MM-dd HH:mm:ss}  {ButtonLabel}\n" +
        $"Command: {Command}\n" +
        $"Result: {Summary}   Duration: {DurationText}\n" +
        (Error is null ? "" : $"Error: {Error}\n") +
        "\n--- stdout ---\n" + Stdout +
        "\n--- stderr ---\n" + Stderr;
}

/// <summary>Keeps the last <see cref="Capacity"/> script runs, newest first, persisted to runlog.json.</summary>
public sealed class RunLog
{
    public const int Capacity = 50;
    /// <summary>Per-stream characters kept in the log file.</summary>
    public const int MaxStoredChars = 64 * 1024;

    private readonly string? _path;
    private readonly List<RunLogEntry> _entries = new();
    private readonly object _gate = new();

    public RunLog(string? path = null)
    {
        _path = path;
        Load();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<RunLogEntry> Entries
    {
        get { lock (_gate) return _entries.ToList(); }
    }

    public void Add(RunLogEntry entry)
    {
        entry.Stdout = Cap(entry.Stdout);
        entry.Stderr = Cap(entry.Stderr);
        lock (_gate)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        }
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Cap(string? s)
    {
        s ??= "";
        return s.Length <= MaxStoredChars ? s : s[..MaxStoredChars] + "\n[truncated]";
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<RunLogEntry>>(File.ReadAllText(_path), ConfigSerializer.Options);
            if (list is null) return;
            lock (_gate)
            {
                _entries.Clear();
                _entries.AddRange(list.Take(Capacity));
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            List<RunLogEntry> copy;
            lock (_gate) copy = _entries.ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            ConfigStore.WriteAtomic(_path, JsonSerializer.Serialize(copy, ConfigSerializer.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
