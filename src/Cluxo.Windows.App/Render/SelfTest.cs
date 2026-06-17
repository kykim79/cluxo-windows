using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cluxo.Core;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Render;

/// <summary>
/// 개발/QA 셀프테스트(<c>--selftest</c>) — OS 입력 합성이 막힌 환경에서 렌더·토글을 검증한다.
/// (1) 모든 그리기 도형을 <see cref="RenderTargetBitmap"/>으로 PNG에 그리고,
/// (2) 클릭통과 토글(WS_EX_TRANSPARENT, P1)을 확인한다.
/// 결과를 %TEMP%\cluxo-selftest.(png|txt)에 쓰고, 모두 통과면 0을 반환한다. STA 스레드에서 호출.
/// </summary>
internal static class SelfTest
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    private static bool HasTransparent(IntPtr h) => ((long)GetWindowLongPtr(h, -20) & 0x20) != 0;

    public static int Run()
    {
        var monitor = new MonitorInfo("M", new RectD(0, 0, 500, 200), 1.0, true);
        string png = Path.Combine(Path.GetTempPath(), "cluxo-selftest.png");
        string txt = Path.Combine(Path.GetTempPath(), "cluxo-selftest.txt");

        // (1) 도형 렌더 → PNG (회색 배경 위에 그려 가시성 확보)
        var el = new OverlayElement(monitor, () => 0.0);
        el.SetFrame(BuildShapeFrame());
        var border = new Border
        {
            Width = 500,
            Height = 200,
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            Child = el,
        };
        border.Measure(new Size(500, 200));
        border.Arrange(new Rect(0, 0, 500, 200));
        border.UpdateLayout();

        var rtb = new RenderTargetBitmap(500, 200, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(border);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(png)) enc.Save(fs);

        // (2) 클릭통과 토글 (P1)
        var w = new OverlayWindow(monitor, () => 0.0);
        w.Show();
        var hwnd = new WindowInteropHelper(w).Handle;
        bool normal = HasTransparent(hwnd);                                 // 평소: 통과 ON
        w.SetClickThrough(false); bool drawing = HasTransparent(hwnd);      // 그리기/라디얼: OFF
        w.SetClickThrough(true); bool restored = HasTransparent(hwnd);      // 복원: ON
        w.Close();

        bool toggleOk = normal && !drawing && restored;
        File.WriteAllText(txt,
            $"png={png}\n" +
            $"transparent_normal={normal}\n" +
            $"transparent_drawing={drawing}\n" +
            $"transparent_restored={restored}\n" +
            $"toggle_ok={toggleOk}\n");
        return toggleOk ? 0 : 1;
    }

    /// <summary>효과(트레일·스크롤·흔들기·더블클릭·클릭·링·드래그·라디얼·키스트로크·브랜딩) 렌더 → PNG.</summary>
    public static int RunFx()
    {
        double now = 0;
        var monitor = new MonitorInfo("M", new RectD(0, 0, 700, 400), 1.0, true);
        var el = new OverlayElement(monitor, () => now);
        el.SetFrame(BuildFxFrame());

        var border = new Border { Width = 700, Height = 400, Background = new SolidColorBrush(Color.FromRgb(35, 35, 40)), Child = el };
        border.Measure(new Size(700, 400));
        border.Arrange(new Rect(0, 0, 700, 400));
        border.UpdateLayout();

        var rtb = new RenderTargetBitmap(700, 400, 96, 96, PixelFormats.Pbgra32);
        // 1차 렌더로 효과 firstSeen을 now=0에 고정, 2차에서 now=0.4 → 중간 애니메이션(progress≈0.5) 캡처.
        now = 0.0; el.SetFrame(BuildFxFrame()); rtb.Render(border);
        now = 0.4; el.SetFrame(BuildFxFrame()); rtb.Render(border);

        string png = Path.Combine(Path.GetTempPath(), "cluxo-selftest-fx.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(png)) enc.Save(fs);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "cluxo-selftest-fx.txt"), $"png={png}\n");
        return 0;
    }

    private sealed class StubLaunch : ILaunchAtLogin { public bool IsEnabled { get; set; } }

    /// <summary>링 모양(원·둥근사각·마름모·육각) + 점선 렌더 → PNG.</summary>
    /// <summary>그리기 툴바 렌더 → PNG. ToolbarVisual을 직접 구성해 패널·도구·두께·색을 검증.</summary>
    public static int RunToolbar()
    {
        const int w = 800, h = 140;
        var accent = new Rgba(0, 230, 255);
        var tools = new[] { DrawingTool.Pen, DrawingTool.Line, DrawingTool.Arrow, DrawingTool.Rectangle,
            DrawingTool.Ellipse, DrawingTool.Highlighter, DrawingTool.Badge };
        const double toolD = 34, toolGap = 7, thickHit = 22, thickGap = 4, colorHit = 22, colorGap = 4, groupGap = 18, closeD = 30, pad = 14;
        var steps = new double[] { 2, 4, 6, 10, 14 };
        var colors = new[] { RingColor.Yellow, RingColor.Red, RingColor.Blue, RingColor.Green, RingColor.Cyan, RingColor.Purple, RingColor.White };

        double toolsW = tools.Length * toolD + (tools.Length - 1) * toolGap;
        double thickW = steps.Length * thickHit + (steps.Length - 1) * thickGap;
        double colorW = colors.Length * colorHit + (colors.Length - 1) * colorGap;
        double panelW = toolsW + groupGap + thickW + groupGap + colorW + groupGap + closeD + pad * 2;
        double panelH = toolD + pad * 2;
        double left = (w - panelW) / 2, top = (h - panelH) / 2 + 8, cy = top + panelH / 2, x = left + pad;

        var toolItems = new List<ToolbarItem>();
        foreach (var t in tools)
        {
            toolItems.Add(new ToolbarItem(new RectD(x, cy - toolD / 2, toolD, toolD), t == DrawingTool.Arrow, t == DrawingTool.Arrow, default, 0, t));
            x += toolD + toolGap;
        }
        x += groupGap - toolGap;
        var thickItems = new List<ToolbarItem>();
        foreach (var sw in steps)
        {
            thickItems.Add(new ToolbarItem(new RectD(x, cy - thickHit / 2, thickHit, thickHit), sw == 6, sw == 6, default, sw, default));
            x += thickHit + thickGap;
        }
        x += groupGap - thickGap;
        var colorItems = new List<ToolbarItem>();
        foreach (var c in colors)
        {
            colorItems.Add(new ToolbarItem(new RectD(x, cy - colorHit / 2, colorHit, colorHit), c == RingColor.Cyan, c == RingColor.Cyan, c.Color(), 0, default));
            x += colorHit + colorGap;
        }
        x += groupGap - colorGap;
        var close = new RectD(x, cy - 15, 30, 30);
        var tb = new ToolbarVisual(new RectD(left, top, panelW, panelH), accent,
            "화살표 · 드래그하여 그리기 · ESC 종료", toolItems, thickItems, colorItems, close);

        var mon = new MonitorInfo("M", new RectD(0, 0, w, h), 1.0, true);
        var el = new OverlayElement(mon, () => 0.0) { Width = w, Height = h };
        el.SetFrame(new OverlayFrame("M", null, null, Array.Empty<DrawingShape>(), BrandingConfig.Default, OverlayEffects.Empty, Toolbar: tb));

        var canvas = new Canvas { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(40, 40, 48)) };
        canvas.Children.Add(el);
        canvas.Measure(new Size(w, h)); canvas.Arrange(new Rect(0, 0, w, h)); canvas.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(canvas);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(Path.Combine(Path.GetTempPath(), "cluxo-selftest-toolbar.png"))) enc.Save(fs);
        return 0;
    }

    /// <summary>스포트라이트 렌더 → PNG. 밝은 배경 위에 디밍 + 커서 주변 구멍을 확인.</summary>
    public static int RunSpotlight()
    {
        const int w = 600, h = 360;
        var monitor = new MonitorInfo("M", new RectD(0, 0, w, h), 1.0, true);
        var el = new OverlayElement(monitor, () => 0.0) { Width = w, Height = h };
        el.SetFrame(new OverlayFrame("M", new PointD(w / 2.0, h / 2.0),
            new RingVisual(new Rgba(0, 230, 255), 27, 1.0, 1.0),
            Array.Empty<DrawingShape>(), BrandingConfig.Default, OverlayEffects.Empty,
            Spotlight: new SpotlightVisual(110, 0.5)));

        // 밝은 그라디언트 배경(디밍이 보이도록) + 오버레이.
        var bgBrush = new LinearGradientBrush(Color.FromRgb(0xFF, 0xE7, 0xB0), Color.FromRgb(0x9B, 0xD8, 0xFF), 30);
        var stack = new Grid();
        stack.Children.Add(new System.Windows.Shapes.Rectangle { Fill = bgBrush });
        for (int gx = 40; gx < w; gx += 80)
            stack.Children.Add(new System.Windows.Shapes.Rectangle { Width = 40, Height = h, Fill = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(gx, 0, 0, 0) });
        stack.Children.Add(el);

        var border = new Border { Width = w, Height = h, Child = stack };
        border.Measure(new Size(w, h)); border.Arrange(new Rect(0, 0, w, h)); border.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(border);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(Path.Combine(Path.GetTempPath(), "cluxo-selftest-spotlight.png"))) enc.Save(fs);
        return 0;
    }

    public static int RunRings()
    {
        const int cell = 130, h = 190;
        var cyan = new Rgba(0, 230, 255);
        // shape, dashed, glow, innerRing, fill, stretchX, stretchY, stretchAngle, name
        var variants = new (RingShape shape, bool dashed, bool glow, bool inner, bool fill,
            double sx, double sy, double sAng, string name)[]
        {
            (RingShape.Circle,   false, false, false, false, 1, 1, 0, "원형"),
            (RingShape.Squircle, false, false, false, false, 1, 1, 0, "둥근 사각형"),
            (RingShape.Rhombus,  false, false, false, false, 1, 1, 0, "둥근 마름모"),
            (RingShape.Hexagon,  false, false, false, false, 1, 1, 0, "둥근 육각형"),
            (RingShape.Circle,   false, false, true,  false, 1, 1, 0, "이중링"),
            (RingShape.Circle,   false, false, false, true,  1, 1, 0, "채우기"),
            (RingShape.Hexagon,  false, false, true,  true,  1, 1, 0, "이중+채우기"),
            (RingShape.Circle,   false, true,  false, true,  1.45, 0.78, 35, "드래그 stretch"),
        };
        int w = cell * variants.Length;
        var canvas = new Canvas { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(32, 32, 38)) };

        for (int i = 0; i < variants.Length; i++)
        {
            var (shape, dashed, glow, inner, fill, sx, sy, sAng, name) = variants[i];
            var mon = new MonitorInfo("M", new RectD(i * cell, 0, cell, h), 1.0, true);
            var el = new OverlayElement(mon, () => 0.0) { Width = cell, Height = h };
            var ring = new RingVisual(cyan, 42, 1.0, 1.0, shape, 4.0, dashed, glow, inner, fill, sx, sy, sAng);
            el.SetFrame(new OverlayFrame("M", new PointD(i * cell + cell / 2.0, 80), ring,
                Array.Empty<DrawingShape>(), BrandingConfig.Default, OverlayEffects.Empty));
            Canvas.SetLeft(el, i * cell);
            canvas.Children.Add(el);

            var label = new TextBlock
            {
                Text = name, Foreground = Brushes.White, FontSize = 12, Width = cell, TextAlignment = TextAlignment.Center,
            };
            Canvas.SetLeft(label, i * cell);
            Canvas.SetTop(label, 150);
            canvas.Children.Add(label);
        }

        canvas.Measure(new Size(w, h));
        canvas.Arrange(new Rect(0, 0, w, h));
        canvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(canvas);
        string png = Path.Combine(Path.GetTempPath(), "cluxo-selftest-rings.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(png)) enc.Save(fs);
        return 0;
    }

    /// <summary>설정창 패널 렌더 → PNG (Window 부모 없이 BuildPanel을 흰 배경에 그린다).</summary>
    public static int RunSettings()
    {
        var settings = new CursorSettings(new JsonSettingsStore());
        // 스크롤바가 뜬 실제 창의 가용 폭(≈ 460 client - 17 scrollbar)을 시뮬레이션 — 잘림 검증.
        const double w = 425;
        var panel = Ui.SettingsWindow.BuildPanel(settings, new StubLaunch());
        var border = new Border { Width = w, Height = 760, Background = Brushes.White, Child = panel };
        border.Measure(new Size(w, 760));
        border.Arrange(new Rect(0, 0, w, 760));
        border.UpdateLayout();

        var rtb = new RenderTargetBitmap((int)w, 760, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(border);

        string png = Path.Combine(Path.GetTempPath(), "cluxo-selftest-settings.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(png)) enc.Save(fs);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "cluxo-selftest-settings.txt"), $"png={png}\n");
        return 0;
    }

    /// <summary>라디얼 메뉴(main + sub + subSub fan 라벨) 렌더 → PNG. Spotlight→반경→140pt 선택 경로.</summary>
    public static int RunRadial()
    {
        const int S = 520;
        double now = 1.5; // 애니메이션 완료 후 시점
        var monitor = new MonitorInfo("M", new RectD(0, 0, S, S), 1.0, true);
        var el = new OverlayElement(monitor, () => now);
        el.DebugSeedRadial(openAt: 0.0, sector: 0, sub: 1); // 0초에 열린 것으로 심음 → now=1.5면 정착

        var values = new[] { "켜짐 · 130pt", "꺼짐", "2/4 켜짐", "보통 (54pt)", "하늘색", "원형", "1/2 켜짐", "꺼짐" };
        var radial = new RadialVisual(Visible: true, Center: new PointD(S / 2.0, S / 2.0),
            Sector: 0, Sub: 1, SubSub: 2, // Spotlight → 반경(branch) → 140pt
            CurrentValues: values, SubActive: new[] { true, false, false }, SubSubActive: new[] { false, false, true, false, false });
        var frame = new OverlayFrame("M", null, null, Array.Empty<DrawingShape>(),
            BrandingConfig.Default, OverlayEffects.Empty, null, null, radial);
        el.SetFrame(frame);

        var border = new Border { Width = S, Height = S, Background = new SolidColorBrush(Color.FromRgb(30, 30, 34)), Child = el };
        border.Measure(new Size(S, S));
        border.Arrange(new Rect(0, 0, S, S));
        border.UpdateLayout();

        var rtb = new RenderTargetBitmap(S, S, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(border);

        string png = Path.Combine(Path.GetTempPath(), "cluxo-selftest-radial.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(png)) enc.Save(fs);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "cluxo-selftest-radial.txt"), $"png={png}\n");
        return 0;
    }

    private static OverlayFrame BuildFxFrame()
    {
        var accent = new Rgba(0, 230, 255);
        var ring = new RingVisual(accent, 27, 1.0, 1.0, RingShape.Hexagon); // 효과가 모양 따라가는지 검증

        var trail = new List<TrailPoint>();
        for (int i = 0; i < 12; i++)
            trail.Add(new TrailPoint(100 + i, new PointD(60 + i * 14, 210 - Math.Sin(i * 0.5) * 18)));

        var effects = new OverlayEffects(
            Clicks: new[] { new ClickEffect(1, new PointD(250, 110), false, false, 0.8) },
            DoubleClicks: new[] { new DoubleClickEffect(2, new PointD(380, 110), 0.8) },
            Scrolls: new[] { new ScrollEffect(3, new PointD(630, 120), true, true, 3, 0.8) },
            Shakes: new[] { new ShakeEffect(4, new PointD(510, 110), 0.8) },
            IdlePulses: Array.Empty<IdlePulseEffect>(),
            Trail: trail,
            DragTrail: Array.Empty<TrailPoint>());

        var drag = new DragVisual(new PointD(300, 250), new PointD(440, 300), AnchoredLineVisible: true, Velocity: 800, Angle: 0.3, ShowAngleLabel: true);
        var radial = new RadialVisual(Visible: true, Center: new PointD(540, 210), Sector: 0, Sub: null, SubSub: null);
        var branding = new BrandingConfig { CompanyName = "Acme Corp", AccentColor = new Rgba(255, 150, 40) };

        return new OverlayFrame("M", new PointD(120, 110), ring, Array.Empty<DrawingShape>(),
            branding, effects, "Ctrl+Alt+D", drag, radial, Inspector: true, RingShape: RingShape.Hexagon);
    }

    private static OverlayFrame BuildShapeFrame()
    {
        var shapes = new List<DrawingShape>();

        var pen = new DrawingShape(DrawingTool.Pen, new Rgba(0, 230, 255), 4, new PointD(20, 40));
        pen.Points.Add(new PointD(120, 70)); pen.Points.Add(new PointD(220, 30));
        shapes.Add(pen);

        var line = new DrawingShape(DrawingTool.Line, new Rgba(255, 77, 77), 4, new PointD(20, 110));
        line.Points.Add(new PointD(220, 150)); shapes.Add(line);

        var arrow = new DrawingShape(DrawingTool.Arrow, new Rgba(77, 255, 128), 4, new PointD(260, 40));
        arrow.Points.Add(new PointD(430, 120)); shapes.Add(arrow);

        var rect = new DrawingShape(DrawingTool.Rectangle, new Rgba(255, 204, 0), 3, new PointD(260, 130));
        rect.Points.Add(new PointD(360, 185)); shapes.Add(rect);

        var ell = new DrawingShape(DrawingTool.Ellipse, new Rgba(204, 77, 255), 3, new PointD(370, 130));
        ell.Points.Add(new PointD(430, 185)); shapes.Add(ell);

        shapes.Add(new DrawingShape(DrawingTool.Badge, new Rgba(0, 122, 255), 3, new PointD(470, 40), 1));

        return new OverlayFrame("M", null, null, shapes, BrandingConfig.Default, OverlayEffects.Empty);
    }
}
