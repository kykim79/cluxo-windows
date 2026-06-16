<#
  Cluxo for Windows 배포 퍼블리시.
  기본: self-contained 단일파일 + ReadyToRun (고객사가 .NET 설치 없이 실행 — 설계 T4).

  사용:
    pwsh scripts/publish.ps1                      # win-x64, self-contained 단일 exe
    pwsh scripts/publish.ps1 -Rid win-arm64       # ARM64
    pwsh scripts/publish.ps1 -FrameworkDependent  # 작은 용량(대상에 .NET 8 Desktop 런타임 필요)

  산출물: publish/<rid>[-fd]/Cluxo.Windows.App.exe
  코드사이닝(SmartScreen 마찰 제거)·MSI는 별도 단계 — docs/PACKAGING.md 참고.
#>
param(
    [string]$Rid = "win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$proj = Join-Path $PSScriptRoot "..\src\Cluxo.Windows.App\Cluxo.Windows.App.csproj"
$selfContained = (-not $FrameworkDependent).ToString().ToLower()
$out = "publish/$Rid" + $(if ($FrameworkDependent) { "-fd" } else { "" })

dotnet publish $proj -c Release -r $Rid `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $out

$exe = Join-Path $out "Cluxo.Windows.App.exe"
if (Test-Path $exe) {
    $mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "✓ Published: $exe ($mb MB)"
} else {
    throw "Publish failed — exe not found in $out"
}
