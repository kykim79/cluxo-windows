using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// KeyFormat.Format — 단축키 포맷팅 검증. (Swift KeyFormatTests의 불변식 이식, Windows 표기로)
// 핵심: Ctrl/Alt/Win 중 하나라도 있어야 표시(단순 타이핑·패스워드 노출 방지). 순서 Ctrl,Alt,Shift,Win.
public class KeyFormatTests
{
    // MARK: 모디파이어 게이트 (Ctrl/Alt/Win 없으면 표시 X)

    [Fact]
    public void NoModifiersIsEmpty()
        => Assert.Equal("", KeyFormat.Format(KeyModifiers.None, characters: "k"));

    [Fact]
    public void ShiftOnlyIsEmpty()
        => Assert.Equal("", KeyFormat.Format(KeyModifiers.Shift, characters: "K")); // 대문자도 노출 위험

    // MARK: 일반 키 + 모디파이어

    [Fact]
    public void ControlOnly()
        => Assert.Equal("Ctrl+K", KeyFormat.Format(KeyModifiers.Control, characters: "k"));

    [Fact]
    public void ControlAlt()
        => Assert.Equal("Ctrl+Alt+K",
            KeyFormat.Format(KeyModifiers.Control | KeyModifiers.Alt, characters: "k"));

    [Fact]
    public void ShiftWin() // ⌘⇧K 대응 — 순서: Shift 먼저, Win 나중
        => Assert.Equal("Shift+Win+K",
            KeyFormat.Format(KeyModifiers.Shift | KeyModifiers.Win, characters: "k"));

    [Fact]
    public void AllModifiers()
        => Assert.Equal("Ctrl+Alt+Shift+Win+K",
            KeyFormat.Format(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Win,
                characters: "k"));

    // MARK: 특수 키 (특수키 심볼 우선)

    [Fact]
    public void SpecialReturn()
        => Assert.Equal("Ctrl+Enter", KeyFormat.Format(KeyModifiers.Control, SpecialKey.Return));

    [Fact]
    public void SpecialEscape()
        => Assert.Equal("Alt+Esc", KeyFormat.Format(KeyModifiers.Alt, SpecialKey.Escape));

    [Fact]
    public void SpecialArrowLeft()
        => Assert.Equal("Ctrl+Alt+←",
            KeyFormat.Format(KeyModifiers.Control | KeyModifiers.Alt, SpecialKey.ArrowLeft));

    [Fact]
    public void SpecialSpace()
        => Assert.Equal("Win+Space", KeyFormat.Format(KeyModifiers.Win, SpecialKey.Space));

    [Fact]
    public void SpecialF1()
        => Assert.Equal("Win+F1", KeyFormat.Format(KeyModifiers.Win, SpecialKey.F1));

    // MARK: 엣지 — 특수키가 문자보다 우선, 빈 키면 빈 문자열

    [Fact]
    public void SpecialTakesPrecedenceOverCharacters()
        => Assert.Equal("Ctrl+Enter",
            KeyFormat.Format(KeyModifiers.Control, SpecialKey.Return, characters: "x"));

    [Fact]
    public void NoKeyIsEmptyEvenWithModifiers()
        => Assert.Equal("", KeyFormat.Format(KeyModifiers.Control, special: null, characters: ""));
}
