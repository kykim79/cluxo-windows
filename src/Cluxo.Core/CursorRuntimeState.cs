namespace Cluxo.Core;

/// <summary>
/// 마우스 위치·가시성·활성 상태·드래그 모션 시멘틱의 런타임 상태. (Swift <c>CursorRuntimeState</c> 이식)
///
/// 설정(CursorSettings)·효과(EffectsState)와 분리 — 60Hz로 갱신되는 cursorPosition이 무관한 것을
/// 흔들지 않게. SwiftUI 스프링 애니메이션 값(ringClickScale/tilt, glowMultiplier)은 렌더 계층이
/// 담당하므로 Core엔 없다(클릭 squash는 render가 ClickEffect 보고, glow는 dragVelocity에서 파생).
/// dragAngle 누적은 이미 이식한 <see cref="DragAngleAccumulator"/> 재사용. magnifier 이미지/권한은 v1.1/플랫폼.
/// </summary>
public sealed class CursorRuntimeState
{
    // ── 위치 / 가시성 ────────────────────────────────────────────
    public PointD CursorPosition { get; set; }
    public bool IsCursorVisible { get; set; } = true;

    // ── 활성 토글 ────────────────────────────────────────────────
    public bool IsSpotlightActive { get; set; }
    public bool IsMagnifierActive { get; set; }
    public bool IsInspectorActive { get; set; } // 좌표 라벨 표시

    // ── 라디얼 메뉴 선택 상태 (콘텐츠 RadialMenuItem 트리는 별도) ──
    public bool IsRadialMenuActive { get; set; }
    public bool IsRadialMenuVisible { get; set; }   // hold 임계 후만 true (marking mode)
    public bool RadialMenuDismissing { get; set; }
    public bool RadialMenuShowHelp { get; set; }    // 첫 N회 hold 동안 사용법
    public bool RadialMenuShowDesc { get; set; }    // dwell 시 항목 설명
    public PointD RadialMenuCenter { get; set; }
    public int? RadialMenuSelectedSector { get; set; }
    public int? RadialMenuSelectedSubItem { get; set; }
    public int? RadialMenuSelectedSubSubItem { get; set; }

    // ── 드래그 / 모션 ────────────────────────────────────────────
    public bool IsDragging { get; private set; }
    public PointD? DragOrigin { get; private set; }
    public double DragVelocity { get; private set; } // pt/s, EMA smoothed (#14 Speed Glow)
    public bool AnchoredLineVisible { get; private set; } // #17 — 거리/시간 임계 만족 시만

    private readonly DragAngleAccumulator _angle = new();
    public double DragAngle => _angle.Angle;

    private double _dragStartTime;

    // #17 임계 (Swift static)
    private const double AnchoredLineDistanceThreshold = 100; // pt
    private const double AnchoredLineTimeThreshold = 1.0;     // seconds
    private const double VelocityAlpha = 0.3;                 // EMA — 새 값 30%

    /// <summary>드래그 시작 — 각도 리셋, 원점 기록, anchored line 숨김.</summary>
    public void StartDrag(PointD origin, double now)
    {
        _angle.EndDrag(); // dragAngle 0
        DragOrigin = origin;
        AnchoredLineVisible = false;
        _dragStartTime = now;
        IsDragging = true;
    }

    /// <summary>
    /// anchored line 자동 활성 — 거리(100pt 초과) 또는 시간(1초 경과) 임계. 매 프레임/이동마다 호출.
    /// 짧은 드래그(스크롤바)는 안 보이고, 의도적 긴 드래그(영역 강조)는 자연스럽게 표시.
    /// </summary>
    public void CheckAnchoredLine(PointD currentPos, double now)
    {
        if (AnchoredLineVisible || DragOrigin is not { } origin) return;
        double dx = currentPos.X - origin.X, dy = currentPos.Y - origin.Y;
        bool farEnough = dx * dx + dy * dy
            > AnchoredLineDistanceThreshold * AnchoredLineDistanceThreshold;
        bool longEnough = now - _dragStartTime >= AnchoredLineTimeThreshold;
        if (farEnough || longEnough) AnchoredLineVisible = true;
    }

    /// <summary>새 raw 각도(atan2) → 최단 경로 누적. (DragAngleAccumulator 위임)</summary>
    public void UpdateDragAngle(double newAngle) => _angle.Update(newAngle);

    /// <summary>새 raw velocity(pt/s) EMA 누적 — 매 frame jitter 회피. alpha=0.3.</summary>
    public void UpdateDragVelocity(double rawVelocity)
        => DragVelocity = DragVelocity * (1 - VelocityAlpha) + rawVelocity * VelocityAlpha;

    /// <summary>드래그 종료 — 상태 리셋. (스냅백/페이드 애니메이션은 렌더 계층)</summary>
    public void EndDrag()
    {
        IsDragging = false;
        _angle.EndDrag();          // dragAngle 0
        DragVelocity = 0;
        AnchoredLineVisible = false;
        DragOrigin = null;         // Mac은 0.3초 fade 후 — 렌더가 페이드, Core는 즉시 클리어
    }
}
