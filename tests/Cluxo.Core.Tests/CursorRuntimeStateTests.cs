using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// CursorRuntimeState — 드래그 모션 시멘틱 + anchored line 임계 검증. (Swift 이식)
public class CursorRuntimeStateTests
{
    // ── 드래그 속도 EMA ──────────────────────────────────────────

    [Fact]
    public void UpdateDragVelocity_EmaSmoothing()
    {
        var s = new CursorRuntimeState();
        Assert.Equal(0, s.DragVelocity);
        s.UpdateDragVelocity(100); // 0*0.7 + 100*0.3 = 30
        Assert.Equal(30, s.DragVelocity, 6);
        s.UpdateDragVelocity(100); // 30*0.7 + 100*0.3 = 51
        Assert.Equal(51, s.DragVelocity, 6);
        s.UpdateDragVelocity(100); // 51*0.7 + 30 = 65.7
        Assert.Equal(65.7, s.DragVelocity, 6);
    }

    // ── 드래그 상태 전이 ─────────────────────────────────────────

    [Fact]
    public void StartDrag_SetsState()
    {
        var s = new CursorRuntimeState();
        s.StartDrag(new PointD(10, 20), now: 0);
        Assert.True(s.IsDragging);
        Assert.Equal(new PointD(10, 20), s.DragOrigin);
        Assert.False(s.AnchoredLineVisible);
        Assert.Equal(0, s.DragAngle);
    }

    [Fact]
    public void EndDrag_ResetsState()
    {
        var s = new CursorRuntimeState();
        s.StartDrag(new PointD(0, 0), now: 0);
        s.UpdateDragAngle(1.0);
        s.UpdateDragVelocity(500);
        s.CheckAnchoredLine(new PointD(200, 0), now: 0); // 거리 임계 → visible
        Assert.True(s.AnchoredLineVisible);

        s.EndDrag();
        Assert.False(s.IsDragging);
        Assert.Equal(0, s.DragAngle);
        Assert.Equal(0, s.DragVelocity);
        Assert.False(s.AnchoredLineVisible);
        Assert.Null(s.DragOrigin);
    }

    // ── Anchored line 임계 ───────────────────────────────────────

    [Fact]
    public void AnchoredLine_DistanceThreshold()
    {
        var s = new CursorRuntimeState();
        s.StartDrag(new PointD(0, 0), now: 0);
        s.CheckAnchoredLine(new PointD(50, 0), now: 0.5);  // 50 < 100, 0.5s < 1s
        Assert.False(s.AnchoredLineVisible);
        s.CheckAnchoredLine(new PointD(150, 0), now: 0.5); // 150 > 100 → 표시
        Assert.True(s.AnchoredLineVisible);
    }

    [Fact]
    public void AnchoredLine_TimeThreshold()
    {
        var s = new CursorRuntimeState();
        s.StartDrag(new PointD(0, 0), now: 0);
        s.CheckAnchoredLine(new PointD(10, 0), now: 0.9); // 가깝고 짧음
        Assert.False(s.AnchoredLineVisible);
        s.CheckAnchoredLine(new PointD(10, 0), now: 1.0); // 1초 경과 → 표시
        Assert.True(s.AnchoredLineVisible);
    }

    [Fact]
    public void AnchoredLine_NoOp_WhenNotDragging()
    {
        var s = new CursorRuntimeState();
        s.CheckAnchoredLine(new PointD(500, 500), now: 5); // dragOrigin 없음 → 무시
        Assert.False(s.AnchoredLineVisible);
    }

    [Fact]
    public void StartDrag_AfterEnd_ResetsAnchoredLine()
    {
        var s = new CursorRuntimeState();
        s.StartDrag(new PointD(0, 0), now: 0);
        s.CheckAnchoredLine(new PointD(200, 0), now: 0);
        Assert.True(s.AnchoredLineVisible);
        s.EndDrag();
        s.StartDrag(new PointD(0, 0), now: 10); // 새 드래그 — 다시 숨김
        Assert.False(s.AnchoredLineVisible);
    }

    // ── 드래그 각도 (DragAngleAccumulator 위임) ──────────────────

    [Fact]
    public void UpdateDragAngle_WrapsShortestPath()
    {
        var s = new CursorRuntimeState();
        s.StartDrag(PointD.Zero, now: 0);
        s.UpdateDragAngle(Math.PI - 0.1);
        s.UpdateDragAngle(-Math.PI + 0.1); // +π→-π wrap, 최단(+0.2)
        Assert.Equal(Math.PI + 0.1, s.DragAngle, 9);
    }

    // ── 활성 토글 (plain) ────────────────────────────────────────

    [Fact]
    public void ActiveToggles_DefaultFalse()
    {
        var s = new CursorRuntimeState();
        Assert.False(s.IsSpotlightActive);
        Assert.False(s.IsMagnifierActive);
        Assert.False(s.IsInspectorActive);
        Assert.True(s.IsCursorVisible);
    }
}
