using System.Windows.Threading;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Render;

/// <summary>
/// <see cref="IOverlayRendererFactory"/> — 모니터별 WPF 오버레이 윈도우를 만든다.
/// 코디네이터가 Start/MonitorsChanged 시 호출(다른 스레드일 수 있음) → Dispatcher로 UI 스레드 마샬링.
/// </summary>
internal sealed class WpfOverlayRendererFactory : IOverlayRendererFactory
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<double> _clock;
    private readonly Action<WpfOverlayRenderer> _track;
    private readonly Action<WpfOverlayRenderer> _untrack;

    public WpfOverlayRendererFactory(Dispatcher dispatcher, Func<double> clock,
        Action<WpfOverlayRenderer> track, Action<WpfOverlayRenderer> untrack)
    {
        _dispatcher = dispatcher;
        _clock = clock;
        _track = track;
        _untrack = untrack;
    }

    public IOverlayRenderer Create(MonitorInfo monitor)
        => _dispatcher.Invoke(() =>
        {
            var window = new OverlayWindow(monitor, _clock);
            window.Show();
            var renderer = new WpfOverlayRenderer(monitor.Id, window, _dispatcher, _untrack);
            _track(renderer);
            return (IOverlayRenderer)renderer;
        });
}
