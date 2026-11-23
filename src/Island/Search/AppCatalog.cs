namespace Island.Search;

public sealed record AppEntry(string Name, string LaunchPath);

/// <summary>
/// Installed apps (desktop and Store) from the shell's Apps folder, kept in memory so the search bar
/// can match them on every keystroke. Enumerated on a dedicated STA thread; refreshed at most every
/// 10 minutes.
/// </summary>
public sealed class AppCatalog
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);

    private volatile IReadOnlyList<AppEntry> _apps = Array.Empty<AppEntry>();
    private DateTime _lastRefresh = DateTime.MinValue;
    private int _refreshing;

    public IReadOnlyList<AppEntry> Apps => _apps;

    public event EventHandler? Changed;

    public void RefreshInBackground()
    {
        if (DateTime.UtcNow - _lastRefresh < RefreshInterval) return;
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        _lastRefresh = DateTime.UtcNow;
        var t = new Thread(() =>
        {
            try
            {
                var apps = Enumerate();
                if (apps.Count > 0)
                {
                    _apps = apps;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception)
            {
                _lastRefresh = DateTime.MinValue; // try again next time
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        })
        {
            IsBackground = true,
            Name = "AppCatalog",
        };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    private static List<AppEntry> Enumerate()
    {
        var list = new List<AppEntry>();
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return list;
        object? shell = Activator.CreateInstance(shellType);
        try
        {
            dynamic folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            if (folder is null) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (dynamic item in folder.Items())
            {
                string? name = item.Name;
                string? parsing = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parsing)) continue;
                // Uninstallers and help links in the Start menu are noise for a launcher.
                if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(name + "|" + parsing)) continue;
                list.Add(new AppEntry(name, @"shell:AppsFolder\" + parsing));
            }
        }
        finally
        {
            if (shell is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
        return list;
    }
}
