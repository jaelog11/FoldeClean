"""이동 실행과 되돌리기. 모든 이동을 journals/*.json 에 기록한다."""
from __future__ import annotations

import datetime as dt
import json
import os
import shutil
import threading
import time

JOURNAL_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "journals")


def _move(src: str, dst: str) -> None:
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    try:
        os.replace(src, dst)          # 같은 드라이브면 즉시
    except OSError:
        shutil.move(src, dst)         # 다른 드라이브면 복사 후 삭제


class Executor:
    def __init__(self):
        self.cancel = threading.Event()
        self.lock = threading.Lock()
        self.state = {"running": False, "done": 0, "total": 0, "failed": 0, "current": "", "journal": None, "finished": False}

    def snapshot(self) -> dict:
        with self.lock:
            return dict(self.state)

    def run(self, moves: list[dict], remove_empty_dirs: bool = True, root: str | None = None, on_progress=None) -> str:
        os.makedirs(JOURNAL_DIR, exist_ok=True)
        stamp = dt.datetime.now().strftime("%Y%m%d_%H%M%S")
        jpath = os.path.join(JOURNAL_DIR, f"{stamp}.json")
        journal = {"created": stamp, "root": root, "entries": [], "failed": []}
        self.cancel.clear()
        with self.lock:
            self.state.update(running=True, done=0, total=len(moves), failed=0, current="", journal=jpath, finished=False)
        last_flush = time.perf_counter()
        for i, m in enumerate(moves):
            if self.cancel.is_set():
                break
            try:
                _move(m["src"], m["dst"])
                journal["entries"].append({"src": m["src"], "dst": m["dst"]})
            except Exception as e:  # noqa: BLE001
                journal["failed"].append({"src": m["src"], "dst": m["dst"], "error": str(e)})
                with self.lock:
                    self.state["failed"] += 1
            with self.lock:
                self.state["done"] = i + 1
                self.state["current"] = m["name"]
            if on_progress and (i % 50 == 0 or i == len(moves) - 1):
                on_progress(self.snapshot())
            if time.perf_counter() - last_flush > 2:
                self._write(jpath, journal); last_flush = time.perf_counter()
        removed = []
        if remove_empty_dirs and root:
            removed = self._remove_empty(root)
        journal["removed_dirs"] = removed
        self._write(jpath, journal)
        with self.lock:
            self.state.update(running=False, finished=True)
        if on_progress:
            on_progress(self.snapshot())
        return jpath

    @staticmethod
    def _write(path: str, journal: dict) -> None:
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(journal, f, ensure_ascii=False, indent=1)
        os.replace(tmp, path)

    @staticmethod
    def _remove_empty(root: str) -> list[str]:
        removed = []
        for cur, dirs, files in os.walk(root, topdown=False):
            if cur == root:
                continue
            try:
                if not os.listdir(cur):
                    os.rmdir(cur); removed.append(cur)
            except OSError:
                pass
        return removed

    def undo(self, journal_path: str, on_progress=None) -> dict:
        with open(journal_path, encoding="utf-8") as f:
            journal = json.load(f)
        entries = journal.get("entries", [])
        ok = fail = 0
        with self.lock:
            self.state.update(running=True, done=0, total=len(entries), failed=0, current="되돌리는 중", finished=False)
        for i, e in enumerate(reversed(entries)):
            try:
                if os.path.exists(e["dst"]):
                    _move(e["dst"], e["src"]); ok += 1
                else:
                    fail += 1
            except Exception:  # noqa: BLE001
                fail += 1
            with self.lock:
                self.state["done"] = i + 1; self.state["failed"] = fail
            if on_progress and i % 50 == 0:
                on_progress(self.snapshot())
        # 되돌린 뒤 비게 된 정리 폴더 제거
        if journal.get("root"):
            self._remove_empty(journal["root"])
        journal["undone"] = dt.datetime.now().strftime("%Y%m%d_%H%M%S")
        self._write(journal_path, journal)
        with self.lock:
            self.state.update(running=False, finished=True)
        if on_progress:
            on_progress(self.snapshot())
        return {"restored": ok, "failed": fail}


def list_journals() -> list[dict]:
    if not os.path.isdir(JOURNAL_DIR):
        return []
    out = []
    for n in sorted(os.listdir(JOURNAL_DIR), reverse=True):
        if not n.endswith(".json"):
            continue
        p = os.path.join(JOURNAL_DIR, n)
        try:
            with open(p, encoding="utf-8") as f:
                j = json.load(f)
            out.append({"path": p, "created": j.get("created"), "root": j.get("root"),
                        "count": len(j.get("entries", [])), "failed": len(j.get("failed", [])),
                        "undone": j.get("undone")})
        except Exception:  # noqa: BLE001
            continue
    return out
