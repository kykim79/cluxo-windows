using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cluxo.Windows.App.Ui;

/// <summary>
/// Cluxo 앱 아이콘 생성기(--make-icon) — 외부 에셋 없이 WPF 렌더로 사이안 링 글리프를 여러 크기로
/// 그려 멀티프레임 .ico(PNG 임베드)를 쓴다. 한 번 생성해 Assets\cluxo.ico로 커밋.
/// </summary>
internal static class IconMaker
{
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

    public static void Make(string outPath)
    {
        var frames = new List<byte[]>();
        foreach (var s in Sizes) frames.Add(RenderPng(s));

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        using var fs = File.Create(outPath);
        using var w = new BinaryWriter(fs);

        // ICONDIR
        w.Write((ushort)0);              // reserved
        w.Write((ushort)1);              // type = icon
        w.Write((ushort)frames.Count);   // count

        int offset = 6 + frames.Count * 16;
        for (int i = 0; i < frames.Count; i++)
        {
            int sz = Sizes[i];
            w.Write((byte)(sz >= 256 ? 0 : sz)); // width (0 = 256)
            w.Write((byte)(sz >= 256 ? 0 : sz)); // height
            w.Write((byte)0);            // color count
            w.Write((byte)0);            // reserved
            w.Write((ushort)1);          // planes
            w.Write((ushort)32);         // bit count
            w.Write(frames[i].Length);   // bytes in res
            w.Write(offset);             // image offset
            offset += frames[i].Length;
        }
        foreach (var f in frames) w.Write(f);
    }

    private static byte[] RenderPng(int size)
    {
        var cyan = Color.FromRgb(0, 230, 255);
        double c = size / 2.0;
        double r = size * 0.34;
        double t = Math.Max(1.5, size * 0.14);

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // 큰 크기에선 은은한 글로우
            if (size >= 48)
            {
                var glow = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5,
                };
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(70, cyan.R, cyan.G, cyan.B), 0));
                glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, cyan.R, cyan.G, cyan.B), 1));
                glow.Freeze();
                dc.DrawEllipse(glow, null, new Point(c, c), c, c);
            }
            var pen = new Pen(new SolidColorBrush(cyan), t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawEllipse(null, pen, new Point(c, c), r, r);
            dc.DrawEllipse(new SolidColorBrush(cyan), null, new Point(c, c), Math.Max(1.0, size * 0.06), Math.Max(1.0, size * 0.06));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
