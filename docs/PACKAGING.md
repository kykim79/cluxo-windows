# 패키징 / 배포

Cluxo for Windows를 고객사에 배포하기 위한 산출물 만들기.

## 아이콘

앱 아이콘은 외부 에셋 없이 **앱 자체 WPF 렌더로 생성**한다(`Ui/IconMaker.cs`) — 사이안 링 글리프 멀티사이즈 .ico.

```powershell
# Assets\cluxo.ico 재생성 (한 번 만들어 커밋해 둠)
dotnet run --project src/Cluxo.Windows.App -- --make-icon src/Cluxo.Windows.App/Assets/cluxo.ico
```

- `<ApplicationIcon>`로 exe 파일 아이콘 임베드. 트레이는 런타임에 출력 폴더의 `Assets\cluxo.ico`를 `LoadImage`로 로드(코브랜딩 시 회사 .ico로 교체 가능).

## 퍼블리시

```powershell
pwsh scripts/publish.ps1                      # win-x64, self-contained 단일 exe (권장)
pwsh scripts/publish.ps1 -Rid win-arm64       # ARM64
pwsh scripts/publish.ps1 -FrameworkDependent  # 작은 용량(대상에 .NET 8 Desktop 런타임 필요)
```

| 방식 | 용량 | 대상 요건 | 비고 |
|------|------|----------|------|
| self-contained 단일 exe | ~170MB | 없음(.NET 내장) | **고객사 무설치 실행**(T4). 첫 실행 시 네이티브 압축해제(일회성, 수초) |
| framework-dependent | ~수 MB | .NET 8 Desktop Runtime | 사내/관리 환경 |

공통: **ReadyToRun**(csproj `PublishReadyToRun`)으로 cold-start JIT 절감(~150-200ms). `DebugType=none`으로 pdb 미포함.

## 남은 단계 (TODO)

- **코드사이닝**(설계 T9): OV(가능하면 EV) 인증서로 `signtool` 서명 → SmartScreen 마찰 제거. 인증서·키는 repo secret(평문 커밋 금지). 미서명 .exe도 동작하나 첫 실행 경고가 뜬다.
- **MSI/설치 관리자**: WiX 등 필요(이 개발 환경 NuGet 프록시가 막아 오프라인 불가) — 네트워크 되는 빌드에서. 직접 배포(서명 .exe)를 우선(설계).
- **자동실행**: 설정창/트레이의 "로그인 시 실행"이 HKCU Run 키로 처리(설치관리자 불필요).
- **CI**: `.github/workflows/windows-app.yml`의 T9 단계에 서명·아티팩트 업로드 추가 예정.

## 렌더 백엔드 메모

현재 오버레이 렌더는 **WPF 투명 윈도우 스톱갭**이다(이 개발 환경에서 Vortice NuGet 복원 불가). 네트워크 되는 환경에서 설계의 **DComp + Direct2D(Vortice)** 백엔드로 `IOverlayRenderer` 구현만 교체하면 성능/충실도가 올라간다(코디네이터·Core 불변).
