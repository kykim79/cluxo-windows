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
