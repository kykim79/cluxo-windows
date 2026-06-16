using Cluxo.Core;

namespace Cluxo.Windows.App.Input;

/// <summary>
/// Win32 가상 키(VK) 상수 + Core 타입으로의 순수 매핑.
///
/// INPUT-LAYER.md §3: LL 키보드 후킹이 준 vkCode를 <see cref="SpecialKey"/>(표시 심볼) 또는
/// 입력 문자로 옮긴다. 게이트(Ctrl/Alt/Win 필수)는 Core <c>KeyFormat.Format</c>이 처리하므로
/// 여기선 매핑만 — 후킹은 모든 키를 넘긴다.
///
/// 이 클래스는 Win32 P/Invoke에 의존하지 않는 순수 로직 → 단위 테스트 대상.
/// 문자 매핑은 US(QWERTY) 레이아웃의 unshifted 기준. 데드키/IME 부작용을 피하려 v1은
/// 정적 테이블만 쓰고 ToUnicodeEx는 쓰지 않는다(키스트로크 표시는 모디파이어 게이트라 충분).
/// </summary>
internal static class VirtualKeys
{
    // ── 자주 쓰는 VK 상수 (winuser.h) ────────────────────────────
    public const uint VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_SPACE = 0x20;
    public const uint VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24;
    public const uint VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_DELETE = 0x2E;
    public const uint VK_F1 = 0x70; // F1..F12 = 0x70..0x7B (연속)

    // 모디파이어 (generic + L/R 구분). LL 후킹은 L/R 구분 코드를 준다.
    public const uint VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    public const uint VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    public const uint VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;
    public const uint VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    public const uint VK_LMENU = 0xA4, VK_RMENU = 0xA5;

    // OEM (US 레이아웃)
    public const uint VK_OEM_1 = 0xBA;      // ;:
    public const uint VK_OEM_PLUS = 0xBB;   // =+
    public const uint VK_OEM_COMMA = 0xBC;  // ,<
    public const uint VK_OEM_MINUS = 0xBD;  // -_
    public const uint VK_OEM_PERIOD = 0xBE; // .>
    public const uint VK_OEM_2 = 0xBF;      // /?
    public const uint VK_OEM_3 = 0xC0;      // `~
    public const uint VK_OEM_4 = 0xDB;      // [{
    public const uint VK_OEM_5 = 0xDC;      // \|
    public const uint VK_OEM_6 = 0xDD;      // ]}
    public const uint VK_OEM_7 = 0xDE;      // '"

    /// <summary>표시 심볼이 있는 특수 키면 <see cref="SpecialKey"/>, 아니면 null.</summary>
    public static SpecialKey? MapSpecial(uint vk)
    {
        // F1..F12 (연속 범위)
        if (vk >= VK_F1 && vk <= VK_F1 + 11)
            return (SpecialKey)((int)SpecialKey.F1 + (int)(vk - VK_F1));

        return vk switch
        {
            VK_RETURN => SpecialKey.Return,
            VK_TAB => SpecialKey.Tab,
            VK_SPACE => SpecialKey.Space,
            VK_BACK => SpecialKey.Backspace,
            VK_ESCAPE => SpecialKey.Escape,
            VK_DELETE => SpecialKey.ForwardDelete,
            VK_LEFT => SpecialKey.ArrowLeft,
            VK_RIGHT => SpecialKey.ArrowRight,
            VK_UP => SpecialKey.ArrowUp,
            VK_DOWN => SpecialKey.ArrowDown,
            VK_HOME => SpecialKey.Home,
            VK_END => SpecialKey.End,
            VK_PRIOR => SpecialKey.PageUp,
            VK_NEXT => SpecialKey.PageDown,
            _ => null,
        };
    }

    /// <summary>
    /// 인쇄 가능 키면 입력 문자(US unshifted, 소문자), 아니면 null.
    /// 소문자/베이스 문자를 돌려준다 — 표시 대문자화는 <c>KeyFormat</c>이, 그리기 단축키(z/[/])
    /// 비교는 그대로 매칭된다.
    /// </summary>
    public static string? MapChar(uint vk)
    {
        if (vk >= 'A' && vk <= 'Z')            // VK 글자코드 = ASCII 대문자
            return ((char)('a' + (vk - 'A'))).ToString();
        if (vk >= '0' && vk <= '9')            // 상단 숫자열
            return ((char)vk).ToString();
        if (vk >= 0x60 && vk <= 0x69)          // VK_NUMPAD0..9
            return ((char)('0' + (vk - 0x60))).ToString();

        return vk switch
        {
            VK_OEM_1 => ";",
            VK_OEM_PLUS => "=",
            VK_OEM_COMMA => ",",
            VK_OEM_MINUS => "-",
            VK_OEM_PERIOD => ".",
            VK_OEM_2 => "/",
            VK_OEM_3 => "`",
            VK_OEM_4 => "[",
            VK_OEM_5 => "\\",
            VK_OEM_6 => "]",
            VK_OEM_7 => "'",
            _ => null,
        };
    }

    /// <summary>모디파이어 VK인지(generic 또는 L/R).</summary>
    public static bool IsModifier(uint vk) => vk switch
    {
        VK_SHIFT or VK_CONTROL or VK_MENU
            or VK_LSHIFT or VK_RSHIFT or VK_LCONTROL or VK_RCONTROL
            or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN => true,
        _ => false,
    };
}
