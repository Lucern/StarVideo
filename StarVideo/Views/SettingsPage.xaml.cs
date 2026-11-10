using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using StarVideo.Services;

namespace StarVideo.Views;

/// <summary>设置：主题、播放偏好、本地数据管理与关于信息。</summary>
public sealed partial class SettingsPage : Page
{
    private bool _loading;
    private bool _checkingUpdate;
    private UpdateCheckResult? _updateResult;
    private ReleaseAsset? _downloadAsset;

    public SettingsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loading = true;
        ThemeCombo.SelectedIndex = AppServices.User.Theme == "Light" ? 1 : 0;
        AutoPlaySwitch.IsOn = AppServices.User.AutoPlayNext;
        ForceFFmpegSwitch.IsOn = AppServices.User.ForceFFmpeg;
        FFmpegPathBox.Text = AppServices.User.FFmpegPath;
        AutoUpdateSwitch.IsOn = AppServices.User.AutoCheckUpdates;
        UpdateRepoBox.Text = AppServices.User.UpdateRepo;
        _loading = false;
        RefreshStats();
        RefreshFFmpeg();

        CurrentVersionText.Text = "v" + UpdateService.CurrentVersion;
        AboutVersionText.Text = $"星影视频  v{UpdateService.CurrentVersion}";

        // --fake-update 自检模式：打开设置页即展示「发现新版本」界面，便于无鼠标验证。
        // 正常启动只读取本地缓存，不会发起网络请求。
        if (UpdateService.SimulatedLatestVersion is not null)
            _ = CheckForUpdatesAsync(force: true);
        else
            RenderUpdateResult(UpdateService.PeekCachedResult());
    }

    private static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarVideo");

    private void RefreshStats()
    {
        var catalog = AppServices.Catalog;
        var user = AppServices.User;
        StatsText.Text = $"片库 {catalog.All.Count} 部作品  ·  收藏 {user.Favorites(catalog).Count} 部  ·  " +
                         $"观看记录 {user.History(catalog).Count} 条";
        PathText.Text = $"用户数据：{Path.Combine(DataDirectory, "user.json")}";
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var theme = ThemeCombo.SelectedIndex == 1 ? "Light" : "Dark";
        AppServices.User.Theme = theme;
        App.MainWindow?.ApplyTheme(theme);
        App.MainWindow?.ShowToast(theme == "Light" ? "已切换到浅色主题" : "已切换到深色主题");
    }

    private void OnAutoPlayToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppServices.User.AutoPlayNext = AutoPlaySwitch.IsOn;
    }

    // ---------------- 版本更新 ----------------

    /// <summary>把一次检查结果渲染到设置页；不联网、不改缓存。</summary>
    private void RenderUpdateResult(UpdateCheckResult? result)
    {
        _updateResult = result;
        if (result is null)
        {
            UpdateStatusText.Text = "尚未检查更新，点「检查更新」与仓库 Release 版本号比对。";
            LatestVersionText.Text = "-";
            LastCheckText.Text = "-";
            UpdateNoticeBorder.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateStatusText.Text = result.Message + (result.Simulated ? "  ·  模拟数据" : "");
        LatestVersionText.Text = result.LatestVersion is { } v
            ? "v" + v
            : result.Release?.Tag ?? "-";
        LastCheckText.Text = result.CheckedAt > DateTimeOffset.MinValue
            ? result.CheckedAt.ToLocalTime().ToString("MM-dd HH:mm")
            : "-";

        var showNotice = result.IsUpdateAvailable && result.Release is not null;
        UpdateNoticeBorder.Visibility = showNotice ? Visibility.Visible : Visibility.Collapsed;
        if (!showNotice) return;

        var release = result.Release!;
        NewVersionTitle.Text = $"新版本 {release.Name} 已发布";
        NewVersionMeta.Text = $"标签 {release.Tag}  ·  " +
                              (release.PublishedAt > DateTimeOffset.MinValue
                                  ? release.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                                  : "发布时间未知") +
                              (release.Prerelease ? "  ·  预发布" : "");

        var notes = (release.Notes ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        ReleaseNotesText.Text = notes.Length > 320 ? notes[..320] + "…" : notes;
        ReleaseNotesText.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _downloadAsset = PickPreferredAsset(release.Assets);
        DownloadAssetButton.Visibility = _downloadAsset is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_downloadAsset is not null)
            DownloadAssetButton.Content = $"下载 {_downloadAsset.Name}（{_downloadAsset.Size / 1024.0 / 1024.0:0.0} MB）";
    }

    /// <summary>优先挑可安装的包，跳过校验文件 / 说明文本。</summary>
    private static ReleaseAsset? PickPreferredAsset(IReadOnlyList<ReleaseAsset> assets)
    {
        if (assets.Count == 0) return null;
        var skip = new[] { ".sha256", ".sig", ".asc", ".txt", ".md", ".json" };
        bool Ignored(ReleaseAsset a) => skip.Any(ext =>
            a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        return assets.FirstOrDefault(a => !Ignored(a) &&
                                          (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                           a.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                                           a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)))
               ?? assets.FirstOrDefault(a => !Ignored(a) &&
                                             a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
               ?? assets.FirstOrDefault(a => !Ignored(a))
               ?? assets[0];
    }

    /// <summary>启动时静默检查完成后，如果设置页正开着就即时刷新。</summary>
    public void OnStartupUpdateChecked(UpdateCheckResult result) => RenderUpdateResult(result);

    private async void OnCheckUpdate(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(force: true);

    private async Task CheckForUpdatesAsync(bool force)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        UpdateRing.IsActive = true;
        UpdateStatusText.Text = "正在读取 GitHub Releases …";

        try
        {
            var result = await UpdateService.CheckAsync(force);
            RenderUpdateResult(result);
            App.MainWindow?.ShowToast(result.Message);
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"检查失败：{ex.Message}";
            App.MainWindow?.ShowToast($"检查更新失败：{ex.Message}");
        }
        finally
        {
            _checkingUpdate = false;
            UpdateRing.IsActive = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OnAutoUpdateToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppServices.User.AutoCheckUpdates = AutoUpdateSwitch.IsOn;
    }

    private async void OnApplyUpdateRepo(object sender, RoutedEventArgs e)
    {
        var text = UpdateRepoBox.Text.Trim();
        if (!UpdateService.TryNormalizeRepo(text, out var repo, out var error))
        {
            App.MainWindow?.ShowToast(error);
            return;
        }

        // 换仓库后必须丢弃旧缓存（里面存的是上一个仓库的 Release 与 ETag）。
        UpdateService.ClearCache();
        AppServices.User.UpdateRepo =
            string.Equals(repo, UpdateService.DefaultRepo, StringComparison.OrdinalIgnoreCase) ? "" : repo;
        UpdateRepoBox.Text = AppServices.User.UpdateRepo;
        App.MainWindow?.ShowToast($"更新源已切换为 {repo}");
        await CheckForUpdatesAsync(force: true);
    }

    private void OnOpenRelease(object sender, RoutedEventArgs e)
    {
        var url = _updateResult?.Release?.HtmlUrl;
        if (string.IsNullOrEmpty(url)) { OnOpenAllReleases(sender, e); return; }
        OpenExternal(url);
    }

    private void OnOpenAllReleases(object sender, RoutedEventArgs e)
    {
        UpdateService.TryNormalizeRepo(AppServices.User.UpdateRepo, out var repo, out _);
        OpenExternal(UpdateService.ReleasesPageUrl(repo));
    }

    private static void OpenExternal(string url)
    {
        try { UpdateService.OpenUrl(url); }
        catch (Exception ex) { App.MainWindow?.ShowToast($"打开链接失败：{ex.Message}"); }
    }

    private async void OnDownloadAsset(object sender, RoutedEventArgs e)
    {
        if (_downloadAsset is null || _updateResult?.Release is null) return;
        DownloadAssetButton.IsEnabled = false;
        DownloadProgressBar.Visibility = Visibility.Visible;
        DownloadProgressBar.Value = 0;
        DownloadStatusText.Visibility = Visibility.Visible;
        DownloadStatusText.Text = $"正在下载 {_downloadAsset.Name} …";

        try
        {
            var progress = new Progress<double>(p => DownloadProgressBar.Value = p);
            var path = await UpdateService.DownloadAssetAsync(
                _downloadAsset, _updateResult.Release.Tag, progress);
            DownloadStatusText.Text = $"已保存到 {path}（不会自动安装）";
            App.MainWindow?.ShowToast("更新包已下载，已打开所在文件夹");
            try { UpdateService.RevealFile(path); } catch { /* 定位失败不影响下载结果 */ }
        }
        catch (Exception ex)
        {
            DownloadStatusText.Text = $"下载失败：{ex.Message}";
            App.MainWindow?.ShowToast($"下载失败：{ex.Message}");
        }
        finally
        {
            DownloadAssetButton.IsEnabled = true;
        }
    }

    // ---------------- FFmpeg ----------------

    private void RefreshFFmpeg()
    {
        FFmpegStatusText.Text = FFmpegService.StatusText +
                                (FFmpegService.HasProbe ? "  ·  ffprobe 已就绪" : "  ·  ffprobe 缺失");
        CacheSizeText.Text = $"转码缓存 {FFmpegService.CacheSize() / 1024.0 / 1024.0:0.0} MB";
    }

    private void OnForceFFmpegToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppServices.User.ForceFFmpeg = ForceFFmpegSwitch.IsOn;
    }

    private void OnApplyFFmpegPath(object sender, RoutedEventArgs e)
    {
        var text = FFmpegPathBox.Text.Trim().Trim('"');
        AppServices.User.FFmpegPath = text;
        RefreshFFmpeg();
        App.MainWindow?.ShowToast(string.IsNullOrEmpty(text)
            ? "已恢复使用随程序发布的 FFmpeg"
            : (FFmpegService.IsAvailable ? "FFmpeg 路径已生效" : "该目录下没有找到 ffmpeg.exe，请检查路径"));
    }

    private void OnOpenFFmpegFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = FFmpegService.BundledDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowToast($"打开目录失败：{ex.Message}");
        }
    }

    private async void OnClearTranscodeCache(object sender, RoutedEventArgs e)
    {
        if (!await Confirm("清空转码缓存", "将删除所有已转换的临时 MP4 与视频缩略图，下次播放需要重新转码。")) return;
        var freed = FFmpegService.ClearCache();
        RefreshFFmpeg();
        App.MainWindow?.ShowToast($"已释放 {freed / 1024.0 / 1024.0:0.0} MB 缓存");
    }

    private async void OnClearHistory(object sender, RoutedEventArgs e)
    {
        if (!await Confirm("清空观看记录", "将删除全部观看进度与继续观看记录，此操作不可撤销。")) return;
        AppServices.User.ClearHistory();
        RefreshStats();
        App.MainWindow?.ShowToast("观看记录已清空");
    }

    private async void OnClearFavorites(object sender, RoutedEventArgs e)
    {
        if (!await Confirm("清空收藏", "将删除全部收藏作品，此操作不可撤销。")) return;
        AppServices.User.ClearFavorites();
        RefreshStats();
        App.MainWindow?.ShowToast("收藏已清空");
    }

    private async Task<bool> Confirm(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "确认清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", DataDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.MainWindow?.ShowToast($"打开目录失败：{ex.Message}");
        }
    }
}
