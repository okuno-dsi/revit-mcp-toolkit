import argparse
import json
import math
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
    ap = argparse.ArgumentParser(description="Export per-wall old/new XY coordinates after plan scaling; match to current walls")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--snapshot", type=str, required=True, help="Path to baseline walls_with_coords.json (old coordinates)")
    ap.add_argument("--ref-id", type=int, required=True)
    ap.add_argument("--target-length-mm", type=float, default=2300.0)
    ap.add_argument("--out", type=str, default=str(Path("Work")/"大阪ビル"/"walls_coords_mapping.json"))
    args = ap.parse_args()

    snap = json.loads(Path(args.snapshot).read_text(encoding="utf-8"))
    base_walls: List[Dict[str, Any]] = list((snap.get("walls") or []))
    if not base_walls:
        Path(args.out).write_text(json.dumps({"ok": False, "error": "No walls in snapshot"}, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({"ok": False, "error": "No walls in snapshot"}, ensure_ascii=False))
        return

    # Left-bottom origin from snapshot
    minx = min(min(float((w.get("start") or {}).get("x", 0.0)), float((w.get("end") or {}).get("x", 0.0))) for w in base_walls)
    miny = min(min(float((w.get("start") or {}).get("y", 0.0)), float((w.get("end") or {}).get("y", 0.0))) for w in base_walls)
    origin = (minx, miny)

    # Reference wall original length
    ref = next((w for w in base_walls if int(w.get("elementId") or w.get("id") or 0) == args.ref_id), None)
    if not ref:
        Path(args.out).write_text(json.dumps({"ok": False, "error": "Ref wall not in snapshot"}, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({"ok": False, "error": "Ref wall not in snapshot"}, ensure_ascii=False))
        return
    s = ref.get("start") or {}; e = ref.get("end") or {}
    x1, y1 = float(s.get("x", 0)), float(s.get("y", 0))
    x2, y2 = float(e.get("x", 0)), float(e.get("y", 0))
    L = math.hypot(x2 - x1, y2 - y1)
    if L <= 1e-6:
        Path(args.out).write_text(json.dumps({"ok": False, "error": "Ref length zero"}, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({"ok": False, "error": "Ref length zero"}, ensure_ascii=False))
        return
    target = q10(args.target_length_mm)
    scale = target / L

    def transform_xy(px: float, py: float) -> Tuple[float, float]:
        ox, oy = origin
        nx = ox + scale * (px - ox)
        ny = oy + scale * (py - oy)
        return q10(nx), q10(ny)

    # Current walls map by segment key
    port = args.port
    cur = unwrap(call_mcp(port, "get_walls", {"skip": 0, "count": 50000}))
    cur_walls: List[Dict[str, Any]] = list(cur.get("walls") or [])
    cur_map: Dict[Tuple[float, float, float, float], int] = {}
    for w in cur_walls:
        s2 = w.get("start") or {}; e2 = w.get("end") or {}
        cx1, cy1 = float(s2.get("x", 0)), float(s2.get("y", 0))
        cx2, cy2 = float(e2.get("x", 0)), float(e2.get("y", 0))
        k = seg_key((cx1, cy1), (cx2, cy2))
        cur_map[k] = int(w.get("elementId") or w.get("id") or 0)

    mappings: List[Dict[str, Any]] = []
    matched = 0
    for w in base_walls:
        s1 = w.get("start") or {}; e1 = w.get("end") or {}
        ox1, oy1 = float(s1.get("x", 0)), float(s1.get("y", 0))
        ox2, oy2 = float(e1.get("x", 0)), float(e1.get("y", 0))
        nx1, ny1 = transform_xy(ox1, oy1)
        nx2, ny2 = transform_xy(ox2, oy2)
        k = seg_key((nx1, ny1), (nx2, ny2))
        new_id = cur_map.get(k)
        if new_id:
            matched += 1
        mappings.append({
            "oldId": int(w.get("elementId") or w.get("id") or 0),
            "newId": new_id,
            "oldStart": {"x": ox1, "y": oy1},
            "oldEnd":   {"x": ox2, "y": oy2},
            "newStart": {"x": nx1, "y": ny1},
            "newEnd":   {"x": nx2, "y": ny2},
            "levelId": int(w.get("levelId") or 0),
            "typeName": w.get("typeName"),
        })

    out = {
        "ok": True,
        "refId": args.ref_id,
        "scale": scale,
        "origin": {"x": origin[0], "y": origin[1]},
        "matched": matched,
        "total": len(mappings),
        "items": mappings,
    }
    Path(args.out).write_text(json.dumps(out, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"ok": True, "savedTo": str(Path(args.out))}, ensure_ascii=False))


if __name__ == "__main__":
    main()

