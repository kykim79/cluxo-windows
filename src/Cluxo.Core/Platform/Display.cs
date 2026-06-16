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

/// <summary>커서 강조 링의 한 프레임 시각 상태. (EffectsState/Ring 뷰 이식 시 필드 확장)</summary>
public readonly record struct RingVisual(Rgba Color, double Radius, double Scale, double Opacity);

/// <summary>
/// 한 모니터·한 프레임의 불변 시각 스냅샷. 코디네이터가 Core 상태에서 만들어 렌더 스레드로 넘긴다.
/// 불변이라 스레드 경계를 안전하게 건넌다(공유 가변 상태 없음).
///
/// 현재는 이식된 상태만 담는다(커서 링·그리기 도형). 클릭/스크롤/트레일/흔들기 효과와
/// 라디얼 메뉴 시각은 EffectsState/Radial 뷰 이식 시 필드를 추가한다.
/// </summary>
public readonly record struct OverlayFrame(
    string MonitorId,
    PointD? CursorPosition,                  // null = 커서가 이 모니터에 없음
    RingVisual? Ring,
    IReadOnlyList<DrawingShape> Shapes,
    BrandingConfig Branding                  // 코브랜딩(워터마크/스플래시 등 렌더에 필요)
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
