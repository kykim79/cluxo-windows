# Cluxo for Windows

발표·강의·화면 녹화에서 **마우스 커서를 눈에 띄게** 강조해 주는 도구. macOS [Cluxo](https://github.com/kykim79/Cluxo)의 네이티브 Windows 재구현입니다.

커서 둘레에 강조 링을 그리고, 클릭·스크롤·드래그에 시각 효과를 더하며, 스포트라이트·돋보기·키 입력 표시·화면 주석(그리기) 등 발표에 필요한 기능을 트레이에서 조용히 제공합니다.

> 상태: 활발히 개발 중. 최신 빌드는 [Releases](https://github.com/kykim79/cluxo-windows/releases)에서 받을 수 있습니다.

## 주요 기능

- **커서 강조 링** — 색·모양(원/사각/마름모/육각)·크기·투명도·이중 링·채우기·호흡(맥박처럼 천천히 스케일)
- **클릭/스크롤/드래그 효과** — 클릭 리플, 더블클릭, 스크롤 화살표, 코멧 꼬리, 정지 펄스, 흔들기로 찾기
- **스포트라이트** — 커서 주변만 남기고 화면을 어둡게
- **돋보기** — 커서 주변 실시간 확대 (Windows Magnification API)
- **키 입력 표시** — 누른 단축키를 화면 하단에 크게 자막처럼
- **라디얼 메뉴** — 가운데 버튼(또는 `Ctrl+Alt+.`)으로 빠르게 색·모양·효과 전환
- **그리기/주석 모드** — 화면 위에 펜·도형·화살표·형광펜으로 주석 (이동 가능한 플로팅 툴바)
- **좌표/각도 표시**, **커스텀 색 선택**
- **발표 자동 감지** — Zoom·OBS·Teams·PowerPoint 등이 켜지면 자동 활성화, 낯선 외장 모니터 연결 시 키 입력 표시 자동 ON
- **트레이 활성/비활성 토글**, **로그인 시 자동 실행**, **다국어**(시스템/한국어/English)
- **자동 업데이트 확인**

## 설치

[Releases](https://github.com/kykim79/cluxo-windows/releases)에서 둘 중 하나를 받으세요 (Windows 10/11):

- **`Cluxo-Setup-x.y.z.exe`** — 설치 프로그램 (Program Files 설치 + 시작 메뉴 + 제거 프로그램)
- **`Cluxo-x.y.z-win-x64.exe`** — 무설치 포터블 단일 실행 파일

> 아직 코드 서명이 없어 첫 실행 시 SmartScreen 경고가 뜰 수 있습니다 — **추가 정보 → 실행**으로 진행하세요.

## 단축키

모든 단축키는 `Ctrl+Alt` 조합입니다 (환경설정 → 단축키에서 일부 변경 가능).

| 단축키 | 기능 |
|--------|------|
| `Ctrl+Alt+.` (또는 가운데 버튼) | 라디얼 메뉴 |
| `Ctrl+Alt+D` | 그리기 모드 |
| `Ctrl+Alt+S` | 스포트라이트 |
| `Ctrl+Alt+M` | 돋보기 |
| `Ctrl+Alt+K` | 키 입력 표시 |
| `Ctrl+Alt+I` | 좌표/각도 표시 |
| `Ctrl+Alt+C` / `H` | 색 / 모양 순환 |
| `Ctrl+Alt+1`~`7` | 색 직접 선택 |

> 참고: 일부 원격 데스크톱·VM 클라이언트는 `Ctrl+Alt+<글자>` 조합을 가로채 단축키가 안 먹을 수 있습니다. 그 경우 라디얼 메뉴(가운데 버튼)로 모든 기능을 쓸 수 있습니다.

## 소스에서 빌드

[.NET 8 SDK](https://dotnet.microsoft.com/download) 필요 (Windows).

```powershell
dotnet build Cluxo.Windows.sln -c Release   # 빌드
dotnet test  Cluxo.Windows.sln              # 단위 테스트
pwsh scripts/publish.ps1                     # 배포용 self-contained 단일 exe 생성
```

## 아키텍처

- **`Cluxo.Core`** — 플랫폼 무관 순수 로직(효과·설정·라디얼·그리기 상태 등)을 단위 테스트와 함께 보유. macOS 버전의 순수 함수 패턴을 C#으로 이식.
- **네이티브 Windows 계층** — 입력(Win32 저수준 후킹·전역 단축키), 렌더(투명 오버레이 윈도우), Shell(트레이·모니터·설정)이 `Cluxo.Core.Platform` 인터페이스를 구현하고 `OverlayCoordinator`가 배선.

계층별 상세 설계는 [`docs/`](docs/)를 참고하세요.

## 라이선스

MIT License — 자세한 내용은 [LICENSE](LICENSE) 참조. (macOS Cluxo와 동일)
