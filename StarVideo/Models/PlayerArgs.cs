using StarVideo.Services;

namespace StarVideo.Models;

/// <summary>打开播放页的参数。</summary>
public sealed class PlayerArgs
{
    public required MediaItem Item { get; init; }
    public int Episode { get; init; } = 1;

    /// <summary>可选的本地视频文件（由播放页「打开本地视频」按钮指定后回传）。</summary>
    public string? LocalFile { get; init; }

    /// <summary>非空时进入播放页立即播放在线测试片源（首页「在线试播」入口使用）。</summary>
    public OnlineSource? Online { get; init; }
}
