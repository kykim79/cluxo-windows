using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// ShakeState.Record(x,y,at) — 마우스 흔들기 감지 알고리즘 검증. (Swift ShakeDetectionTests 이식)
//
// 감지 조건 (각 축 독립): |v| > 150 + 이전 |v| > 150 + 부호 반대(방향 전환),
// 0.5초 안에 전환 5회 누적 → detect. dedup: detect 후 0.5초 안엔 재발화 차단.
// 진동 한 번분 = 0,amp,0,... (dt=0.05·amp=100이면 |v|=2000 > 150).
// 모든 테스트는 시간을 명시적으로 주입해 wall clock 의존 없음.
public class ShakeStateTests
{
    [Fact]
    public void EmptyStateNoDetection()
    {
        var state = new ShakeState();
        Assert.False(state.Record(100, 0, 0));
    }

    [Fact]
    public void FirstSampleNeverDetects()
    {
        var state = new ShakeState();
        Assert.False(state.Record(0, 0, 0));
    }

    [Fact]
    public void SingleFastMoveDoesNotDetect()
    {
        var state = new ShakeState();
        Assert.False(state.Record(0, 0, 0));
        Assert.False(state.Record(100, 0, 0.05)); // vx=+2000, lastV=0 → lastV 설정만
        Assert.False(state.Record(0, 0, 0.10));   // vx=-2000 → dirChanges=1
    }

    [Fact]
    public void HorizontalShakeDetects()
    {
        var state = new ShakeState();
        const double dt = 0.05;
        double t = 0.0;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(100, 0, t)); t += dt; // lastV=+2000
        Assert.False(state.Record(0, 0, t)); t += dt;   // dirChanges=1
        Assert.False(state.Record(100, 0, t)); t += dt; // dirChanges=2
        Assert.False(state.Record(0, 0, t)); t += dt;   // dirChanges=3
        Assert.False(state.Record(100, 0, t)); t += dt; // dirChanges=4
        Assert.True(state.Record(0, 0, t));             // dirChanges=5 → detect
    }

    [Fact]
    public void VerticalShakeDetects()
    {
        var state = new ShakeState();
        const double dt = 0.05;
        double t = 0.0;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(0, 100, t)); t += dt;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(0, 100, t)); t += dt;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(0, 100, t)); t += dt;
        Assert.True(state.Record(0, 0, t));
    }

    [Fact]
    public void DiagonalShakeDetects()
    {
        var state = new ShakeState();
        const double dt = 0.05;
        double t = 0.0;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(100, 200, t)); t += dt;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(100, 200, t)); t += dt;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(100, 200, t)); t += dt;
        Assert.True(state.Record(0, 0, t));
    }

    [Fact]
    public void VerticalShakeNegativeY()
    {
        var state = new ShakeState();
        const double dt = 0.05;
        double t = 0.0;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(0, -100, t)); t += dt;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(0, -100, t)); t += dt;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(0, -100, t)); t += dt;
        Assert.True(state.Record(0, 0, t));
    }

    [Fact]
    public void CounterResetsAfterDetection()
    {
        var state = new ShakeState();
        const double dt = 0.05;
        double t = 0.0;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        Assert.True(state.Record(0, 0, t));
        t += 0.6; // dedup window 통과
        state.Record(100, 0, t); t += dt;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        state.Record(0, 0, t); t += dt;
        Assert.True(state.Record(100, 0, t));
    }

    [Fact]
    public void DedupWithinHalfSecond()
    {
        var state = new ShakeState();
        const double dt = 0.05;
        double t = 0.0;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        Assert.True(state.Record(0, 0, t)); t += dt;
        state.Record(100, 0, t); t += dt;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        Assert.False(state.Record(0, 0, t)); // dedup window 안엔 차단
    }

    [Fact]
    public void DiagonalDetectsOnlyOncePerShake()
    {
        var state = new ShakeState();
        const double dt = 0.05;
        double t = 0.0;
        int detectionCount = 0;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 100, t); t += dt;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 100, t); t += dt;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 100, t); t += dt;
        if (state.Record(0, 0, t)) detectionCount++; t += dt;
        state.Record(100, 100, t); t += dt;
        if (state.Record(0, 0, t)) detectionCount++; t += dt;
        Assert.Equal(1, detectionCount); // 양 축 dedup → 한 번만
    }

    [Fact]
    public void SlowMovementNoDetection()
    {
        var state = new ShakeState();
        const double dt = 0.05;
        const double small = 5; // |v| = 100 < 임계 150
        double t = 0.0;
        for (int i = 0; i < 10; i++)
        {
            Assert.False(state.Record(0, 0, t)); t += dt;
            Assert.False(state.Record(small, small, t)); t += dt;
        }
    }

    [Fact]
    public void FourDirChangesNotEnough()
    {
        var state = new ShakeState();
        const double dt = 0.05;
        double t = 0.0;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(100, 0, t)); t += dt;
        Assert.False(state.Record(0, 0, t)); t += dt;   // dirChanges=1
        Assert.False(state.Record(100, 0, t)); t += dt; // dirChanges=2
        Assert.False(state.Record(0, 0, t)); t += dt;   // dirChanges=3
        Assert.False(state.Record(100, 0, t));          // dirChanges=4 → 미감지
    }

    [Fact]
    public void SensitiveDetectsWithFewerShakes()
    {
        var state = new ShakeState { RequiredDirChanges = 3 };
        const double dt = 0.05;
        double t = 0.0;
        Assert.False(state.Record(0, 0, t)); t += dt;
        Assert.False(state.Record(100, 0, t)); t += dt;
        Assert.False(state.Record(0, 0, t)); t += dt;   // dirChanges=1
        Assert.False(state.Record(100, 0, t)); t += dt; // dirChanges=2
        Assert.True(state.Record(0, 0, t));             // dirChanges=3 → detect
    }

    [Fact]
    public void InsensitiveRequiresMoreShakes()
    {
        var state = new ShakeState { RequiredDirChanges = 8 };
        const double dt = 0.05;
        double t = 0.0;
        double[] xs = { 0, 100, 0, 100, 0, 100, 0 };
        bool detected = false;
        foreach (var x in xs) { detected = state.Record(x, 0, t) || detected; t += dt; }
        Assert.False(detected); // 둔감(8회)에서 전환 5회로 미감지
    }

    [Fact]
    public void GapDuringShakePreventsDetection()
    {
        var state = new ShakeState();
        const double dt = 0.05;
        double t = 0.0;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        state.Record(0, 0, t); t += dt;
        state.Record(100, 0, t); t += dt;
        t += 0.6; // 긴 갭
        Assert.False(state.Record(0, 0, t)); // recent 만료 → detect 불가
    }

    [Fact]
    public void OldRecordsExpired()
    {
        var state = new ShakeState();
        Assert.False(state.Record(0, 0, 0));
        Assert.False(state.Record(1000, 0, 0.05));
        Assert.False(state.Record(0, 0, 1.05)); // 1초 갭 → 이전 샘플 제거
    }

    [Fact]
    public void ZeroTimeStepIsSafe()
    {
        var state = new ShakeState();
        Assert.False(state.Record(0, 0, 0));
        Assert.False(state.Record(100, 0, 0));      // dt=0 → skip
        Assert.False(state.Record(200, 0, 0.0005)); // dt<0.001 → skip
    }
}
