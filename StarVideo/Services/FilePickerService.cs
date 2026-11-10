using System.Runtime.InteropServices;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace StarVideo.Services;

/// <summary>
/// 本地视频文件选择。
///
/// 【为什么要两套实现】Windows / Windows App SDK 有一个长期未修的已知问题：
/// 进程以「管理员身份」运行时，Windows.Storage.Pickers.FileOpenPicker.PickSingleFileAsync()
/// 会抛 COM 异常（microsoft/WindowsAppSDK#2504）。微软给出的 workaround 正是改用
/// Shell 的 Common Item Dialog 取路径，再交给 StorageFile.GetFileFromPathAsync。
/// 普通权限下 WinRT 选择器体验更好（现代 UI / 缩略图），所以保留为首选；
/// 管理员权限或 WinRT 抛异常时，自动回退到不依赖 WinRT 激活、也不依赖包身份的 Win32 原生对话框。
/// 这样点「打开本地视频文件」在任何权限下都不会再报错。
/// </summary>
public static class FilePickerService
{
    public static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".ts",
        ".m2ts", ".mpg", ".mpeg", ".3gp", ".vob", ".mp3", ".m4a", ".aac", ".wav"
    };

    /// <summary>返回选中的绝对路径；用户取消返回 null。绝不在取消时再弹第二个对话框。</summary>
    public static async Task<string?> PickVideoAsync(IntPtr owner)
    {
        // 开发/排查用：--nativepicker 强制走 Shell 原生对话框，用来在没有管理员权限的机器上
        // 也能完整验证管理员分支（WinRT picker 失效时走的就是这一条）的回退链路。
        var forceWin32 = Environment.GetCommandLineArgs().Any(a =>
            string.Equals(a, "--nativepicker", StringComparison.OrdinalIgnoreCase));

        if (!IsElevated() && !forceWin32)
        {
            try
            {
                var picker = new FileOpenPicker();
                // 非打包 WinUI 3 必须把 picker 绑定到宿主 HWND，否则 PickSingleFileAsync 直接抛异常。
                if (owner != IntPtr.Zero) InitializeWithWindow.Initialize(picker, owner);

                picker.ViewMode = PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
                foreach (var ext in VideoExtensions) picker.FileTypeFilter.Add(ext);
                // 个别系统不接受 "*" 通配符，单独包一层；扩展名列表本身已足够。
                try { picker.FileTypeFilter.Add("*"); }
                catch { /* 忽略：不为了通配符让整个选择器失效 */ }

                var file = await picker.PickSingleFileAsync();
                return file?.Path;
            }
            catch (Exception ex)
            {
                AppLog.Write("picker.log", $"WinRT FileOpenPicker 失败，自动回退 Win32 对话框：{ex}");
            }
        }
        else if (IsElevated())
        {
            AppLog.Write("picker.log",
                "检测到以管理员权限运行，直接使用 Shell 原生文件对话框（规避 WindowsAppSDK#2504）");
        }
        else
        {
            AppLog.Write("picker.log", "--nativepicker：自检强制使用 Shell 原生文件对话框");
        }

        try
        {
            return PickWithWin32(owner);
        }
        catch (Exception ex)
        {
            AppLog.Write("picker.log", $"Win32 文件对话框也失败：{ex}");
            throw;
        }
    }

    // ---------------- 首选：WinRT 现代选择器 ----------------

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsUserAnAdmin();

    /// <summary>进程持有已提升的管理员令牌时，WinRT picker 必失败，直接走原生对话框。</summary>
    private static bool IsElevated()
    {
        try { return IsUserAnAdmin(); }
        catch { return false; }
    }

    // ---------------- 回退：Win32 通用文件对话框 ----------------

    private const int OFN_HIDEREADONLY = 0x00000004;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_EXPLORER = 0x00080000;
    private const int MaxPathChars = 32768;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAMEW
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "GetOpenFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAMEW ofn);

    [DllImport("comdlg32.dll", EntryPoint = "CommDlgExtendedErrorW")]
    private static extern uint CommDlgExtendedError();

    private static string? PickWithWin32(IntPtr owner)
    {
        // 过滤器是「说明\0通配符\0…\0\0」的双 NUL 结尾格式；P/Invoke 会自动再补一个终止 NUL。
        var wildcard = string.Join(";", VideoExtensions.Select(e => "*" + e));
        var filter = $"视频文件\0{wildcard}\0所有文件\0*.*\0";

        var buffer = Marshal.AllocHGlobal(MaxPathChars * 2);
        try
        {
            Marshal.Copy(new byte[MaxPathChars * 2], 0, buffer, MaxPathChars * 2);

            var ofn = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = owner,
                hInstance = IntPtr.Zero,
                lpstrFilter = filter,
                lpstrCustomFilter = IntPtr.Zero,
                nMaxCustFilter = 0,
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = MaxPathChars,
                lpstrFileTitle = IntPtr.Zero,
                nMaxFileTitle = 0,
                lpstrInitialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                lpstrTitle = "打开本地视频文件",
                Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST |
                        OFN_HIDEREADONLY | OFN_NOCHANGEDIR,
                lpstrDefExt = "mp4",
                lCustData = IntPtr.Zero,
                lpfnHook = IntPtr.Zero,
                lpTemplateName = IntPtr.Zero,
                pvReserved = IntPtr.Zero,
                dwReserved = 0,
                FlagsEx = 0
            };

            AppLog.Write("picker.log", $"调用 Win32 文件对话框 cbSize={ofn.lStructSize} owner={owner}");
            if (!GetOpenFileNameW(ref ofn))
            {
                var err = CommDlgExtendedError();
                // err == 0 表示用户点了「取消」，不是错误，不要打扰用户。
                AppLog.Write("picker.log", err == 0
                    ? "用户取消了 Win32 文件对话框"
                    : $"GetOpenFileNameW 失败，扩展错误码 {err}");
                return null;
            }

            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
