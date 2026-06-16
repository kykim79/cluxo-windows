namespace Cluxo.Core;

/// <summary>
/// 디자인 토큰 — DESIGN.md 단일 소스에서 수동 미러링. (Swift <c>DesignTokens.Tokens</c> 전체 이식)
///
/// UI에서 색·opacity·spacing·corner radius·motion·radial 거리를 하드코딩하지 말 것.
/// 새 값은 DESIGN.md에 토큰 먼저 추가 후 여기 미러. SwiftUI 전용 타입은 플랫폼 무관 데이터로 옮김:
/// Color.opacity → Rgba(straight alpha), Animation → Spring/Ease, Font → FontToken.
///
/// ringColor(사용자 선택)는 토큰화하지 않음 — 효과는 effectiveColor를 주입받아 사용.
/// </summary>
public static class Tokens
{
    /// <summary>오버레이/패널 배경 (검정 기반 반투명).</summary>
    public static class Surface
    {
        public static readonly Rgba Dim = Rgba.FromBlack(0.78);      // 스포트라이트 dim
        public static readonly Rgba Panel = Rgba.FromBlack(0.72);    // 일반 panel
        public static readonly Rgba Veil = Rgba.FromBlack(0.55);     // 중앙 capsule/약한 veil
        public static readonly Rgba Subtle = Rgba.FromBlack(0.65);   // 서브 wedge 비활성
        public static readonly Rgba MainIdle = Rgba.FromBlack(0.55); // 메인 wedge 비활성
    }

    /// <summary>윤곽선/가이드/텍스트 (흰색 기반).</summary>
    public static class Stroke
    {
        public static readonly Rgba GuideStrong = Rgba.FromWhite(0.30);
        public static readonly Rgba GuideMedium = Rgba.FromWhite(0.18);
        public static readonly Rgba GuideWeak = Rgba.FromWhite(0.12);
        public static readonly Rgba Cursor = Rgba.FromWhite(0.70);     // radial 중 cursor ring
        public static readonly Rgba TextActive = Rgba.FromWhite(0.95);
        public static readonly Rgba TextMuted = Rgba.FromWhite(0.60);
    }

    /// <summary>모션 — DESIGN.md 모션 토큰의 플랫폼 무관 파라미터.</summary>
    public static class Motion
    {
        // Spring (물리 이벤트)
        public static readonly Spring Snap = new(0.10, 0.40);     // 클릭 펄스 ring 축소
        public static readonly Spring Bounce = new(0.15, 0.60);   // snap-back
        public static readonly Spring ReturnTo = new(0.50, 0.45); // 복귀
        public static readonly Spring Drag = new(0.25, 0.70);     // 드래그 시작
        public static readonly Spring DragEnd = new(0.45, 0.55);  // 드래그 종료
        public static readonly Spring Select = new(0.18, 0.75);   // wedge select / ringSize
        public static readonly Spring Shrink = new(0.30, 0.70);   // 일반 spring

        // Ease (페이드/상태 전이)
        public static readonly Ease EaseMicro = new(EaseCurve.Out, 0.12);
        public static readonly Ease EaseShort = new(EaseCurve.Out, 0.15);
        public static readonly Ease EaseMedium = new(EaseCurve.Out, 0.30);
        public static readonly Ease EaseLong = new(EaseCurve.InOut, 0.35); // 모션 상한

        // Pure durations — withAnimation 외 직접 duration 필요할 때
        public const double EaseMicroDuration = 0.12;
        public const double EaseShortDuration = 0.15;
        public const double EaseMediumDuration = 0.30;
        public const double EaseLongDuration = 0.35;
    }

    /// <summary>radial menu 거리 스케일.</summary>
    public static class Radial
    {
        public const double DeadRadius = 50;     // dead zone (cancel)
        public const double MainOuter = 102;     // 메인 영역 바깥
        public const double SubOuter = 174;      // 서브 영역 바깥 (sector lock)
        public const double SubSubOuter = 236;   // 서브-서브 영역 바깥
        public const double ReleaseSafety = 80;  // 오발 방지 최소 drag
        public const double EdgeClamp = 256;     // 화면 가장자리~중심 최소
        public static double CanvasSize => SubSubOuter * 2 + 40; // 캔버스 전체
        public const double LongPressDuration = 0.5;  // hold→메뉴 임계(초)
        public const double LongPressDeadband = 5;    // hold 중 허용 이동
        public const double DwellDelay = 0.6;         // dwell 설명 표시 지연(초)

        // 라벨 폭 (긴 번역 wrap/축소 최대 폭)
        public const double MainLabelWidth = 88;
        public const double SubLabelWidth = 84;
        public const double SubSubLabelWidth = 76;
        public const double CenterLabelWidth = 92;
        public const double DescWidth = 260;
        public const double LabelScale = 0.65;          // 최소 축소 배율
        public const double BranchFillOpacity = 0.22;   // branch sub 배경 accent 옅게
    }

    /// <summary>corner radius 스케일.</summary>
    public static class Radius
    {
        public const double Sm = 4;   // inline pill
        public const double Md = 8;   // 작은 panel
        public const double Lg = 12;  // 키스트로크/카드
        public const double Xl = 16;  // 큰 카드
    }

    /// <summary>spacing — base unit 4pt.</summary>
    public static class Spacing
    {
        public const double Xs = 4;
        public const double Sm = 8;
        public const double Md = 12;
        public const double Lg = 16;
        public const double Xl = 24;
        public const double Xxl = 32;
    }

    /// <summary>⌃⌥D 그리기 도형 토큰.</summary>
    public static class Drawing
    {
        public const double LineWidth = 4;                                  // 기본 stroke 두께
        public static readonly double[] LineWidthSteps = { 2, 4, 6, 10, 14 }; // [ ] 키 5단계
        public const double ArrowHeadLength = 16;                           // 화살촉 변 길이
        public const double ArrowHeadAngle = Math.PI / 6;                   // 화살촉 벌어짐(30°)
        public const double HighlighterWidth = 25;                          // 형광펜 두께
        public const double HighlighterOpacity = 0.35;                      // 형광펜 alpha
        public const double BadgeRadius = 16;                               // 뱃지 반지름
        public const double BadgeBorderWidth = 2;                           // 뱃지 외곽선
        public const double BadgeFontSize = 14;                             // 뱃지 텍스트

        /// <summary>그리기 모드 좌측 하단 도구바.</summary>
        public static class Toolbar
        {
            public const double Padding = 16;
            public const double CornerRadius = 14;
            public const double DividerHeight = 48;
            public const double DividerOpacity = 0.2;
            public const double BorderOpacity = 0.18;
            public const double ToolCircle = 36;     // 도구 아이콘 원 지름
            public const double ToolGlyph = 17;
            public const double ToolLabelSize = 12;
            public const double ToolModifierSize = 10;
            public const double SectionLabelSize = 11;
            public const double SectionHintSize = 9;
            public const double CheatSize = 9;
            public const double ColorDot = 16;
            public const double ColorHitArea = 24;
            public const double ThicknessHitArea = 24;
            public const double DragHandleDot = 3;
            public const double DragHandleDotSpacing = 4;
            public const double CheatSheetHideBelow = 1200;  // 반응형 임계
            public const int OnboardingShowCount = 5;
            public const double OnboardingDuration = 6.0;
            public const double SelectionRingWidth = 2.0;
            public const double GroupSpacing = 18;
        }
    }

    /// <summary>시스템 폰트 역할별 토큰.</summary>
    public static class Text
    {
        public static readonly FontToken Icon = new(28);                                  // 메인 sector glyph
        public static readonly FontToken IconCenter = new(22);                            // 중앙 컨텍스트 icon
        public static readonly FontToken Label = new(13, FontWeight.Semibold);            // 메인 라벨/키스트로크
        public static readonly FontToken LabelTiny = new(9, FontWeight.Semibold);         // 중앙 sector 라벨
        public static readonly FontToken Caption = new(11, FontWeight.Medium);            // sub item/현재값
        public static readonly FontToken CaptionSmall = new(10, FontWeight.Semibold);     // 메인 wedge 라벨
        public static readonly FontToken Hint = new(10, FontWeight.Medium);               // 헬프 텍스트
        public static readonly FontToken Mono = new(11, FontWeight.Semibold, Monospaced: true); // 좌표
    }
}
