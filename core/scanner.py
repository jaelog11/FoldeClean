"""빠른 폴더 탐색. os.scandir 기반, 정션/심볼릭 링크는 따라가지 않는다."""
from __future__ import annotations

import os
import stat
import time
from dataclasses import dataclass, field

FILE_ATTRIBUTE_HIDDEN = 0x2
FILE_ATTRIBUTE_SYSTEM = 0x4
FILE_ATTRIBUTE_REPARSE_POINT = 0x400
FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000  # OneDrive 등 클라우드 자리표시자


@dataclass(slots=True)
class FileInfo:
    path: str
    name: str
    ext: str          # 소문자, 점 제외
    size: int
    mtime: float
    ctime: float
    attrs: int
    depth: int
    risk: str = "safe"          # safe | warn | block
    reasons: list = field(default_factory=list)   # 사유 코드 (화면에서 번역)
    group: str = ""                                # 묶음 키

    @property
    def hidden(self) -> bool:
        return bool(self.attrs & FILE_ATTRIBUTE_HIDDEN)

    @property
    def system(self) -> bool:
        return bool(self.attrs & FILE_ATTRIBUTE_SYSTEM)

    @property
    def cloud_placeholder(self) -> bool:
        return bool(self.attrs & FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS)

    def to_dict(self) -> dict:
        return {
            "path": self.path, "name": self.name, "ext": self.ext, "size": self.size,
            "mtime": self.mtime, "depth": self.depth, "risk": self.risk, "reasons": self.reasons,
            "hidden": self.hidden, "system": self.system, "group": self.group,
        }


@dataclass
class ScanResult:
    root: str
    files: list[FileInfo]
    dirs: list[str]
    skipped_links: int
    errors: int
    elapsed: float

    def summary(self) -> dict:
        total = sum(f.size for f in self.files)
        return {
            "root": self.root, "file_count": len(self.files), "dir_count": len(self.dirs),
            "total_size": total, "skipped_links": self.skipped_links, "errors": self.errors,
            "elapsed": round(self.elapsed, 2),
        }


def scan(root: str, max_depth: int = 50, include_hidden: bool = False, progress=None) -> ScanResult:
    """root 아래 모든 파일을 수집한다. progress(count)가 주어지면 2000개마다 호출."""
    t0 = time.perf_counter()
    root = os.path.abspath(root)
    files: list[FileInfo] = []
    dirs: list[str] = []
    skipped_links = 0
    errors = 0
    stack: list[tuple[str, int]] = [(root, 0)]

    while stack:
        cur, depth = stack.pop()
        try:
            it = os.scandir(cur)
        except OSError:
            errors += 1
            continue
        with it:
            for e in it:
                try:
                    st = e.stat(follow_symlinks=False)
                except OSError:
                    errors += 1
                    continue
                attrs = getattr(st, "st_file_attributes", 0)
                if attrs & FILE_ATTRIBUTE_REPARSE_POINT:
                    skipped_links += 1
                    continue
                if not include_hidden and (attrs & FILE_ATTRIBUTE_HIDDEN) and depth > 0:
                    # 숨김 폴더/파일은 기본 제외 (.git 같은 것은 프로젝트 감지에서 따로 본다)
                    if e.is_dir(follow_symlinks=False):
                        dirs.append(e.path)
                    continue
                if e.is_dir(follow_symlinks=False):
                    dirs.append(e.path)
                    if depth < max_depth:
                        stack.append((e.path, depth + 1))
                elif stat.S_ISREG(st.st_mode):
                    name = e.name
                    dot = name.rfind(".")
                    ext = name[dot + 1:].lower() if dot > 0 else ""
                    files.append(FileInfo(e.path, name, ext, st.st_size, st.st_mtime, st.st_ctime, attrs, depth))
                    if progress and len(files) % 2000 == 0:
                        progress(len(files))
    return ScanResult(root, files, dirs, skipped_links, errors, time.perf_counter() - t0)
