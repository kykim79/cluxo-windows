namespace Cluxo.Core;

/// <summary>플랫폼 무관 2D 점 (double). Core는 CGPoint/PointF 등 프레임워크 타입을 쓰지 않는다.</summary>
public readonly record struct PointD(double X, double Y)
{
    public static readonly PointD Zero = new(0, 0);
}

/// <summary>플랫폼 무관 사각형. Contains는 CGRect.contains와 동일(min 포함, max 제외).</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public bool Contains(PointD p)
        => p.X >= X && p.X < X + Width && p.Y >= Y && p.Y < Y + Height;
}

/// <summary>플랫폼 무관 색 (RGBA 8bit, straight alpha). 렌더 계층이 네이티브 색으로 변환.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Rgba Red = new(255, 0, 0);

    /// <summary>검정 + opacity (SwiftUI <c>Color.black.opacity(x)</c> 대응).</summary>
    public static Rgba FromBlack(double opacity) => new(0, 0, 0, ToAlpha(opacity));

    /// <summary>흰색 + opacity (SwiftUI <c>Color.white.opacity(x)</c> 대응).</summary>
    public static Rgba FromWhite(double opacity) => new(255, 255, 255, ToAlpha(opacity));

    private static byte ToAlpha(double opacity)
        => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255, MidpointRounding.AwayFromZero);

    /// <summary>
    /// 이 색 위에 검정 텍스트가 더 잘 보이나 (WCAG 휘도 L &gt; 0.6). 알파 무시, RGB만.
    /// L = 0.299R + 0.587G + 0.114B. (Swift <c>Color.needsDarkText</c> 이식)
    /// </summary>
    public bool NeedsDarkText
        => (0.299 * R + 0.587 * G + 0.114 * B) / 255.0 > 0.6;
}
