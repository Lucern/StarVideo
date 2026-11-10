using System.IO;

namespace StarVideo.Models;

/// <summary>一部影视作品。字段与 Assets/catalog.json 一一对应，可直接改 JSON 扩充片库。</summary>
public sealed class MediaItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string En { get; set; } = "";
    public string Category { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Region { get; set; } = "";
    public int Year { get; set; }
    public double Score { get; set; }
    public int Hot { get; set; }
    public int Episodes { get; set; }
    public string Duration { get; set; } = "";
    public bool Vip { get; set; }
    public bool Exclusive { get; set; }
    public List<string> Tags { get; set; } = new();
    public string Description { get; set; } = "";
    public List<string> Cast { get; set; } = new();
    public string Tagline { get; set; } = "";
    public bool Banner { get; set; }
    public string ColorA { get; set; } = "#333333";
    public string ColorB { get; set; } = "#111111";
    public string Motif { get; set; } = "";

    // ---- 仅供 UI 使用的派生属性 ----

    public string PosterPath => AssetPath(Path.Combine("Posters", Id + ".jpg"));
    public string BannerPath => AssetPath(Path.Combine("Banners", Id + ".jpg"));

    public string ScoreText => Score.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    public string HotText => Hot >= 1000 ? $"{Hot / 1000.0:0.0}亿" : $"{Hot}万";
    public string YearText => Year.ToString();
    public string GenreText => Genre.Replace(" ", " · ");
    public string CastText => Cast.Count == 0 ? "暂无演职员信息" : string.Join("  /  ", Cast);
    public string VipBadge => Vip ? "VIP" : (Exclusive ? "独家" : "");
    public string CornerBadge => Exclusive ? "独家" : (Vip ? "VIP" : "");
    public string MetaText => $"{Year} · {Region} · {GenreText}";
    public string TagsText => Tags.Count == 0 ? "" : string.Join("   ", Tags.Select(t => "· " + t));
    public bool HasBadge => Vip || Exclusive;
    public bool IsSeries => Category is "电视剧" or "动漫" or "综艺" or "纪录片" or "少儿";

    /// <summary>详情页/播放页用的选集列表。</summary>
    public List<string> EpisodeNames
    {
        get
        {
            if (!IsSeries || Episodes <= 1) return new List<string> { "正片" };
            var unit = Category == "综艺" ? "期" : "集";
            return Enumerable.Range(1, Episodes).Select(i => $"第{i}{unit}").ToList();
        }
    }

    private static string AssetPath(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", relative);

    public override string ToString() => Title;
}
