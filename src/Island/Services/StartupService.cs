using System.Runtime.InteropServices;

namespace Island.Services;

/// <summary>"Start with Windows" via a shortcut in the user's Startup folder.</summary>
public static class StartupService
{
    public static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Island.lnk");

    public static bool IsEnabled => File.Exists(ShortcutPath);

    public static void Apply(bool enable)
    {
        if (enable) Enable();
        else Disable();
    }

    private static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;

        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell is not available.");
        object? shell = null;
        object? lnk = null;
        try
        {
            shell = Activator.CreateInstance(type)!;
            lnk = ((dynamic)shell).CreateShortcut(ShortcutPath);
            dynamic l = lnk!;
            l.TargetPath = exe;
            l.WorkingDirectory = Path.GetDirectoryName(exe);
            l.Description = "Island";
            l.IconLocation = exe + ",0";
            l.Save();
        }
        finally
        {
            if (lnk is not null && Marshal.IsComObject(lnk)) Marshal.ReleaseComObject(lnk);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    private static void Disable()
    {
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
    }
}
