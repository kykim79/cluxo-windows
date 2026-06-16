using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Cluxo.Core;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Render;

/// <summary>
/// 한 모니터 오버레이의 WPF 그리기 표면. 불변 <see cref="OverlayFrame"/>을 받아 OnRender에서 그린다.
/// 좌표는 물리 픽셀(가상 데스크톱) → 로컬 DIP 변환(OVERLAY-RENDER.md §4): local = (phys - origin)/dpiScale.
///
/// 그리는 요소(뒤→앞, §5): 트레일·드래그트레일·스크롤·흔들기·정지펄스·더블클릭·클릭 ripple ·
/// 커서 링 · 드래그 힌트 · 그리기 도형 · 라디얼 메뉴 · 키스트로크 카드 · 코브랜딩 워터마크.
///
/// 효과 progress(§5.1): 모든 효과 Id는 EffectsState에서 전역 고유 → 첫 등장 tick(firstSeen)을 기록해
/// progress = (now - firstSeen)/(ExpiresAt - firstSeen). 수명/animationSpeed를 몰라도 0→1 정확.
/// 스톱갭(WPF) — D2D 교체 시 동일 OverlayFrame을 소비. 라디얼은 기본 wedge(라벨/sub는 후속).
/// </summary>
internal sealed class OverlayElement : FrameworkElement
{
    private readonly MonitorInfo _monitor;
    private readonly Func<double> _clock;
    private readonly Dictionary<int, double> _firstSeen = new(); // 효과 Id → 첫 등장 tick
    private readonly RingSpring _ringSpring = new(Tokens.Motion.ReturnTo); // 클릭 squash → 1.0 복귀(§5.2)
    private OverlayFrame? _frame;
    private double _now;
    private double _lastNow = -1;
    private int _lastClickId; // 새 클릭 감지(효과 Id는 단조 증가)

    // 라디얼 등장 애니메이션 타이밍 (맥 appear/stagger 대응)
    private double _radialOpenAt = -1;
    private int? _radialSeenSector;
    private double _radialSectorAt;
    private int? _radialSeenSub;
    private double _radialSubAt;

    public OverlayElement(MonitorInfo monitor, Func<double> clock)
    {
        _monitor = monitor;
        _clock = clock;
        IsHitTestVisible = false;
    }

    public void SetFrame(OverlayFrame frame)
    {
        _frame = frame;
        _now = _clock();
        double dt = _lastNow < 0 ? 0 : _now - _lastNow;
        _lastNow = _now;

        // 새 클릭 → 링 squash. 이후 매 프레임 스프링이 1.0으로 복귀.
        int maxClick = MaxClickId(frame.Effects.Clicks);
        if (maxClick > _lastClickId) { _ringSpring.Bump(); _lastClickId = maxClick; }
        _ringSpring.Advance(dt);

        InvalidateVisual();
    }

    private static int MaxClickId(IReadOnlyList<ClickEffect> clicks)
    {
        int max = 0;
        foreach (var c in clicks) if (c.Id > max) max = c.Id;
        return max;
    }

    private Point ToLocal(PointD p)
    {
        double s = _monitor.DpiScale <= 0 ? 1.0 : _monitor.DpiScale;
        return new Point((p.X - _monitor.Bounds.X) / s, (p.Y - _monitor.Bounds.Y) / s);
    }

    // firstSeen 기반 0→1 진행도. expiresAt까지의 비율.
    private double Progress(int id, double expiresAt)
    {
        if (!_firstSeen.TryGetValue(id, out var t0)) { t0 = _now; _firstSeen[id] = t0; }
        double denom = Math.Max(0.0001, expiresAt - t0);
        return Math.Clamp((_now - t0) / denom, 0, 1);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_frame is not { } f) return;
        var accent = f.Ring?.Color ?? new Rgba(0, 230, 255); // 커서 없는 모니터엔 기본 accent

        PruneFirstSeen(f.Effects);

        DrawTrail(dc, f.Effects.Trail, accent, 3, 0.5);       // 커서 모션 잔상
        DrawTrail(dc, f.Effects.DragTrail, accent, 5, 0.7);   // 드래그 streak
        DrawScrolls(dc, f.Effects.Scrolls, accent);
        DrawExpandingRings(dc, f.Effects.Shakes.Select(e => (e.Id, e.Position, e.ExpiresAt)), accent, 22, 70, 4, 0.9);
        DrawExpandingRings(dc, f.Effects.IdlePulses.Select(e => (e.Id, e.Position, e.ExpiresAt)), accent, 10, 44, 2, 0.5);
        DrawExpandingRings(dc, f.Effects.DoubleClicks.Select(e => (e.Id, e.Position, e.ExpiresAt)), accent, 18, 64, 3, 0.8);
        DrawClicks(dc, f.Effects.Clicks, accent);

        DrawRing(dc, f);
        DrawDrag(dc, f);
        DrawShapes(dc, f);
        DrawRadial(dc, f, accent);

        DrawInspector(dc, f, accent);
        DrawKeystroke(dc, f);
        DrawBranding(dc, f);
    }

    private void PruneFirstSeen(OverlayEffects fx)
    {
        if (_firstSeen.Count == 0) return;
        var live = new HashSet<int>();
        foreach (var e in fx.Clicks) live.Add(e.Id);
        foreach (var e in fx.DoubleClicks) live.Add(e.Id);
        foreach (var e in fx.Scrolls) live.Add(e.Id);
        foreach (var e in fx.Shakes) live.Add(e.Id);
        foreach (var e in fx.IdlePulses) live.Add(e.Id);
        if (_firstSeen.Count > live.Count)
            foreach (var id in _firstSeen.Keys.Where(id => !live.Contains(id)).ToList())
                _firstSeen.Remove(id);
    }

    // ── 트레일 (fade 폴리라인, 끝=최신이 진함) ──────────────────
    private void DrawTrail(DrawingContext dc, IReadOnlyList<TrailPoint> pts, Rgba color, double width, double maxAlpha)
    {
        if (pts is null || pts.Count < 2) return;
        for (int i = 1; i < pts.Count; i++)
        {
            double a = maxAlpha * (i / (double)(pts.Count - 1)); // 오래된 점일수록 옅게
            dc.DrawLine(StrokePen(color, width, a), ToLocal(pts[i - 1].Position), ToLocal(pts[i].Position));
        }
    }

    // ── 스크롤 방향 화살표 ──────────────────────────────────────
    private void DrawScrolls(DrawingContext dc, IReadOnlyList<ScrollEffect> scrolls, Rgba color)
    {
        foreach (var s in scrolls)
        {
            double p = Progress(s.Id, s.ExpiresAt);
            double alpha = 1 - p;
            double len = 16 + Math.Min(20, s.Magnitude * 4);
            var c = ToLocal(s.Position);
            // 방향 단위벡터(화면 좌표, y 아래로 +)
            double dx = s.IsVertical ? 0 : (s.IsPositive ? 1 : -1);
            double dy = s.IsVertical ? (s.IsPositive ? -1 : 1) : 0; // 세로 +는 위로(forward)
            var tip = new Point(c.X + dx * len, c.Y + dy * len);
            var tail = new Point(c.X - dx * len, c.Y - dy * len);
            var pen = StrokePen(color, 3, alpha);
            dc.DrawLine(pen, tail, tip);
            // 화살촉
            double theta = Math.Atan2(tip.Y - tail.Y, tip.X - tail.X);
            double hl = 9, ha = Math.PI / 6;
            dc.DrawLine(pen, tip, new Point(tip.X - hl * Math.Cos(theta - ha), tip.Y - hl * Math.Sin(theta - ha)));
            dc.DrawLine(pen, tip, new Point(tip.X - hl * Math.Cos(theta + ha), tip.Y - hl * Math.Sin(theta + ha)));
        }
    }

    // ── 퍼지는 ring (흔들기/정지펄스/더블클릭 공용) ─────────────
    private void DrawExpandingRings(DrawingContext dc, IEnumerable<(int Id, PointD Pos, double ExpiresAt)> items,
        Rgba color, double r0, double r1, double width, double maxAlpha)
    {
        foreach (var (id, pos, expiresAt) in items)
        {
            double p = Progress(id, expiresAt);
            double radius = r0 + p * (r1 - r0);
            double alpha = (1 - p) * maxAlpha;
            dc.DrawEllipse(null, StrokePen(color, width, alpha), ToLocal(pos), radius, radius);
        }
    }

    // ── 클릭 ripple ─────────────────────────────────────────────
    private void DrawClicks(DrawingContext dc, IReadOnlyList<ClickEffect> clicks, Rgba color)
    {
        foreach (var c in clicks)
        {
            double p = Progress(c.Id, c.ExpiresAt);
            double radius = 12 + p * 40;
            double alpha = (1 - p) * (c.IsRight ? 0.5 : 0.75);
            dc.DrawEllipse(null, StrokePen(color, c.IsDouble ? 4 : 2, alpha), ToLocal(c.Position), radius, radius);
        }
    }

    // ── 커서 링 (모양·두께·선 스타일) ──────────────────────────
    private void DrawRing(DrawingContext dc, OverlayFrame f)
    {
        if (f.Ring is not { } ring || f.CursorPosition is not { } cursor) return;
        double r = ring.Radius * ring.Scale * _ringSpring.Value; // 클릭 squash 스프링(§5.2)
        var c = ToLocal(cursor);

        // #16 드래그 속도 stretch — 진행 방향(StretchAngle)으로 회전 후 x/y 비대칭 스케일.
        bool stretched = ring.StretchX != 1.0 || ring.StretchY != 1.0;
        if (stretched)
        {
            dc.PushTransform(new RotateTransform(ring.StretchAngle, c.X, c.Y));
            dc.PushTransform(new ScaleTransform(ring.StretchX, ring.StretchY, c.X, c.Y));
        }

        // 글로우 — 커서 주위 은은한 후광(accent 라디얼 그라디언트). 링 외곽선 뒤에 그린다.
        if (ring.Glow)
        {
            double gr = r * 1.9;
            dc.DrawEllipse(GlowBrush(ring.Color, ring.Opacity), null, c, gr, gr);
        }

        double innerR = r * 0.76; // 맥: innerSize = size * 0.76

        // 도넛 채우기 (맥 isRingFillEnabled, 기본 ON) — 안쪽~바깥 사이 반투명 채움.
        if (ring.Fill)
        {
            var donut = new GeometryGroup { FillRule = FillRule.EvenOdd };
            donut.Children.Add(RingGeometry(c, r, ring.Shape));
            donut.Children.Add(RingGeometry(c, innerR, ring.Shape));
            donut.Freeze();
            dc.DrawGeometry(MakeBrush(ring.Color, ring.Opacity * 0.18), null, donut);
        }

        // 안쪽 링 (맥 hasInnerRing) — 0.76 크기, 0.55 두께, 0.32 투명도.
        if (ring.InnerRing)
        {
            var innerPen = RingPen(ring.Color, ring.BorderWidth * 0.55, ring.Opacity * 0.32, false);
            dc.DrawGeometry(null, innerPen, RingGeometry(c, innerR, ring.Shape));
        }

        // 바깥 링
        var pen = RingPen(ring.Color, ring.BorderWidth, ring.Opacity, ring.Dashed);
        dc.DrawGeometry(null, pen, RingGeometry(c, r, ring.Shape));

        if (stretched) { dc.Pop(); dc.Pop(); }
    }

    // 링/효과 공용 외형 — 원·squircle·둥근 마름모·둥근 육각형(맥 anyShape 대응). 채우기·획에 모두 사용.
    private static Geometry RingGeometry(Point c, double r, RingShape shape) => shape switch
    {
        RingShape.Squircle => FrozenRoundedRect(c, r, r * 0.56),       // 맥: 변(2r)의 28%
        RingShape.Rhombus => RoundedPolygon(c, r, 4, 0.2),            // 둥근 마름모(cornerFraction 0.2)
        RingShape.Hexagon => RoundedPolygon(c, r, 6, 0.28),           // 둥근 pointy-top 육각형(0.28)
        _ => FrozenEllipse(c, r),                                      // Circle
    };

    private static Geometry FrozenEllipse(Point c, double r)
    {
        var g = new EllipseGeometry(c, r, r); g.Freeze(); return g;
    }

    private static Geometry FrozenRoundedRect(Point c, double r, double cr)
    {
        var g = new RectangleGeometry(new Rect(c.X - r, c.Y - r, r * 2, r * 2), cr, cr); g.Freeze(); return g;
    }

    private Pen RingPen(Rgba color, double width, double opacity, bool dashed)
    {
        var pen = new Pen(MakeBrush(color, opacity), width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        if (dashed) pen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0); // 폭 단위 대시
        pen.Freeze();
        return pen;
    }

    // 커서 글로우 — 중심이 진하고 가장자리로 사라지는 라디얼 그라디언트.
    private static Brush GlowBrush(Rgba color, double opacity)
    {
        byte a = (byte)Math.Clamp(90 * opacity, 0, 255);
        var b = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(a, color.R, color.G, color.B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        b.Freeze();
        return b;
    }

    // 중심 기준 정n각형(첫 꼭짓점 위, 12시 pointy-top)을 모서리 라운딩한 닫힌 path.
    // 맥 roundedPolygonPath 이식 — 각 꼭짓점을 control point로 한 quadratic curve로 부드럽게 깎는다.
    // cornerFraction은 인접 꼭짓점까지 거리 대비 라운딩 비율(0~0.5).
    private static Geometry RoundedPolygon(Point center, double r, int n, double cornerFraction)
    {
        var v = new Point[n];
        for (int i = 0; i < n; i++)
        {
            double a = (-90.0 + i * 360.0 / n) * Math.PI / 180.0;
            v[i] = new Point(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a));
        }
        static Point Lerp(Point p, Point q, double t) => new(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            Point start0 = Lerp(v[0], v[n - 1], cornerFraction);
            ctx.BeginFigure(start0, isFilled: true, isClosed: true);
            for (int i = 0; i < n; i++)
            {
                Point curr = v[i];
                Point prev = v[(i + n - 1) % n];
                Point next = v[(i + 1) % n];
                Point start = Lerp(curr, prev, cornerFraction);
                Point end = Lerp(curr, next, cornerFraction);
                if (i != 0) ctx.LineTo(start, isStroked: true, isSmoothJoin: false);
                ctx.QuadraticBezierTo(curr, end, isStroked: true, isSmoothJoin: false);
            }
        }
        geo.Freeze();
        return geo;
    }

    // ── 드래그 힌트 (anchored line + speed glow) ────────────────
    private void DrawDrag(DrawingContext dc, OverlayFrame f)
    {
        if (f.Drag is not { } d) return;
        var color = f.Ring?.Color ?? new Rgba(0, 230, 255);
        var origin = ToLocal(d.Origin);
        var cur = ToLocal(d.Current);
        if (d.AnchoredLineVisible)
            dc.DrawLine(StrokePen(color, 2, 0.7), origin, cur); // origin→current 기준선
        // speed glow — 속도 클수록 커서에 옅은 후광
        double glow = 6 + Math.Clamp(d.Velocity / 1000.0, 0, 1) * 18;
        dc.DrawEllipse(MakeBrush(color, 0.18), null, cur, glow, glow);

        // 드래그 각도 라벨 — "↗ 45°" (설정 ON일 때만)
        if (d.ShowAngleLabel)
        {
            int deg = DragAngleLabel.ClockwiseDegrees(d.Angle);
            DrawLabelCard(dc, $"{DragAngleLabel.DirectionArrow(deg)} {deg}°",
                new Point(cur.X + 22, cur.Y - 22), color);
        }
    }

    // ── inspector 좌표 라벨 (⌃⌥I) — 커서 옆에 전역 좌표 ──────────
    private void DrawInspector(DrawingContext dc, OverlayFrame f, Rgba accent)
    {
        if (!f.Inspector || f.CursorPosition is not { } cur) return;
        var at = ToLocal(cur);
        DrawLabelCard(dc, $"x {(int)Math.Round(cur.X)}   y {(int)Math.Round(cur.Y)}",
            new Point(at.X + 18, at.Y + 18), accent, mono: true);
    }

    // 작은 라벨 카드(어두운 배경 + accent 텍스트) — 좌상단 기준점.
    private void DrawLabelCard(DrawingContext dc, string text, Point topLeft, Rgba accent, bool mono = false)
    {
        double ppd = _monitor.DpiScale <= 0 ? 1.0 : _monitor.DpiScale;
        var typeface = new Typeface(new FontFamily(mono ? "Consolas" : "Segoe UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, (double)Tokens.Text.Caption.Size, MakeBrush(accent, 1.0), ppd);
        double padX = Tokens.Spacing.Sm, padY = Tokens.Spacing.Xs;
        var rect = new Rect(topLeft.X, topLeft.Y, ft.Width + padX * 2, ft.Height + padY * 2);
        dc.DrawRoundedRectangle(MakeBrush(Tokens.Surface.Panel, 1.0), null, rect, Tokens.Radius.Sm, Tokens.Radius.Sm);
        dc.DrawText(ft, new Point(topLeft.X + padX, topLeft.Y + padY));
    }

    // ── 그리기 도형 ─────────────────────────────────────────────
    private void DrawShapes(DrawingContext dc, OverlayFrame f)
    {
        foreach (var s in f.Shapes)
        {
            if (s.Points.Count == 0) continue;
            switch (s.Tool)
            {
                case DrawingTool.Pen: DrawPolyline(dc, s, s.LineWidth, 1.0); break;
                case DrawingTool.Highlighter:
                    DrawPolyline(dc, s, Tokens.Drawing.HighlighterWidth, Tokens.Drawing.HighlighterOpacity);
                    break;
                case DrawingTool.Line: DrawStraight(dc, s); break;
                case DrawingTool.Arrow: DrawArrow(dc, s); break;
                case DrawingTool.Rectangle: DrawRect(dc, s); break;
                case DrawingTool.Ellipse: DrawOval(dc, s); break;
                case DrawingTool.Badge: DrawBadge(dc, s); break;
            }
        }
    }

    // ── 라디얼 메뉴 (맥 RadialMenuView 대응 — 채워진 pie wedge + 중앙 컨텍스트) ──
    private void DrawRadial(DrawingContext dc, OverlayFrame f, Rgba accent)
    {
        if (f.Radial is not { } radial || !radial.Visible)
        {
            _radialOpenAt = -1; _radialSeenSector = null; _radialSeenSub = null;
            return;
        }
        var center = ToLocal(radial.Center);
        double dead = Tokens.Radial.DeadRadius, main = Tokens.Radial.MainOuter;
        double subR = Tokens.Radial.SubOuter, subSubR = Tokens.Radial.SubSubOuter;
        var guide = StrokePen(Rgba.FromWhite(0.18), 1.0);

        // 등장 타이밍 — 메뉴 open / sector 선택 / sub 선택 시점 기록(맥 appear/stagger).
        if (_radialOpenAt < 0) _radialOpenAt = _now;
        if (_radialSeenSector != radial.Sector) { _radialSeenSector = radial.Sector; _radialSectorAt = _now; }
        if (_radialSeenSub != radial.Sub) { _radialSeenSub = radial.Sub; _radialSubAt = _now; }
        double el = _now - _radialOpenAt, elSub = _now - _radialSectorAt, elSubSub = _now - _radialSubAt;

        // 전체 메뉴 scale-in (0.85 → 1.0)
        double os = 0.85 + 0.15 * EaseOut(Clamp01(el / 0.22));
        dc.PushTransform(new ScaleTransform(os, os, center.X, center.Y));

        // (1) 8개 메인 wedge + 아이콘 — 시계방향 순차 페이드(stagger)
        for (int i = 0; i < 8; i++)
        {
            double wp = EaseOut(Clamp01((el - i * 0.035) / 0.22));
            if (wp <= 0.001) continue;
            bool active = radial.Sector == i;
            bool mainSel = active && radial.Sub is null;
            Brush fill = mainSel ? MakeBrush(accent, 0.9)
                       : active ? MakeBrush(accent, 0.35)
                       : MakeBrush(Tokens.Surface.MainIdle, 1.0);
            dc.PushOpacity(wp);
            FillWedge(dc, center, dead, main, i * 45.0, 22.5, fill, guide);
            DrawSectorIcon(dc, (RadialMenuItem)i, Polar(center, (dead + main) / 2, i * 45.0),
                Rgba.FromWhite(active ? 1.0 : 0.9));
            dc.Pop();
        }

        // (2) 활성 sector의 sub fan — 섹터 hover 즉시 펼침(맥). 선택/현재값/branch/leaf 색 + stagger.
        if (radial.Sector is { } sct)
        {
            var item = (RadialMenuItem)sct;
            var subs = item.SubItems();
            if (subs.Count > 0)
            {
                double subSpan = item.SubSpan(), subStep = subSpan / subs.Count, subStart = sct * 45.0 - subSpan / 2;
                for (int j = 0; j < subs.Count; j++)
                {
                    double sp = EaseOut(Clamp01((elSub - j * 0.06) / 0.24));
                    if (sp <= 0.001) continue;
                    double cw = subStart + subStep * (j + 0.5);
                    bool selSub = radial.Sub == j;
                    bool curSub = radial.SubActive is { } sa && j < sa.Count && sa[j];
                    bool branch = subs[j].IsBranch;
                    Brush fill = selSub ? MakeBrush(accent, 0.9)
                               : curSub ? MakeBrush(accent, 0.40)
                               : branch ? MakeBrush(accent, Tokens.Radial.BranchFillOpacity)
                               : MakeBrush(Tokens.Surface.Subtle, 1.0);
                    dc.PushOpacity(sp);
                    FillWedge(dc, center, main, subR, cw, subStep / 2, fill, guide);
                    DrawCenteredText(dc, subs[j].Label, Polar(center, (main + subR) / 2, cw),
                        (double)Tokens.Text.Caption.Size, Rgba.FromWhite(1.0), bold: selSub);
                    if (branch) DrawChevron(dc, center, subR - 9, cw);
                    dc.Pop();
                }

                // (3) 선택된 branch sub의 subSub fan — stagger
                if (radial.Sub is { } subI && subI < subs.Count && subs[subI].Children is { Count: > 0 } kids)
                {
                    double subCenter = RadialHitTest.SubCenterAngle(sct, subI, subSpan, subs.Count);
                    double ssSpan = item.SubSubSpan(subI), ssStep = ssSpan / kids.Count, ssStart = subCenter - ssSpan / 2;
                    for (int k = 0; k < kids.Count; k++)
                    {
                        double ssp = EaseOut(Clamp01((elSubSub - k * 0.06) / 0.24));
                        if (ssp <= 0.001) continue;
                        double cw = ssStart + ssStep * (k + 0.5);
                        bool selSS = radial.SubSub == k;
                        bool curSS = radial.SubSubActive is { } sa && k < sa.Count && sa[k];
                        Brush fill = selSS ? MakeBrush(accent, 0.9)
                                   : curSS ? MakeBrush(accent, 0.40)
                                   : MakeBrush(Tokens.Surface.Subtle, 1.0);
                        dc.PushOpacity(ssp);
                        FillWedge(dc, center, subR, subSubR, cw, ssStep / 2, fill, guide);
                        DrawCenteredText(dc, kids[k].Label, Polar(center, (subR + subSubR) / 2, cw),
                            (double)Tokens.Text.CaptionSmall.Size, Rgba.FromWhite(1.0), bold: selSS);
                        dc.Pop();
                    }
                }
            }
        }

        // (4) 중심 — 어두운 원 + 컨텍스트(라벨/현재값) 또는 dead zone "✕ 닫기" (맥의 가운데=close)
        dc.PushOpacity(EaseOut(Clamp01(el / 0.22)));
        dc.DrawEllipse(MakeBrush(Tokens.Surface.Veil, 1.0), null, center, dead, dead);
        if (radial.Sector is { } cs)
        {
            var item = (RadialMenuItem)cs;
            DrawCenteredText(dc, item.Label(), new Point(center.X, center.Y - 8),
                (double)Tokens.Text.Caption.Size, Rgba.FromWhite(0.95), bold: true);
            string detail = RadialDetail(radial, item);
            if (!string.IsNullOrEmpty(detail))
                DrawCenteredText(dc, detail, new Point(center.X, center.Y + 10),
                    (double)Tokens.Text.LabelTiny.Size, Rgba.FromWhite(0.60), bold: false);
        }
        else
        {
            DrawCenteredText(dc, "✕", new Point(center.X, center.Y - 7), 22, Rgba.FromWhite(0.9), bold: false);
            DrawCenteredText(dc, "닫기", new Point(center.X, center.Y + 13), (double)Tokens.Text.LabelTiny.Size, Rgba.FromWhite(0.6), bold: false);
        }
        dc.Pop();

        dc.Pop(); // scale transform
    }

    private static double Clamp01(double t) => Math.Clamp(t, 0, 1);
    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    /// <summary>셀프테스트용 — 라디얼 등장 애니메이션 시작 시점을 과거로 심어 정착 상태를 1회 렌더로 캡처.</summary>
    internal void DebugSeedRadial(double openAt, int? sector, int? sub)
    {
        _radialOpenAt = openAt; _radialSectorAt = openAt; _radialSubAt = openAt;
        _radialSeenSector = sector; _radialSeenSub = sub;
    }

    // 중앙에 표시할 상세 — sub/subSub hover면 그 라벨, 아니면 sector 현재값.
    private static string RadialDetail(RadialVisual radial, RadialMenuItem item)
    {
        var subs = item.SubItems();
        if (radial.Sub is { } subI && subI < subs.Count)
        {
            var subItem = subs[subI];
            if (radial.SubSub is { } ssI && subItem.Children is { } kids && ssI < kids.Count)
                return $"{subItem.Label} · {kids[ssI].Label}";
            return subItem.Label;
        }
        int sec = (int)item;
        return radial.CurrentValues is { } cv && sec < cv.Count ? cv[sec] : "";
    }

    // cwCenter±cwHalf(cw deg) 범위, r0..r1 채워진 부채꼴.
    private void FillWedge(DrawingContext dc, Point center, double r0, double r1, double cwCenter, double cwHalf, Brush fill, Pen stroke)
        => dc.DrawGeometry(fill, stroke, PieWedge(center, r0, r1, (cwCenter - cwHalf) - 90, (cwCenter + cwHalf) - 90));

    // 메인 sector 벡터 아이콘 (맥 SF Symbol 대응 — 폰트 tofu 회피용 직접 그림). p 중심, 약 ±9px.
    private void DrawSectorIcon(DrawingContext dc, RadialMenuItem item, Point p, Rgba color)
    {
        var pen = StrokePen(color, 1.7);
        var fill = MakeBrush(color, 1.0);
        const double u = 9;
        switch (item)
        {
            case RadialMenuItem.Spotlight: // 빛/타겟 — 원 + 중심점 + 광선
                dc.DrawEllipse(null, pen, p, u * 0.5, u * 0.5);
                dc.DrawEllipse(fill, null, p, u * 0.16, u * 0.16);
                for (int k = 0; k < 8; k++)
                {
                    double a = k * 45 * Math.PI / 180;
                    dc.DrawLine(pen, new Point(p.X + Math.Cos(a) * u * 0.72, p.Y + Math.Sin(a) * u * 0.72),
                        new Point(p.X + Math.Cos(a) * u, p.Y + Math.Sin(a) * u));
                }
                break;
            case RadialMenuItem.Magnifier: // 돋보기 — 원 + 손잡이
                dc.DrawEllipse(null, pen, new Point(p.X - u * 0.25, p.Y - u * 0.25), u * 0.55, u * 0.55);
                dc.DrawLine(pen, new Point(p.X + u * 0.2, p.Y + u * 0.2), new Point(p.X + u * 0.85, p.Y + u * 0.85));
                break;
            case RadialMenuItem.Glow: // 효과 — 4점 반짝임 + 작은 별
                Sparkle(dc, new Point(p.X - u * 0.15, p.Y - u * 0.1), u * 0.7, fill);
                Sparkle(dc, new Point(p.X + u * 0.55, p.Y + u * 0.5), u * 0.32, fill);
                break;
            case RadialMenuItem.RingSize: // 링 외형 — 이중 원
                dc.DrawEllipse(null, pen, p, u * 0.85, u * 0.85);
                dc.DrawEllipse(null, StrokePen(color, 1.2), p, u * 0.45, u * 0.45);
                break;
            case RadialMenuItem.Color: // 링 색 — 채운 원(스와치)
                dc.DrawEllipse(fill, null, p, u * 0.7, u * 0.7);
                break;
            case RadialMenuItem.RingShape: // 링 모양 — 사각 + 원 겹침
                dc.DrawRectangle(null, pen, new Rect(p.X - u * 0.85, p.Y - u * 0.55, u * 1.0, u * 1.0));
                dc.DrawEllipse(null, pen, new Point(p.X + u * 0.25, p.Y + u * 0.1), u * 0.5, u * 0.5);
                break;
            case RadialMenuItem.Inspector: // 좌표/각도 — 십자선
                dc.DrawLine(pen, new Point(p.X - u, p.Y), new Point(p.X + u, p.Y));
                dc.DrawLine(pen, new Point(p.X, p.Y - u), new Point(p.X, p.Y + u));
                dc.DrawEllipse(null, StrokePen(color, 1.2), p, u * 0.3, u * 0.3);
                break;
            case RadialMenuItem.Keystroke: // 키 입력 — 키캡(둥근 사각) + 점들
                dc.DrawRoundedRectangle(null, pen, new Rect(p.X - u, p.Y - u * 0.65, u * 2, u * 1.3), 2, 2);
                for (int k = -1; k <= 1; k++)
                    dc.DrawEllipse(fill, null, new Point(p.X + k * u * 0.5, p.Y), u * 0.12, u * 0.12);
                break;
        }
    }

    private void Sparkle(DrawingContext dc, Point p, double r, Brush fill)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(p.X, p.Y - r), true, true);
            ctx.LineTo(new Point(p.X + r * 0.3, p.Y - r * 0.3), true, false);
            ctx.LineTo(new Point(p.X + r, p.Y), true, false);
            ctx.LineTo(new Point(p.X + r * 0.3, p.Y + r * 0.3), true, false);
            ctx.LineTo(new Point(p.X, p.Y + r), true, false);
            ctx.LineTo(new Point(p.X - r * 0.3, p.Y + r * 0.3), true, false);
            ctx.LineTo(new Point(p.X - r, p.Y), true, false);
            ctx.LineTo(new Point(p.X - r * 0.3, p.Y - r * 0.3), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(fill, null, geo);
    }

    // branch sub 바깥에 외향 삼각형(펼침 가능 암시).
    private void DrawChevron(DrawingContext dc, Point center, double r, double cwDeg)
    {
        var tip = Polar(center, r + 6, cwDeg);
        var b1 = Polar(center, r, cwDeg - 4);
        var b2 = Polar(center, r, cwDeg + 4);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open()) { ctx.BeginFigure(tip, true, true); ctx.LineTo(b1, true, false); ctx.LineTo(b2, true, false); }
        geo.Freeze();
        dc.DrawGeometry(MakeBrush(Rgba.FromWhite(0.85), 1.0), null, geo);
    }

    // cw 각도(12시=0, 시계방향) + 반지름 → 화면 점. 화면각 β = cw - 90.
    private static Point Polar(Point center, double radius, double cwDeg)
    {
        double b = (cwDeg - 90.0) * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(b), center.Y + radius * Math.Sin(b));
    }

    private void DrawCenteredText(DrawingContext dc, string text, Point center, double size, Rgba color, bool bold)
    {
        double ppd = _monitor.DpiScale <= 0 ? 1.0 : _monitor.DpiScale;
        var tf = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
            bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf,
            size, MakeBrush(color, 1.0), ppd);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }

    private static Geometry PieWedge(Point c, double r0, double r1, double aLowDeg, double aHighDeg)
    {
        Point P(double r, double deg)
        {
            double a = deg * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(P(r0, aLowDeg), isFilled: true, isClosed: true);
            ctx.LineTo(P(r1, aLowDeg), true, false);
            ctx.ArcTo(P(r1, aHighDeg), new Size(r1, r1), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(P(r0, aHighDeg), true, false);
            ctx.ArcTo(P(r0, aLowDeg), new Size(r0, r0), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        geo.Freeze();
        return geo;
    }

    // ── 키스트로크 카드 (하단 중앙) ─────────────────────────────
    private void DrawKeystroke(DrawingContext dc, OverlayFrame f)
    {
        if (string.IsNullOrEmpty(f.Keystroke)) return;
        double ppd = _monitor.DpiScale <= 0 ? 1.0 : _monitor.DpiScale;
        var ft = new FormattedText(f.Keystroke, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            (double)Tokens.Text.Label.Size, MakeBrush(Rgba.FromWhite(0.95), 1.0), ppd);

        double padX = Tokens.Spacing.Lg, padY = Tokens.Spacing.Sm;
        double w = ft.Width + padX * 2, h = ft.Height + padY * 2;
        double x = (ActualWidth - w) / 2;
        double y = ActualHeight - h - 64; // 하단에서 살짝 위
        var rect = new Rect(x, y, w, h);
        dc.DrawRoundedRectangle(MakeBrush(Tokens.Surface.Panel, 1.0), null, rect, Tokens.Radius.Lg, Tokens.Radius.Lg);
        dc.DrawText(ft, new Point(x + padX, y + padY));
    }

    // ── 코브랜딩 워터마크 (비-순정일 때 회사명, 하단 우측) ──────
    private void DrawBranding(DrawingContext dc, OverlayFrame f)
    {
        var b = f.Branding;
        if (string.IsNullOrEmpty(b.CompanyName) || b.CompanyName == BrandingConfig.Default.CompanyName) return;
        // 로고 이미지는 후속(에셋 비트맵 로드) — 여기선 회사명 텍스트만.
        double ppd = _monitor.DpiScale <= 0 ? 1.0 : _monitor.DpiScale;
        var ft = new FormattedText(b.CompanyName, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), (double)Tokens.Text.Caption.Size, MakeBrush(b.AccentColor, 0.65), ppd);
        dc.DrawText(ft, new Point(ActualWidth - ft.Width - Tokens.Spacing.Lg, ActualHeight - ft.Height - Tokens.Spacing.Lg));
    }

    // ── 도형 그리기 헬퍼 ────────────────────────────────────────
    private Pen StrokePen(Rgba color, double width, double opacity = 1.0)
    {
        var pen = new Pen(MakeBrush(color, opacity), width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    private void DrawPolyline(DrawingContext dc, DrawingShape s, double width, double opacity)
    {
        if (s.Points.Count < 2)
        {
            dc.DrawEllipse(MakeBrush(s.Color, opacity), null, ToLocal(s.Points[0]), width / 2, width / 2);
            return;
        }
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(ToLocal(s.Points[0]), isFilled: false, isClosed: false);
            for (int i = 1; i < s.Points.Count; i++)
                ctx.LineTo(ToLocal(s.Points[i]), isStroked: true, isSmoothJoin: true);
        }
        geo.Freeze();
        dc.DrawGeometry(null, StrokePen(s.Color, width, opacity), geo);
    }

    private void DrawStraight(DrawingContext dc, DrawingShape s)
    {
        if (s.Points.Count < 2) return;
        dc.DrawLine(StrokePen(s.Color, s.LineWidth), ToLocal(s.Points[0]), ToLocal(s.Points[1]));
    }

    private void DrawArrow(DrawingContext dc, DrawingShape s)
    {
        if (s.Points.Count < 2) return;
        var a = ToLocal(s.Points[0]);
        var b = ToLocal(s.Points[1]);
        var pen = StrokePen(s.Color, s.LineWidth);
        dc.DrawLine(pen, a, b);
        double theta = Math.Atan2(b.Y - a.Y, b.X - a.X);
        double len = Tokens.Drawing.ArrowHeadLength, ang = Tokens.Drawing.ArrowHeadAngle;
        dc.DrawLine(pen, b, new Point(b.X - len * Math.Cos(theta - ang), b.Y - len * Math.Sin(theta - ang)));
        dc.DrawLine(pen, b, new Point(b.X - len * Math.Cos(theta + ang), b.Y - len * Math.Sin(theta + ang)));
    }

    private void DrawRect(DrawingContext dc, DrawingShape s)
    {
        if (s.Points.Count < 2) return;
        dc.DrawRectangle(null, StrokePen(s.Color, s.LineWidth), RectOf(ToLocal(s.Points[0]), ToLocal(s.Points[1])));
    }

    private void DrawOval(DrawingContext dc, DrawingShape s)
    {
        if (s.Points.Count < 2) return;
        var r = RectOf(ToLocal(s.Points[0]), ToLocal(s.Points[1]));
        dc.DrawEllipse(null, StrokePen(s.Color, s.LineWidth),
            new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
    }

    private void DrawBadge(DrawingContext dc, DrawingShape s)
    {
        var center = ToLocal(s.Points[0]);
        double r = Tokens.Drawing.BadgeRadius;
        dc.DrawEllipse(MakeBrush(s.Color, 1.0),
            StrokePen(Rgba.FromWhite(0.9), Tokens.Drawing.BadgeBorderWidth), center, r, r);
        var label = (s.BadgeNumber ?? 0).ToString(CultureInfo.InvariantCulture);
        double ppd = _monitor.DpiScale <= 0 ? 1.0 : _monitor.DpiScale;
        var textColor = s.Color.NeedsDarkText ? Rgba.FromBlack(0.9) : Rgba.FromWhite(0.95);
        var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), Tokens.Drawing.BadgeFontSize, MakeBrush(textColor, 1.0), ppd);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }

    private static Rect RectOf(Point a, Point b)
        => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Brush MakeBrush(Rgba c, double opacityMul)
    {
        byte a = (byte)Math.Clamp(c.A * opacityMul, 0, 255);
        var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
