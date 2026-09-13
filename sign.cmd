@echo off
rem FoldeClean exe 코드 서명
rem 사용법:  sign.cmd [서명할 파일]        (기본값 dist\FoldeClean.exe)
rem 지문 우선순위: 1) 아래 THUMBPRINT  2) sign.thumbprint 파일 (make_cert.ps1 이 만듦)
rem 유료 인증서를 쓰면 토큰을 꽂고 THUMBPRINT 에 지문(40자리)을 적는다. 둘 다 없으면 서명을 건너뛴다.
setlocal
set THUMBPRINT=
set TIMESTAMP=http://timestamp.digicert.com
set TARGET=%~1
if "%TARGET%"=="" set TARGET=%~dp0dist\FoldeClean.exe

if "%THUMBPRINT%"=="" if exist "%~dp0sign.thumbprint" set /p THUMBPRINT=<"%~dp0sign.thumbprint"
if "%THUMBPRINT%"=="" (
  echo [sign] 인증서 지문이 없어 서명을 건너뜁니다. 무료 자체 서명은 make_cert.ps1 을 실행하세요.
  exit /b 0
)

set SIGNTOOL=
for /f "delims=" %%p in ('dir /b /s /o-n "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" 2^>nul') do if not defined SIGNTOOL set SIGNTOOL=%%p
if not defined SIGNTOOL (
  echo [sign] signtool.exe 를 찾지 못했습니다. Windows SDK 를 설치하세요.
  exit /b 1
)

echo [sign] %TARGET%
"%SIGNTOOL%" sign /fd SHA256 /td SHA256 /tr %TIMESTAMP% /sha1 %THUMBPRINT% "%TARGET%"
if errorlevel 1 (echo [sign] 서명 실패 & exit /b 1)
"%SIGNTOOL%" verify /pa "%TARGET%" >nul 2>&1 && (echo [sign] 검증 성공: 이 PC에서 신뢰되는 서명) || (echo [sign] 서명은 됐지만 이 PC가 인증서를 신뢰하지 않습니다. make_cert.ps1 또는 FoldeClean-cert.cer 설치 필요)
echo [sign] 완료
endlocal
