namespace Cluxo.Core;

/// <summary>
/// 드래그 각도 라벨의 순수 변환 함수. (Swift <c>DragAngleLabel</c> static funcs 이식)
/// atan2 라디안 → 시계방향 12시=0° 기준 0~359° 정수, 그리고 8방향 화살표.
/// </summary>
public static class DragAngleLabel
{
    /// <summary>
    /// atan2(dy, dx) 결과(라디안)를 시계방향 12시=0° 기준 0~359° 정수로 변환.
    /// CGEvent/화면 y축이 top-left이라 dy 양수=아래. atan2 표준(-π~+π)에 +90° 회전 후 mod 360.
    /// 예: dx=0,dy=-1(위) → atan2=-π/2 → -90°+90° = 0°. dx=1,dy=0(오른쪽) → 0+90 = 90°.
    /// </summary>
    public static int ClockwiseDegrees(double angleRadians)
    {
        double raw = angleRadians * 180.0 / Math.PI;
        double cw = raw + 90;
        // Swift Double.rounded()는 away-from-zero — 반드시 맞춰야 경계값 일치
        int rounded = (int)Math.Round(cw, MidpointRounding.AwayFromZero);
        return ((rounded % 360) + 360) % 360;
    }

    /// <summary>CW degrees → 8방향 화살표. 각 방향 ±22.5° 범위.</summary>
    public static string DirectionArrow(int degrees) => degrees switch
    {
        (>= 338 and <= 360) or (>= 0 and < 23) => "↑",
        >= 23 and < 68 => "↗",
        >= 68 and < 113 => "→",
        >= 113 and < 158 => "↘",
        >= 158 and < 203 => "↓",
        >= 203 and < 248 => "↙",
        >= 248 and < 293 => "←",
        >= 293 and < 338 => "↖",
        _ => "•", // 0~360 밖(음수·361+) 방어값
    };
}

/// <summary>
/// 드래그 회전 각도 누적기. (Swift <c>CursorRuntimeState.updateDragAngle/endDrag</c> 순수 부분 이식)
///
/// 핵심: atan2의 ±π 불연속을 정규화해 항상 최단 방향으로 누적 →
/// 한 바퀴 이상 돌면 2π 이상 값을 유지(0으로 회귀하지 않음).
/// </summary>
public sealed class DragAngleAccumulator
{
    /// <summary>누적 각도(라디안). 한 바퀴 = 2π.</summary>
    public double Angle { get; private set; }

    /// <summary>새 raw 각도(atan2 결과, -π~π)를 받아 최단 경로 차이를 누적.</summary>
    public void Update(double newAngle)
    {
        // 이전 각도의 wrapped 값과 비교해 차이를 (-π, π]로 정규화한 뒤 누적
        double lastWrapped = Math.Atan2(Math.Sin(Angle), Math.Cos(Angle));
        double diff = newAngle - lastWrapped;
        if (diff > Math.PI) diff -= 2 * Math.PI;
        if (diff < -Math.PI) diff += 2 * Math.PI;
        Angle += diff;
    }

    /// <summary>드래그 종료 — 다음 드래그를 위해 0으로 리셋.</summary>
    public void EndDrag() => Angle = 0;
}
