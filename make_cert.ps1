# FoldeClean 자체 서명 인증서 만들기 (무료)
# 실행: PowerShell 창에서   .\make_cert.ps1
#   1) "FoldeClean · Jaelog By 아지랑이" 이름의 코드 서명 인증서를 만든다 (유효 10년)
#   2) 이 PC의 '신뢰할 수 있는 루트'와 '신뢰할 수 있는 게시자'에 넣어 서명이 유효하게 한다
#   3) 다른 PC에 배포할 때 설치할 수 있도록 FoldeClean-cert.cer 를 내보낸다
#   4) sign.cmd 가 자동으로 찾도록 지문을 sign.thumbprint 파일에 적는다
# 이 인증서는 이 PC에서만 인정된다. 다른 PC에서는 FoldeClean-cert.cer 를 더블클릭 → 인증서 설치 →
# 로컬 컴퓨터 → "신뢰할 수 있는 루트 인증 기관" 에 넣으면 같은 효과를 얻는다.

$ErrorActionPreference = 'Stop'
$subject = 'CN=FoldeClean, O=Jaelog By 아지랑이'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$existing = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
if ($existing) {
    Write-Host "이미 인증서가 있습니다: $($existing.Thumbprint)"
    $cert = $existing
} else {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\CurrentUser\My `
        -KeyExportPolicy Exportable -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(10) -FriendlyName 'FoldeClean code signing'
    Write-Host "인증서를 만들었습니다: $($cert.Thumbprint)"
}

# 이 PC에서 신뢰하도록 등록 (현재 사용자 범위)
$cerPath = Join-Path $here 'FoldeClean-cert.cer'
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
foreach ($store in 'Root', 'TrustedPublisher') {
    $s = New-Object System.Security.Cryptography.X509Certificates.X509Store($store, 'CurrentUser')
    $s.Open('ReadWrite')
    if (-not ($s.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint })) { $s.Add($cert) }
    $s.Close()
}
Set-Content -Path (Join-Path $here 'sign.thumbprint') -Value $cert.Thumbprint -Encoding ASCII
Write-Host ''
Write-Host "완료."
Write-Host "  지문        : $($cert.Thumbprint)  (sign.thumbprint 에 저장됨, build.cmd 가 자동 사용)"
Write-Host "  배포용 인증서: $cerPath  (다른 PC에 설치하면 그 PC에서도 서명이 인정됨)"
