namespace Cluxo.Core.Platform;

/// <summary>모니터 한 대. Bounds는 가상 데스크톱 좌표, DpiScale은 Per-Monitor DPI v2 배율(1.0=100%).</summary>
public readonly record struct MonitorInfo(string Id, RectD Bounds, double DpiScale, bool IsPrimary);

/// <summary>
/// 모니터 구성 제공 + 변경 알림 (핫플러그·DPI 변경). Mac의 모니터별 오버레이 윈도우 모델 대응 —
/// 모니터마다 별도 레이어드 오버레이를 만든다.
/// </summary>
public interface IMonitorProvider
{
    IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>모니터 연결/해제/DPI 변경 시 — 코디네이터가 오버레이를 재구성.</summary>
    event Action? MonitorsChanged;
}

/// <summary>
/// 커서 강조 링의 한 프레임 시각 상태. Shape/BorderWidth/Dashed는 설정(RingShape·BorderWeight·
/// BorderStyle)에서 온다. 새 필드는 기본값이 있어 기존 생성 호출과 호환.
/// </summary>
public readonly record struct RingVisual(
    Rgba Color, double Radius, double Scale, double Opacity,
    RingShape Shape = RingShape.Circle, double BorderWidth = 3.0, bool Dashed = false, bool Glow = false,
    bool InnerRing = false, bool Fill = false,
    double StretchX = 1.0, double StretchY = 1.0, double StretchAngle = 0.0);

/// <summary>드래그 중 시각 힌트 — anchored line(#17)·speed glow(#14, Velocity)·드래그 각도 라벨용.
/// ShowAngleLabel은 IsDragAngleLabelEnabled 설정 반영(각도 라벨 표시 여부).</summary>
public readonly record struct DragVisual(
    PointD Origin, PointD Current, bool AnchoredLineVisible, double Velocity, double Angle,
    bool ShowAngleLabel = false);

/// <summary>
/// 라디얼 메뉴 시각 상태 — 콘텐츠는 RadialMenu 트리, 선택은 인덱스. (커서 있는 모니터)
/// CurrentValues(8 sector 현재값)·SubActive·SubSubActive는 코디네이터가 settings/runtime로 계산해
/// 렌더가 중앙 컨텍스트·현재값 강조에 쓴다(맥 RadialMenuView 대응).
/// </summary>
public readonly record struct RadialVisual(
    bool Visible, PointD Center, int? Sector, int? Sub, int? SubSub,
    IReadOnlyList<string>? CurrentValues = null,
    IReadOnlyList<bool>? SubActive = null,
    IReadOnlyList<bool>? SubSubActive = null);

/// <summary>스포트라이트 — 커서 주변만 남기고 화면을 어둡게. Radius=맑은 반경(pt), Softness=경계 부드러움(0~1).</summary>
public readonly record struct SpotlightVisual(double Radius, double Softness);

/// <summary>돋보기 — 커서 주변 화면을 확대한 원형 렌즈. Zoom=배율, Size=렌즈 지름(pt).</summary>
public readonly record struct MagnifierVisual(double Zoom, double Size);

/// <summary>그리기 툴바 한 항목 — 도구 버튼 / 두께 dot / 색 dot 공용. (맥 DrawingToolbarView 대응)</summary>
public readonly record struct ToolbarItem(RectD Rect, bool Active, bool Selected, Rgba Color, double Value, DrawingTool Tool);

/// <summary>
/// 그리기 모드 플로팅 툴바 시각 상태 — 도구 7 + 두께 5 + 색 7. 좌표는 화면(가상 데스크톱).
/// 코디네이터가 레이아웃을 계산해 DrawingState 프레임(히트테스트)과 동시에 채운다.
/// </summary>
public readonly record struct ToolbarVisual(
    RectD Bounds, Rgba Accent, string Hint,
    IReadOnlyList<ToolbarItem> Tools,
    IReadOnlyList<ToolbarItem> Thickness,
    IReadOnlyList<ToolbarItem> Colors,
    RectD Close);

/// <summary>한 모니터의 일시적 효과 스냅샷 (해당 모니터 영역 효과만 필터됨).</summary>
public readonly record struct OverlayEffects(
    IReadOnlyList<ClickEffect> Clicks,
    IReadOnlyList<DoubleClickEffect> DoubleClicks,
    IReadOnlyList<ScrollEffect> Scrolls,
    IReadOnlyList<ShakeEffect> Shakes,
    IReadOnlyList<IdlePulseEffect> IdlePulses,
    IReadOnlyList<TrailPoint> Trail,
    IReadOnlyList<TrailPoint> DragTrail)
{
    public static readonly OverlayEffects Empty = new(
        Array.Empty<ClickEffect>(), Array.Empty<DoubleClickEffect>(), Array.Empty<ScrollEffect>(),
        Array.Empty<ShakeEffect>(), Array.Empty<IdlePulseEffect>(),
        Array.Empty<TrailPoint>(), Array.Empty<TrailPoint>());
}

/// <summary>
/// 한 모니터·한 프레임의 불변 시각 스냅샷. 코디네이터가 Core 상태에서 만들어 렌더 스레드로 넘긴다.
/// 불변이라 스레드 경계를 안전하게 건넌다(공유 가변 상태 없음).
///
/// 라디얼 메뉴 시각은 Radial 뷰 이식 시 필드를 추가한다.
/// </summary>
public readonly record struct OverlayFrame(
    string MonitorId,
    PointD? CursorPosition,                  // null = 커서가 이 모니터에 없음
    RingVisual? Ring,
    IReadOnlyList<DrawingShape> Shapes,
    BrandingConfig Branding,                 // 코브랜딩(워터마크/스플래시 등 렌더에 필요)
    OverlayEffects Effects = default,        // 일시적 효과(모니터별 필터)
    string? Keystroke = null,                // 키스트로크 오버레이 텍스트(보이는 동안만, 렌더가 배치)
    DragVisual? Drag = null,                 // 드래그 시각 힌트(커서 있는 모니터만)
    RadialVisual? Radial = null,             // 라디얼 메뉴(중심 있는 모니터만)
    bool Inspector = false,                  // ⌃⌥I 좌표 표시(커서 있는 모니터가 좌표 라벨 렌더)
    ToolbarVisual? Toolbar = null,           // 그리기 모드 플로팅 툴바(툴바 있는 모니터만)
    RingShape RingShape = RingShape.Circle,  // 현재 링 모양 — 효과(클릭/흔들기 등)가 따라가도록 항상 전달
    SpotlightVisual? Spotlight = null,       // ⌃⌥S 스포트라이트(활성 시 모든 모니터에 전달, 커서 모니터만 구멍)
    MagnifierVisual? Magnifier = null        // ⌃⌥M 돋보기(커서 모니터만 — 렌즈가 커서 따라감)
);

/// <summary>
/// 한 모니터의 투명·클릭통과·항상위 오버레이 렌더러. (Vortice Direct2D + DirectComposition 레이어드 윈도우)
/// 렌더 스레드에서 불변 <see cref="OverlayFrame"/>을 받아 그린다.
/// </summary>
public interface IOverlayRenderer : IDisposable
{
    string MonitorId { get; }

    /// <summary>한 프레임 그리기. in 전달로 복사 최소화(60Hz 핫패스).</summary>
    void Render(in OverlayFrame frame);
}

/// <summary>모니터별 오버레이 렌더러 생성. 모니터 변경 시 코디네이터가 생성/파기.</summary>
public interface IOverlayRendererFactory
{
    IOverlayRenderer Create(MonitorInfo monitor);
}
