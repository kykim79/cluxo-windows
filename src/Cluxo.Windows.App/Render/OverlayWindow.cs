using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Cluxo.Core.Platform;
using static Cluxo.Windows.App.Render.RenderNativeMethods;

namespace Cluxo.Windows.App.Render;

/// <summary>
/// 한 모니터의 투명·클릭통과·항상위 WPF 오버레이 윈도우(OVERLAY-RENDER.md §1).
/// AllowsTransparency로 per-pixel 알파, 모니터 물리 bounds에 SetWindowPos로 배치.
/// 클릭통과는 WS_EX_TRANSPARENT — 그리기/라디얼 모드에서만 끈다(§6, <see cref="SetClickThrough"/>).
/// </summary>
internal sealed class OverlayWindow : Window
{
    private readonly MonitorInfo _monitor;

    public OverlayElement Element { get; }

    public OverlayWindow(MonitorInfo monitor, Func<double> clock)
    {
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Title = "Cluxo Overlay";

        Element = new OverlayElement(monitor, clock);
        Content = Element;

        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        UpdateExStyle(hwnd,
            add: WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
            remove: 0);

        var b = _monitor.Bounds;
        SetWindowPos(hwnd, HWND_TOPMOST, (int)b.X, (int)b.Y, (int)b.Width, (int)b.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>클릭통과 토글(P1) — 그리기/라디얼 모드 진입 시 OFF로 입력 캡처.</summary>
    public void SetClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (enabled) UpdateExStyle(hwnd, WS_EX_TRANSPARENT, 0);
        else UpdateExStyle(hwnd, 0, WS_EX_TRANSPARENT);
    }
}
