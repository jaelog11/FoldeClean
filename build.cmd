@echo off
rem FoldeClean 빌드 (Visual Studio 2026 / .NET 10 SDK 필요)
rem   build.cmd            단일 exe   → dist\FoldeClean.exe            (배포 간편, 백신 의심도 다소 높음)
rem   build.cmd folder     폴더 형태  → dist_folder\FoldeClean.exe + DLL들 (백신 의심도 낮음, 폴더째 복사)
rem 빌드 후 sign.cmd 로 서명한다 (인증서가 없으면 건너뜀).
rem 백신(Bitdefender 등)이 FoldeClean.dll 을 격리하면 "액세스 거부"로 실패합니다. 백신 예외에 이 폴더를 추가하세요.
cd /d "%~dp0"
if /i "%~1"=="folder" (
  set OUT=dist_folder
  dotnet publish FoldeClean\FoldeClean.csproj -c Release -o dist_folder -nologo -p:PublishSingleFile=false -p:SelfContained=false -p:BaseIntermediateOutputPath=obj_pub/ -p:BaseOutputPath=bin_pub/
) else (
  set OUT=dist
  dotnet publish FoldeClean\FoldeClean.csproj -c Release -o dist -nologo -p:BaseIntermediateOutputPath=obj_pub/ -p:BaseOutputPath=bin_pub/
)
if errorlevel 1 (echo 빌드 실패 & pause & exit /b 1)
del /q %OUT%\*.xml 2>nul
call "%~dp0sign.cmd" "%~dp0%OUT%\FoldeClean.exe"
if exist "%~dp0%OUT%\FoldeClean.dll" call "%~dp0sign.cmd" "%~dp0%OUT%\FoldeClean.dll"
echo.
echo 완료: %~dp0%OUT%\FoldeClean.exe
pause
