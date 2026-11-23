using System.Collections.ObjectModel;
using System.IO;
using Island.Core.Config;
using Island.Core.Models;

namespace Island.Tests;

public class ConfigSerializationTests
{
    [Fact]
    public void DefaultConfig_HasExpectedButtons()
    {
        var cfg = DefaultConfig.Create();
        Assert.Equal(new[] { "Fav apps", "Claude Code here", "Screenshot", "Browser", "Settings" },
            cfg.Buttons.Select(b => b.Label));
        var fav = cfg.Buttons[0];
        Assert.IsType<GroupAction>(fav.Action);
        Assert.Equal(new[] { "File Explorer", "Notepad", "Calculator" }, fav.Children!.Select(c => c.Label));
        Assert.IsType<ClaudeCodeHereAction>(cfg.Buttons[1].Action);
        Assert.Equal(ScreenshotMode.Snip, Assert.IsType<ScreenshotAction>(cfg.Buttons[2].Action).Mode);
        Assert.Null(Assert.IsType<OpenUrlAction>(cfg.Buttons[3].Action).Url);
        Assert.IsType<OpenSettingsAction>(cfg.Buttons[4].Action);
    }

    [Fact]
    public void RoundTrip_PreservesEveryActionType()
    {
        var cfg = new IslandConfig();
        cfg.Buttons.Add(ButtonItem.Create("a", IconSpec.Glyph("E710"), new LaunchAppAction { Path = @"C:\x.exe", Arguments = "-v", WorkingDirectory = @"C:\", RunAsAdmin = true }));
        cfg.Buttons.Add(ButtonItem.Create("b", IconSpec.Emoji("🚀"), new OpenFolderAction { Path = @"C:\dir" }));
        cfg.Buttons.Add(ButtonItem.Create("c", IconSpec.Image(@"C:\i.png"), new OpenFileAction { Path = @"C:\f.txt" }));
        cfg.Buttons.Add(ButtonItem.Create("d", IconSpec.App(@"C:\x.exe"), new OpenUrlAction { Url = "https://example.com" }));
        cfg.Buttons.Add(ButtonItem.Create("e", IconSpec.Glyph("E8F1"), new GroupAction(),
            ButtonItem.Create("e1", IconSpec.Glyph("E710"), new ClaudeCodeHereAction { Command = "claude --continue", FallbackFolder = @"C:\src" })));
        cfg.Buttons.Add(ButtonItem.Create("f", IconSpec.Glyph("E722"), new ScreenshotAction { Mode = ScreenshotMode.ActiveWindow, SaveFolder = @"C:\shots", CopyToClipboard = false }));
        cfg.Buttons.Add(ButtonItem.Create("g", IconSpec.Glyph("E713"), new OpenSettingsAction()));
        cfg.Buttons.Add(ButtonItem.Create("h", IconSpec.Glyph("E943"), new RunScriptAction
        {
            Source = ScriptSource.Inline, InlineScript = "Write-Output hi", InlineExtension = ".ps1",
            Arguments = "{currentFolder}", TimeoutSeconds = 5, Output = OutputHandling.JsonProtocol,
            Interpreter = InterpreterMode.Custom, CustomInterpreterPath = "x.exe", ShowConsole = true,
        }));
        cfg.Buttons[0].Icon.TintColor = "#FF0000";
        cfg.Buttons[0].Icon.Background = "#202020";
        cfg.General.Layout = ExpandLayout.Grid;
        cfg.General.Theme = ThemeMode.Dark;

        var json = ConfigSerializer.Serialize(cfg);
        var back = ConfigSerializer.Deserialize(json);

        Assert.Equal(ConfigSerializer.Serialize(back), json);
        var la = Assert.IsType<LaunchAppAction>(back.Buttons[0].Action);
        Assert.True(la.RunAsAdmin);
        Assert.Equal("-v", la.Arguments);
        Assert.Equal("#FF0000", back.Buttons[0].Icon.TintColor);
        Assert.Equal(IconKind.Emoji, back.Buttons[1].Icon.Kind);
        var cc = Assert.IsType<ClaudeCodeHereAction>(back.Buttons[4].Children![0].Action);
        Assert.Equal("claude --continue", cc.Command);
        var rs = Assert.IsType<RunScriptAction>(back.Buttons[7].Action);
        Assert.Equal(OutputHandling.JsonProtocol, rs.Output);
        Assert.Equal(InterpreterMode.Custom, rs.Interpreter);
        Assert.Equal(ExpandLayout.Grid, back.General.Layout);
        Assert.Equal(cfg.Buttons[3].Id, back.Buttons[3].Id);
    }

    [Fact]
    public void Serialize_UsesTypeDiscriminatorAndStringEnums_AndSkipsUiState()
    {
        var cfg = DefaultConfig.Create();
        cfg.Buttons[0].IsSelected = true;
        cfg.Buttons[0].Warning = "x";
        var json = ConfigSerializer.Serialize(cfg);
        Assert.Contains("\"type\": \"Group\"", json);
        Assert.Contains("\"type\": \"ClaudeCodeHere\"", json);
        Assert.Contains("\"mode\": \"Snip\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.DoesNotContain("isSelected", json);
        Assert.DoesNotContain("warning", json);
        Assert.DoesNotContain("displayTooltip", json);
        Assert.DoesNotContain("isGroup", json);
    }

    [Fact]
    public void Deserialize_AcceptsHandEditedJson_CommentsTrailingCommasUnknownFieldsOutOfOrderType()
    {
        const string json = """
        {
          // hand edited
          "schemaVersion": 1,
          "somethingNew": 42,
          "buttons": [
            { "label": "Docs", "action": { "url": "https://learn.microsoft.com", "type": "OpenUrl", "extra": true }, },
            { "label": "Grp", "action": { "type": "Group" } },
          ],
        }
        """;
        var cfg = ConfigSerializer.Deserialize(json);
        Assert.Equal("https://learn.microsoft.com", Assert.IsType<OpenUrlAction>(cfg.Buttons[0].Action).Url);
        Assert.NotEqual(Guid.Empty, cfg.Buttons[0].Id);
        Assert.NotNull(cfg.Buttons[1].Children);          // normalized
        Assert.NotNull(cfg.General);                       // defaulted
        Assert.Equal("claude", cfg.General.ClaudeCommand);
    }

    [Fact]
    public void Deserialize_MissingSchemaVersion_IsMigrated()
    {
        var cfg = ConfigSerializer.Deserialize("""{ "buttons": [] }""");
        Assert.Equal(IslandConfig.CurrentSchemaVersion, cfg.SchemaVersion);
    }

    [Fact]
    public void Deserialize_FutureSchemaVersion_Throws()
    {
        Assert.Throws<ConfigException>(() => ConfigSerializer.Deserialize("""{ "schemaVersion": 999 }"""));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{ "buttons": [ { "action": { "type": "Teleport" } } ] }""")]
    [InlineData("""{ "buttons": "nope" }""")]
    public void Deserialize_Invalid_ThrowsConfigException(string json)
    {
        Assert.Throws<ConfigException>(() => ConfigSerializer.Deserialize(json));
    }

    [Fact]
    public void Normalize_FixesDuplicateIdsAndClampsValues()
    {
        var id = Guid.NewGuid();
        var json = $$"""
        { "general": { "islandSize": 5, "opacity": 7, "snapMargin": -3 },
          "buttons": [ { "id": "{{id}}", "label": "a", "action": { "type": "OpenSettings" } },
                       { "id": "{{id}}", "label": "b", "action": { "type": "OpenSettings" } } ] }
        """;
        var cfg = ConfigSerializer.Deserialize(json);
        Assert.NotEqual(cfg.Buttons[0].Id, cfg.Buttons[1].Id);
        Assert.Equal(24, cfg.General.IslandSize);
        Assert.Equal(1.0, cfg.General.Opacity);
        Assert.Equal(0, cfg.General.SnapMargin);
    }

    [Fact]
    public void CloneButton_WithNewIds_ChangesAllIds()
    {
        var group = DefaultConfig.Create().Buttons[0];
        var copy = ConfigSerializer.CloneButton(group, newIds: true);
        var originalIds = ConfigSerializer.Flatten(new[] { group }).Select(b => b.Id).ToHashSet();
        Assert.All(ConfigSerializer.Flatten(new[] { copy }), b => Assert.DoesNotContain(b.Id, originalIds));
        Assert.Equal(group.Children!.Count, copy.Children!.Count);
    }
}

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "island-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void FirstRun_WritesDefaults()
    {
        var store = new ConfigStore(_dir);
        var r = store.Load();
        Assert.Equal(ConfigLoadSource.FirstRun, r.Source);
        Assert.True(File.Exists(store.ConfigPath));
        Assert.Equal(5, r.Config.Buttons.Count);
    }

    [Fact]
    public void Save_KeepsPreviousVersionAsBackup()
    {
        var store = new ConfigStore(_dir);
        var cfg = store.Load().Config;
        cfg.Buttons[0].Label = "first";
        store.Save(cfg);
        cfg.Buttons[0].Label = "second";
        store.Save(cfg);

        Assert.Equal("second", store.Load().Config.Buttons[0].Label);
        Assert.Equal("first", ConfigSerializer.Deserialize(File.ReadAllText(store.BackupPath)).Buttons[0].Label);
    }

    [Fact]
    public void BrokenConfig_LoadsBackup_AndPreservesBrokenFile()
    {
        var store = new ConfigStore(_dir);
        var cfg = store.Load().Config;
        cfg.Buttons[0].Label = "good";
        store.Save(cfg);
        store.Save(cfg); // backup now == "good"
        File.WriteAllText(store.ConfigPath, "{ broken");

        var r = store.Load();
        Assert.Equal(ConfigLoadSource.Backup, r.Source);
        Assert.NotNull(r.Error);
        Assert.Equal("good", r.Config.Buttons[0].Label);
        Assert.NotNull(r.PreservedBrokenFile);
        Assert.Equal("{ broken", File.ReadAllText(r.PreservedBrokenFile!));
        // The broken primary itself is untouched until the user saves.
        Assert.Equal("{ broken", File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void BrokenConfig_NoBackup_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        var store = new ConfigStore(_dir);
        File.WriteAllText(store.ConfigPath, "nope");
        var r = store.Load();
        Assert.Equal(ConfigLoadSource.Defaults, r.Source);
        Assert.Equal(5, r.Config.Buttons.Count);
    }

    [Fact]
    public void Save_DoesNotOverwriteGoodBackupWithBrokenPrimary()
    {
        var store = new ConfigStore(_dir);
        var cfg = store.Load().Config;
        cfg.Buttons[0].Label = "good";
        store.Save(cfg);
        store.Save(cfg);
        File.WriteAllText(store.ConfigPath, "{ broken");
        cfg.Buttons[0].Label = "new";
        store.Save(cfg);
        Assert.Equal("good", ConfigSerializer.Deserialize(File.ReadAllText(store.BackupPath)).Buttons[0].Label);
    }

    [Fact]
    public void State_RoundTrips()
    {
        var store = new ConfigStore(_dir);
        store.SaveState(new IslandState { Monitor = @"\\.\DISPLAY2", Corner = Island.Core.Layout.Corner.TopLeft, Hidden = true });
        var s = store.LoadState();
        Assert.Equal(@"\\.\DISPLAY2", s.Monitor);
        Assert.Equal(Island.Core.Layout.Corner.TopLeft, s.Corner);
        Assert.True(s.Hidden);
    }
}
