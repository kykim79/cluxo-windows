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
    private OverlayFrame? _frame;
    private double _now;

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
        InvalidateVisual();
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
        double r = ring.Radius * ring.Scale;
        var c = ToLocal(cursor);
        var pen = RingPen(ring.Color, ring.BorderWidth, ring.Opacity, ring.Dashed);

        switch (ring.Shape)
        {
            case RingShape.Squircle:
                double cr = r * 0.45; // 둥근 모서리
                dc.DrawRoundedRectangle(null, pen, new Rect(c.X - r, c.Y - r, r * 2, r * 2), cr, cr);
                break;
            case RingShape.Rhombus:
                dc.DrawGeometry(null, pen, Polygon(c, r, 4)); // 4각(꼭짓점 위) = 마름모
                break;
            case RingShape.Hexagon:
                dc.DrawGeometry(null, pen, Polygon(c, r, 6));
                break;
            default: // Circle
                dc.DrawEllipse(null, pen, c, r, r);
                break;
        }
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

    // 중심 기준 정n각형 외곽(첫 꼭짓점 위, 12시). 화면 좌표.
    private static Geometry Polygon(Point center, double r, int n)
    {
        Point P(int i)
        {
            double a = (-90.0 + i * 360.0 / n) * Math.PI / 180.0;
            return new Point(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a));
        }
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(P(0), isFilled: false, isClosed: true);
            for (int i = 1; i < n; i++) ctx.LineTo(P(i), isStroked: true, isSmoothJoin: false);
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

    // ── 라디얼 메뉴 (기본 wedge) ────────────────────────────────
    private void DrawRadial(DrawingContext dc, OverlayFrame f, Rgba accent)
    {
        if (f.Radial is not { } radial || !radial.Visible) return;
        var center = ToLocal(radial.Center);
        double dead = Tokens.Radial.DeadRadius, main = Tokens.Radial.MainOuter;
        double sub = Tokens.Radial.SubOuter, subSub = Tokens.Radial.SubSubOuter;

        // 활성 깊이까지 veil 배경 + 가이드 원 (라벨 가독성).
        double outer = radial.SubSub is { } ? subSub : radial.Sub is { } ? sub : main;
        dc.DrawEllipse(MakeBrush(Tokens.Surface.Veil, 1.0), StrokePen(Rgba.FromWhite(0.22), 1.5, 1.0), center, outer, outer);
        dc.DrawEllipse(null, StrokePen(Rgba.FromWhite(0.25), 1.5, 1.0), center, dead, dead);
        if (radial.Sub is { }) dc.DrawEllipse(null, StrokePen(Rgba.FromWhite(0.15), 1.0, 1.0), center, main, main);

        // 선택 sector wedge 강조. dy 반전 후 화면각 β = cw - 90 (cw: 12시=0, 시계방향).
        if (radial.Sector is { } sel)
            dc.DrawGeometry(MakeBrush(accent, 0.30), StrokePen(accent, 2, 0.9),
                PieWedge(center, dead, main, sel * 45.0 - 112.5, sel * 45.0 - 67.5));

        // (1) 8개 메인 sector 라벨 — cw = i*45.
        double mainR = (dead + main) / 2;
        for (int i = 0; i < 8; i++)
        {
            bool selected = radial.Sector == i;
            DrawCenteredText(dc, ((RadialMenuItem)i).Label(), Polar(center, mainR, i * 45.0),
                (double)Tokens.Text.CaptionSmall.Size,
                selected ? Rgba.FromWhite(0.98) : Rgba.FromWhite(0.45), bold: selected);
        }

        // (2) 잠긴 sector의 sub fan — cursor가 sub ring 이상이면(Sub 선택됨).
        if (radial.Sector is { } sct && radial.Sub is { })
        {
            var item = (RadialMenuItem)sct;
            var subs = item.SubItems();
            double subSpan = item.SubSpan();
            DrawFan(dc, center, (main + sub) / 2, centerCw: sct * 45.0, span: subSpan,
                Labels(subs), radial.Sub, accent);

            // (3) branch sub의 subSub fan.
            if (radial.SubSub is { } && radial.Sub is { } subIdx
                && subIdx < subs.Count && subs[subIdx].Children is { Count: > 0 } kids)
            {
                double subCenter = RadialHitTest.SubCenterAngle(sct, subIdx, subSpan, subs.Count);
                DrawFan(dc, center, (sub + subSub) / 2, subCenter, item.SubSubSpan(subIdx),
                    Labels(kids), radial.SubSub, accent);
            }
        }

        // 중앙: 선택 sector 라벨(없으면 힌트).
        string centerText = radial.Sector is { } cs ? ((RadialMenuItem)cs).Label() : "···";
        DrawCenteredText(dc, centerText, center, (double)Tokens.Text.Label.Size, accent, bold: true);
    }

    // centerCw 기준 span을 count칸으로 펼친 라벨 fan. (RadialHitTest.FanIndex와 동일 기하)
    private void DrawFan(DrawingContext dc, Point center, double radius, double centerCw, double span,
        IReadOnlyList<string> labels, int? selected, Rgba accent)
    {
        int n = labels.Count;
        if (n == 0) return;
        double step = span / n, start = centerCw - span / 2;
        for (int i = 0; i < n; i++)
        {
            double cw = start + step * (i + 0.5);
            bool sel = selected == i;
            DrawCenteredText(dc, labels[i], Polar(center, radius, cw), (double)Tokens.Text.Caption.Size,
                sel ? accent : Rgba.FromWhite(0.55), bold: sel);
        }
    }

    private static IReadOnlyList<string> Labels(IReadOnlyList<RadialSubItem> items)
    {
        var r = new string[items.Count];
        for (int i = 0; i < items.Count; i++) r[i] = items[i].Label;
        return r;
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
