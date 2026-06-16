namespace Cluxo.Core;

/// <summary>
/// 디자인 토큰 — DESIGN.md 단일 소스에서 수동 미러링. (Swift <c>DesignTokens.Tokens</c> 대응)
/// 토큰 외 값을 코드에 하드코딩하지 말 것. 새 값은 DESIGN.md에 먼저 추가 후 여기 미러.
/// (현재는 이식된 모듈이 쓰는 토큰만. 나머지는 모듈 이식 시 추가.)
/// </summary>
public static class Tokens
{
    /// <summary>⌃⌥D 그리기 도형 토큰.</summary>
    public static class Drawing
    {
        /// <summary>기본 stroke 두께 — 발표 가시성 + 과하지 않은 균형. [ ] 키 baseline.</summary>
        public const double LineWidth = 4;

        /// <summary>두께 조절 단계 — [ ] 키로 순환. 5단계.</summary>
        public static readonly double[] LineWidthSteps = { 2, 4, 6, 10, 14 };
    }
}
