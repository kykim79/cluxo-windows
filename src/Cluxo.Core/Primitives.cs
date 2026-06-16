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

/// <summary>플랫폼 무관 색 (RGBA 8bit). 렌더 계층이 네이티브 색으로 변환.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Rgba Red = new(255, 0, 0);
}
