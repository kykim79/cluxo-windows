using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using static Cluxo.Windows.App.Input.NativeMethods;
using SN = Cluxo.Windows.App.Shell.ShellNativeMethods;

namespace Cluxo.Windows.App.Shell;

/// <summary>
/// 메시지 전용 윈도우 + STA 메시지 펌프(SHELL-LAYER.md §1). 트레이 콜백·디스플레이 변경·WinEvent가
/// 모두 메시지 루프를 필요로 하므로 한 스레드에 모은다.
///
/// - <see cref="Hwnd"/>: 트레이/메뉴가 쓰는 윈도우 핸들.
/// - <see cref="Message"/>: WndProc가 받은 메시지를 구독자에게 전달(트레이 콜백·WM_DISPLAYCHANGE 등).
/// - <see cref="Invoke"/>/<see cref="Post"/>: 작업을 호스트 스레드에서 실행(SetWinEventHook은 펌프 스레드에서 호출해야).
/// </summary>
internal sealed class MessageWindowHost : IDisposable
{
    private readonly Thread _thread;
    private uint _threadId;
    private IntPtr _hwnd;
    private readonly string _className = "CluxoMsgWnd-" + Guid.NewGuid().ToString("N");
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ConcurrentQueue<Action> _ops = new();
    private SN.WndProcDelegate? _wndProc; // GC 방지 — 필드 보관
    private bool _disposed;

    /// <summary>메시지 윈도우 핸들(준비 완료 후 유효).</summary>
    public IntPtr Hwnd => _hwnd;

    /// <summary>WndProc 수신 메시지(msg, wParam, lParam) — 호스트 스레드에서 발생.</summary>
    public event Action<uint, IntPtr, IntPtr>? Message;

    public MessageWindowHost()
    {
        _thread = new Thread(ThreadProc) { IsBackground = true, Name = "Cluxo.MsgWindow" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    private void ThreadProc()
    {
        _threadId = GetCurrentThreadId();
        var hInstance = GetModuleHandle(null);

        _wndProc = WndProc;
        var wc = new SN.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<SN.WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = hInstance,
            lpszClassName = _className,
        };
        SN.RegisterClassEx(ref wc);

        _hwnd = SN.CreateWindowEx(0, _className, "Cluxo", 0, 0, 0, 0, 0,
            new IntPtr(SN.HWND_MESSAGE), IntPtr.Zero, hInstance, IntPtr.Zero);
        _ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(in msg);
            DispatchMessage(in msg);
        }

        if (_hwnd != IntPtr.Zero) SN.DestroyWindow(_hwnd);
        SN.UnregisterClass(_className, hInstance);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == SN.WM_RUNOPS)
        {
            while (_ops.TryDequeue(out var op)) op();
            return IntPtr.Zero;
        }
        Message?.Invoke(msg, wParam, lParam);
        return SN.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>작업을 호스트 스레드에서 실행하고 완료까지 대기.</summary>
    public void Invoke(Action action)
    {
        if (_disposed) return;
        if (GetCurrentThreadId() == _threadId) { action(); return; }
        using var done = new ManualResetEventSlim(false);
        _ops.Enqueue(() => { try { action(); } finally { done.Set(); } });
        SN.PostMessage(_hwnd, SN.WM_RUNOPS, IntPtr.Zero, IntPtr.Zero);
        done.Wait();
    }

    /// <summary>작업을 호스트 스레드에서 비동기 실행.</summary>
    public void Post(Action action)
    {
        if (_disposed) return;
        _ops.Enqueue(action);
        SN.PostMessage(_hwnd, SN.WM_RUNOPS, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(500); // 정상은 ~0ms
        _ready.Dispose();
    }
}
