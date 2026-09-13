"""연결 파일 감지: 시스템 경로, 바로가기 대상, 레지스트리 등록 경로, 잠김, 프로젝트 폴더, 클라우드 자리표시자.

risk 등급
  block : 기본적으로 절대 옮기지 않음 (시스템 폴더, 프로그램 폴더, 레지스트리 등록, 잠김)
  warn  : 옮길 수는 있으나 확인 필요 (바로가기 대상, 프로젝트 폴더 내부, 클라우드 자리표시자, 실행파일)
  safe  : 자유롭게 정리 가능
"""
from __future__ import annotations

import os
import struct
import sys
from functools import lru_cache

from .scanner import FileInfo

IS_WIN = sys.platform == "win32"

EXEC_EXT = {"exe", "dll", "sys", "msi", "com", "bat", "cmd", "ps1", "vbs", "scr", "ocx", "drv", "cpl"}
PROJECT_MARKERS = {
    ".git", ".svn", ".hg", "package.json", "pyproject.toml", "requirements.txt", "Cargo.toml",
    "go.mod", "pom.xml", "build.gradle", "CMakeLists.txt", "Makefile", ".sln", ".csproj", ".vcxproj",
    "Gemfile", "composer.json", ".vs", ".idea", "node_modules", "venv", ".venv",
}
CLOUD_HINTS = ("onedrive", "dropbox", "google drive", "googledrive", "icloud", "box sync")


def _env(name: str) -> str:
    return os.environ.get(name, "")


@lru_cache(maxsize=1)
def system_roots() -> list[str]:
    roots = [
        _env("SystemRoot"), _env("ProgramFiles"), _env("ProgramFiles(x86)"), _env("ProgramData"),
        _env("LOCALAPPDATA"), _env("APPDATA"), _env("ProgramW6432"),
    ]
    drive = os.path.splitdrive(_env("SystemRoot") or "C:\\")[0]
    roots += [drive + "\\$Recycle.Bin", drive + "\\System Volume Information", drive + "\\Recovery",
              drive + "\\PerfLogs", drive + "\\Windows"]
    return [os.path.normcase(os.path.abspath(r)) for r in roots if r]


def _under(path: str, base: str) -> bool:
    p = os.path.normcase(os.path.abspath(path))
    return p == base or p.startswith(base.rstrip("\\") + "\\")


# ---------------------------------------------------------------- .lnk 파싱 (순수 파이썬)
def parse_lnk_target(path: str) -> str | None:
    """Windows 바로가기(.lnk)의 대상 경로를 읽는다. 실패하면 None."""
    try:
        with open(path, "rb") as f:
            data = f.read(64 * 1024)
        if len(data) < 0x4C or data[:4] != b"L\x00\x00\x00":
            return None
        flags = struct.unpack_from("<I", data, 0x14)[0]
        off = 0x4C
        if flags & 0x1:  # HasLinkTargetIDList
            idlist_size = struct.unpack_from("<H", data, off)[0]
            off += 2 + idlist_size
        if flags & 0x2:  # HasLinkInfo
            li_size, li_hdr, li_flags = struct.unpack_from("<III", data, off)
            local_off = struct.unpack_from("<I", data, off + 16)[0]
            if li_flags & 0x1 and local_off:
                start = off + local_off
                end = data.index(b"\x00", start)
                raw = data[start:end]
                try:
                    return raw.decode("mbcs")
                except Exception:
                    return raw.decode("utf-8", "ignore")
            # 네트워크 경로 등은 생략
        return None
    except Exception:
        return None


def shortcut_dirs() -> list[str]:
    home = os.path.expanduser("~")
    cands = [
        os.path.join(home, "Desktop"), os.path.join(home, "OneDrive", "Desktop"),
        os.path.join(_env("APPDATA"), "Microsoft", "Windows", "Start Menu"),
        os.path.join(_env("ProgramData"), "Microsoft", "Windows", "Start Menu"),
        os.path.join(_env("APPDATA"), "Microsoft", "Internet Explorer", "Quick Launch"),
        os.path.join(_env("PUBLIC"), "Desktop"),
    ]
    return [c for c in cands if c and os.path.isdir(c)]


def collect_shortcut_targets(extra_dirs: list[str] | None = None) -> dict[str, str]:
    """{대상경로(normcase): 바로가기 경로}"""
    out: dict[str, str] = {}
    for d in shortcut_dirs() + (extra_dirs or []):
        for cur, _dirs, names in os.walk(d):
            for n in names:
                if n.lower().endswith(".lnk"):
                    lp = os.path.join(cur, n)
                    t = parse_lnk_target(lp)
                    if t:
                        out[os.path.normcase(os.path.abspath(t))] = lp
    return out


# ---------------------------------------------------------------- 레지스트리
def collect_registry_paths() -> set[str]:
    """레지스트리에 등록된 프로그램 경로(normcase)와 그 상위 폴더."""
    paths: set[str] = set()
    if not IS_WIN:
        return paths
    import winreg

    def add(val: str):
        if not isinstance(val, str) or ":" not in val:
            return
        v = val.strip().strip('"')
        # "C:\path\app.exe" --arg 형태에서 경로만
        if v.lower().endswith(".exe") or os.path.exists(v):
            p = os.path.normcase(os.path.abspath(v))
        else:
            idx = v.lower().find(".exe")
            if idx == -1:
                return
            p = os.path.normcase(os.path.abspath(v[: idx + 4].strip('"')))
        paths.add(p)
        paths.add(os.path.dirname(p))

    keys = [
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
        (winreg.HKEY_CURRENT_USER, r"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (winreg.HKEY_CURRENT_USER, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
        (winreg.HKEY_CURRENT_USER, r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
    ]
    for hive, sub in keys:
        try:
            k = winreg.OpenKey(hive, sub)
        except OSError:
            continue
        with k:
            # 값들
            i = 0
            while True:
                try:
                    _n, v, _t = winreg.EnumValue(k, i)
                    add(v)
                    i += 1
                except OSError:
                    break
            # 하위 키들
            i = 0
            while True:
                try:
                    name = winreg.EnumKey(k, i)
                    i += 1
                except OSError:
                    break
                try:
                    with winreg.OpenKey(k, name) as sk:
                        for vn in ("", "Path", "InstallLocation", "DisplayIcon", "UninstallString"):
                            try:
                                v, _t = winreg.QueryValueEx(sk, vn)
                                add(v)
                            except OSError:
                                pass
                except OSError:
                    pass
    return paths


def is_locked(path: str) -> bool:
    """다른 프로세스가 열어 두었는지 검사 (독점 열기 시도)."""
    if not IS_WIN:
        return False
    try:
        import msvcrt
        with open(path, "rb") as f:
            try:
                msvcrt.locking(f.fileno(), msvcrt.LK_NBLCK, 1)
                msvcrt.locking(f.fileno(), msvcrt.LK_UNLCK, 1)
            except OSError:
                return True
    except PermissionError:
        return True
    except OSError:
        return False
    return False


# ---------------------------------------------------------------- 분석기
class SafetyAnalyzer:
    def __init__(self, root: str, check_locks: bool = True, lock_check_limit: int = 5000):
        self.root = os.path.abspath(root)
        self.sys_roots = system_roots()
        self.shortcuts = collect_shortcut_targets([self.root])
        self.shortcut_dirs = {os.path.dirname(p) for p in self.shortcuts}
        self.reg_paths = collect_registry_paths()
        self.check_locks = check_locks
        self.lock_check_limit = lock_check_limit
        self._project_cache: dict[str, str | None] = {}
        self._program_dir_cache: dict[str, bool] = {}

    def project_root_of(self, directory: str) -> str | None:
        """directory 또는 그 상위(루트까지)에 프로젝트 표식이 있으면 그 폴더."""
        d = directory
        chain = []
        while True:
            if d in self._project_cache:
                res = self._project_cache[d]
                break
            chain.append(d)
            try:
                names = set(os.listdir(d))
            except OSError:
                names = set()
            if names & PROJECT_MARKERS or any(n.endswith((".sln", ".csproj")) for n in names):
                res = d
                break
            parent = os.path.dirname(d)
            if parent == d or os.path.normcase(d) == os.path.normcase(self.root):
                res = None
                break
            d = parent
        for c in chain:
            self._project_cache[c] = res
        return res

    def is_program_dir(self, directory: str) -> bool:
        """폴더에 exe와 dll이 함께 있으면 설치된 프로그램 폴더로 본다."""
        if directory in self._program_dir_cache:
            return self._program_dir_cache[directory]
        try:
            exts = {n.rsplit(".", 1)[-1].lower() for n in os.listdir(directory) if "." in n}
        except OSError:
            exts = set()
        res = "exe" in exts and ("dll" in exts or "manifest" in exts)
        self._program_dir_cache[directory] = res
        return res

    def analyze(self, files: list[FileInfo], progress=None) -> dict:
        stats = {"safe": 0, "warn": 0, "block": 0}
        lock_checked = 0
        for i, f in enumerate(files):
            reasons: list[str] = []
            level = "safe"
            p = os.path.normcase(f.path)
            d = os.path.dirname(f.path)
            group = d

            if any(_under(f.path, r) for r in self.sys_roots):
                level = "block"; reasons.append("reason.sysdir")
            if p in self.reg_paths or os.path.normcase(d) in self.reg_paths:
                level = "block"; reasons.append("reason.registry")
            if f.system:
                level = "block"; reasons.append("reason.sysattr")
            if self.is_program_dir(d):
                level = "block"; reasons.append("reason.programdir"); group = d

            if level != "block":
                if p in self.shortcuts:
                    level = "warn"; reasons.append("reason.shortcut|" + os.path.basename(self.shortcuts[p]))
                if f.ext in EXEC_EXT:
                    level = "warn"; reasons.append("reason.exec")
                if f.ext == "lnk":
                    level = "warn"; reasons.append("reason.lnk")
                pr = self.project_root_of(d)
                if pr:
                    level = "warn"; reasons.append("reason.project|" + os.path.basename(pr)); group = pr
                if f.cloud_placeholder or any(h in p for h in CLOUD_HINTS):
                    level = "warn"; reasons.append("reason.cloud")
                if f.hidden:
                    level = "warn"; reasons.append("reason.hidden")
                if self.check_locks and lock_checked < self.lock_check_limit and f.ext not in ("lnk",) and not f.cloud_placeholder:
                    lock_checked += 1
                    if is_locked(f.path):
                        level = "block"; reasons.append("reason.locked")

            f.risk = level
            f.reasons = reasons
            f.group = group
            stats[level] += 1
            if progress and i % 2000 == 0:
                progress(i)
        return {
            "stats": stats,
            "shortcut_count": len(self.shortcuts),
            "registry_count": len(self.reg_paths),
        }
