using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// RadialMenuController — 열기/reveal/선택추적 + 실행 dispatch 검증.
public class RadialMenuControllerTests
{
    private static (RadialMenuController c, CursorSettings s, CursorRuntimeState r) Make()
    {
        var s = new CursorSettings(new JsonSettingsStore());
        var r = new CursorRuntimeState();
        return (new RadialMenuController(s, r), s, r);
    }

    // ── 열기 / reveal / 선택 추적 ────────────────────────────────

    [Fact]
    public void Open_SetsActiveAndCenter()
    {
        var (c, _, r) = Make();
        c.Open(new PointD(500, 400), now: 0);
        Assert.True(r.IsRadialMenuActive);
        Assert.False(r.IsRadialMenuVisible); // reveal 전
        Assert.Equal(new PointD(500, 400), r.RadialMenuCenter);
        Assert.Null(r.RadialMenuSelectedSector);
    }

    [Fact]
    public void Update_RevealsAfterThreshold()
    {
        var (c, _, r) = Make();
        c.Open(new PointD(0, 0), now: 0);
        c.Update(new PointD(0, 0), now: 0.1);  // 0.1 < 0.15
        Assert.False(r.IsRadialMenuVisible);
        c.Update(new PointD(0, 0), now: 0.15); // marking mode 진입
        Assert.True(r.IsRadialMenuVisible);
    }

    [Fact]
    public void Update_TracksSector()
    {
        var (c, _, r) = Make();
        c.Open(new PointD(0, 0), now: 0);
        c.Update(new PointD(0, 80), now: 0.2); // 12시 메인 영역 → sector 0 (Spotlight)
        Assert.Equal(0, r.RadialMenuSelectedSector);
    }

    [Fact]
    public void Update_NoOp_WhenNotActive()
    {
        var (c, _, r) = Make();
        c.Update(new PointD(0, 80), now: 1); // Open 안 함
        Assert.Null(r.RadialMenuSelectedSector);
    }

    // ── 실행 dispatch (런타임 선택 세팅 후 Close) ────────────────

    private static void Select(CursorRuntimeState r, int sector, int? sub = null, int? subSub = null)
    {
        r.RadialMenuSelectedSector = sector;
        r.RadialMenuSelectedSubItem = sub;
        r.RadialMenuSelectedSubSubItem = subSub;
    }

    [Fact]
    public void Close_Spotlight_MainToggles()
    {
        var (c, _, r) = Make();
        c.Open(PointD.Zero, 0);
        Select(r, (int)RadialMenuItem.Spotlight); // 메인(sub null)
        c.Close();
        Assert.True(r.IsSpotlightActive);
        Assert.False(r.IsRadialMenuActive); // 닫힘
    }

    [Fact]
    public void Close_Color_SetsRingColor()
    {
        var (c, s, r) = Make();
        c.Open(PointD.Zero, 0);
        Select(r, (int)RadialMenuItem.Color, sub: 1); // ColorCases[1] = Red
        c.Close();
        Assert.Equal(RingColor.Red, s.RingColor);
    }

    [Fact]
    public void Close_Glow_TogglesTrail()
    {
        var (c, s, r) = Make();
        Assert.False(s.IsTrailEnabled);
        c.Open(PointD.Zero, 0);
        Select(r, (int)RadialMenuItem.Glow, sub: 1); // 트레일
        c.Close();
        Assert.True(s.IsTrailEnabled);
    }

    [Fact]
    public void Close_RingSize_SubSubSetsSize()
    {
        var (c, s, r) = Make();
        c.Open(PointD.Zero, 0);
        Select(r, (int)RadialMenuItem.RingSize, sub: 0, subSub: 2); // SizeCases[2] = Large
        c.Close();
        Assert.Equal(RingSize.Large, s.RingSize);
    }

    [Fact]
    public void Close_Keystroke_TimeoutSub()
    {
        var (c, s, r) = Make();
        c.Open(PointD.Zero, 0);
        Select(r, (int)RadialMenuItem.Keystroke, sub: 2); // times[1] = 2초
        c.Close();
        Assert.Equal(2.0, s.KeystrokeTimeout);
    }

    [Fact]
    public void Close_Inspector_TogglesActive()
    {
        var (c, _, r) = Make();
        c.Open(PointD.Zero, 0);
        Select(r, (int)RadialMenuItem.Inspector, sub: 0);
        c.Close();
        Assert.True(r.IsInspectorActive);
    }

    [Fact]
    public void Close_DeadZone_Cancels_NoChange()
    {
        var (c, s, r) = Make();
        var before = s.RingColor;
        c.Open(PointD.Zero, 0);
        // 선택 없음(dead zone) — sector null
        c.Close();
        Assert.Equal(before, s.RingColor);
        Assert.False(r.IsRadialMenuActive);
    }
}
