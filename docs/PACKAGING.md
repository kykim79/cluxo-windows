# 패키징 / 배포

Cluxo for Windows를 고객사에 배포하기 위한 산출물 만들기.

## 아이콘

앱 아이콘은 외부 에셋 없이 **앱 자체 WPF 렌더로 생성**한다(`Ui/IconMaker.cs`) — 검은 둥근 타일 + 노란(시스템 yellow) 커서 링 + 흰 점(맥 Cluxo와 동일), 멀티사이즈 .ico.

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

## 릴리스 (태그 → GitHub 릴리스)

`.github/workflows/release.yml`: **`v*` 태그를 push하면** windows runner에서 빌드·테스트·퍼블리시하고(시크릿 있으면 서명) **GitHub 릴리스를 자동 생성**하며 `Cluxo-<버전>-win-x64.exe`를 첨부한다.

```bash
git tag v1.0.0
git push origin v1.0.0        # → CI가 릴리스 + exe 첨부
```

- 수동 실행도 가능(Actions → Release → Run workflow → 태그 입력).
- 버전 표기는 태그에서 온다. exe 파일 버전(속성)은 csproj `<Version>`을 따르므로, 올릴 때 같이 bump 권장.
- 미서명이면 다운로드·실행 시 **SmartScreen 경고**가 뜬다(아래 코드사이닝 참고).

## 관리자 권한 (UIPI)

`app.manifest`에 **`requireAdministrator`**가 있어 실행 시 **UAC 동의**가 필요하다. 일반 권한 프로세스는 관리자 권한 창(예: elevated 터미널) 위에서 입력을 후킹할 수 없기 때문(가운데 버튼 라디얼·클릭 효과 등이 그런 창 위에서 안 먹음).

- **자동시작 주의**: 관리자 앱은 HKCU Run 키로는 로그인 시 자동 시작이 막힌다. 자동시작이 필요하면 **작업 스케줄러(가장 높은 권한으로 실행)** 로 등록해야 한다.
- **UAC 없이** 관리자 창 위에서 쓰려면(제대로 된 방식): `uiAccess="true"` + **코드서명** + **Program Files 설치**. 본인 PC면 자체서명으로 무료 가능, 배포면 공인 인증서 필요.

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
- **자동실행**: "로그인 시 실행"은 HKCU Run 키 처리지만, 현재 `requireAdministrator`라 관리자 앱 자동시작은 작업 스케줄러가 필요(위 "관리자 권한" 참고).

## 렌더 백엔드 메모

현재 오버레이 렌더는 **WPF 투명 윈도우 스톱갭**이다(이 개발 환경에서 Vortice NuGet 복원 불가). 네트워크 되는 환경에서 설계의 **DComp + Direct2D(Vortice)** 백엔드로 `IOverlayRenderer` 구현만 교체하면 성능/충실도가 올라간다(코디네이터·Core 불변).
