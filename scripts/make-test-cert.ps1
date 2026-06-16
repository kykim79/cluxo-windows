<#
  사이닝 파이프라인 테스트용 자체서명 코드사이닝 인증서 생성.
  ⚠ 테스트 전용 — SmartScreen는 신뢰하지 않는다(배포엔 OV/EV 인증서 필요, docs/PACKAGING.md).

  사용:
    pwsh scripts/make-test-cert.ps1                 # test-cert.pfx (비번 test1234)
  정리:
    Remove-Item Cert:\CurrentUser\My\<thumbprint>
#>
param(
    [string]$OutPfx = "test-cert.pfx",
    [string]$Password = "test1234",
    [string]$Subject = "CN=Cluxo Test Signing"
)
$ErrorActionPreference = "Stop"

$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject `
    -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature -KeySpec Signature -NotAfter (Get-Date).AddYears(2)

$pwd = ConvertTo-SecureString $Password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $OutPfx -Password $pwd | Out-Null

Write-Host "자체서명 인증서 생성:"
Write-Host "  thumbprint: $($cert.Thumbprint)"
Write-Host "  pfx:        $OutPfx (비번: $Password)"
Write-Host "  정리:        Remove-Item Cert:\CurrentUser\My\$($cert.Thumbprint)"
