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


def build_plan(files: list[FileInfo], root: str, strategy: Strategy, dest_root: str | None = None,
               include_warn: bool = False, exclude_exts: set[str] | None = None,
               min_size: int = 0, max_moves: int | None = None) -> dict:
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
        rel = strategy.dest_dir(f, root)
        if rel is None:
            continue
        rel = os.path.normpath(rel)
        target_dir = os.path.join(dest_root, rel)
        if os.path.normcase(target_dir) == os.path.normcase(os.path.dirname(f.path)):
            skipped_same += 1
            continue
        # 목적지 폴더 안에 이미 있는 파일을 다시 그 안으로 넣는 경우 방지 (하위 정리 시)
        dest = _unique_dest(os.path.join(target_dir, f.name), taken)
        src_rel = os.path.relpath(os.path.dirname(f.path), root)
        src_rel = i18n.name("root") if src_rel == "." else src_rel.split(os.sep)[0]
        top_dest = rel.split(os.sep)[0]
        _k, label = category_of(f)
        moves.append({
            "src": f.path, "dst": dest, "name": f.name, "size": f.size, "ext": f.ext,
            "risk": f.risk, "reasons": f.reasons, "src_group": src_rel, "dst_group": top_dest,
            "dst_rel": rel, "category": label, "renamed": os.path.basename(dest) != f.name,
            "note": strategy.note(f),
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
