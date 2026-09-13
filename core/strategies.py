"""정리 방식(전략). 각 전략은 파일 하나를 받아 목적지 상대 경로(폴더)를 돌려준다.

검증된 정리 체계
  type        : 파일 종류별 분류 (가장 널리 쓰이는 기본 방식)
  date        : 연/월 폴더 (사진·기록물 정리의 표준)
  type_date   : 종류 → 연도 (혼합형, 장기 보관에 유리)
  para        : PARA (Tiago Forte) - Projects / Areas / Resources / Archives 를 최근 사용일로 근사
  johnny      : Johnny.Decimal - 10-19, 20-29 식 번호 체계로 카테고리 수를 제한
  archive_old : 오래된 파일만 Archive/연도 로 이동, 최근 파일은 그대로
  dedupe      : 내용이 같은 중복 파일을 _중복 폴더로 이동 (원본 1개는 유지)
"""
from __future__ import annotations

import datetime as dt
import os
from collections import defaultdict

from .scanner import FileInfo
from . import i18n

CATEGORIES: dict[str, tuple[str, set[str]]] = {
    "documents": ("문서", {"doc", "docx", "hwp", "hwpx", "pdf", "txt", "rtf", "odt", "md", "ppt", "pptx", "xls", "xlsx", "csv", "odp", "ods", "pages", "key", "numbers", "epub"}),
    "images": ("이미지", {"jpg", "jpeg", "png", "gif", "bmp", "webp", "heic", "heif", "tif", "tiff", "svg", "raw", "cr2", "nef", "arw", "psd", "ai"}),
    "videos": ("동영상", {"mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v", "mpg", "mpeg", "ts", "3gp"}),
    "audio": ("음악", {"mp3", "wav", "flac", "aac", "m4a", "ogg", "wma", "opus", "mid", "midi"}),
    "archives": ("압축", {"zip", "rar", "7z", "tar", "gz", "bz2", "xz", "iso", "cab", "alz", "egg"}),
    "installers": ("설치파일", {"exe", "msi", "msix", "appx", "apk", "dmg", "pkg", "deb", "rpm"}),
    "code": ("코드", {"py", "js", "ts", "tsx", "jsx", "html", "css", "scss", "json", "xml", "yaml", "yml", "toml", "ini", "cfg", "c", "cpp", "h", "hpp", "cs", "java", "kt", "go", "rs", "rb", "php", "sh", "bat", "ps1", "sql", "ipynb"}),
    "fonts": ("폰트", {"ttf", "otf", "woff", "woff2", "eot"}),
    "shortcuts": ("바로가기", {"lnk", "url"}),
}
_EXT_TO_CAT = {e: k for k, (_n, exts) in CATEGORIES.items() for e in exts}
OTHER = ("기타", set())

JOHNNY_ORDER = ["documents", "images", "videos", "audio", "archives", "installers", "code", "fonts", "other"]  # 10-19 … 90-99


def category_of(f: FileInfo) -> tuple[str, str]:
    key = _EXT_TO_CAT.get(f.ext, "other")
    return key, i18n.name(key)


def _ym(ts: float, fmt: str) -> str:
    return dt.datetime.fromtimestamp(ts).strftime(fmt)


STRATEGY_META = [
    {"id": "type", "name": "종류별 정리", "tag": "기본", "desc": "문서·이미지·동영상·음악·압축·설치파일·코드로 나눕니다. 가장 직관적이고, 파일 이름을 몰라도 찾기 쉽습니다.",
     "best": "다운로드 폴더, 바탕화면처럼 잡다한 파일이 섞인 곳", "example": "문서/보고서.pdf · 이미지/사진.jpg"},
    {"id": "date", "name": "날짜별 정리", "tag": "기록", "desc": "수정한 연도/월 폴더로 넣습니다. 사진이나 회의록처럼 '언제'가 중요한 자료에 맞습니다.",
     "best": "사진, 스캔본, 기간별 기록물", "example": "2025/03/사진.jpg"},
    {"id": "type_date", "name": "종류 → 연도", "tag": "혼합", "desc": "먼저 종류로 나누고 그 안을 연도로 다시 나눕니다. 파일이 아주 많을 때 한 폴더가 비대해지는 걸 막습니다.",
     "best": "수천 개 이상의 파일, 장기 보관용 자료", "example": "문서/2024/계약서.pdf"},
    {"id": "para", "name": "PARA", "tag": "생산성", "desc": "Tiago Forte의 PARA 체계입니다. 최근에 만진 파일은 '1_Projects', 가끔 쓰는 자료는 '3_Resources', 오래된 것은 '4_Archives'로 보냅니다. 이 프로그램은 사용 시점으로 근사하고, 폴더 이름은 그대로 두어 나중에 손으로 다듬기 쉽습니다.",
     "best": "업무 자료, 지식 관리를 시작하려는 사람", "example": "1_Projects/기획안.docx · 4_Archives/2022/옛자료.xlsx"},
    {"id": "johnny", "name": "Johnny.Decimal", "tag": "번호체계", "desc": "10-19 문서, 20-29 이미지처럼 번호를 붙여 카테고리 수를 10개 이하로 제한합니다. 폴더가 항상 같은 순서로 정렬되고 어디에 넣을지 고민이 줄어듭니다.",
     "best": "체계적인 걸 좋아하는 사람, 여러 PC에서 같은 구조를 쓰고 싶은 경우", "example": "10-19 문서/11 문서/보고서.pdf"},
    {"id": "archive_old", "name": "오래된 파일 보관", "tag": "최소변경", "desc": "지정한 기간(기본 180일) 동안 손대지 않은 파일만 'Archive/연도'로 옮깁니다. 최근 파일은 그대로 두므로 작업 흐름이 끊기지 않습니다.",
     "best": "지금 구조는 유지하면서 묵은 파일만 치우고 싶을 때", "example": "Archive/2023/오래된자료.pdf"},
    {"id": "dedupe", "name": "중복 파일 정리", "tag": "용량확보", "desc": "내용이 완전히 같은 파일을 찾아 하나만 남기고 나머지를 '_중복' 폴더로 모읍니다. 크기 → 앞부분 해시 → 전체 해시 순으로 비교해 큰 폴더에서도 빠릅니다.",
     "best": "여러 번 다운로드한 파일, 복사본이 많은 폴더", "example": "_중복/사진 (2).jpg"},
]


class Strategy:
    id = ""

    def __init__(self, opts: dict):
        self.opts = opts
        self.keep_subdirs = bool(opts.get("keep_subdirs", False))

    def prepare(self, files: list[FileInfo], root: str) -> None:
        pass

    def dest_dir(self, f: FileInfo, root: str) -> str | None:
        """목적지 폴더(루트 기준 상대). None이면 이 파일은 이동하지 않음."""
        raise NotImplementedError

    def note(self, f: FileInfo) -> str | None:
        """이동 목록에 함께 보여줄 부가 설명."""
        return None

    def _sub(self, f: FileInfo, root: str) -> str:
        if not self.keep_subdirs:
            return ""
        rel = os.path.relpath(os.path.dirname(f.path), root)
        return "" if rel == "." else rel


class ByType(Strategy):
    id = "type"

    def dest_dir(self, f, root):
        _k, label = category_of(f)
        return os.path.join(label, self._sub(f, root))


class ByDate(Strategy):
    id = "date"

    def dest_dir(self, f, root):
        fmt = "%Y/%m" if self.opts.get("granularity", "month") == "month" else "%Y"
        return os.path.join(_ym(f.mtime, fmt), self._sub(f, root))


class ByTypeDate(Strategy):
    id = "type_date"

    def dest_dir(self, f, root):
        _k, label = category_of(f)
        return os.path.join(label, _ym(f.mtime, "%Y"), self._sub(f, root))


class Para(Strategy):
    id = "para"

    def dest_dir(self, f, root):
        now = dt.datetime.now().timestamp()
        age = (now - f.mtime) / 86400
        active = float(self.opts.get("active_days", 30))
        resource = float(self.opts.get("resource_days", 180))
        _k, label = category_of(f)
        if age <= active:
            return os.path.join("1_Projects", self._sub(f, root))
        if age <= resource:
            return os.path.join("3_Resources", label, self._sub(f, root))
        return os.path.join("4_Archives", _ym(f.mtime, "%Y"), self._sub(f, root))


class Johnny(Strategy):
    id = "johnny"

    def dest_dir(self, f, root):
        key, label = category_of(f)
        if key not in JOHNNY_ORDER:
            key, label = "other", i18n.name("other")
        idx = JOHNNY_ORDER.index(key) + 1  # 1..9
        area = f"{idx}0-{idx}9 {label}"
        cat = f"{idx}1 {label}"
        return os.path.join(area, cat, self._sub(f, root))


class ArchiveOld(Strategy):
    id = "archive_old"

    def dest_dir(self, f, root):
        days = float(self.opts.get("days", 180))
        age = (dt.datetime.now().timestamp() - f.mtime) / 86400
        if age < days:
            return None
        return os.path.join("Archive", _ym(f.mtime, "%Y"), self._sub(f, root))


class Dedupe(Strategy):
    id = "dedupe"

    def __init__(self, opts):
        super().__init__(opts)
        self.orig: dict[str, str] = {}   # 중복 → 남길 원본

    def prepare(self, files, root):
        from .dupes import find_duplicates
        min_size = int(self.opts.get("min_size", 1024))
        groups = find_duplicates([f for f in files if f.size >= min_size])
        for grp in groups:
            # 가장 오래된(먼저 만든) 파일을 원본으로 남긴다
            grp.sort(key=lambda x: (x.ctime, len(x.path)))
            for d in grp[1:]:
                self.orig[d.path] = grp[0].path

    def dest_dir(self, f, root):
        if f.path not in self.orig:
            return None
        return os.path.join(i18n.name("dupes"), self._sub(f, root))

    def note(self, f):
        o = self.orig.get(f.path)
        return f"dup|{o}" if o else None


STRATEGIES: dict[str, type[Strategy]] = {c.id: c for c in (ByType, ByDate, ByTypeDate, Para, Johnny, ArchiveOld, Dedupe)}


def make_strategy(sid: str, opts: dict | None = None) -> Strategy:
    if sid not in STRATEGIES:
        raise ValueError(f"unknown strategy {sid}")
    return STRATEGIES[sid](opts or {})


def category_breakdown(files: list[FileInfo]) -> list[dict]:
    agg: dict[str, dict] = defaultdict(lambda: {"count": 0, "size": 0})
    for f in files:
        key, _label = category_of(f)
        agg[key]["count"] += 1
        agg[key]["size"] += f.size
    return [{"key": k, "label": i18n.name(k), **v} for k, v in sorted(agg.items(), key=lambda kv: -kv[1]["size"])]
