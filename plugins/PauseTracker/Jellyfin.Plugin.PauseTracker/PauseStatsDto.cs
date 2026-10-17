namespace Jellyfin.Plugin.PauseTracker;

public class PauseStatsDto
{
    public string? ItemId { get; set; }

    public int PauseCount { get; set; }

    public bool IsPaused { get; set; }

    /// <summary>Length of the ongoing pause in milliseconds, 0 if playing.</summary>
    public long CurrentPauseMs { get; set; }

    /// <summary>Accumulated paused time in milliseconds, including any ongoing pause.</summary>
    public long TotalPausedMs { get; set; }

    /// <summary>Lengths of each completed pause interval, in milliseconds.</summary>
    public IReadOnlyList<long> IntervalsMs { get; set; } = Array.Empty<long>();
}
