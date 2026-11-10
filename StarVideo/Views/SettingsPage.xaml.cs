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

    public SettingsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _loading = true;
        ThemeCombo.SelectedIndex = AppServices.User.Theme == "Light" ? 1 : 0;
        AutoPlaySwitch.IsOn = AppServices.User.AutoPlayNext;
        ForceFFmpegSwitch.IsOn = AppServices.User.ForceFFmpeg;
        FFmpegPathBox.Text = AppServices.User.FFmpegPath;
        _loading = false;
        RefreshStats();
        RefreshFFmpeg();
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
