using Cluxo.Core;
using Cluxo.Core.Platform;
using Cluxo.Windows.App.Input;

namespace Cluxo.Windows.App.Tests;

// HotkeyChord → RegisterHotKey 인자 (INPUT-LAYER.md §4). 순수 로직.
public class HotkeyChordMapperTests
{
    [Fact]
    public void CtrlAltD_MapsModifiersAndVk()
    {
        var b = HotkeyChordMapper.Map(new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, "D"));
        Assert.Equal('D', b.Vk); // 단일 글자 VK = ASCII 대문자
        Assert.True((b.Modifiers & HotkeyChordMapper.MOD_CONTROL) != 0);
        Assert.True((b.Modifiers & HotkeyChordMapper.MOD_ALT) != 0);
        Assert.Equal(0u, b.Modifiers & HotkeyChordMapper.MOD_SHIFT);
        Assert.Equal(0u, b.Modifiers & HotkeyChordMapper.MOD_WIN);
    }

    [Fact]
    public void AlwaysIncludesNoRepeat()
    {
        // hold 시 WM_HOTKEY 반복 억제 (라디얼은 chord hold라 핫키 아님 — 일반 핫키는 1회만)
        var b = HotkeyChordMapper.Map(new HotkeyChord(KeyModifiers.Control | KeyModifiers.Alt, "I"));
        Assert.True((b.Modifiers & HotkeyChordMapper.MOD_NOREPEAT) != 0);
    }

    [Fact]
    public void LowercaseKey_NormalizesToUpperVk()
        => Assert.Equal('D', HotkeyChordMapper.Map(new HotkeyChord(KeyModifiers.Control, "d")).Vk);

    [Theory]
    [InlineData("Space", 0x20)]
    [InlineData("Comma", 0xBC)]
    [InlineData("Period", 0xBE)]
    [InlineData("Enter", 0x0D)]
    public void NamedKeys(string key, uint expectedVk)
        => Assert.Equal(expectedVk, HotkeyChordMapper.Map(new HotkeyChord(KeyModifiers.Control, key)).Vk);

    [Fact]
    public void AllModifiers_SetAllBits()
    {
        var all = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Win;
        var b = HotkeyChordMapper.Map(new HotkeyChord(all, "A"));
        Assert.True((b.Modifiers & HotkeyChordMapper.MOD_CONTROL) != 0);
        Assert.True((b.Modifiers & HotkeyChordMapper.MOD_ALT) != 0);
        Assert.True((b.Modifiers & HotkeyChordMapper.MOD_SHIFT) != 0);
        Assert.True((b.Modifiers & HotkeyChordMapper.MOD_WIN) != 0);
    }

    [Fact]
    public void UnknownKey_Throws()
        => Assert.Throws<ArgumentException>(
            () => HotkeyChordMapper.Map(new HotkeyChord(KeyModifiers.Control, "F13")));
}
