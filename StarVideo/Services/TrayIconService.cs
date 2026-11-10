using System.Runtime.InteropServices;

namespace StarVideo.Services;

/// <summary>
/// Win32 系统托盘。非打包的 WinUI 3 没有内置托盘 API，因此用：
///   Shell_NotifyIconW   —— 添加 / 删除托盘图标与气泡提示
///   SetWindowSubclass   —— 挂在主窗口 WndProc 上接收托盘回调（不替换 WndProc，避免与 XAML 框架冲突）
///   TrackPopupMenuEx    —— 右键菜单（TPM_RETURNCMD 直接返回选中项，不需要额外处理 WM_COMMAND）
/// 关闭窗口时"最小化到托盘"的拦截由 MainWindow 的 AppWindow.Closing 负责，
/// 本类只管图标、消息与菜单。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAY = WM_APP + 1;          // 托盘回调消息
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_NULL = 0x0000;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIIF_INFO = 0x00000001;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;
    private const int IDI_APPLICATION = 32512;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_RIGHTBUTTON = 0x00000002;
    private const uint TPM_LEFTALIGN = 0x00000000;

    private const int CmdOpen = 1;
    private const int CmdExit = 2;
    private const uint IconId = 1;
    private static readonly IntPtr SubclassId = new(0x5356);

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOWNOACTIVATE = 4;

    private readonly IntPtr _hwnd;
    private readonly Action _onRestore;
    private readonly Action _onExit;
    // 委托必须作为字段持有，否则 GC 回收后本机回调会崩
    private readonly SubclassProc _subclassProc;
    private readonly MenuWndProc _menuWndProc;
    private readonly uint _taskbarCreatedMsg;
    private readonly IntPtr _icon;
    private bool _ownsIcon;
    private IntPtr _menuHwnd;
    private bool _iconAdded;
    private bool _disposed;
    private bool _hinted;
    private long _lastMenuTicks;

    public bool IsIconAdded => _iconAdded;

    public TrayIconService(IntPtr hwnd, Action onRestore, Action onExit, string? iconPath = null)
    {
        _hwnd = hwnd;
        _onRestore = onRestore;
        _onExit = onExit;
        _subclassProc = Subclass;
        _menuWndProc = (h, m, w, l) => DefWindowProcW(h, m, w, l);

        _icon = LoadIcon(iconPath, out _ownsIcon);
        _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");
        _menuHwnd = CreateMenuHost();

        if (!SetWindowSubclass(hwnd, _subclassProc, SubclassId, IntPtr.Zero))
            AppLog.Write("tray.log", "SetWindowSubclass 失败，托盘点击可能无响应");

        EnsureIcon();
        AppLog.Write("tray.log",
            $"托盘初始化 hwnd={hwnd} cbSize={Marshal.SizeOf<NOTIFYICONDATAW>()} icon={_icon} added={_iconAdded}");
    }

    // ---------------- 图标 ----------------

    /// <summary>Explorer 重启后会广播 TaskbarCreated，此时需要重新添加图标。</summary>
    public void EnsureIcon()
    {
        if (_iconAdded || _disposed) return;

        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_TRAY;
        data.hIcon = _icon;
        data.szTip = "星影视频 - 双击恢复窗口";

        _iconAdded = Shell_NotifyIconW(NIM_ADD, ref data);
        if (!_iconAdded) AppLog.Write("tray.log", "Shell_NotifyIcon(NIM_ADD) 失败");
    }

    /// <summary>首次隐藏到托盘时给一次气泡提示，之后不再打扰。</summary>
    public void ShowFirstMinimizeHint()
    {
        if (_hinted || !_iconAdded) return;
        _hinted = true;
        try
        {
            var data = NewData();
            data.uFlags = NIF_INFO;
            data.szInfoTitle = "星影视频";
            data.szInfo = "已最小化到系统托盘。双击图标或右键「打开星影视频」恢复。";
            data.dwInfoFlags = NIIF_INFO;
            data.uTimeoutOrVersion = 4000;
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
        catch (Exception ex) { AppLog.Write("tray.log", $"气泡提示失败：{ex.Message}"); }
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        szTip = "",
        szInfo = "",
        szInfoTitle = ""
    };

    private static IntPtr LoadIcon(string? path, out bool owns)
    {
        owns = false;
        path ??= Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "app.ico");
        try
        {
            var cx = GetSystemMetrics(SM_CXSMICON);
            var cy = GetSystemMetrics(SM_CYSMICON);
            var h = LoadImageW(IntPtr.Zero, path, IMAGE_ICON, cx, cy, LR_LOADFROMFILE);
            if (h != IntPtr.Zero) { owns = true; return h; }
        }
        catch (Exception ex) { AppLog.Write("tray.log", $"加载托盘图标失败：{ex.Message}"); }

        // 找不到 ico 时用系统默认应用图标兜底，至少保证托盘里有个可点的图标
        var fallback = LoadIconW(IntPtr.Zero, new IntPtr(IDI_APPLICATION));
        return fallback;
    }

    private void RemoveIcon()
    {
        if (!_iconAdded) return;
        _iconAdded = false;
        var data = NewData();
        Shell_NotifyIconW(NIM_DELETE, ref data);
    }

    // ---------------- 消息 / 菜单 ----------------

    /// <summary>
    /// 右键菜单必须由"可见的顶层窗口"承载：主窗口关闭到托盘后是 SW_HIDE，
    /// SetForegroundWindow 对隐藏窗口会失败，菜单拿不到键盘焦点、点菜单外也不收起，
    /// 更糟的是 UI 线程会一直卡在 TrackPopupMenuEx 的模态循环里。
    /// 这里建一个 1x1、屏幕外、WS_EX_TOOLWINDOW（不进任务栏）的弹出窗口专供菜单使用。
    /// </summary>
    private IntPtr CreateMenuHost()
    {
        try
        {
            var hInst = GetModuleHandleW(null);
            var wc = new WNDCLASSW
            {
                lpfnWndProc = _menuWndProc,
                hInstance = hInst,
                lpszClassName = "StarVideoTrayMenuHost"
            };
            // 1410 = ERROR_CLASS_ALREADY_EXISTS，同进程重复初始化时视为成功
            if (RegisterClassW(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410)
            {
                AppLog.Write("tray.log", "RegisterClass 失败，菜单回退到主窗口宿主");
                return IntPtr.Zero;
            }

            var host = CreateWindowExW(WS_EX_TOOLWINDOW, "StarVideoTrayMenuHost", "",
                WS_POPUP, -32000, -32000, 1, 1,
                IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
            if (host == IntPtr.Zero)
            {
                AppLog.Write("tray.log", "CreateWindowEx 失败，菜单回退到主窗口宿主");
                return IntPtr.Zero;
            }
            ShowWindow(host, SW_SHOWNOACTIVATE);
            return host;
        }
        catch (Exception ex)
        {
            AppLog.Write("tray.log", $"创建菜单宿主窗口失败：{ex}");
            return IntPtr.Zero;
        }
    }

    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// Windows 的前台窗口锁：主窗口隐藏到托盘后本进程往往不是前台进程，
    /// SetForegroundWindow 直接调用会失败，菜单拿不到键盘焦点、点菜单外也不收起。
    /// 依次用「模拟 ALT 键解锁 + BringWindowToTop」再试，尽量让菜单宿主成为前台窗口。
    /// </summary>
    private static bool ForceForeground(IntPtr hwnd)
    {
        if (SetForegroundWindow(hwnd)) return true;
        try
        {
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (SetForegroundWindow(hwnd)) return true;
            BringWindowToTop(hwnd);
            if (SetForegroundWindow(hwnd)) return true;
        }
        catch (Exception ex) { AppLog.Write("tray.log", $"前台激活兜底失败：{ex.Message}"); }
        return false;
    }

    private IntPtr Subclass(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                            IntPtr uIdSubclass, IntPtr dwRefData)
    {
        try
        {
            if (msg == WM_TRAY)
            {
                // 经典 NOTIFYICONDATA：lParam 低字是鼠标消息，高字是图标 ID
                var mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouse == WM_LBUTTONDBLCLK)
                    _onRestore();
                else if (mouse == WM_RBUTTONUP || mouse == WM_CONTEXTMENU)
                    ShowContextMenu(hWnd);
            }
            else if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
            {
                _iconAdded = false;   // Explorer 重启后旧图标已失效，需要重新添加
                EnsureIcon();
            }
            else if (msg == WM_COMMAND)
            {
                // 标准 Win32 菜单：选中项以 WM_COMMAND 投递给菜单宿主窗口。
                // 用消息而不是 TrackPopupMenuEx 的返回值，菜单循环是否被系统打断都不影响命令生效。
                var cmd = (int)(wParam.ToInt64() & 0xFFFF);
                if (cmd == CmdOpen) _onRestore();
                else if (cmd == CmdExit) _onExit();
            }
            else if (msg == WM_DESTROY)
            {
                RemoveIcon();
            }
        }
        catch (Exception ex)
        {
            // WndProc 里绝不能让托管异常跨越本机边界
            AppLog.Write("tray.log", $"托盘消息处理异常：{ex}");
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu(IntPtr hWnd)
    {
        // 某些系统会同时投递 WM_RBUTTONUP 和 WM_CONTEXTMENU，去重避免连弹两次菜单
        var now = Environment.TickCount64;
        if (now - _lastMenuTicks < 400) return;
        _lastMenuTicks = now;

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenuW(menu, MF_STRING, new UIntPtr(CmdOpen), "打开星影视频");
            AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, new UIntPtr(CmdExit), "退出");

            GetCursorPos(out var pt);
            // 用常驻的可见宿主窗口争取前台权限；主窗口隐藏时它仍能成为前台窗口。
            var host = _menuHwnd != IntPtr.Zero ? _menuHwnd : hWnd;
            var foreground = ForceForeground(host);
            AppLog.Write("tray.log",
                $"弹出右键菜单 host={host} main={hWnd} fg={foreground} pt=({pt.X},{pt.Y})");

            // owner 传主窗口：菜单项被选中时系统把 WM_COMMAND 发给它，由上面的子类过程处理。
            TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_LEFTALIGN,
                pt.X, pt.Y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RemoveIcon();
        RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
        if (_menuHwnd != IntPtr.Zero)
        {
            DestroyWindow(_menuHwnd);
            _menuHwnd = IntPtr.Zero;
        }
        // LoadImage(LR_LOADFROMFILE) 创建的图标归调用方所有，需要自己销毁；
        // LoadIcon 返回的是系统共享图标，绝不能 DestroyIcon。
        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _ownsIcon = false;
        }
    }

    // ---------------- Win32 声明 ----------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private delegate IntPtr MenuWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public MenuWndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                                         IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassW", SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string className, string windowName,
        uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc,
                                                 IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc,
                                                    IntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type,
                                            int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIconW(IntPtr hInst, IntPtr name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", EntryPoint = "TrackPopupMenuEx")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y,
                                               IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
