# 8단계 알리기 — 준비물과 문구

## 1. 저장소 단장 (10분)

https://github.com/jaelog11/FoldeClean 에서:

1. 오른쪽 위 **About** 옆 톱니바퀴 → Description 에 한 줄 설명을 넣습니다.
   `프로그램·시스템과 연결된 파일을 미리 찾아 보호하고, 검증된 정리 체계로 폴더를 안전하게 정리합니다.`
2. 같은 창 Topics 에 태그를 넣습니다.
   `windows` `file-organizer` `folder-cleanup` `duplicate-finder` `wpf` `dotnet` `winget` `freeware`
3. **Settings → Features → Issues** 를 켭니다. 이게 문의 창구가 됩니다.
4. README 맨 위에 화면 캡처 1장을 넣으면 첫인상이 크게 달라집니다. (아래 2번에서 만든 이미지)

## 2. 보여줄 것 만들기 (20분)

가장 효과가 큰 건 "지저분한 폴더가 정리되는 장면"입니다.

- **화면 녹화**: `Windows 키 + Alt + R` 로 녹화 시작/정지. 30초 안쪽으로.
  순서: 다운로드 폴더 선택 → 검사 결과 → 정리 방식 고르기 → 흐름도 → 실행 → 되돌리기
- **캡처**: `Windows 키 + Shift + S` 로 영역 선택. 흐름도 화면과 검사 결과 화면 2장이면 충분합니다.
- 실제 개인 파일이 보이면 안 되니, `py -3.11 make_sample.py` 로 만든 `sample_data` 폴더로 찍으세요.

## 3. 올릴 곳

| 곳 | 성격 | 비고 |
|---|---|---|
| 클리앙 "팁과 강좌" | 국내, 반응 빠름 | 자기 홍보 규정 확인. 사용법 중심으로 쓰면 무난 |
| 뽐뿌 자유게시판 / 코딩 커뮤니티 | 국내 | |
| 네이버 카페 (윈도우·PC 활용) | 국내 | |
| Reddit r/software, r/Windows10 | 해외, 시장이 훨씬 큼 | 영어 화면이 이미 있어 유리 |
| Hacker News (Show HN) | 해외 개발자 | 오픈소스라 잘 맞음 |
| 커뮤니티 위키/블로그 | 검색 유입 | 꾸준히 효과 |

## 4. 글 초안 (국내용)

> **제목**: 폴더 정리 프로그램을 만들었습니다 — 프로그램이랑 연결된 파일은 건드리지 않습니다
>
> 다운로드 폴더가 몇천 개씩 쌓여서 정리 프로그램을 찾아봤는데, 대부분 그냥 확장자별로 옮기기만 하더군요.
> 그러다 바로가기가 깨지거나 프로그램이 안 켜지는 일이 생겨서 직접 만들었습니다.
>
> **FoldeClean** 은 옮기기 전에 먼저 이런 파일을 찾아냅니다.
> - 바로가기가 가리키고 있는 파일
> - 레지스트리에 등록된 프로그램의 파일
> - 지금 다른 프로그램이 쓰고 있는 파일
> - 프로젝트 폴더(.git, package.json 등) 안의 파일
> - 클라우드 동기화 폴더
>
> 이런 건 "차단" 또는 "주의"로 표시하고 기본적으로 건드리지 않습니다.
>
> 정리 방식은 7가지 중에 고릅니다. 종류별, 날짜별, 종류→연도, PARA, Johnny.Decimal, 오래된 파일 보관, 중복 파일 정리.
> 실행 전에 어디서 어디로 가는지 흐름도로 보여주고, 마음에 안 드는 파일은 빼고 실행할 수 있습니다.
> 옮긴 건 전부 기록되니 한 번에 되돌릴 수도 있습니다.
>
> 무료이고 광고 없고, 아무 정보도 밖으로 안 보냅니다.
> 한국어·영어·중국어·일본어를 지원합니다.
>
> 받는 곳: https://github.com/jaelog11/FoldeClean/releases
> (winget 등록 심사 중입니다. 통과하면 `winget install Jaelog.FoldeClean` 한 줄로 설치됩니다)
>
> 파일을 대량으로 옮기는 프로그램이라 백신 하나가 오탐을 내는데, 신고해서 수정 중입니다.
> 처음 쓰실 때는 잃어도 괜찮은 폴더로 먼저 시험해 보세요.

## 5. 글 초안 (영어, Reddit / Show HN)

> **Title**: FoldeClean — a folder organizer that finds the files your programs depend on, before moving anything
>
> Most folder organizers just move files by extension. That broke shortcuts and a couple of installed programs
> on my machine, so I wrote one that looks before it leaps.
>
> Before moving anything, FoldeClean finds and protects:
> - files that a shortcut (.lnk) points to
> - programs registered in the registry (App Paths, Uninstall)
> - files currently open by another process
> - anything inside a project folder (.git, package.json, *.sln …)
> - cloud-sync folders and placeholder files
>
> Those are marked Blocked or Caution and left alone by default.
>
> Then you pick one of seven organizing systems — by type, by date, type→year, PARA, Johnny.Decimal,
> archive-old-files, or duplicate cleanup — and see a flow diagram of exactly what would move where.
> Every move is journaled, so one click puts everything back.
>
> Free, no ads, no telemetry, nothing leaves your machine. Korean / English / Chinese / Japanese UI.
> C# + WPF + WebView2, source is public.
>
> https://github.com/jaelog11/FoldeClean
>
> Heads-up: one antivirus engine family flags it as a ransomware heuristic (it moves a lot of files and hashes
> them for duplicate detection). I've filed a false-positive report and they're updating. Microsoft Defender
> and the rest report it clean.

## 6. 하면 안 되는 것

- 같은 글을 여러 게시판에 도배하지 않기. 커뮤니티마다 결이 다르니 문구를 조금씩 고쳐 쓰기
- 자기 홍보 금지인 게시판에 올리지 않기. 규정을 먼저 읽기
- 백신 오탐을 숨기지 않기. 미리 밝히면 신뢰가 올라가고, 숨기면 신고당합니다
- 다운로드 수를 부풀리지 않기

## 7. 올린 뒤

- 댓글에는 하루 안에 답하기. 초기 반응이 검색 순위와 신뢰에 크게 작용합니다
- 버그 신고는 GitHub Issues 로 유도하기 (프로그램 안 "오류 보고서 저장" 버튼을 안내)
- 요청이 많은 기능을 `ROADMAP.md` 에 반영하기
