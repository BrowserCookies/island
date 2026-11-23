using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using Island.Core.Scripts;
using Microsoft.Win32;

namespace Island.Services;

public static class ShellLauncher
{
    /// <summary>ShellExecute a file, folder, URL or protocol.</summary>
    public static void Open(string target, string? arguments = null, string? workingDirectory = null, bool runAsAdmin = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true,
        };
        if (!string.IsNullOrWhiteSpace(arguments)) psi.Arguments = arguments;
        if (!string.IsNullOrWhiteSpace(workingDirectory)) psi.WorkingDirectory = workingDirectory;
        if (runAsAdmin) psi.Verb = "runas";
        try
        {
            using var _ = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user declined the UAC prompt; not an error.
        }
    }

    public static void OpenFolder(string path) => Open("explorer.exe", CommandLine.Quote(path));

    /// <summary>Opens Explorer with the file selected.</summary>
    public static void RevealFile(string path) => Open("explorer.exe", "/select," + CommandLine.Quote(path));

    /// <summary>Opens a path that may be a file or a folder.</summary>
    public static void OpenPath(string path)
    {
        var p = Environment.ExpandEnvironmentVariables(path);
        if (Directory.Exists(p)) OpenFolder(p);
        else Open(p);
    }

    /// <summary>
    /// The default browser's executable (from the http UserChoice ProgId), used to open the
    /// browser's home page when an OpenUrl button has no URL.
    /// </summary>
    public static string? DefaultBrowserPath()
    {
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice")
                               ?? Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            var progId = choice?.GetValue("ProgId") as string;
            if (string.IsNullOrEmpty(progId)) return null;
            using var cmd = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            var exe = CommandLine.ExtractExecutable(cmd?.GetValue(null) as string);
            return exe is not null && File.Exists(exe) ? exe : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Finds an executable on PATH (with PATHEXT), e.g. "wt.exe" or "claude".</summary>
    public static string? FindOnPath(string name)
    {
        if (Path.IsPathRooted(name)) return File.Exists(name) ? name : null;
        var exts = Path.HasExtension(name)
            ? new[] { "" }
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        // Windows Terminal's app-execution alias lives here and is not always on PATH for GUI apps.
        dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps"));
        foreach (var d in dirs)
        {
            foreach (var e in exts)
            {
                try
                {
                    var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(d.Trim().Trim('"')), name + e);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }
}

/// <summary>Clipboard access with retries (the clipboard is often briefly locked by other apps).</summary>
public static class ClipboardHelper
{
    public static string GetText()
    {
        return Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : "") ?? "";
    }

    public static void SetText(string text)
    {
        Retry(() =>
        {
            Clipboard.SetDataObject(text, true);
            return true;
        });
    }

    public static void SetImage(BitmapSource image)
    {
        Retry(() =>
        {
            Clipboard.SetImage(image);
            return true;
        });
    }

    private static T? Retry<T>(Func<T> f)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                return f();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // COMException / CLIPBRD_E_CANT_OPEN: someone else has the clipboard open.
                Thread.Sleep(60);
            }
        }
        return default;
    }
}
