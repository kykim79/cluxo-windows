using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// RadialHitTest.Classify — 거리/각도 → sector/sub/subSub 분류 검증. (Swift RadialHitTestTests 이식)
//
// 기본 트리(클로저 주입): sector 0 = sub 3개 [leaf, branch(자식5), branch(자식3)],
// 그 외 sector = sub 4개 모두 leaf. 거리 링: dead 50 / main 150 / sub 230 / subSub 290.
//
// 좌표 규약: clockwiseFromTop = (90 - atan2(dy,dx)). 12시(cw=0)는 (0, +r). 3시(cw=90)는 (r, 0).
public class RadialHitTestTests
{
    private static readonly RadialHitTest.Rings Rings = new(Dead: 50, Main: 150, Sub: 230, SubSub: 290);

    private static int SubCountOf(int s) => s == 0 ? 3 : 4;
    private static bool IsBranch(int s, int sub) => s == 0 && (sub == 1 || sub == 2);
    private static int SubSubCountOf(int s, int sub)
    {
        if (s != 0) return 0;
        if (sub == 1) return 5; // 반경 5단계
        if (sub == 2) return 3; // 경계 3단계
        return 0;
    }

    // 테스트용 span — 개수 기반. 항목당 22°, 50~140° clamp.
    private static double Span(int n) => Math.Min(140, Math.Max(50, n * 22.0));

    private static RadialHitTest.Hit Classify(double dx, double dy, int? lockSector = null, int? lockSub = null)
        => RadialHitTest.Classify(
            dx, dy, lockSector, lockSub, Rings,
            subCountOf: SubCountOf,
            subSpanOf: s => Span(SubCountOf(s)),
            isBranch: IsBranch,
            subSubCountOf: SubSubCountOf,
            subSubSpanOf: (s, sub) => Span(SubSubCountOf(s, sub)));

    // MARK: 거리 구간

    [Fact]
    public void DeadZone_NoSelection()
        => Assert.Equal(new RadialHitTest.Hit(null, null, null), Classify(10, 10)); // dist ~14 < 50

    [Fact]
    public void BeyondOuter_NoSelection()
        => Assert.Equal(new RadialHitTest.Hit(null, null, null), Classify(0, 320)); // dist 320 > 290

    [Fact]
    public void BeyondOuter_BranchClears()
        => Assert.Equal(new RadialHitTest.Hit(null, null, null), Classify(0, 320, lockSector: 0, lockSub: 1));

    [Fact]
    public void MainArea_SelectsSectorFreely_NoSub()
    {
        var hit = Classify(0, 100); // 12시, main(dist 100)
        Assert.Equal(0, hit.Sector);
        Assert.Null(hit.Sub);
        Assert.Null(hit.SubSub);
    }

    [Fact]
    public void MainArea_EachOfEightSectors()
    {
        for (int k = 0; k < 8; k++)
        {
            double ang = (90.0 - 45.0 * k) * Math.PI / 180;
            double dx = Math.Cos(ang) * 100;
            double dy = Math.Sin(ang) * 100;
            Assert.Equal(k, Classify(dx, dy).Sector);
        }
    }

    // MARK: Sub 영역 (2번째 ring)

    [Fact]
    public void SubArea_SelectsSub_NoSubSub()
    {
        var hit = Classify(0, 190); // sector 0, sub(dist 190), 12시 → 3개 fan 중앙
        Assert.Equal(0, hit.Sector);
        Assert.Equal(1, hit.Sub);
        Assert.Null(hit.SubSub);
    }

    [Fact]
    public void SubArea_LeafSectorNeverHasSubSub()
    {
        var hit = Classify(0, 190, lockSector: 2, lockSub: 1); // sector 2 = leaf 4개
        Assert.Equal(2, hit.Sector);
        Assert.NotNull(hit.Sub);
        Assert.Null(hit.SubSub);
    }

    // MARK: SubSub 영역 (3번째 ring)

    [Fact]
    public void SubSubArea_BranchSub_OpensChildren()
    {
        var hit = Classify(0, 260, lockSector: 0, lockSub: 1); // branch(자식5), 12시
        Assert.Equal(0, hit.Sector);
        Assert.Equal(1, hit.Sub);
        Assert.Equal(2, hit.SubSub); // 5개 fan 중앙
    }

    [Fact]
    public void SubSubArea_LeafSub_Closes()
        => Assert.Equal(new RadialHitTest.Hit(null, null, null),
                        Classify(0, 260, lockSector: 0, lockSub: 0)); // leaf sub 0 → 닫기

    [Fact]
    public void SubSubArea_LockedSubDoesNotDriftToNeighbor()
    {
        double ang = (90.0 - 20.0) * Math.PI / 180; // 12시에서 20° 벗어남
        double dx = Math.Cos(ang) * 260;
        double dy = Math.Sin(ang) * 260;
        var hit = Classify(dx, dy, lockSector: 0, lockSub: 1);
        Assert.Equal(1, hit.Sub); // lock 유지
        Assert.NotNull(hit.SubSub);
    }

    // MARK: 순수 헬퍼

    [Fact]
    public void ClockwiseFromTop_CardinalDirections()
    {
        Assert.Equal(0, RadialHitTest.ClockwiseFromTop(0, 100), 2);    // 12시
        Assert.Equal(90, RadialHitTest.ClockwiseFromTop(100, 0), 2);   // 3시
        Assert.Equal(180, RadialHitTest.ClockwiseFromTop(0, -100), 2); // 6시
        Assert.Equal(270, RadialHitTest.ClockwiseFromTop(-100, 0), 2); // 9시
    }
}
