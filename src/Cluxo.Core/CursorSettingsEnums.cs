namespace Cluxo.Core;

// CursorSettings의 값 enum들. (Swift CursorSettings 중첩 enum 이식)
// 라벨은 한국어 source(번역은 localization 계층). 색 팔레트는 Windows용이라 디자인 리뷰에서 tune 가능.

/// <summary>링 강조 색.</summary>
public enum RingColor { Yellow, Red, Blue, Green, Cyan, Purple, White, Custom }

/// <summary>링 모양.</summary>
public enum RingShape { Circle, Squircle, Rhombus, Hexagon }

/// <summary>링 크기.</summary>
public enum RingSize { Small, Medium, Large, XLarge }

/// <summary>애니메이션 속도(배율).</summary>
public enum AnimationSpeed { Slow, Normal, Fast }

/// <summary>흔들기 감지 민감도(방향 전환 횟수).</summary>
public enum ShakeSensitivity { Sensitive, Normal, Insensitive }

/// <summary>링 외곽선 두께.</summary>
public enum BorderWeight { Thin, Normal, Bold, Heavy }

/// <summary>링 외곽선 선 종류.</summary>
public enum BorderStyle { Solid, Dashed }

/// <summary>UI 표시 언어 — System이면 OS 설정 따름.</summary>
public enum PreferredLanguage { System, Ko, En }

/// <summary>값 enum의 순수 값·라벨 매핑. (Swift 각 enum의 computed property 이식)</summary>
public static class CursorSettingsEnumValues
{
    public static Rgba Color(this RingColor c) => c switch
    {
        RingColor.Yellow => new Rgba(255, 204, 0),   // 시스템 yellow 근사 (팔레트 design-tunable)
        RingColor.Red => new Rgba(255, 77, 77),      // (1, 0.3, 0.3)
        RingColor.Blue => new Rgba(77, 153, 255),    // (0.3, 0.6, 1)
        RingColor.Green => new Rgba(77, 255, 128),   // (0.3, 1, 0.5)
        RingColor.White => new Rgba(255, 255, 255),
        RingColor.Cyan => new Rgba(0, 230, 255),     // (0, 0.9, 1)
        RingColor.Purple => new Rgba(204, 77, 255),  // (0.8, 0.3, 1)
        RingColor.Custom => new Rgba(255, 128, 0),   // placeholder; 실제는 customRingColor
        _ => new Rgba(255, 255, 255),
    };

    /// <summary>색 위에 어두운 텍스트가 가독성 좋은가 — 휘도 위임(custom 색도 자동).</summary>
    public static bool NeedsDarkText(this RingColor c) => c.Color().NeedsDarkText;

    public static string Label(this RingColor c) => c switch
    {
        RingColor.Yellow => "노란색", RingColor.Red => "빨간색", RingColor.Blue => "파란색",
        RingColor.Green => "초록색", RingColor.White => "흰색", RingColor.Cyan => "하늘색",
        RingColor.Purple => "보라색", RingColor.Custom => "커스텀", _ => c.ToString(),
    };

    public static string Label(this RingShape s) => s switch
    {
        RingShape.Circle => "원형", RingShape.Squircle => "둥근 사각형",
        RingShape.Rhombus => "둥근 마름모", RingShape.Hexagon => "둥근 육각형", _ => s.ToString(),
    };

    public static double Diameter(this RingSize s) => s switch
    {
        RingSize.Small => 36, RingSize.Medium => 54, RingSize.Large => 72, RingSize.XLarge => 96, _ => 54,
    };

    public static string Label(this RingSize s) => s switch
    {
        RingSize.Small => "작게 (36pt)", RingSize.Medium => "보통 (54pt)",
        RingSize.Large => "크게 (72pt)", RingSize.XLarge => "매우 크게 (96pt)", _ => s.ToString(),
    };

    public static double Multiplier(this AnimationSpeed a) => a switch
    {
        AnimationSpeed.Slow => 1.7, AnimationSpeed.Normal => 1.0, AnimationSpeed.Fast => 0.5, _ => 1.0,
    };

    public static string Label(this AnimationSpeed a) => a switch
    {
        AnimationSpeed.Slow => "느리게", AnimationSpeed.Normal => "보통", AnimationSpeed.Fast => "빠르게", _ => a.ToString(),
    };

    /// <summary>감지에 필요한 0.5초 내 방향 전환 횟수 — ShakeState.RequiredDirChanges로 주입.</summary>
    public static int RequiredDirChanges(this ShakeSensitivity s) => s switch
    {
        ShakeSensitivity.Sensitive => 3, ShakeSensitivity.Normal => 5, ShakeSensitivity.Insensitive => 8, _ => 5,
    };

    public static string Label(this ShakeSensitivity s) => s switch
    {
        ShakeSensitivity.Sensitive => "민감", ShakeSensitivity.Normal => "보통", ShakeSensitivity.Insensitive => "둔감", _ => s.ToString(),
    };

    public static double LineWidth(this BorderWeight w) => w switch
    {
        BorderWeight.Thin => 1.5, BorderWeight.Normal => 3.0, BorderWeight.Bold => 5.5, BorderWeight.Heavy => 9.0, _ => 1.5,
    };

    public static string Label(this BorderWeight w) => w switch
    {
        BorderWeight.Thin => "얇게", BorderWeight.Normal => "보통", BorderWeight.Bold => "굵게", BorderWeight.Heavy => "두껍게", _ => w.ToString(),
    };

    public static string Label(this BorderStyle b) => b switch
    {
        BorderStyle.Solid => "실선", BorderStyle.Dashed => "대시", _ => b.ToString(),
    };

    /// <summary>UI 언어 코드 — System은 null(OS 설정 따름).</summary>
    public static string? LanguageCode(this PreferredLanguage p) => p switch
    {
        PreferredLanguage.Ko => "ko", PreferredLanguage.En => "en", _ => null,
    };

    public static string Label(this PreferredLanguage p) => p switch
    {
        PreferredLanguage.System => "시스템 기본", PreferredLanguage.Ko => "한국어", PreferredLanguage.En => "English", _ => p.ToString(),
    };
}
