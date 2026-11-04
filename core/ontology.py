"""분류 체계 (ontology.json). C# Core/Ontology.cs 와 같은 규칙으로 동작한다.

중요: 정리 폴더 이름에는 최상위 개념만 쓴다. 세부 개념(계약서, 스크린샷 …)은
내 규칙과 검색에서만 쓴다. 그래야 예전에 정리해 둔 결과와 어긋나지 않는다.
"""
from __future__ import annotations

import json
import os

from . import i18n
from .edition import APPDATA

_HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_PATH = os.path.join(_HERE, "ontology.json")
USER_PATH = os.path.join(APPDATA, "ontology.json")

_data: dict | None = None
_by_id: dict[str, dict] = {}
_ext_to_top: dict[str, str] = {}
_with_patterns: list[dict] = []


def _load() -> None:
    global _data, _by_id, _ext_to_top, _with_patterns
    d = None
    for p in (USER_PATH, DEFAULT_PATH):          # 사용자 파일이 있으면 우선
        try:
            with open(p, encoding="utf-8") as f:
                d = json.load(f)
            if d.get("concepts"):
                break
        except Exception:
            d = None
    _data = d or {"concepts": []}
    _by_id = {}
    _ext_to_top = {}
    _with_patterns = []
    for c in _data["concepts"]:
        _by_id.setdefault(c["id"].lower(), c)
        if c.get("parent") is None:
            for e in c.get("ext", []):
                _ext_to_top.setdefault(e.lower(), c["id"])
        if c.get("name_patterns"):
            _with_patterns.append(c)


def data() -> dict:
    if _data is None:
        _load()
    return _data  # type: ignore[return-value]


def reload() -> None:
    global _data
    _data = None
    data()


def top_id_of(ext: str) -> str:
    data()
    return _ext_to_top.get((ext or "").lower(), "other")


def label(concept_id: str) -> str:
    data()
    c = _by_id.get((concept_id or "").lower())
    if not c:
        return concept_id or ""
    labels = c.get("labels", {})
    return labels.get(i18n.LANG) or labels.get("ko") or c["id"]


def ancestors(concept_id: str) -> list[str]:
    data()
    out: list[str] = []
    seen: set[str] = set()
    cur = concept_id
    while cur and cur.lower() not in seen:
        seen.add(cur.lower())
        out.append(cur)
        c = _by_id.get(cur.lower())
        cur = c.get("parent") if c else None
    return out


def is_a(concept_id: str, ancestor_id: str) -> bool:
    return any(a.lower() == (ancestor_id or "").lower() for a in ancestors(concept_id))


def concepts_of(f) -> list[str]:
    """첫 번째는 최상위 개념(폴더 이름용), 뒤에 이름으로 알아낸 세부 개념."""
    data()
    top = top_id_of(f.ext)
    out = [top]
    name = f.name.lower()
    for c in _with_patterns:
        if c["id"].lower() == top.lower():
            continue
        parent = c.get("parent")
        if parent and not is_a(top, parent):     # 같은 갈래 아래에서만 인정
            continue
        if any(p and p.lower() in name for p in c.get("name_patterns", [])):
            out.append(c["id"])
    return out


def expand_query(query: str) -> list[str]:
    """검색어를 동의어로 넓힌다. "계약" -> 계약, 계약서, contract …"""
    data()
    q = (query or "").strip()
    if not q:
        return []
    ql = q.lower()
    out = {q}
    for c in _data["concepts"]:  # type: ignore[index]
        words = list(c.get("labels", {}).values())
        for vs in c.get("synonyms", {}).values():
            words.extend(vs)
        hit = c["id"].lower() == ql or any(w and (w.lower() in ql or ql in w.lower()) for w in words)
        if not hit:
            continue
        out.update(w for w in words if w)
        out.update(p for p in c.get("name_patterns", []) if p)
    return list(out)


def for_picker() -> list[dict]:
    data()
    order: list[dict] = []
    for top in [c for c in _data["concepts"] if c.get("parent") is None]:  # type: ignore[index]
        order.append(top)
        order.extend(c for c in _data["concepts"] if (c.get("parent") or "").lower() == top["id"].lower())  # type: ignore[index]
    return [{"id": c["id"], "label": label(c["id"]), "parent": c.get("parent"), "top": c.get("parent") is None}
            for c in order]
