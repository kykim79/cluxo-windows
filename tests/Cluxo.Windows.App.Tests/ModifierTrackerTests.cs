using Cluxo.Core;
using Cluxo.Windows.App.Input;

namespace Cluxo.Windows.App.Tests;

// 키 down/up 스트림 → 모디파이어 상태 (좌/우 독립 추적). 순수 로직.
public class ModifierTrackerTests
{
    [Fact]
    public void Empty_IsNone()
        => Assert.Equal(KeyModifiers.None, new ModifierTracker().Current);

    [Fact]
    public void CtrlDown_SetsControl_UpClears()
    {
        var t = new ModifierTracker();
        t.OnKey(VirtualKeys.VK_LCONTROL, down: true);
        Assert.Equal(KeyModifiers.Control, t.Current);
        t.OnKey(VirtualKeys.VK_LCONTROL, down: false);
        Assert.Equal(KeyModifiers.None, t.Current);
    }

    [Fact]
    public void CtrlAlt_Combine()
    {
        var t = new ModifierTracker();
        t.OnKey(VirtualKeys.VK_LCONTROL, true);
        t.OnKey(VirtualKeys.VK_LMENU, true);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Alt, t.Current);
    }

    [Fact]
    public void LeftAndRightCtrl_Independent_StaysHeldUntilBothReleased()
    {
        var t = new ModifierTracker();
        t.OnKey(VirtualKeys.VK_LCONTROL, true);
        t.OnKey(VirtualKeys.VK_RCONTROL, true);
        t.OnKey(VirtualKeys.VK_LCONTROL, false); // 왼쪽만 뗌 — 오른쪽 유지
        Assert.Equal(KeyModifiers.Control, t.Current);
        t.OnKey(VirtualKeys.VK_RCONTROL, false);
        Assert.Equal(KeyModifiers.None, t.Current);
    }

    [Fact]
    public void NonModifierKey_Ignored()
    {
        var t = new ModifierTracker();
        t.OnKey(0x41, true); // 'A'
        Assert.Equal(KeyModifiers.None, t.Current);
    }

    [Fact]
    public void Seed_InjectsAlreadyHeldModifier()
    {
        var t = new ModifierTracker();
        t.Seed(VirtualKeys.VK_LWIN, down: true); // 후킹 설치 시 이미 눌림
        Assert.Equal(KeyModifiers.Win, t.Current);
    }
}
