using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Cluxo.Windows.App.Render;

/// <summary>
/// 화면 영역 GDI BitBlt 캡처 → WPF <see cref="BitmapSource"/>. 돋보기 렌즈용.
///
/// CAPTUREBLT를 쓰지 않으므로 위에 떠 있는 <b>레이어드 윈도우(우리 투명 오버레이)는 캡처에서 제외</b>된다
/// → 렌즈가 자기 자신을 재귀로 비추지 않는다. 좌표는 가상 데스크톱(물리 픽셀, 주 모니터 원점 0,0).
/// </summary>
internal static class ScreenCapture
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);

    private const int SRCCOPY = 0x00CC0020;

    /// <summary>(x,y)에서 w×h 영역을 캡처. 실패 시 null. 결과는 Frozen.</summary>
    public static BitmapSource? CaptureRegion(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) return null;
        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        BitmapSource? result = null;
        try
        {
            if (BitBlt(mem, 0, 0, w, h, screen, x, y, SRCCOPY))
            {
                result = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                result.Freeze(); // 렌더 스레드 경계 안전
            }
        }
        catch { result = null; }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
        return result;
    }
}
