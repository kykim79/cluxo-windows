namespace Cluxo.Core;

/// <summary>
/// 라디얼 메뉴 라벨 fan 각도 계산 — 항목 개수가 아니라 **라벨 내용 폭**에 맞춘다.
/// (Swift <c>CursorSettings.RadialMenuItem.contentSpan/estLabelWidth</c> 이식)
/// 렌더와 hittest가 같은 함수를 쓰도록 단일 source.
/// </summary>
public static class RadialLabel
{
    /// <summary>
    /// 라벨 폭(추정)×개수를 반경에서 각도로 환산한 fan span. 각 항목이 자기 라벨을 담을 호 길이를
    /// 갖도록 maxWidth 기준 균등 분할. 50°~150° clamp.
    /// </summary>
    public static double ContentSpan(IReadOnlyList<string> labels, double radius)
    {
        int n = labels.Count;
        if (n == 0 || radius <= 1) return 50;
        double maxW = 20;
        foreach (var s in labels) maxW = Math.Max(maxW, EstLabelWidth(s));
        double perItemDeg = (maxW + 14) / radius * 180 / Math.PI; // 호 길이(라벨폭+gap) → 각도
        return Math.Min(150, Math.Max(50, perItemDeg * n));
    }

    /// <summary>
    /// 라벨 폭 추정(pt) — CJK(한글 등)는 wide(~12pt), 그 외(숫자·영문)는 narrow(~7pt).
    /// </summary>
    public static double EstLabelWidth(string s)
    {
        double acc = 0;
        foreach (char ch in s)
        {
            int v = ch;
            bool wide = (v >= 0xAC00 && v <= 0xD7A3)   // 한글 음절
                     || (v >= 0x3000 && v <= 0x9FFF)   // CJK
                     || (v >= 0xFF00 && v <= 0xFFEF);  // 전각
            acc += wide ? 12.0 : 7.0;
        }
        return acc;
    }
}
