using Cluxo.Core;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Input;

// 공유 LowLevelInputThread 위의 얇은 facade들 — 코디네이터가 보는 인터페이스 단위로 노출한다.
// 실제 후킹 수명은 Win32InputLayer가 소유. facade의 Stop/Dispose는 이벤트 전달 게이트만 끈다.

/// <summary><see cref="IMouseHook"/> — 공유 입력 스레드의 마우스 이벤트를 게이트해 전달.</summary>
internal sealed class MouseHook : IMouseHook
{
    private readonly LowLevelInputThread _thread;
    private volatile bool _enabled;

    public event Action<MouseButton, PointD>? ButtonDown;
    public event Action<MouseButton, PointD>? ButtonUp;
    public event Action<ScrollDelta, PointD>? Scrolled;
    public event Action? HookRemoved;

    public MouseHook(LowLevelInputThread thread)
    {
        _thread = thread;
        _thread.ButtonDown += (b, p) => { if (_enabled) ButtonDown?.Invoke(b, p); };
        _thread.ButtonUp += (b, p) => { if (_enabled) ButtonUp?.Invoke(b, p); };
        _thread.Scrolled += (d, p) => { if (_enabled) Scrolled?.Invoke(d, p); };
        _thread.HookRemoved += () => { if (_enabled) HookRemoved?.Invoke(); };
    }

    public void Start() { _enabled = true; _thread.EnsureStarted(); }
    public void Stop() => _enabled = false;
    public void Dispose() => _enabled = false; // 실제 teardown은 Win32InputLayer
}

/// <summary><see cref="IKeyboardHook"/> — 공유 입력 스레드의 키 이벤트를 게이트해 전달.</summary>
internal sealed class KeyboardHook : IKeyboardHook
{
    private readonly LowLevelInputThread _thread;
    private volatile bool _enabled;

    public event Action<KeyEvent>? KeyPressed;

    public KeyboardHook(LowLevelInputThread thread)
    {
        _thread = thread;
        _thread.KeyPressed += e => { if (_enabled) KeyPressed?.Invoke(e); };
    }

    public void Start() { _enabled = true; _thread.EnsureStarted(); }
    public void Stop() => _enabled = false;
    public void Dispose() => _enabled = false;
}

/// <summary>
/// <see cref="IRadialTrigger"/> — ⌃⌥, hold. 공유 입력 스레드의 chord 감지 결과를 전달.
/// (키보드 후킹이 돌고 있어야 동작 — 코디네이터가 키보드 후킹을 Start하므로 보장된다.)
/// </summary>
internal sealed class RadialTrigger : IRadialTrigger
{
    public event Action? Opened;
    public event Action? Closed;

    public RadialTrigger(LowLevelInputThread thread)
    {
        thread.RadialOpened += () => Opened?.Invoke();
        thread.RadialClosed += () => Closed?.Invoke();
    }

    public void Dispose() { }
}
