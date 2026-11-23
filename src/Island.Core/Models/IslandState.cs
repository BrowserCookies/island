using Island.Core.Layout;

namespace Island.Core.Models;

/// <summary>
/// Runtime window state (position, visibility). Stored separately from the config
/// in %APPDATA%\Island\state.json so that dragging the island never rewrites config.json.
/// </summary>
public sealed class IslandState
{
    /// <summary>Device name of the monitor (e.g. "\\.\DISPLAY1").</summary>
    public string? Monitor { get; set; }
    public Corner Corner { get; set; } = Corner.BottomRight;
    /// <summary>True if the island was dropped at an exact position (snapping off).</summary>
    public bool Exact { get; set; }
    /// <summary>Exact position, in physical pixels relative to the monitor's work area origin.</summary>
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public bool Hidden { get; set; }
}
