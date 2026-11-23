using System.Collections.ObjectModel;
using Island.Core.Config;
using Island.Core.Models;
using Island.Core.Util;

namespace Island.Tests;

public class ExplorerPathTests
{
    [Theory]
    [InlineData("file:///C:/Users/me/My%20Docs", @"C:\Users\me\My Docs")]
    [InlineData("file:///C:/", @"C:\")]
    [InlineData("file:///D:", @"D:\")]
    [InlineData("file://server/share/dir", @"\\server\share\dir")]
    [InlineData("file://localhost/C:/temp", @"C:\temp")]
    [InlineData("file:///C:/a%23b/%E2%9C%93", @"C:\a#b\✓")]
    public void ConvertsFileUrls(string url, string expected) => Assert.Equal(expected, ExplorerPath.FromLocationUrl(url));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    [InlineData("search-ms:query=foo")]
    [InlineData("https://example.com")]
    public void NonFileLocations_ReturnNull(string? url) => Assert.Null(ExplorerPath.FromLocationUrl(url));

    [Theory]
    [InlineData(@"C:\x", true)]
    [InlineData(@"\\srv\share", true)]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false)]
    [InlineData("shell:Downloads", false)]
    [InlineData("", false)]
    public void IsFileSystemPath(string path, bool expected) => Assert.Equal(expected, ExplorerPath.IsFileSystemPath(path));
}

public class TerminalCommandTests
{
    [Fact]
    public void WindowsTerminal_Basic()
    {
        var r = TerminalCommandBuilder.ForWindowsTerminal("wt.exe", @"C:\My Projects\app", "claude", commandIsBatchShim: false);
        Assert.Equal("wt.exe", r.FileName);
        Assert.Equal("-d \"C:\\My Projects\\app\" claude", r.Arguments);
    }

    [Fact]
    public void WindowsTerminal_EscapesSemicolons_AndWrapsBatchShims()
    {
        var r = TerminalCommandBuilder.ForWindowsTerminal("wt.exe", @"C:\x", "claude --continue; echo", commandIsBatchShim: true);
        Assert.Equal(@"-d C:\x cmd /k claude --continue\; echo", r.Arguments);
    }

    [Fact]
    public void WindowsTerminal_TrailingBackslashFolder_IsQuotedSafely()
    {
        var r = TerminalCommandBuilder.ForWindowsTerminal("wt.exe", @"C:\My Dir\", "claude", false);
        Assert.Equal("-d \"C:\\My Dir\\\\\" claude", r.Arguments);
    }

    [Fact]
    public void PowerShellFallback_EscapesSingleQuotes()
    {
        var r = TerminalCommandBuilder.ForPowerShell("powershell.exe", @"C:\Bob's stuff", "claude");
        Assert.Equal("powershell.exe", r.FileName);
        Assert.Equal("-NoExit -Command \"Set-Location -LiteralPath 'C:\\Bob''s stuff'; claude\"", r.Arguments);
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Space")]
    [InlineData("win + shift + i", HotkeyModifiers.Win | HotkeyModifiers.Shift, "I")]
    [InlineData("F9", HotkeyModifiers.None, "F9")]
    [InlineData("Control+OemTilde", HotkeyModifiers.Ctrl, "OemTilde")]
    public void Parses(string text, HotkeyModifiers mods, string key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(key, g.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ctrl+Alt")]
    [InlineData("A")]
    [InlineData("Ctrl+A+B")]
    public void Rejects(string? text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Fact]
    public void FormatsCanonically_AndAddsShift()
    {
        Assert.True(HotkeyGesture.TryParse("alt+ctrl+space", out var g));
        Assert.Equal("Ctrl+Alt+Space", g.ToString());
        Assert.Equal("Ctrl+Alt+Shift+Space", g.WithShift().ToString());
    }
}

public class ButtonTreeTests
{
    private static (ObservableCollection<ButtonItem> root, ButtonItem a, ButtonItem g, ButtonItem g1, ButtonItem g2, ButtonItem b) Build()
    {
        var g1 = ButtonItem.Create("g1", IconSpec.Glyph("E710"), new OpenSettingsAction());
        var g2 = ButtonItem.Create("g2", IconSpec.Glyph("E710"), new OpenSettingsAction());
        var g = ButtonItem.Create("g", IconSpec.Glyph("E710"), new GroupAction(), g1, g2);
        var a = ButtonItem.Create("a", IconSpec.Glyph("E710"), new OpenSettingsAction());
        var b = ButtonItem.Create("b", IconSpec.Glyph("E710"), new OpenSettingsAction());
        return (new ObservableCollection<ButtonItem> { a, g, b }, a, g, g1, g2, b);
    }

    private static string Shape(IEnumerable<ButtonItem> items) =>
        string.Join(",", items.Select(i => i.Children is null ? i.Label : $"{i.Label}[{Shape(i.Children)}]"));

    [Fact]
    public void MoveUpDown_WithinList()
    {
        var (root, a, _, _, _, b) = Build();
        Assert.True(ButtonTree.MoveDown(root, a));
        Assert.Equal("g[g1,g2],a,b", Shape(root));
        Assert.True(ButtonTree.MoveUp(root, b));
        Assert.Equal("g[g1,g2],b,a", Shape(root));
        Assert.False(ButtonTree.MoveDown(root, a)); // already last at top level
    }

    [Fact]
    public void MoveUp_AtTopOfGroup_LeavesGroup()
    {
        var (root, _, _, g1, _, _) = Build();
        Assert.True(ButtonTree.MoveUp(root, g1));
        Assert.Equal("a,g1,g[g2],b", Shape(root));
    }

    [Fact]
    public void MoveDown_AtBottomOfGroup_LeavesGroup()
    {
        var (root, _, _, _, g2, _) = Build();
        Assert.True(ButtonTree.MoveDown(root, g2));
        Assert.Equal("a,g[g1],g2,b", Shape(root));
    }

    [Fact]
    public void Move_IntoGroup()
    {
        var (root, a, g, _, _, _) = Build();
        Assert.True(ButtonTree.Move(root, a, g.Children!, 1));
        Assert.Equal("g[g1,a,g2],b", Shape(root));
    }

    [Fact]
    public void Move_WithinSameList_AdjustsIndex()
    {
        var (root, a, _, _, _, _) = Build();
        Assert.True(ButtonTree.Move(root, a, root, 3)); // "drop after b"
        Assert.Equal("g[g1,g2],b,a", Shape(root));
    }

    [Fact]
    public void Move_GroupIntoItself_IsRejected()
    {
        var (root, _, g, _, _, _) = Build();
        Assert.False(ButtonTree.Move(root, g, g.Children!, 0));
        Assert.Equal("a,g[g1,g2],b", Shape(root));
    }

    [Fact]
    public void FindParent()
    {
        var (root, a, g, g1, _, _) = Build();
        Assert.Same(g, ButtonTree.FindParentButton(root, g1));
        Assert.Null(ButtonTree.FindParentButton(root, a));
        Assert.Same(g.Children, ButtonTree.FindParentCollection(root, g1));
        Assert.True(ButtonTree.IsSelfOrDescendant(g, g1));
        Assert.False(ButtonTree.IsSelfOrDescendant(g1, g));
    }
}

public class ValidatorTests
{
    private static readonly ButtonValidator V = new(
        fileExists: p => p is @"C:\exists.exe" or @"C:\s.ps1",
        dirExists: p => p is @"C:\dir",
        expand: p => p.Replace("%X%", "C:"));

    private static IReadOnlyList<string> Check(ActionBase a, string label = "x") =>
        V.Validate(ButtonItem.Create(label, IconSpec.Glyph("E710"), a));

    [Fact]
    public void LaunchApp()
    {
        Assert.Empty(Check(new LaunchAppAction { Path = @"%X%\exists.exe" }));
        Assert.Empty(Check(new LaunchAppAction { Path = "notepad" })); // resolved via PATH
        Assert.Contains(Check(new LaunchAppAction { Path = @"C:\missing.exe" }), m => m.Contains("not found"));
        Assert.Contains(Check(new LaunchAppAction { Path = "" }), m => m.Contains("required"));
    }

    [Fact]
    public void Folder_File_Url()
    {
        Assert.Empty(Check(new OpenFolderAction { Path = @"C:\dir" }));
        Assert.NotEmpty(Check(new OpenFolderAction { Path = @"C:\nope" }));
        Assert.NotEmpty(Check(new OpenFileAction { Path = "" }));
        Assert.Empty(Check(new OpenUrlAction()));
        Assert.Empty(Check(new OpenUrlAction { Url = "https://example.com/x" }));
        Assert.Empty(Check(new OpenUrlAction { Url = "ms-settings:display" }));
        Assert.Contains(Check(new OpenUrlAction { Url = "example.com" }), m => m.Contains("Invalid URL"));
    }

    [Fact]
    public void Label_Group_Script()
    {
        Assert.Contains(Check(new OpenSettingsAction(), label: " "), m => m.Contains("Label"));
        Assert.Contains(V.Validate(new ButtonItem { Label = "g", Action = new GroupAction() }), m => m.Contains("no buttons"));
        Assert.Empty(Check(new RunScriptAction { Source = ScriptSource.File, ScriptPath = @"C:\s.ps1" }));
        Assert.NotEmpty(Check(new RunScriptAction { Source = ScriptSource.Inline, InlineScript = "" }));
        Assert.Contains(Check(new RunScriptAction { Source = ScriptSource.Inline, InlineScript = "x", InlineExtension = ".rb" }), m => m.Contains("custom interpreter"));
        Assert.Contains(Check(new RunScriptAction { InlineScript = "x", Disabled = true }), m => m.Contains("disabled"));
        Assert.Contains(Check(new RunScriptAction { InlineScript = "x", Interpreter = InterpreterMode.Custom }), m => m.Contains("Custom interpreter"));
    }

    [Fact]
    public void PlaceholderWorkingDirs_AreNotChecked()
    {
        Assert.Empty(Check(new LaunchAppAction { Path = "notepad", WorkingDirectory = "{currentFolder}" }));
    }
}

public class DropFactoryTests
{
    [Theory]
    [InlineData(@"C:\Apps\Tool.exe", ActionType.LaunchApp, "Tool", IconKind.AppIcon)]
    [InlineData(@"C:\Users\me\Desktop\Steam.lnk", ActionType.LaunchApp, "Steam", IconKind.AppIcon)]
    [InlineData(@"C:\s\backup.ps1", ActionType.RunScript, "backup", IconKind.Glyph)]
    [InlineData(@"C:\s\x.py", ActionType.RunScript, "x", IconKind.Glyph)]
    [InlineData(@"C:\s\x.js", ActionType.RunScript, "x", IconKind.Glyph)]
    [InlineData(@"C:\s\x.bat", ActionType.RunScript, "x", IconKind.Glyph)]
    [InlineData(@"C:\docs\report.pdf", ActionType.OpenFile, "report", IconKind.AppIcon)]
    public void Files(string path, ActionType type, string label, IconKind icon)
    {
        var b = DropFactory.Create(path, isDirectory: false);
        Assert.Equal(type, b.Action.Type);
        Assert.Equal(label, b.Label);
        Assert.Equal(icon, b.Icon.Kind);
    }

    [Fact]
    public void Folder()
    {
        var b = DropFactory.Create(@"C:\Projects\", isDirectory: true);
        Assert.Equal("Projects", b.Label);
        Assert.Equal(@"C:\Projects\", Assert.IsType<OpenFolderAction>(b.Action).Path);
    }

    [Fact]
    public void UrlFile()
    {
        var b = DropFactory.Create(@"C:\x\GitHub.url", false, _ => "[InternetShortcut]\r\nURL=https://github.com/\r\nIconIndex=0\r\n");
        Assert.Equal("https://github.com/", Assert.IsType<OpenUrlAction>(b.Action).Url);
        Assert.Equal("GitHub", b.Label);
    }

    [Fact]
    public void ScriptDrop_UsesFileSource()
    {
        var a = Assert.IsType<RunScriptAction>(DropFactory.Create(@"C:\s\a.ps1", false).Action);
        Assert.Equal(ScriptSource.File, a.Source);
        Assert.Equal(@"C:\s\a.ps1", a.ScriptPath);
    }
}
