using Cluxo.Core;

namespace Cluxo.Windows.App.Input;

/// <summary>라디얼 chord 상태 전이 결과.</summary>
internal enum ChordEdge { None, Opened, Closed }

/// <summary>
/// 라디얼 메뉴 토글 트리거(⌃⌥.)를 키보드 후킹 스트림에서 직접 감지한다.
/// (콤마 ⌃⌥,는 Windows Terminal "기본 설정 열기"와 충돌해 마침표로 변경.)
/// 코디네이터는 Opened 전이만 받아 ToggleRadial로 처리(누를 때마다 토글) — Closed는 무시.
///
/// 규약:
///   - Ctrl &amp;&amp; Alt &amp;&amp; Period 가 모두 눌린 상태로 "진입"하는 순간 → Opened (한 번).
///   - 그 상태에서 셋 중 아무거나 떼여 조건이 깨지는 순간 → Closed (한 번).
///   - chord 유지(auto-repeat) 중엔 재발화 안 함(전이에서만).
///
/// Ctrl/Alt 상태는 <see cref="ModifierTracker"/>가 준 <see cref="KeyModifiers"/>로 받고,
/// Period 눌림만 자체 추적. 순수 로직 → 단위 테스트 대상.
/// </summary>
internal sealed class RadialChordDetector
{
    private bool _periodDown;
    private bool _chordActive;

    /// <summary>
    /// 키 전이 + (전이 반영 후) 현재 모디파이어를 받아 edge 판정.
    /// </summary>
    public ChordEdge OnKey(uint vk, bool down, KeyModifiers modifiers)
    {
        if (vk == VirtualKeys.VK_OEM_PERIOD) _periodDown = down;

        bool conditionMet = _periodDown
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
