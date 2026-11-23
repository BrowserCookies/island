using System.Runtime.InteropServices;
using Island.Core.Util;
using static Island.Interop.NativeMethods;

namespace Island.Services;

/// <summary>
/// Finds the folder shown in a File Explorer window by enumerating <c>Shell.Application.Windows()</c>
/// and matching on HWND. On Windows 11 (tabbed Explorer) several shell windows share one HWND, so the
/// active tab is identified through IShellBrowser::GetWindow.
/// </summary>
public static class ExplorerFolderResolver
{
    public static bool IsExplorerWindow(IntPtr hwnd)
    {
        var cls = GetClassName(hwnd);
        return cls is "CabinetWClass" or "ExploreWClass";
    }

    public static bool IsDesktop(IntPtr hwnd)
    {
        var cls = GetClassName(hwnd);
        return cls is "Progman" or "WorkerW";
    }

    /// <summary>
    /// Returns the Explorer window's folder, or null if it isn't a file-system location
    /// (e.g. "This PC") or can't be determined. Runs the COM work on a dedicated STA thread
    /// with a timeout so a hung Explorer can't freeze the island.
    /// </summary>
    public static string? TryGetFolder(IntPtr explorerHwnd, int timeoutMs = 3000)
    {
        if (explorerHwnd == IntPtr.Zero) return null;
        string? result = null;
        var t = new Thread(() =>
        {
            try { result = GetFolderCore(explorerHwnd); }
            catch (Exception) { result = null; }
        })
        {
            IsBackground = true,
            Name = "ExplorerFolderResolver",
        };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return t.Join(timeoutMs) ? result : null;
    }

    private static string? GetFolderCore(IntPtr hwnd)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return null;
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic windows = ((dynamic)shell!).Windows();
            int count = windows.Count;

            var activeTab = FindWindowEx(hwnd, IntPtr.Zero, "ShellTabWindowClass", null);
            string? firstMatch = null;

            for (int i = 0; i < count; i++)
            {
                dynamic? w = null;
                try
                {
                    w = windows.Item(i);
                    if (w is null) continue;
                    long whwnd = (long)w.HWND;
                    if (new IntPtr(whwnd) != hwnd) continue;

                    string? path = PathOf(w);
                    if (activeTab != IntPtr.Zero && IsActiveTab(w, activeTab)) return path;
                    firstMatch ??= path;
                }
                catch (Exception)
                {
                    // Internet Explorer windows or windows that are closing can throw; skip them.
                }
                finally
                {
                    if (w is not null && Marshal.IsComObject(w)) Marshal.ReleaseComObject(w);
                }
            }
            return firstMatch;
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    private static string? PathOf(dynamic w)
    {
        string? url = null;
        try { url = (string?)w.LocationURL; } catch (Exception) { }
        var path = ExplorerPath.FromLocationUrl(url);
        if (path is not null) return path;

        // Libraries / special folders may have no LocationURL; ask the folder object itself.
        try
        {
            string? selfPath = (string?)w.Document.Folder.Self.Path;
            if (ExplorerPath.IsFileSystemPath(selfPath)) return selfPath;
        }
        catch (Exception) { }
        return null; // e.g. "This PC", "Home", "Gallery"
    }

    private static bool IsActiveTab(object shellWindow, IntPtr activeTab)
    {
        if (shellWindow is not Interop.NativeMethods.IServiceProvider sp) return false;
        var sid = SID_STopLevelBrowser;
        var iid = IID_IShellBrowser;
        if (sp.QueryService(ref sid, ref iid, out var obj) != 0 || obj is not IShellBrowser browser) return false;
        try
        {
            return browser.GetWindow(out var tabHwnd) == 0 && tabHwnd == activeTab;
        }
        finally
        {
            Marshal.ReleaseComObject(browser);
        }
    }
}

/// <summary>Resolves "{currentFolder}": the active Explorer folder, the Desktop, or a fallback folder.</summary>
public sealed class CurrentFolderService
{
    private readonly ForegroundTracker _tracker;

    public CurrentFolderService(ForegroundTracker tracker) => _tracker = tracker;

    public string Resolve(string? fallback)
    {
        var hwnd = _tracker.LastExternalWindowIfAlive;
        if (hwnd != IntPtr.Zero)
        {
            if (ExplorerFolderResolver.IsExplorerWindow(hwnd))
            {
                var folder = ExplorerFolderResolver.TryGetFolder(hwnd);
                if (folder is not null && Directory.Exists(folder)) return folder;
            }
            else if (ExplorerFolderResolver.IsDesktop(hwnd))
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }
        }
        return ResolveFallback(fallback);
    }

    public static string ResolveFallback(string? fallback)
    {
        var f = string.IsNullOrWhiteSpace(fallback) ? "%USERPROFILE%" : fallback!;
        f = Environment.ExpandEnvironmentVariables(f);
        return Directory.Exists(f) ? f : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
