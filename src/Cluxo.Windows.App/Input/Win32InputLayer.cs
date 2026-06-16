using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Input;

/// <summary>
/// 입력 계층 조립 루트 — 공유 LL 입력 스레드 + 마우스/키보드/라디얼 facade + 핫키 등록기 + 커서 소스.
/// Program.cs가 이걸 만들어 <c>OverlayCoordinator</c>에 주입한다(INPUT-LAYER.md §6).
///
///   var input = new Win32InputLayer();
///   var coordinator = new OverlayCoordinator(
///       input.Mouse, input.Keyboard, input.Hotkeys, input.CursorSource, monitors,
///       rendererFactory, settings, branding, foreground, input.RadialTrigger, clock);
///   coordinator.Start();
///   // 종료: coordinator.Dispose(); input.Dispose();
/// </summary>
public sealed class Win32InputLayer : IDisposable
{
    private readonly LowLevelInputThread _thread = new();
    private readonly HotkeyRegistrar _hotkeys;
    private bool _disposed;

    public IMouseHook Mouse { get; }
    public IKeyboardHook Keyboard { get; }
    public IRadialTrigger RadialTrigger { get; }
    public IHotkeyRegistrar Hotkeys => _hotkeys;
    public ICursorPositionSource CursorSource { get; }

    public Win32InputLayer()
    {
        Mouse = new MouseHook(_thread);
        Keyboard = new KeyboardHook(_thread);
        RadialTrigger = new RadialTrigger(_thread);
        _hotkeys = new HotkeyRegistrar();
        CursorSource = new CursorPositionSource();
    }

    /// <summary>후킹 재설치 요청(T2) — 세션/데스크톱 전환 후 Shell 계층이 호출.</summary>
    public void RequestReinstall() => _thread.RequestReinstall();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _thread.Dispose();
        _hotkeys.Dispose(); // 멱등 — 코디네이터가 먼저 Dispose해도 안전
    }
}
