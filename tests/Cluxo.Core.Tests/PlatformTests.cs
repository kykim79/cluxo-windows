using Cluxo.Core;
using Cluxo.Core.Platform;
using Xunit;

namespace Cluxo.Core.Tests;

// 네이티브 계층 인터페이스가 Core와 실제로 물리는지 검증 — 테스트 더블로 시ams 조립 확인.
// (인터페이스 자체엔 로직이 없으니, 가치는 "경계가 컴파일되고 흐름이 연결된다"의 증명)
public class PlatformTests
{
    // ── 테스트 더블 ─────────────────────────────────────────────

    private sealed class FakeClock : IClock
    {
        public double NowSeconds { get; set; }
    }

    private sealed class FakeMouseHook : IMouseHook
    {
        public event Action<MouseButton, PointD>? ButtonDown;
        public event Action<MouseButton, PointD>? ButtonUp;
        public event Action<ScrollDelta, PointD>? Scrolled;
        public event Action? HookRemoved;
        public bool Started { get; private set; }
        public void Start() => Started = true;
        public void Stop() => Started = false;
        public void Dispose() { }
        // 테스트용 발생기
        public void RaiseDown(MouseButton b, PointD p) => ButtonDown?.Invoke(b, p);
        public void RaiseUp(MouseButton b, PointD p) => ButtonUp?.Invoke(b, p);
        public void RaiseScroll(ScrollDelta d, PointD p) => Scrolled?.Invoke(d, p);
        public void RaiseRemoved() => HookRemoved?.Invoke();
    }

    private sealed class CapturingRenderer : IOverlayRenderer
    {
        public string MonitorId { get; }
        public OverlayFrame? Last { get; private set; }
        public CapturingRenderer(string id) => MonitorId = id;
        public void Render(in OverlayFrame frame) => Last = frame;
        public void Dispose() { }
    }

    // ── Clock → ShakeState (시간 주입 시ams) ─────────────────────

    [Fact]
    public void Clock_FeedsShakeState_Detection()
    {
        var clock = new FakeClock();
        var shake = new ShakeState();
        const double dt = 0.05;
        // 좌우 진동 5회 — 시간은 clock에서만 (wall clock 없음)
        double[] xs = { 0, 100, 0, 100, 0, 100, 0 };
        bool detected = false;
        foreach (var x in xs)
        {
            detected = shake.Record(x, 0, clock.NowSeconds) || detected;
            clock.NowSeconds += dt;
        }
        Assert.True(detected, "Clock가 주입한 시간으로 흔들기 감지");
    }

    // ── MouseHook → DrawingState (입력 라우팅 시ams) ─────────────

    [Fact]
    public void MouseHook_ButtonDown_RoutesToDrawingState()
    {
        var hook = new FakeMouseHook();
        var drawing = new DrawingState { IsDrawingModeActive = true };

        // 코디네이터 배선: 좌클릭 down → 그리기 시작
        hook.ButtonDown += (button, point) =>
        {
            if (button == MouseButton.Left)
                drawing.StartShape(point, KeyModifiers.None, Rgba.Red);
        };
        hook.Start();
        hook.RaiseDown(MouseButton.Left, new PointD(42, 24));

        Assert.True(hook.Started);
        Assert.Equal(DrawingTool.Pen, drawing.CurrentShape?.Tool);
        Assert.Equal(new PointD(42, 24), drawing.CurrentShape!.Points[0]);
    }

    [Fact]
    public void MouseHook_HookRemoved_Notifies()
    {
        var hook = new FakeMouseHook();
        bool notified = false;
        hook.HookRemoved += () => notified = true; // T2: 후킹 제거 감지 → 알림
        hook.RaiseRemoved();
        Assert.True(notified);
    }

    // ── OverlayFrame 전달 (렌더 시ams) ──────────────────────────

    [Fact]
    public void OverlayFrame_CarriesCoreState_ToRenderer()
    {
        var drawing = new DrawingState();
        drawing.StartShape(new PointD(0, 0), KeyModifiers.None, Rgba.Red);
        drawing.UpdateShape(new PointD(5, 5));
        drawing.EndShape();

        var renderer = new CapturingRenderer("DISPLAY1");
        var frame = new OverlayFrame(
            MonitorId: "DISPLAY1",
            CursorPosition: new PointD(100, 200),
            Ring: new RingVisual(Rgba.Red, Radius: 24, Scale: 1.0, Opacity: 1.0),
            Shapes: drawing.Shapes,
            Branding: BrandingConfig.Default);
        renderer.Render(in frame);

        Assert.NotNull(renderer.Last);
        Assert.Equal("DISPLAY1", renderer.Last!.Value.MonitorId);
        Assert.Equal(new PointD(100, 200), renderer.Last.Value.CursorPosition);
        Assert.Single(renderer.Last.Value.Shapes);
    }

    // ── DTO 값 의미론 ───────────────────────────────────────────

    [Fact]
    public void HotkeyChord_ValueEquality()
        => Assert.Equal(new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, "D"),
                        new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, "D"));

    [Fact]
    public void KeyEvent_FeedsKeyFormat()
    {
        var e = new KeyEvent(KeyModifiers.Control | KeyModifiers.Alt, SpecialKey.ArrowLeft, null);
        Assert.Equal("Ctrl+Alt+←", KeyFormat.Format(e.Modifiers, e.Special, e.Characters));
    }

    [Fact]
    public void MonitorInfo_ValueEquality()
        => Assert.Equal(new MonitorInfo("M1", new RectD(0, 0, 1920, 1080), 1.0, true),
                        new MonitorInfo("M1", new RectD(0, 0, 1920, 1080), 1.0, true));

    [Fact]
    public void TrayMenuItem_Defaults()
    {
        var item = new TrayMenuItem("toggle", "그리기 모드");
        Assert.False(item.IsChecked);
        Assert.True(item.IsEnabled);
        Assert.False(item.IsSeparatorBefore);
    }
}
