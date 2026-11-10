using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Windows.Media.Core;

namespace StarVideo.Services;

/// <summary>
/// 开发自检（--selftest）：覆盖频道 Tab 点击 / 横向滚动、FFmpeg 探测与转码、
/// 在线测试片源可达性、MediaSource 创建。结果写入 %LOCALAPPDATA%\StarVideo\selftest.log。
/// </summary>
public static class SelfTest
{
    public static async Task<string> RunAsync(MainWindow window)
    {
        var log = new StringBuilder();
        log.AppendLine($"[StarVideo] 自检 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        log.AppendLine("---- 频道 Tab 点击 / 滚动 ----");
        try { log.AppendLine(await window.RunNavCheckAsync()); }
        catch (Exception ex) { log.AppendLine($"NAV FAIL {ex}"); }

        log.AppendLine("---- FFmpeg ----");
        log.AppendLine($"available={FFmpegService.IsAvailable} probe={FFmpegService.HasProbe} {FFmpegService.StatusText}");

        var dir = FFmpegService.LocalDataDirectory;
        foreach (var name in new[] { "test_h264.mp4", "test_remux.mkv", "test_hevc.mkv" })
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path))
            {
                log.AppendLine($"FILE {name} MISSING（用 Tools/ffmpeg 生成后可重跑）");
                continue;
            }

            var probe = await FFmpegService.ProbeAsync(path);
            var mode = FFmpegService.Decide(probe, path);
            log.AppendLine($"FILE {name} mode={mode} probe={probe.Summary}");

            if (mode == TranscodeMode.Direct)
            {
                log.AppendLine("  DIRECT 无需转码");
                continue;
            }

            try
            {
                var sw = Stopwatch.StartNew();
                var lastProgress = "";
                var progress = new Progress<TranscodeProgress>(p => lastProgress = p.Message);
                var output = await FFmpegService.ConvertAsync(path, mode, probe.DurationSeconds, progress);
                sw.Stop();
                var size = new FileInfo(output).Length;
                var check = await FFmpegService.ProbeAsync(output);
                var ok = size > 1024 && check.VideoCodec == "h264" && check.AudioCodec == "aac";
                log.AppendLine($"  CONVERT {(ok ? "OK" : "FAIL")} {sw.ElapsedMilliseconds}ms " +
                               $"out={Path.GetFileName(output)} {size / 1024}KB " +
                               $"codec={check.VideoCodec}/{check.AudioCodec} last=\"{lastProgress}\"");
            }
            catch (Exception ex) { log.AppendLine($"  CONVERT FAIL {ex.Message}"); }
        }

        log.AppendLine("---- 在线测试片源 ----");
        foreach (var source in OnlineSources.Presets)
        {
            var valid = OnlineSources.TryValidate(source.Url, out var uri, out var error);
            string http;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var request = new HttpRequestMessage(HttpMethod.Get, uri!);
                request.Headers.Range = new RangeHeaderValue(0, 1023);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                http = $"{(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType ?? "?"}";
            }
            catch (Exception ex) { http = "ERR " + ex.Message; }
            log.AppendLine($"URL {source.Badge,-3} {source.Name,-24} valid={valid} http={http} {error}");
        }

        log.AppendLine("---- MediaSource ----");
        try
        {
            using var ms = MediaSource.CreateFromUri(new Uri(OnlineSources.Default.Url));
            log.AppendLine($"MEDIASOURCE create OK state={ms.State}");
        }
        catch (Exception ex) { log.AppendLine($"MEDIASOURCE FAIL {ex.Message}"); }

        log.AppendLine("---- 版本比对 / Release 更新检查 ----");
        var cases = new (string Latest, string Current, int Expected)[]
        {
            ("v1.2.3", "1.0.0", 1),
            ("1.0.0", "v1.0.0", 0),
            ("v1.0.0", "v1.0.1", -1),
            ("v2.0.0-beta.1", "v2.0.0", -1),
            ("v1.0.0", "v1.0.0-rc.1", 1),
            ("v1.3", "1.2.9", 1),
            ("v1.0.0+abc123", "v1.0.0", 0),
            ("v1.10.0", "v1.9.9", 1),
            ("v1.2.3-beta.2", "v1.2.3-beta.10", -1)
        };
        var passed = 0;
        foreach (var c in cases)
        {
            var ok = AppVersion.TryParse(c.Latest, out var latest) &&
                     AppVersion.TryParse(c.Current, out var current) &&
                     Math.Sign(AppVersion.Compare(latest, current)) == c.Expected;
            if (ok) passed++;
            else log.AppendLine($"COMPARE FAIL {c.Latest} vs {c.Current} expect={c.Expected}");
        }
        log.AppendLine($"COMPARE {passed}/{cases.Length} OK");
        log.AppendLine($"COMPARE unparsable={!AppVersion.TryParse("release-2026", out _)}");
        log.AppendLine($"VERSION current={UpdateService.CurrentVersion} repo={UpdateService.DefaultRepo}");
        try
        {
            var r = await UpdateService.CheckAsync(force: false);
            log.AppendLine($"UPDATE repo={r.Repo} latest={r.LatestVersion ?? r.Release?.Tag ?? "-"} " +
                           $"status={r.Status} cache={r.FromCache} assets={r.Release?.Assets.Count ?? 0} " +
                           $"msg={r.Message}");
        }
        catch (Exception ex) { log.AppendLine($"UPDATE FAIL {ex.Message}"); }

        return log.ToString();
    }
}
