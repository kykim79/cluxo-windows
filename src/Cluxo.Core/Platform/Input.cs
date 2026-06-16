namespace Cluxo.Core.Platform;

/// <summary>마우스 버튼.</summary>
public enum MouseButton { Left, Right, Middle }

/// <summary>스크롤 변위 (라인/픽셀은 플랫폼 정규화 후).</summary>
public readonly record struct ScrollDelta(double X, double Y);

/// <summary>키 입력 한 건 — 키스트로크 오버레이용. <see cref="KeyFormat.Format"/>에 그대로 넘긴다.</summary>
public readonly record struct KeyEvent(KeyModifiers Modifiers, SpecialKey? Special, string? Characters);

/// <summary>전역 단축키 조합. Key는 정규화된 키 이름("D","Space","Comma") — 플랫폼이 VK로 매핑.</summary>
public readonly record struct HotkeyChord(KeyModifiers Modifiers, string Key);

/// <summary>
/// 프레임 시점 커서 위치 소스 (GetCursorPos). 렌더 루프가 vsync마다 폴링 — 이동마다 후킹하지 않는다.
/// (설계 발견1: 하이브리드 입력 — 위치는 샘플, 클릭/스크롤만 후킹)
/// </summary>
public interface ICursorPositionSource
{
    /// <summary>현재 전역 커서 위치(가상 데스크톱 좌표).</summary>
    PointD GetCursorPosition();
}

/// <summary>
/// 저수준 마우스 후킹 — 클릭/스크롤만(이동 제외). (WH_MOUSE_LL / Raw Input)
/// 콜백은 경량이어야 OS가 후킹을 제거하지 않는다. 제거 감지 시 <see cref="HookRemoved"/>로 알리고
/// 구현이 재설치한다(설계 발견 T2: critical gap — 후킹이 조용히 멈추면 안 됨).
/// </summary>
public interface IMouseHook : IDisposable
{
    event Action<MouseButton, PointD>? ButtonDown;
    event Action<MouseButton, PointD>? ButtonUp;
    event Action<ScrollDelta, PointD>? Scrolled;

    /// <summary>OS가 후킹을 제거함(콜백 timeout 등) — 구현은 재설치, 코디네이터는 사용자에게 알림.</summary>
    event Action? HookRemoved;

    void Start();
    void Stop();
}

/// <summary>전역 키 입력 후킹 — 키스트로크 오버레이용. (WH_KEYBOARD_LL)</summary>
public interface IKeyboardHook : IDisposable
{
    event Action<KeyEvent>? KeyPressed;
    void Start();
    void Stop();
}

/// <summary>전역 단축키 등록. (RegisterHotKey) 반환 핸들 Dispose = 등록 해제.</summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>chord 등록, 눌릴 때 onPressed 호출. 반환값 Dispose 시 해제.</summary>
    IDisposable Register(HotkeyChord chord, Action onPressed);
}
