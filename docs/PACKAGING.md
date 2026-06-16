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

## 코드사이닝 (설계 T9)

서명하면 SmartScreen 경고/마찰이 줄고(EV는 즉시 평판), 변조 방지가 된다. 미서명 .exe도 실행은 되나 첫 실행 경고가 뜬다.

**스크립트** — `scripts/sign.ps1`은 `signtool`(Windows SDK)이 있으면 우선 사용, 없으면 PowerShell `Set-AuthenticodeSignature`로 폴백:

```powershell
# PFX로 서명(타임스탬프 포함 — 배포 시 권장)
pwsh scripts/sign.ps1 -File publish/win-x64/Cluxo.Windows.App.exe -PfxPath cert.pfx -PfxPassword $env:PFX_PW
# EV 토큰 등 인증서 저장소 thumbprint로
pwsh scripts/sign.ps1 -File ... -Thumbprint <thumbprint>
```

- **타임스탬프 필수**(기본 RFC3161 서버): 인증서 만료 후에도 기존 서명이 유효하게 유지된다.
- **파이프라인 테스트**: `scripts/make-test-cert.ps1`로 자체서명 인증서를 만들어 흐름을 검증할 수 있다(⚠ SmartScreen는 신뢰 안 함 — 배포엔 진짜 인증서 필요).

**인증서 발급**: OV(조직 검증) 또는 **EV**(확장 검증, 즉시 SmartScreen 평판·HSM/토큰 보관) 코드사이닝 인증서를 CA(DigiCert·Sectigo 등)에서 구입.

**CI 서명**(`.github/workflows/windows-app.yml`의 `publish` 잡, 수동 실행):
- repo secret `SIGN_PFX_BASE64`(PFX를 base64), `SIGN_PFX_PASSWORD` 등록(평문 커밋 금지).
- secret이 있으면 퍼블리시된 exe를 서명 후 아티팩트 업로드, 없으면 미서명으로 업로드.
- EV는 보통 HSM/토큰이라 클라우드 서명 서비스(예: Azure Trusted Signing, DigiCert KeyLocker) 연동이 필요 — 그 경우 `sign.ps1`을 해당 서비스 CLI로 교체.

## 남은 단계 (TODO)

- **MSI/설치 관리자**: WiX 등 필요(이 개발 환경 NuGet 프록시가 막아 오프라인 불가) — 네트워크 되는 빌드에서. 직접 배포(서명 .exe)를 우선(설계).
- **자동실행**: 설정창/트레이의 "로그인 시 실행"이 HKCU Run 키로 처리(설치관리자 불필요).

## 렌더 백엔드 메모

현재 오버레이 렌더는 **WPF 투명 윈도우 스톱갭**이다(이 개발 환경에서 Vortice NuGet 복원 불가). 네트워크 되는 환경에서 설계의 **DComp + Direct2D(Vortice)** 백엔드로 `IOverlayRenderer` 구현만 교체하면 성능/충실도가 올라간다(코디네이터·Core 불변).
