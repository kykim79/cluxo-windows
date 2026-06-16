using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// EffectsState — 시간 주입 효과 큐 + 프레임 프루닝 검증. (Swift EffectsState 순수 이식, Task.sleep→Prune)
public class EffectsStateTests
{
    // MARK: 클릭

    [Fact]
    public void AddClick_Single_NoDoubleClick()
    {
        var e = new EffectsState();
        e.AddClick(new PointD(10, 20), isRight: false, isDouble: false, now: 0, animationSpeed: 1);
        Assert.Single(e.Clicks);
        Assert.Empty(e.DoubleClicks);
        Assert.Equal(0.7, e.Clicks[0].ExpiresAt, 6);
        Assert.Equal(new PointD(10, 20), e.Clicks[0].Position);
    }

    [Fact]
    public void AddClick_Double_AddsBoth()
    {
        var e = new EffectsState();
        e.AddClick(PointD.Zero, isRight: false, isDouble: true, now: 0, animationSpeed: 1);
        Assert.Single(e.Clicks);
        Assert.Single(e.DoubleClicks);
        Assert.Equal(0.9, e.DoubleClicks[0].ExpiresAt, 6);
    }

    [Fact]
    public void AnimationSpeed_ScalesLifetime()
    {
        var e = new EffectsState();
        e.AddClick(PointD.Zero, false, false, now: 1.0, animationSpeed: 2.0);
        Assert.Equal(1.0 + 0.7 * 2.0, e.Clicks[0].ExpiresAt, 6); // now + life*speed
    }

    // MARK: Prune (만료)

    [Fact]
    public void Prune_RemovesAtOrAfterExpiry()
    {
        var e = new EffectsState();
        e.AddClick(PointD.Zero, false, false, now: 0, animationSpeed: 1); // expires 0.7
        e.Prune(0.69);
        Assert.Single(e.Clicks);   // 아직 만료 전
        e.Prune(0.7);
        Assert.Empty(e.Clicks);    // ExpiresAt <= now → 제거
    }

    [Fact]
    public void Prune_AcrossAllQueues()
    {
        var e = new EffectsState();
        e.AddClick(PointD.Zero, false, false, now: 0, animationSpeed: 1);   // 0.7
        e.AddScroll(PointD.Zero, true, true, 5, null, now: 0, animationSpeed: 1); // 0.65
        e.AddShake(PointD.Zero, now: 0, animationSpeed: 1);                 // 1.8
        e.AddIdlePulse(PointD.Zero, now: 0);                               // 0.9
        e.Prune(1.0); // click·scroll·idle 만료, shake 생존
        Assert.Empty(e.Clicks);
        Assert.Empty(e.Scrolls);
        Assert.Empty(e.IdlePulses);
        Assert.Single(e.Shakes);
    }

    // MARK: 스크롤 영역 dedup

    [Fact]
    public void AddScroll_SameRegion_RemovesPrevious()
    {
        var e = new EffectsState();
        var region = new RectD(0, 0, 1000, 1000);
        e.AddScroll(new PointD(10, 10), true, true, 5, region, now: 0, animationSpeed: 1);
        e.AddScroll(new PointD(20, 20), true, true, 5, region, now: 0, animationSpeed: 1);
        Assert.Single(e.Scrolls); // 같은 영역 이전 효과 제거
        Assert.Equal(new PointD(20, 20), e.Scrolls[0].Position);
    }

    [Fact]
    public void AddScroll_DifferentRegions_Coexist()
    {
        var e = new EffectsState();
        var regionA = new RectD(0, 0, 1000, 1000);
        var regionB = new RectD(2000, 0, 1000, 1000);
        e.AddScroll(new PointD(10, 10), true, true, 5, regionA, now: 0, animationSpeed: 1);
        e.AddScroll(new PointD(2010, 10), true, true, 5, regionB, now: 0, animationSpeed: 1);
        Assert.Equal(2, e.Scrolls.Count); // 다른 모니터 효과 유지
    }

    // MARK: 흔들기 수명 floor

    [Theory]
    [InlineData(0.1, 1.5)] // max(1.5, 0.18) = 1.5
    [InlineData(1.0, 1.8)] // max(1.5, 1.8) = 1.8
    [InlineData(2.0, 3.6)] // max(1.5, 3.6) = 3.6
    public void AddShake_LifetimeHasFloor(double speed, double expectedExpiry)
    {
        var e = new EffectsState();
        e.AddShake(PointD.Zero, now: 0, animationSpeed: speed);
        Assert.Equal(expectedExpiry, e.Shakes[0].ExpiresAt, 6);
    }

    // MARK: Trail cap

    [Fact]
    public void Trail_CapsAt26_DropsOldest()
    {
        var e = new EffectsState();
        for (int i = 0; i < 30; i++) e.UpdateTrail(new PointD(i, 0));
        Assert.Equal(26, e.Trail.Count);
        Assert.Equal(new PointD(4, 0), e.Trail[0].Position); // 0..3 떨어져 나감
        Assert.Equal(new PointD(29, 0), e.Trail[^1].Position);
    }

    [Fact]
    public void ClearTrail_Empties()
    {
        var e = new EffectsState();
        e.UpdateTrail(PointD.Zero);
        e.ClearTrail();
        Assert.Empty(e.Trail);
    }

    [Fact]
    public void DragTrail_CapsAt14()
    {
        var e = new EffectsState();
        for (int i = 0; i < 20; i++) e.UpdateDragTrail(new PointD(i, 0));
        Assert.Equal(14, e.DragTrail.Count);
    }

    // MARK: 드래그 trail fade (40ms당 1점)

    [Fact]
    public void DragTrailFade_DrainsOnePointPer40ms()
    {
        var e = new EffectsState();
        for (int i = 0; i < 5; i++) e.UpdateDragTrail(new PointD(i, 0));
        e.BeginDragTrailFade(now: 0);
        e.Prune(0.0); Assert.Equal(5, e.DragTrail.Count);
        e.Prune(0.04); Assert.Equal(4, e.DragTrail.Count);
        e.Prune(0.16); Assert.Single(e.DragTrail); // floor(0.16/0.04)=4 제거 → 1 남음
        e.Prune(0.20); Assert.Empty(e.DragTrail);
    }

    [Fact]
    public void UpdateDragTrail_CancelsFade()
    {
        var e = new EffectsState();
        for (int i = 0; i < 5; i++) e.UpdateDragTrail(new PointD(i, 0));
        e.BeginDragTrailFade(now: 0);
        e.UpdateDragTrail(new PointD(99, 0)); // 새 드래그 → fade 취소
        e.Prune(1.0);                         // fade 안 돌아야
        Assert.Equal(6, e.DragTrail.Count);
    }
}
