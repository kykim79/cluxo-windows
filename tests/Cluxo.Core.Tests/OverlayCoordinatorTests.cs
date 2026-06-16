using Cluxo.Core;
using Cluxo.Core.Platform;
using Xunit;

namespace Cluxo.Core.Tests;

// OverlayCoordinator 스켈레톤 — 모든 플랫폼 의존성을 fake로 채워 배선을 end-to-end 검증.
// (인터페이스에만 의존하므로 Windows 없이 맥에서 전체 흐름 테스트 가능)
public class OverlayCoordinatorTests
{
    // ── Fakes ───────────────────────────────────────────────────

    private sealed class FakeMouseHook : IMouseHook
    {
        public event Action<MouseButton, PointD>? ButtonDown;
        public event Action<MouseButton, PointD>? ButtonUp;
        public event Action<ScrollDelta, PointD>? Scrolled;
        public event Action? HookRemoved;
        public bool Started;
        public void Start() => Started = true;
        public void Stop() => Started = false;
        public void Dispose() { }
        public void Down(MouseButton b, PointD p) => ButtonDown?.Invoke(b, p);
        public void Up(MouseButton b, PointD p) => ButtonUp?.Invoke(b, p);
        public void Scroll(ScrollDelta d, PointD p) => Scrolled?.Invoke(d, p);
        public void Remove() => HookRemoved?.Invoke();
    }

    private sealed class FakeKeyboardHook : IKeyboardHook
    {
        public event Action<KeyEvent>? KeyPressed;
        public bool Started;
        public void Start() => Started = true;
        public void Stop() => Started = false;
        public void Dispose() { }
        public void Press(KeyEvent e) => KeyPressed?.Invoke(e);
    }

    private sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
    {
        private readonly Dictionary<HotkeyChord, Action> _regs = new();
        public IDisposable Register(HotkeyChord chord, Action onPressed)
        {
            _regs[chord] = onPressed;
            return new Reg(() => _regs.Remove(chord));
        }
        public void Press(HotkeyChord chord) { if (_regs.TryGetValue(chord, out var a)) a(); }
        public void Dispose() { }
        private sealed class Reg : IDisposable
        {
            private readonly Action _onDispose;
            public Reg(Action onDispose) => _onDispose = onDispose;
            public void Dispose() => _onDispose();
        }
    }

    private sealed class FakeCursorSource : ICursorPositionSource
    {
        public PointD Position;
        public PointD GetCursorPosition() => Position;
    }

    private sealed class FakeMonitorProvider : IMonitorProvider
    {
        public List<MonitorInfo> List = new();
        public IReadOnlyList<MonitorInfo> Monitors => List;
        public event Action? MonitorsChanged;
        public void Raise() => MonitorsChanged?.Invoke();
    }

    private sealed class FakeRenderer : IOverlayRenderer
    {
        public string MonitorId { get; }
        public OverlayFrame? Last { get; private set; }
        public bool Disposed { get; private set; }
        public FakeRenderer(string id) => MonitorId = id;
        public void Render(in OverlayFrame frame) => Last = frame;
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeRendererFactory : IOverlayRendererFactory
    {
        public readonly Dictionary<string, FakeRenderer> Created = new();
        public IOverlayRenderer Create(MonitorInfo monitor)
        {
            var r = new FakeRenderer(monitor.Id);
            Created[monitor.Id] = r;
            return r;
        }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public JsonSettingsStore Store = new();
        public int SaveCount;
        public JsonSettingsStore Load() => Store;
        public void Save(JsonSettingsStore store) { Store = store; SaveCount++; }
    }

    private sealed class FakeBranding : IBrandingProvider
    {
        public BrandingConfig Current { get; set; } = BrandingConfig.Default;
    }

    private sealed class FakeForeground : IForegroundAppMonitor
    {
        public ForegroundApp Current { get; set; }
        public event Action<ForegroundApp>? Changed;
        public bool Started;
        public void Start() => Started = true;
        public void Stop() => Started = false;
        public void Dispose() { }
        public void Raise(ForegroundApp a) { Current = a; Changed?.Invoke(a); }
    }

    private sealed class FakeClock : IClock { public double NowSeconds { get; set; } }

    private sealed class FakeRadialTrigger : IRadialTrigger
    {
        public event Action? Opened;
        public event Action? Closed;
        public void Open() => Opened?.Invoke();
        public void Close() => Closed?.Invoke();
        public void Dispose() { }
    }

    // ── Harness ─────────────────────────────────────────────────

    private sealed class Harness
    {
        public readonly FakeMouseHook Mouse = new();
        public readonly FakeKeyboardHook Keyboard = new();
        public readonly FakeHotkeyRegistrar Hotkeys = new();
        public readonly FakeCursorSource Cursor = new();
        public readonly FakeMonitorProvider Monitors = new();
        public readonly FakeRendererFactory Factory = new();
        public readonly FakeSettingsStore Settings = new();
        public readonly FakeBranding Branding = new();
        public readonly FakeForeground Foreground = new();
        public readonly FakeRadialTrigger Radial = new();
        public readonly FakeClock Clock = new();
        public readonly OverlayCoordinator Coordinator;

        public static readonly HotkeyChord DrawToggle = new(KeyModifiers.Control | KeyModifiers.Alt, "D");
        public static readonly MonitorInfo MonA = new("A", new RectD(0, 0, 1920, 1080), 1.0, true);
        public static readonly MonitorInfo MonB = new("B", new RectD(1920, 0, 1920, 1080), 1.0, false);

        public Harness(params MonitorInfo[] monitors)
        {
            Monitors.List.AddRange(monitors.Length == 0 ? new[] { MonA } : monitors);
            Coordinator = new OverlayCoordinator(Mouse, Keyboard, Hotkeys, Cursor, Monitors,
                Factory, Settings, Branding, Foreground, Radial, Clock);
        }

        public void EnterDrawingMode() => Hotkeys.Press(DrawToggle);
    }

    // ── Tests ───────────────────────────────────────────────────

    [Fact]
    public void Start_CreatesRendererPerMonitor_AndStartsHooks()
    {
        var h = new Harness(Harness.MonA, Harness.MonB);
        h.Coordinator.Start();
        Assert.Equal(2, h.Factory.Created.Count);
        Assert.True(h.Mouse.Started);
        Assert.True(h.Keyboard.Started);
        Assert.True(h.Foreground.Started);
    }

    [Fact]
    public void DrawingHotkey_TogglesMode()
    {
        var h = new Harness();
        h.Coordinator.Start();
        Assert.False(h.Coordinator.IsDrawingModeActive);
        h.EnterDrawingMode();
        Assert.True(h.Coordinator.IsDrawingModeActive);
    }

    [Fact]
    public void DrawDrag_FollowsFrameSampledPosition_ThenCommits()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.EnterDrawingMode();

        h.Mouse.Down(MouseButton.Left, new PointD(10, 10)); // StartShape(p0)
        h.Cursor.Position = new PointD(50, 50);              // 프레임 샘플 위치
        h.Coordinator.RenderFrame();                         // UpdateShape(p1) — 하이브리드
        h.Mouse.Up(MouseButton.Left, new PointD(50, 50));    // EndShape

        var shapes = h.Coordinator.DrawingShapes;
        Assert.Single(shapes);
        Assert.Equal(DrawingTool.Pen, shapes[0].Tool);
        Assert.Contains(new PointD(10, 10), shapes[0].Points);
        Assert.Contains(new PointD(50, 50), shapes[0].Points);
    }

    [Fact]
    public void DrawDrag_InProgressShape_ShowsInFrame_BeforeCommit()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.EnterDrawingMode();

        h.Mouse.Down(MouseButton.Left, new PointD(10, 10)); // StartShape — 아직 미커밋
        h.Cursor.Position = new PointD(50, 50);
        h.Coordinator.RenderFrame();                         // UpdateShape + 프레임에 라이브 프리뷰

        // 커밋 전: DrawingShapes(커밋분)는 비었지만 프레임엔 진행 중 stroke가 보인다
        Assert.Empty(h.Coordinator.DrawingShapes);
        var live = h.Factory.Created["A"].Last!.Value.Shapes;
        Assert.Single(live);
        Assert.Contains(new PointD(10, 10), live[0].Points);
        Assert.Contains(new PointD(50, 50), live[0].Points);

        // 커밋 후: 1개만(라이브 프리뷰가 커밋분과 중복되지 않음 — CurrentShape=null)
        h.Mouse.Up(MouseButton.Left, new PointD(50, 50));
        h.Coordinator.RenderFrame();
        Assert.Single(h.Coordinator.DrawingShapes);
        Assert.Single(h.Factory.Created["A"].Last!.Value.Shapes);
    }

    [Fact]
    public void RenderFrame_CursorOnlyOnContainingMonitor()
    {
        var h = new Harness(Harness.MonA, Harness.MonB);
        h.Coordinator.Start();
        h.Cursor.Position = new PointD(2000, 500); // 모니터 B 영역
        h.Coordinator.RenderFrame();

        Assert.Null(h.Factory.Created["A"].Last!.Value.CursorPosition);
        Assert.Equal(new PointD(2000, 500), h.Factory.Created["B"].Last!.Value.CursorPosition);
    }

    [Fact]
    public void KeyEscape_InDrawingMode_ClearsAndExits()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.EnterDrawingMode();
        h.Mouse.Down(MouseButton.Left, new PointD(0, 0));
        h.Cursor.Position = new PointD(5, 5);
        h.Coordinator.RenderFrame();
        h.Mouse.Up(MouseButton.Left, new PointD(5, 5)); // 1 committed shape

        h.Keyboard.Press(new KeyEvent(KeyModifiers.None, SpecialKey.Escape, null));

        Assert.Empty(h.Coordinator.DrawingShapes);
        Assert.False(h.Coordinator.IsDrawingModeActive);
    }

    [Fact]
    public void BracketKey_InDrawingMode_AdjustsAndPersistsLineWidth()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.EnterDrawingMode();
        Assert.Equal(4, h.Coordinator.LineWidth);

        h.Keyboard.Press(new KeyEvent(KeyModifiers.None, null, "]")); // 두께 증가

        Assert.Equal(6, h.Coordinator.LineWidth);
        Assert.True(h.Settings.SaveCount >= 1);
        Assert.Equal(6.0, h.Settings.Store.Get("drawing.lineWidth", 0.0)); // 영구화까지 확인
    }

    [Fact]
    public void HookRemoved_RaisesMouseHookLost()
    {
        var h = new Harness();
        bool notified = false;
        h.Coordinator.MouseHookLost += () => notified = true;
        h.Coordinator.Start();
        h.Mouse.Remove();
        Assert.True(notified);
    }

    [Fact]
    public void MonitorsChanged_AddsAndRemovesRenderers()
    {
        var h = new Harness(Harness.MonA);
        h.Coordinator.Start();
        Assert.Single(h.Factory.Created);

        // B 추가
        h.Monitors.List.Add(Harness.MonB);
        h.Monitors.Raise();
        Assert.Equal(2, h.Factory.Created.Count);

        // A 제거 → A 렌더러 dispose
        h.Monitors.List.RemoveAll(m => m.Id == "A");
        h.Monitors.Raise();
        Assert.True(h.Factory.Created["A"].Disposed);
    }

    [Fact]
    public void Dispose_SavesSettings_AndStopsHooks()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Coordinator.Dispose();
        Assert.True(h.Settings.SaveCount >= 1);
        Assert.False(h.Mouse.Started);
        Assert.False(h.Foreground.Started);
    }

    // ── 효과 배선 ───────────────────────────────────────────────

    [Fact]
    public void LeftClick_NotDrawing_AddsClickEffect()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Mouse.Down(MouseButton.Left, new PointD(100, 100)); // 모니터 A 영역
        h.Coordinator.RenderFrame();
        Assert.Single(h.Factory.Created["A"].Last!.Value.Effects.Clicks);
    }

    [Fact]
    public void DrawingMode_SuppressesClickEffect()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.EnterDrawingMode();
        h.Mouse.Down(MouseButton.Left, new PointD(100, 100)); // 그리기 start, 클릭 효과 X
        h.Coordinator.RenderFrame();
        Assert.Empty(h.Factory.Created["A"].Last!.Value.Effects.Clicks);
        Assert.True(h.Coordinator.IsDrawingModeActive);
    }

    [Fact]
    public void RightClick_AddsClickEffect_MarkedRight()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Mouse.Down(MouseButton.Right, new PointD(100, 100));
        h.Coordinator.RenderFrame();
        var clicks = h.Factory.Created["A"].Last!.Value.Effects.Clicks;
        Assert.Single(clicks);
        Assert.True(clicks[0].IsRight);
    }

    [Fact]
    public void DoubleClick_DetectedWithinWindow()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Clock.NowSeconds = 0.0;
        h.Mouse.Down(MouseButton.Left, new PointD(100, 100)); // 1st
        h.Clock.NowSeconds = 0.1;
        h.Mouse.Down(MouseButton.Left, new PointD(101, 100)); // 2nd, 근처·0.4초 내
        h.Coordinator.RenderFrame();
        Assert.NotEmpty(h.Factory.Created["A"].Last!.Value.Effects.DoubleClicks);
    }

    [Fact]
    public void Scroll_NotDrawing_AddsScrollEffect()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Mouse.Scroll(new ScrollDelta(0, 5), new PointD(100, 100));
        h.Coordinator.RenderFrame();
        var scrolls = h.Factory.Created["A"].Last!.Value.Effects.Scrolls;
        Assert.Single(scrolls);
        Assert.True(scrolls[0].IsVertical);
        Assert.True(scrolls[0].IsPositive);
    }

    [Fact]
    public void Shake_DetectedThroughRenderFrames_AddsShakeEffect()
    {
        var h = new Harness();
        h.Coordinator.Start();
        double[] xs = { 0, 100, 0, 100, 0, 100, 0 };
        foreach (var x in xs)
        {
            h.Cursor.Position = new PointD(x, 0);
            h.Coordinator.RenderFrame();   // shake.Record + (감지 시) AddShake
            h.Clock.NowSeconds += 0.05;
        }
        Assert.NotEmpty(h.Factory.Created["A"].Last!.Value.Effects.Shakes);
    }

    // ── 효과 토글 게이팅 ────────────────────────────────────────

    [Fact]
    public void Trail_Enabled_ProducesTrail()
    {
        var h = new Harness();
        h.Settings.Store.Set("isTrailEnabled", true);
        h.Coordinator.Start();
        foreach (var x in new[] { 100, 110, 120 })
        {
            h.Cursor.Position = new PointD(x, 100);
            h.Coordinator.RenderFrame();
        }
        Assert.NotEmpty(h.Factory.Created["A"].Last!.Value.Effects.Trail);
    }

    [Fact]
    public void Trail_OffByDefault_NoTrail()
    {
        var h = new Harness();
        h.Coordinator.Start(); // isTrailEnabled 기본 false
        foreach (var x in new[] { 100, 110, 120 })
        {
            h.Cursor.Position = new PointD(x, 100);
            h.Coordinator.RenderFrame();
        }
        Assert.Empty(h.Factory.Created["A"].Last!.Value.Effects.Trail);
    }

    [Fact]
    public void Scroll_Disabled_NoEffect()
    {
        var h = new Harness();
        h.Settings.Store.Set("scrollIndicator", false);
        h.Coordinator.Start();
        h.Mouse.Scroll(new ScrollDelta(0, 5), new PointD(100, 100));
        h.Coordinator.RenderFrame();
        Assert.Empty(h.Factory.Created["A"].Last!.Value.Effects.Scrolls);
    }

    [Fact]
    public void Shake_Disabled_NoEffect()
    {
        var h = new Harness();
        h.Settings.Store.Set("isShakeEnabled", false);
        h.Coordinator.Start();
        foreach (var x in new double[] { 0, 100, 0, 100, 0, 100, 0 })
        {
            h.Cursor.Position = new PointD(x, 0);
            h.Coordinator.RenderFrame();
            h.Clock.NowSeconds += 0.05;
        }
        Assert.Empty(h.Factory.Created["A"].Last!.Value.Effects.Shakes);
    }

    [Fact]
    public void Ring_Glow_FlagFromSetting()
    {
        var h = new Harness();
        h.Settings.Store.Set("isGlowEnabled", true);
        h.Coordinator.Start();
        h.Cursor.Position = new PointD(100, 100);
        h.Coordinator.RenderFrame();
        Assert.True(h.Factory.Created["A"].Last!.Value.Ring!.Value.Glow);
    }

    [Fact]
    public void IdlePulse_AfterTimeout_WhenStationary()
    {
        var h = new Harness(); // isIdlePulseEnabled 기본 true, idleTimeout 3.0
        h.Coordinator.Start();
        h.Cursor.Position = new PointD(100, 100);
        h.Clock.NowSeconds = 0; h.Coordinator.RenderFrame();   // idle anchor at t=0
        h.Clock.NowSeconds = 3.0; h.Coordinator.RenderFrame(); // 정지 3초 → 펄스
        Assert.NotEmpty(h.Factory.Created["A"].Last!.Value.Effects.IdlePulses);
    }

    [Fact]
    public void IdlePulse_ResetsOnMovement()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Cursor.Position = new PointD(100, 100);
        h.Clock.NowSeconds = 0; h.Coordinator.RenderFrame();
        h.Cursor.Position = new PointD(300, 100); // 움직임 → 재무장
        h.Clock.NowSeconds = 2.0; h.Coordinator.RenderFrame();
        h.Clock.NowSeconds = 4.0; h.Coordinator.RenderFrame(); // 마지막 움직임(t=2)에서 2초 — 아직 미달
        Assert.Empty(h.Factory.Created["A"].Last!.Value.Effects.IdlePulses);
    }

    [Fact]
    public void IdlePulse_Disabled_NoPulse()
    {
        var h = new Harness();
        h.Settings.Store.Set("isIdlePulseEnabled", false);
        h.Coordinator.Start();
        h.Cursor.Position = new PointD(100, 100);
        h.Clock.NowSeconds = 0; h.Coordinator.RenderFrame();
        h.Clock.NowSeconds = 3.0; h.Coordinator.RenderFrame();
        Assert.Empty(h.Factory.Created["A"].Last!.Value.Effects.IdlePulses);
    }

    [Fact]
    public void Keystroke_OffByDefault_NotShown()
    {
        var h = new Harness();
        h.Coordinator.Start(); // isKeystrokeEnabled 기본 false
        h.Keyboard.Press(new KeyEvent(KeyModifiers.Control, null, "c"));
        h.Coordinator.RenderFrame();
        Assert.Null(h.Coordinator.Keystroke);
    }

    // ── 키스트로크 오버레이 배선 ─────────────────────────────────

    [Fact]
    public void KeyPressed_WithModifier_ShowsKeystroke()
    {
        var h = new Harness();
        h.Settings.Store.Set("isKeystrokeEnabled", true); // 기본 OFF — 설정 ON
        h.Coordinator.Start();
        h.Keyboard.Press(new KeyEvent(KeyModifiers.Control, null, "c"));
        h.Coordinator.RenderFrame();
        Assert.Equal("Ctrl+C", h.Coordinator.Keystroke);
        Assert.Equal("Ctrl+C", h.Factory.Created["A"].Last!.Value.Keystroke);
    }

    [Fact]
    public void KeyPressed_NoModifier_NoKeystroke()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Keyboard.Press(new KeyEvent(KeyModifiers.None, null, "a")); // 단순 타이핑 → 표시 X
        h.Coordinator.RenderFrame();
        Assert.Null(h.Coordinator.Keystroke);
    }

    [Fact]
    public void DrawingToggle_ShowsStatusNotification()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.EnterDrawingMode();
        h.Coordinator.RenderFrame();
        Assert.Equal("그리기 모드 ON", h.Coordinator.Keystroke);
    }

    // ── CursorSettings 배선 ──────────────────────────────────────

    [Fact]
    public void Ring_UsesSettings_ColorSizeOpacity()
    {
        var h = new Harness(); // 기본: Cyan, Medium(54)
        h.Coordinator.Start();
        h.Cursor.Position = new PointD(100, 100); // 모니터 A
        h.Coordinator.RenderFrame();
        var ring = h.Factory.Created["A"].Last!.Value.Ring;
        Assert.NotNull(ring);
        Assert.Equal(RingColor.Cyan.Color(), ring!.Value.Color);
        Assert.Equal(RingSize.Medium.Diameter() / 2, ring.Value.Radius); // 27
        Assert.Equal(1.0, ring.Value.Opacity);
        Assert.Equal(RingShape.Circle, ring.Value.Shape);                 // 기본 모양
        Assert.Equal(BorderWeight.Thin.LineWidth(), ring.Value.BorderWidth); // 기본 두께 1.5
        Assert.False(ring.Value.Dashed);
        Assert.False(ring.Value.Glow); // 기본 글로우 OFF
    }

    [Fact]
    public void Ring_UsesSettings_ShapeWeightStyle()
    {
        var h = new Harness();
        h.Settings.Store.Set("ringShape", RingShape.Hexagon);
        h.Settings.Store.Set("borderWeight", BorderWeight.Bold);
        h.Settings.Store.Set("borderStyle", BorderStyle.Dashed);
        h.Coordinator.Start();
        h.Cursor.Position = new PointD(100, 100); // 모니터 A
        h.Coordinator.RenderFrame();

        var ring = h.Factory.Created["A"].Last!.Value.Ring!.Value;
        Assert.Equal(RingShape.Hexagon, ring.Shape);
        Assert.Equal(BorderWeight.Bold.LineWidth(), ring.BorderWidth); // 5.5
        Assert.True(ring.Dashed);
    }

    [Fact]
    public void DrawStroke_UsesEffectiveRingColor_FromSettings()
    {
        var h = new Harness();
        h.Settings.Store.Set("ringColor", RingColor.Red); // Start 전 프리셋
        h.Coordinator.Start();
        h.EnterDrawingMode();
        h.Mouse.Down(MouseButton.Left, new PointD(0, 0));
        h.Cursor.Position = new PointD(5, 5);
        h.Coordinator.RenderFrame();
        h.Mouse.Up(MouseButton.Left, new PointD(5, 5));
        Assert.Equal(RingColor.Red.Color(), h.Coordinator.DrawingShapes[0].Color);
    }

    [Fact]
    public void ShakeSensitivity_Sensitive_DetectsWithFewerChanges()
    {
        var h = new Harness();
        h.Settings.Store.Set("shakeSensitivity", ShakeSensitivity.Sensitive); // 전환 3회
        h.Coordinator.Start();
        double[] xs = { 0, 100, 0, 100, 0 }; // 전환 3회 (보통=5라면 미감지)
        foreach (var x in xs)
        {
            h.Cursor.Position = new PointD(x, 0);
            h.Coordinator.RenderFrame();
            h.Clock.NowSeconds += 0.05;
        }
        Assert.NotEmpty(h.Factory.Created["A"].Last!.Value.Effects.Shakes);
    }

    [Fact]
    public void KeystrokeTimeout_ComesFromSettings_Default3s()
    {
        var h = new Harness();
        h.Settings.Store.Set("isKeystrokeEnabled", true); // 기본 OFF — 설정 ON
        h.Coordinator.Start();
        h.Clock.NowSeconds = 0;
        h.Keyboard.Press(new KeyEvent(KeyModifiers.Control, null, "c"));
        h.Coordinator.RenderFrame();
        h.Clock.NowSeconds = 1.5; // 옛 하드코딩(1.5)이면 여기서 숨음 — 설정 기본 3.0이라 유지
        h.Coordinator.RenderFrame();
        Assert.Equal("Ctrl+C", h.Coordinator.Keystroke);
        h.Clock.NowSeconds = 3.0;
        h.Coordinator.RenderFrame();
        Assert.Null(h.Coordinator.Keystroke);
    }

    [Fact]
    public void ClickEffectLifetime_ScaledByAnimationSpeed()
    {
        var h = new Harness();
        h.Settings.Store.Set("animationSpeed", AnimationSpeed.Slow); // ×1.7
        h.Coordinator.Start();
        h.Clock.NowSeconds = 0;
        h.Mouse.Down(MouseButton.Left, new PointD(100, 100));
        h.Coordinator.RenderFrame();
        var click = h.Factory.Created["A"].Last!.Value.Effects.Clicks[0];
        Assert.Equal(0.7 * 1.7, click.ExpiresAt, 6);
    }

    // ── 라디얼 메뉴 배선 ─────────────────────────────────────────

    [Fact]
    public void Radial_Open_ActivatesAndTracksSelection()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Cursor.Position = new PointD(0, 0); // 중심
        h.Radial.Open();
        Assert.True(h.Coordinator.IsRadialMenuActive);

        h.Cursor.Position = new PointD(0, -80); h.Clock.NowSeconds = 0.2; // 화면-위(12시) 메인 → sector 0
        h.Coordinator.RenderFrame();
        var radial = h.Factory.Created["A"].Last!.Value.Radial;
        Assert.NotNull(radial);
        Assert.True(radial!.Value.Visible);   // reveal threshold(0.15) 경과
        Assert.Equal(0, radial.Value.Sector); // Spotlight
    }

    [Fact]
    public void Radial_Close_ExecutesSelection()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Cursor.Position = new PointD(0, 0);
        h.Radial.Open();
        h.Cursor.Position = new PointD(0, -80); h.Clock.NowSeconds = 0.2;
        h.Coordinator.RenderFrame();        // sector 0 (Spotlight) 메인 선택
        h.Radial.Close();                   // 실행 → 스포트라이트 토글
        Assert.True(h.Coordinator.IsSpotlightActive);
        Assert.False(h.Coordinator.IsRadialMenuActive); // 닫힘
    }

    [Fact]
    public void Radial_SuppressesClickEffects()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Radial.Open();
        h.Mouse.Down(MouseButton.Left, new PointD(100, 100)); // 라디얼 중 클릭 무시
        h.Coordinator.RenderFrame();
        Assert.Empty(h.Factory.Created["A"].Last!.Value.Effects.Clicks);
    }

    // ── CursorRuntimeState 배선 ──────────────────────────────────

    [Fact]
    public void NonDrawingDrag_TracksVelocityAndAnchoredLine()
    {
        var h = new Harness();
        h.Settings.Store.Set("isAnchoredLineEnabled", true); // 기본 OFF — 설정 ON
        h.Coordinator.Start();
        h.Clock.NowSeconds = 0;
        h.Mouse.Down(MouseButton.Left, new PointD(0, 0)); // StartDrag

        h.Cursor.Position = new PointD(100, 0); h.Clock.NowSeconds = 0.1;
        h.Coordinator.RenderFrame();
        h.Cursor.Position = new PointD(200, 0); h.Clock.NowSeconds = 0.2; // 원점서 200 > 100
        h.Coordinator.RenderFrame();

        Assert.True(h.Coordinator.IsDragging);
        Assert.True(h.Coordinator.DragVelocity > 0);
        Assert.True(h.Coordinator.AnchoredLineVisible);

        h.Mouse.Up(MouseButton.Left, new PointD(200, 0));
        Assert.False(h.Coordinator.IsDragging);
        Assert.False(h.Coordinator.AnchoredLineVisible);
    }

    [Fact]
    public void NonDrawingDrag_PutsDragVisualInFrame()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Mouse.Down(MouseButton.Left, new PointD(0, 0));
        h.Cursor.Position = new PointD(100, 0);
        h.Coordinator.RenderFrame();

        var drag = h.Factory.Created["A"].Last!.Value.Drag;
        Assert.NotNull(drag);
        Assert.Equal(new PointD(0, 0), drag!.Value.Origin);
        Assert.Equal(new PointD(100, 0), drag.Value.Current);
        Assert.False(drag.Value.ShowAngleLabel); // 기본 OFF
    }

    [Fact]
    public void DragVisual_ShowAngleLabel_FromSetting()
    {
        var h = new Harness();
        h.Settings.Store.Set("isDragAngleLabelEnabled", true);
        h.Coordinator.Start();
        h.Mouse.Down(MouseButton.Left, new PointD(0, 0));
        h.Cursor.Position = new PointD(100, 0);
        h.Coordinator.RenderFrame();
        Assert.True(h.Factory.Created["A"].Last!.Value.Drag!.Value.ShowAngleLabel);
    }

    [Fact]
    public void DrawingMode_NoDragVisual()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.EnterDrawingMode();
        h.Mouse.Down(MouseButton.Left, new PointD(0, 0)); // 그리기 — runtime 드래그 아님
        h.Cursor.Position = new PointD(100, 0);
        h.Coordinator.RenderFrame();
        Assert.Null(h.Factory.Created["A"].Last!.Value.Drag);
    }

    [Fact]
    public void InspectorHotkey_Toggles_AndNotifies()
    {
        var h = new Harness();
        h.Coordinator.Start();
        Assert.False(h.Coordinator.IsInspectorActive);
        h.Hotkeys.Press(new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, "I"));
        Assert.True(h.Coordinator.IsInspectorActive);
        h.Coordinator.RenderFrame();
        Assert.Equal("좌표 표시 ON", h.Coordinator.Keystroke);
    }

    [Fact]
    public void Inspector_Active_SetsFrameFlag()
    {
        var h = new Harness();
        h.Coordinator.Start();
        h.Cursor.Position = new PointD(100, 100); // 모니터 A
        h.Coordinator.RenderFrame();
        Assert.False(h.Factory.Created["A"].Last!.Value.Inspector); // 기본 OFF

        h.Hotkeys.Press(new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, "I")); // 토글 ON
        h.Coordinator.RenderFrame();
        Assert.True(h.Factory.Created["A"].Last!.Value.Inspector);
    }
}
