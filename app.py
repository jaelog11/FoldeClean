"""FoldeClean - 안전한 폴더 정리 프로그램.

실행:  py -3.11 app.py            (데스크톱 창)
       py -3.11 app.py --web      (브라우저에서 열기, http://127.0.0.1:8765)
"""
from __future__ import annotations

import json
import os
import sys
import threading
import traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from core.executor import Executor, list_journals  # noqa: E402
from core.planner import build_plan  # noqa: E402
from core.safety import SafetyAnalyzer  # noqa: E402
from core.scanner import scan  # noqa: E402
from core.strategies import STRATEGY_META, category_breakdown, make_strategy  # noqa: E402
from core import i18n  # noqa: E402
from core import edition as ed  # noqa: E402
from core import rules as rl  # noqa: E402

UI_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ui")
VERSION = "0.2"   # C# 판(FoldeClean.csproj의 Version)과 맞춘다


class Api:
    """JS에서 window.pywebview.api.<method>(...) 로 호출된다. 반환값은 JSON 직렬화 가능해야 한다."""

    def __init__(self):
        self._window = None
        self._scan_result = None
        self._plan = None
        self._executor = Executor()
        self._progress = {"phase": "idle", "count": 0, "total": 0}

    # ---------------------------------------------------------------- 진행 상태
    def _set_progress(self, phase: str, count: int = 0, total: int = 0):
        self._progress = {"phase": phase, "count": count, "total": total}

    def app_info(self):
        from core.executor import JOURNAL_DIR
        return {"name": "FoldeClean", "version": VERSION, "owner": "Jaelog By 아지랑이", "engine": f"Python {sys.version.split()[0]}", "journals": JOURNAL_DIR, "lang": i18n.LANG}

    def get_progress(self):
        p = dict(self._progress)
        p["exec"] = self._executor.snapshot()
        return p

    # ---------------------------------------------------------------- 폴더 선택
    def pick_folder(self):
        if self._window is None:
            return None
        try:
            import webview
            res = self._window.create_file_dialog(webview.FOLDER_DIALOG)
            if res:
                return res[0] if isinstance(res, (list, tuple)) else res
        except Exception:
            traceback.print_exc()
        return None

    def default_folders(self):
        home = os.path.expanduser("~")
        cands = {"downloads": ("다운로드", os.path.join(home, "Downloads")), "desktop": ("바탕화면", os.path.join(home, "Desktop")),
                 "documents": ("문서", os.path.join(home, "Documents")), "pictures": ("사진", os.path.join(home, "Pictures"))}
        return [{"key": k, "label": l, "path": v} for k, (l, v) in cands.items() if os.path.isdir(v)]

    # ---------------------------------------------------------------- 검사
    def scan_folder(self, root: str, check_locks: bool = True):
        if not root or not os.path.isdir(root):
            return {"error": "폴더를 찾을 수 없습니다."}
        self._set_progress("scan", 0, 0)
        res = scan(root, progress=lambda c: self._set_progress("scan", c, 0))
        self._set_progress("analyze", 0, len(res.files))
        analyzer = SafetyAnalyzer(root, check_locks=check_locks)
        info = analyzer.analyze(res.files, progress=lambda i: self._set_progress("analyze", i, len(res.files)))
        self._scan_result = res
        self._plan = None
        self._set_progress("idle")
        flagged = [f.to_dict() for f in res.files if f.risk != "safe"]
        flagged.sort(key=lambda d: (d["risk"] != "block", d["path"]))
        return {
            "summary": res.summary(), "risk": info, "categories": category_breakdown(res.files),
            "flagged": flagged[:2000], "flagged_total": len(flagged),
        }

    def strategies(self):
        return [{"id": m["id"]} for m in STRATEGY_META]   # 문구는 ui/i18n.js

    def set_lang(self, lang: str):
        return i18n.set_lang(lang)

    # ---------------------------------------------------------------- 판 구분과 규칙 (Pro)
    def edition_info(self):
        return ed.info()

    def start_trial(self):
        if not ed.start_trial():
            return {"error": "체험을 이미 사용했거나 Pro 상태입니다."}
        return ed.info()

    def rules_get(self):
        return {"rules": rl.load(), "pro_active": ed.pro_active(),
                "scanned": len(self._scan_result.files) if self._scan_result else 0}

    def rules_save(self, rules: list | None = None):
        if not ed.pro_active():
            return {"error": "규칙은 Pro 기능입니다."}
        return {"saved": rl.save(rules or [])}

    def rules_test(self, rule: dict | None = None):
        if self._scan_result is None:
            return {"error": "먼저 폴더를 검사하세요."}
        if not rule:
            return {"error": "규칙이 비어 있습니다."}
        return rl.test(rule, [f for f in self._scan_result.files if f.risk != "block"])

    def save_report(self, note: str = ""):
        """오류 보고서를 바탕화면에 저장 (파일 이름 같은 개인 정보는 담지 않음)."""
        import datetime as dt
        desktop = os.path.join(os.path.expanduser("~"), "Desktop")
        path = os.path.join(desktop, f"FoldeClean-report-{dt.datetime.now():%Y%m%d_%H%M%S}.txt")
        lines = ["FoldeClean error report (python)", f"time: {dt.datetime.now():%Y-%m-%d %H:%M:%S}", f"version: {VERSION}", f"note: {note}", ""]
        if self._scan_result:
            r = self._scan_result
            lines.append(f"scan root={r.root} files={len(r.files)} errors={r.errors}")
        if self._plan:
            lines.append(f"plan strategy={self._plan['strategy']} moves={len(self._plan['moves'])}")
        lines.append(f"executor {self._executor.snapshot()}")
        with open(path, "w", encoding="utf-8-sig") as f:
            f.write("\n".join(lines) + "\n")
        return {"path": path}

    # ---------------------------------------------------------------- 계획
    def build_plan(self, strategy_id: str, opts: dict | None = None, include_warn: bool = False,
                   dest_root: str | None = None, exclude_exts: list[str] | None = None, min_size: int = 0):
        if self._scan_result is None:
            return {"error": "먼저 폴더를 검사하세요."}
        self._set_progress("plan", 0, 0)
        try:
            strat = make_strategy(strategy_id, opts or {})
            active_rules = rl.load() if ed.pro_active() else None
            plan = build_plan(self._scan_result.files, self._scan_result.root, strat, dest_root=dest_root or None,
                              include_warn=include_warn, exclude_exts=set(exclude_exts or []), min_size=int(min_size or 0),
                              rules=active_rules or None)
        except Exception as e:  # noqa: BLE001
            traceback.print_exc()
            self._set_progress("idle")
            return {"error": str(e)}
        self._plan = plan
        self._set_progress("idle")
        out = dict(plan)
        out["moves"] = plan["moves"][:5000]
        out["moves_total"] = len(plan["moves"])
        return out

    def plan_moves(self, offset: int = 0, limit: int = 500, query: str = ""):
        if not self._plan:
            return []
        moves = self._plan["moves"]
        if query:
            q = query.lower()
            moves = [m for m in moves if q in m["name"].lower() or q in m["dst_rel"].lower()]
        return {"items": moves[offset: offset + limit], "total": len(moves)}

    def exclude_moves(self, paths: list[str]):
        if not self._plan:
            return 0
        s = set(paths)
        before = len(self._plan["moves"])
        self._plan["moves"] = [m for m in self._plan["moves"] if m["src"] not in s]
        return before - len(self._plan["moves"])

    # ---------------------------------------------------------------- 실행
    def execute(self, remove_empty_dirs: bool = True):
        if not self._plan or not self._plan["moves"]:
            return {"error": "실행할 계획이 없습니다."}
        if self._executor.snapshot()["running"]:
            return {"error": "이미 실행 중입니다."}
        moves = list(self._plan["moves"])
        root = self._plan["root"]

        def worker():
            self._executor.run(moves, remove_empty_dirs=remove_empty_dirs, root=root)
        threading.Thread(target=worker, daemon=True).start()
        return {"started": True, "total": len(moves)}

    def cancel(self):
        self._executor.cancel.set()
        return True

    def journals(self):
        return list_journals()

    def undo(self, journal_path: str):
        if self._executor.snapshot()["running"]:
            return {"error": "실행 중에는 되돌릴 수 없습니다."}
        threading.Thread(target=lambda: self._executor.undo(journal_path), daemon=True).start()
        return {"started": True}

    def reveal_path(self, path: str):
        try:
            import subprocess
            subprocess.Popen(["explorer.exe", f"/select,{path}"])
            return True
        except Exception:
            return False

    def open_path(self, path: str):
        try:
            os.startfile(path)  # type: ignore[attr-defined]
            return True
        except Exception:
            return False


# ---------------------------------------------------------------- 브라우저 모드 (개발/검증용)
def serve(api: Api, port: int = 8765):
    from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

    class H(SimpleHTTPRequestHandler):
        def __init__(self, *a, **k):
            super().__init__(*a, directory=UI_DIR, **k)

        def log_message(self, *a):  # 조용히
            pass

        def do_POST(self):
            name = self.path.rsplit("/", 1)[-1]
            n = int(self.headers.get("Content-Length", 0))
            args = json.loads(self.rfile.read(n) or b"[]")
            fn = getattr(api, name, None)
            if not fn or name.startswith("_"):
                self.send_response(404); self.end_headers(); return
            try:
                result = fn(*args)
            except Exception as e:  # noqa: BLE001
                traceback.print_exc()
                result = {"error": str(e)}
            body = json.dumps(result, ensure_ascii=False).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

    print(f"FoldeClean web mode: http://127.0.0.1:{port}")
    ThreadingHTTPServer(("127.0.0.1", port), H).serve_forever()


def main():
    api = Api()
    if "--web" in sys.argv:
        serve(api)
        return
    import webview
    api.window = webview.create_window(
        "FoldeClean · 안전한 폴더 정리", os.path.join(UI_DIR, "index.html"), js_api=api,
        width=1280, height=820, min_size=(960, 640), background_color="#0f1115",
    )
    webview.start(debug="--debug" in sys.argv)


if __name__ == "__main__":
    main()
