"""이동 계획 수립: 전략 적용 → 충돌 해결 → 흐름(출발 폴더 → 도착 폴더) 집계."""
from __future__ import annotations

import os
from collections import defaultdict

from .scanner import FileInfo
from .strategies import Strategy, category_of
from . import i18n


def _unique_dest(dest: str, taken: set[str]) -> str:
    key = os.path.normcase(dest)
    if key not in taken and not os.path.exists(dest):
        taken.add(key)
        return dest
    base, ext = os.path.splitext(dest)
    i = 1
    while True:
        cand = f"{base} ({i}){ext}"
        k = os.path.normcase(cand)
        if k not in taken and not os.path.exists(cand):
            taken.add(k)
            return cand
        i += 1


def _first_rule(rules: list[dict], f: FileInfo) -> dict | None:
    from . import rules as _rl
    return _rl.first_match(rules, f)


def build_plan(files: list[FileInfo], root: str, strategy: Strategy, dest_root: str | None = None,
               include_warn: bool = False, exclude_exts: set[str] | None = None,
               min_size: int = 0, max_moves: int | None = None, rules: list[dict] | None = None) -> dict:
    root = os.path.abspath(root)
    dest_root = os.path.abspath(dest_root or root)
    exclude_exts = {e.lower().lstrip(".") for e in (exclude_exts or set())}

    candidates = [
        f for f in files
        if f.risk == "safe" or (include_warn and f.risk == "warn")
    ]
    candidates = [f for f in candidates if f.ext not in exclude_exts and f.size >= min_size]
    strategy.prepare(candidates, root)

    moves: list[dict] = []
    taken: set[str] = set()
    skipped_same = 0
    flows: dict[tuple[str, str], dict] = defaultdict(lambda: {"count": 0, "size": 0})
    dest_dirs: dict[str, dict] = defaultdict(lambda: {"count": 0, "size": 0})
    src_dirs: dict[str, dict] = defaultdict(lambda: {"count": 0, "size": 0})

    for f in candidates:
        # 규칙(Pro)이 전략보다 먼저. 처음 맞는 하나만 적용된다.
        rel = None
        rule_name = None
        file_name = f.name
        matched = _first_rule(rules, f) if rules else None
        if matched is not None:
            from . import rules as _rl
            rule_name = matched.get("name") or matched.get("id")
            act = matched.get("action", "folder")
            if act == "skip":
                continue
            if act == "folder":
                exp = _rl.expand(matched.get("value", ""), f).strip()   # 폴더 이름에서만 공백 정리
                if exp:
                    rel = exp
            elif act == "prefix":
                file_name = _rl.expand(matched.get("value", ""), f) + f.name
            elif act == "suffix":
                stem, ext_ = os.path.splitext(f.name)
                file_name = stem + _rl.expand(matched.get("value", ""), f) + ext_
        if rel is None:
            rel = strategy.dest_dir(f, root)
        if rel is None:
            continue
        rel = os.path.normpath(rel)
        target_dir = os.path.join(dest_root, rel)
        if os.path.normcase(target_dir) == os.path.normcase(os.path.dirname(f.path)):
            skipped_same += 1
            continue
        # 목적지 폴더 안에 이미 있는 파일을 다시 그 안으로 넣는 경우 방지 (하위 정리 시)
        dest = _unique_dest(os.path.join(target_dir, file_name), taken)
        src_rel = os.path.relpath(os.path.dirname(f.path), root)
        src_rel = i18n.name("root") if src_rel == "." else src_rel.split(os.sep)[0]
        top_dest = rel.split(os.sep)[0]
        _k, label = category_of(f)
        moves.append({
            "src": f.path, "dst": dest, "name": f.name, "size": f.size, "ext": f.ext,
            "risk": f.risk, "reasons": f.reasons, "src_group": src_rel, "dst_group": top_dest,
            "dst_rel": rel, "category": label, "renamed": os.path.basename(dest) != f.name,
            "note": strategy.note(f), "rule": rule_name,
        })
        fl = flows[(src_rel, top_dest)]
        fl["count"] += 1; fl["size"] += f.size
        dest_dirs[rel]["count"] += 1; dest_dirs[rel]["size"] += f.size
        src_dirs[src_rel]["count"] += 1; src_dirs[src_rel]["size"] += f.size
        if max_moves and len(moves) >= max_moves:
            break

    total_size = sum(m["size"] for m in moves)
    warn_count = sum(1 for m in moves if m["risk"] == "warn")
    renamed = sum(1 for m in moves if m["renamed"])
    return {
        "root": root, "dest_root": dest_root, "strategy": strategy.id,
        "moves": moves,
        "summary": {
            "move_count": len(moves), "total_size": total_size, "warn_count": warn_count,
            "renamed": renamed, "skipped_same": skipped_same, "candidates": len(candidates),
        },
        "flows": [{"src": s, "dst": d, **v} for (s, d), v in sorted(flows.items(), key=lambda kv: -kv[1]["size"])],
        "dest_tree": _tree(dest_dirs),
        "src_groups": [{"name": k, **v} for k, v in sorted(src_dirs.items(), key=lambda kv: -kv[1]["size"])],
    }


def flows_at(moves: list[dict], dest: str = "") -> dict:
    """흐름도를 한 단계 안으로 들어가서 본다. dest 가 빈 문자열이면 최상위. (C# Planner.Flows 와 동일)"""
    sep = os.sep
    dest = (dest or "").strip().replace("/", sep).strip(sep)   # 화면은 / 를 쓸 수 있다
    prefix = dest + sep if dest else ""
    if dest:
        scoped = [m for m in moves
                  if m["dst_rel"].lower() == dest.lower() or m["dst_rel"].lower().startswith(prefix.lower())]
    else:
        scoped = moves

    def rest(m):
        r = m["dst_rel"] if not dest else (m["dst_rel"][len(prefix):] if len(m["dst_rel"]) > len(prefix) else "")
        return r.split(sep) if r else []

    flows: dict[tuple[str, str], dict] = defaultdict(lambda: {"count": 0, "size": 0})
    src_dirs: dict[str, dict] = defaultdict(lambda: {"count": 0, "size": 0})
    nodes: dict[str, dict] = defaultdict(lambda: {"count": 0, "size": 0})
    deeper: set[str] = set()

    for m in scoped:
        parts = rest(m)
        seg = parts[0] if parts else ""
        if len(parts) > 1:
            deeper.add(seg)
        for d, k in ((flows, (m["src_group"], seg)), (src_dirs, m["src_group"]), (nodes, seg)):
            d[k]["count"] += 1
            d[k]["size"] += m["size"]

    return {
        "path": dest,
        "crumbs": dest.split(sep) if dest else [],
        "move_count": len(scoped),
        "total_size": sum(m["size"] for m in scoped),
        "flows": [{"src": s, "dst": d, **v} for (s, d), v in sorted(flows.items(), key=lambda kv: -kv[1]["size"])],
        "src_groups": [{"name": k, **v} for k, v in sorted(src_dirs.items(), key=lambda kv: -kv[1]["size"])],
        "dest_nodes": [{"name": k, **v, "has_children": k in deeper} for k, v in sorted(nodes.items(), key=lambda kv: -kv[1]["size"])],
    }


def _tree(dest_dirs: dict[str, dict]) -> list[dict]:
    root: dict = {}
    for rel, v in dest_dirs.items():
        node = root
        for part in rel.split(os.sep):
            node = node.setdefault(part, {"_count": 0, "_size": 0, "_children": {}})
            node["_count"] += v["count"]; node["_size"] += v["size"]
            node = node["_children"]

    def conv(d: dict) -> list[dict]:
        out = []
        for name, n in sorted(d.items(), key=lambda kv: -kv[1]["_size"]):
            out.append({"name": name, "count": n["_count"], "size": n["_size"], "children": conv(n["_children"])})
        return out
    return conv(root)
