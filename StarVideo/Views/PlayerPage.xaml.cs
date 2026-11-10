using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using StarVideo.Models;
using StarVideo.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using WinRT.Interop;
using WinUI = Microsoft.UI.Xaml;

namespace StarVideo.Views;

/// <summary>
/// 播放页：三种片源
///  1) 演示模式 —— 海报 + 模拟进度（默认，无真实文件）；
///  2) 本地文件 —— FFmpeg 探测编码，系统解码器能直放就直放，否则 -c copy 重封装或 libx264 转码后播放；
///  3) 在线片源 —— MediaSource.CreateFromUri 播 MP4 / HLS，内置已验证的公开测试视频。
/// </summary>
public sealed partial class PlayerPage : Page
{
    private MediaItem _item = null!;
    private int _episode = 1;
    private double _position;
    private double _duration;
    private double _speed = 1.0;
    private bool _playing = true;
    private bool _hasRealMedia;
    private bool _syncing;
    private bool _isFullScreen;
    private int _tickCount;
    private string _sourceLabel = "演示播放";
    private bool _overlaysVisible = true;
    // 双击全屏时第一击已经被当成"点击画面"处理过，用这几个字段把播放状态回滚，避免画面抖动
    private bool _pendingTapRollback;
    private bool _playingBeforeTap;
    private long _lastTapTicks;

    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _hideTimer;
    private MediaPlayer? _player;
    private MediaPlayerElement? _mediaElement;
    private MediaSource? _currentSource;
    private CancellationTokenSource? _transcodeCts;

    private static readonly double[] Speeds = { 0.75, 1.0, 1.25, 1.5, 2.0 };

    public PlayerPage()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTick;
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _hideTimer.Tick += (_, _) => HideOverlays();

        Root.AllowDrop = true;
        Root.DragOver += OnDragOver;
        Root.Drop += OnDrop;

        Loaded += (_, _) =>
        {
            // 顶部浮层右侧让出系统标题栏按钮的真实宽度（含 DPI/系统版本差异），
            // 标题文字与系统按钮不会挤在一起。
            try
            {
                CaptionSpacerColumn.Width = new GridLength(
                    (App.MainWindow?.CaptionButtonsWidth ?? 138) + 10);
            }
            catch { /* 保留 XAML 里的默认留白 */ }
            UpdateFullScreenButton();
        };

        BuildOnlinePresets();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var args = e.Parameter as PlayerArgs ?? new PlayerArgs
        {
            Item = AppServices.Catalog.All.First(),
            Episode = 1
        };
        _item = args.Item;
        _episode = Math.Max(1, args.Episode);

        var record = AppServices.User.RecordOf(_item.Id);
        _duration = _item.Category == "电影" ? 105 * 60 : 45 * 60;
        _position = record is not null && record.Episode == _episode && record.DurationSeconds > 0
            ? Math.Min(record.PositionSeconds, record.DurationSeconds * 0.97)
            : 0;

        ApplyPlaceholder();
        BuildEpisodePanel();
        UpdateHeader();
        UpdateTime();
        SetPlaying(true);
        _timer.Start();
        ShowOverlays();

        // 首页「在线试播」入口：等页面进入可视树后立即开始在线播放。
        if (args.Online is { } online)
            Loaded += (_, _) => DispatcherQueue.TryEnqueue(() => _ = PlayOnlineAsync(online));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        SaveProgress();
        _timer.Stop();
        _hideTimer.Stop();
        CancelTranscode();

        try
        {
            _player?.Pause();
            _player?.Dispose();
            _currentSource?.Dispose();
        }
        catch { /* 释放失败不影响退出 */ }

        _player = null;
        _mediaElement = null;
        _currentSource = null;
        App.MainWindow?.SetFullScreen(false);
    }

    // ---------------- 初始画面 ----------------

    private void ApplyPlaceholder()
    {
        var converter = (Helpers.ImagePathConverter)Application.Current.Resources["ImagePath"];
        var bitmap = converter.Convert(_item.BannerPath, typeof(ImageSource), null!, "") as BitmapImage
                     ?? converter.Convert(_item.PosterPath, typeof(ImageSource), null!, "") as BitmapImage;
        if (bitmap is not null)
            PlaceholderBackdrop.Background = new ImageBrush
            {
                ImageSource = bitmap,
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };

        PlaceholderTitle.Text = _item.Title;
        PlaceholderHint.Text = "本演示片库不含真实片源，进度条为模拟播放。\n" +
                               (FFmpegService.IsAvailable
                                   ? $"{FFmpegService.VersionText} 已就绪，可打开任意格式的本地视频。"
                                   : "打开本地视频或在线测试片源，即可体验真实播放。");
    }

    private void UpdateHeader()
    {
        VideoTitle.Text = _item.Title;
        var unit = _item.Category == "综艺" ? "期" : "集";
        VideoSubtitle.Text = _item.IsSeries
            ? $"第 {_episode} {unit}  ·  {_item.MetaText}"
            : _item.MetaText;
        EpisodePanelHint.Text = $"{_item.Title}  ·  共 {Math.Max(1, _item.EpisodeNames.Count)} {unit}";
    }

    // ---------------- 选集面板 ----------------

    private void BuildEpisodePanel()
    {
        EpisodePanelGrid.Children.Clear();
        EpisodePanelGrid.RowDefinitions.Clear();
        EpisodePanelGrid.ColumnDefinitions.Clear();
        EpisodePanelGrid.ColumnSpacing = 8;
        EpisodePanelGrid.RowSpacing = 8;

        var names = _item.EpisodeNames;
        const int columns = 5;
        for (var c = 0; c < columns; c++)
            EpisodePanelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rows = (int)Math.Ceiling(names.Count / (double)columns);
        for (var r = 0; r < rows; r++)
            EpisodePanelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var normal = (Style)Application.Current.Resources["ChipButtonStyle"];
        var active = (Style)Application.Current.Resources["SelectedChipStyle"];

        for (var i = 0; i < names.Count; i++)
        {
            var episode = i + 1;
            var button = new Button
            {
                Content = (i + 1).ToString("00"),
                Tag = episode,
                Height = 40,
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Style = episode == _episode ? active : normal
            };
            ToolTipService.SetToolTip(button, names[i]);
            Grid.SetRow(button, i / columns);
            Grid.SetColumn(button, i % columns);
            button.Click += OnPanelEpisodeClick;
            EpisodePanelGrid.Children.Add(button);
        }
    }

    private void OnPanelEpisodeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: int episode }) SwitchEpisode(episode);
    }

    private void SwitchEpisode(int episode)
    {
        SaveProgress();
        var max = Math.Max(1, _item.EpisodeNames.Count);
        _episode = Math.Clamp(episode, 1, max);
        _position = 0;
        UpdateHeader();
        UpdateTime();
        BuildEpisodePanel();
        App.MainWindow?.ShowToast($"已切换到第 {_episode} 集");
    }

    private void OnPrevEpisode(object sender, RoutedEventArgs e) => SwitchEpisode(_episode - 1);
    private void OnNextEpisode(object sender, RoutedEventArgs e) => SwitchEpisode(_episode + 1);

    // ---------------- 播放循环 ----------------

    private void OnTick(object? sender, object e)
    {
        _syncing = true;
        if (_hasRealMedia && _player is not null)
        {
            try
            {
                var session = _player.PlaybackSession;
                if (session.NaturalDuration.TotalSeconds > 0)
                    _duration = session.NaturalDuration.TotalSeconds;
                _position = session.Position.TotalSeconds;
                // 在线流在 Playing / Buffering / Opening 之间来回切换，若只认 Playing，
                // 一次缓冲就会把状态误判为"已暂停"，导致控制条再也不会自动隐藏。
                _playing = session.PlaybackState is MediaPlaybackState.Playing
                    or MediaPlaybackState.Buffering or MediaPlaybackState.Opening;
            }
            catch { /* 媒体尚未打开时忽略 */ }
        }
        else if (_playing)
        {
            _position += 0.25 * _speed;
            if (_position >= _duration)
            {
                _position = _duration;
                if (AppServices.User.AutoPlayNext && _item.IsSeries) SwitchEpisode(_episode + 1);
                else SetPlaying(false);
            }
        }

        UpdateTime();
        _syncing = false;

        if (++_tickCount % 20 == 0) SaveProgress();
    }

    private void UpdateTime()
    {
        if (_duration <= 0) _duration = 45 * 60;
        SeekBar.Value = Math.Clamp(_position / _duration * 100, 0, 100);
        TimeText.Text = $"{WatchRecord.FormatTime(_position)} / {WatchRecord.FormatTime(_duration)}";
    }

    private void SaveProgress() =>
        AppServices.User.RecordProgress(_item.Id, _episode, _position, _duration);

    private void SetPlaying(bool on)
    {
        _playing = on;
        if (_hasRealMedia && _player is not null)
        {
            if (on) _player.Play();
            else _player.Pause();
        }

        PlayIcon.Glyph = on ? "\uE769" : "\uE768";
        StatusText.Text = _hasRealMedia
            ? $"{_sourceLabel} · {(on ? "播放中" : "已暂停")}"
            : (on ? "演示播放中" : "已暂停");
        ShowOverlays();
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => SetPlaying(!_playing);

    /// <summary>
    /// 主窗口关闭到系统托盘时调用：立即暂停真实播放（避免后台继续出声），并停掉 UI 计时器。
    /// 窗口只是隐藏、页面不销毁，所以播放进度与片源都原样保留。
    /// </summary>
    public void PauseForTray()
    {
        if (_hasRealMedia)
        {
            SetPlaying(false);
        }
        else
        {
            _playing = false;
            PlayIcon.Glyph = "\uE768";
            StatusText.Text = "已暂停";
        }
        _timer.Stop();
        _hideTimer.Stop();
        AppLog.Write("tray.log",
            $"播放页已暂停：真实媒体={_hasRealMedia}，playing={_playing}，位置={_position:0.0}s");
    }

    /// <summary>从托盘恢复时重启计时器；不自动续播，由用户点画面 / 播放键继续。</summary>
    public void ResumeFromTray()
    {
        _timer.Start();
        ShowOverlays();
        AppLog.Write("tray.log", $"播放页已从托盘恢复：playing={_playing}（保持暂停，等待用户继续）");
    }

    private void OnSeekChanged(object sender, WinUI.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncing || _duration <= 0) return;
        _position = e.NewValue / 100 * _duration;
        if (_hasRealMedia && _player is not null)
        {
            try { _player.PlaybackSession.Position = TimeSpan.FromSeconds(_position); }
            catch { /* 定位失败时保留模拟进度 */ }
        }
        TimeText.Text = $"{WatchRecord.FormatTime(_position)} / {WatchRecord.FormatTime(_duration)}";
        ShowOverlays();
    }

    private void OnVolumeChanged(object sender, WinUI.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_player is not null) _player.Volume = e.NewValue / 100;
    }

    private void OnSpeedClick(object sender, RoutedEventArgs e)
    {
        var index = Array.IndexOf(Speeds, _speed);
        _speed = Speeds[(index + 1) % Speeds.Length];
        SpeedButton.Content = $"{_speed:0.##}x";
        if (_hasRealMedia && _player is not null)
        {
            try { _player.PlaybackSession.PlaybackRate = _speed; }
            catch { /* 部分格式不支持变速 */ }
        }
        App.MainWindow?.ShowToast($"播放速度 {_speed:0.##}x");
        ShowOverlays();
    }

    // ---------------- 在线片源 ----------------

    // Flyout 的内容是按需实例化的，打开时再重建一次，确保按钮始终存在。
    private void OnOnlineFlyoutOpening(object? sender, object e) => BuildOnlinePresets();

    private void BuildOnlinePresets()
    {
        if (OnlinePresetPanel is null) return;
        OnlinePresetPanel.Children.Clear();
        var chip = (Style)Application.Current.Resources["ChipButtonStyle"];
        var muted = (Brush)Application.Current.Resources["TextMutedBrush"];

        foreach (var source in OnlineSources.Presets)
        {
            var content = new StackPanel { Spacing = 3 };
            content.Children.Add(new TextBlock
            {
                Text = $"{source.Name}    [{source.Badge}]",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            content.Children.Add(new TextBlock
            {
                Text = source.Description,
                FontSize = 10.5,
                Foreground = muted,
                TextWrapping = TextWrapping.Wrap
            });

            var button = new Button
            {
                Tag = source,
                Content = content,
                Style = chip,
                Padding = new Thickness(12, 9, 12, 9),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            button.Click += OnOnlinePresetClick;
            OnlinePresetPanel.Children.Add(button);
        }
    }

    private void OnPlayDefaultOnline(object sender, RoutedEventArgs e) =>
        _ = PlayOnlineAsync(OnlineSources.Default);

    private void OnOnlinePresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OnlineSource source }) _ = PlayOnlineAsync(source);
    }

    private void OnPlayCustomUrl(object sender, RoutedEventArgs e)
    {
        if (!OnlineSources.TryValidate(CustomUrlBox.Text, out var uri, out var error))
        {
            App.MainWindow?.ShowToast(error);
            return;
        }
        _ = PlayOnlineAsync(new OnlineSource("自定义地址", uri!.Host, uri.ToString(), "URL"));
    }

    private Task PlayOnlineAsync(OnlineSource source)
    {
        if (!OnlineSources.TryValidate(source.Url, out var uri, out var error))
        {
            App.MainWindow?.ShowToast(error);
            return Task.CompletedTask;
        }

        CancelTranscode();
        ShowTranscodePanel("正在连接在线片源", source.Name, 0, cancelable: false);
        try
        {
            PlaySource(MediaSource.CreateFromUri(uri!), $"在线 · {source.Name}", source.Description);
            VideoSubtitle.Text = source.Description;
        }
        catch (Exception ex)
        {
            HideTranscodePanel();
            App.MainWindow?.ShowToast($"在线播放失败：{ex.Message}");
        }
        return Task.CompletedTask;
    }

    // ---------------- 本地视频 + FFmpeg ----------------

    /// <summary>开发自检入口：用代码触发同一个按钮处理器（含 picker 与播放全链路）。</summary>
    public void OpenLocalFileFromCode() => OnOpenLocalFile(this, new RoutedEventArgs());

    private async void OnOpenLocalFile(object sender, RoutedEventArgs e)
    {
        try
        {
            // 统一的 picker 服务会按权限选路：普通权限用 WinRT 现代对话框，
            // 管理员权限（WinRT picker 必抛 COM 异常）或任何 WinRT 失败时自动回退 Shell 原生对话框。
            var hwnd = App.MainWindow is not null
                ? WindowNative.GetWindowHandle(App.MainWindow)
                : IntPtr.Zero;
            var path = await FilePickerService.PickVideoAsync(hwnd);
            if (string.IsNullOrWhiteSpace(path)) { ShowOverlays(); return; } // 用户取消

            await PrepareLocalAsync(path);
        }
        catch (Exception ex)
        {
            AppLog.Write("picker.log", $"[OnOpenLocalFile] {ex}");
            App.MainWindow?.ShowToast($"无法打开本地视频：{ex.Message}");
        }
        ShowOverlays();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.OfType<StorageFile>().FirstOrDefault() is { } file)
                await PrepareLocalAsync(file.Path, file);
        }
        catch (Exception ex)
        {
            AppLog.Write("picker.log", $"[OnDrop] {ex}");
            App.MainWindow?.ShowToast($"无法读取拖入的文件：{ex.Message}");
        }
    }

    /// <summary>
    /// FFmpeg 探测 → 决策 →（必要时）重封装/转码 → 交给 MediaPlayerElement。
    /// 直放模式零延迟；mkv/avi 等只做 -c copy 重封装（秒级）；HEVC/AV1/DTS 等才真正转码。
    /// </summary>
    private async Task PrepareLocalAsync(string path, StorageFile? file = null)
    {
        CancelTranscode();
        var cts = _transcodeCts = new CancellationTokenSource();

        ShowTranscodePanel("FFmpeg 正在探测视频", "读取容器、编码、分辨率与时长…", 0);

        MediaProbe probe;
        try
        {
            probe = await FFmpegService.ProbeAsync(path, cts.Token);
        }
        catch (Exception) when (cts.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            probe = new MediaProbe(0, null, null, 0, 0, null, ex.Message);
        }

        if (cts.IsCancellationRequested) return;

        var mode = FFmpegService.Decide(probe, path);
        if (AppServices.User.ForceFFmpeg && mode == TranscodeMode.Direct)
            mode = TranscodeMode.Remux;

        var playPath = path;
        var modeLabel = "直放";

        if (mode != TranscodeMode.Direct)
        {
            if (!FFmpegService.IsAvailable)
            {
                HideTranscodePanel();
                App.MainWindow?.ShowToast($"需要 FFmpeg 处理此格式，但未找到 ffmpeg.exe：{FFmpegService.BundledDirectory}");
                return;
            }

            modeLabel = mode == TranscodeMode.Remux ? "重封装" : "转码";
            ShowTranscodePanel($"FFmpeg 正在{modeLabel}", probe.Summary, 0);

            var progress = new Progress<TranscodeProgress>(p =>
                DispatcherQueue.TryEnqueue(() => UpdateTranscode(p)));

            try
            {
                playPath = await FFmpegService.ConvertAsync(
                    path, mode, probe.DurationSeconds, progress, cts.Token);
            }
            catch (Exception) when (cts.IsCancellationRequested) { return; }
            catch (OperationCanceledException)
            {
                HideTranscodePanel();
                App.MainWindow?.ShowToast("已取消 FFmpeg 处理");
                return;
            }
            catch (Exception ex)
            {
                HideTranscodePanel();
                App.MainWindow?.ShowToast($"FFmpeg 处理失败：{ex.Message}");
                return;
            }
        }

        if (cts.IsCancellationRequested) return;

        try
        {
            if (probe.DurationSeconds > 0) _duration = probe.DurationSeconds;
            var name = file?.Name ?? Path.GetFileName(playPath);
            var source = await CreateLocalSourceAsync(playPath,
                string.Equals(playPath, path, StringComparison.OrdinalIgnoreCase) ? file : null);

            PlaySource(source,
                mode == TranscodeMode.Direct ? "本地直放" : $"FFmpeg {modeLabel}",
                $"{name}  ·  {probe.Summary}");
            AppLog.Write("picker.log", $"本地片源就绪：{name} mode={modeLabel} path={playPath}");
            HideTranscodePanel();
            App.MainWindow?.ShowToast(mode == TranscodeMode.Direct
                ? $"FFmpeg 探测完成，系统解码器直接播放：{probe.VideoCodec?.ToUpperInvariant()}"
                : $"FFmpeg {modeLabel}完成，开始播放");
        }
        catch (Exception ex)
        {
            HideTranscodePanel();
            App.MainWindow?.ShowToast($"播放失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 优先用 StorageFile（MediaFoundation 最稳）；若 WinRT 拿不到该路径（权限 / 网络盘等），
    /// 退回本地 file:// URI，保证「打开本地视频」不会因一个 API 失败就整个报错。
    /// </summary>
    private static async Task<MediaSource> CreateLocalSourceAsync(string path, StorageFile? known = null)
    {
        try
        {
            var file = known ?? await StorageFile.GetFileFromPathAsync(path);
            return MediaSource.CreateFromStorageFile(file);
        }
        catch (Exception ex)
        {
            AppLog.Write("picker.log", $"CreateFromStorageFile 失败，改用 file URI：{ex.Message}");
            return MediaSource.CreateFromUri(new Uri(path));
        }
    }

    // ---------------- 统一的播放源切换 ----------------

    private void EnsureMediaPlayer()
    {
        if (_mediaElement is not null) return;

        _player = new MediaPlayer { Volume = VolumeBar.Value / 100, AutoPlay = true };
        _player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(HideTranscodePanel);
        _player.MediaFailed += (_, args) => DispatcherQueue.TryEnqueue(() =>
        {
            HideTranscodePanel();
            StatusText.Text = "片源打开失败";
            App.MainWindow?.ShowToast($"播放失败：{args.ErrorMessage}");
        });

        _mediaElement = new MediaPlayerElement
        {
            AreTransportControlsEnabled = false,
            Stretch = Stretch.Uniform,
            // 交换链尚未绘制 / 视频上下黑边区域统一为纯黑，杜绝任何白底透出
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x05, 0x06, 0x0A)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _mediaElement.SetMediaPlayer(_player);
        // 必须插到占位背景 / 压暗层之上、点击命中层之下：
        // 既保证视频可见，又保证点击能落到 VideoTapArea 上。
        var index = VideoHost.Children.IndexOf(VideoTapArea);
        VideoHost.Children.Insert(index < 0 ? VideoHost.Children.Count : index, _mediaElement);
    }

    private void PlaySource(MediaSource source, string label, string subtitle)
    {
        EnsureMediaPlayer();

        var previous = _currentSource;
        _currentSource = source;
        try { _player!.Source = source; }
        catch { throw; }
        try { previous?.Dispose(); } catch { /* 旧片源释放失败不影响新片源 */ }

        _hasRealMedia = true;
        _sourceLabel = label;
        _position = 0;
        PlaceholderBackdrop.Visibility = Visibility.Collapsed;
        DimLayer.Visibility = Visibility.Collapsed;
        PlaceholderInfo.Visibility = Visibility.Collapsed;
        VideoSubtitle.Text = subtitle;
        SetPlaying(true);
        ShowOverlays();
    }

    // ---------------- 转码进度面板 ----------------

    private void ShowTranscodePanel(string title, string detail, double percent, bool cancelable = true)
    {
        TranscodePanel.Visibility = Visibility.Visible;
        PlaceholderInfo.Visibility = Visibility.Collapsed;
        TranscodeTitle.Text = title;
        TranscodeDetail.Text = detail;
        TranscodeBar.Value = percent;
        TranscodePercent.Text = $"{percent:0}%";
        CancelTranscodeButton.Content = cancelable ? "取消" : "关闭";
        ShowOverlays();
    }

    private void UpdateTranscode(TranscodeProgress p)
    {
        if (TranscodePanel.Visibility != Visibility.Visible) return;
        TranscodeDetail.Text = p.Message;
        TranscodeBar.Value = p.Percent;
        TranscodePercent.Text = $"{p.Percent:0}%";
    }

    private void HideTranscodePanel() => TranscodePanel.Visibility = Visibility.Collapsed;

    private void OnCancelTranscode(object sender, RoutedEventArgs e)
    {
        CancelTranscode();
        HideTranscodePanel();
        App.MainWindow?.ShowToast("已取消");
    }

    private void CancelTranscode()
    {
        if (_transcodeCts is null) return;
        try { _transcodeCts.Cancel(); } catch { /* 已释放 */ }
        _transcodeCts.Dispose();
        _transcodeCts = null;
    }

    // ---------------- 浮层 / 全屏 / 返回 ----------------

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e) => ShowOverlays();

    /// <summary>点击画面区域：切换播放 / 暂停并唤出控制条，取代原来挡在画面中央的大按钮。</summary>
    private void OnVideoAreaTapped(object sender, TappedRoutedEventArgs e)
    {
        // 占位区里的「打开本地视频 / 在线试播 / 取消」等按钮自己有行为，不要顺带切换播放状态
        if (IsInteractiveTarget(e.OriginalSource as DependencyObject)) return;

        _playingBeforeTap = _playing;
        _lastTapTicks = Environment.TickCount64;
        _pendingTapRollback = true;
        SetPlaying(!_playing);
    }

    private static bool IsInteractiveTarget(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is Button || node is Slider || node is TextBox) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>
    /// 浮层显隐只改 Opacity（XAML 里配了 ScalarTransition 做淡入淡出）与命中状态，
    /// 不改 Visibility、不改任何布局，视频区域尺寸恒定、交换链不重建，因此不会出现白块闪帧。
    /// </summary>
    private void ShowOverlays()
    {
        _hideTimer.Stop();
        _hideTimer.Start();
        // 鼠标每次移动只重置计时器，不重复触发淡入动画
        if (_overlaysVisible) return;
        SetOverlayState(true);
    }

    private void HideOverlays()
    {
        _hideTimer.Stop();
        // 演示模式（无真实片源）保持控制条常驻；转码/加载中不隐藏。
        if (!_hasRealMedia || !_playing ||
            TranscodePanel.Visibility == Visibility.Visible ||
            EpisodePanel.Visibility == Visibility.Visible) return;
        if (_overlaysVisible) SetOverlayState(false);
    }

    private void SetOverlayState(bool show)
    {
        _overlaysVisible = show;
        TopOverlay.IsHitTestVisible = show;
        ControlBar.IsHitTestVisible = show;
        TopOverlay.Opacity = show ? 1 : 0;
        ControlBar.Opacity = show ? 1 : 0;
    }

    private void OnToggleEpisodes(object sender, RoutedEventArgs e)
    {
        EpisodePanel.Visibility = EpisodePanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        ShowOverlays();
    }

    private void OnFullScreenClick(object sender, RoutedEventArgs e) => ToggleFullScreen();

    /// <summary>全屏按钮在底部控制条，图标与提示随状态切换。</summary>
    private void UpdateFullScreenButton()
    {
        FullScreenIcon.Glyph = _isFullScreen ? "\uE73F" : "\uE740";
        ToolTipService.SetToolTip(FullScreenButton,
            _isFullScreen ? "退出全屏（也可双击画面）" : "全屏（也可双击画面）");
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // 双击全屏时，第一击已经被当作"点击画面"切换过播放状态，这里回滚，避免播放状态抖动。
        if (_pendingTapRollback && Environment.TickCount64 - _lastTapTicks < 600)
        {
            _pendingTapRollback = false;
            SetPlaying(_playingBeforeTap);
        }
        ToggleFullScreen();
    }

    private void ToggleFullScreen()
    {
        _isFullScreen = !_isFullScreen;
        App.MainWindow?.SetFullScreen(_isFullScreen);
        UpdateFullScreenButton();
        AppLog.Write("player.log", $"切换全屏：fullScreen={_isFullScreen}");
        App.MainWindow?.ShowToast(_isFullScreen ? "已进入全屏（双击画面或点控制条按钮退出）" : "已退出全屏");
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => App.MainWindow?.GoBackPage();
}
