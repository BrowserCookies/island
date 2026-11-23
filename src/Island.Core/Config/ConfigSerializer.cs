using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Island.Core.Models;

namespace Island.Core.Config;

public static class ConfigSerializer
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            AllowOutOfOrderMetadataProperties = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public static string Serialize(IslandConfig config) => JsonSerializer.Serialize(config, Options);

    /// <summary>Parses, migrates and normalizes a config. Throws <see cref="ConfigException"/> on failure.</summary>
    public static IslandConfig Deserialize(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            throw new ConfigException($"config.json is not valid JSON: {ex.Message}", ex);
        }

        if (root is not JsonObject obj)
            throw new ConfigException("config.json must contain a JSON object.");

        int version = obj.TryGetPropertyValue("schemaVersion", out var v) && v is JsonValue jv && jv.TryGetValue<int>(out var n) ? n : 0;
        if (version > IslandConfig.CurrentSchemaVersion)
            throw new ConfigException(
                $"config.json has schemaVersion {version}, but this version of Island only understands up to {IslandConfig.CurrentSchemaVersion}.");

        ConfigMigrations.Migrate(obj, version);

        IslandConfig? config;
        try
        {
            config = obj.Deserialize<IslandConfig>(Options);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new ConfigException($"config.json could not be read: {ex.Message}", ex);
        }

        if (config is null) throw new ConfigException("config.json is empty.");
        Normalize(config);
        return config;
    }

    /// <summary>Deep copy via JSON round trip (UI state is not copied).</summary>
    public static IslandConfig Clone(IslandConfig config) => Deserialize(Serialize(config));

    public static ButtonItem CloneButton(ButtonItem item, bool newIds)
    {
        var json = JsonSerializer.Serialize(item, Options);
        var copy = JsonSerializer.Deserialize<ButtonItem>(json, Options)!;
        NormalizeButton(copy, new HashSet<Guid>());
        if (newIds) AssignNewIds(copy);
        return copy;
    }

    public static void AssignNewIds(ButtonItem item)
    {
        item.Id = Guid.NewGuid();
        if (item.Children is null) return;
        foreach (var c in item.Children) AssignNewIds(c);
    }

    /// <summary>Fixes up things hand edits can break: null collections, duplicate/empty ids, missing icons.</summary>
    public static void Normalize(IslandConfig config)
    {
        config.SchemaVersion = IslandConfig.CurrentSchemaVersion;
        config.General ??= new GeneralSettings();
        config.Buttons ??= new ObservableCollection<ButtonItem>();
        var seen = new HashSet<Guid>();
        // Drop null entries (e.g. "buttons": [null]).
        foreach (var nullItem in config.Buttons.Where(b => b is null).ToList()) config.Buttons.Remove(nullItem);
        foreach (var b in config.Buttons) NormalizeButton(b, seen);

        var g = config.General;
        g.IslandSize = Math.Clamp(g.IslandSize, 24, 160);
        g.SnapMargin = Math.Clamp(g.SnapMargin, 0, 400);
        g.Opacity = double.IsNaN(g.Opacity) ? 1 : Math.Clamp(g.Opacity, 0.2, 1.0);
        g.GridColumns = Math.Clamp(g.GridColumns, 1, 12);
        g.ClaudeCommand = string.IsNullOrWhiteSpace(g.ClaudeCommand) ? "claude" : g.ClaudeCommand;
        g.PythonCommand = string.IsNullOrWhiteSpace(g.PythonCommand) ? "python" : g.PythonCommand;
        g.NodeCommand = string.IsNullOrWhiteSpace(g.NodeCommand) ? "node" : g.NodeCommand;
        g.FallbackFolder ??= "%USERPROFILE%";
        g.ScreenshotFolder ??= @"%USERPROFILE%\Pictures\Screenshots";
    }

    private static void NormalizeButton(ButtonItem b, HashSet<Guid> seen)
    {
        if (b.Id == Guid.Empty || !seen.Add(b.Id))
        {
            b.Id = Guid.NewGuid();
            seen.Add(b.Id);
        }
        b.Label ??= "";
        b.Icon ??= new IconSpec();
        b.Icon.Value ??= "";
        b.Action ??= new OpenSettingsAction();
        if (b.Action is GroupAction)
            b.Children ??= new ObservableCollection<ButtonItem>();
        if (b.Action is RunScriptAction rs)
        {
            rs.TimeoutSeconds = Math.Max(0, rs.TimeoutSeconds);
            rs.InlineExtension = NormalizeExtension(rs.InlineExtension);
            rs.CustomArgumentTemplate ??= "\"{script}\" {args}";
        }
        if (b.Children is null) return;
        foreach (var nullItem in b.Children.Where(c => c is null).ToList()) b.Children.Remove(nullItem);
        foreach (var c in b.Children) NormalizeButton(c, seen);
    }

    public static string NormalizeExtension(string? ext)
    {
        ext = (ext ?? "").Trim();
        if (ext.Length == 0) return ".ps1";
        return ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
    }

    /// <summary>All buttons, depth first, in island order.</summary>
    public static IEnumerable<ButtonItem> Flatten(IEnumerable<ButtonItem> items)
    {
        foreach (var i in items)
        {
            yield return i;
            if (i.Children is null) continue;
            foreach (var c in Flatten(i.Children)) yield return c;
        }
    }
}

public sealed class ConfigException : Exception
{
    public ConfigException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Schema migrations operate on raw JSON so old shapes can be upgraded before binding.
/// Add a case for each new schema version.
/// </summary>
public static class ConfigMigrations
{
    public static void Migrate(JsonObject obj, int fromVersion)
    {
        // v0 (no schemaVersion) -> v1: nothing structural changed, just stamp the version.
        if (fromVersion < 1)
            obj["schemaVersion"] = 1;
    }
}
