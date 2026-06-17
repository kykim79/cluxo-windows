using System.Runtime.InteropServices;

namespace Cluxo.Windows.App.Render;

/// <summary>오버레이 윈도우 스타일/배치용 Win32 P/Invoke (OVERLAY-RENDER.md §1·§6).</summary>
internal static class RenderNativeMethods
{
    public const int GWL_EXSTYLE = -20;

    public const uint WS_EX_TRANSPARENT = 0x00000020; // 클릭 통과 (★ 모드별 토글)
    public const uint WS_EX_TOOLWINDOW = 0x00000080;  // 작업표시줄 미노출
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_NOACTIVATE = 0x08000000;  // 포커스 안 뺏음
    public const uint WS_EX_LAYERED = 0x00080000;     // WPF AllowsTransparency가 사용

    // 외부 캡처(스크린샷·OBS)에서 창 제외 — WDA_EXCLUDEFROMCAPTURE. WDA_NONE=정상 캡처.
    public const uint WDA_NONE = 0x0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>ex-style 비트 추가/제거.</summary>
    public static void UpdateExStyle(IntPtr hwnd, uint add, uint remove)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style |= add;
        style &= ~(long)remove;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }
}
