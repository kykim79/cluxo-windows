namespace Cluxo.Core;

/// <summary>라디얼 메뉴 8개 메인 sector — 값 = sector index(12시=0, 시계방향 45°). (Swift <c>RadialMenuItem</c>)</summary>
public enum RadialMenuItem { Spotlight = 0, Magnifier, Glow, RingSize, Color, RingShape, Inspector, Keystroke }

/// <summary>서브 항목. Children 있으면 branch(2단계 → 3번째 ring으로 자식 fan). 없으면 leaf(클릭 즉시).</summary>
public sealed record RadialSubItem(string Label, string? Desc = null, IReadOnlyList<RadialSubItem>? Children = null)
{
    public bool IsBranch => Children is { Count: > 0 };
}

/// <summary>
/// 라디얼 메뉴 콘텐츠 — 트리 구조 + 라벨/설명 + 현재 상태 로직. (Swift <c>RadialMenuItem</c> 이식)
///
/// SF Symbol 아이콘은 macOS 전용이라 제외 — 렌더 계층이 sector→아이콘 매핑. 트리는 설정 *값*이 아니라
/// enum 케이스 집합에만 의존하므로 정적. 렌더와 hit-test가 같은 트리를 쓰도록 단일 source(RadialHitTest 주입).
/// </summary>
public static class RadialMenu
{
    public static readonly RadialMenuItem[] Items = Enum.GetValues<RadialMenuItem>();

    // branch 자식 값 배열 (Swift static)
    public static readonly double[] SpotlightRadii = { 60, 100, 140, 180, 220 };
    public static readonly double[] SpotlightSoftnesses = { 0, 0.4, 0.8 };
    public static readonly double[] MagnifierZooms = { 1.5, 2, 2.5, 3, 4 };
    public static readonly double[] MagnifierSizes = { 160, 200, 260, 320 };
    public static readonly double[] RingOpacities = { 1.0, 0.8, 0.6, 0.4, 0.2 };

    // 값 케이스 집합 — Tree.Build()가 참조하므로 Tree보다 먼저 초기화돼야 함(정적 초기화 순서).
    private static readonly RingColor[] ColorCases =
        Enum.GetValues<RingColor>().Where(c => c != RingColor.Custom).ToArray();
    private static readonly RingShape[] ShapeCases = Enum.GetValues<RingShape>();
    private static readonly RingSize[] SizeCases = Enum.GetValues<RingSize>();
    private static readonly BorderWeight[] WeightCases = Enum.GetValues<BorderWeight>();
    private static readonly BorderStyle[] StyleCases = Enum.GetValues<BorderStyle>();
    private static readonly double[] KeystrokeTimes = { 1, 2, 4, 8 };

    private static readonly Dictionary<RadialMenuItem, IReadOnlyList<RadialSubItem>> Tree = Build();

    public static IReadOnlyList<RadialSubItem> SubItems(this RadialMenuItem m) => Tree[m];
    public static int SubCount(this RadialMenuItem m) => Tree[m].Count;

    /// <summary>서브 fan 각도 — 라벨 내용 폭 기반(개수 아님). 렌더·hittest 공유.</summary>
    public static double SubSpan(this RadialMenuItem m)
        => RadialLabel.ContentSpan(Labels(Tree[m]), (Tokens.Radial.MainOuter + Tokens.Radial.SubOuter) / 2);

    /// <summary>branch sub의 자식 fan 각도.</summary>
    public static double SubSubSpan(this RadialMenuItem m, int subIndex)
    {
        var subs = Tree[m];
        if (subIndex >= subs.Count || subs[subIndex].Children is not { Count: > 0 } kids) return 0;
        return RadialLabel.ContentSpan(Labels(kids), (Tokens.Radial.SubOuter + Tokens.Radial.SubSubOuter) / 2);
    }

    public static string Label(this RadialMenuItem m) => m switch
    {
        RadialMenuItem.Spotlight => "스포트라이트", RadialMenuItem.Magnifier => "돋보기",
        RadialMenuItem.Glow => "효과", RadialMenuItem.RingSize => "링 외형",
        RadialMenuItem.Color => "링 색", RadialMenuItem.RingShape => "링 모양",
        RadialMenuItem.Inspector => "좌표/각도", RadialMenuItem.Keystroke => "키 입력", _ => m.ToString(),
    };

    public static string Desc(this RadialMenuItem m) => m switch
    {
        RadialMenuItem.Spotlight => "커서 주변만 밝히고 나머지 화면을 어둡게 덮어 시선을 모읍니다.",
        RadialMenuItem.Magnifier => "커서 주변을 실시간 확대해 작은 글씨·UI를 키워 봅니다.",
        RadialMenuItem.Glow => "글로우·트레일·정지 펄스·코멧 등 커서 강조 효과를 켜고 끕니다.",
        RadialMenuItem.RingSize => "커서를 감싸는 링의 크기·투명도·두께·선 스타일을 조절합니다.",
        RadialMenuItem.Color => "커서 강조 링의 색을 바꿉니다.",
        RadialMenuItem.RingShape => "링의 형태(원·둥근 사각형·마름모·육각형)를 바꿉니다.",
        RadialMenuItem.Inspector => "커서 좌표와 드래그 각도를 화면에 표시합니다.",
        RadialMenuItem.Keystroke => "누른 단축키를 화면에 자막처럼 표시합니다.", _ => "",
    };

    /// <summary>중심에 표시할 "라벨 / 값" 중 값 부분 — 현재 설정/상태 요약.</summary>
    public static string CurrentValue(this RadialMenuItem m, CursorSettings settings, CursorRuntimeState runtime) => m switch
    {
        RadialMenuItem.Spotlight => runtime.IsSpotlightActive ? $"켜짐 · {(int)settings.SpotlightRadius}pt" : "꺼짐",
        RadialMenuItem.Magnifier => runtime.IsMagnifierActive ? $"켜짐 · {settings.MagnifierZoom.ToString("0.0")}×" : "꺼짐",
        RadialMenuItem.Glow => $"{CountOn(settings.IsGlowEnabled, settings.IsTrailEnabled, settings.IsIdlePulseEnabled, settings.IsCometTailEnabled)}/4 켜짐",
        RadialMenuItem.RingSize => settings.RingSize.Label(),
        RadialMenuItem.Color => settings.RingColor.Label(),
        RadialMenuItem.RingShape => settings.RingShape.Label(),
        RadialMenuItem.Inspector => $"{CountOn(runtime.IsInspectorActive, settings.IsDragAngleLabelEnabled)}/2 켜짐",
        RadialMenuItem.Keystroke => settings.IsKeystrokeEnabled ? $"켜짐 · {(int)settings.KeystrokeTimeout}초" : "꺼짐",
        _ => "",
    };

    /// <summary>서브 항목 i가 현재 설정/상태와 일치하나 — 활성 강조용. (토글류는 enabled 반영, 값류는 현재값 일치)</summary>
    public static bool IsSubCurrent(this RadialMenuItem m, int i, CursorSettings settings, CursorRuntimeState runtime) => m switch
    {
        RadialMenuItem.Spotlight => i == 0 && runtime.IsSpotlightActive,
        RadialMenuItem.Magnifier => i == 0 && runtime.IsMagnifierActive,
        RadialMenuItem.Glow => i switch
        {
            0 => settings.IsGlowEnabled, 1 => settings.IsTrailEnabled,
            2 => settings.IsIdlePulseEnabled, 3 => settings.IsCometTailEnabled, _ => false,
        },
        RadialMenuItem.RingSize => false, // 전부 branch
        RadialMenuItem.Color => IndexEquals(ColorCases, i, settings.RingColor),
        RadialMenuItem.RingShape => IndexEquals(ShapeCases, i, settings.RingShape),
        RadialMenuItem.Keystroke => i == 0
            ? settings.IsKeystrokeEnabled
            : i - 1 < KeystrokeTimes.Length && Math.Abs(settings.KeystrokeTimeout - KeystrokeTimes[i - 1]) < 0.05,
        RadialMenuItem.Inspector => i switch
        {
            0 => runtime.IsInspectorActive, 1 => settings.IsDragAngleLabelEnabled, _ => false,
        },
        _ => false,
    };

    /// <summary>branch sub의 자식 j가 현재 설정과 일치하나 — subSub fan 강조용.</summary>
    public static bool IsSubSubCurrent(this RadialMenuItem m, int sub, int subSub, CursorSettings settings, CursorRuntimeState runtime) => m switch
    {
        RadialMenuItem.Spotlight =>
            sub == 1 ? Near(SpotlightRadii, subSub, settings.SpotlightRadius, 0.5)
          : sub == 2 ? Near(SpotlightSoftnesses, subSub, settings.SpotlightEdgeSoftness, 0.05)
          : false,
        RadialMenuItem.Magnifier =>
            sub == 1 ? Near(MagnifierZooms, subSub, settings.MagnifierZoom, 0.05)
          : sub == 2 ? Near(MagnifierSizes, subSub, settings.MagnifierSize, 0.5)
          : false,
        RadialMenuItem.RingSize => sub switch
        {
            0 => IndexEquals(SizeCases, subSub, settings.RingSize),
            1 => Near(RingOpacities, subSub, settings.RingOpacity, 0.025),
            2 => IndexEquals(WeightCases, subSub, settings.BorderWeight),
            3 => IndexEquals(StyleCases, subSub, settings.BorderStyle),
            _ => false,
        },
        _ => false,
    };

    // ── 내부 ─────────────────────────────────────────────────────

    private static string[] Labels(IReadOnlyList<RadialSubItem> items)
    {
        var r = new string[items.Count];
        for (int i = 0; i < items.Count; i++) r[i] = items[i].Label;
        return r;
    }

    private static int CountOn(params bool[] flags) => flags.Count(f => f);

    private static bool IndexEquals<T>(T[] cases, int i, T value) => i >= 0 && i < cases.Length && EqualityComparer<T>.Default.Equals(cases[i], value);

    private static bool Near(double[] arr, int i, double value, double tol) => i >= 0 && i < arr.Length && Math.Abs(arr[i] - value) < tol;

    private static Dictionary<RadialMenuItem, IReadOnlyList<RadialSubItem>> Build()
    {
        RadialSubItem Leaf(string label, string? desc = null) => new(label, desc);
        RadialSubItem[] Of(params RadialSubItem[] xs) => xs;

        return new()
        {
            [RadialMenuItem.Spotlight] = Of(
                Leaf("토글", "스포트라이트를 켜거나 끕니다."),
                new("반경", "밝게 남길 원의 반경을 정합니다.",
                    SpotlightRadii.Select(r => new RadialSubItem($"{(int)r}pt")).ToArray()),
                new("경계", "밝은 영역과 어두운 영역 사이 경계의 부드러움을 정합니다.",
                    Of(Leaf("또렷"), Leaf("보통"), Leaf("부드럽게")))),

            [RadialMenuItem.Magnifier] = Of(
                Leaf("토글", "돋보기를 켜거나 끕니다."),
                new("배율", "확대 배율을 정합니다.",
                    Of(Leaf("1.5×"), Leaf("2×"), Leaf("2.5×"), Leaf("3×"), Leaf("4×"))),
                new("크기", "돋보기 창의 크기를 정합니다.",
                    Of(Leaf("작게"), Leaf("보통"), Leaf("크게"), Leaf("매우 크게")))),

            [RadialMenuItem.Glow] = Of(
                Leaf("글로우", "커서 주위에 은은한 빛 번짐을 더합니다."),
                Leaf("트레일", "커서가 지나간 자리에 짧은 잔상을 남깁니다."),
                Leaf("정지펄스", "커서가 잠시 멈추면 물결 펄스로 위치를 알립니다."),
                Leaf("코멧", "드래그할 때 혜성 같은 꼬리를 그립니다.")),

            [RadialMenuItem.RingSize] = Of(
                new("크기", "링의 지름을 정합니다.", SizeCases.Select(s => new RadialSubItem(s.Label())).ToArray()),
                new("투명도", "링의 불투명도를 정합니다.",
                    RingOpacities.Select(o => new RadialSubItem($"{(int)(o * 100)}%")).ToArray()),
                new("두께", "링 외곽선의 두께를 정합니다.", WeightCases.Select(w => new RadialSubItem(w.Label())).ToArray()),
                new("스타일", "링 외곽선의 선 종류(실선·점선 등)를 정합니다.", StyleCases.Select(b => new RadialSubItem(b.Label())).ToArray())),

            [RadialMenuItem.Color] = ColorCases.Select(c => new RadialSubItem(c.Label())).ToArray(),
            [RadialMenuItem.RingShape] = ShapeCases.Select(s => new RadialSubItem(s.Label())).ToArray(),

            [RadialMenuItem.Inspector] = Of(
                Leaf("좌표", "커서의 화면 좌표(x, y)를 실시간 표시합니다."),
                Leaf("드래그각도", "드래그 중 이동 방향의 각도를 표시합니다.")),

            [RadialMenuItem.Keystroke] = Of(
                Leaf("토글"), Leaf("1초"), Leaf("2초"), Leaf("4초"), Leaf("8초")),
        };
    }
}
