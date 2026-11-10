using Microsoft.UI.Xaml;

namespace StarVideo;

/// <summary>
/// 应用入口。全局服务与窗口引用放在这里，页面通过 AppServices / App.MainWindow 取用。
/// --smoke 依次打开所有页面做运行时自检；--selftest 校验 Tab / 滚动 / FFmpeg / 在线片源。
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private DispatcherTimer? _smokeTimer;
    private DispatcherTimer? _updateTimer;

    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services.AppServices.Initialize();

        _window = new MainWindow();
        MainWindow = (MainWindow)_window;
        _window.Activate();

        var cli = Environment.GetCommandLineArgs();

        // 开发用：伪造最新 Release 版本号，用来验证「发现新版本」界面（不发起真实请求）。
        if (cli.FirstOrDefault(a => a.StartsWith("--fake-update=", StringComparison.OrdinalIgnoreCase)) is { } fake)
            Services.UpdateService.SimulatedLatestVersion = fake.Substring("--fake-update=".Length);

        if (cli.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
            RunSelfTest();
        else if (cli.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase)))
            RunSmokeTest();
        else if (cli.Any(a => string.Equals(a, "--checkupdate", StringComparison.OrdinalIgnoreCase)))
            RunUpdateCheck();
        else if (cli.FirstOrDefault(a => a.StartsWith("--goto=", StringComparison.OrdinalIgnoreCase)) is { } go)
            OpenPreviewPage(go.Substring(7));
        else
            ScheduleUpdateCheck();
    }

    /// <summary>
    /// 正常双击 exe 启动时静默比对仓库 Release 版本号。
    /// 所有 --xxx 命令行模式（自检 / 预览 / --checkupdate）都不做自动检查，避免干扰自动化。
    /// </summary>
    private void ScheduleUpdateCheck()
    {
        if (!Services.AppServices.User.AutoCheckUpdates) return;

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Stop();
            try
            {
                var result = await Services.UpdateService.CheckAsync(force: false);
                var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                queue?.TryEnqueue(() =>
                {
                    MainWindow?.RefreshSettingsUpdate(result);
                    if (result.IsUpdateAvailable && result.LatestVersion is { } version &&
                        !string.Equals(version, Services.AppServices.User.LastNotifiedVersion,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Services.AppServices.User.LastNotifiedVersion = version;
                        var message = $"发现新版本 v{version}，前往「设置 → 版本更新」查看";
                        Services.AppLog.Write("update.log", $"TOAST {message}");
                        MainWindow?.ShowToast(message);
                    }
                });
            }
            catch (Exception ex)
            {
                Services.AppLog.Write("update.log", $"启动检查异常：{ex.Message}");
            }
        };
        _updateTimer.Start();
    }

    /// <summary>开发用：无界面跑一次真实检查并写日志后退出，便于 CI / 自动化取证。</summary>
    private async void RunUpdateCheck()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StarVideo", "update-check.log");
        try
        {
            await Task.Delay(300);
            var r = await Services.UpdateService.CheckAsync(force: true);
            File.WriteAllText(path,
                $"[StarVideo] 更新检查 {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                $"repo={r.Repo}\r\n" +
                $"current={r.CurrentVersion}\r\n" +
                $"latest={r.LatestVersion ?? r.Release?.Tag ?? "-"}\r\n" +
                $"status={r.Status}\r\n" +
                $"message={r.Message}\r\n" +
                $"from_cache={r.FromCache}\r\n" +
                $"simulated={r.Simulated}\r\n" +
                $"assets={r.Release?.Assets.Count ?? 0}\r\n" +
                "UPDATECHECK DONE\r\n");
        }
        catch (Exception ex)
        {
            File.WriteAllText(path, "UPDATECHECK FAIL\r\n" + ex);
        }
        _window?.Close();
    }

    /// <summary>开发用：完整功能自检，结果写 selftest.log 后自动退出。</summary>
    private async void RunSelfTest()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StarVideo", "selftest.log");
        try
        {
            await Task.Delay(800); // 等窗口完成首次布局
            var text = await Services.SelfTest.RunAsync(MainWindow!);
            File.WriteAllText(path, text + "SELFTEST DONE\r\n");
        }
        catch (Exception ex)
        {
            File.WriteAllText(path, "SELFTEST FAIL\r\n" + ex);
        }
        _window?.Close();
    }

    /// <summary>开发用：启动后直接打开指定页面，方便截图检查。</summary>
    private void OpenPreviewPage(string name)
    {
        var item = Services.AppServices.Catalog.Banners.First();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            switch (name.ToLowerInvariant())
            {
                case "detail": MainWindow?.OpenDetail(item); break;
                case "player": MainWindow?.OpenPlayer(item); break;
                case "online": MainWindow?.OpenOnlinePlayer(); break;
                // 自检用：不经鼠标直接触发本地文件选择，便于自动化验证 picker 与播放链路
                case "pick": MainWindow?.OpenLocalPickerForTest(); break;
                case "search": MainWindow?.OpenSearch("都市"); break;
                case "library": MainWindow?.OpenLibrary(0); break;
                case "rank": MainWindow?.NavigateTo("rank"); break;
                case "channel": MainWindow?.NavigateTo("电视剧", "电视剧"); break;
                case "settings": MainWindow?.NavigateTo("settings"); break;
            }
        };
        timer.Start();
    }

    // ---------------- 页面自检（开发用） ----------------

    private void RunSmokeTest()
    {
        var catalog = Services.AppServices.Catalog;
        var first = catalog.All.First();
        var steps = new (string Target, object? Parameter)[]
        {
            ("home", null),
            ("电视剧", "电视剧"),
            ("电影", "电影"),
            ("rank", null),
            ("search", "都市"),
            ("detail", first),
            ("player", new Models.PlayerArgs { Item = first, Episode = 1 }),
            ("library", 0),
            ("library", 1),
            ("settings", null),
            ("home", null)
        };

        var log = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StarVideo", "smoke.log");
        File.WriteAllText(log, $"[StarVideo] 页面自检 {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n");

        var index = 0;
        _smokeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _smokeTimer.Tick += (_, _) =>
        {
            if (index >= steps.Length)
            {
                _smokeTimer.Stop();
                File.AppendAllText(log, "SMOKE OK: 所有页面均已成功导航\r\n");
                _window?.Close();
                return;
            }

            var (target, parameter) = steps[index++];
            try
            {
                MainWindow?.NavigateTo(target, parameter);
                File.AppendAllText(log, $"OK   {target}\r\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(log, $"FAIL {target}: {ex}\r\n");
            }
        };
        _smokeTimer.Start();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // 演示项目：避免一个非致命异常直接让整个窗口消失，同时把异常落盘便于排查。
        System.Diagnostics.Debug.WriteLine($"[StarVideo] {e.Exception}");
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarVideo");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\r\n");
        }
        catch { /* 日志写入失败不应影响主流程 */ }
        e.Handled = true;
    }
}
