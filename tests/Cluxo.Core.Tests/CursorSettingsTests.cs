using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// CursorSettings + 값 enum + RadialLabel — Swift CursorSettings 이식 검증.
public class CursorSettingsTests
{
    // ── 값 enum 매핑 ─────────────────────────────────────────────

    [Fact]
    public void RingSize_Diameter()
    {
        Assert.Equal(36, RingSize.Small.Diameter());
        Assert.Equal(54, RingSize.Medium.Diameter());
        Assert.Equal(96, RingSize.XLarge.Diameter());
    }

    [Fact]
    public void AnimationSpeed_Multiplier()
    {
        Assert.Equal(1.7, AnimationSpeed.Slow.Multiplier());
        Assert.Equal(1.0, AnimationSpeed.Normal.Multiplier());
        Assert.Equal(0.5, AnimationSpeed.Fast.Multiplier());
    }

    [Fact]
    public void ShakeSensitivity_RequiredDirChanges_FeedsShakeState()
    {
        Assert.Equal(3, ShakeSensitivity.Sensitive.RequiredDirChanges());
        Assert.Equal(5, ShakeSensitivity.Normal.RequiredDirChanges());
        Assert.Equal(8, ShakeSensitivity.Insensitive.RequiredDirChanges());
        // 실제 주입 — 민감(3)으로 ShakeState 설정 시 전환 3회에 감지
        var shake = new ShakeState { RequiredDirChanges = ShakeSensitivity.Sensitive.RequiredDirChanges() };
        double dt = 0.05, t = 0;
        double[] xs = { 0, 100, 0, 100, 0 }; // 전환 3회
        bool detected = false;
        foreach (var x in xs) { detected = shake.Record(x, 0, t) || detected; t += dt; }
        Assert.True(detected);
    }

    [Fact]
    public void BorderWeight_LineWidth()
    {
        Assert.Equal(1.5, BorderWeight.Thin.LineWidth());
        Assert.Equal(5.5, BorderWeight.Bold.LineWidth());
    }

    [Fact]
    public void PreferredLanguage_LanguageCode()
    {
        Assert.Null(PreferredLanguage.System.LanguageCode());
        Assert.Equal("ko", PreferredLanguage.Ko.LanguageCode());
        Assert.Equal("en", PreferredLanguage.En.LanguageCode());
    }

    [Fact]
    public void RingColor_NeedsDarkText()
    {
        Assert.True(RingColor.White.NeedsDarkText());   // 밝음 → 어두운 텍스트
        Assert.True(RingColor.Yellow.NeedsDarkText());
        Assert.False(RingColor.Red.NeedsDarkText());    // 어두움 → 흰 텍스트
        Assert.True(RingColor.Cyan.NeedsDarkText());
    }

    // ── 설정 모델 ────────────────────────────────────────────────

    [Fact]
    public void Defaults_MatchSwift()
    {
        var s = new CursorSettings(new JsonSettingsStore());
        Assert.Equal(RingColor.Cyan, s.RingColor);
        Assert.Equal(RingShape.Circle, s.RingShape);
        Assert.Equal(RingSize.Medium, s.RingSize);
        Assert.Equal(1.0, s.RingOpacity);
        Assert.Equal(AnimationSpeed.Normal, s.AnimationSpeed);
        Assert.Equal(3.0, s.KeystrokeTimeout);
        Assert.True(s.IsShakeEnabled);       // default on
        Assert.True(s.IsIdlePulseEnabled);   // default on
        Assert.False(s.IsGlowEnabled);       // minimalist default off
        Assert.False(s.IsKeystrokeEnabled);
        Assert.Equal(130.0, s.SpotlightRadius);
    }

    [Fact]
    public void WriteThenReadAcrossInstances()
    {
        var store = new JsonSettingsStore();
        var s = new CursorSettings(store);
        s.RingColor = RingColor.Red;
        s.RingOpacity = 0.6;
        s.IsGlowEnabled = true;
        s.ShakeSensitivity = ShakeSensitivity.Insensitive;

        var reloaded = new CursorSettings(JsonSettingsStore.Load(store.Serialize()));
        Assert.Equal(RingColor.Red, reloaded.RingColor);
        Assert.Equal(0.6, reloaded.RingOpacity);
        Assert.True(reloaded.IsGlowEnabled);
        Assert.Equal(ShakeSensitivity.Insensitive, reloaded.ShakeSensitivity);
    }

    [Fact]
    public void EffectiveRingColor_PresetVsCustom()
    {
        var s = new CursorSettings(new JsonSettingsStore());
        Assert.Equal(RingColor.Cyan.Color(), s.EffectiveRingColor); // default Cyan
        s.RingColor = RingColor.Custom;
        s.CustomRingColor = new Rgba(10, 20, 30);
        Assert.Equal(new Rgba(10, 20, 30), s.EffectiveRingColor);   // Custom → customRingColor
    }

    [Fact]
    public void Changed_FiresOnSet()
    {
        var s = new CursorSettings(new JsonSettingsStore());
        int count = 0;
        s.Changed += () => count++;
        s.RingColor = RingColor.Blue;
        s.RingOpacity = 0.8;
        Assert.Equal(2, count);
    }

    [Fact]
    public void TrustedMonitor_AddRemove()
    {
        var s = new CursorSettings(new JsonSettingsStore());
        Assert.False(s.IsTrustedMonitor("M1"));
        s.SetTrusted("M1", true);
        Assert.True(s.IsTrustedMonitor("M1"));
        s.SetTrusted("M1", true); // 중복 추가 무해
        Assert.Single(s.TrustedMonitorUUIDs);
        s.SetTrusted("M1", false);
        Assert.False(s.IsTrustedMonitor("M1"));
    }

    [Fact]
    public void ScreenshotMode_IsTransient_NotPersisted()
    {
        var store = new JsonSettingsStore();
        var s = new CursorSettings(store) { IsScreenshotMode = true };
        Assert.True(s.IsScreenshotMode);
        // store에 안 들어가야(재시작 시 false)
        Assert.False(store.Contains("isScreenshotMode"));
        Assert.False(new CursorSettings(JsonSettingsStore.Load(store.Serialize())).IsScreenshotMode);
    }

    // ── RadialLabel (앞서 미룬 contentSpan 테스트 포함) ──────────

    [Fact]
    public void EstLabelWidth_CjkWiderThanAscii()
    {
        Assert.True(RadialLabel.EstLabelWidth("매우 크게") > RadialLabel.EstLabelWidth("2x"));
        Assert.Equal(7.0 * 2, RadialLabel.EstLabelWidth("2x"));   // ascii 7pt
        Assert.Equal(12.0 * 2 + 7.0, RadialLabel.EstLabelWidth("크게 ")); // 한글2 + space(ascii)
    }

    [Fact]
    public void ContentSpan_WiderForLongerLabels()
    {
        double r = 130;
        double shortSpan = RadialLabel.ContentSpan(new[] { "1×", "2×" }, r);
        double longSpan = RadialLabel.ContentSpan(new[] { "매우 크게", "보통" }, r);
        Assert.True(longSpan > shortSpan, "긴 라벨이면 같은 개수라도 더 넓은 span");
        Assert.InRange(shortSpan, 50, 150);
        Assert.InRange(longSpan, 50, 150);
    }

    [Fact]
    public void ContentSpan_Clamps()
    {
        Assert.Equal(50, RadialLabel.ContentSpan(System.Array.Empty<string>(), 130)); // 빈 → 50
        Assert.Equal(50, RadialLabel.ContentSpan(new[] { "a" }, 0));                  // radius<=1 → 50
    }
}
