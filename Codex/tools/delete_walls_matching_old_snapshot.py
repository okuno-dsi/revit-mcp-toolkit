import argparse
import json
from pathlib import Path
from typing import Any, Dict, List, Tuple

from tools.mcp_safe import call_mcp


def unwrap(x: Dict[str, Any]) -> Dict[str, Any]:
    top = x.get("result") or x
    if isinstance(top, dict) and "result" in top:
        top = top["result"]
    return top if isinstance(top, dict) else {}


def seg_key(a: Tuple[float, float], b: Tuple[float, float], tol: float = 5.0) -> Tuple[float, float, float, float]:
    ax, ay = round(a[0] / tol) * tol, round(a[1] / tol) * tol
    bx, by = round(b[0] / tol) * tol, round(b[1] / tol) * tol
    if (bx, by) < (ax, ay):
        ax, ay, bx, by = bx, by, ax, ay
    return (ax, ay, bx, by)


def main() -> None:
    ap = argparse.ArgumentParser(description="Delete walls that match OLD snapshot geometry (unscaled) to remove duplicates")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--snapshot", type=str, required=True, help="Path to baseline walls_with_coords.json (old geometry)")
    ap.add_argument("--tolerance-mm", type=float, default=5.0, help="XY tolerance for matching (mm)")
    ap.add_argument("--out", type=str, default=str(Path("Work")/"大阪ビル"/"Logs"/"delete_old_walls_only_report.json"))
    args = ap.parse_args()

    snap = json.loads(Path(args.snapshot).read_text(encoding="utf-8"))
    base_walls: List[Dict[str, Any]] = list((snap.get("walls") or []))
    if not base_walls:
        Path(args.out).write_text(json.dumps({"ok": False, "error": "No walls in snapshot"}, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({"ok": False, "error": "No walls in snapshot"}, ensure_ascii=False))
        return

    # Build old (unscaled) keys set
    old_keys = set()
    tol = float(args.tolerance_mm)
    for w in base_walls:
        s = w.get("start") or {}; e = w.get("end") or {}
        ox1, oy1 = float(s.get("x", 0)), float(s.get("y", 0))
        ox2, oy2 = float(e.get("x", 0)), float(e.get("y", 0))
        old_keys.add(seg_key((ox1, oy1), (ox2, oy2), tol=tol))

    # Current walls
    port = args.port
    cur = unwrap(call_mcp(port, "get_walls", {"skip": 0, "count": 50000}))
    cur_walls: List[Dict[str, Any]] = list(cur.get("walls") or [])
    to_delete: List[int] = []
    kept: List[int] = []
    for w in cur_walls:
        s2 = w.get("start") or {}; e2 = w.get("end") or {}
        cx1, cy1 = float(s2.get("x", 0)), float(s2.get("y", 0))
        cx2, cy2 = float(e2.get("x", 0)), float(e2.get("y", 0))
        k = seg_key((cx1, cy1), (cx2, cy2), tol=tol)
        wid = int(w.get("elementId") or w.get("id") or 0)
        if k in old_keys:
            to_delete.append(wid)
        else:
            kept.append(wid)

    deleted = 0
    failures: List[int] = []
    for wid in to_delete:
        try:
            call_mcp(port, "delete_wall", {"elementId": wid})
            deleted += 1
        except Exception:
            failures.append(wid)

    report = {
        "ok": len(failures) == 0,
        "oldSnapshotWalls": len(base_walls),
        "matchedForDeletion": len(to_delete),
        "deleted": deleted,
        "deleteFailures": failures,
        "kept": len(kept),
        "toleranceMm": tol,
    }
    Path(args.out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()

