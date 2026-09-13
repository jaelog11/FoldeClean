# FoldeClean 배포 묶음 만들기 (winget 용)
# 실행: PowerShell 창에서   .\release.ps1 -GitHubUser <깃허브아이디>
#   1) 폴더 형태로 빌드 (build.cmd folder 와 동일, 서명 포함)
#   2) release\FoldeClean-<버전>-win-x64.zip 생성 + SHA256
#   3) release\manifests\j\Jaelog\FoldeClean\<버전>\ 에 winget 매니페스트 3개 생성 (버전·주소·해시 채움)
#   4) 다음 할 일을 출력
param([Parameter(Mandatory = $true)][string]$GitHubUser)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

$csproj = Get-Content "$here\FoldeClean\FoldeClean.csproj" -Raw
$ver = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $ver) { throw "csproj 에서 <Version> 을 찾지 못했습니다." }
# winget 은 세 자리 버전을 권장하므로 0.1 → 0.1.0
$wver = if (($ver -split '\.').Count -lt 3) { "$ver.0" } else { $ver }

Write-Host "== 1/3 빌드 (폴더 형태) v$ver"
& dotnet publish "$here\FoldeClean\FoldeClean.csproj" -c Release -o "$here\dist_folder" -nologo -p:PublishSingleFile=false -p:SelfContained=false -p:BaseIntermediateOutputPath=obj_pub/ -p:BaseOutputPath=bin_pub/
if ($LASTEXITCODE -ne 0) { throw "빌드 실패 (백신 예외 등록을 확인하세요)" }
Remove-Item "$here\dist_folder\*.xml" -Force -ErrorAction SilentlyContinue
& cmd /c "`"$here\sign.cmd`" `"$here\dist_folder\FoldeClean.exe`""
& cmd /c "`"$here\sign.cmd`" `"$here\dist_folder\FoldeClean.dll`""
Copy-Item "$here\LICENSE.txt" "$here\dist_folder\LICENSE.txt" -Force
Copy-Item "$here\README.md" "$here\dist_folder\README.md" -Force

Write-Host "== 2/3 zip"
$rel = "$here\release"; New-Item -ItemType Directory -Force $rel | Out-Null
$zip = "$rel\FoldeClean-$wver-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$here\dist_folder\*" -DestinationPath $zip -CompressionLevel Optimal
$sha = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host "   $zip"; Write-Host "   SHA256 $sha"

Write-Host "== 3/3 winget 매니페스트"
$mdir = "$rel\manifests\j\Jaelog\FoldeClean\$wver"; New-Item -ItemType Directory -Force $mdir | Out-Null
Get-ChildItem "$here\winget\*.yaml" | ForEach-Object {
    $txt = Get-Content $_.FullName -Raw
    $txt = $txt.Replace('__VERSION__', $wver).Replace('__GITHUB_USER__', $GitHubUser).Replace('__SHA256__', $sha)
    $txt = ($txt -split "`n" | Where-Object { $_ -notmatch '^\s*#' }) -join "`n"
    [IO.File]::WriteAllText("$mdir\$($_.Name)", $txt, (New-Object Text.UTF8Encoding $false))
}
Write-Host "   $mdir"
Write-Host ''
Write-Host "다음 할 일"
Write-Host " 1. GitHub 저장소 https://github.com/$GitHubUser/FoldeClean 에 코드를 올리고 태그 v$wver 로 Release 를 만든 뒤,"
Write-Host "    $zip 파일을 그 Release 에 첨부합니다 (파일 이름 그대로)."
Write-Host " 2. 매니페스트 검증:  winget validate --manifest `"$mdir`""
Write-Host " 3. 로컬 설치 시험:  winget install --manifest `"$mdir`"   (설정에서 로컬 매니페스트 허용 필요: winget settings --enable LocalManifestFiles)"
Write-Host " 4. 제출: https://github.com/microsoft/winget-pkgs 를 포크해 manifests\j\Jaelog\FoldeClean\$wver\ 에 세 파일을 넣고 Pull Request."
Write-Host "    또는 wingetcreate 도구:  wingetcreate submit --token <GitHub토큰> `"$mdir`""
