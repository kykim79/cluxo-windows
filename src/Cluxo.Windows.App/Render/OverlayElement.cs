using System.Windows;
using System.Windows.Media;
using Cluxo.Core;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Render;

/// <summary>
/// 한 모니터 오버레이의 WPF 그리기 표면. 불변 <see cref="OverlayFrame"/>을 받아 OnRender에서 그린다.
/// 좌표는 물리 픽셀(가상 데스크톱) → 로컬 DIP 변환(OVERLAY-RENDER.md §4): local = (phys - origin)/dpiScale.
///
/// 스톱갭 범위(슬라이스1): 커서 링 + 클릭 ripple. 트레일·스크롤·그리기 도형·라디얼·키스트로크·브랜딩은
/// 후속(D2D 교체 또는 WPF 확장)에서. 효과 progress는 효과 Id의 첫 등장 tick으로 계산(§5.1).
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

        DrawClicks(dc, f);
        DrawRing(dc, f);
    }

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
            double radius = 12 + progress * 40;                 // 바깥으로 퍼짐
            double alpha = (1 - progress) * (c.IsRight ? 0.5 : 0.75); // fade out
            var color = f.Ring?.Color ?? new Rgba(0, 230, 255);
            var pen = new Pen(MakeBrush(color, alpha), c.IsDouble ? 4 : 2);
            pen.Freeze();
            dc.DrawEllipse(null, pen, ToLocal(c.Position), radius, radius);
        }

        // Prune된 효과의 firstSeen 정리
        if (_clickFirstSeen.Count > live.Count)
            foreach (var id in _clickFirstSeen.Keys.Where(id => !live.Contains(id)).ToList())
                _clickFirstSeen.Remove(id);
    }

    private void DrawRing(DrawingContext dc, OverlayFrame f)
    {
        if (f.Ring is not { } ring || f.CursorPosition is not { } cursor) return;
        double r = ring.Radius * ring.Scale;
        var pen = new Pen(MakeBrush(ring.Color, ring.Opacity), 3);
        pen.Freeze();
        dc.DrawEllipse(null, pen, ToLocal(cursor), r, r);
    }

    private static Brush MakeBrush(Rgba c, double opacityMul)
    {
        byte a = (byte)Math.Clamp(c.A * opacityMul, 0, 255);
        var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
