"""판 구분: Standard(무료) / Trial(체험) / Pro. C# Core/Edition.cs 와 같은 규칙."""
from __future__ import annotations

import datetime as dt
import json
import os

TRIAL_DAYS = 14
APPDATA = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")), "FoldeClean")
STATE = os.path.join(APPDATA, "edition.json")


def _load() -> dict:
    try:
        with open(STATE, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def _save(s: dict) -> None:
    os.makedirs(APPDATA, exist_ok=True)
    with open(STATE, "w", encoding="utf-8") as f:
        json.dump(s, f, ensure_ascii=False, indent=2)


def _trial_start() -> dt.datetime | None:
    v = _load().get("trial_started")
    if not v:
        return None
    try:
        return dt.datetime.fromisoformat(v.replace("Z", "+00:00"))
    except ValueError:
        return None


def trial_days_left() -> int | None:
    st = _trial_start()
    if st is None:
        return None
    used = (dt.datetime.now(dt.timezone.utc) - st).total_seconds() / 86400
    import math
    return max(0, math.ceil(TRIAL_DAYS - used))


def has_license() -> bool:
    return bool((_load().get("license") or "").strip())


def current() -> str:
    if has_license():
        return "pro"
    left = trial_days_left()
    return "trial" if (left or 0) > 0 else "standard"


def pro_active() -> bool:
    return current() in ("pro", "trial")


def start_trial() -> bool:
    if _trial_start() is not None or has_license():
        return False
    s = _load()
    s["trial_started"] = dt.datetime.now(dt.timezone.utc).isoformat()
    _save(s)
    return True


def info() -> dict:
    return {
        "edition": current(), "pro_active": pro_active(), "trial_days_left": trial_days_left(),
        "trial_used": _trial_start() is not None, "trial_days": TRIAL_DAYS,
    }
