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


def q10(v: float) -> float:
    return round(float(v) / 10.0) * 10.0


def seg_key(a: Tuple[float, float], b: Tuple[float, float], tol: float = 5.0) -> Tuple[float, float, float, float]:
    ax, ay = round(a[0] / tol) * tol, round(a[1] / tol) * tol
    bx, by = round(b[0] / tol) * tol, round(b[1] / tol) * tol
    if (bx, by) < (ax, ay):
        ax, ay, bx, by = bx, by, ax, ay
    return (ax, ay, bx, by)


def main() -> None:
    ap = argparse.ArgumentParser(description="Delete old (pre-scaled) walls based on snapshot + ref scale; keep only expected new walls")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--snapshot", type=str, required=True, help="Path to baseline walls_with_coords.json")
    ap.add_argument("--ref-id", type=int, required=True)
    ap.add_argument("--target-length-mm", type=float, default=2300.0)
    ap.add_argument("--out", type=str, default=str(Path("Work")/"大阪ビル"/"Logs"/"delete_old_walls_report.json"))
    args = ap.parse_args()

    snap = json.loads(Path(args.snapshot).read_text(encoding="utf-8"))
    base_walls: List[Dict[str, Any]] = list((snap.get("walls") or []))
    if not base_walls:
        print(json.dumps({"ok": False, "error": "No walls in snapshot"}, ensure_ascii=False))
        return

    # Origin from baseline
    minx = min(min(float((w.get("start") or {}).get("x", 0.0)), float((w.get("end") or {}).get("x", 0.0))) for w in base_walls)
    miny = min(min(float((w.get("start") or {}).get("y", 0.0)), float((w.get("end") or {}).get("y", 0.0))) for w in base_walls)
    origin = (minx, miny)

    # Ref scale
    import math
    ref = next((w for w in base_walls if int(w.get("elementId") or w.get("id") or 0) == args.ref_id), None)
    if not ref:
        print(json.dumps({"ok": False, "error": "Ref wall not in snapshot"}, ensure_ascii=False))
        return
    sx, sy = float((ref.get("start") or {}).get("x", 0)), float((ref.get("start") or {}).get("y", 0))
    ex, ey = float((ref.get("end") or {}).get("x", 0)), float((ref.get("end") or {}).get("y", 0))
    L = math.hypot(ex - sx, ey - sy)
    if L <= 1e-6:
        print(json.dumps({"ok": False, "error": "Ref length zero"}, ensure_ascii=False))
        return
    target = q10(args.target_length_mm)
    scale = target / L

    def transform_xy(px: float, py: float) -> Tuple[float, float]:
        ox, oy = origin
        nx = ox + scale * (px - ox)
        ny = oy + scale * (py - oy)
        return q10(nx), q10(ny)

    # Expected keys after transform
    expected_keys = set()
    for w in base_walls:
        s = w.get("start") or {}; e = w.get("end") or {}
        ox1, oy1 = float(s.get("x", 0)), float(s.get("y", 0))
        ox2, oy2 = float(e.get("x", 0)), float(e.get("y", 0))
        nx1, ny1 = transform_xy(ox1, oy1)
        nx2, ny2 = transform_xy(ox2, oy2)
        expected_keys.add(seg_key((nx1, ny1), (nx2, ny2)))

    # Current walls
    cur = unwrap(call_mcp(args.port, "get_walls", {"skip": 0, "count": 50000}))
    cur_walls: List[Dict[str, Any]] = list(cur.get("walls") or [])
    to_delete: List[int] = []
    kept: List[int] = []
    for w in cur_walls:
        s2 = w.get("start") or {}; e2 = w.get("end") or {}
        cx1, cy1 = float(s2.get("x", 0)), float(s2.get("y", 0))
        cx2, cy2 = float(e2.get("x", 0)), float(e2.get("y", 0))
        k = seg_key((cx1, cy1), (cx2, cy2))
        wid = int(w.get("elementId") or w.get("id") or 0)
        if k in expected_keys:
            kept.append(wid)
        else:
            to_delete.append(wid)

    deleted = 0
    failures: List[int] = []
    for wid in to_delete:
        try:
            call_mcp(args.port, "delete_wall", {"elementId": wid})
            deleted += 1
        except Exception:
            failures.append(wid)

    report = {
        "ok": len(failures) == 0,
        "scale": scale,
        "origin": {"x": origin[0], "y": origin[1]},
        "expected": len(expected_keys),
        "kept": len(kept),
        "deleted": deleted,
        "deleteFailures": failures,
    }
    Path(args.out).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()

