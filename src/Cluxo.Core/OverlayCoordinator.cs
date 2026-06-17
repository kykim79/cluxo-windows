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

    // 가운데 버튼 라디얼 트리거 (키 chord 앱 충돌 회피용). 가운데 버튼은 후킹에서 흡수.
    // 모델(맥 클릭): 가운데 클릭으로 열어 유지 → leaf 클릭=실행+유지(연속 토글), 중앙 ✕/바깥 클릭=닫기.
    private bool _radialByMiddle;

    // 설정 캐시 — 60Hz 핫패스에서 매 프레임 store를 읽지 않게 ApplyRuntimeSettings에서만 갱신.
    private Rgba _activeColor = RingColor.Cyan.Color();
    private double _ringRadius = RingSize.Medium.Diameter() / 2;
    private double _ringOpacity = 1.0;
    private RingShape _ringShape = RingShape.Circle;
    private double _ringBorderWidth = BorderWeight.Thin.LineWidth();
    private bool _ringDashed;
    private bool _hasInnerRing;          // 이중링 (맥 hasInnerRing)
    private bool _ringFillEnabled = true; // 도넛 채우기 (맥 isRingFillEnabled, 기본 ON)
    private double _spotlightRadius = 130.0;   // ⌃⌥S 스포트라이트 맑은 반경
    private double _spotlightSoftness = 0.4;    // 경계 부드러움
    private double _magnifierZoom = 2.0;        // ⌃⌥M 돋보기 배율
    private double _magnifierSize = 200.0;      // 렌즈 지름
    private double _animationSpeed = 1.0;
    private double _keystrokeTimeout = 3.0;

    // 효과 토글 캐시 — 설정창/라디얼에서 켜고 끔. (시각 효과가 있는 것만 게이트)
    private bool _trailEnabled;
    private bool _cometTailEnabled;
    private bool _shakeEnabled = true;
    private bool _scrollEnabled = true;
    private bool _keystrokeEnabled;
    private volatile bool _keystrokeForced; // 낯선 외장 모니터 자동표시 — 설정과 별개의 임시 강제 ON
    private bool _anchoredLineEnabled;
    private bool _glowEnabled;
    private bool _idlePulseEnabled = true;
    private bool _dragAngleLabelEnabled;
    private double _idleTimeout = 3.0;

    // 정지펄스 idle 추적 — 커서가 deadband 안에서 idleTimeout 이상 멈추면 1회 펄스.
    private PointD _idleAnchor;
    private double _idleSince;
    private bool _idlePulsed;
    private const double IdleDeadband = 4;

    // 숨김 대기 — 커서가 안 움직이면 링을 페이드 아웃. 0이면 비활성(항상 표시).
    private double _ringHideSeconds;
    private double _lastMoveTime = -1; // -1 = 첫 프레임 미초기화
    private PointD _lastMovePos;
    private double _ringFade = 1.0;
    private const double RingFadeDuration = 0.5; // 페이드 시간(초)

    // 호흡 — 링이 맥박처럼 천천히 스케일(맥 breathingScale). 드래그 중엔 미적용.
    private bool _breathingEnabled = true;
    private double _breathingScale = 1.0;
    private const double BreathPeriod = 3.8; // 한 호흡 주기(초)

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

    /// <summary>낯선 외장 모니터 자동표시 — 키스트로크를 설정과 무관하게 임시 강제 ON/OFF(연결 시 켜고, 분리 시 원복).</summary>
    public void SetKeystrokeForced(bool forced) => _keystrokeForced = forced;

    /// <summary>전체 활성 여부(트레이 토글). false면 아무 오버레이도 안 그린다(맥 비활성화 대응).</summary>
    public bool IsActive { get { lock (_gate) return _active; } }
    private bool _active = true;

    /// <summary>활성/비활성 전환 시 발생(트레이 메뉴·아이콘 체크 갱신용). 인자는 활성 여부.</summary>
    public event Action<bool>? ActiveChanged;

    /// <summary>전체 활성/비활성 토글 — 트레이 아이콘 클릭·메뉴. 비활성 시 트레일/효과를 비운다.</summary>
    public void ToggleActive()
    {
        bool active;
        lock (_gate)
        {
            _active = !_active;
            active = _active;
            if (!active) { _effects.ClearTrail(); _effects.ClearDragTrail(); }
        }
        ActiveChanged?.Invoke(active);
    }

    /// <summary>
    /// ⌃⌥M 돋보기 상태 — 활성이면 커서 물리 좌표·배율·렌즈 물리 지름, 아니면 null.
    /// 렌더 호스트가 매 프레임 폴링해 Magnification API 창을 구동한다(WPF 렌더와 별도).
    /// </summary>
    public MagnifierState? CurrentMagnifier
    {
        get
        {
            lock (_gate)
            {
                // 라디얼 중에도 렌즈를 보여 토글이 즉시 반영되게 한다(렌즈는 커서 중앙, 라디얼 링은 그 바깥).
                // 그리기 중엔 숨김(주석 전용 모드).
                if (!_active || !_runtime.IsMagnifierActive || _drawing.IsDrawingModeActive) return null;
                var pos = _runtime.CursorPosition;
                double dpi = MonitorContaining(pos)?.DpiScale ?? 1.0;
                return new MagnifierState(pos, _magnifierZoom, _magnifierSize * dpi, _activeColor);
            }
        }
    }

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
        RegisterHotkeys();      // 설정의 단축키 키로 등록

        _mouse.ButtonDown += OnButtonDown;
        _mouse.ButtonUp += OnButtonUp;
        _mouse.Scrolled += OnScrolled;
        _mouse.HookRemoved += OnHookRemoved;
        _keyboard.KeyPressed += OnKeyPressed;
        _foreground.Changed += OnForegroundChanged;
        // ⌃⌥. chord 완성(Opened 전이)마다 라디얼 토글. Closed(떼임)는 토글 모델이라 사용 안 함.
        _radialTrigger.Opened += ToggleRadial;
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

            // 숨김 대기 — 커서가 안 움직이면 링을 페이드 아웃(움직이면 즉시 복귀). 활성/비활성 무관 추적.
            if (_lastMoveTime < 0 || Math.Abs(pos.X - _lastMovePos.X) + Math.Abs(pos.Y - _lastMovePos.Y) > 2)
            { _lastMoveTime = now; _lastMovePos = pos; }
            _ringFade = _ringHideSeconds <= 0 ? 1.0
                : Math.Max(0.0, 1.0 - Math.Max(0.0, (now - _lastMoveTime) - _ringHideSeconds) / RingFadeDuration);

            // 호흡 — 0.94↔1.08 사인. 드래그 게이트는 링 생성 시 적용.
            _breathingScale = _breathingEnabled ? 1.01 + 0.07 * Math.Sin(now * (2 * Math.PI / BreathPeriod)) : 1.0;

            // 비활성화(트레이 토글) — 아무것도 그리지 않는다(맥 비활성화 대응). 효과 처리도 건너뜀.
            if (!_active)
            {
                batch = BuildEmptyFrames();
            }
            else
            {
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

            // 트레일 — 활성 + 비-그리기 + 비-라디얼일 때만 샘플. 그 외(모드 진입/비활성)엔 즉시 비운다.
            // (전엔 비-그리기 분기에서만 갱신해, 라디얼·그리기 진입 시 링버퍼가 얼어붙어 일부가 잔존했다.)
            if (_trailEnabled && !_drawing.IsDrawingModeActive && !_runtime.IsRadialMenuActive)
                _effects.UpdateTrail(pos);
            else if (_effects.Trail.Count > 0)
                _effects.ClearTrail();

              _effects.Prune(now);   // 만료 효과 + 드래그 trail fade 진행
              _keystrokes.Tick(now); // 키스트로크 오버레이 자동 숨김
              batch = BuildFrames(pos);
            }
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

        // 라디얼 중앙 컨텍스트/현재값 강조 데이터 — 활성 시 1회 계산(모든 모니터 공유). 맥 RadialMenuView 대응.
        string[]? radialValues = null;
        bool[]? radialSubActive = null, radialSubSubActive = null;
        if (_runtime.IsRadialMenuActive)
        {
            radialValues = new string[8];
            for (int i = 0; i < 8; i++)
                radialValues[i] = ((RadialMenuItem)i).CurrentValue(_settingsModel, _runtime);
            if (_runtime.RadialMenuSelectedSector is { } sec)
            {
                var item = (RadialMenuItem)sec;
                var subs = item.SubItems();
                radialSubActive = new bool[subs.Count];
                for (int s = 0; s < subs.Count; s++)
                    radialSubActive[s] = item.IsSubCurrent(s, _settingsModel, _runtime);
                if (_runtime.RadialMenuSelectedSubItem is { } subI && subI < subs.Count
                    && subs[subI].Children is { Count: > 0 } kids)
                {
                    radialSubSubActive = new bool[kids.Count];
                    for (int k = 0; k < kids.Count; k++)
                        radialSubSubActive[k] = item.IsSubSubCurrent(subI, k, _settingsModel, _runtime);
                }
            }
        }

        // 스포트라이트 — 활성 시 모든 모니터에 전달(커서 모니터만 구멍, 나머지는 전체 디밍).
        SpotlightVisual? spotlight = _runtime.IsSpotlightActive
            ? new SpotlightVisual(_spotlightRadius, _spotlightSoftness)
            : null;

        // 그리기 툴바 — 활성 시 커서 모니터(없으면 첫 모니터) 하단 중앙에 1회 레이아웃.
        // DrawingState 프레임(히트테스트)과 ToolbarVisual(렌더)을 동시에 채운다.
        ToolbarVisual? toolbar = null;
        if (_drawing.IsDrawingModeActive)
        {
            var tbMon = MonitorContaining(pos) ?? _monitors.Monitors.FirstOrDefault();
            if (tbMon.Id is not null) toolbar = BuildToolbar(tbMon);
        }

        foreach (var monitor in _monitors.Monitors)
        {
            if (!_renderers.TryGetValue(monitor.Id, out var renderer)) continue;
            var b = monitor.Bounds;
            PointD? cursorHere = b.Contains(pos) ? pos : null;
            // 툴바는 자기 경계 안에 중심이 있는 모니터만 렌더
            ToolbarVisual? toolbarHere = toolbar is { } tvb
                && b.Contains(new PointD(tvb.Bounds.X + tvb.Bounds.Width / 2, tvb.Bounds.Y + tvb.Bounds.Height / 2))
                ? toolbar : null;
            // 링 외형 — CursorSettings 캐시(색·크기·투명도) + 이중링/채우기 + 드래그 속도 stretch.
            // #16 Velocity Stretch(맥): 진행 방향으로 회전 후 x 1.05→1.5 / y 0.95→0.7 비대칭 스케일.
            double sx = 1.0, sy = 1.0, sAngle = 0.0;
            if (cursorHere is not null && _runtime.IsDragging)
            {
                double vr = Math.Min(1.0, _runtime.DragVelocity / 1000.0);
                sx = 1.05 + 0.45 * vr;
                sy = 0.95 - 0.25 * vr;
                sAngle = _runtime.DragAngle * 180.0 / Math.PI; // rad→deg (렌더는 화면 deg 회전)
            }
            RingVisual? ring = cursorHere is null
                ? null
                : new RingVisual(_activeColor, _ringRadius, Scale: _runtime.IsDragging ? 1.0 : _breathingScale, _ringOpacity * _ringFade,
                    _ringShape, _ringBorderWidth, _ringDashed, _glowEnabled,
                    _hasInnerRing, _ringFillEnabled, sx, sy, sAngle);
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
                ? new DragVisual(org, cp, _runtime.AnchoredLineVisible, _runtime.DragVelocity, _runtime.DragAngle,
                    _dragAngleLabelEnabled)
                : null;
            // 라디얼 메뉴 — 중심이 이 모니터에 있을 때만
            RadialVisual? radial = _runtime.IsRadialMenuActive && b.Contains(_runtime.RadialMenuCenter)
                ? new RadialVisual(_runtime.IsRadialMenuVisible, _runtime.RadialMenuCenter,
                    _runtime.RadialMenuSelectedSector, _runtime.RadialMenuSelectedSubItem, _runtime.RadialMenuSelectedSubSubItem,
                    radialValues, radialSubActive, radialSubSubActive)
                : null;
            result.Add((renderer, new OverlayFrame(monitor.Id, cursorHere, ring, shapes, branding, effects, keystroke, drag, radial,
                _runtime.IsInspectorActive, toolbarHere, _ringShape, spotlight)));
        }
        return result;
    }

    // 비활성 — 모든 모니터에 빈 프레임(링·효과·모든 오버레이 없음)을 보내 화면을 비운다.
    private List<(IOverlayRenderer, OverlayFrame)> BuildEmptyFrames()
    {
        var branding = _branding.Current;
        var result = new List<(IOverlayRenderer, OverlayFrame)>(_renderers.Count);
        foreach (var monitor in _monitors.Monitors)
            if (_renderers.TryGetValue(monitor.Id, out var renderer))
                result.Add((renderer, new OverlayFrame(monitor.Id, null, null,
                    Array.Empty<DrawingShape>(), branding, OverlayEffects.Empty)));
        return result;
    }

    private RectD _toolbarCloseRect; // 그리기 툴바 종료(✕) 버튼 — BuildToolbar에서 갱신, OnButtonDown에서 히트테스트.

    // 도구 표시 순서 (맥 DrawingToolbarView와 동일): 펜·직선·화살표·사각형·타원·형광펜·뱃지.
    private static readonly DrawingTool[] ToolbarOrder =
    {
        DrawingTool.Pen, DrawingTool.Line, DrawingTool.Arrow, DrawingTool.Rectangle,
        DrawingTool.Ellipse, DrawingTool.Highlighter, DrawingTool.Badge,
    };

    // 그리기 툴바 레이아웃 — 화면(가상 데스크톱) 좌표로 계산. DrawingState 프레임(히트테스트)과
    // ToolbarVisual(렌더)을 동시에 채운다. mon 하단 중앙 배치. (맥 DrawingToolbarView 대응)
    private ToolbarVisual BuildToolbar(MonitorInfo mon)
    {
        // 치수는 논리(DIP) 기준 → 물리 픽셀로 환산(× DpiScale)해 화면 좌표로 배치한다. 렌더(ToLocalRect)가
        // 다시 ÷DpiScale 하므로, 이렇게 해야 고배율에서도 의도한 크기로 보인다. (전엔 물리=논리로 둬 절반 크기였음)
        double dpi = mon.DpiScale <= 0 ? 1.0 : mon.DpiScale;
        double pad = 14 * dpi, toolD = 38 * dpi, toolGap = 7 * dpi, thickHit = 24 * dpi, thickGap = 4 * dpi,
               colorHit = 24 * dpi, colorGap = 4 * dpi, groupGap = 18 * dpi, closeD = 34 * dpi, bottomMargin = 150 * dpi;
        var steps = Tokens.Drawing.LineWidthSteps;
        var colors = ColorPalette;

        double toolsW = ToolbarOrder.Length * toolD + (ToolbarOrder.Length - 1) * toolGap;
        double thickW = steps.Length * thickHit + (steps.Length - 1) * thickGap;
        double colorW = colors.Length * colorHit + (colors.Length - 1) * colorGap;
        double panelW = toolsW + groupGap + thickW + groupGap + colorW + groupGap + closeD + pad * 2;
        double panelH = toolD + pad * 2;

        double left = mon.Bounds.X + (mon.Bounds.Width - panelW) / 2;
        double top = mon.Bounds.Y + mon.Bounds.Height - bottomMargin - panelH;
        var bounds = new RectD(left, top, panelW, panelH);
        double cy = top + panelH / 2;
        double x = left + pad;

        var preview = _drawing.PreviewTool;
        _drawing.ToolbarFrames.Clear();
        var toolItems = new List<ToolbarItem>(ToolbarOrder.Length);
        foreach (var t in ToolbarOrder)
        {
            var rect = new RectD(x, cy - toolD / 2, toolD, toolD);
            _drawing.ToolbarFrames[t] = rect;
            toolItems.Add(new ToolbarItem(rect, t == preview, t == _drawing.SelectedTool, default, 0, t));
            x += toolD + toolGap;
        }
        x += groupGap - toolGap;

        _drawing.ThicknessFrames.Clear();
        var thickItems = new List<ToolbarItem>(steps.Length);
        foreach (var w in steps)
        {
            var rect = new RectD(x, cy - thickHit / 2, thickHit, thickHit);
            _drawing.ThicknessFrames[w] = rect;
            bool sel = Math.Abs(w - _drawing.LineWidth) < 0.01;
            thickItems.Add(new ToolbarItem(rect, sel, sel, default, w, default));
            x += thickHit + thickGap;
        }
        x += groupGap - thickGap;

        _drawing.ColorFrames.Clear();
        var colorItems = new List<ToolbarItem>(colors.Length);
        var curColor = _settingsModel.RingColor;
        foreach (var c in colors)
        {
            var rect = new RectD(x, cy - colorHit / 2, colorHit, colorHit);
            _drawing.ColorFrames[c.ToString()] = rect;
            bool sel = c == curColor;
            colorItems.Add(new ToolbarItem(rect, sel, sel, c.Color(), 0, default));
            x += colorHit + colorGap;
        }
        x += groupGap - colorGap;

        // 종료 버튼 — 마우스로 그리기 모드를 끌 수 있게(클릭 흡수 중에도 툴바는 처리됨).
        _toolbarCloseRect = new RectD(x, cy - closeD / 2, closeD, closeD);

        string hint = $"{preview.DisplayName()} · 드래그하여 그리기 · ESC 종료";
        return new ToolbarVisual(bounds, _activeColor, hint, toolItems, thickItems, colorItems, _toolbarCloseRect);
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
            _hasInnerRing = _settingsModel.HasInnerRing;
            _ringFillEnabled = _settingsModel.IsRingFillEnabled;
            _spotlightRadius = _settingsModel.SpotlightRadius;
            _spotlightSoftness = _settingsModel.SpotlightEdgeSoftness;
            _magnifierZoom = _settingsModel.MagnifierZoom;
            _magnifierSize = _settingsModel.MagnifierSize;
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
            _breathingEnabled = _settingsModel.IsBreathingEnabled;
            _dragAngleLabelEnabled = _settingsModel.IsDragAngleLabelEnabled;
            _idleTimeout = _settingsModel.IdleTimeout;
            _ringHideSeconds = _settingsModel.RingHideSeconds;
            _autoActivateEnabled = _settingsModel.IsAutoActivateEnabled;
        }
    }

    private void OnSettingsChanged()
    {
        ApplyRuntimeSettings();
        if (HotkeySignature() != _hkSig) RegisterHotkeys(); // 단축키 키 변경 시 재등록
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

    /// <summary>그리기 모드가 토글될 때마다 발생(진단/트레이 체크 갱신용). 인자는 활성 여부.</summary>
    public event Action<bool>? DrawingModeChanged;

    /// <summary>그리기 모드 토글 (⌃⌥D 핫키 + 트레이 메뉴 공용).</summary>
    public void ToggleDrawingMode()
    {
        double now = _clock.NowSeconds;
        bool active;
        lock (_gate)
        {
            if (!_active) return; // 비활성 — 그리기 모드 진입 무시
            _drawing.ToggleMode();
            active = _drawing.IsDrawingModeActive;
            _keystrokes.ShowStatusNotification(active ? "그리기 모드 ON" : "그리기 모드 OFF", now);
        }
        DrawingModeChanged?.Invoke(active);
    }

    private void OnButtonDown(MouseButton button, PointD point)
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            if (!_active) return; // 비활성 — 입력 무시
            // 가운데 버튼 — 라디얼 토글(맥 클릭 모델). 닫혀 있으면 열고 유지, 열려 있으면 클릭 지점을
            // 커밋: leaf는 실행 후 메뉴 유지(연속 토글), 중앙 dead zone(✕)/바깥 클릭만 닫는다. 앱으론 후킹이 흡수.
            if (button == MouseButton.Middle)
            {
                if (_drawing.IsDrawingModeActive) return;
                if (_runtime.IsRadialMenuActive && _radialByMiddle)
                {
                    _radial.Update(point, now); // 클릭 지점으로 선택 확정
                    if (!_radial.Commit())      // leaf 실행+유지 / 중앙·바깥이면 false → 닫기
                    {
                        _radial.Close();
                        _radialByMiddle = false;
                    }
                }
                else if (!_runtime.IsRadialMenuActive)
                {
                    _radial.Open(point, now);   // 열고 유지
                    _runtime.IsRadialMenuVisible = true;
                    _radialByMiddle = true;
                }
                return;
            }

            // 라디얼 열림 중 — 좌클릭으로도 탐색/커밋(가운데와 동일). 가운데로 열고 좌클릭으로 선택·닫기가
            // 자연스럽다. leaf=실행+유지, 중앙 ✕/바깥=닫기. 좌·우 클릭은 후킹이 흡수해 아래 창에 안 샌다.
            if (_runtime.IsRadialMenuActive)
            {
                if (button == MouseButton.Left)
                {
                    _radial.Update(point, now);
                    if (!_radial.Commit())
                    {
                        _radial.Close();
                        _radialByMiddle = false;
                    }
                }
                return; // 라디얼 중 클릭은 드래그/효과 억제
            }

            if (button == MouseButton.Left) _leftDown = true;

            if (_drawing.IsDrawingModeActive)
            {
                if (button == MouseButton.Left)
                {
                    // 종료(✕) 버튼이면 그리기 모드 끄기(도형 유지). 마우스만으로도 빠져나갈 수 있게.
                    if (_toolbarCloseRect.Contains(point))
                    {
                        _drawing.ToggleMode(); // 활성 → OFF, 진행 중 stroke 정리
                        _keystrokes.ShowStatusNotification("그리기 모드 OFF", now);
                        return;
                    }
                    // 툴바 클릭이면 도구/두께/색 선택만(도형 시작 안 함). 그 외 영역은 그리기 시작.
                    if (_drawing.HitToolbarAndSelect(point))
                        _keystrokes.ShowStatusNotification($"도구 · {_drawing.SelectedTool.DisplayName()}", now);
                    else if (_drawing.HitThicknessAndSelect(point))
                        _keystrokes.ShowStatusNotification($"두께 · {(int)_drawing.LineWidth}pt", now);
                    else if (_drawing.ColorAt(point) is { } cn && Enum.TryParse<RingColor>(cn, out var rc))
                    {
                        _settingsModel.RingColor = rc;
                        _keystrokes.ShowStatusNotification($"색 · {rc.Label()}", now);
                    }
                    else
                        _drawing.StartShape(point, _modifiers, _activeColor); // stroke도 accent 따름(DESIGN.md)
                }
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
            // 가운데 버튼 뗌 — 클릭 토글 모델이라 down에서 모두 처리, up은 무시(후킹만 흡수).
            if (button == MouseButton.Middle) return;

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

    /// <summary>스포트라이트 토글 (⌃⌥S). 라디얼 sector 0과 동일 상태 — 렌더는 D2D 복구 후.</summary>
    public void ToggleSpotlight()
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            _runtime.IsSpotlightActive = !_runtime.IsSpotlightActive;
            _keystrokes.ShowStatusNotification(
                _runtime.IsSpotlightActive ? "스포트라이트 ON" : "스포트라이트 OFF", now);
        }
    }

    /// <summary>돋보기 토글 (⌃⌥M). 라디얼 sector 1과 동일 상태 — 확대 렌더는 D2D 복구 후.</summary>
    public void ToggleMagnifier()
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            _runtime.IsMagnifierActive = !_runtime.IsMagnifierActive;
            _keystrokes.ShowStatusNotification(
                _runtime.IsMagnifierActive ? "돋보기 ON" : "돋보기 OFF", now);
        }
    }

    /// <summary>키 입력 표시 토글 (⌃⌥K).</summary>
    public void ToggleKeystroke()
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            _settingsModel.IsKeystrokeEnabled = !_settingsModel.IsKeystrokeEnabled;
            _keystrokes.ShowStatusNotification(
                _settingsModel.IsKeystrokeEnabled ? "키 입력 표시 ON" : "키 입력 표시 OFF", now);
        }
    }

    /// <summary>다음 링 색으로 순환 (⌃⌥C). 발표 중 빠른 색 변경.</summary>
    public void CycleColor()
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            int i = Array.IndexOf(ColorPalette, _settingsModel.RingColor);
            var next = ColorPalette[((i < 0 ? 0 : i) + 1) % ColorPalette.Length];
            _settingsModel.RingColor = next;
            _keystrokes.ShowStatusNotification($"링 색 · {next.Label()}", now);
        }
    }

    /// <summary>링 색 직접 지정 (⌃⌥1~7).</summary>
    public void SetRingColor(RingColor color)
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            _settingsModel.RingColor = color;
            _keystrokes.ShowStatusNotification($"링 색 · {color.Label()}", now);
        }
    }

    /// <summary>라디얼 메뉴 토글 (⌃⌥.) — 닫혀 있으면 커서 위치에 열고 유지, 열려 있으면 실행 없이 닫는다.
    /// 가운데 버튼과 동일한 유지 모델: 좌클릭으로 탐색·선택, 중앙 ✕/다시 ⌃⌥.로 닫기.</summary>
    public void ToggleRadial()
    {
        lock (_gate)
        {
            if (!_active || _drawing.IsDrawingModeActive) return; // 비활성·그리기 중엔 무시
            if (_runtime.IsRadialMenuActive)
            {
                _radial.Cancel(); // 키 토글 닫기 = 실행 없이 취소
                _radialByMiddle = false;
            }
            else
            {
                var pos = _cursor.GetCursorPosition();
                _radial.Open(pos, _clock.NowSeconds);
                _runtime.IsRadialMenuVisible = true;
                _radialByMiddle = true; // 가운데/좌클릭 커밋 경로 공유
            }
        }
    }

    /// <summary>다음 링 모양으로 순환 (⌃⌥H).</summary>
    public void CycleRingShape()
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            var cases = Enum.GetValues<RingShape>();
            int i = Array.IndexOf(cases, _settingsModel.RingShape);
            var next = cases[((i < 0 ? 0 : i) + 1) % cases.Length];
            _settingsModel.RingShape = next;
            _keystrokes.ShowStatusNotification($"링 모양 · {next.Label()}", now);
        }
    }

    /// <summary>커스텀 제외 링 색 팔레트 — 맥 ⌃⌥1~7 순서와 동일(enum 정의 순서).</summary>
    private static readonly RingColor[] ColorPalette =
        Enum.GetValues<RingColor>().Where(c => c != RingColor.Custom).ToArray();

    /// <summary>등록에 실패한(충돌난) ⌃⌥ 단축키 키 목록 — 진단/안내용.</summary>
    public IReadOnlyList<string> FailedHotkeys => _failedHotkeys;
    private readonly List<string> _failedHotkeys = new();
    private string _hkSig = ""; // 현재 등록된 사용자 지정 키 시그니처(변경 감지용)

    // 사용자 지정 키(그리기·좌표·스포트라이트·돋보기·키입력) 시그니처. 바뀌면 RegisterHotkeys 재실행.
    private string HotkeySignature() => string.Join("|",
        _settingsModel.HotkeyDrawing, _settingsModel.HotkeyInspector, _settingsModel.HotkeySpotlight,
        _settingsModel.HotkeyMagnifier, _settingsModel.HotkeyKeystroke);

    /// <summary>
    /// 글로벌 단축키 등록(전부 ⌃⌥). 사용자 지정 5종은 설정 키로, 색 순환·번호는 고정 키로.
    /// 라디얼(⌃⌥.)은 RadialChordDetector(LL 후킹)가 처리 — 여기서 등록하지 않는다.
    /// 재호출 시 기존 등록을 모두 해제하고 다시 등록(키 변경 반영).
    /// </summary>
    private void RegisterHotkeys()
    {
        foreach (var r in _hotkeyRegs) r.Dispose();
        _hotkeyRegs.Clear();
        _failedHotkeys.Clear();

        TryRegisterHotkey(_settingsModel.HotkeyDrawing, ToggleDrawingMode);   // 그리기 모드
        TryRegisterHotkey(_settingsModel.HotkeyInspector, ToggleInspector);   // 좌표 표시
        TryRegisterHotkey(_settingsModel.HotkeySpotlight, ToggleSpotlight);   // 스포트라이트
        TryRegisterHotkey(_settingsModel.HotkeyMagnifier, ToggleMagnifier);   // 돋보기
        TryRegisterHotkey(_settingsModel.HotkeyKeystroke, ToggleKeystroke);   // 키 입력 표시
        TryRegisterHotkey("C", CycleColor);                                   // 링 색 순환(고정)
        TryRegisterHotkey("H", CycleRingShape);                               // 링 모양 순환(고정)
        // ⌃⌥1~7 → 색 직접 지정 (맥과 동일 순서: 노랑/빨강/파랑/초록/하늘/보라/흰).
        for (int n = 0; n < ColorPalette.Length && n < 7; n++)
        {
            var color = ColorPalette[n];
            TryRegisterHotkey(((char)('1' + n)).ToString(), () => SetRingColor(color));
        }
        _hkSig = HotkeySignature();
    }

    /// <summary>⌃⌥{key} 핫키 등록 — 다른 앱과 충돌하면 그 키만 건너뛴다(예외 삼킴).</summary>
    private void TryRegisterHotkey(string key, Action onPressed)
    {
        try
        {
            _hotkeyRegs.Add(_hotkeys.Register(
                new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, key), onPressed));
        }
        catch (Exception)
        {
            _failedHotkeys.Add(key); // 충돌(InvalidOperationException) 등 — 그 키만 비활성, 나머지는 유지.
        }
    }

    private void OnScrolled(ScrollDelta delta, PointD point)
    {
        double now = _clock.NowSeconds;
        lock (_gate)
        {
            if (!_active) return; // 비활성 — 입력 무시
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
            if (!_active) { _modifiers = e.Modifiers; return; } // 비활성 — 효과/그리기 키 무시(모디파이어만 추적)
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

            // 키스트로크 오버레이 — 설정 ON 또는 낯선 모니터 자동표시 강제 시. Format이 게이트(Ctrl/Alt/Win 필수).
            if (_keystrokeEnabled || _keystrokeForced)
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

    // 발표·녹화·회의 앱 프로세스명(소문자 부분일치). Zoom·OBS·Teams·PowerPoint·Meet·Webex·Slack 등.
    private static readonly string[] PresentationApps =
    {
        "zoom", "obs", "obs64", "teams", "ms-teams", "powerpnt", "wpp", "webex", "webexmta",
        "gotomeeting", "slack", "discord", "skype", "anydesk", "camtasia", "screenrec",
    };

    private bool _autoActivateEnabled;

    /// <summary>발표/녹화 앱이 포그라운드로 오면(설정 ON) Cluxo를 자동 활성화. (맥 자동 활성화 대응, 비활성화는 안 함)</summary>
    private void OnForegroundChanged(ForegroundApp app)
    {
        bool fire;
        lock (_gate)
        {
            if (!_autoActivateEnabled || _active) return;
            string name = (app.ProcessName ?? "").ToLowerInvariant();
            bool isPresentation = false;
            foreach (var p in PresentationApps) if (name.Contains(p)) { isPresentation = true; break; }
            if (!isPresentation) return;
            _active = true; fire = true;
        }
        if (fire) ActiveChanged?.Invoke(true);
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
        _radialTrigger.Opened -= ToggleRadial;
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
