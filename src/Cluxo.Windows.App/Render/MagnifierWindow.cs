using System.Runtime.InteropServices;

namespace Cluxo.Windows.App.Render;

/// <summary>
/// Windows Magnification API 기반 돋보기 렌즈. 별도 레이어드·클릭통과·topmost 호스트 창에 WC_MAGNIFIER
/// 컨트롤을 띄우고 <c>MagSetWindowFilterList(EXCLUDE)</c>로 우리 오버레이 창들을 확대 대상에서 제외한다 →
/// 렌즈가 자기 자신을 재귀로 비추지 않고, 화면 녹화/공유엔 그대로 보인다.
///
/// 테두리: 호스트 창을 렌즈보다 (2×border)만큼 크게 만들고 자식(확대 컨트롤)을 안쪽에 둔다. 호스트의
/// 여백을 WM_PAINT에서 테두리 색으로 채우면 원형 테두리가 된다 — <b>콘텐츠와 같은 창이라 함께(지연 없이)
/// 이동</b>(WPF 오버레이로 따로 그리면 합성 지연으로 외곽선이 늦게 따라옴).
///
/// 메시지 펌프가 있는 스레드(WPF UI 스레드)에서 생성·갱신해야 한다. 좌표는 물리 픽셀.
/// </summary>
internal sealed class MagnifierWindow : IDisposable
{
    // ── Magnification.dll ───────────────────────────────────────
    [DllImport("Magnification.dll")] private static extern bool MagInitialize();
    [DllImport("Magnification.dll")] private static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] private static extern bool MagSetWindowSource(IntPtr hwnd, RECT rect);
    [DllImport("Magnification.dll")] private static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM transform);
    [DllImport("Magnification.dll")] private static extern bool MagSetWindowFilterList(IntPtr hwnd, int mode, int count, IntPtr[] hwnds);

    // ── user32 / gdi32 ──────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(
        uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hrgn, bool redraw);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rc, bool erase);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rc);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT lprc, IntPtr hbr);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MAGTRANSFORM { public float m00, m01, m02, m10, m11, m12, m20, m21, m22; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore; public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private const uint WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000;
    private const uint WS_EX_TOPMOST = 0x8, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    private const uint LWA_ALPHA = 0x2, SWP_NOACTIVATE = 0x10;
    private const uint WM_PAINT = 0x000F;
    private const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    private const int MW_FILTERMODE_EXCLUDE = 0;
    private const uint MS_SHOWMAGNIFIEDCURSOR = 1;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const string WC_MAGNIFIER = "Magnifier";
    private const string HostClass = "CluxoMagnifierHost";

    // GC 방지 + 프로세스 1회 등록. 테두리 색·브러시는 WndProc(WM_PAINT)이 읽는다(단일 인스턴스).
    private static readonly WndProcDelegate s_wndProc = HostWndProc;
    private static bool s_classRegistered;
    private static int s_borderColorRef = 0x00FFE600; // 기본 cyan(0x00BBGGRR)
    private static IntPtr s_brush;
    private static int s_brushColor = -1;

    private static IntPtr HostWndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == WM_PAINT)
        {
            if (BeginPaint(h, out var ps) != IntPtr.Zero)
            {
                if (GetClientRect(h, out var rc))
                {
                    if (s_brushColor != s_borderColorRef)
                    {
                        if (s_brush != IntPtr.Zero) DeleteObject(s_brush);
                        s_brush = CreateSolidBrush(s_borderColorRef);
                        s_brushColor = s_borderColorRef;
                    }
                    FillRect(ps.hdc, ref rc, s_brush);
                }
                EndPaint(h, ref ps);
            }
            return IntPtr.Zero;
        }
        return DefWindowProc(h, msg, w, l);
    }

    private IntPtr _host, _mag;
    private bool _ok, _visible;
    private int _lastHostW;
    private double _lastZoom = -1;
    private IntPtr[] _lastExclude = Array.Empty<IntPtr>();

    public MagnifierWindow()
    {
        try
        {
            if (!MagInitialize()) return;
            var hInst = GetModuleHandle(null);
            if (!s_classRegistered)
            {
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = s_wndProc,
                    hInstance = hInst,
                    lpszClassName = HostClass,
                };
                RegisterClassEx(ref wc);
                s_classRegistered = true;
            }
            _host = CreateWindowEx(
                WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                HostClass, "CluxoMag", WS_POPUP, 0, 0, 100, 100, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
            if (_host == IntPtr.Zero) return;
            SetLayeredWindowAttributes(_host, 0, 255, LWA_ALPHA);
            _mag = CreateWindowEx(0, WC_MAGNIFIER, "CluxoMagCtrl", WS_CHILD | WS_VISIBLE | MS_SHOWMAGNIFIEDCURSOR,
                0, 0, 100, 100, _host, IntPtr.Zero, hInst, IntPtr.Zero);
            if (_mag == IntPtr.Zero) return;
            _ok = true;
        }
        catch { _ok = false; }
    }

    /// <summary>렌즈를 (cx,cy) 물리 중심·지름 lens(물리)·배율 zoom으로, 테두리 색 borderColorRef로 보인다.</summary>
    public void Update(int cx, int cy, double zoom, int lens, int borderColorRef, IntPtr[] exclude)
    {
        if (!_ok) return;
        zoom = Math.Max(1.1, zoom);
        lens = Math.Max(40, lens);
        int border = Math.Max(3, lens / 50);
        int hostW = lens + 2 * border;
        int x = cx - hostW / 2, y = cy - hostW / 2;
        SetWindowPos(_host, HWND_TOPMOST, x, y, hostW, hostW, SWP_NOACTIVATE);

        if (hostW != _lastHostW)
        {
            MoveWindow(_mag, border, border, lens, lens, true);          // 자식을 안쪽으로(여백=테두리)
            SetWindowRgn(_host, CreateEllipticRgn(0, 0, hostW, hostW), true);
            SetWindowRgn(_mag, CreateEllipticRgn(0, 0, lens, lens), true); // 자식도 원형
            _lastHostW = hostW;
            InvalidateRect(_host, IntPtr.Zero, true);                    // 테두리 다시 칠
        }

        if (borderColorRef != s_borderColorRef)
        {
            s_borderColorRef = borderColorRef;
            InvalidateRect(_host, IntPtr.Zero, true);
        }

        // 우리 오버레이 + 호스트 제외(재귀 방지). 목록 바뀔 때만(매 프레임 호출은 끊김 유발).
        var list = new IntPtr[exclude.Length + 1];
        Array.Copy(exclude, list, exclude.Length);
        list[^1] = _host;
        if (!ExcludeEqual(list))
        {
            MagSetWindowFilterList(_mag, MW_FILTERMODE_EXCLUDE, list.Length, list);
            _lastExclude = list;
        }

        if (zoom != _lastZoom)
        {
            float z = (float)zoom;
            var t = new MAGTRANSFORM { m00 = z, m11 = z, m22 = 1f };
            MagSetWindowTransform(_mag, ref t);
            _lastZoom = zoom;
        }

        // 소스(커서 추적)는 매 프레임 — 콘텐츠 리프레시.
        int half = (int)Math.Round(lens / (2.0 * zoom));
        var src = new RECT { left = cx - half, top = cy - half, right = cx + half, bottom = cy + half };
        MagSetWindowSource(_mag, src);

        if (!_visible) { ShowWindow(_host, SW_SHOWNOACTIVATE); _visible = true; }
    }

    private bool ExcludeEqual(IntPtr[] list)
    {
        if (list.Length != _lastExclude.Length) return false;
        for (int i = 0; i < list.Length; i++) if (list[i] != _lastExclude[i]) return false;
        return true;
    }

    public void Hide()
    {
        if (_ok && _visible) { ShowWindow(_host, SW_HIDE); _visible = false; }
    }

    public void Dispose()
    {
        if (_host != IntPtr.Zero) { DestroyWindow(_host); _host = IntPtr.Zero; }
        if (_ok) { MagUninitialize(); _ok = false; }
    }
}
