using Cluxo.Core;
using Cluxo.Windows.App.Input;

namespace Cluxo.Windows.App.Tests;

// VK → SpecialKey / 입력 문자 매핑 (INPUT-LAYER.md §3). 순수 로직.
public class VirtualKeysTests
{
    [Theory]
    [InlineData(0x0D, SpecialKey.Return)]
    [InlineData(0x09, SpecialKey.Tab)]
    [InlineData(0x20, SpecialKey.Space)]
    [InlineData(0x08, SpecialKey.Backspace)]
    [InlineData(0x1B, SpecialKey.Escape)]
    [InlineData(0x2E, SpecialKey.ForwardDelete)]
    [InlineData(0x25, SpecialKey.ArrowLeft)]
    [InlineData(0x27, SpecialKey.ArrowRight)]
    [InlineData(0x26, SpecialKey.ArrowUp)]
    [InlineData(0x28, SpecialKey.ArrowDown)]
    [InlineData(0x24, SpecialKey.Home)]
    [InlineData(0x23, SpecialKey.End)]
    [InlineData(0x21, SpecialKey.PageUp)]
    [InlineData(0x22, SpecialKey.PageDown)]
    public void MapSpecial_KnownKeys(uint vk, SpecialKey expected)
        => Assert.Equal(expected, VirtualKeys.MapSpecial(vk));

    [Theory]
    [InlineData(0x70, SpecialKey.F1)]
    [InlineData(0x77, SpecialKey.F8)]
    [InlineData(0x7B, SpecialKey.F12)]
    public void MapSpecial_FunctionKeys_ContiguousRange(uint vk, SpecialKey expected)
        => Assert.Equal(expected, VirtualKeys.MapSpecial(vk));

    [Fact]
    public void MapSpecial_PastF12_IsNotSpecial()
        => Assert.Null(VirtualKeys.MapSpecial(0x7C)); // F13 — Core SpecialKey에 없음

    [Theory]
    [InlineData(0x41, "a")] // 'A' VK → 소문자 (KeyFormat이 표시용 대문자화)
    [InlineData(0x5A, "z")] // 'Z' — 그리기 undo 비교(OrdinalIgnoreCase "z")와 매칭
    [InlineData(0x30, "0")]
    [InlineData(0x39, "9")]
    [InlineData(0x60, "0")] // NUMPAD0
    [InlineData(0x69, "9")] // NUMPAD9
    public void MapChar_LettersDigits(uint vk, string expected)
        => Assert.Equal(expected, VirtualKeys.MapChar(vk));

    [Theory]
    [InlineData(0xDB, "[")] // VK_OEM_4 — 그리기 두께 감소 비교 "["
    [InlineData(0xDD, "]")] // VK_OEM_6 — 두께 증가 "]"
    [InlineData(0xBC, ",")] // VK_OEM_COMMA
    [InlineData(0xBE, ".")]
    [InlineData(0xBF, "/")]
    public void MapChar_OemKeys_UsLayout(uint vk, string expected)
        => Assert.Equal(expected, VirtualKeys.MapChar(vk));

    [Fact]
    public void MapChar_SpecialKey_ReturnsNull()
        => Assert.Null(VirtualKeys.MapChar(0x0D)); // Return은 문자 아님 → special로 처리

    [Theory]
    [InlineData(0xA2, true)]  // VK_LCONTROL
    [InlineData(0xA5, true)]  // VK_RMENU(Alt)
    [InlineData(0x5B, true)]  // VK_LWIN
    [InlineData(0x41, false)] // 'A'
    public void IsModifier(uint vk, bool expected)
        => Assert.Equal(expected, VirtualKeys.IsModifier(vk));
}
