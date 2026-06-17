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
            // 배경 타일 — 둥근 사각형 + 시안→블루 대각 그라디언트. 꽉 찬 컬러라 밝은/어두운 작업표시줄·
            // 트레이 어디서나 또렷한 실루엣. (투명 링은 잘 안 보여서 교체)
            double pad = Math.Max(0.5, sz * 0.055);
            var rect = new Rect(pad, pad, sz - 2 * pad, sz - 2 * pad);
            double corner = (sz - 2 * pad) * 0.27;
            var bg = new LinearGradientBrush(
                Color.FromRgb(0x10, 0xC8, 0xEC), Color.FromRgb(0x16, 0x57, 0xE6),
                new Point(0, 0), new Point(1, 1));
            bg.Freeze();
            dc.DrawRoundedRectangle(bg, null, rect, corner, corner);

            // 흰 커서 링 + 중심 점 — 컬러 타일 위에서 고대비.
            double r = sz * 0.26;
            double t = Math.Max(1.6, sz * 0.105);
            var white = new SolidColorBrush(Colors.White); white.Freeze();
            var pen = new Pen(white, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawEllipse(null, pen, new Point(c, c), r, r);
            dc.DrawEllipse(white, null, new Point(c, c), Math.Max(1.0, sz * 0.055), Math.Max(1.0, sz * 0.055));
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
