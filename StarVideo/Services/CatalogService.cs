using System.IO;
using System.Text.Json;
using StarVideo.Models;

namespace StarVideo.Services;

public sealed class CatalogService
{
    private readonly Dictionary<string, MediaItem> _byId;

    public IReadOnlyList<MediaItem> All { get; }
    public IReadOnlyList<MediaItem> Banners { get; }
    public IReadOnlyList<string> Categories { get; } =
        new[] { "电视剧", "电影", "综艺", "动漫", "纪录片", "少儿" };

    public CatalogService()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "catalog.json");
        var json = File.ReadAllText(path);
        var items = JsonSerializer.Deserialize<List<MediaItem>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<MediaItem>();

        All = items;
        Banners = items.Where(i => i.Banner).ToList();
        _byId = items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
    }

    public MediaItem? Find(string? id) =>
        id is not null && _byId.TryGetValue(id, out var item) ? item : null;

    public IReadOnlyList<string> RegionsOf(string? category) =>
        Query(category, null, null).Select(i => i.Region).Distinct().OrderBy(x => x).ToList();

    public IReadOnlyList<string> YearsOf(string? category) =>
        Query(category, null, null).Select(i => i.Year).Distinct().OrderByDescending(x => x)
            .Select(x => x.ToString()).ToList();

    /// <summary>按频道筛选。category = "全部" 或 null 表示不限。</summary>
    public List<MediaItem> Query(string? category, string? region, string? year,
        string sort = "热度", bool onlyVip = false, bool onlyFree = false, string? keyword = null)
    {
        IEnumerable<MediaItem> q = All;

        if (!string.IsNullOrWhiteSpace(category) && category != "全部")
            q = q.Where(i => i.Category == category);
        if (!string.IsNullOrWhiteSpace(region) && region != "全部")
            q = q.Where(i => i.Region == region);
        if (!string.IsNullOrWhiteSpace(year) && year != "全部" && int.TryParse(year, out var y))
            q = q.Where(i => i.Year == y);
        if (onlyVip) q = q.Where(i => i.Vip);
        if (onlyFree) q = q.Where(i => !i.Vip);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            q = q.Where(i =>
                i.Title.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                i.En.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                i.Genre.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                i.Region.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                i.Category.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                i.Tags.Any(t => t.Contains(k, StringComparison.OrdinalIgnoreCase)) ||
                i.Description.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                i.Cast.Any(c => c.Contains(k, StringComparison.OrdinalIgnoreCase)));
        }

        return sort switch
        {
            "评分" => q.OrderByDescending(i => i.Score).ThenByDescending(i => i.Hot).ToList(),
            "最新" => q.OrderByDescending(i => i.Year).ThenByDescending(i => i.Hot).ToList(),
            _ => q.OrderByDescending(i => i.Hot).ToList()
        };
    }

    public List<MediaItem> Ranked(string? category = null, int take = 20) =>
        Query(category, null, null, "热度").Take(take).ToList();

    /// <summary>猜你喜欢：同分类优先，再按评分兜底。</summary>
    public List<MediaItem> Related(MediaItem item, int take = 6) =>
        All.Where(i => i.Id != item.Id)
           .OrderByDescending(i => i.Category == item.Category)
           .ThenByDescending(i => i.Score)
           .Take(take).ToList();

    public List<string> Suggest(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return new List<string>();
        return All.Where(i => i.Title.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase) ||
                              i.En.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase))
                  .OrderByDescending(i => i.Hot)
                  .Select(i => i.Title)
                  .Take(8).ToList();
    }
}
