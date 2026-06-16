using System.Collections.Concurrent;
using Cluxo.Core.Platform;
using static Cluxo.Windows.App.Input.NativeMethods;

namespace Cluxo.Windows.App.Input;

/// <summary>
/// <see cref="IHotkeyRegistrar"/> — RegisterHotKey + 전용 메시지 루프(INPUT-LAYER.md §4).
///
/// WM_HOTKEY는 RegisterHotKey를 호출한 **스레드의 메시지 큐**로 오므로, 등록도 콜백 수신도
/// 한 전용 스레드에서 펌프한다. Register()는 그 스레드에 등록 op를 마샬링하고 결과를 기다린다.
/// 콜백(코디네이터 핸들러)은 _gate를 잡아도 안전 — LL 후킹 스레드가 아니다.
/// </summary>
internal sealed class HotkeyRegistrar : IHotkeyRegistrar
{
    private readonly Thread _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ConcurrentQueue<Action> _ops = new();
    private readonly Dictionary<int, Action> _callbacks = new(); // 스레드 전용 접근(락 불필요)
    private int _nextId;
    private volatile bool _disposed;

    public HotkeyRegistrar()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Cluxo.Hotkeys" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    public IDisposable Register(HotkeyChord chord, Action onPressed)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HotkeyRegistrar));
        var binding = HotkeyChordMapper.Map(chord);
        int id = Interlocked.Increment(ref _nextId);

        bool ok = RunOnThread(() =>
        {
            if (RegisterHotKey(IntPtr.Zero, id, binding.Modifiers, binding.Vk))
            {
                _callbacks[id] = onPressed;
                return true;
            }
            return false;
        });

        if (!ok)
            throw new InvalidOperationException(
                $"핫키 등록 실패: {chord.Modifiers}+{chord.Key} (다른 앱과 충돌일 수 있음)");

        return new Registration(this, id);
    }

    private void UnregisterId(int id) => RunOnThread(() =>
    {
        UnregisterHotKey(IntPtr.Zero, id);
        _callbacks.Remove(id);
        return true;
    });

    // op를 핫키 스레드에 마샬링하고 완료까지 동기 대기.
    private bool RunOnThread(Func<bool> action)
    {
        if (_disposed) return false;
        bool result = false;
        using var done = new ManualResetEventSlim(false);
        _ops.Enqueue(() => { try { result = action(); } finally { done.Set(); } });
        PostThreadMessage(_threadId, WM_APP, UIntPtr.Zero, IntPtr.Zero);
        done.Wait();
        return result;
    }

    private void Loop()
    {
        _threadId = GetCurrentThreadId();
        _ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            switch (msg.message)
            {
                case WM_APP:
                    while (_ops.TryDequeue(out var op)) op();
                    break;
                case WM_HOTKEY:
                    if (_callbacks.TryGetValue((int)(uint)msg.wParam, out var cb)) cb();
                    break;
                default:
                    TranslateMessage(in msg);
                    DispatchMessage(in msg);
                    break;
            }
        }

        // 종료 정리 — 남은 등록 해제
        foreach (var id in _callbacks.Keys) UnregisterHotKey(IntPtr.Zero, id);
        _callbacks.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _ready.Dispose();
    }

    private sealed class Registration : IDisposable
    {
        private readonly HotkeyRegistrar _owner;
        private readonly int _id;
        private bool _done;
        public Registration(HotkeyRegistrar owner, int id) { _owner = owner; _id = id; }
        public void Dispose()
        {
            if (_done || _owner._disposed) return;
            _done = true;
            _owner.UnregisterId(_id);
        }
    }
}
