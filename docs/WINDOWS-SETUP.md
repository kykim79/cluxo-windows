# Windows 개발 환경 셋업

Cluxo for Windows 네이티브 계층을 개발하기 위한 환경 가이드.
개발 머신은 **Apple Silicon (M-series) macOS** 기준 — Parallels Win11 ARM VM을 쓴다.

설계 근거: `~/.gstack/projects/kykim79-Cluxo/ktoy-main-design-20260616-145510.md`

---

## 0. 무엇이 어디서 빌드되나 (핵심)

| 프로젝트 | TFM | Mac에서 | VM(Windows)에서 |
|---------|-----|--------|----------------|
| `Cluxo.Core` | `net8.0` | ✅ 빌드·테스트 (266 tests) | ✅ |
| `Cluxo.Core.Tests` | `net8.0` | ✅ | ✅ |
| `Cluxo.Windows.App` (신규) | `net8.0-windows` | ❌ (Win32/Direct2D 없음) | ✅ 빌드·실행 |

- **순수 로직은 맥에서 계속 짠다** (Core는 플랫폼 무관, `dotnet test`로 즉시 검증).
- **네이티브 앱은 VM에서만** 빌드·실행된다 (`net8.0-windows`는 macOS에서 컴파일 불가).
- 그래서 `Cluxo.Windows.App`은 **Mac 솔루션(`Cluxo.sln`)에 넣지 않는다** — 넣으면 맥에서 `dotnet build`가 깨진다. VM에서 별도 솔루션(`Cluxo.Windows.sln`)으로 묶는다.

---

## 1. Parallels Desktop + Windows 11 (Apple Silicon)

1. **Parallels Desktop 설치** (Pro 권장 — 더 많은 RAM/CPU 할당).
2. **새 VM 생성 → "Windows 11 다운로드 및 설치"** — Parallels가 Windows 11 ARM ISO를 자동 내려받아 설치한다(Microsoft 계정 로그인 필요).
3. VM 사양: M5 Pro면 RAM 8GB+, CPU 4코어+ 할당 권장.
4. 설치 후 **Parallels Tools** 설치(자동) — 클립보드/공유 폴더/해상도 자동.
5. **다중 모니터·DPI 주의**: VM은 호스트 디스플레이를 추상화한다. 멀티모니터·혼합 DPI·GPU 투명 오버레이의 *진짜* 동작은 VM이 가릴 수 있다 → **출시 전 실 x64 하드웨어(회사 자금 미니PC)에서 최종 QA 필수** (설계 T10).

> Windows 11 ARM은 x64 앱을 에뮬레이션으로 돌리고, ARM64·x64 둘 다 빌드할 수 있다. Cluxo는 ARM64 네이티브로 빌드하면 VM에서 가장 빠르다.

---

## 2. VM 안: .NET 8 SDK + 워크로드

VM(Windows) 안에서:

1. **.NET 8 SDK 설치** — https://dotnet.microsoft.com/download/dotnet/8.0 에서 **Arm64 SDK installer** 다운로드·실행. (또는 `winget install Microsoft.DotNet.SDK.8`)
2. 검증:
   ```powershell
   dotnet --version          # 8.0.x
   dotnet --list-sdks
   ```
3. **IDE** (택1):
   - **Visual Studio 2022** (Community, 무료) — "**.NET 데스크톱 개발**" 워크로드 설치. WPF 디자이너·디버거 최상.
   - **JetBrains Rider** — 맥/윈도우 일관 경험. 라이선스 필요.
   - **VS Code + C# Dev Kit** — 가벼움.

---

## 3. 리포 클론 (private)

리포는 GitHub private (`kykim79/cluxo-windows`)다. VM 안에서:

```powershell
winget install Git.Git GitHub.cli   # git + gh
gh auth login                        # 브라우저로 로그인
git clone https://github.com/kykim79/cluxo-windows.git
cd cluxo-windows
dotnet test                          # Core 266 tests 통과 확인 (윈도우에서도 green)
```

> 공유 폴더로 맥 작업트리를 VM에 마운트해도 되지만, **VM 안에서 별도 클론**이 빌드 충돌(bin/obj 경합)이 없어 깔끔하다. 맥에서 push → VM에서 pull.

---

## 4. 네이티브 앱 프로젝트 생성 (`Cluxo.Windows.App`)

VM 안, 리포 루트에서:

```powershell
# 윈도우 전용 솔루션 (Mac sln과 분리)
dotnet new sln -n Cluxo.Windows
dotnet new classlib -n Cluxo.Windows.App -o src/Cluxo.Windows.App -f net8.0-windows
del src\Cluxo.Windows.App\Class1.cs

# 솔루션 구성: 네이티브 앱 + Core(참조) + Core.Tests
dotnet sln Cluxo.Windows.sln add src/Cluxo.Windows.App/Cluxo.Windows.App.csproj
dotnet sln Cluxo.Windows.sln add src/Cluxo.Core/Cluxo.Core.csproj
dotnet sln Cluxo.Windows.sln add tests/Cluxo.Core.Tests/Cluxo.Core.Tests.csproj
dotnet add src/Cluxo.Windows.App reference src/Cluxo.Core/Cluxo.Core.csproj

# 패키지
dotnet add src/Cluxo.Windows.App package Vortice.Windows              # Direct2D/DXGI/DirectComposition
dotnet add src/Cluxo.Windows.App package Microsoft.Windows.CsWin32    # Win32 P/Invoke 소스 생성기
```

`Cluxo.Windows.App.csproj` 권장 형태 (콘솔/트레이 앱 + WPF 설정창):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>          <!-- 콘솔 창 없이 -->
    <TargetFramework>net8.0-windows</TargetFramework>
    <RuntimeIdentifiers>win-arm64;win-x64</RuntimeIdentifiers>
    <UseWPF>true</UseWPF>                     <!-- 설정창용. 오버레이는 raw Win32+Direct2D -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationManifest>app.manifest</ApplicationManifest>  <!-- PerMonitorV2 DPI 선언 -->
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
</Project>
```

`app.manifest` 핵심 (Per-Monitor DPI v2 — 멀티모니터 정합의 전제):

```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
  </windowsSettings>
</application>
```

`NativeMethods.txt` (CsWin32에 생성할 API 나열 — 한 줄에 하나):

```
SetWindowsHookEx
CallNextHookEx
UnhookWindowsHookEx
GetCursorPos
RegisterHotKey
UnregisterHotKey
SetWinEventHook
GetForegroundWindow
GetWindowThreadProcessId
Shell_NotifyIcon
CreateWindowEx
SetWindowLong
DwmEnableBlurBehindWindow
```
(필요할 때마다 추가. CsWin32가 안전한 P/Invoke 시그니처를 생성한다.)

빌드 확인 (VM):
```powershell
dotnet build Cluxo.Windows.sln
```

> `Cluxo.Windows.sln`·`Cluxo.Windows.App/`은 커밋해도 되지만, **`Cluxo.sln`(맥용)에는 절대 App을 추가하지 말 것** — 맥 CI/로컬 `dotnet test`가 깨진다. CI는 windows-latest에서 `Cluxo.Windows.sln`을 빌드한다(아래 7).

---

## 5. 구현할 플랫폼 인터페이스 (11개 → Win32/Direct2D 매핑)

`Cluxo.Core.Platform`의 인터페이스를 `Cluxo.Windows.App`에서 구현한다. 코디네이터·Core는 이미 다 됨 — **이것만 끼우면 동작한다.**

| 인터페이스 | Win32/Direct2D 구현 | 비고 |
|-----------|--------------------|------|
| `ICursorPositionSource` | `GetCursorPos` | 렌더 루프가 vsync마다 폴링 |
| `IMouseHook` | `SetWindowsHookEx(WH_MOUSE_LL)` — 클릭/스크롤만 | 콜백 경량! 제거 감지 → `HookRemoved` 발생 + 재설치 (T2) |
| `IKeyboardHook` | `SetWindowsHookEx(WH_KEYBOARD_LL)` | 키 → `KeyEvent`(VK→`SpecialKey`/문자 매핑) |
| `IHotkeyRegistrar` | `RegisterHotKey` + 메시지 루프 | ⌃⌥D, ⌃⌥I 등 |
| `IRadialTrigger` | 키보드 후킹으로 ⌃⌥, **down/up 감지** | hold 기반 (RegisterHotKey는 일회성이라 부족) |
| `IMonitorProvider` | `EnumDisplayMonitors` + Per-Monitor DPI | `WM_DISPLAYCHANGE`/`WM_DPICHANGED` → `MonitorsChanged` |
| `IOverlayRendererFactory` / `IOverlayRenderer` | **레이어드 윈도우**(`WS_EX_LAYERED\|TRANSPARENT\|TOPMOST\|TOOLWINDOW`) + DirectComposition + Direct2D (Vortice) | 모니터별. `OverlayFrame` 받아 그림 |
| `ISettingsStore` | `%APPDATA%\Cluxo\settings.json` 읽기/쓰기 + 디바운스 | `JsonSettingsStore` 직렬화 사용 |
| `IBrandingProvider` | 브랜딩 에셋 파일 로드 → `BrandingLoader.Load`(HMAC 검증) | 코브랜딩 (T3) |
| `ILaunchAtLogin` | 레지스트리 `Run` 키 또는 작업 스케줄러 | |
| `ITrayIcon` | `Shell_NotifyIcon` + 컨텍스트 메뉴 | 작업표시줄 미노출 |
| `IForegroundAppMonitor` | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` + 프로세스명 | 발표앱 감지 |
| `IClock` | `Stopwatch.GetTimestamp()` (monotonic 초) | ShakeState 등 시간 주입원 |

**진입점**: `Program.cs`에서 위 구현들을 `new OverlayCoordinator(...)`에 주입 → `Start()` → 렌더 루프(타이머/DComp commit)에서 `RenderFrame()` 호출.

가장 어려운 순서: **오버레이 렌더(DComp+Direct2D)** > 입력 후킹 > 트레이/설정. 오버레이부터 "빈 투명 윈도우에 링 하나 그리기"로 시작해 `OverlayFrame`을 점진 소비하는 게 좋다.

계층별 상세 설계:
> - 오버레이 렌더(컴포지션 스택·DPI 변환·그리기 모드 입력 토글): **[OVERLAY-RENDER.md](OVERLAY-RENDER.md)**
> - 입력(LL 후킹 스레드·timeout 회피·라디얼 chord hold): **[INPUT-LAYER.md](INPUT-LAYER.md)**
> - Shell(설정·코브랜딩·트레이·발표앱 감지·모니터): **[SHELL-LAYER.md](SHELL-LAYER.md)**

---

## 6. 개발 루프

```
맥 (M5 Pro)                      VM (Win11 ARM)
─────────────────                ─────────────────
Cluxo.Core 로직 수정              git pull
dotnet test (즉시 검증)    push   네이티브 구현 (App)
git push          ───────▶       dotnet build Cluxo.Windows.sln
                                 F5 실행 → 오버레이 확인
                          ◀───── (네이티브 변경도 push)
```

- 순수 로직(Core)은 맥에서 빠르게.
- 네이티브(App)는 VM에서. IDE 디버거로 레이어드 윈도우 확인.

---

## 7. 주의 / 다음

- **ARM VM은 실하드웨어가 아니다**: GPU 투명 합성·Per-Monitor DPI·멀티모니터 정합은 VM이 가린다. **출시 전 회사 자금 Windows 미니PC(x64)에서 최종 QA** (설계 T10).
- **코드사이닝**: SmartScreen 마찰 제거에 OV(가능하면 EV) 인증서 필요 — 배포 직전 (설계 T9). 개발 중엔 불필요.
- **CI**: GitHub Actions `windows-latest`에서 `dotnet build Cluxo.Windows.sln` + (추후) MSIX/서명. `Cluxo.sln`(Core) 워크플로와 별도 잡.
- **배포(T4 해소)**: 고객사 환경에서 설치·실행 가능 확인됨(현장 판단). "IT 차단" 우려는 게이트 아님. 남은 건 패키징(서명 .exe/MSI) 선택.
