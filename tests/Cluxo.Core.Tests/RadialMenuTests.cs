using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// RadialMenu — 트리 구조 + 현재 상태 로직 + RadialHitTest 연동. (Swift RadialMenuItem 이식)
public class RadialMenuTests
{
    private static CursorSettings DefaultSettings() => new(new JsonSettingsStore());
    private static CursorRuntimeState Runtime() => new();

    // ── 구조 ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(RadialMenuItem.Spotlight, 3)]
    [InlineData(RadialMenuItem.Magnifier, 3)]
    [InlineData(RadialMenuItem.Glow, 4)]
    [InlineData(RadialMenuItem.RingSize, 4)]
    [InlineData(RadialMenuItem.Color, 7)]   // custom 제외 7색
    [InlineData(RadialMenuItem.RingShape, 4)]
    [InlineData(RadialMenuItem.Inspector, 2)]
    [InlineData(RadialMenuItem.Keystroke, 5)]
    public void SubCounts(RadialMenuItem m, int expected)
        => Assert.Equal(expected, m.SubCount());

    [Fact]
    public void IsBranch_SpotlightSubs()
    {
        var subs = RadialMenuItem.Spotlight.SubItems();
        Assert.False(subs[0].IsBranch); // 토글 = leaf
        Assert.True(subs[1].IsBranch);  // 반경 = branch(5)
        Assert.True(subs[2].IsBranch);  // 경계 = branch(3)
        Assert.Equal(5, subs[1].Children!.Count);
    }

    [Fact]
    public void Color_AllLeaf()
        => Assert.All(RadialMenuItem.Color.SubItems(), s => Assert.False(s.IsBranch));

    [Fact]
    public void AllSectors_HaveLabelAndDesc()
    {
        foreach (var m in RadialMenu.Items)
        {
            Assert.False(string.IsNullOrEmpty(m.Label()));
            Assert.False(string.IsNullOrEmpty(m.Desc()));
        }
    }

    [Fact]
    public void Spans_AreClamped_50to150()
    {
        foreach (var m in RadialMenu.Items)
            Assert.InRange(m.SubSpan(), 50, 150);
        Assert.InRange(RadialMenuItem.Spotlight.SubSubSpan(1), 50, 150); // 반경 branch
        Assert.Equal(0, RadialMenuItem.Spotlight.SubSubSpan(0));         // leaf → 0
    }

    // ── 현재 상태 로직 ───────────────────────────────────────────

    [Fact]
    public void CurrentValue_Defaults()
    {
        var s = DefaultSettings();
        var r = Runtime();
        Assert.Equal("빨간색", RadialMenuItem.Color.CurrentValue(s, r));       // 기본 Red
        Assert.Equal("보통 (54pt)", RadialMenuItem.RingSize.CurrentValue(s, r)); // Medium
        Assert.Equal("꺼짐", RadialMenuItem.Spotlight.CurrentValue(s, r));      // 비활성
        Assert.Equal("1/4 켜짐", RadialMenuItem.Glow.CurrentValue(s, r));       // idlePulse만 default on
        Assert.Equal("0/2 켜짐", RadialMenuItem.Inspector.CurrentValue(s, r));
        Assert.Equal("꺼짐", RadialMenuItem.Keystroke.CurrentValue(s, r));
    }

    [Fact]
    public void IsSubCurrent_Color_HighlightsRed()
    {
        var s = DefaultSettings();
        var r = Runtime();
        // RingColor 순서 Yellow,Red,Blue,Green,Cyan,... → 기본 Red index 1
        Assert.True(RadialMenuItem.Color.IsSubCurrent(1, s, r));
        Assert.False(RadialMenuItem.Color.IsSubCurrent(0, s, r));
    }

    [Fact]
    public void IsSubCurrent_Glow_Toggles()
    {
        var s = DefaultSettings();
        var r = Runtime();
        Assert.True(RadialMenuItem.Glow.IsSubCurrent(2, s, r));  // 정지펄스 default on
        Assert.False(RadialMenuItem.Glow.IsSubCurrent(0, s, r)); // 글로우 off
        s.IsGlowEnabled = true;
        Assert.True(RadialMenuItem.Glow.IsSubCurrent(0, s, r));
    }

    [Fact]
    public void IsSubSubCurrent_RingSize()
    {
        var s = DefaultSettings();
        var r = Runtime();
        // 크기 sub(0): RingSize 순서 Small,Medium,.. → Medium index 1
        Assert.True(RadialMenuItem.RingSize.IsSubSubCurrent(0, 1, s, r));
        Assert.False(RadialMenuItem.RingSize.IsSubSubCurrent(0, 0, s, r));
        // 투명도 sub(1): RingOpacities[0]=1.0 = 기본
        Assert.True(RadialMenuItem.RingSize.IsSubSubCurrent(1, 0, s, r));
    }

    [Fact]
    public void IsSubCurrent_Keystroke_TimeoutMatch()
    {
        var s = DefaultSettings();
        var r = Runtime();
        s.IsKeystrokeEnabled = true;
        s.KeystrokeTimeout = 2.0;
        Assert.True(RadialMenuItem.Keystroke.IsSubCurrent(0, s, r));  // 토글 on
        Assert.True(RadialMenuItem.Keystroke.IsSubCurrent(2, s, r));  // "2초" (times[1]=2)
        Assert.False(RadialMenuItem.Keystroke.IsSubCurrent(1, s, r)); // "1초"
    }

    // ── RadialHitTest 연동 (트리가 hit-test 구동) ────────────────

    [Fact]
    public void Tree_DrivesRadialHitTest()
    {
        var rings = new RadialHitTest.Rings(
            Tokens.Radial.DeadRadius, Tokens.Radial.MainOuter, Tokens.Radial.SubOuter, Tokens.Radial.SubSubOuter);

        RadialHitTest.Hit Classify(double dx, double dy, int? sector, int? sub) =>
            RadialHitTest.Classify(dx, dy, sector, sub, rings,
                s => ((RadialMenuItem)s).SubCount(),
                s => ((RadialMenuItem)s).SubSpan(),
                (s, sub) => ((RadialMenuItem)s).SubItems()[sub].IsBranch,
                (s, sub) =>
                {
                    var subs = ((RadialMenuItem)s).SubItems();
                    return sub < subs.Count && subs[sub].Children is { Count: > 0 } k ? k.Count : 0;
                },
                (s, sub) => ((RadialMenuItem)s).SubSubSpan(sub));

        // 12시 메인 영역 → sector 0 (Spotlight). clockwiseFromTop: (0,+r)=cw0
        var mainHit = Classify(0, 80, null, null);
        Assert.Equal(0, mainHit.Sector);
        Assert.Null(mainHit.Sub);

        // Spotlight sub1(반경, branch) lock + subSub ring → 자식 선택
        var subSubHit = Classify(0, 200, 0, 1);
        Assert.Equal(0, subSubHit.Sector);
        Assert.Equal(1, subSubHit.Sub);
        Assert.NotNull(subSubHit.SubSub); // branch라 자식 fan 선택됨
    }
}
