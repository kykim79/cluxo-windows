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
        public readonly FakeClock Clock = new();
        public readonly OverlayCoordinator Coordinator;

        public static readonly HotkeyChord DrawToggle = new(KeyModifiers.Control | KeyModifiers.Alt, "D");
        public static readonly MonitorInfo MonA = new("A", new RectD(0, 0, 1920, 1080), 1.0, true);
        public static readonly MonitorInfo MonB = new("B", new RectD(1920, 0, 1920, 1080), 1.0, false);

        public Harness(params MonitorInfo[] monitors)
        {
            Monitors.List.AddRange(monitors.Length == 0 ? new[] { MonA } : monitors);
            Coordinator = new OverlayCoordinator(Mouse, Keyboard, Hotkeys, Cursor, Monitors,
                Factory, Settings, Branding, Foreground, Clock);
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
}
