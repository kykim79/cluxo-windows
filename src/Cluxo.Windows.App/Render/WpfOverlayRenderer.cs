using System.Windows.Threading;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Render;

/// <summary>
/// <see cref="IOverlayRenderer"/> — WPF 오버레이 윈도우 1개를 감싼다.
/// <see cref="Render"/>는 렌더 타이머(UI 스레드)에서 호출되므로 윈도우를 직접 갱신한다.
/// </summary>
internal sealed class WpfOverlayRenderer : IOverlayRenderer
{
    private readonly OverlayWindow _window;
    private readonly Dispatcher _dispatcher;
    private readonly Action<WpfOverlayRenderer> _onDispose;
    private bool _disposed;

    public string MonitorId { get; }

    public WpfOverlayRenderer(string monitorId, OverlayWindow window, Dispatcher dispatcher,
        Action<WpfOverlayRenderer> onDispose)
    {
        MonitorId = monitorId;
        _window = window;
        _dispatcher = dispatcher;
        _onDispose = onDispose;
    }

    public void Render(in OverlayFrame frame) => _window.Element.SetFrame(frame);

    internal void SetClickThrough(bool enabled) => _window.SetClickThrough(enabled);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _onDispose(this);
        _dispatcher.Invoke(_window.Close);
    }
}
