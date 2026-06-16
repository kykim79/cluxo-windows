using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// 링 스케일 스프링(설계 §5.2) — squash 후 1.0 springy 복귀. 순수 적분 로직.
public class RingSpringTests
{
    private static RingSpring Make() => new(Tokens.Motion.ReturnTo); // Spring(0.5, 0.45) — underdamped

    [Fact]
    public void Initial_IsRest()
        => Assert.Equal(1.0, Make().Value);

    [Fact]
    public void Bump_SquashesDown()
    {
        var s = Make();
        s.Bump();
        Assert.Equal(0.75, s.Value);
    }

    [Fact]
    public void Advance_ConvergesBackToRest()
    {
        var s = Make();
        s.Bump();
        for (int i = 0; i < 180; i++) s.Advance(1.0 / 60); // 3초
        Assert.True(Math.Abs(s.Value - 1.0) < 0.01, $"settled at {s.Value}");
    }

    [Fact]
    public void Underdamped_OvershootsRest()
    {
        var s = Make();
        s.Bump();
        double max = s.Value;
        for (int i = 0; i < 120; i++) { s.Advance(1.0 / 60); max = Math.Max(max, s.Value); }
        Assert.True(max > 1.0, $"overshoot past rest expected, max={max}"); // dampingFraction<1 → 오버슈트
    }

    [Fact]
    public void Advance_LargeDt_StaysStable()
    {
        var s = Make();
        s.Bump();
        s.Advance(0.5); // 큰 스텝 1회 — 분할 적분으로 발산 안 함
        Assert.InRange(s.Value, 0.5, 1.5);
    }

    [Fact]
    public void NoBump_StaysAtRest()
    {
        var s = Make();
        for (int i = 0; i < 60; i++) s.Advance(1.0 / 60);
        Assert.Equal(1.0, s.Value);
    }
}
