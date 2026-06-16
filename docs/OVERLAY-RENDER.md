# 오버레이 렌더 구조 (네이티브)

`IOverlayRenderer` Windows 구현 설계 — **가장 어려운 네이티브 조각**. 모니터별로
투명·클릭통과·항상-위 윈도우를 만들어 60Hz로 `OverlayFrame`을 그린다.

> 아래 코드는 **검증 전 아웃라인**(net8.0-windows라 맥에서 컴파일 불가). VM에서 확정.
> 스택: **D3D11 + DXGI 컴포지션 스왑체인 + DirectComposition + Direct2D**, 전부 Vortice.Windows.

---

## 0. 왜 이 스택인가

투명 GPU 오버레이의 현대 표준은 **DirectComposition + 컴포지션 스왑체인**이다.
구식 `UpdateLayeredWindow`(WS_EX_LAYERED)는 CPU 비트맵이라 60Hz 효과에 느리다.

```
D3D11 디바이스 (BGRA 지원)
   └─ DXGI 디바이스
        ├─ DXGI 컴포지션 스왑체인 (B8G8R8A8, 알파 Premultiplied, FlipSequential, FrameLatencyWaitable)
        │     └─ 매 프레임 back buffer(DXGI surface) → D2D 비트맵 타깃
        ├─ D2D 디바이스 → D2D 디바이스 컨텍스트 (그리기)
        └─ DirectComposition 디바이스
              └─ Target(hwnd) → Visual → SetContent(스왑체인) → Commit
```

DComp가 스왑체인 내용을 데스크톱 위에 알파 합성한다. 윈도우 자체엔 redirection bitmap이
없어야(`WS_EX_NOREDIRECTIONBITMAP`) 뒤가 비친다.

---

## 1. 윈도우 (모니터당 1개)

스타일:

```
exStyle = WS_EX_NOREDIRECTIONBITMAP   // DComp 투명의 전제 (redirection surface 없음)
        | WS_EX_TOPMOST               // 항상 위
        | WS_EX_TOOLWINDOW            // 작업표시줄 미노출
        | WS_EX_NOACTIVATE            // 포커스 안 뺏음
        | WS_EX_TRANSPARENT           // 클릭 통과 (★ 모드별 토글 — 아래 §6)
style   = WS_POPUP | WS_VISIBLE
```

- 위치/크기: `SetWindowPos(hwnd, HWND_TOPMOST, mon.x, mon.y, mon.w, mon.h, SWP_NOACTIVATE)` — **모니터 물리 픽셀**.
- `WS_EX_LAYERED`는 **쓰지 않는다**(DComp와 병용 X).
- 모니터별 윈도우 → `IOverlayRendererFactory.Create(MonitorInfo)`가 1개 생성. `MonitorsChanged` 시 코디네이터가 생성/파기(이미 배선됨).

---

## 2. 컴포지션 스택 초기화 (Create 시 1회, Vortice 아웃라인)

```csharp
// D3D11 — BGRA 플래그 필수(D2D interop)
D3D11.D3D11CreateDevice(null, DriverType.Hardware,
    DeviceCreationFlags.BgraSupport, ..., out ID3D11Device d3d, out var ctx);
using var dxgiDevice = d3d.QueryInterface<IDXGIDevice>();

// 컴포지션 스왑체인 (모니터 물리 픽셀)
var scDesc = new SwapChainDescription1 {
    Width = mon.PixelWidth, Height = mon.PixelHeight,
    Format = Format.B8G8R8A8_UNorm, Stereo = false,
    SampleDescription = new(1, 0), BufferUsage = Usage.RenderTargetOutput,
    BufferCount = 2, Scaling = Scaling.Stretch,
    SwapEffect = SwapEffect.FlipSequential,
    AlphaMode = AlphaMode.Premultiplied,
    Flags = SwapChainFlags.FrameLatencyWaitableObject,
};
using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
var swapChain = factory.CreateSwapChainForComposition(dxgiDevice, scDesc);
swapChain.MaximumFrameLatency = 1;
var waitable = swapChain.FrameLatencyWaitableObject; // vsync 페이싱

// DirectComposition — 데스크톱 위에 스왑체인 합성
DComp.DCompositionCreateDevice(dxgiDevice, out IDCompositionDevice dcomp);
var target = dcomp.CreateTargetForHwnd(hwnd, topmost: true);
var visual = dcomp.CreateVisual();
visual.SetContent(swapChain);
target.SetRoot(visual);
dcomp.Commit();

// Direct2D — 스왑체인에 그릴 컨텍스트
using var d2dDevice = D2D1.D2D1CreateDevice(dxgiDevice, default);
var dc = d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
dc.Dpi = new(mon.Dpi, mon.Dpi);   // ★ DPI — §4
```

매 프레임 back buffer를 D2D 타깃으로:

```csharp
using var backBuf = swapChain.GetBuffer<IDXGISurface>(0);
using var bmp = dc.CreateBitmapFromDxgiSurface(backBuf, new BitmapProperties1 {
    PixelFormat = new(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
    DpiX = mon.Dpi, DpiY = mon.Dpi,
    BitmapOptions = BitmapOptions.Target | BitmapOptions.CannotDraw,
});
dc.Target = bmp;
dc.BeginDraw();
dc.Clear(new Color4(0, 0, 0, 0));   // 완전 투명
// ... OverlayFrame 그리기 (§5) ...
dc.EndDraw();
swapChain.Present(1, PresentFlags.None);
dc.Target = null;
```

---

## 3. 렌더 루프 / vsync

전용 렌더 스레드 1개:

```
loop (running):
    WaitForSingleObject(primarySwapChain.waitable, 100ms)   // vsync 페이싱
    coordinator.RenderFrame()    // 코디네이터가 모니터별 OverlayFrame 만들어 각 renderer.Render 호출
```

- `RenderFrame()`은 이미 락 안 스냅샷 → 락 밖 렌더 구조. 렌더 스레드에서 호출하면 됨.
- 멀티모니터: 모니터마다 swapchain·refresh 다를 수 있음. v1은 **주 모니터 waitable 1개로 60Hz 페이싱**(단순). 추후 모니터별 waitable 최적화.
- 대안: 고해상 타이머 ~16.6ms. waitable이 티어링·과도 렌더 방지에 더 낫다.

---

## 4. DPI + 좌표 변환 (★ 멀티모니터 버그의 1순위)

- 프로세스는 **Per-Monitor DPI v2**(app.manifest) — `GetCursorPos`는 **물리 픽셀**(가상 데스크톱).
- D2D `dc.Dpi = mon.Dpi`로 두면 **D2D 논리 단위 = DIP(96 기준)** = DESIGN.md 토큰 단위(pt)와 일치. 토큰 값(링 54pt 등)을 그대로 쓰면 됨.
- `OverlayFrame`의 좌표(CursorPosition·효과 Position 등)는 **물리 픽셀(가상 데스크톱)** →
  **윈도우 로컬 DIP**로 변환해 그린다:

```
localDip.x = (frame.pos.x - mon.PhysicalLeft) / mon.DpiScale
localDip.y = (frame.pos.y - mon.PhysicalTop)  / mon.DpiScale
```

- `mon.DpiScale` = 1.0(100%)/1.5(150%) 등. `MonitorInfo.DpiScale`에 이미 있음.
- `WM_DPICHANGED`/`WM_DISPLAYCHANGE` → 윈도우 리사이즈 + 스왑체인 `ResizeBuffers` + dc.Dpi 갱신.

> 이 변환을 한 곳(헬퍼)에 모아라. 흩어지면 모니터마다 어긋난다.

---

## 5. OverlayFrame 그리기 (Core 데이터 → D2D)

`OverlayFrame`(불변)을 받아 아래 순서로(뒤→앞):

| 요소 | Core 필드 | D2D 그리기 |
|------|----------|-----------|
| 스포트라이트 dim | (CursorRuntimeState.IsSpotlightActive, v1.1 일부) | 전체 어둡게 + 커서 원형 구멍 (radial gradient, edge softness) |
| 트레일/드래그트레일 | `Effects.Trail/DragTrail` | 점들 잇는 폴리라인, 끝으로 갈수록 fade |
| 스크롤 | `Effects.Scrolls` | 방향 화살표, magnitude 비례 크기 |
| 클릭/더블/흔들기/정지펄스 | `Effects.*` | 퍼지는 ring(애니메이션 — §5.1) |
| **커서 링** | `Ring`(색·반경·scale·opacity) + 설정(모양·두께·선스타일) | 원/squircle/마름모/육각, BorderWeight 두께, dashed 옵션 |
| 드래그 시각 | `Drag`(origin·current·anchoredLine·velocity·angle) | anchored line(origin→current), speed glow(velocity), 각도 라벨 |
| **그리기 도형** | `Shapes`(DrawingShape) | pen=폴리라인, line/arrow/rect/ellipse=[start,end], highlighter=굵은 반투명, badge=번호 원 |
| **라디얼 메뉴** | `Radial`(visible·center·sector·sub·subSub) | wedge들(RadialMenu 트리 라벨 + RadialHitTest 기하), 선택 강조 |
| 키스트로크 | `Keystroke` | 하단 중앙 텍스트 카드 |
| **코브랜딩** | `Branding`(로고·회사명) | 워터마크/스플래시 (코브랜딩 배포) |

색: `Rgba` → `new Color4(r/255, g/255, b/255, a/255)` (또는 D2D Premultiplied 주의).

### 5.1 효과 애니메이션 진행도

효과는 `ExpiresAt`만 들고 시작 시각이 없다. 렌더가 **효과 Id로 첫 등장 tick을 기록**해
경과(progress)를 계산한다:

```
firstSeen[effect.Id] ??= now
progress = (now - firstSeen[effect.Id]) / lifetime   // 0→1, ring 퍼짐·fade
```

(lifetime은 효과별 상수 — render 쪽에 같은 값 둠. 또는 ExpiresAt − firstSeen로 역산.)
Core의 `Prune(now)`가 만료분을 빼면 `firstSeen`에서도 제거.

### 5.2 링 모션 (스프링)

`RingVisual.Scale`은 v1에서 1.0(코디네이터). 클릭 시 squash·snap-back은 **render-side
스프링**: 새 ClickEffect를 보면 ring scale을 0.75로 줬다가 spring으로 1.0 복귀
(Mac DesignTokens.Motion.snap/returnTo 파라미터 = `Tokens.Motion.Snap/ReturnTo` 이식됨).
speed glow는 `Drag.Velocity`에서 파생(1000pt/s에서 max).

---

## 6. 그리기 모드 입력 충돌 해소 (외부보이스 P1)

클릭통과(`WS_EX_TRANSPARENT`)면 그리기 입력을 못 받고, 끄면 데스크톱 클릭이 막힌다.
**해소: 입력을 캡처해야 하는 모드에서만 클릭통과를 끈다.**

```
capturesInput = coordinator.IsDrawingModeActive || coordinator.IsRadialMenuActive
hwnd EX_TRANSPARENT:  capturesInput ? 제거 : 설정
```

- **그리기/라디얼 모드**: 클릭통과 OFF → 오버레이가 마우스를 먹어 밑 앱이 안 눌림(드래그로 그려도 버튼 안 클릭됨). 그리기 자체는 기존 하이브리드 입력(전역 후킹 + 프레임 샘플)이 구동.
- **평소**: 클릭통과 ON → 순수 시각, 모든 클릭은 밑 앱으로.
- 토글은 모드 전이 시 `SetWindowLong(GWL_EXSTYLE, ...)` 1회. 매 프레임 X.
- (Core에 `bool CapturesInput` 접근자 하나 추가하면 깔끔 — 지금은 두 accessor 조합으로 계산 가능.)

화면 캡처: v1 오버레이는 OBS/녹화에 **잡혀야**(효과가 녹화에 보임) → `WDA_NONE`(기본).
돋보기(v1.1) 자기재캡처 방지는 그때 `SetWindowDisplayAffinity` 검토.

---

## 7. 권장 구현 순서 (VM에서 점진적으로)

작은 것부터 — "보이는 것"이 빨리 나오게:

1. **빈 투명 클릭통과 윈도우** — 빨간 반투명 사각 하나 그려 데스크톱 위에 뜨는지(클릭 통과되는지) 확인. ← 스택 검증의 80%.
2. **커서 링** — `ICursorPositionSource`(GetCursorPos) + `OverlayFrame.Ring`만 그림. 좌표 변환(§4) 검증.
3. **`IMouseHook`/`IKeyboardHook`/`IHotkeyRegistrar`** — 입력 배선. 클릭 효과(ripple) 그리기.
4. **그리기 모드** — `WS_EX_TRANSPARENT` 토글(§6) + Shapes 그리기. P1 해소 실증.
5. **라디얼 메뉴** — `IRadialTrigger` + Radial wedge 그리기.
6. **멀티모니터·DPI** — 모니터별 윈도우, WM_DPICHANGED. ← 실하드웨어(미니PC) QA.
7. **Shell** — 트레이·설정창(WPF)·자동실행·발표앱 감지·브랜딩.

각 단계는 Core가 이미 데이터를 주므로(266 tests 통과), **그리기/입력 배선만** 하면 된다.

---

## 8. 함정 체크리스트

- [ ] `WS_EX_NOREDIRECTIONBITMAP` 빠뜨리면 → 윈도우 불투명(뒤 안 비침).
- [ ] 스왑체인 AlphaMode = **Premultiplied** + D2D 그릴 때 색도 premultiplied 고려.
- [ ] `dc.Dpi` 안 맞추면 → 100% 아닌 모니터에서 링 크기·위치 틀어짐.
- [ ] 좌표를 물리 픽셀 그대로 그리면 → 고DPI에서 작게/어긋남. **로컬 DIP 변환 필수.**
- [ ] 후킹 콜백에서 무거운 작업 → OS가 `WH_MOUSE_LL` 제거(T2). 콜백은 큐로 넘기고 즉시 반환.
- [ ] 렌더 스레드에서 코디네이터 호출 시, 입력 콜백 스레드와 `_gate` 락 공유 — 이미 코디네이터가 처리.
- [ ] `ResizeBuffers` 전 D2D 타깃/back buffer 참조 모두 해제 안 하면 실패.
