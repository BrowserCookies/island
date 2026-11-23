using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Island.Core.Models;
using Island.Core.Scripts;

namespace Island.Tests;

public class PlaceholderTests
{
    private static PlaceholderExpander Create(Func<string>? clip = null) =>
        PlaceholderExpander.CreateStandard(() => @"C:\work", clip ?? (() => "clip text"), @"C:\Users\me\AppData\Roaming\Island",
            () => new DateTime(2026, 3, 4, 5, 6, 7));

    [Fact]
    public void ExpandsAllKnownPlaceholders()
    {
        var e = Create();
        Assert.Equal(@"C:\work|clip text|2026-03-04|05-06-07|C:\Users\me\AppData\Roaming\Island",
            e.Expand("{currentFolder}|{clipboard}|{date}|{time}|{islandDir}"));
    }

    [Fact]
    public void IsCaseInsensitive_AndLeavesUnknownAlone()
    {
        var e = Create();
        Assert.Equal(@"C:\work {nope} {", e.Expand("{CurrentFolder} {nope} {"));
    }

    [Fact]
    public void DoubleBracesEscape()
    {
        Assert.Equal("{date} 2026-03-04", Create().Expand("{{date}} {date}"));
    }

    [Fact]
    public void ProvidersAreLazyAndCached()
    {
        int calls = 0;
        var e = Create(() => { calls++; return "x"; });
        Assert.Equal("no clipboard here", e.Expand("no clipboard here"));
        Assert.Equal(0, calls);
        Assert.Equal("x x", e.Expand("{clipboard} {clipboard}"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ThrowingProvider_ExpandsToEmpty()
    {
        var e = Create(() => throw new InvalidOperationException());
        Assert.Equal("[]", e.Expand("[{clipboard}]"));
    }

    [Fact]
    public void NullOrEmpty()
    {
        Assert.Equal("", Create().Expand(null));
        Assert.False(PlaceholderExpander.ContainsPlaceholder("C:\\plain"));
        Assert.True(PlaceholderExpander.ContainsPlaceholder("{currentFolder}\\x"));
    }
}

public class ScriptOutputParserTests
{
    [Fact]
    public void ParsesLastNonEmptyLineAsJson()
    {
        var stdout = "log line 1\nlog line 2\n{\"notify\":\"Done\",\"clipboard\":\"abc\",\"openUrl\":\"https://x.y\",\"openPath\":\"C:\\\\tmp\",\"popup\":\"long\",\"error\":\"bad\",\"unknown\":1}\n\n  \r\n";
        var r = ScriptOutputParser.ParseJsonProtocol(stdout);
        Assert.NotNull(r);
        Assert.Equal("Done", r!.Notify);
        Assert.Equal("abc", r.Clipboard);
        Assert.Equal("https://x.y", r.OpenUrl);
        Assert.Equal(@"C:\tmp", r.OpenPath);
        Assert.Equal("long", r.Popup);
        Assert.Equal("bad", r.Error);
    }

    [Fact]
    public void PartialObject_OnlySetsGivenFields()
    {
        var r = ScriptOutputParser.ParseJsonProtocol("{\"notify\": \"hi\"}");
        Assert.Equal("hi", r!.Notify);
        Assert.Null(r.Clipboard);
        Assert.False(r.IsEmpty);
    }

    [Fact]
    public void NonStringValues_AreRenderedAsJsonText()
    {
        var r = ScriptOutputParser.ParseJsonProtocol("{\"notify\": 42, \"clipboard\": null}");
        Assert.Equal("42", r!.Notify);
        Assert.Null(r.Clipboard);
    }

    [Theory]
    [InlineData("")]
    [InlineData("just text")]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"notify\":\"x\"}\ntrailing text")]
    public void InvalidJson_ReturnsNull(string stdout)
    {
        Assert.Null(ScriptOutputParser.ParseJsonProtocol(stdout));
    }

    [Fact]
    public void LineHelpers()
    {
        Assert.Equal("c", ScriptOutputParser.LastNonEmptyLine("a\r\nb\r\nc\r\n\r\n"));
        Assert.Equal("a", ScriptOutputParser.FirstNonEmptyLine("\n\n a \nb"));
        Assert.Null(ScriptOutputParser.LastNonEmptyLine("  \n \n"));
        Assert.Equal("abc", ScriptOutputParser.LastNonEmptyLine("\uFEFFabc"));
    }
}

public class InterpreterResolverTests
{
    private static readonly GeneralSettings Settings = new();

    [Fact]
    public void Ps1_UsesWindowsPowerShellByDefault()
    {
        var r = InterpreterResolver.Resolve(@"C:\s\a b.ps1", "x y", new RunScriptAction(), Settings, pwshAvailable: true);
        Assert.Equal("powershell.exe", r.FileName);
        Assert.Equal("-NoProfile -ExecutionPolicy Bypass -File \"C:\\s\\a b.ps1\" x y", r.Arguments);
    }

    [Fact]
    public void Ps1_PrefersPwshWhenEnabledAndInstalled()
    {
        var s = new GeneralSettings { PowerShell = PowerShellKind.Pwsh };
        Assert.Equal("pwsh.exe", InterpreterResolver.Resolve(@"C:\a.ps1", "", new RunScriptAction(), s, true).FileName);
        Assert.Equal("powershell.exe", InterpreterResolver.Resolve(@"C:\a.ps1", "", new RunScriptAction(), s, false).FileName);
    }

    [Fact]
    public void Python_SupportsCommandWithPrefixArgs()
    {
        var s = new GeneralSettings { PythonCommand = "py -3" };
        var r = InterpreterResolver.Resolve(@"C:\a.py", "--flag", new RunScriptAction(), s, false);
        Assert.Equal("py", r.FileName);
        Assert.Equal("-3 \"C:\\a.py\" --flag", r.Arguments);
    }

    [Theory]
    [InlineData(".js")]
    [InlineData(".mjs")]
    public void Node(string ext)
    {
        var r = InterpreterResolver.Resolve(@"C:\a" + ext, "", new RunScriptAction(), Settings, false);
        Assert.Equal("node", r.FileName);
        Assert.Equal($"\"C:\\a{ext}\"", r.Arguments);
    }

    [Fact]
    public void Batch_UsesCmdWithOuterQuotes()
    {
        var r = InterpreterResolver.Resolve(@"C:\my dir\a.cmd", "\"one two\"", new RunScriptAction(), Settings, false);
        Assert.Equal("cmd.exe", r.FileName);
        Assert.Equal("/d /s /c \"\"C:\\my dir\\a.cmd\" \"one two\"\"", r.Arguments);
    }

    [Fact]
    public void Exe_RunsDirectly()
    {
        var r = InterpreterResolver.Resolve(@"C:\tool.exe", "-a", new RunScriptAction(), Settings, false);
        Assert.Equal(@"C:\tool.exe", r.FileName);
        Assert.Equal("-a", r.Arguments);
    }

    [Fact]
    public void Custom_SubstitutesTemplate()
    {
        var a = new RunScriptAction { Interpreter = InterpreterMode.Custom, CustomInterpreterPath = "\"C:\\ruby\\ruby.exe\"", CustomArgumentTemplate = "-w \"{script}\" -- {args}" };
        var r = InterpreterResolver.Resolve(@"C:\a.rb", "1 2", a, Settings, false);
        Assert.Equal(@"C:\ruby\ruby.exe", r.FileName);
        Assert.Equal("-w \"C:\\a.rb\" -- 1 2", r.Arguments);
    }

    [Fact]
    public void UnknownExtension_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => InterpreterResolver.Resolve(@"C:\a.rb", "", new RunScriptAction(), Settings, false));
        Assert.False(InterpreterResolver.IsKnownExtension(".rb"));
        Assert.True(InterpreterResolver.IsKnownExtension(".PS1"));
    }
}

public class CommandLineTests
{
    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("has space", "\"has space\"")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\my dir\", "\"C:\\my dir\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("", "\"\"")]
    public void Quote(string input, string expected) => Assert.Equal(expected, CommandLine.Quote(input));

    [Fact]
    public void Split_HandlesQuotes()
    {
        Assert.Equal(new[] { @"C:\Program Files\x.exe", "--a", "b c", "" },
            CommandLine.Split("\"C:\\Program Files\\x.exe\" --a \"b c\" \"\""));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" --single-argument %1", @"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"C:\Program Files\Mozilla Firefox\firefox.exe -osint -url ""%1""", @"C:\Program Files\Mozilla Firefox\firefox.exe")]
    [InlineData("", null)]
    public void ExtractExecutable(string cmd, string? expected) => Assert.Equal(expected, CommandLine.ExtractExecutable(cmd));
}

public class ProcessRunnerTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static ProcessSpec Shell(string script) => IsWindows
        ? new ProcessSpec("cmd.exe", "/d /c " + script)
        : new ProcessSpec("/bin/sh", "-c " + CommandLine.Quote(script));

    [Fact]
    public async Task CapturesStdoutStderrAndExitCode()
    {
        var r = await ProcessRunner.RunAsync(Shell(IsWindows ? "echo out& echo err 1>&2& exit /b 3" : "echo out; echo err 1>&2; exit 3"),
            TimeSpan.FromSeconds(20), CancellationToken.None);
        Assert.Equal(ProcessOutcome.Completed, r.Outcome);
        Assert.Equal(3, r.ExitCode);
        Assert.Equal("out", ScriptOutputParser.LastNonEmptyLine(r.Stdout));
        Assert.Equal("err", ScriptOutputParser.FirstNonEmptyLine(r.Stderr)?.Trim());
        Assert.False(r.Succeeded);
    }

    [Fact]
    public async Task Timeout_KillsProcess()
    {
        var r = await ProcessRunner.RunAsync(Shell(IsWindows ? "ping -n 30 127.0.0.1 >nul" : "sleep 30"),
            TimeSpan.FromMilliseconds(500), CancellationToken.None);
        Assert.Equal(ProcessOutcome.TimedOut, r.Outcome);
        Assert.True(r.Duration < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Cancel_KillsProcess()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var r = await ProcessRunner.RunAsync(Shell(IsWindows ? "ping -n 30 127.0.0.1 >nul" : "sleep 30"), null, cts.Token);
        Assert.Equal(ProcessOutcome.Cancelled, r.Outcome);
    }

    [Fact]
    public async Task MissingExecutable_FailsToStart()
    {
        var r = await ProcessRunner.RunAsync(new ProcessSpec("definitely-not-a-real-binary-xyz", ""), null, CancellationToken.None);
        Assert.Equal(ProcessOutcome.FailedToStart, r.Outcome);
        Assert.NotNull(r.StartError);
    }
}

public class RunLogTests
{
    [Fact]
    public void KeepsLast50_NewestFirst_AndPersists()
    {
        var path = Path.Combine(Path.GetTempPath(), "island-runlog-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var log = new RunLog(path);
            for (int i = 0; i < 60; i++)
                log.Add(new RunLogEntry { ButtonLabel = "b" + i, Time = DateTime.Now, Outcome = ProcessOutcome.Completed });
            Assert.Equal(RunLog.Capacity, log.Entries.Count);
            Assert.Equal("b59", log.Entries[0].ButtonLabel);
            Assert.Equal("b10", log.Entries[^1].ButtonLabel);

            var reloaded = new RunLog(path);
            Assert.Equal(50, reloaded.Entries.Count);
            Assert.Equal("b59", reloaded.Entries[0].ButtonLabel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Summary_Text()
    {
        Assert.Equal("OK", new RunLogEntry { Outcome = ProcessOutcome.Completed }.Summary);
        Assert.Equal("Exit 2", new RunLogEntry { Outcome = ProcessOutcome.Completed, ExitCode = 2 }.Summary);
        Assert.Equal("Timed out", new RunLogEntry { Outcome = ProcessOutcome.TimedOut }.Summary);
        Assert.Equal("1.5 s", new RunLogEntry { DurationMs = 1500 }.DurationText);
    }

    [Fact]
    public void TruncatesHugeOutput()
    {
        var log = new RunLog();
        log.Add(new RunLogEntry { Stdout = new string('x', RunLog.MaxStoredChars * 2) });
        Assert.True(log.Entries[0].Stdout.Length < RunLog.MaxStoredChars + 100);
    }
}
