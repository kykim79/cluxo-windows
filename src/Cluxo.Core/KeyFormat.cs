namespace Cluxo.Core;

/// <summary>키스트로크 표시용 모디파이어 플래그 (Windows 네이티브 집합).</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1 << 0,
    Alt = 1 << 1,
    Shift = 1 << 2,
    Win = 1 << 3,
}

/// <summary>
/// 표시 심볼이 있는 특수 키. 플랫폼 계층이 네이티브 VK 코드를 이 값으로 매핑한다
/// (macOS 가상 키코드 같은 플랫폼 디테일은 Core에 들어오지 않음).
/// </summary>
public enum SpecialKey
{
    Return, Tab, Space, Backspace, Escape, ForwardDelete,
    ArrowLeft, ArrowRight, ArrowDown, ArrowUp, Home, End, PageUp, PageDown,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
}

/// <summary>
/// 키스트로크 오버레이용 단축키 포맷. (Swift <c>KeyboardHotkeyHandler.formatKey</c> 로직 이식)
///
/// 보존된 불변식:
///   - 게이트: ⌃·⌥·⌘(=Ctrl/Alt/Win) 중 하나라도 있어야 표시 (단순 타이핑·패스워드 노출 방지).
///     Shift 단독으로는 표시 안 함 (대문자 K도 노출 위험).
///   - 순서: ⌃⌥⇧⌘ → Ctrl, Alt, Shift, Win.
///   - 키: 특수키 심볼 우선, 없으면 입력 문자 대문자. 빈 키면 전체 빈 문자열.
///
/// 글리프 표기는 Windows 관례를 따른다(Mac의 ⌃⌥⇧⌘·↩⎋ 대신 Ctrl/Alt/Shift/Win·Enter/Esc).
/// 정확한 표기 스타일(텍스트 vs 심볼, 구분자)은 디자인 리뷰에서 확정 — 여기선 로직이 핵심.
/// </summary>
public static class KeyFormat
{
    private const string Separator = "+";

    /// <summary>모디파이어 + (특수키 또는 입력 문자) → 표시 문자열. 표시 안 함이면 "".</summary>
    public static string Format(KeyModifiers modifiers, SpecialKey? special = null, string? characters = null)
    {
        // 게이트 — Ctrl/Alt/Win 없으면 표시 안 함 (Shift는 게이트에서 제외)
        var gate = modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win);
        if (gate == KeyModifiers.None) return "";

        var parts = new List<string>(5);
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");

        string key = special.HasValue ? Symbol(special.Value) : (characters ?? "").ToUpperInvariant();
        if (string.IsNullOrEmpty(key)) return "";

        parts.Add(key);
        return string.Join(Separator, parts);
    }

    private static string Symbol(SpecialKey k) => k switch
    {
        SpecialKey.Return => "Enter",
        SpecialKey.Tab => "Tab",
        SpecialKey.Space => "Space",
        SpecialKey.Backspace => "Backspace",
        SpecialKey.Escape => "Esc",
        SpecialKey.ForwardDelete => "Del",
        SpecialKey.ArrowLeft => "←",
        SpecialKey.ArrowRight => "→",
        SpecialKey.ArrowDown => "↓",
        SpecialKey.ArrowUp => "↑",
        SpecialKey.Home => "Home",
        SpecialKey.End => "End",
        SpecialKey.PageUp => "PgUp",
        SpecialKey.PageDown => "PgDn",
        _ => k.ToString(), // F1..F12 그대로
    };
}
