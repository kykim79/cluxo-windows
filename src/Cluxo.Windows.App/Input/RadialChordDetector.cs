using Cluxo.Core;

namespace Cluxo.Windows.App.Input;

/// <summary>라디얼 chord 상태 전이 결과.</summary>
internal enum ChordEdge { None, Opened, Closed }

/// <summary>
/// 라디얼 메뉴 hold 트리거(⌃⌥,)를 키보드 후킹 스트림에서 직접 감지한다.
/// RegisterHotKey는 누름 1회만 알려 hold 불가 → 키 전이로 chord down/up을 판정(INPUT-LAYER.md §5).
///
/// 규약:
///   - Ctrl &amp;&amp; Alt &amp;&amp; Comma 가 모두 눌린 상태로 "진입"하는 순간 → Opened (한 번).
///   - 그 상태에서 셋 중 아무거나 떼여 조건이 깨지는 순간 → Closed (한 번).
///   - chord 유지 중 다른 키 입력은 무시(라디얼 navigation은 커서로).
///
/// Ctrl/Alt 상태는 <see cref="ModifierTracker"/>가 준 <see cref="KeyModifiers"/>로 받고,
/// Comma 눌림만 자체 추적. 순수 로직 → 단위 테스트 대상.
/// </summary>
internal sealed class RadialChordDetector
{
    private bool _commaDown;
    private bool _chordActive;

    /// <summary>
    /// 키 전이 + (전이 반영 후) 현재 모디파이어를 받아 edge 판정.
    /// </summary>
    public ChordEdge OnKey(uint vk, bool down, KeyModifiers modifiers)
    {
        if (vk == VirtualKeys.VK_OEM_COMMA) _commaDown = down;

        bool conditionMet = _commaDown
                            && modifiers.HasFlag(KeyModifiers.Control)
                            && modifiers.HasFlag(KeyModifiers.Alt);

        if (conditionMet && !_chordActive)
        {
            _chordActive = true;
            return ChordEdge.Opened;
        }
        if (!conditionMet && _chordActive)
        {
            _chordActive = false;
            return ChordEdge.Closed;
        }
        return ChordEdge.None;
    }
}
