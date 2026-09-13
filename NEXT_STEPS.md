# 따라 하기: winget 배포부터 Pro 판매까지

각 단계는 **[사용자]** 가 직접 하는 일과 **[Claude]** 가 하는 일로 나뉩니다. 사용자 단계가 끝나면 채팅에 "N단계 완료"라고 알려주세요. 그러면 제가 다음 단계를 이어갑니다.
현재 위치: **1단계** (2026-09-13 기준. 백신 예외 등록과 빌드는 끝남)

---

## 1단계. GitHub 계정과 저장소 만들기 (약 10분)  [사용자]

GitHub는 소스 코드를 올리는 무료 저장소이고, winget은 여기 올린 zip 파일 주소를 가리킵니다.

1. https://github.com 에서 계정을 만듭니다(이미 있으면 로그인). 아이디는 영문입니다. 예: `jaelog`.
2. 오른쪽 위 **+** → **New repository**.
   - Repository name: `FoldeClean` (이 이름이어야 준비된 파일과 맞습니다)
   - Public 선택 (winget은 공개 주소가 필요합니다)
   - "Add a README" 등은 모두 체크하지 않음
   - **Create repository**
3. 채팅에 **"1단계 완료, 아이디는 ○○○"** 라고 알려주세요.

## 2단계. 소스 올리기 (약 5분)  [Claude → 사용자 한 번 확인]

1. [Claude] 이 폴더를 git 저장소로 만들고 첫 커밋을 만듭니다. `.gitignore` 로 빌드 결과물·샘플 데이터·서명 키는 제외됩니다.
2. [사용자] 제가 알려드리는 `git push` 명령을 PowerShell에서 실행합니다. 처음 한 번 GitHub 로그인 창이 뜨면 브라우저로 로그인합니다.

```powershell
cd C:\JU_Pro\U4_FoldeClean
git push -u origin main
```

3. 브라우저에서 https://github.com/<아이디>/FoldeClean 에 파일이 보이면 완료.

## 3단계. 배포 묶음 만들기 (약 5분)  [Claude]

`release.ps1 -GitHubUser <아이디>` 를 실행해 다음을 만듭니다.
- `release\FoldeClean-0.1.0-win-x64.zip` (폴더 빌드, 서명, 라이선스 포함)
- `release\manifests\j\Jaelog\FoldeClean\0.1.0\` 안에 winget 매니페스트 3개 (버전·주소·해시가 채워짐)

## 4단계. 백신 오탐 확인과 신고 (약 20분)  [사용자]

zip을 여러 백신으로 검사해 잡히는 곳에 "정상 프로그램"이라고 알립니다. 무료입니다.

1. https://www.virustotal.com 접속 → **Choose file** → `C:\JU_Pro\U4_FoldeClean\release\FoldeClean-0.1.0-win-x64.zip` 선택.
2. 결과 화면에서 빨간색(탐지)으로 표시된 백신 이름을 채팅에 알려주세요. (0개면 그대로 진행)
3. 탐지한 백신마다 오탐 신고. 자주 쓰는 곳:
   - Bitdefender: https://www.bitdefender.com/consumer/support/answer/29358/
   - Microsoft: https://www.microsoft.com/wdsi/filesubmission
   - Avast/AVG: https://www.avast.com/false-positive-file-form.php
   - Kaspersky: https://opentip.kaspersky.com/
   - ESET: samples@eset.com 으로 zip 첨부 (제목: False positive)
   신고 양식에 "Folder organizer utility, moves user files by category, open freeware" 정도로 적으면 됩니다.
4. 채팅에 **"4단계 완료"**.

## 5단계. GitHub Release 만들기 (약 5분)  [사용자]

winget이 내려받을 zip을 공개 주소에 올리는 단계입니다.

1. https://github.com/<아이디>/FoldeClean/releases/new 접속.
2. **Choose a tag** 에 `v0.1.0` 입력 → **Create new tag**.
3. Release title: `FoldeClean 0.1.0`
4. 본문(Describe): `CHANGELOG.md` 의 0.1 내용을 붙여 넣습니다.
5. 아래 파일 첨부 칸에 `release\FoldeClean-0.1.0-win-x64.zip` 을 끌어다 놓습니다. **파일 이름을 바꾸지 마세요.**
6. **Publish release**.
7. 채팅에 **"5단계 완료"**.

## 6단계. winget 매니페스트 검증과 로컬 설치 시험 (약 10분)  [Claude → 사용자 확인]

1. [Claude] `winget validate` 로 매니페스트 형식을 검사하고, zip 주소가 실제로 내려받아지는지와 해시가 맞는지 확인합니다.
2. [사용자] 로컬 매니페스트 설치를 허용하고 시험 설치합니다. (관리자 PowerShell)

```powershell
winget settings --enable LocalManifestFiles
winget install --manifest C:\JU_Pro\U4_FoldeClean\release\manifests\j\Jaelog\FoldeClean\0.1.0
foldeclean
```

3. 프로그램이 뜨면 `winget uninstall Jaelog.FoldeClean` 으로 지웁니다. 채팅에 **"6단계 완료"**.

## 7단계. winget 저장소에 제출 (약 15분)  [사용자]

Microsoft가 운영하는 공개 목록에 등록을 요청하는 단계입니다. 심사는 자동 검사 + 사람 확인으로 보통 1~5일 걸립니다.

방법 A (도구 사용, 권장):
1. GitHub 개인 토큰 만들기: https://github.com/settings/tokens → **Generate new token (classic)** → 이름 `wingetcreate`, 권한 **public_repo** 체크 → 생성 → 토큰 문자열 복사 (한 번만 보입니다. 채팅에 붙여넣지 마세요).
2. PowerShell:

```powershell
winget install Microsoft.WingetCreate
wingetcreate submit --token <복사한토큰> C:\JU_Pro\U4_FoldeClean\release\manifests\j\Jaelog\FoldeClean\0.1.0
```

3. 출력에 나오는 Pull Request 주소를 열어 두고, 며칠 뒤 "merged" 가 되면 등록 완료.

방법 B (웹으로 직접):
1. https://github.com/microsoft/winget-pkgs 오른쪽 위 **Fork**.
2. 내 포크에서 `manifests/j/Jaelog/FoldeClean/0.1.0/` 폴더를 만들고 매니페스트 3개를 업로드(Add file → Upload files).
3. **Contribute → Open pull request** → 제목 `New package: Jaelog.FoldeClean version 0.1.0` → 제출.

등록되면 누구나 `winget install Jaelog.FoldeClean` 으로 설치할 수 있습니다. 채팅에 **"7단계 완료"**.

## 8단계. 알리기 (계속)  [사용자]

- README 의 사용 흐름을 캡처해 30초 GIF 또는 영상으로 만듭니다. (Windows 키+Shift+R 화면 녹화)
- 블로그, 커뮤니티(클리앙 팁과강좌, 뽐뿌 자유게시판, 네이버 카페), 해외는 Reddit r/software 에 "winget install 한 줄" 과 함께 올립니다.
- 후기·요청은 GitHub Issues 로 받습니다. 저장소 Settings → Features → Issues 켜기.

## 9단계. Pro 골격 만들기 (0.4, 약 2주)  [Claude]

무료판이 배포된 뒤 시작합니다.
1. Edition 상태(Standard / 체험 / Pro)와 잠긴 Pro 카드 표시, 14일 체험 시작 버튼.
2. Pro 1차 기능: 규칙 편집기 → 정리 프로필과 원클릭 실행 → 다른 위치로 정리 + 여러 폴더.
3. 예약 실행과 폴더 감시(트레이).
각 기능마다 가짜 데이터로 자체 점검을 통과시키고 0.4, 0.5 로 버전을 올립니다.

## 10단계. Microsoft Store 등록과 판매 (약 1주 + 심사)  [사용자 + Claude]

1. [사용자] https://partner.microsoft.com/dashboard 에서 개발자 계정 등록(개인, 1회 약 2만 원). 신분 확인에 며칠 걸립니다.
2. [Claude] Store 용 패키지(MSIX)와 인앱 구매(Pro 추가 기능) 연결 코드를 만듭니다.
3. [사용자] 파트너 센터에서 앱 이름 `FoldeClean` 예약, 스크린샷·설명 등록, 추가 기능 "FoldeClean Pro" 가격 9,900원 설정, 제출.
4. 심사 통과(보통 1~3일)하면 판매 시작. 수익은 월 단위로 등록한 계좌로 들어옵니다.

## 11단계. 세금  [사용자]

판매 수입이 생기기 시작하면 홈택스에서 사업자 등록(간이과세)을 검토하세요. 후원금만 있는 동안은 기타소득으로 신고합니다. 정확한 판단은 세무사 상담을 권합니다.

---

## 버전 올릴 때마다 (반복)

1. [Claude] 기능 추가 → 자체 점검 → `FoldeClean.csproj` 의 `<Version>` 올림 → `CHANGELOG.md` 기록 → `release.ps1` 실행.
2. [사용자] GitHub Release 에 새 zip 첨부 (5단계와 동일, 태그는 새 버전).
3. [사용자] `wingetcreate update Jaelog.FoldeClean --version <새버전> --urls <zip주소> --submit --token <토큰>` 한 줄로 winget 갱신.
