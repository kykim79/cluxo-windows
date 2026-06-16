using Cluxo.Core;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Input;

/// <summary>RegisterHotKey 인자: fsModifiers 비트 + 가상 키.</summary>
internal readonly record struct HotkeyBinding(uint Modifiers, uint Vk);

/// <summary>
/// <see cref="HotkeyChord"/>(정규화된 모디파이어 + 키 이름)를 RegisterHotKey의 fsModifiers/vk로 매핑.
/// INPUT-LAYER.md §4. 순수 로직 → 단위 테스트 대상.
/// </summary>
internal static class HotkeyChordMapper
{
    // RegisterHotKey fsModifiers (winuser.h)
    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000; // hold 반복 WM_HOTKEY 억제

    /// <summary>chord → (fsModifiers, vk). 알 수 없는 키면 예외.</summary>
    public static HotkeyBinding Map(HotkeyChord chord)
    {
        uint mods = MOD_NOREPEAT;
        if (chord.Modifiers.HasFlag(KeyModifiers.Control)) mods |= MOD_CONTROL;
        if (chord.Modifiers.HasFlag(KeyModifiers.Alt)) mods |= MOD_ALT;
        if (chord.Modifiers.HasFlag(KeyModifiers.Shift)) mods |= MOD_SHIFT;
        if (chord.Modifiers.HasFlag(KeyModifiers.Win)) mods |= MOD_WIN;
        return new HotkeyBinding(mods, MapKey(chord.Key));
    }

    private static uint MapKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("빈 핫키 키", nameof(key));

        // 단일 글자/숫자 → VK = ASCII 대문자/숫자 코드
        if (key.Length == 1)
        {
            char c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }

        return key switch
        {
            "Space" => VirtualKeys.VK_SPACE,
            "Comma" => VirtualKeys.VK_OEM_COMMA,
            "Period" => VirtualKeys.VK_OEM_PERIOD,
            "Minus" => VirtualKeys.VK_OEM_MINUS,
            "Plus" or "Equals" => VirtualKeys.VK_OEM_PLUS,
            "Tab" => VirtualKeys.VK_TAB,
            "Escape" => VirtualKeys.VK_ESCAPE,
            "Return" or "Enter" => VirtualKeys.VK_RETURN,
            _ => throw new ArgumentException($"매핑되지 않은 핫키 키: '{key}'", nameof(key)),
        };
    }
}
