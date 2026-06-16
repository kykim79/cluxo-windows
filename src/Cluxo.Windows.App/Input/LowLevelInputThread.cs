using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Cluxo.Core;
using Cluxo.Core.Platform;
using static Cluxo.Windows.App.Input.NativeMethods;

namespace Cluxo.Windows.App.Input;

/// <summary>
/// 전용 입력 스레드 — WH_MOUSE_LL + WH_KEYBOARD_LL을 설치하고 메시지를 펌프한다(INPUT-LAYER.md §1).
/// 마우스/키보드/라디얼 트리거가 이 하나의 스레드를 공유한다.
///
/// 스레드 모델:
///   [펌프 스레드]  후킹 설치 → GetMessage 루프. 후킹 콜백(mouse/keyProc)은 **경량** —
///                  RawEvent를 큐에 enqueue하고 즉시 반환(코디네이터 _gate 락 대기 회피, T2 timeout 방지).
///   [디스패치 스레드]  큐 drain → 모디파이어 추적·라디얼 chord 판정 후 고수준 이벤트 발생
///                      (이 이벤트 핸들러는 _gate를 잡아도 안전 — 후킹 스레드가 아님).
///
/// 고수준 이벤트(<see cref="ButtonDown"/> 등)는 디스패치 스레드에서 발생한다.
/// 소유/수명은 <see cref="Win32InputLayer"/>가 관리(개별 facade Dispose는 게이트만 끔).
/// </summary>
internal sealed class LowLevelInputThread : IDisposable
{
    private enum RawKind { ButtonDown, ButtonUp, Scroll, Key }

    private readonly record struct RawEvent(
        RawKind Kind, MouseButton Button, PointD Point, ScrollDelta Scroll, uint Vk, bool KeyDown);

    // 디스패치 스레드 전용(단일 소비자) — 락 불필요
    private readonly ModifierTracker _modifiers = new();
    private readonly RadialChordDetector _radial = new();

    private readonly BlockingCollection<RawEvent> _queue = new(new ConcurrentQueue<RawEvent>());

    // 후킹 콜백 델리게이트는 필드로 보관 — GC가 후킹 설치 중 수거하지 못하게.
    private LowLevelProc? _mouseProc;
    private LowLevelProc? _keyboardProc;

    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private IntPtr _moduleHandle;

    private Thread? _pumpThread;
    private Thread? _dispatchThread;
    private uint _pumpThreadId;
    private readonly ManualResetEventSlim _pumpReady = new(false);
    private Timer? _watchdog;

    private int _reinstallRequested; // Interlocked
    private bool _started;
    private bool _disposed;
    private readonly object _lifecycle = new();

    // 고수준 이벤트 (디스패치 스레드에서 발생)
    public event Action<MouseButton, PointD>? ButtonDown;
    public event Action<MouseButton, PointD>? ButtonUp;
    public event Action<ScrollDelta, PointD>? Scrolled;
    public event Action<KeyEvent>? KeyPressed;
    public event Action? RadialOpened;
    public event Action? RadialClosed;

    /// <summary>진단용 — 모든 키 전이(vk, down, 반영후 모디파이어). 평소 구독자 없음.</summary>
    public event Action<uint, bool, KeyModifiers>? RawKeyDiag;

    /// <summary>마우스 후킹 재설치됨(T2) — 코디네이터가 트레이로 알림.</summary>
    public event Action? HookRemoved;

    /// <summary>멱등. 첫 호출에 펌프/디스패치 스레드를 띄우고 두 후킹을 설치한다.</summary>
    public void EnsureStarted()
    {
        lock (_lifecycle)
        {
            if (_started || _disposed) return;
            _started = true;

            SeedModifiers(); // 설치 시점에 이미 눌린 모디파이어 1회 주입

            _dispatchThread = new Thread(DispatchLoop) { IsBackground = true, Name = "Cluxo.InputDispatch" };
            _dispatchThread.Start();

            _pumpThread = new Thread(PumpLoop) { IsBackground = true, Name = "Cluxo.InputPump" };
            _pumpThread.SetApartmentState(ApartmentState.STA);
            _pumpThread.Start();

            _pumpReady.Wait(); // 후킹 설치 + 스레드 ID 확정까지 대기

            // 워치독: 후킹 핸들 유효성 주기 점검(보수적). OS의 조용한 제거는 일반 API로 감지 불가 →
            // 1차 방어는 경량 콜백, 세션/데스크톱 전환 시엔 RequestReinstall()(Shell 계층) 호출 권장.
            _watchdog = new Timer(_ => CheckHealth(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
    }

    private void SeedModifiers()
    {
        void Seed(uint vk) => _modifiers.Seed(vk, (GetAsyncKeyState((int)vk) & 0x8000) != 0);
        Seed(VirtualKeys.VK_LCONTROL); Seed(VirtualKeys.VK_RCONTROL);
        Seed(VirtualKeys.VK_LMENU); Seed(VirtualKeys.VK_RMENU);
        Seed(VirtualKeys.VK_LSHIFT); Seed(VirtualKeys.VK_RSHIFT);
        Seed(VirtualKeys.VK_LWIN); Seed(VirtualKeys.VK_RWIN);
    }

    // ── 펌프 스레드 ──────────────────────────────────────────────
    private void PumpLoop()
    {
        _pumpThreadId = GetCurrentThreadId();
        _moduleHandle = GetModuleHandle(null);
        InstallHooks();
        _pumpReady.Set();

        // 메시지 루프 — LL 후킹 콜백은 이 펌프가 돌아야 호출된다.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_APP) { ProcessPumpCommands(); continue; }
            TranslateMessage(in msg);
            DispatchMessage(in msg);
        }

        UninstallHooks();
    }

    private void InstallHooks()
    {
        _mouseProc = MouseProc;       // 필드에 고정(GC 방지)
        _keyboardProc = KeyboardProc;
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, _moduleHandle, 0);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, _moduleHandle, 0);
    }

    private void UninstallHooks()
    {
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
    }

    // 펌프 스레드에서 실행 — 재설치 요청 처리(T2).
    private void ProcessPumpCommands()
    {
        if (Interlocked.Exchange(ref _reinstallRequested, 0) == 1)
        {
            UninstallHooks();
            InstallHooks();
            HookRemoved?.Invoke(); // 코디네이터 → "마우스 후킹이 재설치되었습니다"
        }
    }

    // ── 후킹 콜백 (펌프 스레드) — 반드시 O(1): enqueue 후 즉시 반환 ──
    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION)
        {
            var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var pt = new PointD(ms.pt.X, ms.pt.Y);
            switch ((uint)wParam)
            {
                case WM_LBUTTONDOWN: Enqueue(RawKind.ButtonDown, MouseButton.Left, pt); break;
                case WM_LBUTTONUP: Enqueue(RawKind.ButtonUp, MouseButton.Left, pt); break;
                case WM_RBUTTONDOWN: Enqueue(RawKind.ButtonDown, MouseButton.Right, pt); break;
                case WM_RBUTTONUP: Enqueue(RawKind.ButtonUp, MouseButton.Right, pt); break;
                case WM_MBUTTONDOWN: Enqueue(RawKind.ButtonDown, MouseButton.Middle, pt); break;
                case WM_MBUTTONUP: Enqueue(RawKind.ButtonUp, MouseButton.Middle, pt); break;
                case WM_MOUSEWHEEL: EnqueueScroll(new ScrollDelta(0, WheelDelta(ms.mouseData) / 120.0), pt); break;
                case WM_MOUSEHWHEEL: EnqueueScroll(new ScrollDelta(WheelDelta(ms.mouseData) / 120.0, 0), pt); break;
                // WM_MOUSEMOVE → 무시 (위치는 ICursorPositionSource 폴링)
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION)
        {
            uint msg = (uint)wParam;
            bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
            if (down || up)
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                EnqueueKey(kb.vkCode, down);
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void Enqueue(RawKind kind, MouseButton button, PointD pt)
    {
        if (!_queue.IsAddingCompleted) _queue.Add(new RawEvent(kind, button, pt, default, 0, false));
    }

    private void EnqueueScroll(ScrollDelta delta, PointD pt)
    {
        if (!_queue.IsAddingCompleted) _queue.Add(new RawEvent(RawKind.Scroll, default, pt, delta, 0, false));
    }

    private void EnqueueKey(uint vk, bool down)
    {
        if (!_queue.IsAddingCompleted) _queue.Add(new RawEvent(RawKind.Key, default, default, default, vk, down));
    }

    // ── 디스패치 스레드 — 큐 drain, 고수준 이벤트 발생 ──
    private void DispatchLoop()
    {
        try
        {
            foreach (var ev in _queue.GetConsumingEnumerable())
                Process(ev);
        }
        catch (InvalidOperationException) { /* CompleteAdding 후 종료 */ }
    }

    private void Process(in RawEvent ev)
    {
        switch (ev.Kind)
        {
            case RawKind.ButtonDown: ButtonDown?.Invoke(ev.Button, ev.Point); break;
            case RawKind.ButtonUp: ButtonUp?.Invoke(ev.Button, ev.Point); break;
            case RawKind.Scroll: Scrolled?.Invoke(ev.Scroll, ev.Point); break;
            case RawKind.Key:
                _modifiers.OnKey(ev.Vk, ev.KeyDown);
                var mods = _modifiers.Current;
                RawKeyDiag?.Invoke(ev.Vk, ev.KeyDown, mods);

                // 라디얼 chord(⌃⌥,) 전이는 down/up 모두로 판정
                switch (_radial.OnKey(ev.Vk, ev.KeyDown, mods))
                {
                    case ChordEdge.Opened: RadialOpened?.Invoke(); break;
                    case ChordEdge.Closed: RadialClosed?.Invoke(); break;
                }

                // 키스트로크 오버레이는 down에서만 표시(게이트는 Core KeyFormat이 처리)
                if (ev.KeyDown)
                {
                    var special = VirtualKeys.MapSpecial(ev.Vk);
                    string? chars = special is null ? VirtualKeys.MapChar(ev.Vk) : null;
                    KeyPressed?.Invoke(new KeyEvent(mods, special, chars));
                }
                break;
        }
    }

    // ── T2 워치독 / 재설치 ───────────────────────────────────────
    /// <summary>후킹 재설치를 펌프 스레드에 요청(세션/데스크톱 전환 후 Shell 계층이 호출 권장).</summary>
    public void RequestReinstall()
    {
        if (!_started || _disposed) return;
        Interlocked.Exchange(ref _reinstallRequested, 1);
        PostThreadMessage(_pumpThreadId, WM_APP, UIntPtr.Zero, IntPtr.Zero);
    }

    private void CheckHealth()
    {
        if (!_started || _disposed) return;
        // 보수적: 핸들이 비었으면(설치 실패/무효화) 재설치. OS의 조용한 제거 자체는 감지 한계.
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
            RequestReinstall();
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            if (!_started) return;
        }

        _watchdog?.Dispose();
        _queue.CompleteAdding();                                   // 디스패치 루프 종료
        PostThreadMessage(_pumpThreadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero); // 펌프 루프 종료
        _pumpThread?.Join(500);   // 정상은 ~0ms; 상한은 멈춤 방어용
        _dispatchThread?.Join(500);
        _pumpReady.Dispose();
        _queue.Dispose();
    }
}
