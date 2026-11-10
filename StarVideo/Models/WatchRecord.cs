namespace StarVideo.Models;

/// <summary>一条观看记录（同时用于「继续观看」和「历史」）。</summary>
public sealed class WatchRecord
{
    public string MediaId { get; set; } = "";
    public int Episode { get; set; } = 1;
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; } = 45 * 60;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public double ProgressPercent =>
        DurationSeconds <= 0 ? 0 : Math.Max(0, Math.Min(100, PositionSeconds / DurationSeconds * 100));

    public string ProgressText =>
        DurationSeconds <= 0 ? "" : $"看到 {FormatTime(PositionSeconds)} / {FormatTime(DurationSeconds)}";

    public static string FormatTime(double seconds)
    {
        var s = (int)Math.Max(0, seconds);
        return s >= 3600
            ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}"
            : $"{s / 60:00}:{s % 60:00}";
    }
}
