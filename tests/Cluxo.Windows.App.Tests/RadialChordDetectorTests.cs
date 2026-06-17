using Cluxo.Core;
using Cluxo.Windows.App.Input;

namespace Cluxo.Windows.App.Tests;

// ⌃⌥. hold 트리거 감지 (INPUT-LAYER.md §5). modifiers는 ModifierTracker가 전이 반영 후 준 값.
public class RadialChordDetectorTests
{
    private const KeyModifiers CtrlAlt = KeyModifiers.Control | KeyModifiers.Alt;

    [Fact]
    public void FullChord_OpensOnPeriod_ClosesOnPeriodUp()
    {
        var d = new RadialChordDetector();
        // Ctrl, Alt 먼저 — period 없으니 아직 None
        Assert.Equal(ChordEdge.None, d.OnKey(VirtualKeys.VK_LCONTROL, true, KeyModifiers.Control));
        Assert.Equal(ChordEdge.None, d.OnKey(VirtualKeys.VK_LMENU, true, CtrlAlt));
        // period 눌리는 순간 진입
        Assert.Equal(ChordEdge.Opened, d.OnKey(VirtualKeys.VK_OEM_PERIOD, true, CtrlAlt));
        // period 떼면 종료
        Assert.Equal(ChordEdge.Closed, d.OnKey(VirtualKeys.VK_OEM_PERIOD, false, CtrlAlt));
    }

    [Fact]
    public void ReleasingModifier_ClosesChord()
    {
        var d = new RadialChordDetector();
        d.OnKey(VirtualKeys.VK_LCONTROL, true, KeyModifiers.Control);
        d.OnKey(VirtualKeys.VK_LMENU, true, CtrlAlt);
        Assert.Equal(ChordEdge.Opened, d.OnKey(VirtualKeys.VK_OEM_PERIOD, true, CtrlAlt));
        // Ctrl 떼면(mods=Alt만) period는 여전히 down이어도 조건 깨짐 → Closed
        Assert.Equal(ChordEdge.Closed, d.OnKey(VirtualKeys.VK_LCONTROL, false, KeyModifiers.Alt));
    }

    [Fact]
    public void PeriodWithoutModifiers_DoesNotOpen()
    {
        var d = new RadialChordDetector();
        Assert.Equal(ChordEdge.None, d.OnKey(VirtualKeys.VK_OEM_PERIOD, true, KeyModifiers.None));
    }

    [Fact]
    public void OtherKeyDuringChord_NoDuplicateEdge()
    {
        var d = new RadialChordDetector();
        d.OnKey(VirtualKeys.VK_LCONTROL, true, KeyModifiers.Control);
        d.OnKey(VirtualKeys.VK_LMENU, true, CtrlAlt);
        Assert.Equal(ChordEdge.Opened, d.OnKey(VirtualKeys.VK_OEM_PERIOD, true, CtrlAlt));
        // chord 유지 중 다른 키 — Opened 중복 발생 없음
        Assert.Equal(ChordEdge.None, d.OnKey(0x41, true, CtrlAlt));
        Assert.Equal(ChordEdge.None, d.OnKey(0x41, false, CtrlAlt));
    }

    [Fact]
    public void Reopen_AfterClose()
    {
        var d = new RadialChordDetector();
        d.OnKey(VirtualKeys.VK_OEM_PERIOD, true, CtrlAlt); // Opened
        d.OnKey(VirtualKeys.VK_OEM_PERIOD, false, CtrlAlt); // Closed
        Assert.Equal(ChordEdge.Opened, d.OnKey(VirtualKeys.VK_OEM_PERIOD, true, CtrlAlt)); // 재진입
    }
}
