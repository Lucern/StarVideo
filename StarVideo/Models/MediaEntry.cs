using StarVideo.Models;

namespace StarVideo;

/// <summary>「继续观看 / 历史」列表项：把作品与观看记录打包，方便 XAML 直接绑定。</summary>
public sealed class WatchEntry
{
    public required MediaItem Item { get; init; }
    public required WatchRecord Record { get; init; }
    public string Title => Item.Title;
    public string PosterPath => Item.PosterPath;
    public string ProgressText => Record.ProgressText;
    public double ProgressPercent => Record.ProgressPercent;
    public string EpisodeText => Item.IsSeries ? $"第{Record.Episode}集" : "正片";
}

/// <summary>排行榜列表项。</summary>
public sealed class RankEntry
{
    public required int Rank { get; init; }
    public required MediaItem Item { get; init; }
    public string RankText => Rank.ToString("00");
    public string Title => Item.Title;
    public string PosterPath => Item.PosterPath;
    public string ScoreText => Item.ScoreText;
    public string HotText => Item.HotText;
    public string MetaText => Item.MetaText;
}
