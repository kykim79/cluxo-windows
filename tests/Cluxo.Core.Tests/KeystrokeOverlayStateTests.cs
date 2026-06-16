using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// KeystrokeOverlayState — 시간 주입 표시/자동 숨김 검증. (Swift 이식, Task.sleep→Tick)
public class KeystrokeOverlayStateTests
{
    [Fact]
    public void ShowKeystroke_SetsTextAndVisible()
    {
        var s = new KeystrokeOverlayState();
        s.ShowKeystroke("Ctrl+C", timeout: 1.0, now: 0);
        Assert.Equal("Ctrl+C", s.KeystrokeText);
        Assert.True(s.IsVisible);
    }

    [Fact]
    public void Tick_BeforeTimeout_StaysVisible()
    {
        var s = new KeystrokeOverlayState();
        s.ShowKeystroke("X", timeout: 1.0, now: 0);
        s.Tick(0.99);
        Assert.True(s.IsVisible);
    }

    [Fact]
    public void Tick_AtTimeout_Hides()
    {
        var s = new KeystrokeOverlayState();
        s.ShowKeystroke("X", timeout: 1.0, now: 0);
        s.Tick(1.0);
        Assert.False(s.IsVisible);
    }

    [Fact]
    public void TextRemains_AfterHide()
    {
        var s = new KeystrokeOverlayState();
        s.ShowKeystroke("X", timeout: 1.0, now: 0);
        s.Tick(1.0);
        Assert.False(s.IsVisible);
        Assert.Equal("X", s.KeystrokeText); // 텍스트는 유지(렌더가 페이드)
    }

    [Fact]
    public void ShowAgain_ResetsTimer()
    {
        var s = new KeystrokeOverlayState();
        s.ShowKeystroke("A", timeout: 1.0, now: 0);
        s.Tick(0.9);
        Assert.True(s.IsVisible);
        s.ShowKeystroke("B", timeout: 1.0, now: 0.9); // 재표시 → hideAt 1.9
        s.Tick(1.5);
        Assert.True(s.IsVisible);   // 이전 타이머였다면 숨었을 시점
        Assert.Equal("B", s.KeystrokeText);
        s.Tick(1.9);
        Assert.False(s.IsVisible);
    }

    [Fact]
    public void ShowStatusNotification_Uses1_5Seconds()
    {
        var s = new KeystrokeOverlayState();
        s.ShowStatusNotification("그리기 모드", now: 0);
        s.Tick(1.49);
        Assert.True(s.IsVisible);
        s.Tick(KeystrokeOverlayState.StatusNotificationTimeout);
        Assert.False(s.IsVisible);
    }
}
