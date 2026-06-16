# Cluxo for Windows

macOS Cluxo(커서 강조 발표 도구)의 **네이티브 Windows 재구현**. 회사 코브랜딩 홍보용 배포를 목표로 한 별도 제품 — 오픈소스 Mac repo와 분리해 IP·라이선스를 깨끗하게 유지한다.

설계·전략 전체: `~/.gstack/projects/kykim79-Cluxo/ktoy-main-design-20260616-145510.md`
(office-hours → plan-eng-review → plan-ceo-review 통과, 2026-06-16)

## 아키텍처 (요약)

- **언어/스택**: C# (.NET 8) + Win32 P/Invoke + Vortice.Windows(로우레벨 Direct2D) 예정.
- **계층**:
  - `Cluxo.Core` — 플랫폼 무관 순수 로직 (xUnit 100% 목표). Mac의 static 순수함수 패턴 이식. 훗날 공유 코어(Approach C) 씨앗.
  - Input / Render / Shell — 네이티브 Windows 계층 (예정).
- **v1 스코프**: 커서 강조 링·클릭 스포트라이트·키스트로크 오버레이·라디얼 메뉴·그리기/주석 모드·흔들기로 찾기·코브랜딩. 제외: 트랙패드 제스처(강제), 돋보기(v1.1).

## 빌드 / 테스트

```bash
dotnet build      # 솔루션 빌드
dotnet test       # Cluxo.Core 단위 테스트
```

## Cluxo.Core 이식 진행 (Mac → C#)

| 모듈 | 상태 | 비고 |
|------|------|------|
| ShakeState | ✅ 이식·테스트(17) | 시간 주입, 축별 독립 추적, 0.5초 dedup |
| DragAngleLabel / DragAngleAccumulator | ✅ 이식·테스트(28) | ±π wrapping, 8방향 화살표(away-from-zero 반올림) |
| RadialHitTest | ✅ 이식·테스트(11) | 거리/각도 → sector/sub/subSub, lock 유지. contentSpan 테스트는 CursorSettings 이식 때로 미룸 |
| KeyFormat | ✅ 이식·테스트(13) | 게이트(Ctrl/Alt/Win 필수)·순서·특수키 불변식 보존. 글리프는 Windows 관례(디자인 리뷰서 확정), VK 매핑은 플랫폼 계층 |
| DrawingState | ✅ 이식·테스트(48) | 7개 도구·모디파이어 매핑(Cmd→Ctrl/Opt→Alt)·두께 단계·undo·툴바 hit-test. onboarding은 UI 계층 |
| JsonSettingsStore (Persisted) | ✅ 이식·테스트(7) | %APPDATA% JSON 영구화. 기본값/손상 fallback·라운드트립. 파일IO·디바운스는 플랫폼 계층 |
| BrandingConfig | ✅ 이식·테스트(11) | 코브랜딩 런타임 주입 + HMAC 무결성(변조/미서명 → 순정 fallback). 외부보이스 P1(T3) |
| Tokens (DESIGN.md 전체) | ✅ 이식·테스트(19) | Surface/Stroke/Motion/Radial/Radius/Spacing/Drawing/Text 전체. SwiftUI 타입→플랫폼 무관 데이터 |
| EffectsState | ✅ 이식·테스트(15) | 클릭/스크롤/흔들기/정지펄스/트레일 큐. Task.sleep→시간주입 Prune(now)로 대체(결정적). 트랙패드/클립보드 제외 |
| KeystrokeOverlayState | ✅ 이식·테스트(6) | 키스트로크/상태알림 표시 + 자동 숨김. Task.sleep→시간주입 Tick(now) |

추가 Core 타입: `PointD`/`RectD`/`Rgba`(+opacity 팩토리·needsDarkText), `Spring`/`Ease`/`FontToken`(Visuals), `KeyModifiers`/`SpecialKey`.

**Cluxo.Core v1 순수 로직 + 디자인 토큰 이식 완료 — 198 tests green.** (GestureClassifier는 설계대로 제외: Windows에 raw 터치 입력원 없음.)

## 네이티브 계층 경계 (`Cluxo.Core.Platform`)

Core(순수 로직)와 네이티브 Windows 구현 사이 계약. 네이티브가 구현, Core/코디네이터가 소비.

```
[후킹 스레드]                  [코디네이터]              [렌더 스레드 / vsync]
IMouseHook(클릭·스크롤만) ─▶  Core 상태 갱신     ICursorPositionSource(이동=프레임 샘플)
IKeyboardHook ───────────▶  (DrawingState,           │
IHotkeyRegistrar ────────▶   ShakeState …)  ──▶ OverlayFrame(불변) ─▶ IOverlayRenderer
IForegroundAppMonitor ───▶                            (모니터별 Direct2D)
IClock.NowSeconds ───────▶
Shell: ISettingsStore · IBrandingProvider · ILaunchAtLogin · ITrayIcon · IMonitorProvider
```

- **스레드 규약**: 입력 콜백 경량(WH_MOUSE_LL timeout 회피) → Core 갱신만. 렌더는 별 스레드에서 vsync마다 위치 샘플 + 불변 `OverlayFrame` 수신(공유 가변 상태 없음).
- **하이브리드 입력**(설계 발견1): 이동은 후킹 안 함, 클릭/스크롤만 `IMouseHook`. 위치는 `ICursorPositionSource` 폴링.
- **T2 critical gap**: `IMouseHook.HookRemoved` — 후킹 제거 감지 → 재설치 + 알림.
- 인터페이스는 테스트 더블로 시ams 조립 검증(PlatformTests 8): Clock→ShakeState, MouseHook→DrawingState, OverlayFrame 전달.

## 코디네이터 (`OverlayCoordinator`)

Core 상태 + 플랫폼 인터페이스를 배선하는 중앙 조정자 (Mac `AppDelegate` 대응). 인터페이스에만 의존 → Windows 없이 맥에서 전체 흐름 테스트(OverlayCoordinatorTests 9, 전부 fake 하니스).

- **렌더 루프 미보유**: 플랫폼이 vsync마다 `RenderFrame()` 호출 → 테스트 가능.
- **스레딩 규약 구현**: 입력 콜백은 `_gate` 락 안 Core 갱신만, `RenderFrame`은 락 안 스냅샷 → 락 밖 렌더(GPU 작업 중 락 X).
- **하이브리드 입력 행사**: 그리기 드래그 경로는 후킹이 아니라 `RenderFrame`의 프레임 샘플 위치를 따라간다.
- 배선: ⌃⌥D 토글, 좌클릭 그리기 start/end, ESC clear, `[ ]` 두께+영구화, Ctrl+Z undo, 모니터 변경 시 렌더러 재구성, 후킹 분실(T2) → `MouseHookLost`, 종료 시 설정 flush.
- **효과 배선**(EffectsState): 좌/우클릭 + 더블클릭 감지 → `AddClick`, 스크롤 → `AddScroll`(모니터 영역), 흔들기 감지 → `AddShake`, 매 프레임 트레일/드래그트레일 + `Prune(now)`. 그리기 모드에선 효과 억제. `OverlayFrame`에 모니터별 필터된 `OverlayEffects` 포함.
- TODO(상태 이식 시 연결): 링 외형(CursorSettings), 키스트로크 오버레이, 발표앱 감지 동작, 정지펄스 트리거.

**현재 198 tests green.** 다음 단계는 플랫폼 인터페이스의 **네이티브 구현**(Input/Render/Shell)으로, Windows 실행 환경(Parallels VM/미니PC)이 필요.

## 선행 게이트 (코드 본투자 전)

**T4 — 파일럿 고객사 IT가 마우스 훅 앱 설치를 허용하는지 먼저 확인.** 막히면 GTM 자체가 막힌다.
