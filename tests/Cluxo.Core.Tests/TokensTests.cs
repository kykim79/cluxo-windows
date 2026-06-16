using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// Tokens / Rgba 팩토리 / needsDarkText — DESIGN.md 미러 검증.
// SwiftUI 타입(Color.opacity·Animation·Font)을 플랫폼 무관 데이터로 옮긴 변환 정확성 + 값 sanity.
public class TokensTests
{
    // MARK: Rgba 팩토리 (Color.black/white.opacity → straight alpha)

    [Theory]
    [InlineData(1.0, 255)]
    [InlineData(0.0, 0)]
    [InlineData(0.78, 199)] // 0.78*255 = 198.9 → away-from-zero 반올림
    [InlineData(0.30, 77)]  // 0.30*255 = 76.5 → away-from-zero → 77
    public void FromBlack_MapsOpacityToAlpha(double opacity, byte expectedA)
    {
        var c = Rgba.FromBlack(opacity);
        Assert.Equal(new Rgba(0, 0, 0, expectedA), c);
    }

    [Fact]
    public void FromWhite_IsWhiteWithAlpha()
        => Assert.Equal(new Rgba(255, 255, 255, 199), Rgba.FromWhite(0.78));

    [Fact]
    public void FromOpacity_ClampsOutOfRange()
    {
        Assert.Equal((byte)255, Rgba.FromBlack(1.5).A);
        Assert.Equal((byte)0, Rgba.FromBlack(-0.2).A);
    }

    // MARK: needsDarkText (WCAG 휘도 L > 0.6)

    [Fact]
    public void NeedsDarkText_WhiteTrue_BlackFalse()
    {
        Assert.True(new Rgba(255, 255, 255).NeedsDarkText);  // L=1.0
        Assert.False(new Rgba(0, 0, 0).NeedsDarkText);       // L=0
    }

    [Fact]
    public void NeedsDarkText_BrightYellowTrue()
        => Assert.True(new Rgba(255, 255, 0).NeedsDarkText); // L=0.299+0.587=0.886

    [Fact]
    public void NeedsDarkText_DefaultBlueFalse()
        // 기본 accent (0,0x7A,0xFF): L=(0.587*122 + 0.114*255)/255 ≈ 0.395
        => Assert.False(new Rgba(0x00, 0x7A, 0xFF).NeedsDarkText);

    [Fact]
    public void NeedsDarkText_IgnoresAlpha()
        => Assert.True(new Rgba(255, 255, 255, 1).NeedsDarkText); // 알파 무시, RGB만

    // MARK: Motion 파라미터 (Animation 매핑)

    [Fact]
    public void Motion_SpringParams()
    {
        Assert.Equal(new Spring(0.10, 0.40), Tokens.Motion.Snap);
        Assert.Equal(new Spring(0.45, 0.55), Tokens.Motion.DragEnd);
    }

    [Fact]
    public void Motion_EaseParams()
    {
        Assert.Equal(new Ease(EaseCurve.Out, 0.12), Tokens.Motion.EaseMicro);
        Assert.Equal(new Ease(EaseCurve.InOut, 0.35), Tokens.Motion.EaseLong);
    }

    [Fact]
    public void Motion_DurationsMatchEases()
    {
        Assert.Equal(Tokens.Motion.EaseLongDuration, Tokens.Motion.EaseLong.Duration);
        Assert.Equal(Tokens.Motion.EaseMicroDuration, Tokens.Motion.EaseMicro.Duration);
    }

    // MARK: 거리/spacing/radius 값 sanity

    [Fact]
    public void Radial_CanvasSize_Derived()
        => Assert.Equal(236 * 2 + 40, Tokens.Radial.CanvasSize); // subSubOuter*2 + 40 = 512

    [Fact]
    public void Radial_RingsAscending()
    {
        Assert.True(Tokens.Radial.DeadRadius < Tokens.Radial.MainOuter);
        Assert.True(Tokens.Radial.MainOuter < Tokens.Radial.SubOuter);
        Assert.True(Tokens.Radial.SubOuter < Tokens.Radial.SubSubOuter);
    }

    [Fact]
    public void Spacing_BaseUnit4()
    {
        Assert.Equal(4, Tokens.Spacing.Xs);
        Assert.Equal(8, Tokens.Spacing.Sm);
        Assert.Equal(32, Tokens.Spacing.Xxl);
    }

    [Fact]
    public void Drawing_ArrowHeadAngle_30Degrees()
        => Assert.Equal(Math.PI / 6, Tokens.Drawing.ArrowHeadAngle);

    [Fact]
    public void Drawing_DefaultsMatchModule()
    {
        // DrawingState가 쓰는 토큰과 일치 (이중 소스 아님 확인)
        Assert.Equal(4, Tokens.Drawing.LineWidth);
        Assert.Equal(new double[] { 2, 4, 6, 10, 14 }, Tokens.Drawing.LineWidthSteps);
    }

    // MARK: Font 토큰

    [Fact]
    public void Text_FontTokens()
    {
        Assert.Equal(new FontToken(13, FontWeight.Semibold), Tokens.Text.Label);
        Assert.Equal(new FontToken(28), Tokens.Text.Icon); // default Regular, not mono
        Assert.True(Tokens.Text.Mono.Monospaced);
        Assert.Equal(FontWeight.Semibold, Tokens.Text.Mono.Weight);
    }
}
