"""중복 파일 탐지: 크기 → 앞 64KB 해시 → 전체 해시. 해시는 스레드 풀로 병렬."""
from __future__ import annotations

import hashlib
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor

from .scanner import FileInfo

HEAD = 64 * 1024


def _hash(path: str, limit: int | None) -> str | None:
    h = hashlib.blake2b(digest_size=16)
    try:
        with open(path, "rb") as f:
            if limit:
                h.update(f.read(limit))
            else:
                for chunk in iter(lambda: f.read(1 << 20), b""):
                    h.update(chunk)
    except OSError:
        return None
    return h.hexdigest()


def _group(files: list[FileInfo], limit: int | None, workers: int) -> list[list[FileInfo]]:
    with ThreadPoolExecutor(max_workers=workers) as ex:
        digests = list(ex.map(lambda f: _hash(f.path, limit), files))
    buckets: dict[str, list[FileInfo]] = defaultdict(list)
    for f, d in zip(files, digests):
        if d:
            buckets[d].append(f)
    return [g for g in buckets.values() if len(g) > 1]


def find_duplicates(files: list[FileInfo], workers: int = 8) -> list[list[FileInfo]]:
    by_size: dict[int, list[FileInfo]] = defaultdict(list)
    for f in files:
        by_size[f.size].append(f)
    result: list[list[FileInfo]] = []
    for size, grp in by_size.items():
        if len(grp) < 2:
            continue
        stage1 = _group(grp, HEAD, workers) if size > HEAD else [grp]
        for g in stage1:
            if size <= HEAD:
                result.extend(_group(g, None, workers))
            else:
                result.extend(_group(g, None, workers))
    return result
