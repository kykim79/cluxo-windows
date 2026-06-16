using Cluxo.Core;
using Cluxo.Windows.App.Input;

namespace Cluxo.Windows.App.Tests;

// ⌃⌥, hold 트리거 감지 (INPUT-LAYER.md §5). modifiers는 ModifierTracker가 전이 반영 후 준 값.
public class RadialChordDetectorTests
{
    private const KeyModifiers CtrlAlt = KeyModifiers.Control | KeyModifiers.Alt;

    [Fact]
    public void FullChord_OpensOnComma_ClosesOnCommaUp()
    {
        var d = new RadialChordDetector();
        // Ctrl, Alt 먼저 — comma 없으니 아직 None
        Assert.Equal(ChordEdge.None, d.OnKey(VirtualKeys.VK_LCONTROL, true, KeyModifiers.Control));
        Assert.Equal(ChordEdge.None, d.OnKey(VirtualKeys.VK_LMENU, true, CtrlAlt));
        // comma 눌리는 순간 진입
        Assert.Equal(ChordEdge.Opened, d.OnKey(VirtualKeys.VK_OEM_COMMA, true, CtrlAlt));
        // comma 떼면 종료
        Assert.Equal(ChordEdge.Closed, d.OnKey(VirtualKeys.VK_OEM_COMMA, false, CtrlAlt));
    }

    [Fact]
    public void ReleasingModifier_ClosesChord()
    {
        var d = new RadialChordDetector();
        d.OnKey(VirtualKeys.VK_LCONTROL, true, KeyModifiers.Control);
        d.OnKey(VirtualKeys.VK_LMENU, true, CtrlAlt);
        Assert.Equal(ChordEdge.Opened, d.OnKey(VirtualKeys.VK_OEM_COMMA, true, CtrlAlt));
        // Ctrl 떼면(mods=Alt만) comma는 여전히 down이어도 조건 깨짐 → Closed
        Assert.Equal(ChordEdge.Closed, d.OnKey(VirtualKeys.VK_LCONTROL, false, KeyModifiers.Alt));
    }

    [Fact]
    public void CommaWithoutModifiers_DoesNotOpen()
    {
        var d = new RadialChordDetector();
        Assert.Equal(ChordEdge.None, d.OnKey(VirtualKeys.VK_OEM_COMMA, true, KeyModifiers.None));
    }

    [Fact]
    public void OtherKeyDuringChord_NoDuplicateEdge()
    {
        var d = new RadialChordDetector();
        d.OnKey(VirtualKeys.VK_LCONTROL, true, KeyModifiers.Control);
        d.OnKey(VirtualKeys.VK_LMENU, true, CtrlAlt);
        Assert.Equal(ChordEdge.Opened, d.OnKey(VirtualKeys.VK_OEM_COMMA, true, CtrlAlt));
        // chord 유지 중 다른 키 — Opened 중복 발생 없음
        Assert.Equal(ChordEdge.None, d.OnKey(0x41, true, CtrlAlt));
        Assert.Equal(ChordEdge.None, d.OnKey(0x41, false, CtrlAlt));
    }

    [Fact]
    public void Reopen_AfterClose()
    {
        var d = new RadialChordDetector();
        d.OnKey(VirtualKeys.VK_OEM_COMMA, true, CtrlAlt); // Opened
        d.OnKey(VirtualKeys.VK_OEM_COMMA, false, CtrlAlt); // Closed
        Assert.Equal(ChordEdge.Opened, d.OnKey(VirtualKeys.VK_OEM_COMMA, true, CtrlAlt)); // 재진입
    }
}
