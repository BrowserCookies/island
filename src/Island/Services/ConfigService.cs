using System.Windows.Threading;
using Island.Core.Config;
using Island.Core.Models;

namespace Island.Services;

/// <summary>
/// Owns the live config: loading (with backup fallback), saving, and live reload when
/// config.json is edited by hand (debounced FileSystemWatcher).
/// </summary>
public sealed class ConfigService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;

    public ConfigService(string dataDirectory, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Store = new ConfigStore(dataDirectory);
        Directory.CreateDirectory(Store.ScriptsDirectory);
        Directory.CreateDirectory(Store.IconsDirectory);
    }

    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Island");

    public ConfigStore Store { get; }
    public string DataDirectory => Store.DataDirectory;
    public IslandConfig Current { get; private set; } = DefaultConfig.Create();

    /// <summary>Raised on the UI thread after a save or a live reload.</summary>
    public event EventHandler? ConfigChanged;

    /// <summary>Raised on the UI thread when a hand-edited config can't be parsed (the old config stays active).</summary>
    public event EventHandler<string>? ReloadFailed;

    public ConfigLoadResult Load()
    {
        var r = Store.Load();
        Current = r.Config;
        return r;
    }

    public void Save(IslandConfig config)
    {
        ScriptFiles.WriteInlineScripts(config, Store.ScriptsDirectory);
        Store.Save(config);
        Current = config;
        ConfigChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartWatching()
    {
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => ReloadFromDisk(), _dispatcher);
        _debounce.Stop();

        _watcher = new FileSystemWatcher(Store.DataDirectory, "config.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        FileSystemEventHandler onChange = (_, _) => _dispatcher.BeginInvoke(() =>
        {
            _debounce!.Stop();
            _debounce.Start();
        });
        _watcher.Changed += onChange;
        _watcher.Created += onChange;
        _watcher.Renamed += (_, e) => onChange(this, e);
        _watcher.EnableRaisingEvents = true;
    }

    private void ReloadFromDisk()
    {
        _debounce?.Stop();
        string text;
        try
        {
            if (!File.Exists(Store.ConfigPath)) return;
            text = ReadShared(Store.ConfigPath);
        }
        catch (IOException)
        {
            // Editor still holds the file; try again shortly.
            _debounce?.Start();
            return;
        }

        if (text == Store.LastWrittenText) return; // our own write

        try
        {
            var cfg = ConfigSerializer.Deserialize(text);
            Current = cfg;
            ConfigChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (ConfigException ex)
        {
            ReloadFailed?.Invoke(this, ex.Message);
        }
    }

    private static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Stop();
    }
}

public static class ScriptFiles
{
    /// <summary>Path of an inline script's file: %APPDATA%\Island\scripts\&lt;buttonId&gt;&lt;ext&gt;.</summary>
    public static string InlinePath(string scriptsDir, Guid buttonId, string ext) =>
        Path.Combine(scriptsDir, buttonId.ToString("N") + ConfigSerializer.NormalizeExtension(ext));

    public static void WriteInlineScripts(IslandConfig config, string scriptsDir)
    {
        foreach (var b in ConfigSerializer.Flatten(config.Buttons))
        {
            if (b.Action is RunScriptAction { Source: ScriptSource.Inline } rs)
            {
                try { EnsureInlineFile(b.Id, rs, scriptsDir); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Writes the inline script if the file is missing or differs. Returns its path.</summary>
    public static string EnsureInlineFile(Guid buttonId, RunScriptAction rs, string scriptsDir)
    {
        Directory.CreateDirectory(scriptsDir);
        var path = InlinePath(scriptsDir, buttonId, rs.InlineExtension);
        var text = rs.InlineScript ?? "";
        if (!File.Exists(path) || File.ReadAllText(path) != text)
        {
            // PowerShell 5 needs a BOM to read non-ASCII UTF-8 scripts correctly.
            var bom = ConfigSerializer.NormalizeExtension(rs.InlineExtension) == ".ps1";
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(bom));
        }
        return path;
    }
}
