"""사용자 규칙 (Pro). C# Core/Rules.cs 와 같은 규칙으로 동작한다."""
from __future__ import annotations

import datetime as dt
import json
import os
import re
import uuid

from .edition import APPDATA
from .scanner import FileInfo
from .strategies import category_of

RULES_PATH = os.path.join(APPDATA, "rules.json")
_TEXT_FIELDS = {"name", "ext", "dir", "path"}


def _field_value(f: FileInfo, field: str):
    if field == "size":
        return float(f.size)
    if field == "age":
        return (dt.datetime.now().timestamp() - f.mtime) / 86400.0
    if field == "ext":
        return f.ext
    if field == "dir":
        return os.path.basename(os.path.dirname(f.path))
    if field == "path":
        return f.path
    return f.name


def _match_cond(c: dict, f: FileInfo) -> bool:
    field = c.get("field", "name")
    op = c.get("op", "contains")
    val = c.get("value", "") or ""
    try:
        actual = _field_value(f, field)
        if field in ("size", "age"):
            v = float(val)
            return {"gt": actual > v, "lt": actual < v, "equals": abs(actual - v) < 1e-4}.get(op, False)
        a, v = str(actual).lower(), val.lower()
        if op == "contains":
            return v in a
        if op == "not_contains":
            return v not in a
        if op == "equals":
            return a == v
        if op == "starts":
            return a.startswith(v)
        if op == "ends":
            return a.endswith(v)
        if op == "regex":
            return re.search(val, str(actual), re.IGNORECASE) is not None
    except Exception:
        return False
    return False


def matches(rule: dict, f: FileInfo) -> bool:
    conds = rule.get("conditions") or []
    return bool(rule.get("enabled", True)) and len(conds) > 0 and all(_match_cond(c, f) for c in conds)


def expand(template: str, f: FileInfo) -> str:
    if not template:
        return ""
    t = dt.datetime.fromtimestamp(f.mtime)
    _k, label = category_of(f)
    return (template
            .replace("{year}", t.strftime("%Y"))
            .replace("{month}", t.strftime("%m"))
            .replace("{day}", t.strftime("%d"))
            .replace("{ext}", f.ext)
            .replace("{category}", label)
            .replace("{dir}", os.path.basename(os.path.dirname(f.path))))
    # 앞뒤 공백은 지우지 않는다. 이름 앞뒤에 붙일 때 공백이 의도된 값일 수 있다.


def load() -> list[dict]:
    try:
        with open(RULES_PATH, encoding="utf-8") as fh:
            return json.load(fh).get("rules", [])
    except Exception:
        return []


def save(rules: list[dict]) -> int:
    os.makedirs(APPDATA, exist_ok=True)
    for r in rules:
        if not r.get("id"):
            r["id"] = uuid.uuid4().hex[:8]
    tmp = RULES_PATH + ".tmp"
    with open(tmp, "w", encoding="utf-8") as fh:
        json.dump({"rules": rules}, fh, ensure_ascii=False, indent=2)
    os.replace(tmp, RULES_PATH)
    return len(rules)


def first_match(rules: list[dict], f: FileInfo) -> dict | None:
    for r in rules:
        if matches(r, f):
            return r
    return None


def test(rule: dict, files: list[FileInfo], sample: int = 8) -> dict:
    hits = [f for f in files if matches(rule, f)]
    return {
        "count": len(hits),
        "samples": [{"name": f.name, "dest": None if rule.get("action") == "skip" else expand(rule.get("value", ""), f)}
                    for f in hits[:sample]],
    }
