using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace StarVideo.Services;

/// <summary>GitHub Release 里的一个可下载附件。</summary>
public sealed record ReleaseAsset(string Name, long Size, string DownloadUrl);

/// <summary>一条 Release 的展示信息。</summary>
public sealed record ReleaseInfo(
    string Tag,
    string Name,
    string HtmlUrl,
    DateTimeOffset PublishedAt,
    string Notes,
    bool Prerelease,
    IReadOnlyList<ReleaseAsset> Assets);

public enum UpdateStatus
{
    /// <summary>已是最新。</summary>
    UpToDate,
    /// <summary>发现更高版本。</summary>
    UpdateAvailable,
    /// <summary>仓库还没有发布任何 Release。</summary>
    NoRelease,
    /// <summary>检查失败（网络 / 限流 / 版本号无法解析）。</summary>
    Failed
}

// 用 record 是为了支持 with 表达式（304 / 降级时基于已有结果改写 Message）。
public sealed record UpdateCheckResult
{
    public UpdateStatus Status { get; init; }
    public required string CurrentVersion { get; init; }
    public string Repo { get; init; } = "";
    public ReleaseInfo? Release { get; init; }
    public string? LatestVersion { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
    /// <summary>本次结果来自本地缓存（节流命中或网络失败降级）。</summary>
    public bool FromCache { get; init; }
    /// <summary>命令行 --fake-update 注入的模拟结果，仅用于界面自检。</summary>
    public bool Simulated { get; init; }
    public bool IsUpdateAvailable => Status == UpdateStatus.UpdateAvailable;
}

/// <summary>
/// 简易语义化版本：支持 v 前缀、1.2 / 1.2.3、-pre.1 预发布与 +build 元数据。
/// 只做版本比较，不实现完整 SemVer 规范里与比较无关的部分。
/// </summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch, string PreRelease)
{
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        if (s[0] is 'v' or 'V') s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];           // build metadata 不参与比较

        var pre = "";
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..];
            s = s[..dash];
        }

        var parts = s.Split('.');
        if (parts.Length is < 1 or > 3) return false;
        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new AppVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    /// <summary>大于返回正数，等于返回 0，小于返回负数。正式版高于同号的预发布版。</summary>
    public static int Compare(AppVersion a, AppVersion b)
    {
        var c = a.Major.CompareTo(b.Major);
        if (c != 0) return c;
        c = a.Minor.CompareTo(b.Minor);
        if (c != 0) return c;
        c = a.Patch.CompareTo(b.Patch);
        if (c != 0) return c;
        return ComparePreRelease(a.PreRelease, b.PreRelease);
    }

    private static int ComparePreRelease(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 0;
        if (a.Length == 0) return 1;    // 正式版更高
        if (b.Length == 0) return -1;

        var sa = a.Split('.');
        var sb = b.Split('.');
        for (var i = 0; i < Math.Max(sa.Length, sb.Length); i++)
        {
            if (i >= sa.Length) return -1;   // 段数少者更低
            if (i >= sb.Length) return 1;
            var na = int.TryParse(sa[i], out var ia);
            var nb = int.TryParse(sb[i], out var ib);
            int c;
            if (na && nb) c = ia.CompareTo(ib);
            else if (na) c = -1;             // 纯数字段低于字母数字段
            else if (nb) c = 1;
            else c = string.Compare(sa[i], sb[i], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return 0;
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (PreRelease.Length > 0 ? "-" + PreRelease : "");
}

/// <summary>
/// 应用内版本更新检查：比对 GitHub 仓库最新 Release 的 tag 与当前程序版本。
///
/// 仓库地址来自本项目实际的 git remote（Lucern/StarVideo），用户可在「设置 → 版本更新」里改。
/// API 为 https://api.github.com/repos/{owner}/{repo}/releases/latest —— GitHub 定义为
/// 「最新一个非草稿、非预发布的 Release」。未认证访问限流 60 次/小时/IP，所以：
///   1) 自动检查 6 小时节流，命中节流直接复用缓存，不发请求；
///   2) 带 If-None-Match / ETag，返回 304 时复用上次响应且不计入限流；
///   3) 限流或断网时降级展示上次结果，不会把「检查失败」误报成「有/没有新版本」。
///
/// 设计边界：只做「检查 + 提示 + 跳转/下载」，不自动覆盖正在运行的 exe。
/// 非打包桌面程序运行时无法安全替换自身，且涉及签名校验与回滚，交给用户手动安装更稳妥。
/// </summary>
public static class UpdateService
{
    /// <summary>本项目 git remote 的真实地址，作默认值。</summary>
    public const string DefaultRepo = "Lucern/StarVideo";

    public static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(6);
    private const string ApiHost = "https://api.github.com/repos/";
    private const int RequestTimeoutSeconds = 12;

    private static string? _currentVersion;
    private static string? _simulatedLatest;

    /// <summary>仅供 --fake-update 自检：伪造一个最新版本号，走完整 UI 链路但不联网。</summary>
    public static string? SimulatedLatestVersion
    {
        get => _simulatedLatest;
        set => _simulatedLatest = value;
    }

    /// <summary>当前程序版本（来自 csproj &lt;Version&gt; 生成的程序集信息）。</summary>
    public static string CurrentVersion
    {
        get
        {
            if (_currentVersion is not null) return _currentVersion;

            string? raw = null;
            try
            {
                raw = Assembly.GetEntryAssembly()?
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            }
            catch { /* 程序集信息读取失败再退回文件版本 */ }

            try
            {
                raw ??= FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? "").ProductVersion;
            }
            catch { /* 忽略 */ }

            // InformationalVersion 可能带 +commit-sha；版本比较只用主版本部分。
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var plus = raw.IndexOf('+');
                var clean = (plus > 0 ? raw[..plus] : raw).Trim();
                if (AppVersion.TryParse(clean, out var parsed))
                    return _currentVersion = parsed.ToString();
            }
            return _currentVersion = "1.0.0";
        }
    }

    public static string ReleasesPageUrl(string repo) => $"https://github.com/{repo}/releases";

    public static string ApiUrl(string repo) => ApiHost + repo + "/releases/latest";

    /// <summary>把用户输入的 owner/repo 或 GitHub 仓库地址规范化；留空恢复默认仓库。</summary>
    public static bool TryNormalizeRepo(string? text, out string repo, out string error)
    {
        repo = "";
        error = "";
        var s = (text ?? "").Trim().TrimEnd('/');
        if (s.Length == 0)
        {
            repo = DefaultRepo;
            return true;
        }

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                !uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase))
            {
                error = "目前只支持 github.com 仓库";
                return false;
            }
            s = uri.AbsolutePath.Trim('/');
        }

        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        s = s.Trim('/');
        var parts = s.Split('/');
        static bool Safe(string p) => p.Length > 0 && p.All(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

        if (parts.Length != 2 || !parts.All(Safe))
        {
            error = "仓库格式应为 owner/repo，或完整的 GitHub 仓库地址";
            return false;
        }
        repo = parts[0] + "/" + parts[1];
        return true;
    }

    // ---------------- 检查入口 ----------------

    /// <param name="force">true = 用户手动点击，忽略 6 小时节流（仍会带 ETag 走条件请求）。</param>
    public static async Task<UpdateCheckResult> CheckAsync(bool force = false, CancellationToken ct = default)
    {
        if (!TryNormalizeRepo(AppServices.User.UpdateRepo, out var repo, out var repoError))
        {
            AppLog.Write("update.log", $"仓库配置非法：{repoError}");
            return new UpdateCheckResult
            {
                Status = UpdateStatus.Failed,
                CurrentVersion = CurrentVersion,
                Repo = repo,
                Message = repoError,
                CheckedAt = DateTimeOffset.Now
            };
        }

        // 自检注入：伪造最新 Release，用于验证「发现新版本」的界面与按钮。
        if (_simulatedLatest is { Length: > 0 } fake &&
            AppVersion.TryParse(fake, out var fakeVersion))
        {
            var simulated = new ReleaseInfo(
                "v" + fakeVersion,
                "v" + fakeVersion + "（模拟）",
                ReleasesPageUrl(repo),
                DateTimeOffset.Now,
                "这是 --fake-update 注入的模拟新版本，仅用于验证应用内更新界面，不会真的下载或安装。",
                false,
                Array.Empty<ReleaseAsset>());
            var r = BuildResult(simulated, repo, DateTimeOffset.Now, false) with { Simulated = true };
            AppLog.Write("update.log", $"SIMULATED latest={fakeVersion} status={r.Status}");
            return r;
        }

        var cache = LoadCache();
        var sameRepo = cache is not null &&
                       string.Equals(cache.Repo, repo, StringComparison.OrdinalIgnoreCase);
        var now = DateTimeOffset.Now;

        if (!force && sameRepo && cache!.CheckedAt > DateTimeOffset.MinValue &&
            now - cache.CheckedAt < AutoCheckInterval &&
            (cache.RawJson is not null || cache.NoRelease))
        {
            var hit = BuildCachedResult(cache, repo);
            AppLog.Write("update.log", $"THROTTLE repo={repo} age={(now - cache.CheckedAt).TotalMinutes:0}min 复用缓存 status={hit.Status}");
            return hit;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"StarVideo/{CurrentVersion}");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl(repo));
            // force 只跳过「6 小时时间节流」，仍然带 ETag：GitHub 对条件请求返回 304 时不计入限额，
            // 缓存里保存的就是上次完整响应，结果完全等价。
            if (sameRepo && !string.IsNullOrEmpty(cache?.ETag))
                request.Headers.TryAddWithoutValidation("If-None-Match", cache.ETag);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)response.StatusCode;
            var remaining = Header(response, "X-RateLimit-Remaining");
            var etag = response.Headers.ETag?.Tag;

            if (response.StatusCode == HttpStatusCode.NotModified && sameRepo && cache?.RawJson is not null)
            {
                cache.CheckedAt = now;
                cache.ETag = etag ?? cache.ETag;
                SaveCache(cache);
                var cached = BuildCachedResult(cache, repo);
                AppLog.Write("update.log", $"HTTP 304 repo={repo} 复用缓存 status={cached.Status} remaining={remaining}");
                return cached;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // GitHub 对「仓库不存在」与「仓库还没有 Release」都返回 404，靠 body 里的 message 区分：
                //   Release not found -> 确实没有发布；Not Found -> 仓库地址写错 / 私有仓库无权限。
                var body = await response.Content.ReadAsStringAsync(ct);
                var noRelease = body.Contains("Release not found", StringComparison.OrdinalIgnoreCase);
                if (noRelease)
                    SaveCache(new UpdateCache { Repo = repo, CheckedAt = now, NoRelease = true });

                var message = noRelease
                    ? "该仓库还没有发布任何 Release"
                    : "仓库不存在或无访问权限（HTTP 404），请检查「GitHub 仓库」配置";
                var brief = body.Replace("\r", " ").Replace("\n", " ");
                if (brief.Length > 160) brief = brief[..160];
                AppLog.Write("update.log",
                    $"HTTP 404 repo={repo} noRelease={noRelease} body={brief} remaining={remaining}");
                return new UpdateCheckResult
                {
                    Status = noRelease ? UpdateStatus.NoRelease : UpdateStatus.Failed,
                    CurrentVersion = CurrentVersion,
                    Repo = repo,
                    Message = message,
                    CheckedAt = now
                };
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                var reset = RateLimitResetText(response);
                var msg = $"GitHub API 限额已用尽（未认证 60 次/小时）{reset}";
                AppLog.Write("update.log", $"HTTP {code} 限流 repo={repo} {msg}");
                return Fallback(cache, sameRepo, repo, msg, now);
            }

            if (!response.IsSuccessStatusCode)
            {
                var msg = $"GitHub API 返回 {code} {response.ReasonPhrase}";
                AppLog.Write("update.log", $"HTTP {code} repo={repo} {msg}");
                return Fallback(cache, sameRepo, repo, msg, now);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var release = ParseRelease(json, repo);
            if (release is null)
            {
                var msg = "Release 数据格式无法解析";
                AppLog.Write("update.log", $"PARSE FAIL repo={repo}");
                return Fallback(cache, sameRepo, repo, msg, now);
            }

            SaveCache(new UpdateCache { Repo = repo, CheckedAt = now, ETag = etag, RawJson = json });
            var result = BuildResult(release, repo, now, false);
            AppLog.Write("update.log",
                $"HTTP 200 repo={repo} tag={release.Tag} current={CurrentVersion} " +
                $"status={result.Status} assets={release.Assets.Count} remaining={remaining}");
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = ex is HttpRequestException ? $"网络不可用：{ex.Message}" : $"检查失败：{ex.Message}";
            AppLog.Write("update.log", $"EXCEPTION repo={repo} {ex.GetType().Name}: {ex.Message}");
            return Fallback(cache, sameRepo, repo, msg, now);
        }
    }

    // ---------------- 结果构造 ----------------

    private static UpdateCheckResult BuildCachedResult(UpdateCache cache, string repo)
    {
        if (cache.NoRelease)
        {
            return new UpdateCheckResult
            {
                Status = UpdateStatus.NoRelease,
                CurrentVersion = CurrentVersion,
                Repo = repo,
                Message = "该仓库还没有发布任何 Release",
                CheckedAt = cache.CheckedAt,
                FromCache = true
            };
        }

        if (cache.RawJson is not null && ParseRelease(cache.RawJson, repo) is { } release)
            return BuildResult(release, repo, cache.CheckedAt, true);

        return new UpdateCheckResult
        {
            Status = UpdateStatus.Failed,
            CurrentVersion = CurrentVersion,
            Repo = repo,
            Message = "本地缓存不完整，请手动重新检查",
            CheckedAt = cache.CheckedAt,
            FromCache = true
        };
    }

    private static UpdateCheckResult Fallback(UpdateCache? cache, bool sameRepo, string repo,
                                              string reason, DateTimeOffset now)
    {
        if (sameRepo && cache is not null && (cache.RawJson is not null || cache.NoRelease))
        {
            var cached = BuildCachedResult(cache, repo);
            return cached with { Message = cached.Message + $"（{reason}，显示上次结果）" };
        }
        return new UpdateCheckResult
        {
            Status = UpdateStatus.Failed,
            CurrentVersion = CurrentVersion,
            Repo = repo,
            Message = reason,
            CheckedAt = now
        };
    }

    private static UpdateCheckResult BuildResult(ReleaseInfo release, string repo,
                                                 DateTimeOffset checkedAt, bool fromCache)
    {
        var current = CurrentVersion;
        if (!AppVersion.TryParse(release.Tag, out var latest))
        {
            return new UpdateCheckResult
            {
                Status = UpdateStatus.Failed,
                CurrentVersion = current,
                Repo = repo,
                Release = release,
                LatestVersion = release.Tag,
                Message = $"Release 标签「{release.Tag}」不是可识别的版本号（建议用 v1.2.3）",
                CheckedAt = checkedAt,
                FromCache = fromCache
            };
        }

        AppVersion.TryParse(current, out var currentParsed);
        var newer = AppVersion.Compare(latest, currentParsed) > 0;
        var status = newer ? UpdateStatus.UpdateAvailable : UpdateStatus.UpToDate;
        var message = newer
            ? $"发现新版本 v{latest}（当前 v{currentParsed}）"
            : $"已是最新版本 v{currentParsed}";
        if (fromCache) message += "  ·  来自本地缓存";

        return new UpdateCheckResult
        {
            Status = status,
            CurrentVersion = current,
            Repo = repo,
            Release = release,
            LatestVersion = latest.ToString(),
            Message = message,
            CheckedAt = checkedAt,
            FromCache = fromCache
        };
    }

    private static ReleaseInfo? ParseRelease(string json, string repo)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = Str(root, "tag_name");
            if (string.IsNullOrEmpty(tag)) return null;

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
                    var name = Str(a, "name");
                    var url = Str(a, "browser_download_url");
                    if (name.Length > 0 && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        assets.Add(new ReleaseAsset(name, Num(a, "size"), url));
                }
            }

            var published = DateTimeOffset.TryParse(Str(root, "published_at"),
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
                ? dt
                : DateTimeOffset.MinValue;

            return new ReleaseInfo(
                tag,
                Str(root, "name") is { Length: > 0 } n ? n : tag,
                Str(root, "html_url") is { Length: > 0 } h ? h : ReleasesPageUrl(repo),
                published,
                Str(root, "body"),
                root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True,
                assets);
        }
        catch (Exception ex)
        {
            AppLog.Write("update.log", $"JSON 解析失败：{ex.Message}");
            return null;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object &&
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";

    private static long Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object &&
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v)
            ? v
            : 0;

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : "?";

    private static string RateLimitResetText(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), out var unix) && unix > 0)
        {
            try
            {
                var local = DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime();
                return $"，约 {local:HH:mm} 后恢复";
            }
            catch { /* 时间戳异常就省略 */ }
        }
        return "";
    }

    // ---------------- 本地缓存 ----------------

    private sealed class UpdateCache
    {
        public string Repo { get; set; } = "";
        public string? ETag { get; set; }
        /// <summary>保存 GitHub 原始响应，304 时可直接复用解析，避免 DTO 映射丢失字段。</summary>
        public string? RawJson { get; set; }
        public bool NoRelease { get; set; }
        public DateTimeOffset CheckedAt { get; set; }
    }

    private static string CachePath => Path.Combine(AppLog.DirectoryPath, "update-cache.json");

    private static UpdateCache? LoadCache()
    {
        try
        {
            return File.Exists(CachePath)
                ? JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(CachePath))
                : null;
        }
        catch (Exception ex)
        {
            AppLog.Write("update.log", $"缓存读取失败（忽略）：{ex.Message}");
            return null;
        }
    }

    private static void SaveCache(UpdateCache cache)
    {
        try
        {
            Directory.CreateDirectory(AppLog.DirectoryPath);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cache));
        }
        catch (Exception ex)
        {
            AppLog.Write("update.log", $"缓存写入失败（忽略）：{ex.Message}");
        }
    }

    /// <summary>只读本地缓存，不发起任何网络请求；供设置页打开时展示上次结果。</summary>
    public static UpdateCheckResult? PeekCachedResult()
    {
        if (!TryNormalizeRepo(AppServices.User.UpdateRepo, out var repo, out _)) return null;
        var cache = LoadCache();
        if (cache is null || !string.Equals(cache.Repo, repo, StringComparison.OrdinalIgnoreCase) ||
            (cache.RawJson is null && !cache.NoRelease))
            return null;
        return BuildCachedResult(cache, repo);
    }

    /// <summary>清空缓存，用于「强制重新检查」或切换仓库后避免误用旧结果。</summary>
    public static void ClearCache()
    {
        try { if (File.Exists(CachePath)) File.Delete(CachePath); }
        catch (Exception ex) { AppLog.Write("update.log", $"缓存清理失败（忽略）：{ex.Message}"); }
    }

    // ---------------- 下载 / 打开 ----------------

    /// <summary>下载 Release 附件到 %LOCALAPPDATA%\StarVideo\Updates\{tag}\，不覆盖正在运行的程序。</summary>
    public static async Task<string> DownloadAssetAsync(ReleaseAsset asset, string tag,
                                                        IProgress<double>? progress = null,
                                                        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("下载地址无效");

        var safeTag = string.Concat(tag.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim('.');
        var fileName = Path.GetFileName(asset.Name);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "update.bin";
        var dir = Path.Combine(AppLog.DirectoryPath, "Updates", safeTag);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, fileName);
        var part = target + ".part";

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"StarVideo/{CurrentVersion}");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var dest = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            readTotal += read;
            if (total > 0) progress?.Report(Math.Min(1.0, readTotal / (double)total));
        }
        dest.Close();

        if (File.Exists(target)) File.Delete(target);
        File.Move(part, target);
        progress?.Report(1.0);
        AppLog.Write("update.log", $"DOWNLOAD OK {fileName} {readTotal} bytes -> {target}");
        return target;
    }

    /// <summary>用默认浏览器打开发布页 / Release 页面。</summary>
    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("链接无效");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>在资源管理器里定位已下载的文件。</summary>
    public static void RevealFile(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
}
