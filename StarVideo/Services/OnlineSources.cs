namespace StarVideo.Services;

/// <summary>一条可在线播放的测试片源。</summary>
public sealed record OnlineSource(string Name, string Description, string Url, string Badge);

/// <summary>
/// 内置的公开测试片源，全部为可匿名访问、支持 Range 请求的视频地址。
/// 交付前用 HTTP Range 请求逐个验证过返回 206 + video/mp4（HLS 为 audio/mpegurl）。
/// </summary>
public static class OnlineSources
{
    public static IReadOnlyList<OnlineSource> Presets { get; } = new[]
    {
        new OnlineSource(
            "Sintel 预告片",
            "W3C 官方示例 · H.264/AAC MP4 · 约 52 秒",
            "https://media.w3.org/2010/05/sintel/trailer.mp4",
            "MP4"),
        new OnlineSource(
            "Big Buck Bunny 720p",
            "test-videos.co.uk · H.264/AAC MP4 · 10 秒 / 2MB",
            "https://test-videos.co.uk/vids/bigbuckbunny/mp4/h264/720/Big_Buck_Bunny_720_10s_2MB.mp4",
            "MP4"),
        new OnlineSource(
            "Flower 花朵",
            "MDN 示例视频 · H.264/AAC MP4 · 约 5 秒",
            "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4",
            "MP4"),
        new OnlineSource(
            "Mux HLS 测试流",
            "多码率 HLS 直播点播流 · m3u8",
            "https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8",
            "HLS")
    };

    public static OnlineSource Default => Presets[0];

    /// <summary>校验自定义地址，只接受 http/https 的绝对地址。</summary>
    public static bool TryValidate(string text, out Uri? uri, out string error)
    {
        uri = null;
        error = "";
        text = text.Trim();
        if (text.Length == 0)
        {
            error = "请输入视频地址";
            return false;
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            error = "地址无效，请输入 http:// 或 https:// 开头的 MP4 / m3u8 链接";
            return false;
        }
        uri = parsed;
        return true;
    }
}
