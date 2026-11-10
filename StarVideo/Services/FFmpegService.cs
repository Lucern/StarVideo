using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StarVideo.Models;

namespace StarVideo.Services;

/// <summary>ffprobe 探测到的媒体信息。</summary>
public sealed record MediaProbe(
    double DurationSeconds,
    string? VideoCodec,
    string? AudioCodec,
    int Width,
    int Height,
    string? FormatName,
    string? Error = null)
{
    public bool HasVideo => VideoCodec is not null;

    public string Summary => Error is not null
        ? Error
        : $"{FormatName?.ToUpperInvariant() ?? "未知容器"} · " +
          $"{VideoCodec?.ToUpperInvariant() ?? "无视频"} / {AudioCodec?.ToUpperInvariant() ?? "无音频"} · " +
          $"{Width}x{Height} · {WatchRecord.FormatTime(DurationSeconds)}";
}

/// <summary>本地文件交给 MediaFoundation 之前的处理方式。</summary>
public enum TranscodeMode
{
    /// <summary>容器与编码系统解码器都能直接处理，零延迟播放。</summary>
    Direct,
    /// <summary>编码兼容但容器不兼容（mkv/avi/ts…），用 -c copy 秒级重封装为 mp4。</summary>
    Remux,
    /// <summary>编码不兼容（HEVC/AV1/VP9/DTS…），用 libx264 + aac 转码。</summary>
    Full
}

public sealed record TranscodeProgress(double Percent, string Message);

/// <summary>
/// FFmpeg 集成层。二进制随程序放在 Tools\ffmpeg（也可以由用户在设置里指定目录），
/// 负责：元数据探测、判断 MediaFoundation 能否直放、重封装 / 转码、进度回调、缩略图。
/// </summary>
public static class FFmpegService
{
    private static string? _version;

    public static string LocalDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarVideo");

    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg");

    public static bool IsAvailable => File.Exists(FFmpegPath);

    public static bool HasProbe => File.Exists(FFprobePath);

    public static string FFmpegPath => Resolve("ffmpeg.exe");

    public static string FFprobePath => Resolve("ffprobe.exe");

    /// <summary>给设置页展示的一行状态。</summary>
    public static string StatusText => IsAvailable
        ? $"{VersionText}  ·  {FFmpegPath}"
        : $"未找到 ffmpeg.exe（请把 FFmpeg 放到 {BundledDirectory}）";

    public static string VersionText
    {
        get
        {
            if (_version is not null) return _version;
            if (!IsAvailable) return _version = "FFmpeg 不可用";
            try
            {
                var psi = new ProcessStartInfo(FFmpegPath, "-version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                var first = p?.StandardOutput.ReadLine() ?? "";
                var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                _version = parts.Length > 2 && first.StartsWith("ffmpeg version", StringComparison.OrdinalIgnoreCase)
                    ? "FFmpeg " + parts[2].Split('-')[0]
                    : "FFmpeg 已就绪";
            }
            catch { _version = "FFmpeg 已就绪"; }
            return _version;
        }
    }

    // ---------------- 路径解析 ----------------

    private static string Resolve(string exeName)
    {
        // 1) 用户在设置里指定的目录 / 可执行文件
        var custom = AppServices.User.FFmpegPath;
        if (!string.IsNullOrWhiteSpace(custom))
        {
            var candidate = Directory.Exists(custom)
                ? Path.Combine(custom, exeName)
                : custom;
            if (File.Exists(candidate) &&
                (Directory.Exists(custom) || candidate.EndsWith(exeName, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        // 2) 随程序发布的二进制
        var bundled = Path.Combine(BundledDirectory, exeName);
        if (File.Exists(bundled)) return bundled;

        // 3) 系统 PATH
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim().Trim('"'), exeName);
                if (File.Exists(p)) return p;
            }
            catch { /* 非法 PATH 项跳过 */ }
        }

        return bundled;
    }

    // ---------------- 元数据探测 ----------------

    /// <summary>用 ffprobe 读取容器、编码、分辨率与时长；失败时返回带 Error 的对象而不抛异常。</summary>
    public static async Task<MediaProbe> ProbeAsync(string input, CancellationToken ct = default)
    {
        if (!HasProbe)
            return new MediaProbe(0, null, null, 0, 0, null, "未找到 ffprobe.exe，跳过媒体探测");

        try
        {
            var (code, stdout, stderr) = await RunAsync(FFprobePath, new[]
            {
                "-v", "error", "-print_format", "json",
                "-show_format", "-show_streams", "-i", input
            }, null, ct);

            if (string.IsNullOrWhiteSpace(stdout))
                return new MediaProbe(0, null, null, 0, 0, null,
                    $"ffprobe 失败（退出码 {code}）：{Take(stderr, 160)}");

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            double duration = 0;
            string? format = null;
            if (root.TryGetProperty("format", out var fmt))
            {
                if (fmt.TryGetProperty("format_name", out var fn)) format = fn.GetString();
                if (fmt.TryGetProperty("duration", out var dur) &&
                    double.TryParse(dur.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    duration = d;
            }

            string? video = null, audio = null;
            var width = 0;
            var height = 0;
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var s in streams.EnumerateArray())
                {
                    var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                    var codec = s.TryGetProperty("codec_name", out var c) ? c.GetString() : null;
                    if (type == "video" && video is null)
                    {
                        video = codec;
                        if (s.TryGetProperty("width", out var w)) width = w.GetInt32();
                        if (s.TryGetProperty("height", out var h)) height = h.GetInt32();
                        if (duration <= 0 && s.TryGetProperty("duration", out var sd) &&
                            double.TryParse(sd.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var dd))
                            duration = dd;
                    }
                    else if (type == "audio" && audio is null)
                    {
                        audio = codec;
                    }
                }
            }

            return new MediaProbe(duration, video, audio, width, height, format);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new MediaProbe(0, null, null, 0, 0, null, $"探测失败：{ex.Message}");
        }
    }

    /// <summary>按容器 + 编码判断 Windows Media Foundation 能否直接播放。</summary>
    public static TranscodeMode Decide(MediaProbe probe, string input)
    {
        if (probe.Error is not null) return TranscodeMode.Direct; // 探测失败就退回系统解码器自己试

        var ext = Path.GetExtension(input).ToLowerInvariant();
        var containerOk = ext is ".mp4" or ".m4v" or ".mov";

        var videoOk = probe.VideoCodec is null || IsSupportedVideo(probe.VideoCodec);
        var audioOk = probe.AudioCodec is null || IsSupportedAudio(probe.AudioCodec);

        if (containerOk && videoOk && audioOk) return TranscodeMode.Direct;
        if (videoOk && audioOk) return TranscodeMode.Remux;
        return TranscodeMode.Full;
    }

    private static bool IsSupportedVideo(string codec) => codec.ToLowerInvariant() is
        "h264" or "avc1" or "mpeg4" or "msmpeg4v3" or "vc1" or "wmv3" or "mpeg2video" or "mjpeg";

    private static bool IsSupportedAudio(string codec) => codec.ToLowerInvariant() is
        "aac" or "mp3" or "ac3" or "eac3" or "pcm_s16le" or "pcm_s24le" or "wma" or "wmav2";

    // ---------------- 转封装 / 转码 ----------------

    /// <summary>
    /// 把 input 转成 MediaFoundation 可播的 H.264/AAC MP4，返回缓存文件路径。
    /// 同一文件 + 同一模式会复用缓存，避免重复转码。
    /// </summary>
    public static async Task<string> ConvertAsync(string input, TranscodeMode mode,
        double durationSeconds, IProgress<TranscodeProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!IsAvailable) throw new InvalidOperationException(
            $"未找到 ffmpeg.exe，请把 FFmpeg 放到 {BundledDirectory} 或在设置中指定路径");

        var cacheDir = Path.Combine(LocalDataDirectory, "transcode");
        Directory.CreateDirectory(cacheDir);

        var output = Path.Combine(cacheDir, CacheKey(input, mode) + ".mp4");
        if (File.Exists(output) && new FileInfo(output).Length > 1024)
        {
            progress?.Report(new TranscodeProgress(100, "使用已缓存的转码结果"));
            return output;
        }

        var temp = output + ".part.mp4";
        TryDelete(temp);

        var verb = mode == TranscodeMode.Remux ? "重封装" : "转码";
        progress?.Report(new TranscodeProgress(0, $"FFmpeg 正在{verb}：{Path.GetFileName(input)}"));

        var args = new List<string> { "-y", "-v", "error", "-i", input, "-map", "0:v:0?", "-map", "0:a:0?" };
        if (mode == TranscodeMode.Remux)
        {
            args.AddRange(new[] { "-c", "copy", "-movflags", "+faststart" });
        }
        else
        {
            args.AddRange(new[]
            {
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p",
                "-vf", "scale='min(1920,iw)':-2",
                "-c:a", "aac", "-b:a", "128k", "-ac", "2",
                "-movflags", "+faststart"
            });
        }
        args.AddRange(new[] { "-progress", "pipe:1", "-nostats", temp });

        void OnLine(string line)
        {
            if (progress is null) return;
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) ||
                line.StartsWith("out_time_ms=", StringComparison.Ordinal))
            {
                var value = line[(line.IndexOf('=') + 1)..];
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var micros) &&
                    durationSeconds > 0)
                {
                    var pct = Math.Clamp(micros / 1_000_000 / durationSeconds * 100, 0, 99);
                    progress.Report(new TranscodeProgress(pct,
                        $"FFmpeg {verb}中 {pct:0}%  ·  {Path.GetFileName(input)}"));
                }
            }
            else if (line.StartsWith("progress=end", StringComparison.Ordinal))
            {
                progress.Report(new TranscodeProgress(100, $"FFmpeg {verb}完成"));
            }
        }

        try
        {
            var (code, _, stderr) = await RunAsync(FFmpegPath, args, OnLine, ct);
            if (code != 0 || !File.Exists(temp) || new FileInfo(temp).Length < 1024)
                throw new IOException($"FFmpeg 退出码 {code}：{Take(stderr, 300)}");

            File.Move(temp, output, true);
            progress?.Report(new TranscodeProgress(100, $"FFmpeg {verb}完成，开始播放"));
            return output;
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>抽取一帧作为真实视频封面（失败时返回 null，界面继续用海报）。</summary>
    public static async Task<string?> ExtractThumbnailAsync(string input, double seconds = 3,
        CancellationToken ct = default)
    {
        if (!IsAvailable) return null;
        var dir = Path.Combine(LocalDataDirectory, "thumbs");
        Directory.CreateDirectory(dir);
        var output = Path.Combine(dir, CacheKey(input, TranscodeMode.Direct) + ".jpg");
        if (File.Exists(output)) return output;

        try
        {
            var (code, _, _) = await RunAsync(FFmpegPath, new[]
            {
                "-y", "-v", "error", "-ss", seconds.ToString("0.##", CultureInfo.InvariantCulture),
                "-i", input, "-frames:v", "1", "-vf", "scale=960:-2", "-q:v", "3", output
            }, null, ct);
            return code == 0 && File.Exists(output) ? output : null;
        }
        catch { return null; }
    }

    /// <summary>清空转码缓存，返回释放的字节数。</summary>
    public static long ClearCache()
    {
        long total = 0;
        foreach (var dir in new[] { "transcode", "thumbs" })
        {
            var path = Path.Combine(LocalDataDirectory, dir);
            if (!Directory.Exists(path)) continue;
            foreach (var f in Directory.EnumerateFiles(path))
            {
                try { total += new FileInfo(f).Length; File.Delete(f); }
                catch { /* 占用中的文件跳过 */ }
            }
        }
        return total;
    }

    public static long CacheSize()
    {
        long total = 0;
        foreach (var dir in new[] { "transcode", "thumbs" })
        {
            var path = Path.Combine(LocalDataDirectory, dir);
            if (!Directory.Exists(path)) continue;
            foreach (var f in Directory.EnumerateFiles(path))
            {
                try { total += new FileInfo(f).Length; } catch { /* 忽略 */ }
            }
        }
        return total;
    }

    // ---------------- 内部工具 ----------------

    private static string CacheKey(string input, TranscodeMode mode)
    {
        string stamp;
        try
        {
            var info = new FileInfo(input);
            stamp = $"{info.Length}_{info.LastWriteTimeUtc.Ticks}";
        }
        catch { stamp = "0"; }

        var raw = $"{input}|{stamp}|{mode}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
    }

    private static async Task<(int Code, string Out, string Err)> RunAsync(string exe,
        IEnumerable<string> args, Action<string>? onStdoutLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var outSb = new StringBuilder();
        var errSb = new StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            onStdoutLine?.Invoke(e.Data);
            outSb.AppendLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) errSb.AppendLine(e.Data);
        };

        proc.Start();
        proc.StandardInput.Close();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { /* 进程已退出 */ }
        });

        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { /* 由调用方通过 ct 判断 */ }

        return (proc.HasExited ? proc.ExitCode : -1, outSb.ToString(), errSb.ToString());
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 忽略 */ }
    }

    private static string Take(string text, int max) =>
        string.IsNullOrWhiteSpace(text) ? "无错误输出" :
        text.Length <= max ? text.Trim() : text[..max].Trim() + "…";
}
