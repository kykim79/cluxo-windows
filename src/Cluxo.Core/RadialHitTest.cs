namespace Cluxo.Core;

/// <summary>
/// 라디얼 메뉴에서 cursor의 (중심 기준) 상대 위치 → 어떤 sector/sub/subSub가 선택됐는지 분류.
/// (Swift <c>RadialHitTest</c> 이식) 순수 함수 — 트리 모양(subCount/branch/subSubCount)을 델리게이트로 주입.
///
/// 거리 구간 (안→밖): dead(cancel) / main(sector 자유) / sub(sector LOCK + sub fan) /
/// subSub(branch sub LOCK + 자식 fan; leaf면 닫기) / 바깥(닫기).
/// </summary>
public static class RadialHitTest
{
    /// <summary>선택 결과. 각 레벨은 미선택 시 null.</summary>
    public readonly record struct Hit(int? Sector, int? Sub, int? SubSub);

    /// <summary>거리 링(안→밖).</summary>
    public readonly record struct Rings(double Dead, double Main, double Sub, double SubSub);

    /// <summary>12시=0, 시계방향으로 증가하는 각도(0~360).</summary>
    public static double ClockwiseFromTop(double dx, double dy)
    {
        double atan2Deg = Math.Atan2(dy, dx) * 180 / Math.PI;
        return (90 - atan2Deg + 720) % 360;
    }

    /// <summary>중심각(centerDeg) 기준 span을 count칸 분할했을 때 cw가 속한 인덱스(0..count-1).</summary>
    public static int FanIndex(double cw, double centerDeg, double span, int count)
    {
        if (count <= 0) return 0;
        double step = span / count;
        double diff = cw - centerDeg;
        if (diff > 180) diff -= 360;
        if (diff < -180) diff += 360;
        double rel = diff + span / 2;                       // 0~span 정규화
        double clamped = Math.Max(0, Math.Min(span - 0.001, rel));
        return (int)(clamped / step);
    }

    /// <summary>sub fan 내 sub 인덱스의 중심 각도 — subSub fan은 이 각도를 중심으로 펼쳐진다.</summary>
    public static double SubCenterAngle(int sector, int sub, double subSpan, int subCount)
    {
        double mainAngle = sector * 45.0;
        double step = subSpan / Math.Max(1, subCount);
        double start = mainAngle - subSpan / 2;
        return start + step * (sub + 0.5);
    }

    /// <summary>
    /// cursor 상대좌표(dx,dy)와 현재 lock 상태로부터 선택 결과를 계산.
    /// lockedSector/lockedSub: sub·subSub 영역에서 sector/sub를 고정하기 위한 현재 선택값.
    /// </summary>
    public static Hit Classify(
        double dx, double dy,
        int? lockedSector,
        int? lockedSub,
        Rings rings,
        Func<int, int> subCountOf,
        Func<int, double> subSpanOf,            // sector → sub fan 각도
        Func<int, int, bool> isBranch,
        Func<int, int, int> subSubCountOf,
        Func<int, int, double> subSubSpanOf)    // (sector,sub) → 자식 fan 각도
    {
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < rings.Dead) return new Hit(null, null, null);     // cancel
        if (dist > rings.SubSub) return new Hit(null, null, null);   // 바깥 너머 → 닫기(✕)

        double cw = ClockwiseFromTop(dx, dy);

        // 메인 영역 — sector 자유
        if (dist < rings.Main)
            return new Hit((int)((cw + 22.5) / 45) % 8, null, null);

        // [main, subSub] → sector LOCK (이미 선택돼 있으면 유지, 첫 진입이면 angle)
        int sector = lockedSector ?? ((int)((cw + 22.5) / 45) % 8);
        int subCount = subCountOf(sector);
        if (subCount <= 0) return new Hit(sector, null, null);

        double subSpan = subSpanOf(sector);
        int sub = FanIndex(cw, sector * 45.0, subSpan, subCount);

        // 2번째 ring — sub 값
        if (dist < rings.Sub) return new Hit(sector, sub, null);

        // 3번째 ring — branch sub LOCK (옆 branch로 새지 않게)
        int lockSub = lockedSub ?? sub;
        if (!isBranch(sector, lockSub))
            return new Hit(null, null, null); // leaf — 확장 자식 없음, 바깥으로 벗어나면 닫기

        int kidCount = subSubCountOf(sector, lockSub);
        if (kidCount <= 0) return new Hit(sector, lockSub, null);

        double center = SubCenterAngle(sector, lockSub, subSpan, subCount);
        int subSub = FanIndex(cw, center, subSubSpanOf(sector, lockSub), kidCount);
        return new Hit(sector, lockSub, subSub);
    }
}
