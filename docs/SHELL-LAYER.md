# Shell 계층 구조 (네이티브)

트레이·설정 영구화·코브랜딩·자동실행·발표앱 감지·모니터·시계 — 시스템 연동 인터페이스 구현.

> 코드는 검증 전 아웃라인(net8.0-windows라 맥 빌드 불가, VM에서 확정). P/Invoke는 CsWin32.

---

## 0. 인터페이스 → Win32 매핑

| 인터페이스 | 구현 | 비고 |
|-----------|------|------|
| `ISettingsStore` | `%APPDATA%\Cluxo\settings.json` 읽기/쓰기 + 디바운스 | `JsonSettingsStore` 직렬화 사용 |
| `IBrandingProvider` | 브랜딩 에셋 로드 → `BrandingLoader.Load`(HMAC) | 코브랜딩 (T3) |
| `ILaunchAtLogin` | 레지스트리 `Run` 키 | 또는 작업 스케줄러 |
| `ITrayIcon` | `Shell_NotifyIcon` + `TrackPopupMenu` | 작업표시줄 미노출 |
| `IForegroundAppMonitor` | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` | 발표앱 감지 |
| `IMonitorProvider` | `EnumDisplayMonitors` + `GetDpiForMonitor` | `WM_DISPLAYCHANGE`/`WM_DPICHANGED` |
| `IClock` | `Stopwatch` (monotonic) | ShakeState 등 시간 주입원 |

---

## 1. 스레딩 개요

```
[메인 STA 스레드]   메시지 루프 보유
  - ITrayIcon (Shell_NotifyIcon + 메시지 콜백 윈도우)
  - WPF 설정창
  - IHotkeyRegistrar (WM_HOTKEY)
  - IForegroundAppMonitor (out-of-context WinEvent → 메시지 큐)
  - IMonitorProvider 변경 감지 (WM_DISPLAYCHANGE/WM_DPICHANGED)
[입력 스레드]   LL 후킹 (INPUT-LAYER.md)
[렌더 스레드]   오버레이 (OVERLAY-RENDER.md)
```

- 트레이·설정·WinEvent·디스플레이 변경은 모두 **메시지 루프**가 필요 → 메인 STA에 모은다.
- 메시지 콜백 윈도우 1개(message-only, `HWND_MESSAGE` 부모)로 트레이 콜백·핫키·디스플레이 변경을 받는다.

---

## 2. `ISettingsStore` — %APPDATA% JSON + 디바운스

```csharp
string Path = Combine(GetFolderPath(ApplicationData), "Cluxo", "settings.json");

JsonSettingsStore Load() =>
    File.Exists(Path) ? JsonSettingsStore.Load(File.ReadAllText(Path)) : new JsonSettingsStore();

void Save(JsonSettingsStore store) {
    // 디바운스: 코디네이터가 설정 변경마다 호출 → 코얼레싱(0.5s)
    _pending = store;
    _debounce.Restart(0.5s, () => WriteAtomic(Path, _pending.Serialize()));
}
```

- **원자적 쓰기**: temp 파일에 쓰고 `File.Move(temp, Path, overwrite)` — 쓰는 중 크래시해도 손상 안 됨.
- 디렉터리 없으면 생성.
- `Load` 손상/없음 → 빈 store(Core가 기본값 fallback).
- 디바운스는 Core가 아니라 **여기(IO 계층)** 책임(설계대로).

---

## 3. `IBrandingProvider` — 코브랜딩 + 무결성 (T3)

```csharp
BrandingConfig Load() {
    // 서명 빌드에 embed된 비밀 키 (코드에 const byte[] — 난독 권장)
    byte[] key = EmbeddedSigningKey;
    string dir = Combine(AppDir, "branding");
    var contentPath = Combine(dir, "config.json");
    var tagPath = Combine(dir, "config.hmac"); // hex 태그
    if (!File.Exists(contentPath) || !File.Exists(tagPath)) return BrandingConfig.Default;
    return BrandingLoader.Load(File.ReadAllBytes(contentPath), key, File.ReadAllText(tagPath).Trim());
}
public BrandingConfig Current { get; } = Load(); // 시작 시 1회
```

- 코브랜딩 패키지(고객사별): `branding\config.json` + `branding\config.hmac` + 로고 이미지.
- `BrandingLoader`가 HMAC 검증 — 변조/미서명/형식오류 → 순정 Default(이미 테스트됨, 11 tests).
- 로고 이미지(LogoAssetName)는 **렌더 계층이 Direct2D 비트맵으로** 로드. 여기선 config만.
- 빌드 1개 + 에셋 교체로 고객사 온보딩(설계 T8 — 런타임 주입).

---

## 4. `ILaunchAtLogin` — 레지스트리 Run

```csharp
const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
bool IsEnabled {
    get => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue("Cluxo") != null;
    set {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (value) k.SetValue("Cluxo", $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue("Cluxo", throwOnMissingValue: false);
    }
}
```

- HKCU(사용자별) — 관리자 권한 불필요.
- 지연 시작·조건부가 필요하면 작업 스케줄러(`TaskService`)가 대안.

---

## 5. `ITrayIcon` — Shell_NotifyIcon

```csharp
// message-only 윈도우 + 콜백 메시지(WM_APP+1)
Shell_NotifyIcon(NIM_ADD, ref nid{ hWnd, uID, uCallbackMessage=WM_TRAY, hIcon, szTip });

// wndProc:
case WM_TRAY:
  if (lParam == WM_RBUTTONUP || lParam == WM_CONTEXTMENU) {
    var menu = CreatePopupMenu();
    foreach (item in _items) {
      if (item.IsSeparatorBefore) AppendMenu(menu, MF_SEPARATOR);
      AppendMenu(menu, MF_STRING | (item.IsChecked?MF_CHECKED:0) | (item.IsEnabled?0:MF_GRAYED), item.cmdId, item.Label);
    }
    SetForegroundWindow(hwnd);                  // 메뉴 닫힘 버그 회피
    int cmd = TrackPopupMenu(menu, TPM_RETURNCMD, pt.x, pt.y, 0, hwnd, null);
    if (cmd != 0) ItemClicked?.Invoke(_items[cmd].Id);
  }
```

- `SetMenu(items)` → `_items` 갱신(체크/활성 상태 변경 시 다시 호출).
- 아이콘은 .ico 리소스. 코브랜딩이면 회사 아이콘.
- 좌클릭 → 설정창 토글 등(선택).

---

## 6. `IForegroundAppMonitor` — WinEvent

```csharp
_hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
    IntPtr.Zero, WinEventProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

void WinEventProc(.., IntPtr hwnd, ..) {
    GetWindowThreadProcessId(hwnd, out uint pid);
    string proc = ProcessName(pid);   // OpenProcess + QueryFullProcessImageName
    string title = GetWindowText(hwnd);
    var app = new ForegroundApp(proc, title);
    Current = app; Changed?.Invoke(app);
}
```

- `WINEVENT_OUTOFCONTEXT` → 콜백이 **메시지 루프 통해** 전달(메인 STA가 펌프).
- `WINEVENT_SKIPOWNPROCESS` → 자기 창 전환 무시.
- 코디네이터 TODO: 발표/녹화 앱(PowerPoint·OBS·Zoom 등) 감지 시 키스트로크 자동 ON 등(설계 autoEnableOnRecording / 낯선 모니터 trust). 프로세스명 화이트리스트는 추후.

---

## 7. `IMonitorProvider` — EnumDisplayMonitors + DPI

```csharp
IReadOnlyList<MonitorInfo> Enumerate() {
    var list = new List<MonitorInfo>();
    EnumDisplayMonitors(default, default, (hMon, _, _, _) => {
        var mi = new MONITORINFOEXW { cbSize = ... };
        GetMonitorInfo(hMon, ref mi);                 // rcMonitor = 물리 bounds
        GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dx, out _);
        list.Add(new MonitorInfo(
            Id: StableId(mi.szDevice),                // ↓ 주의
            Bounds: new RectD(mi.rcMonitor...),
            DpiScale: dx / 96.0,
            IsPrimary: (mi.dwFlags & MONITORINFOF_PRIMARY) != 0));
        return true;
    }, default);
    return list;
}
// WM_DISPLAYCHANGE/WM_DPICHANGED (message 윈도우) → Enumerate() → MonitorsChanged?.Invoke()
```

- **안정 Id 주의(발표 안전·MonitorIdentity)**: `szDevice`(\\.\DISPLAY1)는 재배열 시 바뀔 수 있다. Mac의 EDID 기반 stableUUID에 해당하는 Windows 방법은 **DisplayConfig**(`QueryDisplayConfig` → `DISPLAYCONFIG_TARGET_DEVICE_NAME`의 monitorDevicePath, EDID 기반) 또는 `EnumDisplayDevices`의 DeviceID. 신뢰 모니터(`trustedMonitorUUIDs`) 매칭엔 이 안정 id를 써야 한다. v1 초기는 szDevice로 시작하되 **출시 전 DisplayConfig 기반으로 교체**(낯선 모니터 감지가 어긋나면 발표 중 오작동).
- Bounds·DpiScale은 오버레이 윈도우 배치/좌표 변환(OVERLAY-RENDER §4)의 입력.

---

## 8. `IClock`

```csharp
public double NowSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
```

- monotonic(시계 변경 무관). ShakeState·EffectsState·KeystrokeOverlayState·RadialMenuController·드래그 모션의 시간 주입원.

---

## 9. 함정 체크리스트

- [ ] 설정 저장은 **원자적**(temp+move) — 디바운스 중 크래시해도 손상 X.
- [ ] `TrackPopupMenu` 전 `SetForegroundWindow` 안 하면 → 메뉴가 클릭 밖에서 안 닫힘.
- [ ] WinEvent/트레이/디스플레이 변경 모두 **메시지 루프 필요** — 메인 STA에 모음.
- [ ] 모니터 안정 Id를 szDevice로만 하면 → 모니터 재배열·핫플러그 시 신뢰 매칭 어긋남(DisplayConfig 권장).
- [ ] `GetDpiForMonitor`는 프로세스가 PerMonitorV2일 때만 정확(app.manifest).
- [ ] 브랜딩 embed 키가 평문 const면 추출 쉬움 — 최소 난독/분할. (완벽한 보호는 불가, 변조 *탐지*가 목적.)
