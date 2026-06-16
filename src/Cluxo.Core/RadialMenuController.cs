namespace Cluxo.Core;

/// <summary>
/// 라디얼 메뉴 hold 상호작용 — 열기/선택추적/실행. (Swift radial menu marking-mode 로직 이식)
///
/// 시간 주입(now)으로 marking-mode 진입(reveal threshold) 결정 — 빠른 flick은 시각 없이 실행,
/// 느린 hold는 메뉴 표시. 선택은 <see cref="RadialHitTest"/>를 <see cref="RadialMenu"/> 트리로 구동.
/// 실행은 선택에 따라 CursorSettings/CursorRuntimeState를 변경. 순수·테스트 가능.
/// </summary>
public sealed class RadialMenuController
{
    private readonly CursorSettings _settings;
    private readonly CursorRuntimeState _runtime;
    private double _openTime;

    /// <summary>hold 이 시간 지나면 메뉴 표시(marking mode). 그 전 release는 시각 없이 실행.</summary>
    public const double RevealThreshold = 0.15;

    private static readonly RadialHitTest.Rings Rings = new(
        Tokens.Radial.DeadRadius, Tokens.Radial.MainOuter, Tokens.Radial.SubOuter, Tokens.Radial.SubSubOuter);

    public RadialMenuController(CursorSettings settings, CursorRuntimeState runtime)
    {
        _settings = settings;
        _runtime = runtime;
    }

    /// <summary>chord hold 시작 — 중심 고정, 선택 초기화.</summary>
    public void Open(PointD center, double now)
    {
        _runtime.IsRadialMenuActive = true;
        _runtime.IsRadialMenuVisible = false;
        _runtime.RadialMenuCenter = center;
        _runtime.RadialMenuSelectedSector = null;
        _runtime.RadialMenuSelectedSubItem = null;
        _runtime.RadialMenuSelectedSubSubItem = null;
        _openTime = now;
    }

    /// <summary>hold 중 매 프레임 — 커서 위치로 선택 갱신 + reveal threshold 후 메뉴 표시.</summary>
    public void Update(PointD cursorPos, double now)
    {
        if (!_runtime.IsRadialMenuActive) return;
        if (now - _openTime >= RevealThreshold) _runtime.IsRadialMenuVisible = true;

        double dx = cursorPos.X - _runtime.RadialMenuCenter.X;
        double dy = cursorPos.Y - _runtime.RadialMenuCenter.Y;
        var hit = Classify(dx, dy);
        _runtime.RadialMenuSelectedSector = hit.Sector;
        _runtime.RadialMenuSelectedSubItem = hit.Sub;
        _runtime.RadialMenuSelectedSubSubItem = hit.SubSub;
    }

    /// <summary>chord 떼임 — 현재 선택 액션 실행(dead zone/바깥이면 취소) 후 닫기.</summary>
    public void Close()
    {
        if (!_runtime.IsRadialMenuActive) return;
        Execute(_runtime.RadialMenuSelectedSector, _runtime.RadialMenuSelectedSubItem, _runtime.RadialMenuSelectedSubSubItem);
        _runtime.IsRadialMenuActive = false;
        _runtime.IsRadialMenuVisible = false;
        _runtime.RadialMenuSelectedSector = null;
        _runtime.RadialMenuSelectedSubItem = null;
        _runtime.RadialMenuSelectedSubSubItem = null;
    }

    private RadialHitTest.Hit Classify(double dx, double dy)
        => RadialHitTest.Classify(dx, dy,
            _runtime.RadialMenuSelectedSector, _runtime.RadialMenuSelectedSubItem, Rings,
            s => ((RadialMenuItem)s).SubCount(),
            s => ((RadialMenuItem)s).SubSpan(),
            (s, sub) => ((RadialMenuItem)s).SubItems()[sub].IsBranch,
            (s, sub) =>
            {
                var subs = ((RadialMenuItem)s).SubItems();
                return sub < subs.Count && subs[sub].Children is { Count: > 0 } k ? k.Count : 0;
            },
            (s, sub) => ((RadialMenuItem)s).SubSubSpan(sub));

    // 선택 → 액션. sub null(메인) = sector 기본(토글류만), sub/subSub = 값 선택. (Swift 실행 로직 이식)
    private void Execute(int? sectorIdx, int? sub, int? subSub)
    {
        if (sectorIdx is not { } si) return; // dead zone / 바깥 → 취소
        var item = (RadialMenuItem)si;
        var s = _settings;
        var rt = _runtime;

        switch (item)
        {
            case RadialMenuItem.Spotlight:
                if (sub is null or 0) rt.IsSpotlightActive = !rt.IsSpotlightActive;
                else if (sub == 1 && subSub is { } a && a < RadialMenu.SpotlightRadii.Length)
                    s.SpotlightRadius = RadialMenu.SpotlightRadii[a];
                else if (sub == 2 && subSub is { } b && b < RadialMenu.SpotlightSoftnesses.Length)
                    s.SpotlightEdgeSoftness = RadialMenu.SpotlightSoftnesses[b];
                break;

            case RadialMenuItem.Magnifier:
                if (sub is null or 0) rt.IsMagnifierActive = !rt.IsMagnifierActive;
                else if (sub == 1 && subSub is { } z && z < RadialMenu.MagnifierZooms.Length)
                    s.MagnifierZoom = RadialMenu.MagnifierZooms[z];
                else if (sub == 2 && subSub is { } w && w < RadialMenu.MagnifierSizes.Length)
                    s.MagnifierSize = RadialMenu.MagnifierSizes[w];
                break;

            case RadialMenuItem.Glow:
                switch (sub)
                {
                    case 0: s.IsGlowEnabled = !s.IsGlowEnabled; break;
                    case 1: s.IsTrailEnabled = !s.IsTrailEnabled; break;
                    case 2: s.IsIdlePulseEnabled = !s.IsIdlePulseEnabled; break;
                    case 3: s.IsCometTailEnabled = !s.IsCometTailEnabled; break;
                }
                break;

            case RadialMenuItem.RingSize:
                switch (sub)
                {
                    case 0 when subSub is { } i0 && i0 < SizeCases.Length: s.RingSize = SizeCases[i0]; break;
                    case 1 when subSub is { } i1 && i1 < RadialMenu.RingOpacities.Length: s.RingOpacity = RadialMenu.RingOpacities[i1]; break;
                    case 2 when subSub is { } i2 && i2 < WeightCases.Length: s.BorderWeight = WeightCases[i2]; break;
                    case 3 when subSub is { } i3 && i3 < StyleCases.Length: s.BorderStyle = StyleCases[i3]; break;
                }
                break;

            case RadialMenuItem.Color:
                if (sub is { } ci && ci < ColorCases.Length) s.RingColor = ColorCases[ci];
                break;

            case RadialMenuItem.RingShape:
                if (sub is { } shi && shi < ShapeCases.Length) s.RingShape = ShapeCases[shi];
                break;

            case RadialMenuItem.Inspector:
                if (sub == 0) rt.IsInspectorActive = !rt.IsInspectorActive;
                else if (sub == 1) s.IsDragAngleLabelEnabled = !s.IsDragAngleLabelEnabled;
                break;

            case RadialMenuItem.Keystroke:
                if (sub is null or 0) s.IsKeystrokeEnabled = !s.IsKeystrokeEnabled;
                else if (sub is { } ki && ki - 1 < KeystrokeTimes.Length) s.KeystrokeTimeout = KeystrokeTimes[ki - 1];
                break;
        }
    }

    private static readonly RingColor[] ColorCases =
        Enum.GetValues<RingColor>().Where(c => c != RingColor.Custom).ToArray();
    private static readonly RingShape[] ShapeCases = Enum.GetValues<RingShape>();
    private static readonly RingSize[] SizeCases = Enum.GetValues<RingSize>();
    private static readonly BorderWeight[] WeightCases = Enum.GetValues<BorderWeight>();
    private static readonly BorderStyle[] StyleCases = Enum.GetValues<BorderStyle>();
    private static readonly double[] KeystrokeTimes = { 1, 2, 4, 8 };
}
