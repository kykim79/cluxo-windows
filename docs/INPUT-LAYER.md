# 입력 계층 구조 (네이티브)

`Cluxo.Core.Platform`의 입력 인터페이스 Windows 구현 설계.
**하이브리드 입력**(설계 발견1): 위치는 프레임 샘플, 클릭/스크롤만 후킹.

> 코드는 검증 전 아웃라인(net8.0-windows라 맥 빌드 불가, VM에서 확정). P/Invoke는 CsWin32 생성 사용.

---

## 0. 인터페이스 → Win32 매핑

| 인터페이스 | Win32 | 핵심 함정 |
|-----------|-------|----------|
| `ICursorPositionSource` | `GetCursorPos` | 물리 픽셀(PerMonitorV2). 렌더 스레드가 폴링 |
| `IMouseHook` | `SetWindowsHookEx(WH_MOUSE_LL)` | **콜백 경량!** timeout 시 OS 제거(T2). 이동 제외(클릭/스크롤만) |
| `IKeyboardHook` | `SetWindowsHookEx(WH_KEYBOARD_LL)` | VK→`SpecialKey`/문자, 모디파이어 조회 |
| `IHotkeyRegistrar` | `RegisterHotKey` + 메시지 루프 | WM_HOTKEY는 등록 스레드 큐로 옴 |
| `IRadialTrigger` | 키보드 후킹으로 chord **down/up** | RegisterHotKey는 일회성이라 부족(hold X) |

---

## 1. 스레딩 — 입력 스레드 (★)

**LL 후킹은 설치한 스레드에서 콜백이 돌고, 그 스레드는 메시지 루프를 펌프해야 한다.**

```
[입력 스레드]  (전용)
  SetWindowsHookEx(WH_MOUSE_LL,   mouseProc,  hInst, 0)
  SetWindowsHookEx(WH_KEYBOARD_LL, keyProc,   hInst, 0)
  while (GetMessage(&msg)) { TranslateMessage; DispatchMessage; }   // 콜백 펌프

mouseProc/keyProc:
  ── 절대 무겁게 하지 말 것 ──
  이벤트를 ConcurrentQueue에 enqueue → CallNextHookEx → 즉시 반환
[디스패치 스레드]  큐 drain → coordinator.OnButtonDown/OnScrolled/OnKeyPressed
```

- **왜 큐?** LL 콜백이 `LowLevelHooksTimeout`(기본 300ms) 넘으면 OS가 후킹을 조용히 제거(T2). 콜백을 O(1)로 유지하려면 enqueue 후 즉시 반환. 코디네이터의 `_gate` 락 대기조차 콜백에서 피한다.
- 키보드/핫키는 빈도 낮아 직접 호출도 가능하지만, 일관성 위해 같은 큐 권장.
- 코디네이터는 thread-safe(`_gate`)라 디스패치 스레드에서 호출 안전.

---

## 2. `IMouseHook` — WH_MOUSE_LL

```csharp
// mouseProc(nCode, wParam, lParam)
if (nCode >= 0) {
    var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam); // pt(물리), mouseData, flags
    switch ((uint)wParam) {
        case WM_LBUTTONDOWN: Enqueue(Down, Left,  ms.pt); break;
        case WM_LBUTTONUP:   Enqueue(Up,   Left,  ms.pt); break;
        case WM_RBUTTONDOWN: Enqueue(Down, Right, ms.pt); break;
        case WM_RBUTTONUP:   Enqueue(Up,   Right, ms.pt); break;
        case WM_MBUTTONDOWN: Enqueue(Down, Middle, ms.pt); break;
        case WM_MBUTTONUP:   Enqueue(Up,   Middle, ms.pt); break;
        case WM_MOUSEWHEEL:  Enqueue(Scroll, dyFrom(ms.mouseData), ms.pt); break;
        case WM_MOUSEHWHEEL: Enqueue(Scroll, dxFrom(ms.mouseData), ms.pt); break;
        // WM_MOUSEMOVE → 무시 (위치는 프레임 샘플)
    }
}
return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
```

- 휠 델타: `(short)HIWORD(mouseData)` (±120 단위). `ScrollDelta`로 정규화(부호/크기).
- `ms.pt`는 **물리 픽셀**(가상 데스크톱) → `PointD` 그대로. 좌표 변환은 렌더가.

### T2 — 후킹 제거 감지/복구

OS는 제거 시 알림을 주지 않는다. 방어:
1. **1차: 콜백을 빠르게**(§1 큐) — 사실상 제거 안 됨.
2. **2차 워치독**: 입력 스레드가 주기적(예 5초)으로 살아있는지 self-ping. 콜백이 굶었거나 후킹 핸들 이상이면 `UnhookWindowsHookEx`+`SetWindowsHookEx` 재설치 후 `HookRemoved` 발생(코디네이터 → 트레이 알림).
3. 세션 전환(`WTS`)·UAC 데스크톱 전환 후엔 재설치가 안전.

---

## 3. `IKeyboardHook` — WH_KEYBOARD_LL → KeyEvent

```csharp
// keyProc, wParam == WM_KEYDOWN/WM_SYSKEYDOWN 만 (down에서 표시)
var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam); // vkCode, scanCode, flags
var mods = ReadModifiers(); // GetKeyState(VK_CONTROL/VK_MENU/VK_SHIFT/VK_LWIN|RWIN)
var special = MapSpecial(kb.vkCode);          // VK_RETURN→Return, VK_ESCAPE→Escape, VK_LEFT→ArrowLeft, VK_F1..→F1..
string? chars = special is null ? MapChar(kb.vkCode, kb.scanCode) : null; // ToUnicodeEx
Enqueue(new KeyEvent(mods, special, chars));
```

- `KeyModifiers`: Control/Alt/Shift/Win 비트. (Alt = VK_MENU)
- `MapSpecial`: VK → `SpecialKey`(Enter/Tab/Space/Backspace/Esc/Del/화살표/Home/End/PgUp/PgDn/F1-12).
- `MapChar`: 인쇄 가능 키는 `ToUnicodeEx`로 문자(대문자는 `KeyFormat`이 처리하므로 그냥 키 문자). 데드키 주의 — 실패 시 null.
- 게이트(Ctrl/Alt/Win 없으면 표시 X)는 **Core `KeyFormat.Format`이 처리** — 후킹은 그냥 다 넘김.
- (선택) 패스워드 필드 포커스 시 키스트로크 억제 — Mac은 함. v1은 KeyFormat 게이트로 충분, 추후 `GetGUIThreadInfo`로 보강.

---

## 4. `IHotkeyRegistrar` — RegisterHotKey

```csharp
// 메시지 루프 있는 스레드(메인 STA 권장)에서:
RegisterHotKey(hwnd, id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, vk); // chord → mods+vk
// 루프에서:
case WM_HOTKEY: callbacks[(int)wParam]();   // wParam = id
```

- `HotkeyChord.Key`("D","I","Space","Comma") → VK 매핑 테이블.
- 모디파이어 비트 변환: Control→MOD_CONTROL, Alt→MOD_ALT, Shift→MOD_SHIFT, Win→MOD_WIN.
- `Register` 반환 핸들 Dispose = `UnregisterHotKey(hwnd, id)`.
- WM_HOTKEY는 **등록한 스레드의 메시지 큐**로 오므로, 그 스레드가 펌프해야 함(메인 STA).
- ⌃⌥D(그리기)·⌃⌥I(좌표) 등. 충돌 시 등록 실패(false) → 사용자에게 안내.

---

## 5. `IRadialTrigger` — chord hold (⌃⌥,)

RegisterHotKey는 누름 1회만 → hold 불가. **키보드 후킹에서 직접 감지**:

```
keyProc에서 모디파이어 상태 + 콤마키 추적:
  Ctrl && Alt && Comma 모두 down 전이  → Opened 발생 (한 번)
  위 조합에서 아무 키나 up            → Closed 발생 (한 번), 상태 리셋
```

- 상태 머신: `_radialDown`(bool). 진입/이탈 edge에서만 발생(중복 방지).
- chord 중 다른 키 입력은 무시(라디얼 navigation은 커서로).
- Comma의 VK = `VK_OEM_COMMA`(0xBC).
- 코디네이터: Opened → `_cursor.GetCursorPosition()`을 중심으로 `_radial.Open`. Closed → `_radial.Close`(실행).

---

## 6. 진입점 배선 (Program.cs 스케치)

```csharp
var input = new Win32InputLayer();   // 입력 스레드 + 큐 시작
var coordinator = new OverlayCoordinator(
    input.Mouse, input.Keyboard, input.Hotkeys, input.CursorSource,
    monitors, rendererFactory, settings, branding, foreground, input.RadialTrigger, clock);
coordinator.MouseHookLost += () => tray.Notify("마우스 후킹이 재설치되었습니다");
coordinator.Start();
// 렌더 스레드: loop { wait vsync; coordinator.RenderFrame(); }
```

---

## 7. 함정 체크리스트

- [ ] LL 콜백에서 무거운 작업/락 대기 → OS가 후킹 제거(T2). enqueue 후 즉시 반환.
- [ ] 후킹 스레드에 메시지 루프 없으면 → 콜백 안 옴.
- [ ] `WM_MOUSEMOVE`를 후킹으로 받으면 호출 폭주 → 제거 위험. **이동은 GetCursorPos 폴링.**
- [ ] WM_HOTKEY는 등록 스레드 큐 → 다른 스레드면 못 받음.
- [ ] `RegisterHotKey`에 `MOD_NOREPEAT` 넣어 hold 반복 발생 억제.
- [ ] x64 마샬링: `MSLLHOOKSTRUCT`/`KBDLLHOOKSTRUCT` 정확히(CsWin32가 처리).
- [ ] 데드키/IME 입력 시 `ToUnicodeEx` 부작용 — 키스트로크 표시는 게이트(모디파이어 필수)라 평소 무관.
