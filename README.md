# 星影视频 · StarVideo

一款使用 **WinUI 3 / Windows App SDK 2.6.5 / .NET 8** 开发的桌面影音客户端界面演示，UI 交互参考主流长视频平台（首页推荐、频道筛选、排行榜、搜索、详情、播放、我的片单、设置）。主窗口采用 **AcrylicThin 半透明磨砂材质**。

> 本地演示项目：片名、海报、焦点图、评分、简介、演员均为 `Tools/generate_assets.py` 生成的**原创虚构素材**，不连接任何真实视频服务、不采集数据。播放页内置 FFmpeg 解码与几条**公开测试视频地址**，只有用户主动点播时才会访问网络。

---

## 一、运行

### 直接双击（推荐）

```
C:\Workspace\WinUI.Projs\StarVideo\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\StarVideo.exe
```

项目配置为 **非 MSIX 打包 + 自包含 Windows App SDK 运行时**（`WindowsPackageType=None` + `WindowsAppSDKSelfContained=true`），生成的是可直接双击运行的普通 exe，无需安装 Windows App Runtime，也不需要管理员权限。

FFmpeg 二进制（`ffmpeg.exe` / `ffprobe.exe` / `LICENSE`）已随程序放在输出目录的 `Tools\ffmpeg\`，开箱即用。

### Visual Studio 2022

打开 `C:\Workspace\WinUI.Projs\StarVideo.sln`，选择 **x64 / Release**，F5 运行。需要安装「.NET 桌面开发」和「Windows 应用 SDK」工作负载。

### 命令行

```bash
cd C:\Workspace\WinUI.Projs\StarVideo
dotnet restore
dotnet build StarVideo.csproj -c Release -p:Platform=x64 -r win-x64
```

### 开发调试参数

| 参数 | 作用 |
| --- | --- |
| `--smoke` | 依次打开全部 11 个页面做运行时自检，结果写 `%LOCALAPPDATA%\StarVideo\smoke.log` 后退出 |
| `--selftest` | 完整功能自检：8 个频道点击 / 频道栏横向滚动、FFmpeg 探测与转码、4 个在线片源 HTTP 可达性、MediaSource 创建；结果写 `selftest.log` 后退出 |
| `--goto=detail` | 启动后直接打开指定页面，便于截图检查。可选 `detail / player / online / pick / search / library / rank / channel / settings`（`online` 直接播放在线测试片源；`pick` 不经鼠标直接弹出本地文件选择框，供自动化验证） |
| `--nativepicker` | 强制使用 Shell 原生文件对话框，用于在普通权限下复现「管理员权限」分支（`--goto=pick --nativepicker`） |

> 只有 `--selftest` / `--smoke` 会禁用「关闭到托盘」以便自动退出，其余启动方式（含 `--goto=`）都保留正常托盘行为。

`--selftest` 的转码用例依赖三个 3 秒测试片（H.264 MP4 / MKV 容器 / HEVC MKV），可用随项目的 FFmpeg 生成到 `%LOCALAPPDATA%\StarVideo\`：

```bash
set FF=Tools\ffmpeg\ffmpeg.exe
%FF% -y -f lavfi -i testsrc=size=320x240:rate=15:duration=3 -f lavfi -i sine=frequency=440:duration=3 -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -shortest "%LOCALAPPDATA%\StarVideo\test_h264.mp4"
%FF% -y -i "%LOCALAPPDATA%\StarVideo\test_h264.mp4" -c copy "%LOCALAPPDATA%\StarVideo\test_remux.mkv"
%FF% -y -f lavfi -i testsrc=size=320x240:rate=15:duration=3 -f lavfi -i sine=frequency=440:duration=3 -c:v libx265 -preset ultrafast -c:a aac -shortest "%LOCALAPPDATA%\StarVideo\test_hevc.mkv"
```

---

## 二、功能

| 页面 | 内容 |
| --- | --- |
| 首页 | 焦点图自动轮播（可点圆点切换）、7 个快捷入口（含「在线试播」）、继续观看、为你推荐（换一批）、热播榜、高分精选、4 个频道分区 |
| 频道页 | 电视剧 / 电影 / 综艺 / 动漫 / 纪录片 / 少儿，按类型、地区、年份、排序、免费/VIP 多条件筛选，空结果可一键重置 |
| 排行榜 | 热播榜 / 口碑榜 / 新片榜 × 全频道筛选，左侧完整榜单 + 右侧前三名速览 |
| 搜索页 | 关键词检索片名 / 演员 / 类型 / 标签 / 简介，热搜词、排序、空结果提示 |
| 详情页 | 主视觉 + 海报 + 评分 / 元信息 / 简介 / 演职员、选集网格、收藏、分享、猜你喜欢、继续播放 |
| 播放页 | 三种片源（演示 / 本地 FFmpeg / 在线），**点击画面播放/暂停**、拖动进度、上下集、音量、0.75~2.0 倍速、选集侧栏、**控制条最右侧的全屏按钮**、双击全屏、**悬浮叠加且淡入淡出自动隐藏的控制条**；窗口最小化 / 最大化 / 关闭按钮在纯黑画面上自动转为白色 |
| 我的 | 我的收藏 / 观看历史 / 继续观看，进度条与观看时间展示，支持清空 |
| 设置 | 深浅主题、自动下一集、FFmpeg 状态与自定义路径、强制转码开关、转码缓存管理、数据管理 |

其它：自绘标题栏（拖拽 / 系统按钮 / 后退前进）、**独立的频道导航行（可点击 + 鼠标滚轮/左右箭头横向滚动）**、全局搜索联想、VIP 入口、Toast 提示、DataChanged 事件驱动的跨页面刷新、**窗口级 AcrylicThin 半透明磨砂背景**、**系统托盘（关闭到托盘 / 双击恢复 / 右键菜单）**。

---

## 三、系统托盘与本地文件选择

### 系统托盘

`Services/TrayIconService.cs` 用 Win32 实现（非打包 WinUI 3 没有内置托盘 API）：`Shell_NotifyIconW` 创建图标，`SetWindowSubclass` 挂在主窗口 WndProc 上接收托盘回调，`TrackPopupMenuEx` 显示右键菜单。

| 操作 | 行为 |
| --- | --- |
| 点窗口右上角 ✕ | **不退出**：`AppWindow.Closing` 里 `Cancel = true`，窗口 `SW_HIDE` 隐藏并从任务栏移除，进程继续运行；首次会弹一次气泡提示 |
| 在播放页点 ✕ | 先调用 `PlayerPage.PauseForTray()` 暂停真实播放并停掉计时器，**再**隐藏；从托盘恢复后保持暂停，由用户点画面 / 播放键继续 |
| 双击托盘图标 | 恢复窗口并置前 |
| 右键 →「打开星影视频」 | 恢复窗口，**页面内容与关闭前完全一致**（窗口只是隐藏、从未销毁，`Frame` 的导航栈、播放进度、滚动位置都原样保留） |
| 右键 →「退出」 | 移除托盘图标并真正结束进程（`Close()` + `Application.Exit()`，另有兜底强制退出） |

实现上的两个关键点：

- **右键菜单需要独立的可见宿主窗口**。主窗口隐藏后 `SetForegroundWindow` 会失败，菜单拿不到键盘焦点、点菜单外不收起，还会把 UI 线程卡在菜单的模态循环里。所以另建一个 1×1、屏幕外、`WS_EX_TOOLWINDOW` 的弹出窗口承载菜单，并用「模拟 ALT 键 + `BringWindowToTop`」兜底争取前台权限；菜单项以标准 `WM_COMMAND` 消息投递，不依赖 `TrackPopupMenuEx` 的返回值。
- 订阅 `TaskbarCreated` 广播，Explorer 重启后自动重新添加图标。

### 「打开本地视频文件」的两条路径

Windows / Windows App SDK 有一个长期未修的已知问题：**进程以管理员身份运行时，`Windows.Storage.Pickers.FileOpenPicker.PickSingleFileAsync()` 会抛 COM 异常**（[WindowsAppSDK#2504](https://github.com/microsoft/WindowsAppSDK/issues/2504)），表现就是点「打开本地视频文件」直接报错。微软给出的替代方案是用 Shell 的 Common Item Dialog 取路径，再交给 `StorageFile.GetFileFromPathAsync`。

`Services/FilePickerService.cs` 因此做了双路径：

1. 普通权限 → WinRT 现代选择器（缩略图视图、视频库起始位置）；
2. 管理员权限，或 WinRT 选择器抛任何异常 → 自动回退到 `GetOpenFileNameW` 原生对话框，不报错、不中断。

两条路径都返回绝对路径，统一交给 `PrepareLocalAsync`；`*` 通配符过滤器被单独包了 try/catch（个别系统不接受），拖放文件与 `CreateFromStorageFile` 失败时也分别有异常保护和 `file://` URI 兜底。选择过程写入 `picker.log`。

---

## 四、窗口材质与播放页交互

### AcrylicThin

`MainWindow` 的 `SystemBackdrop` 设为 `Services/ThinAcrylicBackdrop`。XAML 的 `DesktopAcrylicBackdrop` **没有 `Kind` 属性**，直接用它拿不到 Thin 变体，所以这里派生 `SystemBackdrop`，内部驱动 `DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin }`，并在主题 / 激活状态变化时同步 `SystemBackdropConfiguration`。

配套要求：`AppBackgroundBrush` / `TopBarBrush` 只留一层很淡的半透明薄纱（`Themes/Styles.xaml`），不透明的 `Surface/Card` 画刷保证卡片文字可读；远程桌面、关闭透明效果、省电模式、高对比度等场景 `IsSupported()` 返回 false，窗口自动退回不透明主题底色（在 `ApplyTheme` 中处理）。播放页本身是不透明纯黑，沉浸式观看时不会透出桌面。

### 播放页浮层

- 画面区、顶部标题栏、底部控制条、右侧选集面板**全部叠在同一层 Grid 上**，控制条用 `VerticalAlignment=Bottom` 悬浮，不占布局行；
- 显隐只改 `Opacity`（XAML 配 `ScalarTransition` 做 180ms 淡入淡出）与 `IsHitTestVisible`，**不改 `Visibility`**，视频区域尺寸恒定、`MediaPlayerElement` 交换链不重建，避免尺寸变化时的白块闪帧；
- 控制条背景改为「顶部全透明 → 底部深色」的渐变，隐藏时不会出现生硬的矩形边界；
- 画面中央不再有任何播放/暂停按钮。点击画面区域（`Tapped` 挂在画面容器上，按钮类目标自动排除）即切换播放/暂停；双击全屏时会回滚第一击造成的播放状态变化，避免抖动；
- **全屏按钮在底部控制条最右侧**（`FullScreen` / `BackToWindow` 图标随状态切换），右上角完全让给系统的最小化 / 最大化 / 关闭按钮，不再有按钮互相压住。顶部浮层第 3 列按 `AppWindow.TitleBar.RightInset` 动态留白，影片标题不会伸到系统按钮下面；
- 播放页是纯黑画面，`MainWindow.ApplyCaptionButtonColors()` 在该页强制把系统标题栏按钮（含失焦 / 悬停 / 按下态）设为白色前景 + 浅色半透明底，离开播放页再恢复主题色。否则浅色主题下黑色按钮会融进背景，看起来像「没有最小化 / 最大化」。

---

## 五、FFmpeg 本地播放

打开本地视频（「打开本地视频文件」按钮或直接拖放文件；文件对话框的两条路径见上一节）后，流程是：

1. `ffprobe` 读取容器、视频/音频编码、分辨率、时长，显示在播放页与控制条状态里；
2. 自动决策 `Services/FFmpegService.Decide`：
   - **Direct** —— MP4/M4V/MOV + H.264/MPEG4 + AAC/MP3 等系统解码器支持的组合，零延迟直接播放；
   - **Remux** —— 编码兼容但容器不兼容（MKV / AVI / TS / FLV…），`ffmpeg -c copy -movflags +faststart` 重封装成 MP4，通常几十毫秒；
   - **Full** —— 编码不兼容（HEVC / AV1 / VP9 / DTS…），`libx264 + aac` 转码成 H.264/AAC MP4，带实时进度与取消按钮；
3. 用 `MediaPlayerElement` 播放结果文件，可正常拖动进度、变速、全屏。

转码结果按「路径 + 大小 + 修改时间 + 模式」做哈希缓存，位于 `%LOCALAPPDATA%\StarVideo\transcode\`，同一文件不会重复转码；缩略图在 `thumbs\`。设置页可以一键清空缓存、强制所有文件走 FFmpeg、或指定自己的 FFmpeg 目录（留空则用随程序发布的版本，也支持从系统 PATH 查找）。

**许可**：随附的是 [FFmpeg gyan.dev essentials 静态构建 9.0.2](https://www.gyan.dev/ffmpeg/builds/)（GPLv3），完整许可证见 `Tools/ffmpeg/LICENSE`。若重新分发本程序，请一并保留该许可证与对应源码获取说明。

---

## 六、在线测试片源

播放页控制条点「在线」可打开片源面板，也可从首页「在线试播」一键进入。内置 4 条公开测试地址（交付前均用 Range 请求验证返回 `206`）：

| 名称 | 类型 | 地址 |
| --- | --- | --- |
| Sintel 预告片（默认） | MP4 H.264/AAC，约 52 秒 | `https://media.w3.org/2010/05/sintel/trailer.mp4` |
| Big Buck Bunny 720p | MP4，10 秒 / 2MB | `https://test-videos.co.uk/vids/bigbuckbunny/mp4/h264/720/Big_Buck_Bunny_720_10s_2MB.mp4` |
| Flower 花朵 | MP4，约 5 秒 | `https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4` |
| Mux HLS 测试流 | 多码率 HLS `m3u8` | `https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8` |

面板里还可以粘贴任意 `http(s)` 的 MP4 / m3u8 地址自定义播放。在线播放走 `MediaSource.CreateFromUri`，不经过 FFmpeg；打开失败会由 `MediaPlayer.MediaFailed` 弹 Toast 说明。

---

## 七、数据与素材

- 片库：`Assets/catalog.json`（36 部作品），`Assets/Posters/*.jpg`（36 张竖版海报）、`Assets/Banners/*.jpg`（9 张焦点图）
- 重新生成素材：`python Tools/generate_assets.py`（依赖 Pillow，使用系统中文字体）
- 想扩充片库：直接改 `catalog.json` 并补一张同名海报到 `Assets/Posters/`，无需改代码
- 用户数据：`%LOCALAPPDATA%\StarVideo\user.json`（收藏、观看进度、主题、自动下一集、FFmpeg 路径与强制转码开关）
- 日志：同目录 `error.log`（未处理异常）、`picker.log`（文件选择器与本地片源）、`tray.log`（托盘生命周期 / 暂停恢复）、`player.log`（全屏切换）、`smoke.log`、`selftest.log`

---

## 八、目录结构

```
WinUI.Projs/
├─ StarVideo.sln
├─ README.md
├─ Preview.png                      成品首页预览
├─ Preview-Player.png               成品播放页预览（系统按钮 / 控制条全屏按钮）
└─ StarVideo/
   ├─ App.xaml(.cs)                 入口、异常落盘、--smoke / --selftest / --goto
   ├─ MainWindow.xaml(.cs)          自绘标题栏 + 独立频道导航行、Frame 路由、主题、系统按钮配色、AcrylicThin、Toast、关闭到托盘
   ├─ Themes/Styles.xaml            主题色板（半透明底色）、按钮 / 文本样式、卡片数据模板
   ├─ Controls/NavTab.xaml(.cs)     频道 Tab 控件
   ├─ Models/                       MediaItem / WatchRecord / WatchEntry / RankEntry / PlayerArgs
   ├─ Services/
   │   ├─ CatalogService.cs         片库查询
   │   ├─ UserDataService.cs        本地持久化
   │   ├─ FFmpegService.cs          探测 / 决策 / 重封装 / 转码 / 缓存 / 缩略图
   │   ├─ FilePickerService.cs      WinRT 选择器 + Shell 原生对话框双路径（管理员权限回退）
   │   ├─ TrayIconService.cs        系统托盘图标、右键菜单、TaskbarCreated 重建
   │   ├─ AppLog.cs                 轻量本地日志
   │   ├─ OnlineSources.cs          内置在线测试片源与 URL 校验
   │   ├─ ThinAcrylicBackdrop.cs    AcrylicThin 窗口材质（自定义 SystemBackdrop）
   │   ├─ SelfTest.cs               --selftest 检查项
   │   └─ AppServices.cs            全局服务容器
   ├─ Helpers/Converters.cs         本地图片路径 → BitmapImage（带缓存）等转换器
   ├─ Views/                        8 个页面
   ├─ Assets/                       海报、焦点图、Logo、catalog.json
   └─ Tools/
       ├─ generate_assets.py        素材生成脚本
       └─ ffmpeg/                   ffmpeg.exe / ffprobe.exe / LICENSE
```

---

## 九、开发中踩过的坑

### WinUI 3 XamlCompiler（部分错误会静默失败，只报 MSB3073；1.8 / 2.x 通用）

1. `ThemeDictionaries` 必须是资源字典的**最后一个元素**，其后不能放任何资源。
2. `RectangleGeometry` 在 WinUI 3 **没有** `RadiusX / RadiusY`（那是 WPF 属性）；圆角裁剪改用 `ImageBrush` 作为带 `CornerRadius` 容器的背景。
3. XAML 中出现**未知属性**会导致编译器静默崩溃，例如把 `IsItemClickEnabled` 误写成 `IsItemClickable`。
4. 不要把 `.cs` 备份文件放在项目目录下（SDK 默认 glob 会当源码编译，产生重复类定义）；备份统一用 `.txt` / `.bak`。
5. 首页 `FlipView.ItemsSource` 赋值会**立即**触发 `SelectionChanged`，轮播定时器必须先于它初始化。

### 运行时 / 布局

6. **频道 Tab 点不动、也不能横向滚动的根因**：原来把整个标题栏（包含频道导航）传给 `SetTitleBar()`，该区域内的非交互控件（`UserControl`、`ScrollViewer`）鼠标事件会被系统当作「拖动窗口」吞掉。修法是把频道导航拆成**标题栏下方的独立一行**（普通内容区），Logo 改成 `Button`；再补上鼠标滚轮横滚、左右箭头、选中 Tab 自动滚入视野。
7. **点击后选中态不更新**：`Frame.Navigated` 触发时目标页的 `OnNavigatedTo` 可能还没执行，从页面实例读 `Category` 会拿到旧值/默认值。改用 `NavigationEventArgs.Parameter` 判断当前频道。
8. **视频画面被挡住**：`MediaPlayerElement` 若 `Insert(0, ...)` 会落在占位背景图之下，画面完全不可见。必须插到占位提示之前，并在切到真实片源时隐藏占位背景与压暗层。
9. **高 DPI 下窗口被挤成窄条**：`DisplayArea.WorkArea` 在部分 150% DPI 环境返回异常小的值，导致标题栏各 Auto 列总宽溢出、内容错位。改用 `GetSystemMetrics` 的物理像素计算窗口尺寸，并按宽度分档收起标题栏次要控件。
10. `FFmpeg -progress pipe:1` 的 `out_time_us / out_time_ms` 单位是**微秒**，算进度时要除以 1,000,000。

### 播放页浮层 / 版本升级

11. **控制条显隐影响画面、隐藏时闪白块**：旧布局把控制条放在 `Grid.Row=1 (Auto)`，切 `Visibility` 会改变视频行高度，`MediaPlayerElement` 尺寸变化触发交换链重建，未绘制区域短暂显示为白色。修法见「三、窗口材质与播放页交互」——改为同层悬浮叠加 + `Opacity` 过渡，永不改布局。
12. **`DesktopAcrylicBackdrop` 没有 `Kind` 属性**：XAML 里写 `<DesktopAcrylicBackdrop/>` 只能拿到系统默认亚克力，拿不到 AcrylicThin。必须派生 `SystemBackdrop` 自己驱动 `DesktopAcrylicController`。另外 XAML 根 / 页面若铺不透明背景，材质会被完全盖住，看不到任何效果。
13. **在线流让「控制条自动隐藏」失效**：`PlaybackSession.PlaybackState` 会在 `Playing / Buffering / Opening` 之间切换，只判断 `Playing` 会把缓冲误判为暂停，隐藏逻辑被永久跳过。
14. **升级到 Windows App SDK 2.6.5**：从 `1.8.260317003` 升级，2.x 起 NuGet 元包会拆出 `Base / Foundation / WinUI / Runtime` 等子包，但仍由 `Microsoft.WindowsAppSDK` 统一引入；现有 `net8.0-windows10.0.19041.0` + 非打包自包含配置无需改动，Release x64 构建 0 警告 0 错误。

### 本地文件选择 / 系统托盘

15. **「打开本地视频文件」在管理员权限下必报错**：不是本项目代码写错，而是 `Windows.Storage.Pickers.FileOpenPicker` 在已提升的进程里抛 COM 异常（WindowsAppSDK#2504，常年未修）。普通权限下不复现，所以很容易误判为"偶发"。修法见第三节：按 `IsUserAnAdmin()` 分流，加上 WinRT 抛异常时的自动回退。Win32 侧注意三点：`OPENFILENAMEW` 的 `lStructSize` 用 `Marshal.SizeOf`（x64 为 152）、过滤器是双 NUL 结尾且内嵌 `\0` 不能被 marshal 截断、`lpstrFile` 要自己 `AllocHGlobal` 并清零。
16. **托盘右键菜单卡住 UI 线程**：主窗口 `SW_HIDE` 后 `SetForegroundWindow` 失败，菜单既拿不到键盘焦点也不会因点击外部而收起，`TrackPopupMenuEx` 的模态循环就一直占着 UI 线程。解决办法是建一个屏幕外 1×1 的可见弹出窗口做菜单宿主，并用 `WM_COMMAND` 而非 `TPM_RETURNCMD` 交付菜单命令。
17. **受限会话里截图不可信**：在锁屏 / 服务窗口站下 `ImageGrab` 抓到的是锁屏壁纸而不是应用窗口，鼠标点击也送不到应用。验证托盘这类功能要以 Win32 状态（`IsWindowVisible`、进程存活、`FindWindow` 菜单类 `#32768`）和 `tray.log` 为准，不要只看截图；`--goto=pick` 提供了不依赖鼠标的自动化入口。

### 播放页窗口按钮

18. **播放页「没有」最小化 / 最大化按钮、全屏按钮和关闭按钮重叠**：其实三个系统按钮一直都在且可点，把右上角截图提亮就能看到——浅色主题下 `AppWindow.TitleBar.ButtonForegroundColor` 被设成黑色，叠在播放页纯黑画面上几乎不可见（横线、方框比 ✕ 更难辨认），而顶栏右侧的白色全屏按钮又正好压在系统关闭按钮的位置上。修法不是自绘一套重复按钮，而是：(a) 播放页强制白色系统按钮配色（含 inactive / hover / pressed），离开后按主题恢复；(b) 把全屏按钮移到控制条末尾；(c) 顶栏第 3 列按 `RightInset` 留白。验证时用 `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` 取可见窗口边界来算点击坐标，比 `GetWindowRect`（含阴影边框）准。

---

## 十、已知限制

- 演示片库不含真实片源，默认播放页仍是「标题 + 模拟进度」；真实播放请选择本地视频或在线测试片源。
- FFmpeg 采用「先转码成临时 MP4、再交给系统播放器」的方案，因此长视频首次打开需要等待转码（带进度与取消，结果会缓存）；不是逐帧解码的自定义渲染管线。
- 在线片源为第三方公开测试地址，可用性取决于对方服务与本机网络（`Big Buck Bunny` 那条偶发 10 秒超时）。
- AcrylicThin 依赖系统透明效果与显卡合成能力；远程桌面 / 虚拟机 / 高对比度 / 省电模式下会自动退回纯色背景。
- 托盘按单窗口设计：窗口隐藏后只能从托盘图标恢复或退出；`--selftest` / `--smoke` 为能自动结束进程，关闭时不进托盘。
- 托盘右键菜单依赖前台窗口；在锁屏、服务会话等没有前台窗口的环境里键盘导航可能失效，鼠标点选与菜单命令本身仍可正常工作。
- 分享、VIP 开通为界面示意，Toast 会提示未接入真实服务。
- 仅构建并验证了 **win-x64**；csproj 中保留了 x86 / ARM64 的 `RuntimeIdentifiers`，如需其它架构请对应切换 `Platform` 与 `-r`（FFmpeg 二进制也需换成对应架构版本）。
