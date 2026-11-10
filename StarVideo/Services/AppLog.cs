namespace StarVideo.Services;

/// <summary>轻量本地日志：异常落盘到 %LOCALAPPDATA%\StarVideo，便于排查但不打扰用户。</summary>
public static class AppLog
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarVideo");

    public static void Write(string fileName, string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.AppendAllText(Path.Combine(DirectoryPath, fileName),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* 日志写入失败绝不影响主流程 */ }
    }
}
