namespace StarVideo.Services;

/// <summary>全局服务定位（演示项目，避免引入额外的 DI 容器依赖）。</summary>
public static class AppServices
{
    public static bool IsInitialized { get; private set; }
    public static CatalogService Catalog { get; private set; } = null!;
    public static UserDataService User { get; private set; } = null!;

    /// <summary>收藏 / 观看进度变化后通知各页面刷新。</summary>
    public static event EventHandler? DataChanged;

    public static void Initialize()
    {
        if (IsInitialized) return;
        Catalog = new CatalogService();
        User = new UserDataService();
        IsInitialized = true;
    }

    public static void NotifyDataChanged() => DataChanged?.Invoke(null, EventArgs.Empty);
}
