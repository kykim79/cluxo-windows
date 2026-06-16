using System.Runtime.InteropServices;

namespace Cluxo.Windows.App.Input;

/// <summary>
/// 입력 계층용 Win32 P/Invoke 선언/구조체/상수. (렌더 계층은 CsWin32 도입 예정 — INPUT-LAYER.md)
/// LL 후킹·핫키·커서 위치·메시지 루프에 필요한 최소 표면만.
/// </summary>
internal static class NativeMethods
{
    // ── 후킹 종류 ────────────────────────────────────────────────
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const int HC_ACTION = 0;

    // ── 윈도우 메시지 ────────────────────────────────────────────
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    public const uint WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
    public const uint WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;

    public const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    public const uint WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_APP = 0x8000; // 스레드 깨우기(핫키 등록/해제 op 처리용)

    // ── 구조체 ───────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;        // 물리 픽셀(가상 데스크톱)
        public uint mouseData;  // 휠 델타는 HIWORD(부호 있음)
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    /// <summary>LL 후킹 콜백 시그니처. 콜백은 반드시 경량(INPUT-LAYER.md §1 — timeout 시 OS 제거 T2).</summary>
    public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>WM_MOUSEWHEEL/HWHEEL의 부호 있는 휠 델타(±120 단위).</summary>
    public static int WheelDelta(uint mouseData) => (short)(mouseData >> 16);

    // ── 후킹 ─────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    // ── 커서 / 키 상태 ───────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    // ── 핫키 ─────────────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ── 메시지 루프 ──────────────────────────────────────────────
    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(in MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(in MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);
}
