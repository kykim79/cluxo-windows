using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// DragAngleLabel.ClockwiseDegrees / DirectionArrow — 순수 변환 검증.
// (Swift DragAngleLabelTests 이식) atan2 라디안 → 시계방향 0~359° 정수, 8방향 화살표.
public class DragAngleLabelTests
{
    [Fact]
    public void ClockwiseDegrees_Up()
        => Assert.Equal(0, DragAngleLabel.ClockwiseDegrees(Math.Atan2(-1.0, 0.0)));

    [Fact]
    public void ClockwiseDegrees_Right()
        => Assert.Equal(90, DragAngleLabel.ClockwiseDegrees(0));

    [Fact]
    public void ClockwiseDegrees_Down()
        => Assert.Equal(180, DragAngleLabel.ClockwiseDegrees(Math.Atan2(1.0, 0.0)));

    [Fact]
    public void ClockwiseDegrees_Left()
        => Assert.Equal(270, DragAngleLabel.ClockwiseDegrees(Math.Atan2(0.0, -1.0)));

    [Fact]
    public void ClockwiseDegrees_UpperRight()
        => Assert.Equal(45, DragAngleLabel.ClockwiseDegrees(Math.Atan2(-1.0, 1.0)));

    [Fact]
    public void ClockwiseDegrees_LowerLeft()
        => Assert.Equal(225, DragAngleLabel.ClockwiseDegrees(Math.Atan2(1.0, -1.0)));

    [Fact]
    public void ClockwiseDegrees_AlwaysInRange_0to359()
    {
        // 결정적 시드로 임의 라디안 → 결과 모두 [0, 360)
        var rng = new Random(12345);
        for (int i = 0; i < 100; i++)
        {
            double radians = rng.NextDouble() * 20.0 - 10.0; // -10..10
            int result = DragAngleLabel.ClockwiseDegrees(radians);
            Assert.InRange(result, 0, 359);
        }
    }

    [Theory]
    [InlineData(0, "↑")]
    [InlineData(22, "↑")]
    [InlineData(338, "↑")]
    [InlineData(359, "↑")]
    [InlineData(23, "↗")]
    [InlineData(45, "↗")]
    [InlineData(67, "↗")]
    [InlineData(90, "→")]
    [InlineData(112, "→")]
    [InlineData(135, "↘")]
    [InlineData(180, "↓")]
    [InlineData(225, "↙")]
    [InlineData(270, "←")]
    [InlineData(315, "↖")]
    public void DirectionArrow_Maps(int degrees, string expected)
        => Assert.Equal(expected, DragAngleLabel.DirectionArrow(degrees));

    [Fact]
    public void DirectionArrow_AllEightDirections()
    {
        Assert.Equal("↑", DragAngleLabel.DirectionArrow(0));
        Assert.Equal("↗", DragAngleLabel.DirectionArrow(45));
        Assert.Equal("→", DragAngleLabel.DirectionArrow(90));
        Assert.Equal("↘", DragAngleLabel.DirectionArrow(135));
        Assert.Equal("↓", DragAngleLabel.DirectionArrow(180));
        Assert.Equal("↙", DragAngleLabel.DirectionArrow(225));
        Assert.Equal("←", DragAngleLabel.DirectionArrow(270));
        Assert.Equal("↖", DragAngleLabel.DirectionArrow(315));
    }
}

// DragAngleAccumulator — ±π wrapping + 누적 검증. (Swift DragAngleTests 이식)
public class DragAngleAccumulatorTests
{
    [Fact]
    public void InitialAngleIsZero()
        => Assert.Equal(0, new DragAngleAccumulator().Angle);

    [Fact]
    public void SmallIncrement()
    {
        var a = new DragAngleAccumulator();
        a.Update(0.5);
        Assert.Equal(0.5, a.Angle, 9);
    }

    // 가장 중요한 케이스 — atan2 불연속 (+π → -π)
    [Fact]
    public void WrapAcrossPositivePiToNegativePi()
    {
        var a = new DragAngleAccumulator();
        a.Update(Math.PI - 0.1);   // +π 근처에서 시작
        a.Update(-Math.PI + 0.1);  // -π 근처 (시각적으로 +0.2)
        Assert.Equal(Math.PI + 0.1, a.Angle, 9);
    }

    [Fact]
    public void WrapAcrossNegativePiToPositivePi()
    {
        var a = new DragAngleAccumulator();
        a.Update(-Math.PI + 0.1);
        a.Update(Math.PI - 0.1);
        Assert.Equal(-Math.PI - 0.1, a.Angle, 9);
    }

    [Fact]
    public void AccumulatesAcrossMultipleRotations()
    {
        var a = new DragAngleAccumulator();
        // 한 바퀴를 atan2가 반환하는 형식대로 8단계로 회전
        var steps = new double[9];
        for (int i = 0; i <= 8; i++)
        {
            double theta = i * Math.PI / 4;
            steps[i] = Math.Atan2(Math.Sin(theta), Math.Cos(theta));
        }
        for (int i = 1; i <= 8; i++) a.Update(steps[i]);
        Assert.Equal(2 * Math.PI, a.Angle, 9);
    }

    [Fact]
    public void EndDragResets()
    {
        var a = new DragAngleAccumulator();
        a.Update(Math.PI - 0.1);
        a.EndDrag();
        Assert.Equal(0, a.Angle);
    }
}
