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
    private readonly IRadialTrigger _radialTrigger;
    private readonly IClock _clock;

    private readonly DrawingState _drawing = new();
    private readonly ShakeState _shake = new();
    private readonly EffectsState _effects = new();
    private readonly KeystrokeOverlayState _keystrokes = new();
    private readonly CursorRuntimeState _runtime = new();
    private RadialMenuController _radial;
    private readonly object _gate = new();
    private readonly Dictionary<string, IOverlayRenderer> _renderers = new();
    private readonly List<IDisposable> _hotkeyRegs = new();

    private JsonSettingsStore _store = new();
    private CursorSettings _settingsModel = new(new JsonSettingsStore());
    private KeyModifiers _modifiers;
    private bool _leftDown;
    private bool _running;
    private bool _disposed;

    // 더블클릭 감지 — 같은 위치 근처 0.4초 내 두 번째 좌클릭.
    private double _lastLeftDownTime = double.NegativeInfinity;
    private PointD _lastLeftDownPos;

    // 비-그리기 드래그 모션 — 속도/각도 계산용 직전 샘플.
    private PointD _lastDragPos;
    private double _lastDragTime;

    // 설정 캐시 — 60Hz 핫패스에서 매 프레임 store를 읽지 않게 ApplyRuntimeSettings에서만 갱신.
    private Rgba _activeColor = RingColor.Cyan.Color();
    private double _ringRadius = RingSize.Medium.Diameter() / 2;
    private double _ringOpacity = 1.0;
    private RingShape _ringShape = RingShape.Circle;
    private double _ringBorderWidth = BorderWeight.Thin.LineWidth();
    private bool _ringDashed;
    private double _animationSpeed = 1.0;
    private double _keystrokeTimeout = 3.0;

    // 효과 토글 캐시 — 설정창/라디얼에서 켜고 끔. (시각 효과가 있는 것만 게이트)
    private bool _trailEnabled;
    private bool _cometTailEnabled;
    private bool _shakeEnabled = true;
    private bool _scrollEnabled = true;
    private bool _keystrokeEnabled;
    private bool _anchoredLineEnabled;
    private bool _glowEnabled;
    private bool _idlePulseEnabled = true;
    private double _idleTimeout = 3.0;

    // 정지펄스 idle 추적 — 커서가 deadband 안에서 idleTimeout 이상 멈추면 1회 펄스.
    private PointD _idleAnchor;
    private double _idleSince;
    private bool _idlePulsed;
    private const double IdleDeadband = 4;

    private const string LineWidthKey = "drawing.lineWidth";
    private const double DoubleClickWindow = 0.4;
    private const double DoubleClickRadius = 6;

    /// <summary>마우스 후킹 분실(T2) — 구현이 재설치하고, 코디네이터가 트레이/알림에 surface.</summary>
    public event Action? MouseHookLost;

    public bool IsDrawingModeActive { get { lock (_gate) return _drawing.IsDrawingModeActive; } }
    public double LineWidth { get { lock (_gate) return _drawing.LineWidth; } }
    public IReadOnlyList<DrawingShape> DrawingShapes { get { lock (_gate) return _drawing.Shapes.ToArray(); } }
    public string? Keystroke { get { lock (_gate) return _keystrokes.IsVisible ? _keystrokes.KeystrokeText : null; } }
    public bool IsDragging { get { lock (_gate) return _runtime.IsDragging; } }
    public double DragVelocity { get { lock (_gate) return _runtime.DragVelocity; } }
    public bool AnchoredLineVisible { get { lock (_gate) return _runtime.AnchoredLineVisible; } }
    public bool IsInspectorActive { get { lock (_gate) return _runtime.IsInspectorActive; } }
    public bool IsRadialMenuActive { get { lock (_gate) return _runtime.IsRadialMenuActive; } }
    public bool IsSpotlightActive { get { lock (_gate) return _runtime.IsSpotlightActive; } }

    /// <summary>라이브 설정 모델(설정창이 편집 → Changed로 즉시 적용·영구화). Start 후 유효.</summary>
    public CursorSettings Settings => _settingsModel;

    public OverlayCoordinator(
        IMouseHook mouse, IKeyboardHook keyboard, IHotkeyRegistrar hotkeys,
        ICursorPositionSource cursor, IMonitorProvider monitors,
        IOverlayRendererFactory rendererFactory, ISettingsStore settings,
        IBrandingProvider branding, IForegroundAppMonitor foreground,
        IRadialTrigger radialTrigger, IClock clock)
    {
        _mouse = mouse; _keyboard = keyboard; _hotkeys = hotkeys; _cursor = cursor;
        _monitors = monitors; _rendererFactory = rendererFactory; _settings = settings;
        _branding = branding; _foreground = foreground; _radialTrigger = radialTrigger; _clock = clock;
        _radial = new RadialMenuController(_settingsModel, _runtime);
    }

    /// <summary>설정 로드, 단축키 등록, 입력/모니터 구독, 렌더러 생성, 후킹 시작.</summary>
    public void Start()
    {
        if (_running) return;

        _store = _settings.Load();
        _settingsModel = new CursorSettings(_store);
        _settingsModel.Changed += OnSettingsChanged;
        _radial = new RadialMenuController(_settingsModel, _runtime); // 로드된 설정으로 재생성
        lock (_gate) _drawing.LineWidth = _store.Get(LineWidthKey, Tokens.Drawing.LineWidth);
        ApplyRuntimeSettings(); // 캐시 채우기 + shake 민감도 적용

        // ⌃⌥D → 그리기 모드 토글, ⌃⌥I → 좌표(inspector) 토글 (Mac과 동일 단축키)
        _hotkeyRegs.Add(_hotkeys.Register(
            new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, "D"), ToggleDrawingMode));
        _hotkeyRegs.Add(_hotkeys.Register(
            new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, "I"), ToggleInspector));

        _mouse.ButtonDown += OnButtonDown;
        _mouse.ButtonUp += OnButtonUp;
        _mouse.Scrolled += OnScrolled;
        _mouse.HookRemoved += OnHookRemoved;
        _keyboard.KeyPressed += OnKeyPressed;
        _foreground.Changed += OnForegroundChanged;
        _radialTrigger.Opened += OnRadialOpened;
        _radialTrigger.Closed += OnRadialClosed;
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
            _runtime.CursorPosition = pos;

            if (_runtime.IsRadialMenuActive)
            {
                // 라디얼 메뉴 모드 — 일반 인터랙션 억제, 커서로 선택만 추적
                _radial.Update(pos, now);
            }
            else
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
                    // 일시적 효과는 그리기 모드에선 억제(오버레이가 annotation 전용). 각 효과는 설정 토글로 게이트.
                    if (shook && _shakeEnabled) _effects.AddShake(pos, now, _animationSpeed);
                    if (_trailEnabled) _effects.UpdateTrail(pos);
                    if (_leftDown)
                    {
                        if (_cometTailEnabled) _effects.UpdateDragTrail(pos); // 비-그리기 드래그 streak(코멧)
                        // 드래그 모션 — 프레임 샘플 위치 델타로 속도/각도(하이브리드 입력)
                        double dt = now - _lastDragTime;
                        if (dt > 0.0001)
                        {
                            double dx = pos.X - _lastDragPos.X, dy = pos.Y - _lastDragPos.Y;
                            double dist = Math.Sqrt(dx * dx + dy * dy);
                            _runtime.UpdateDragVelocity(dist / dt);
                            if (dist > 0.5) _runtime.UpdateDragAngle(Math.Atan2(dy, dx));
                            _lastDragPos = pos;
                            _lastDragTime = now;
                        }
                        if (_anchoredLineEnabled) _runtime.CheckAnchoredLine(pos, now); // #17 거리/시간 임계
                    }
                    else
                    {
                        // 정지펄스 — 커서가 멈춰 있고(버튼 안 누름) idleTimeout 경과 시 1회 펄스
                        double idx = pos.X - _idleAnchor.X, idy = pos.Y - _idleAnchor.Y;
                        if (idx * idx + idy * idy > IdleDeadband * IdleDeadband)
                        {
                            _idleAnchor = pos; _idleSince = now; _idlePulsed = false; // 움직임 → 재무장
                        }
                        else if (_idlePulseEnabled && !_idlePulsed && now - _idleSince >= _idleTimeout)
                        {
                            _effects.AddIdlePulse(pos, now);
                            _idlePulsed = true;
                        }
                    }
                }
            }

            _effects.Prune(now);   // 만료 효과 + 드래그 trail fade 진행
            _keystrokes.Tick(now); // 키스트로크 오버레이 자동 숨김
            batch = BuildFrames(pos);
        }

        foreach (var (renderer, frame) in batch)
            renderer.Render(in frame);
    }

    // 락 안에서 호출 — 모니터별 불변 프레임 스냅샷 + 렌더러 참조를 모은다.
    private List<(IOverlayRenderer, OverlayFrame)> BuildFrames(PointD pos)
    {
        var branding = _branding.Current;
        var shapes = SnapshotShapes(); // 커밋된 도형 + 진행 중 stroke 라이브 프리뷰(불변 스냅샷)
        string? keystroke = _keystrokes.IsVisible ? _keystrokes.KeystrokeText : null;
        var result = new List<(IOverlayRenderer, OverlayFrame)>(_renderers.Count);

        foreach (var monitor in _monitors.Monitors)
        {
            if (!_renderers.TryGetValue(monitor.Id, out var renderer)) continue;
            var b = monitor.Bounds;
            PointD? cursorHere = b.Contains(pos) ? pos : null;
            // 링 외형 — CursorSettings 캐시(색·크기·투명도). Scale=1(런타임 모션은 이후 RingMotion 이식 시)
            RingVisual? ring = cursorHere is null
                ? null
                : new RingVisual(_activeColor, _ringRadius, Scale: 1.0, _ringOpacity,
                    _ringShape, _ringBorderWidth, _ringDashed, _glowEnabled);
            // 효과는 이 모니터 영역 것만 (Mac의 per-screen 필터). TODO: 프레임당 Where/ToArray 최적화
            var effects = new OverlayEffects(
                _effects.Clicks.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.DoubleClicks.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.Scrolls.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.Shakes.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.IdlePulses.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.Trail.Where(e => b.Contains(e.Position)).ToArray(),
                _effects.DragTrail.Where(e => b.Contains(e.Position)).ToArray());
            // 드래그 시각 힌트 — 커서 있는 모니터에만 (anchored line·speed glow·각도)
            DragVisual? drag = cursorHere is { } cp && _runtime.IsDragging && _runtime.DragOrigin is { } org
                ? new DragVisual(org, cp, _runtime.AnchoredLineVisible, _runtime.DragVelocity, _runtime.DragAngle)
                : null;
            // 라디얼 메뉴 — 중심이 이 모니터에 있을 때만
            RadialVisual? radial = _runtime.IsRadialMenuActive && b.Contains(_runtime.RadialMenuCenter)
                ? new RadialVisual(_runtime.IsRadialMenuVisible, _runtime.RadialMenuCenter,
                    _runtime.RadialMenuSelectedSector, _runtime.RadialMenuSelectedSubItem, _runtime.RadialMenuSelectedSubSubItem)
                : null;
            result.Add((renderer, new OverlayFrame(monitor.Id, cursorHere, ring, shapes, branding, effects, keystroke, drag, radial)));
        }
        return result;
    }

    // 커밋된 도형 + (그리기 중이면) 진행 중 stroke를 불변 스냅샷으로. 진행 중 stroke를 포함해
    // 드래그 도중에도 선이 보인다(라이브 프리뷰). EndShape 후엔 CurrentShape=null이라 중복 없음.
    // CurrentShape는 매 프레임 변하므로 points를 복사해 렌더 스레드가 가변 리스트를 보지 않게 한다.
    private IReadOnlyList<DrawingShape> SnapshotShapes()
    {
        var committed = _drawing.Shapes;
        var current = _drawing.CurrentShape;
        if (current is null) return committed.ToArray();

        var result = new DrawingShape[committed.Count + 1];
        for (int i = 0; i < committed.Count; i++) result[i] = committed[i];
        result[committed.Count] = CopyShape(current);
        return result;
    }

    private static DrawingShape CopyShape(DrawingShape s)
    {
        var copy = new DrawingShape(s.Tool, s.Color, s.LineWidth, s.Points[0], s.BadgeNumber);
        for (int i = 1; i < s.Points.Count; i++) copy.Points.Add(s.Points[i]);
        return copy;
    }

    // 설정값을 캐시로 반영 + shake 민감도 적용. Start와 설정 변경 시에만 호출(핫패스 아님).
    private void ApplyRuntimeSettings()
    {
        lock (_gate)
        {
            _activeColor = _settingsModel.EffectiveRingColor;        // 모든 Active 효과 accent
            _ringRadius = _settingsModel.RingSize.Diameter() / 2;
            _ringOpacity = _settingsModel.RingOpacity;
            _ringShape = _settingsModel.RingShape;
            _ringBorderWidth = _settingsModel.BorderWeight.LineWidth();
            _ringDashed = _settingsModel.BorderStyle == BorderStyle.Dashed;
            _animationSpeed = _settingsModel.AnimationSpeed.Multiplier();
            _keystrokeTimeout = _settingsModel.KeystrokeTimeout;
            _shake.RequiredDirChanges = _settingsModel.ShakeSensitivity.RequiredDirChanges();

            _trailEnabled = _settingsModel.IsTrailEnabled;
            _cometTailEnabled = _settingsModel.IsCometTailEnabled;
            _shakeEnabled = _settingsModel.IsShakeEnabled;
            _scrollEnabled = _settingsModel.IsScrollIndicatorEnabled;
            _keystrokeEnabled = _settingsModel.IsKeystrokeEnabled;
            _anchoredLineEnabled = _settingsModel.IsAnchoredLineEnabled;
            _glowEnabled = _settingsModel.IsGlowEnabled;
            _idlePulseEnabled = _settingsModel.IsIdlePulseEnabled;
            _idleTimeout = _settingsModel.IdleTimeout;
        }
    }

    private void OnSettingsChanged()
    {
        ApplyRuntimeSettings();
        _settings.Save(_store); // 플랫폼 구현이 디바운스
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

    /// <summary>그리기 모드 토글 (⌃⌥D 핫키 + 트레이 메뉴 공용).</summary>
    public void ToggleDrawingMode()
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            _drawing.ToggleMode();
            _keystrokes.ShowStatusNotification(
                _drawing.IsDrawingModeActive ? "그리기 모드 ON" : "그리기 모드 OFF", now);
        }
    }

    private void OnButtonDown(MouseButton button, PointD point)
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            if (_runtime.IsRadialMenuActive) return; // 라디얼 모드 — 마우스 클릭 무시(chord hold로 선택)
            if (button == MouseButton.Left) _leftDown = true;

            if (_drawing.IsDrawingModeActive)
            {
                if (button == MouseButton.Left)
                    _drawing.StartShape(point, _modifiers, _activeColor); // stroke도 accent 따름(DESIGN.md)
                return; // 그리기 모드 — 클릭 효과 억제
            }

            // 비-그리기: 좌클릭이면 드래그 모션 시작
            if (button == MouseButton.Left)
            {
                _runtime.StartDrag(point, now);
                _lastDragPos = point;
                _lastDragTime = now;
            }
            // 클릭 스포트라이트/리플 (좌/우, 더블클릭 동반)
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
            if (_runtime.IsRadialMenuActive) return;
            if (button != MouseButton.Left) return;
            _leftDown = false;
            if (_drawing.IsDrawingModeActive)
            {
                _drawing.EndShape();
            }
            else
            {
                _effects.BeginDragTrailFade(now); // 비-그리기 드래그 종료 → streak fade out
                _runtime.EndDrag();
            }
        }
    }

    private void OnRadialOpened()
    {
        PointD pos = _cursor.GetCursorPosition();
        double now = _clock.NowSeconds;
        lock (_gate) _radial.Open(pos, now);
    }

    private void OnRadialClosed()
    {
        lock (_gate) _radial.Close(); // 선택 액션 실행(설정/런타임 변경)
    }

    /// <summary>좌표(inspector) 토글 (⌃⌥I 핫키 + 트레이 메뉴 공용).</summary>
    public void ToggleInspector()
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            _runtime.IsInspectorActive = !_runtime.IsInspectorActive;
            _keystrokes.ShowStatusNotification(
                _runtime.IsInspectorActive ? "좌표 표시 ON" : "좌표 표시 OFF", now);
        }
    }

    private void OnScrolled(ScrollDelta delta, PointD point)
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            if (_runtime.IsRadialMenuActive || _drawing.IsDrawingModeActive) return; // 효과 억제
            if (!_scrollEnabled) return; // 스크롤 표시 설정 OFF
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
        double now = _clock.NowSeconds;
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

            // 키스트로크 오버레이 — 설정 ON일 때만. Format이 게이트(Ctrl/Alt/Win 필수)라 단순 타이핑은 "" 반환.
            if (_keystrokeEnabled)
            {
                string display = KeyFormat.Format(e.Modifiers, e.Special, e.Characters);
                if (!string.IsNullOrEmpty(display))
                    _keystrokes.ShowKeystroke(display, _keystrokeTimeout, now);
            }
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

        _settingsModel.Changed -= OnSettingsChanged;
        _mouse.ButtonDown -= OnButtonDown;
        _mouse.ButtonUp -= OnButtonUp;
        _mouse.Scrolled -= OnScrolled;
        _mouse.HookRemoved -= OnHookRemoved;
        _keyboard.KeyPressed -= OnKeyPressed;
        _foreground.Changed -= OnForegroundChanged;
        _radialTrigger.Opened -= OnRadialOpened;
        _radialTrigger.Closed -= OnRadialClosed;
        _monitors.MonitorsChanged -= RebuildRenderers;

        foreach (var reg in _hotkeyRegs) reg.Dispose();
        _hotkeyRegs.Clear();

        _settings.Save(_store); // 종료 시 설정 flush

        _mouse.Stop(); _keyboard.Stop(); _foreground.Stop();
        _mouse.Dispose(); _keyboard.Dispose(); _hotkeys.Dispose(); _foreground.Dispose(); _radialTrigger.Dispose();

        lock (_gate)
        {
            foreach (var r in _renderers.Values) r.Dispose();
            _renderers.Clear();
        }
    }
}
