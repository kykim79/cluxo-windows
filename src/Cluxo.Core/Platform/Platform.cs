namespace Cluxo.Core.Platform;

// ════════════════════════════════════════════════════════════════════════════
//  Cluxo.Core ↔ 네이티브 계층 경계 (Windows)
//
//  Core(순수 로직)는 어떤 Win32/Direct2D도 모른다. 네이티브 계층이 아래 인터페이스를
//  구현해 Core에 입력을 밀어넣고, Core가 만든 불변 프레임 스냅샷을 받아 그린다.
//
//   [후킹 스레드]                    [코디네이터]                  [렌더 스레드 / vsync]
//   IMouseHook  ──ButtonDown/Up──▶                  ICursorPositionSource.GetCursorPosition()
//   IKeyboardHook ──KeyPressed──▶   Core 상태 갱신          │  (이동은 프레임마다 샘플, 후킹 X)
//   IHotkeyRegistrar ──onPressed─▶  (DrawingState,          ▼
//   IForegroundAppMonitor ─Changed▶  ShakeState, ...)  ──▶ OverlayFrame(불변) ──▶ IOverlayRenderer.Render
//   IClock.NowSeconds ───────────▶                                                  (모니터별, Direct2D)
//
//   Shell:  ISettingsStore(%APPDATA% JSON)  IBrandingProvider(검증된 브랜딩)
//           ILaunchAtLogin  ITrayIcon  IMonitorProvider
//
//  스레드 규약: 입력 콜백은 경량으로 — Core 상태 갱신만 하고 즉시 반환(WH_MOUSE_LL timeout 회피).
//  렌더는 별도 스레드에서 vsync마다 위치를 샘플하고 불변 OverlayFrame을 받아 그린다.
//  OverlayFrame이 불변인 이유: 코디네이터/렌더 스레드 간 안전한 전달(공유 가변 상태 없음).
// ════════════════════════════════════════════════════════════════════════════

/// <summary>monotonic 시간 소스 — ShakeState 등 시간 주입(at:)의 단일 출처. (테스트는 fake로 주입)</summary>
public interface IClock
{
    /// <summary>단조 증가 초. wall clock 아님(시계 변경에 영향 없게).</summary>
    double NowSeconds { get; }
}
