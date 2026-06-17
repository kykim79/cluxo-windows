using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Cluxo.Core;
using Cluxo.Core.Platform;
using Cluxo.Windows.App.Ui;

namespace Cluxo.Windows.App.Render;

/// <summary>
/// WPF 오버레이 호스트 — 전용 STA 스레드 + Dispatcher 위에서 모니터별 오버레이 윈도우를 띄우고
/// ~60Hz 렌더 루프로 <c>coordinator.RenderFrame()</c>을 구동한다(OVERLAY-RENDER.md §3).
///
/// 스레드 모델: 코디네이터의 RenderFrame이 이 UI 스레드(렌더 타이머)에서 돌아 renderer.Render가
/// 같은 스레드에서 WPF 비주얼을 직접 갱신한다. factory.Create/renderer.Dispose는 다른 스레드에서
/// 와도 Dispatcher로 마샬링한다.
///
/// 종료 순서(Program): StopRenderLoop() → coordinator.Dispose() → host.Dispose(). 렌더 루프를 먼저
/// 멈춰야 UI 스레드가 coordinator._gate를 다투지 않는다(Dispose 데드락 회피).
/// </summary>
public sealed class WpfOverlayHost : IDisposable
{
    private readonly Thread _thread;
    private Dispatcher _dispatcher = null!;
    private Application? _app;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly List<WpfOverlayRenderer> _renderers = new();
    private DispatcherTimer? _timer;
    private Func<bool>? _capturesInput;
    private Func<MagnifierState?>? _magnifierProvider;
    private Func<bool>? _screenshotMode;
    private MagnifierWindow? _magnifier;
    private bool _lastCapturesInput;
    private bool _lastScreenshotMode;
    private bool _screenshotApplied;
    private bool _disposed;

    public IOverlayRendererFactory Factory { get; }

    public WpfOverlayHost(Func<double> clock)
    {
        _thread = new Thread(UiThread) { Name = "Cluxo.Overlay", IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
        Factory = new WpfOverlayRendererFactory(_dispatcher, clock, Track, Untrack);
    }

    private void UiThread()
    {
        _app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        _dispatcher = Dispatcher.CurrentDispatcher;
        _ready.Set();
        _app.Run(); // 디스패처 펌프 — Shutdown까지 블록
    }

    private void Track(WpfOverlayRenderer r) { lock (_renderers) _renderers.Add(r); }
    private void Untrack(WpfOverlayRenderer r) { lock (_renderers) _renderers.Remove(r); }

    /// <summary>
    /// UI 스레드에서 ~60Hz로 renderFrame 구동. capturesInput을 주면 그리기/라디얼 모드에서
    /// 클릭통과를 끈다(P1, 상태 변경 시에만 토글).
    /// </summary>
    public void StartRenderLoop(Action renderFrame, Func<bool>? capturesInput = null, Func<MagnifierState?>? magnifierProvider = null, Func<bool>? screenshotMode = null)
    {
        _capturesInput = capturesInput;
        _magnifierProvider = magnifierProvider;
        _screenshotMode = screenshotMode;
        _dispatcher.Invoke(() =>
        {
            if (_magnifierProvider is not null) _magnifier = new MagnifierWindow(); // 메시지 펌프 있는 UI 스레드에서 생성
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) =>
            {
                ApplyClickThrough();
                ApplyScreenshotMode();
                renderFrame();
                UpdateMagnifier();
            };
            _timer.Start();
        });
    }

    // 스크린샷 모드 — true면 외부 캡처(OBS·스크린샷)에서 오버레이 제외. 변경 시에만 적용.
    private void ApplyScreenshotMode()
    {
        if (_screenshotMode is null) return;
        bool excluded = _screenshotMode();
        if (_screenshotApplied && excluded == _lastScreenshotMode) return;
        _lastScreenshotMode = excluded; _screenshotApplied = true;
        lock (_renderers)
            foreach (var r in _renderers) r.SetCaptureExcluded(excluded);
    }

    // 돋보기 — 코디네이터 상태를 폴링해 Magnification 창을 구동. 우리 오버레이 창들은 확대 대상에서 제외.
    private void UpdateMagnifier()
    {
        if (_magnifier is null || _magnifierProvider is null) return;
        var state = _magnifierProvider();
        if (state is { } m)
        {
            IntPtr[] exclude;
            lock (_renderers) exclude = _renderers.Select(r => r.Hwnd).Where(h => h != IntPtr.Zero).ToArray();
            int colorRef = m.Border.R | (m.Border.G << 8) | (m.Border.B << 16); // Rgba → COLORREF(0x00BBGGRR)
            _magnifier.Update((int)Math.Round(m.CursorPhysical.X), (int)Math.Round(m.CursorPhysical.Y),
                m.Zoom, (int)Math.Round(m.LensPhysical), colorRef, exclude);
        }
        else
        {
            _magnifier.Hide();
        }
    }

    private void ApplyClickThrough()
    {
        if (_capturesInput is null) return;
        bool captures = _capturesInput();
        if (captures == _lastCapturesInput) return; // 변경 시에만(매 프레임 SetWindowLong 회피)
        _lastCapturesInput = captures;
        lock (_renderers)
            foreach (var r in _renderers) r.SetClickThrough(!captures);
    }

    public void StopRenderLoop() => _dispatcher.Invoke(() => _timer?.Stop());

    private Window? _settingsWindow;

    /// <summary>설정창을 WPF 스레드에서 띄운다(단일 인스턴스 — 이미 열려 있으면 앞으로).</summary>
    public void ShowSettings(CursorSettings settings, ILaunchAtLogin launch)
        => _dispatcher.Invoke(() =>
        {
            if (_settingsWindow is { IsVisible: true } w) { w.Activate(); return; }
            _settingsWindow = new SettingsWindow(settings, launch);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dispatcher.Invoke(() => { _timer?.Stop(); _magnifier?.Dispose(); _app?.Shutdown(); });
        _thread.Join(1000); // WPF Application.Run 종료 — 정상은 ~수ms
        _ready.Dispose();
    }
}
