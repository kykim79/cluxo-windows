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
        double sz = size;
        double c = sz / 2.0;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // 배경 타일 — 검은 둥근 사각형(맥 아이콘과 동일). 꽉 찬 어두운 실루엣이라 어디서나 또렷.
            double pad = Math.Max(0.5, sz * 0.055);
            var rect = new Rect(pad, pad, sz - 2 * pad, sz - 2 * pad);
            double corner = (sz - 2 * pad) * 0.27;
            var bg = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x0F)); bg.Freeze();
            dc.DrawRoundedRectangle(bg, null, rect, corner, corner);

            // 노란(시스템 yellow) 커서 링 + 흰 중심 점 — 맥 아이콘과 동일.
            double r = sz * 0.27;
            double t = Math.Max(1.6, sz * 0.11);
            var yellow = new SolidColorBrush(Color.FromRgb(255, 204, 0)); yellow.Freeze();
            var pen = new Pen(yellow, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawEllipse(null, pen, new Point(c, c), r, r);
            var white = new SolidColorBrush(Colors.White); white.Freeze();
            dc.DrawEllipse(white, null, new Point(c, c), Math.Max(1.0, sz * 0.06), Math.Max(1.0, sz * 0.06));
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
