using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace StarVideo.Services;

/// <summary>
/// 窗口级 AcrylicThin 半透明背景。
///
/// 注意：XAML 的 <see cref="DesktopAcrylicBackdrop"/> 只有一个无参构造，并没有 Kind 属性，
/// 直接写 SystemBackdrop = new DesktopAcrylicBackdrop() 得到的是系统默认（深色下接近 Base）的亚克力。
/// 要拿到明确的 Thin 变体，必须派生 SystemBackdrop 并自行驱动
/// DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin }。
///
/// 系统不支持时（远程桌面 / 关闭了透明效果 / 省电模式 / 高对比度）IsSupported 返回 false，
/// 调用方直接不设置 SystemBackdrop，窗口会退回主题纯色背景，无需额外处理。
/// </summary>
public sealed class ThinAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;

    public static bool IsSupported
    {
        get
        {
            try { return DesktopAcrylicController.IsSupported(); }
            catch { return false; }
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (_controller is not null) return;

        try
        {
            // 只指定 Thin 变体；染色与回退色交给系统按当前主题决定，
            // 文字可读性由 XAML 层那层很淡的半透明底色保证。
            _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
            _controller.AddSystemBackdropTarget(connectedTarget);
            _controller.SetSystemBackdropConfiguration(
                GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
        }
        catch (Exception ex)
        {
            // 某些显卡 / 远程会话下创建控制器会抛异常，降级为纯色背景即可
            System.Diagnostics.Debug.WriteLine($"[StarVideo] AcrylicThin 降级: {ex.Message}");
            try { _controller?.Dispose(); } catch { /* 忽略 */ }
            _controller = null;
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        try { _controller?.RemoveSystemBackdropTarget(disconnectedTarget); } catch { /* 忽略 */ }
        try { _controller?.Dispose(); } catch { /* 忽略 */ }
        _controller = null;
    }

    // 主题 / 激活状态变化由 XAML 根元素的默认配置驱动，这里把新配置同步给控制器，
    // 深浅主题切换后亚克力才会跟着变。
    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        try
        {
            base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);

            _controller?.SetSystemBackdropConfiguration(
                GetDefaultSystemBackdropConfiguration(target, xamlRoot));
        }
        catch { /* 配置同步失败不影响窗口 */ }
    }
}
