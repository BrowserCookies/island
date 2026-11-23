using System.Runtime.InteropServices;
using Island.Core.Layout;
using static Island.Interop.NativeMethods;

namespace Island.Services;

/// <summary>Enumerates displays in physical pixels (the process is Per-Monitor-V2 DPI aware).</summary>
public static class MonitorService
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        MonitorEnumProc proc = (IntPtr hMon, IntPtr _, ref RECT __, IntPtr ___) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMon, ref info))
            {
                double scale = 1.0;
                try
                {
                    if (GetDpiForMonitor(hMon, 0, out var dx, out uint _dy) == 0 && dx > 0) scale = dx / 96.0;
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }

                list.Add(new MonitorInfo(
                    info.szDevice ?? $"monitor{list.Count}",
                    PixelRect.FromLTRB(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom),
                    PixelRect.FromLTRB(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom),
                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                    scale));
            }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        GC.KeepAlive(proc);

        if (list.Count == 0)
        {
            // Should never happen, but keep the island usable.
            var wa = System.Windows.SystemParameters.WorkArea;
            list.Add(new MonitorInfo("primary", new PixelRect(0, 0, (int)System.Windows.SystemParameters.PrimaryScreenWidth, (int)System.Windows.SystemParameters.PrimaryScreenHeight),
                new PixelRect((int)wa.X, (int)wa.Y, (int)wa.Width, (int)wa.Height), true));
        }
        return list;
    }
}
