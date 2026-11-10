using System.IO;
using System.Text.Json;
using StarVideo.Models;

namespace StarVideo.Services;

/// <summary>收藏 / 观看历史 / 继续观看，落地到 %LOCALAPPDATA%\StarVideo\user.json。</summary>
public sealed class UserDataService
{
    private sealed class Data
    {
        public List<string> Favorites { get; set; } = new();
        public List<WatchRecord> History { get; set; } = new();
        public string Theme { get; set; } = "Dark";
        public bool AutoPlayNext { get; set; } = true;
        /// <summary>用户自定义 FFmpeg 目录，留空则使用随程序发布的 Tools\ffmpeg。</summary>
        public string FFmpegPath { get; set; } = "";
        /// <summary>本地视频是否强制经过 FFmpeg 转码后再播放。</summary>
        public bool ForceFFmpeg { get; set; }
    }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StarVideo", "user.json");

    private Data _data;

    public UserDataService()
    {
        try
        {
            _data = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data()
                : new Data();
        }
        catch
        {
            _data = new Data();
        }
    }

    public string Theme { get => _data.Theme; set { _data.Theme = value; Save(); } }
    public bool AutoPlayNext { get => _data.AutoPlayNext; set { _data.AutoPlayNext = value; Save(); } }
    public string FFmpegPath { get => _data.FFmpegPath; set { _data.FFmpegPath = value; Save(); } }
    public bool ForceFFmpeg { get => _data.ForceFFmpeg; set { _data.ForceFFmpeg = value; Save(); } }

    public bool IsFavorite(string id) => _data.Favorites.Contains(id, StringComparer.OrdinalIgnoreCase);

    public bool ToggleFavorite(string id)
    {
        if (IsFavorite(id)) _data.Favorites.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        else _data.Favorites.Insert(0, id);
        Save();
        AppServices.NotifyDataChanged();
        return IsFavorite(id);
    }

    public List<MediaItem> Favorites(CatalogService catalog) =>
        _data.Favorites.Select(catalog.Find).Where(i => i is not null).Select(i => i!).ToList();

    public List<WatchEntry> History(CatalogService catalog) =>
        _data.History.OrderByDescending(r => r.UpdatedAt)
            .Select(r => catalog.Find(r.MediaId) is { } item
                ? new WatchEntry { Item = item, Record = r }
                : null)
            .Where(e => e is not null).Select(e => e!).ToList();

    public List<WatchEntry> ContinueWatching(CatalogService catalog) =>
        History(catalog).Where(e => e.Record.ProgressPercent is > 1 and < 96).Take(8).ToList();

    public WatchRecord? RecordOf(string id) => _data.History.FirstOrDefault(r => r.MediaId == id);

    public void RecordProgress(string id, int episode, double position, double duration)
    {
        var rec = _data.History.FirstOrDefault(r => r.MediaId == id);
        if (rec is null)
        {
            rec = new WatchRecord { MediaId = id };
            _data.History.Insert(0, rec);
        }
        rec.Episode = episode;
        rec.PositionSeconds = position;
        rec.DurationSeconds = duration;
        rec.UpdatedAt = DateTime.Now;
        Save();
        AppServices.NotifyDataChanged();
    }

    public void RemoveHistory(string id)
    {
        _data.History.RemoveAll(r => r.MediaId == id);
        Save();
        AppServices.NotifyDataChanged();
    }

    public void ClearHistory()
    {
        _data.History.Clear();
        Save();
        AppServices.NotifyDataChanged();
    }

    public void ClearFavorites()
    {
        _data.Favorites.Clear();
        Save();
        AppServices.NotifyDataChanged();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StarVideo] 保存用户数据失败: {ex.Message}");
        }
    }
}
