# FoldeClean · 안전한 폴더 정리

프로그램이나 시스템과 연결된 파일을 미리 찾아내고, 검증된 정리 체계 중 하나를 골라 파일을 깔끔하게 정리하는 Windows 프로그램입니다. 모든 이동은 기록되며 한 번에 되돌릴 수 있습니다.

## 실행

`dist\FoldeClean.exe` 를 더블클릭하면 됩니다. 파이썬이나 .NET 설치가 필요 없는 단독 실행 파일입니다.
(Windows 11에 기본 포함된 WebView2 런타임을 사용합니다.)

## 빌드 (Visual Studio 2026)

- `FoldeClean.sln` 을 Visual Studio 2026에서 열고 빌드하면 됩니다. 필요한 구성 요소는 ".NET 데스크톱 개발" 워크로드입니다.
- 명령줄로 단독 exe를 만들려면 `build.cmd` 를 실행하거나 아래를 입력합니다.

```bash
dotnet publish FoldeClean\FoldeClean.csproj -c Release -o dist
```

결과물은 `dist\FoldeClean.exe` 하나입니다 (약 60MB, .NET 런타임 포함).

주의: Bitdefender 같은 백신이 빌드 직후의 FoldeClean.dll을 랜섬웨어 유사 행동(파일 대량 이동)으로 오탐해 격리하면 "접근 거부"로 빌드가 실패합니다. 백신 예외 목록에 이 프로젝트 폴더(`C:\JU_Pro\U4_FoldeClean`)를 추가한 뒤 빌드하세요. 실행 중인 FoldeClean 창도 닫아야 `dist\FoldeClean.exe`를 덮어쓸 수 있습니다.

## 코드 서명과 백신 오탐

파일을 대량으로 옮기는 프로그램이라 백신(Bitdefender 등)이 랜섬웨어로 오탐할 수 있습니다. 무료로 할 수 있는 대응은 다음과 같습니다.

1. **백신 예외 등록**: 이 프로젝트 폴더를 백신 예외에 추가합니다. 빌드가 되려면 필수입니다.
2. **오탐 신고**: Bitdefender 오탐 신고 페이지(https://www.bitdefender.com/consumer/support/answer/29358/)에 `dist\FoldeClean.exe`를 올립니다. 며칠 뒤 화이트리스트에 오릅니다. 새로 빌드하면 다시 신고해야 합니다.
3. **자체 서명(무료)**: PowerShell에서 `.\make_cert.ps1`을 한 번 실행하면 인증서를 만들고 이 PC에 등록하며, 이후 `build.cmd`가 자동으로 서명합니다. 다른 PC에서는 함께 생성된 `FoldeClean-cert.cer`를 더블클릭해 "신뢰할 수 있는 루트 인증 기관"에 설치하면 그 PC에서도 서명이 인정됩니다.
4. **폴더 형태 빌드**: `build.cmd folder`로 만들면 압축된 단일 exe 대신 exe와 DLL이 있는 폴더가 나옵니다. 자가추출 exe보다 백신 의심도가 낮습니다. 실행에는 .NET 10 런타임이 필요합니다.
5. **오픈소스라면**: SignPath 재단(https://signpath.org/)이 공개 저장소의 오픈소스 프로그램에 진짜 인증서 서명을 무료로 제공합니다.

유료 인증서(OV/EV)를 나중에 구입하면 토큰을 꽂고 `sign.cmd`의 `THUMBPRINT=`에 지문을 적기만 하면 됩니다. 타임스탬프 서버를 지정하므로 인증서가 만료돼도 이전 서명은 유효합니다.

## winget 배포

배포 채널은 winget입니다. 사용자는 아래 한 줄로 설치하고, 새 버전이 나오면 `winget upgrade`로 갱신합니다.

```bash
winget install Jaelog.FoldeClean
```

배포 절차 (제작자):
1. `FoldeClean.csproj`의 `<Version>`을 올리고 `CHANGELOG.md`에 내용을 적습니다.
2. PowerShell에서 `.\release.ps1 -GitHubUser <깃허브아이디>` 를 실행합니다. 폴더 형태로 빌드·서명하고 `release\FoldeClean-<버전>-win-x64.zip`과 winget 매니페스트 3개(`release\manifests\...`)를 만듭니다.
3. GitHub 저장소에 태그 `v<버전>`으로 Release를 만들고 zip을 첨부합니다. 파일 이름은 그대로 둡니다.
4. `winget validate --manifest <매니페스트 폴더>` 로 검증하고, `winget install --manifest <폴더>` 로 이 PC에서 설치를 시험합니다.
5. microsoft/winget-pkgs 저장소에 매니페스트 3개를 Pull Request로 제출합니다. `wingetcreate submit` 도구를 쓰면 자동입니다. 심사는 보통 며칠입니다.

매니페스트는 zip 안의 `FoldeClean.exe`를 "포터블" 방식으로 설치하고, .NET 10 데스크톱 런타임을 의존성으로 선언해 없으면 함께 설치되게 합니다.

## 언어

왼쪽 아래 "언어"에서 한국어, English, 中文, 日本語를 고를 수 있습니다. 화면 문구뿐 아니라 검사 사유와 정리 결과 폴더 이름(문서 / Documents / 文档 / 書類)도 바뀝니다. 선택은 기억됩니다.
문구를 고치거나 언어를 더하려면 `ui/i18n.js`(화면)와 `FoldeClean/Core/I18n.cs`(폴더 이름)를 수정합니다.

## 버전과 소유주

버전은 `FoldeClean/FoldeClean.csproj`의 `<Version>` 한 곳에서 관리합니다. 0.1부터 시작해 기능이 더해질 때마다 0.2, 0.3으로 올립니다. 변경 내용은 `CHANGELOG.md`, 앞으로의 계획은 `ROADMAP.md`에 있습니다.
소유주 표시는 `Api.cs`의 `Owner`와 csproj의 `<Company>`에 있습니다.

## 사용 흐름

1. **폴더 선택**: 정리할 폴더를 고릅니다. 다운로드, 바탕화면 등은 버튼 한 번으로 선택됩니다.
2. **검사**: 파일 구성과 안전 등급을 봅니다.
   - 차단: 시스템 폴더, 프로그램 폴더(exe+dll), 레지스트리 등록 경로, 사용 중인 파일. 절대 옮기지 않습니다.
   - 주의: 바로가기가 가리키는 파일, 실행파일, 프로젝트 폴더 내부(.git, package.json 등), 클라우드 동기화 폴더, 숨김 파일. 기본 제외이며 옵션으로 포함할 수 있습니다.
3. **정리 방식**: 7가지 중 선택. 카드를 고르면 예시 애니메이션과 옵션이 나옵니다.
   - 종류별 · 날짜별 · 종류→연도 · PARA · Johnny.Decimal · 오래된 파일 보관 · 중복 파일 정리
4. **미리보기**: 출발 폴더에서 도착 폴더로 흐르는 흐름도, 정리 후 폴더 구조, 이동 목록을 확인합니다. 원하지 않는 항목은 체크해서 뺄 수 있습니다.
5. **실행**: 진행률을 보며 이동합니다. 끝난 뒤 "방금 정리 되돌리기"로 원상복구할 수 있고, 왼쪽 아래 "되돌리기 기록"에서 이전 정리도 되돌릴 수 있습니다.

되돌리기 기록은 `%LOCALAPPDATA%\FoldeClean\journals` 에 저장됩니다.

## 성능

- 탐색은 파일 2만 개를 1초 안에 읽습니다.
- 중복 검사는 크기 → 앞 64KB 해시 → 전체 해시 3단계라 큰 파일이 많아도 빠릅니다.
- 이동 목록은 화면에 보이는 행만 그립니다. 수만 건도 부드럽게 스크롤됩니다.
- 사용 중(잠긴) 파일 검사는 8개 스레드로 병렬 처리하고 최대 20초, 5,000개까지만 확인합니다. 클라우드 자리표시자(OneDrive 등 아직 내려받지 않은 파일)는 열면 내려받기가 시작되므로 건너뜁니다. 폴더 선택 화면에서 끌 수도 있습니다.
- 2만 6천 개 파일 기준: 탐색 0.05초, 바로가기·레지스트리 수집 2초, 연결 파일 검사 1.5초.

## 구조

```
FoldeClean.sln              Visual Studio 2026 솔루션
FoldeClean/
  FoldeClean.csproj         WPF + WebView2, 단독 exe 설정
  MainWindow.xaml(.cs)      창, JS ↔ C# 통신
  Api.cs                    화면에서 부르는 API
  Core/Scanner.cs           빠른 탐색
  Core/Safety.cs            연결 파일 감지 (시스템 경로, .lnk 대상, 레지스트리, 잠김, 프로젝트, 클라우드)
  Core/Strategies.cs        정리 방식 7종
  Core/Dupes.cs             중복 탐지
  Core/Planner.cs           이동 계획, 이름 충돌 처리, 흐름 집계
  Core/Executor.cs          이동 실행, 기록, 되돌리기
ui/                         화면 (HTML/CSS/JS). exe 안에 내장됨
build.cmd                   단독 exe 빌드 (folder 옵션: 폴더 형태), 빌드 후 자동 서명
sign.cmd                    exe 서명 (인증서 지문이 있을 때만)
make_cert.ps1               무료 자체 서명 인증서 생성·등록
make_icon.py                앱 아이콘 생성 (FoldeClean/app.ico, ui/icon.png)
dist/FoldeClean.exe         빌드 결과

app.py, core/               같은 기능의 파이썬 판 (개발·검증용, py -3.11 app.py)
make_sample.py              테스트용 가짜 폴더 생성
```

## 자체 점검

```bash
dist\FoldeClean.exe --selftest 폴더경로
```
지정 폴더를 검사·계획·실행·되돌리기까지 돌려 보고 결과를 상위 폴더의 `selftest.json` 에 기록합니다. 실제 파일이 이동되었다가 복원되므로 반드시 테스트용 폴더에만 쓰세요.

## 테스트용 가짜 데이터

```bash
py -3.11 make_sample.py
```
`sample_data/` 에 가짜 파일 600개가 만들어집니다. 실제 자료 없이 모든 기능을 시험할 수 있습니다.
