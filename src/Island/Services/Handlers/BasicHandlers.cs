using Island.Core.Config;
using Island.Core.Models;
using Island.Core.Util;

namespace Island.Services.Handlers;

internal static class PathUtil
{
    public static string Expand(string? path) =>
        Environment.ExpandEnvironmentVariables((path ?? "").Trim().Trim('"'));
}

public sealed class LaunchAppHandler : ActionHandler<LaunchAppAction>
{
    protected override Task ExecuteAsync(LaunchAppAction a, ActionContext ctx, CancellationToken ct)
    {
        var path = PathUtil.Expand(a.Path);
        if (path.Length == 0) throw new ActionException("No app path is set.");
        if (Path.IsPathRooted(path) && !File.Exists(path) && !Directory.Exists(path))
            throw new ActionException($"App not found: {path}");

        var wd = PathUtil.Expand(a.WorkingDirectory);
        if (wd.Length == 0 && Path.IsPathRooted(path)) wd = Path.GetDirectoryName(path) ?? "";
        ShellLauncher.Open(path, a.Arguments, wd.Length > 0 && Directory.Exists(wd) ? wd : null, a.RunAsAdmin);
        return Task.CompletedTask;
    }
}

public sealed class OpenFolderHandler : ActionHandler<OpenFolderAction>
{
    protected override Task ExecuteAsync(OpenFolderAction a, ActionContext ctx, CancellationToken ct)
    {
        var path = PathUtil.Expand(a.Path);
        if (!Directory.Exists(path)) throw new ActionException($"Folder not found: {a.Path}");
        ShellLauncher.OpenFolder(path);
        return Task.CompletedTask;
    }
}

public sealed class OpenFileHandler : ActionHandler<OpenFileAction>
{
    protected override Task ExecuteAsync(OpenFileAction a, ActionContext ctx, CancellationToken ct)
    {
        var path = PathUtil.Expand(a.Path);
        if (!File.Exists(path)) throw new ActionException($"File not found: {a.Path}");
        ShellLauncher.Open(path);
        return Task.CompletedTask;
    }
}

public sealed class OpenUrlHandler : ActionHandler<OpenUrlAction>
{
    protected override Task ExecuteAsync(OpenUrlAction a, ActionContext ctx, CancellationToken ct)
    {
        var url = a.Url?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            // No URL: start the default browser with no arguments, which opens its home page.
            var browser = ShellLauncher.DefaultBrowserPath();
            if (browser is not null) ShellLauncher.Open(browser);
            else ShellLauncher.Open("microsoft-edge:");
            return Task.CompletedTask;
        }

        if (!url.Contains("://", StringComparison.Ordinal) && !url.Contains(':')) url = "https://" + url;
        if (!ButtonValidator.IsValidUrl(url)) throw new ActionException($"Invalid URL: {a.Url}");
        ShellLauncher.Open(url);
        return Task.CompletedTask;
    }
}

/// <summary>Groups are opened by the panel itself; running one directly (e.g. "Test") just explains that.</summary>
public sealed class GroupHandler : ActionHandler<GroupAction>
{
    protected override Task ExecuteAsync(GroupAction a, ActionContext ctx, CancellationToken ct)
    {
        if (ctx.IsTest)
            ctx.Services.Toasts.Info($"\"{ctx.Button.Label}\" opens a sub-panel with {ctx.Button.Children?.Count ?? 0} button(s).");
        return Task.CompletedTask;
    }
}

public sealed class OpenSettingsHandler : ActionHandler<OpenSettingsAction>
{
    protected override Task ExecuteAsync(OpenSettingsAction a, ActionContext ctx, CancellationToken ct)
    {
        ctx.Services.Host.OpenSettings();
        return Task.CompletedTask;
    }
}

public sealed class ClaudeCodeHereHandler : ActionHandler<ClaudeCodeHereAction>
{
    protected override async Task ExecuteAsync(ClaudeCodeHereAction a, ActionContext ctx, CancellationToken ct)
    {
        var fallback = string.IsNullOrWhiteSpace(a.FallbackFolder) ? ctx.Settings.FallbackFolder : a.FallbackFolder;
        var folder = await Task.Run(() => ctx.Services.CurrentFolder.Resolve(fallback), ct);
        var command = string.IsNullOrWhiteSpace(a.Command) ? ctx.Settings.ClaudeCommand : a.Command!;
        Launch(folder, command, ctx.Settings);
    }

    public static void Launch(string folder, string command, GeneralSettings settings)
    {
        var wt = ShellLauncher.FindOnPath("wt.exe");
        Core.Scripts.ResolvedCommand cmd;
        if (wt is not null)
        {
            var first = Core.Scripts.CommandLine.Split(command).FirstOrDefault() ?? "";
            var resolved = first.Length > 0 ? ShellLauncher.FindOnPath(first) : null;
            bool isShim = resolved is not null &&
                          (resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || resolved.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));
            cmd = TerminalCommandBuilder.ForWindowsTerminal(wt, folder, command, isShim);
        }
        else
        {
            var ps = settings.PowerShell == PowerShellKind.Pwsh && ShellLauncher.FindOnPath("pwsh.exe") is { } pwsh ? pwsh : "powershell.exe";
            cmd = TerminalCommandBuilder.ForPowerShell(ps, folder, command);
        }
        ShellLauncher.Open(cmd.FileName, cmd.Arguments, folder);
    }
}

public sealed class ScreenshotHandler : ActionHandler<ScreenshotAction>
{
    protected override async Task ExecuteAsync(ScreenshotAction a, ActionContext ctx, CancellationToken ct)
    {
        if (a.Mode == ScreenshotMode.Snip)
        {
            ShellLauncher.Open("ms-screenclip:");
            return;
        }

        var folderSetting = string.IsNullOrWhiteSpace(a.SaveFolder) ? ctx.Settings.ScreenshotFolder : a.SaveFolder!;
        var folder = PathUtil.Expand(folderSetting);
        if (folder.Length == 0)
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

        var target = ctx.Services.Foreground.LastExternalWindowIfAlive;
        using var bmp = await ctx.Services.Host.RunHiddenAsync(() => a.Mode == ScreenshotMode.FullScreen
            ? ScreenshotService.CaptureFullScreen()
            : ScreenshotService.CaptureWindow(target));

        var path = ScreenshotService.Save(bmp, folder);
        if (a.CopyToClipboard)
            ClipboardHelper.SetImage(ScreenshotService.ToBitmapSource(bmp));

        ctx.Services.Toasts.Success(
            a.CopyToClipboard ? $"Saved and copied to the clipboard.\n{Path.GetFileName(path)}" : $"Saved {Path.GetFileName(path)}",
            "Screenshot",
            new ToastAction("Open", () => ShellLauncher.Open(path)),
            new ToastAction("Show in folder", () => ShellLauncher.RevealFile(path)));
    }
}
