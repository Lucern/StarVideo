using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StarVideo.Controls;
using StarVideo.Models;
using StarVideo.Services;
using StarVideo.Views;
using Windows.Graphics;
using WinRT.Interop;

namespace StarVideo;

/// <summary>
/// 应用外壳：自绘标题栏 + 顶部频道导航 + 内容 Frame。
/// 所有页面通过 App.MainWindow 上的便捷方法互相跳转。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly List<NavTab> _tabs = new();
    private readonly DispatcherTimer _toastTimer;
    private bool _acrylicEnabled;
    private bool _lightTheme;
    private bool _immersive;

    private TrayIconService? _tray;
    private IntPtr _hwnd;
    private bool _allowClose;
    private bool _trayCloseEnabled;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureWindow();

        foreach (var tab in TabPanel.Children.OfType<NavTab>())
        {
            tab.Clicked += OnTabClicked;
            _tabs.Add(tab);
        }

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _toastTimer.Tick += (_, _) => { Toast.IsOpen = false; _toastTimer.Stop(); };

        ApplyTheme(AppServices.User.Theme);
        NavigateTo("home");

        RootGrid.Loaded += (_, _) => { ApplyResponsiveLayout(); UpdateTabScrollButtons(); };
    }

    /// <summary>
    /// 标题栏各列都是 Auto，窗口变窄时总宽会超出可视区，进而把导航行与页面内容一起挤歪。
    /// 这里按可用宽度收起次要控件，保证任何窗口尺寸下列宽之和都小于窗口宽度。
    /// </summary>
    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        var w = RootGrid.ActualWidth;
        if (w <= 0) return;

        var ultra = w < 620;   // 极窄窗口：再收起品牌文字与前进按钮，避免系统按钮区被压住
        var compact = w < 760;
        var medium = w < 1000;
        SearchBox.Width = ultra ? 88 : compact ? 128 : medium ? 176 : 240;
        LogoText.Visibility = ultra ? Visibility.Collapsed : Visibility.Visible;
        ForwardButton.Visibility = ultra ? Visibility.Collapsed : Visibility.Visible;
        LibraryButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HistoryButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        VipButton.Visibility = medium ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ConfigureWindow()
    {
        Title = "星影视频";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        // 窗口级 AcrylicThin 半透明材质。XAML 内容层只铺很淡的半透明底色，
        // 否则不透明的 AppBackgroundBrush 会把材质完全盖住。
        // 远程桌面 / 关闭透明效果 / 高对比度等场景 IsSupported=false，自动退回纯色。
        try
        {
            if (ThinAcrylicBackdrop.IsSupported)
            {
                SystemBackdrop = new ThinAcrylicBackdrop();
                _acrylicEnabled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StarVideo] 亚克力背景降级: {ex.Message}");
        }

        try
        {
            ApplyCaptionButtonColors();
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "app.ico"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StarVideo] 标题栏配置降级: {ex.Message}");
        }

        try
        {
            // DisplayArea.WorkArea 在部分高 DPI / 多显示器环境下会返回异常小的值，
            // 直接用屏幕物理像素计算窗口尺寸，避免窗口被挤成窄条后标题栏列溢出。
            var screenW = GetSystemMetrics(0); // SM_CXSCREEN
            var screenH = GetSystemMetrics(1); // SM_CYSCREEN
            var w = Math.Min(1600, Math.Max(1200, screenW - 160));
            var h = Math.Min(940, Math.Max(760, screenH - 160));
            AppWindow.Resize(new SizeInt32(w, h));
            AppWindow.Move(new PointInt32(
                Math.Max(0, (screenW - w) / 2), Math.Max(0, (screenH - h) / 2)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StarVideo] 窗口定位降级: {ex.Message}");
        }

        SetupTray();
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    // ---------------- 系统托盘 ----------------
    // 关闭拦截用 AppWindow.Closing（可 Cancel），比子类化 WndProc 拦 WM_CLOSE 稳定；
    // 托盘图标消息仍由 TrayIconService 的窗口子类接收。窗口只隐藏不销毁，
    // 所以 Frame 的页面、播放进度、滚动位置在恢复后原样保留。

    private void SetupTray()
    {
        _hwnd = WindowNative.GetWindowHandle(this);

        // 自动退出的无界面模式（自检 / 冒烟 / 更新检查）需要"点 X 直接退出"；
        // 其余（含 --goto= 页面预览）都保留正常的托盘关闭行为，方便验证。
        var cli = Environment.GetCommandLineArgs().Skip(1).ToArray();
        _trayCloseEnabled = !cli.Any(a =>
            string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--checkupdate", StringComparison.OrdinalIgnoreCase));

        try
        {
            AppWindow.Closing += OnAppWindowClosing;
            _tray = new TrayIconService(_hwnd, RestoreFromTray, ExitFromTray);
            Closed += (_, _) => _tray?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Write("tray.log", $"托盘初始化失败，关闭行为回退为直接退出：{ex}");
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || !_trayCloseEnabled || _tray is null) return;
        args.Cancel = true;      // 不真的关窗，只隐藏到托盘
        MinimizeToTray();
    }

    private void MinimizeToTray()
    {
        var page = ContentFrame.CurrentSourcePageType?.Name ?? "null";
        // 在播放页关闭：先暂停，避免窗口藏起来后声音还在后台继续播。
        if (ContentFrame.Content is PlayerPage player) player.PauseForTray();

        _tray?.EnsureIcon();
        ShowWindow(_hwnd, SW_HIDE);   // 同时从任务栏移除；窗口未销毁，页面内容不变
        _tray?.ShowFirstMinimizeHint();
        AppLog.Write("tray.log", $"关闭到托盘：保留页面={page}，窗口已隐藏");
    }

    /// <summary>托盘双击 / 右键「打开星影视频」：原样恢复关闭前的窗口与页面。</summary>
    public void RestoreFromTray()
    {
        ShowWindow(_hwnd, SW_SHOW);
        SetForegroundWindow(_hwnd);
        try { Activate(); } catch { /* 已经在前台时忽略 */ }
        var page = ContentFrame.CurrentSourcePageType?.Name ?? "null";
        if (ContentFrame.Content is PlayerPage player) player.ResumeFromTray();
        AppLog.Write("tray.log", $"从托盘恢复：页面={page}，窗口已显示");
    }

    /// <summary>托盘右键「退出」：真正结束应用。</summary>
    public void ExitFromTray()
    {
        _allowClose = true;
        _tray?.Dispose();
        _tray = null;

        // 这个回调可能来自右键菜单的模态循环，直接销毁窗口容易重入；
        // 排队回正常消息循环再关窗。
        DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
        {
            try { Close(); } catch { /* 已在关闭中 */ }
            try { Microsoft.UI.Xaml.Application.Current.Exit(); } catch { /* 忽略 */ }
        });

        // 部分非打包 WinUI 应用在窗口关闭后不会自行退出消息循环（或 UI 线程被菜单占用），
        // 兜底强制结束进程；进程若已正常退出，这个任务自然不会执行。
        _ = Task.Run(async () => { await Task.Delay(1200); Environment.Exit(0); });
    }

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    // ---------------- 导航 ----------------

    public void NavigateTo(string target, object? parameter = null)
    {
        var type = Resolve(target);
        if (ContentFrame.CurrentSourcePageType == type && parameter is null) return;

        if (parameter is null) ContentFrame.Navigate(type);
        else ContentFrame.Navigate(type, parameter);
    }

    private static Type Resolve(string target) => target switch
    {
        "home" => typeof(HomePage),
        "rank" => typeof(RankPage),
        "search" => typeof(SearchPage),
        "detail" => typeof(DetailPage),
        "player" => typeof(PlayerPage),
        "library" => typeof(LibraryPage),
        "settings" => typeof(SettingsPage),
        _ => typeof(ChannelPage)   // 其余一律视为频道分类名
    };

    public void OpenDetail(MediaItem item) => NavigateTo("detail", item);

    public void OpenPlayer(MediaItem item, int episode = 1) =>
        NavigateTo("player", new PlayerArgs { Item = item, Episode = episode });

    /// <summary>开发自检：不经过鼠标点击，直接触发播放页的「打开本地视频文件」。</summary>
    public void OpenLocalPickerForTest()
    {
        NavigateTo("player");
        DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            (ContentFrame.Content as PlayerPage)?.OpenLocalFileFromCode());
    }

    /// <summary>首页「在线试播」：直接进入播放页并播放在线测试视频。</summary>
    public void OpenOnlinePlayer(Services.OnlineSource? source = null) =>
        NavigateTo("player", new PlayerArgs
        {
            Item = AppServices.Catalog.Banners.First(),
            Episode = 1,
            Online = source ?? Services.OnlineSources.Default
        });

    public void OpenSearch(string? keyword) => NavigateTo("search", keyword ?? "");

    public void OpenLibrary(int tabIndex = 0) => NavigateTo("library", tabIndex);

    public void GoBackPage()
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        else NavigateTo("home");
    }

    private void OnFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        var type = e.SourcePageType;
        var immersive = type == typeof(PlayerPage);
        _immersive = immersive;
        TitleBar.Visibility = immersive ? Visibility.Collapsed : Visibility.Visible;
        NavBar.Visibility = immersive ? Visibility.Collapsed : Visibility.Visible;
        TitleBarRow.Height = immersive ? new GridLength(0) : new GridLength(48);
        NavBarRow.Height = immersive ? new GridLength(0) : new GridLength(44);
        // 播放页是纯黑画面：系统标题栏按钮必须切成白色，否则浅色主题下会"看不见"最小化/最大化。
        ApplyCaptionButtonColors();

        // Frame.Navigated 触发时目标页的 OnNavigatedTo 可能尚未执行，
        // 不能从页面实例读分类，否则选中态会一直停留在上一个频道（看起来像点击没生效）。
        string? activeTag = type switch
        {
            _ when type == typeof(HomePage) => "home",
            _ when type == typeof(RankPage) => "rank",
            _ when type == typeof(ChannelPage) =>
                e.Parameter as string ?? (ContentFrame.Content as ChannelPage)?.Category,
            _ => null
        };

        NavTab? selected = null;
        foreach (var tab in _tabs)
        {
            tab.IsSelected = activeTag is not null && (string)tab.Tag == activeTag;
            if (tab.IsSelected) selected = tab;
        }

        if (selected is not null) EnsureTabVisible(selected);
        BackButton.IsEnabled = ContentFrame.CanGoBack;
        ForwardButton.IsEnabled = ContentFrame.CanGoForward;
    }

    private void OnTabClicked(object? sender, EventArgs e)
    {
        if (sender is NavTab { Tag: string tag })
            NavigateTo(tag, tag is "home" or "rank" ? null : tag);
    }

    // ---------------- 频道栏横向滚动 ----------------
    // 频道栏是普通内容区（不是 SetTitleBar 区域），因此 Tapped / PointerWheel 都能正常收到。
    // 这里额外提供鼠标滚轮横滚与左右箭头，保证在窄窗口下也能看到全部频道。

    private double MaxTabOffset => Math.Max(0, TabScroller.ExtentWidth - TabScroller.ViewportWidth);

    private void OnTabWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(TabScroller).Properties.MouseWheelDelta;
        if (delta == 0) return;
        var target = Math.Clamp(TabScroller.HorizontalOffset - delta * 0.9, 0, MaxTabOffset);
        TabScroller.ChangeView(target, null, null, false);
        e.Handled = true;
    }

    private void OnTabScrollLeft(object sender, RoutedEventArgs e) => ScrollTabs(-260);

    private void OnTabScrollRight(object sender, RoutedEventArgs e) => ScrollTabs(260);

    private void ScrollTabs(double delta) =>
        TabScroller.ChangeView(Math.Clamp(TabScroller.HorizontalOffset + delta, 0, MaxTabOffset),
            null, null, true);

    private void OnTabViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) =>
        UpdateTabScrollButtons();

    private void OnTabScrollerSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateTabScrollButtons();

    private void UpdateTabScrollButtons()
    {
        var max = MaxTabOffset;
        var overflow = max > 1;
        TabLeftButton.Visibility = overflow && TabScroller.HorizontalOffset > 1
            ? Visibility.Visible : Visibility.Collapsed;
        TabRightButton.Visibility = overflow && TabScroller.HorizontalOffset < max - 1
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 自检入口（--selftest）：逐个触发 8 个频道的点击处理器，验证页面切换与选中态；
    /// 再把窗口临时收窄，验证频道栏溢出检测、左右箭头与 ChangeView 横向滚动。
    /// </summary>
    public async Task<string> RunNavCheckAsync()
    {
        var lines = new List<string>();
        foreach (var tab in _tabs)
        {
            var tag = (string)tab.Tag;
            try
            {
                OnTabClicked(tab, EventArgs.Empty);
                var page = ContentFrame.CurrentSourcePageType?.Name ?? "null";
                var selected = _tabs.FirstOrDefault(t => t.IsSelected)?.Tag as string ?? "none";
                lines.Add($"TAB {tag,-8} page={page,-16} selected={selected}");
            }
            catch (Exception ex) { lines.Add($"TAB {tag} FAIL {ex.Message}"); }
        }

        var original = AppWindow.Size;
        try
        {
            AppWindow.Resize(new SizeInt32(560, 800));
            await Task.Delay(500);
            ApplyResponsiveLayout();
            UpdateTabScrollButtons();

            var extent = TabScroller.ExtentWidth;
            var viewport = TabScroller.ViewportWidth;
            var max = MaxTabOffset;
            var rightVisible = TabRightButton.Visibility == Visibility.Visible;
            ScrollTabs(300);
            await Task.Delay(500);
            var offset = TabScroller.HorizontalOffset;
            var leftVisible = TabLeftButton.Visibility == Visibility.Visible;

            lines.Add($"SCROLL extent={extent:0} viewport={viewport:0} max={max:0} " +
                      $"rightArrow={rightVisible} leftArrowAfterScroll={leftVisible} offset={offset:0}");
            lines.Add($"SCROLL_RESULT {(max > 0 && rightVisible && offset > 0 ? "OK" : "CHECK")}");
        }
        catch (Exception ex) { lines.Add($"SCROLL FAIL {ex.Message}"); }
        finally
        {
            try { AppWindow.Resize(original); } catch { /* 恢复失败不影响自检结论 */ }
            try { TabScroller.ChangeView(0, null, null, false); } catch { /* 忽略 */ }
        }

        NavigateTo("home");
        return string.Join("\r\n", lines);
    }

    /// <summary>导航完成后把当前选中的频道滚动到可视范围内。</summary>
    private void EnsureTabVisible(NavTab tab)
    {
        if (tab.ActualWidth <= 0 || TabScroller.ViewportWidth <= 0) return;
        var point = tab.TransformToVisual(TabPanel)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        var left = point.X;
        var right = left + tab.ActualWidth;
        var offset = TabScroller.HorizontalOffset;
        var viewport = TabScroller.ViewportWidth;

        if (left < offset + 8)
            TabScroller.ChangeView(Math.Max(0, left - 8), null, null, false);
        else if (right > offset + viewport - 8)
            TabScroller.ChangeView(Math.Min(MaxTabOffset, right - viewport + 8), null, null, false);
    }

    // ---------------- 主题 / 全屏 / 提示 ----------------

    /// <summary>
    /// 系统标题栏按钮（最小化 / 最大化 / 关闭）由 WinUI 绘制，颜色跟着主题走。
    /// 但播放页整页是黑色画面：浅色主题的黑色按钮会完全融进背景，看起来像"没有"这些按钮。
    /// 因此播放页强制使用白色前景与浅色悬停底，离开播放页再恢复主题色。
    /// </summary>
    private void ApplyCaptionButtonColors()
    {
        try
        {
            var tb = AppWindow.TitleBar;
            var light = _lightTheme && !_immersive;
            var fg = light ? Colors.Black : Colors.White;
            tb.ButtonBackgroundColor = Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Colors.Transparent;
            tb.ButtonForegroundColor = fg;
            tb.ButtonInactiveForegroundColor = light
                ? Windows.UI.Color.FromArgb(0x99, 0x00, 0x00, 0x00)
                : Windows.UI.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF);
            tb.ButtonHoverBackgroundColor = light
                ? Windows.UI.Color.FromArgb(30, 0x00, 0x00, 0x00)
                : Windows.UI.Color.FromArgb(40, 0xFF, 0xFF, 0xFF);
            tb.ButtonHoverForegroundColor = fg;
            tb.ButtonPressedBackgroundColor = light
                ? Windows.UI.Color.FromArgb(45, 0x00, 0x00, 0x00)
                : Windows.UI.Color.FromArgb(64, 0xFF, 0xFF, 0xFF);
            tb.ButtonPressedForegroundColor = fg;
        }
        catch { /* 部分系统版本不支持自定义标题栏按钮色，忽略 */ }
    }

    /// <summary>系统最小化 / 最大化 / 关闭按钮占用的右侧宽度（供播放页顶栏留白）。</summary>
    public double CaptionButtonsWidth => Math.Max(138, AppWindow.TitleBar.RightInset);

    public void ApplyTheme(string theme)
    {
        var light = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        _lightTheme = light;
        RootGrid.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;

        if (!_acrylicEnabled)
        {
            // 没有材质时 WinUI 窗口的默认底色是白色，深色主题下会白底白字；
            // 这里自己铺一层不透明主题色作为回退。
            RootGrid.Background = new SolidColorBrush(light
                ? Windows.UI.Color.FromArgb(0xFF, 0xF5, 0xF6, 0xFA)
                : Windows.UI.Color.FromArgb(0xFF, 0x0E, 0x0F, 0x14));
        }

        ApplyCaptionButtonColors();
    }

    public void SetFullScreen(bool on) =>
        AppWindow.SetPresenter(on ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);

    /// <summary>启动时的静默更新检查完成后，若设置页正打开则即时刷新结果。</summary>
    public void RefreshSettingsUpdate(UpdateCheckResult result)
    {
        if (ContentFrame.Content is SettingsPage page) page.OnStartupUpdateChecked(result);
    }

    public void ShowToast(string message)
    {
        Toast.Subtitle = message;
        Toast.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ---------------- 标题栏事件 ----------------

    private void OnLogoTapped(object sender, RoutedEventArgs e) => NavigateTo("home");

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
    }

    private void OnForwardClick(object sender, RoutedEventArgs e)
    {
        if (ContentFrame.CanGoForward) ContentFrame.GoForward();
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = AppServices.Catalog.Suggest(sender.Text);
    }

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var text = (args.ChosenSuggestion as string) ?? args.QueryText ?? "";
        text = text.Trim();
        if (text.Length == 0) return;
        OpenSearch(text);
        sender.Text = "";
        sender.IsSuggestionListOpen = false;
    }

    private void OnLibraryClick(object sender, RoutedEventArgs e) => OpenLibrary(0);
    private void OnHistoryClick(object sender, RoutedEventArgs e) => OpenLibrary(1);
    private void OnSettingsMenuClick(object sender, RoutedEventArgs e) => NavigateTo("settings");

    private void OnVipClick(object sender, RoutedEventArgs e) =>
        ShowToast("演示版：所有内容均可直接观看，VIP 标识仅用于界面示意");

    private async void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "关于星影视频",
            Content = "一款使用 WinUI 3 / Windows App SDK 开发的桌面影音客户端界面演示。\n\n" +
                      "片库、海报与焦点图均为本地生成的原创演示素材，不连接任何真实视频服务。\n" +
                      "播放页内置 FFmpeg 解码（本地视频任意格式）与公开在线测试片源；" +
                      "除用户主动播放的测试视频外不连接其它网络服务。",
            CloseButtonText = "知道了",
            XamlRoot = RootGrid.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
