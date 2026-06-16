using Cluxo.Core.Platform;

namespace Cluxo.Core;

/// <summary>
/// 중앙 코디네이터 — Core 상태와 네이티브 플랫폼 인터페이스를 배선한다. (Mac <c>AppDelegate</c> 대응)
///
/// 플랫폼 인터페이스에만 의존 → Windows 없이 테스트 가능. 렌더 루프 타이머는 플랫폼이 소유하고
/// vsync마다 <see cref="RenderFrame"/>을 호출한다(코디네이터는 타이머 미보유).
///
/// 스레딩 규약(설계 발견1·스레드 모델):
///   - 입력 콜백(후킹 스레드)은 _gate 락 안에서 Core 상태만 갱신하고 즉시 반환.
///   - RenderFrame(렌더 스레드)은 락 안에서 상태를 스냅샷하고, 락 밖에서 그린다(GPU 작업 중 락 보유 X).
///   - 그리기 드래그 경로는 후킹이 아니라 프레임 샘플 위치를 따라간다(하이브리드 입력).
///
/// 스켈레톤 범위: 이식된 상태(DrawingState·ShakeState·설정·브랜딩)만 배선. 효과(클릭/스크롤/트레일)·
/// 링 외형·키스트로크 오버레이·발표 감지 동작은 해당 상태 이식 시 연결한다(아래 TODO 주석).
/// </summary>
public sealed class OverlayCoordinator : IDisposable
{
    private readonly IMouseHook _mouse;
    private readonly IKeyboardHook _keyboard;
    private readonly IHotkeyRegistrar _hotkeys;
    private readonly ICursorPositionSource _cursor;
    private readonly IMonitorProvider _monitors;
    private readonly IOverlayRendererFactory _rendererFactory;
    private readonly ISettingsStore _settings;
    private readonly IBrandingProvider _branding;
    private readonly IForegroundAppMonitor _foreground;
    private readonly IClock _clock;

    private readonly DrawingState _drawing = new();
    private readonly ShakeState _shake = new();
    private readonly EffectsState _effects = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, IOverlayRenderer> _renderers = new();
    private readonly List<IDisposable> _hotkeyRegs = new();

    private JsonSettingsStore _store = new();
    private KeyModifiers _modifiers;
    private bool _leftDown;
    private bool _running;
    private bool _disposed;

    // 더블클릭 감지 — 같은 위치 근처 0.4초 내 두 번째 좌클릭.
    private double _lastLeftDownTime = double.NegativeInfinity;
    private PointD _lastLeftDownPos;
    private double _animationSpeed = 1.0; // CursorSettings 이식 시 그쪽에서

    private const string LineWidthKey = "drawing.lineWidth";
    private const double DoubleClickWindow = 0.4;
    private const double DoubleClickRadius = 6;

    /// <summary>그리기 색 — CursorSettings.ringColor 이식 시 그쪽에서. 임시 기본 빨강.</summary>
    public Rgba DrawColor { get; set; } = Rgba.Red;

    /// <summary>마우스 후킹 분실(T2) — 구현이 재설치하고, 코디네이터가 트레이/알림에 surface.</summary>
    public event Action? MouseHookLost;

    public bool IsDrawingModeActive { get { lock (_gate) return _drawing.IsDrawingModeActive; } }
    public double LineWidth { get { lock (_gate) return _drawing.LineWidth; } }
    public IReadOnlyList<DrawingShape> DrawingShapes { get { lock (_gate) return _drawing.Shapes.ToArray(); } }

    public OverlayCoordinator(
        IMouseHook mouse, IKeyboardHook keyboard, IHotkeyRegistrar hotkeys,
        ICursorPositionSource cursor, IMonitorProvider monitors,
        IOverlayRendererFactory rendererFactory, ISettingsStore settings,
        IBrandingProvider branding, IForegroundAppMonitor foreground, IClock clock)
    {
        _mouse = mouse; _keyboard = keyboard; _hotkeys = hotkeys; _cursor = cursor;
        _monitors = monitors; _rendererFactory = rendererFactory; _settings = settings;
        _branding = branding; _foreground = foreground; _clock = clock;
    }

    /// <summary>설정 로드, 단축키 등록, 입력/모니터 구독, 렌더러 생성, 후킹 시작.</summary>
    public void Start()
    {
        if (_running) return;

        _store = _settings.Load();
        lock (_gate) _drawing.LineWidth = _store.Get(LineWidthKey, Tokens.Drawing.LineWidth);

        // ⌃⌥D → 그리기 모드 토글 (Mac과 동일 단축키)
        _hotkeyRegs.Add(_hotkeys.Register(
            new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, "D"), ToggleDrawingMode));

        _mouse.ButtonDown += OnButtonDown;
        _mouse.ButtonUp += OnButtonUp;
        _mouse.Scrolled += OnScrolled;
        _mouse.HookRemoved += OnHookRemoved;
        _keyboard.KeyPressed += OnKeyPressed;
        _foreground.Changed += OnForegroundChanged;
        _monitors.MonitorsChanged += RebuildRenderers;

        RebuildRenderers();
        _mouse.Start();
        _keyboard.Start();
        _foreground.Start();
        _running = true;
    }

    /// <summary>플랫폼이 vsync마다 호출. 상태를 스냅샷(락 안)하고 모니터별로 그린다(락 밖).</summary>
    public void RenderFrame()
    {
        if (!_running) return;
        PointD pos = _cursor.GetCursorPosition();

        double now = _clock.NowSeconds;
        List<(IOverlayRenderer Renderer, OverlayFrame Frame)> batch;
        lock (_gate)
        {
            bool drawing = _drawing.IsDrawingModeActive;
            // 흔들기 감지 — 시간은 IClock에서만(wall clock 없음)
            bool shook = _shake.Record(pos.X, pos.Y, now);

            if (drawing)
            {
                // 그리기 드래그 경로 — 후킹이 아니라 프레임 샘플 위치를 따라간다(하이브리드 입력)
                if (_leftDown) _drawing.UpdateShape(pos);
            }
            else
            {
                // 일시적 효과는 그리기 모드에선 억제(오버레이가 annotation 전용)
                if (shook) _effects.AddShake(pos, now, _animationSpeed);
                _effects.UpdateTrail(pos);
                if (_leftDown) _effects.UpdateDragTrail(pos); // 비-그리기 드래그(창 이동 등) streak
            }

            _effects.Prune(now); // 만료 효과 + 드래그 trail fade 진행
            batch = BuildFrames(pos);
        }

        foreach (var (renderer, frame) in batch)
            renderer.Render(in frame);
    }

    // 락 안에서 호출 — 모니터별 불변 프레임 스냅샷 + 렌더러 참조를 모은다.
    private List<(IOverlayRenderer, OverlayFrame)> BuildFrames(PointD pos)
    {
        var branding = _branding.Current;
        var shapes = _drawing.Shapes.ToArray(); // 렌더 스레드가 가변 리스트 안 보게 스냅샷
        var result = new List<(IOverlayRenderer, OverlayFrame)>(_renderers.Count);

        foreach (var monitor in _monitors.Monitors)
        {
            if (!_renderers.TryGetValue(monitor.Id, out var renderer)) continue;
            var b = monitor.Bounds;
            PointD? cursorHere = b.Contains(pos) ? pos : null;
            // 링 외형은 CursorSettings/RingAppearance 이식 시 — 지금은 토큰 기반 기본값
            RingVisual? ring = cursorHere is null
                ? null
                : new RingVisual(DrawColor, Radius: 24, Scale: 1.0, Opacity: 1.0);
            // 효과는 이 모니터 영역 것만 (Mac의 per-screen 필터). TODO: 프레임당 Where/ToArray 최적화
            var effects = new OverlayEffects(
                _effects.Clicks.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.DoubleClicks.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.Scrolls.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.Shakes.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.IdlePulses.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.Trail.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.DragTrail.Where(e => b.Contains(e.Position)).ToArray());
            result.Add((renderer, new OverlayFrame(monitor.Id, cursorHere, ring, shapes, branding, effects)));
        }
        return result;
    }

    private MonitorInfo? MonitorContaining(PointD point)
    {
        foreach (var m in _monitors.Monitors)
            if (m.Bounds.Contains(point)) return m;
        return null;
    }

    private bool DetectDoubleClick(PointD point, double now)
    {
        double dx = point.X - _lastLeftDownPos.X, dy = point.Y - _lastLeftDownPos.Y;
        bool isDouble = (now - _lastLeftDownTime) <= DoubleClickWindow
                        && Math.Sqrt(dx * dx + dy * dy) <= DoubleClickRadius;
        _lastLeftDownTime = isDouble ? double.NegativeInfinity : now; // 더블 후 리셋(트리플 방지)
        _lastLeftDownPos = point;
        return isDouble;
    }

    private void ToggleDrawingMode() { lock (_gate) _drawing.ToggleMode(); }

    private void OnButtonDown(MouseButton button, PointD point)
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            if (button == MouseButton.Left) _leftDown = true;

            if (_drawing.IsDrawingModeActive)
            {
                if (button == MouseButton.Left)
                    _drawing.StartShape(point, _modifiers, DrawColor);
                return; // 그리기 모드 — 클릭 효과 억제
            }

            // 비-그리기: 클릭 스포트라이트/리플 (좌/우, 더블클릭 동반)
            if (button == MouseButton.Left || button == MouseButton.Right)
            {
                bool isDouble = button == MouseButton.Left && DetectDoubleClick(point, now);
                _effects.AddClick(point, isRight: button == MouseButton.Right, isDouble, now, _animationSpeed);
            }
        }
    }

    private void OnButtonUp(MouseButton button, PointD point)
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            if (button != MouseButton.Left) return;
            _leftDown = false;
            if (_drawing.IsDrawingModeActive)
                _drawing.EndShape();
            else
                _effects.BeginDragTrailFade(now); // 비-그리기 드래그 종료 → streak fade out
        }
    }

    private void OnScrolled(ScrollDelta delta, PointD point)
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            if (_drawing.IsDrawingModeActive) return; // 그리기 모드 — 효과 억제
            bool isVertical = Math.Abs(delta.Y) >= Math.Abs(delta.X);
            double magnitude = isVertical ? Math.Abs(delta.Y) : Math.Abs(delta.X);
            bool isPositive = isVertical ? delta.Y > 0 : delta.X > 0;
            _effects.AddScroll(point, isPositive, isVertical, magnitude,
                MonitorContaining(point)?.Bounds, now, _animationSpeed);
        }
    }

    private void OnHookRemoved() => MouseHookLost?.Invoke(); // T2: 사용자에게 알림(구현이 재설치)

    private void OnKeyPressed(KeyEvent e)
    {
        lock (_gate)
        {
            _modifiers = e.Modifiers;
            _drawing.CurrentModifiers = e.Modifiers;

            if (_drawing.IsDrawingModeActive)
            {
                if (e.Special == SpecialKey.Escape) _drawing.ClearAndExit();
                else if (e.Characters == "[") DecreaseLineWidthAndPersist();
                else if (e.Characters == "]") IncreaseLineWidthAndPersist();
                else if (e.Modifiers.HasFlag(KeyModifiers.Control)
                         && string.Equals(e.Characters, "z", StringComparison.OrdinalIgnoreCase))
                    _drawing.UndoLastShape();
            }
            // TODO: 키스트로크 오버레이 — KeyFormat.Format(e.Modifiers, e.Special, e.Characters) → KeystrokeOverlayState
        }
    }

    private void DecreaseLineWidthAndPersist()
    {
        _store.Set(LineWidthKey, _drawing.DecreaseLineWidth());
        _settings.Save(_store);
    }

    private void IncreaseLineWidthAndPersist()
    {
        _store.Set(LineWidthKey, _drawing.IncreaseLineWidth());
        _settings.Save(_store);
    }

    private void OnForegroundChanged(ForegroundApp app)
    {
        // TODO: 발표/녹화 앱 감지 시 키스트로크 표시 등 켜기(발표 안전). MonitorIdentity/trust 이식 시.
    }

    private void RebuildRenderers()
    {
        lock (_gate)
        {
            var wanted = new HashSet<string>(_monitors.Monitors.Select(m => m.Id));
            // 사라진 모니터 렌더러 파기
            foreach (var id in _renderers.Keys.Where(id => !wanted.Contains(id)).ToList())
            {
                _renderers[id].Dispose();
                _renderers.Remove(id);
            }
            // 새 모니터 렌더러 생성
            foreach (var m in _monitors.Monitors)
                if (!_renderers.ContainsKey(m.Id))
                    _renderers[m.Id] = _rendererFactory.Create(m);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;

        _mouse.ButtonDown -= OnButtonDown;
        _mouse.ButtonUp -= OnButtonUp;
        _mouse.Scrolled -= OnScrolled;
        _mouse.HookRemoved -= OnHookRemoved;
        _keyboard.KeyPressed -= OnKeyPressed;
        _foreground.Changed -= OnForegroundChanged;
        _monitors.MonitorsChanged -= RebuildRenderers;

        foreach (var reg in _hotkeyRegs) reg.Dispose();
        _hotkeyRegs.Clear();

        _settings.Save(_store); // 종료 시 설정 flush

        _mouse.Stop(); _keyboard.Stop(); _foreground.Stop();
        _mouse.Dispose(); _keyboard.Dispose(); _hotkeys.Dispose(); _foreground.Dispose();

        lock (_gate)
        {
            foreach (var r in _renderers.Values) r.Dispose();
            _renderers.Clear();
        }
    }
}
