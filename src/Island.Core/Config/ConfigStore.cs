using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Island.Core.Models;

namespace Island.Core.Config;

public enum ConfigLoadSource { Primary, Backup, Defaults, FirstRun }

public sealed record ConfigLoadResult(IslandConfig Config, ConfigLoadSource Source, string? Error, string? PreservedBrokenFile);

/// <summary>
/// Reads/writes config.json in the Island data folder.
/// <list type="bullet">
/// <item>Save: the previous good config.json is copied to config.backup.json, then the new file is written atomically.</item>
/// <item>Load: if config.json is broken it is preserved as config.broken-&lt;timestamp&gt;.json (never silently
/// overwritten) and the backup, or else defaults, are used.</item>
/// </list>
/// </summary>
public sealed class ConfigStore
{
    public ConfigStore(string dataDirectory)
    {
        DataDirectory = dataDirectory;
    }

    public string DataDirectory { get; }
    public string ConfigPath => Path.Combine(DataDirectory, "config.json");
    public string BackupPath => Path.Combine(DataDirectory, "config.backup.json");
    public string StatePath => Path.Combine(DataDirectory, "state.json");
    public string ScriptsDirectory => Path.Combine(DataDirectory, "scripts");
    public string IconsDirectory => Path.Combine(DataDirectory, "icons");
    public string RunLogPath => Path.Combine(DataDirectory, "runlog.json");

    /// <summary>The exact text last written by <see cref="Save"/>; lets the file watcher ignore our own writes.</summary>
    public string? LastWrittenText { get; private set; }

    public ConfigLoadResult Load()
    {
        Directory.CreateDirectory(DataDirectory);

        if (!File.Exists(ConfigPath))
        {
            var fresh = DefaultConfig.Create();
            Save(fresh);
            return new ConfigLoadResult(fresh, ConfigLoadSource.FirstRun, null, null);
        }

        string primaryError;
        try
        {
            var text = File.ReadAllText(ConfigPath);
            var cfg = ConfigSerializer.Deserialize(text);
            LastWrittenText = text;
            return new ConfigLoadResult(cfg, ConfigLoadSource.Primary, null, null);
        }
        catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException)
        {
            primaryError = ex.Message;
        }

        // Preserve the broken file so it's never silently overwritten by the next save.
        string? preserved = null;
        try
        {
            preserved = Path.Combine(DataDirectory, $"config.broken-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(ConfigPath, preserved, overwrite: true);
        }
        catch (Exception)
        {
            preserved = null;
        }

        if (File.Exists(BackupPath))
        {
            try
            {
                var cfg = ConfigSerializer.Deserialize(File.ReadAllText(BackupPath));
                return new ConfigLoadResult(cfg, ConfigLoadSource.Backup, primaryError, preserved);
            }
            catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException)
            {
                primaryError += $" (backup also failed: {ex.Message})";
            }
        }

        return new ConfigLoadResult(DefaultConfig.Create(), ConfigLoadSource.Defaults, primaryError, preserved);
    }

    /// <summary>Parses the current file without side effects (used by the live-reload watcher).</summary>
    public IslandConfig ReadCurrent() => ConfigSerializer.Deserialize(File.ReadAllText(ConfigPath));

    public void Save(IslandConfig config)
    {
        Directory.CreateDirectory(DataDirectory);
        var text = ConfigSerializer.Serialize(config);

        // Back up the current file, but only if it is itself valid: a broken
        // config must not replace a good backup.
        if (File.Exists(ConfigPath))
        {
            try
            {
                var current = File.ReadAllText(ConfigPath);
                ConfigSerializer.Deserialize(current);
                File.WriteAllText(BackupPath, current, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is ConfigException or IOException or UnauthorizedAccessException)
            {
                // keep existing backup
            }
        }

        WriteAtomic(ConfigPath, text);
        LastWrittenText = text;
    }

    public IslandState LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
                return JsonSerializer.Deserialize<IslandState>(File.ReadAllText(StatePath), ConfigSerializer.Options) ?? new IslandState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
        }
        return new IslandState();
    }

    public void SaveState(IslandState state)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            WriteAtomic(StatePath, JsonSerializer.Serialize(state, ConfigSerializer.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
