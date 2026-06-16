<#
  Cluxo for Windows 코드사이닝(Authenticode).
  signtool(Windows SDK)이 있으면 우선 사용, 없으면 PowerShell Set-AuthenticodeSignature로 폴백.

  사용:
    # PFX 파일로 서명(로컬/CI)
    pwsh scripts/sign.ps1 -File publish/win-x64/Cluxo.Windows.App.exe -PfxPath cert.pfx -PfxPassword $env:PFX_PW
    # 인증서 저장소 thumbprint로 서명(EV 토큰 등)
    pwsh scripts/sign.ps1 -File ... -Thumbprint ABC123...
    # 오프라인 테스트는 타임스탬프 생략
    pwsh scripts/sign.ps1 -File ... -PfxPath test.pfx -PfxPassword test1234 -TimestampUrl ''

  타임스탬프는 인증서 만료 후에도 서명 유효를 위해 필수(배포 시). 기본 RFC3161 서버 사용.
#>
param(
    [Parameter(Mandatory)][string]$File,
    [string]$PfxPath,
    [string]$PfxPassword,
    [string]$Thumbprint,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path $File)) { throw "파일 없음: $File" }
if (-not $Thumbprint -and -not $PfxPath) { throw "-Thumbprint 또는 -PfxPath 중 하나가 필요합니다." }

$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'x64' } | Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if ($signtool) {
    $a = @('sign', '/fd', 'SHA256')
    if ($TimestampUrl) { $a += @('/tr', $TimestampUrl, '/td', 'SHA256') }
    if ($Thumbprint) { $a += @('/sha1', $Thumbprint) }
    else { $a += @('/f', $PfxPath); if ($PfxPassword) { $a += @('/p', $PfxPassword) } }
    $a += $File
    Write-Host "signtool: $signtool"
    & $signtool @a
    if ($LASTEXITCODE -ne 0) { throw "signtool 실패 (exit $LASTEXITCODE)" }
}
else {
    Write-Warning "signtool 없음(Windows SDK 미설치) — Set-AuthenticodeSignature 폴백."
    $cert = if ($Thumbprint) {
        Get-Item "Cert:\CurrentUser\My\$Thumbprint"
    } else {
        New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $PfxPassword)
    }
    $params = @{ FilePath = $File; Certificate = $cert; HashAlgorithm = 'SHA256' }
    if ($TimestampUrl) { $params.TimestampServer = $TimestampUrl }
    $r = Set-AuthenticodeSignature @params
    if ($r.Status -notin 'Valid', 'UnknownError', 'NotTrusted') {
        throw "서명 실패: $($r.Status) — $($r.StatusMessage)"
    }
}

$sig = Get-AuthenticodeSignature $File
Write-Host "서명됨: status=$($sig.Status), signer=$($sig.SignerCertificate.Subject)"
if (-not $sig.SignerCertificate) { throw "서명이 적용되지 않았습니다." }
