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
/// 스톱갭 범위: 커서 링 + 클릭 ripple + 그리기 도형(pen·line·arrow·rect·ellipse·highlighter·badge).
/// 트레일·스크롤·라디얼·키스트로크·브랜딩은 후속. 효과 progress는 효과 Id의 첫 등장 tick(§5.1).
/// 진행 중 stroke는 코디네이터가 EndShape 후에만 프레임에 담으므로 커밋된 도형만 그린다.
/// </summary>
internal sealed class OverlayElement : FrameworkElement
{
    private const double ClickLife = 0.7; // EffectsState.ClickLife 미러

    private readonly MonitorInfo _monitor;
    private readonly Func<double> _clock;
    private readonly Dictionary<int, double> _clickFirstSeen = new();
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

    protected override void OnRender(DrawingContext dc)
    {
        if (_frame is not { } f) return;

        DrawClicks(dc, f);     // 뒤 (퍼지는 ring)
        DrawRing(dc, f);       // 커서 링
        DrawShapes(dc, f);     // 앞 (annotation 도형)
    }

    // ── 클릭 ripple ─────────────────────────────────────────────
    private void DrawClicks(DrawingContext dc, OverlayFrame f)
    {
        var live = new HashSet<int>();
        foreach (var c in f.Effects.Clicks)
        {
            live.Add(c.Id);
            if (!_clickFirstSeen.TryGetValue(c.Id, out var t0))
            {
                t0 = _now;
                _clickFirstSeen[c.Id] = t0;
            }
            double progress = Math.Clamp((_now - t0) / ClickLife, 0, 1);
            double radius = 12 + progress * 40;
            double alpha = (1 - progress) * (c.IsRight ? 0.5 : 0.75);
            var color = f.Ring?.Color ?? new Rgba(0, 230, 255);
            var pen = new Pen(MakeBrush(color, alpha), c.IsDouble ? 4 : 2);
            pen.Freeze();
            dc.DrawEllipse(null, pen, ToLocal(c.Position), radius, radius);
        }

        if (_clickFirstSeen.Count > live.Count)
            foreach (var id in _clickFirstSeen.Keys.Where(id => !live.Contains(id)).ToList())
                _clickFirstSeen.Remove(id);
    }

    // ── 커서 링 ─────────────────────────────────────────────────
    private void DrawRing(DrawingContext dc, OverlayFrame f)
    {
        if (f.Ring is not { } ring || f.CursorPosition is not { } cursor) return;
        double r = ring.Radius * ring.Scale;
        var pen = new Pen(MakeBrush(ring.Color, ring.Opacity), 3);
        pen.Freeze();
        dc.DrawEllipse(null, pen, ToLocal(cursor), r, r);
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
            // 단일 점 — 작은 점 찍기(가시성)
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
        double len = Tokens.Drawing.ArrowHeadLength;
        double ang = Tokens.Drawing.ArrowHeadAngle;
        var p1 = new Point(b.X - len * Math.Cos(theta - ang), b.Y - len * Math.Sin(theta - ang));
        var p2 = new Point(b.X - len * Math.Cos(theta + ang), b.Y - len * Math.Sin(theta + ang));
        dc.DrawLine(pen, b, p1);
        dc.DrawLine(pen, b, p2);
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
