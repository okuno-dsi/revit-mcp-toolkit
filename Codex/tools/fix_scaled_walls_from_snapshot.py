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
    ap = argparse.ArgumentParser(description="Audit and fix scaled walls from snapshot: delete duplicates, create missing")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--snapshot", type=str, required=True, help="Path to baseline snapshot (walls_with_coords.json)")
    ap.add_argument("--ref-id", type=int, required=True)
    ap.add_argument("--target-length-mm", type=float, default=2300.0)
    ap.add_argument("--out", type=str, default=str(Path("Work")/"大阪ビル"/"Logs"/"fix_scaled_walls_report.json"))
    args = ap.parse_args()

    port = args.port
    snap_path = Path(args.snapshot)
    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    snap = json.loads(snap_path.read_text(encoding="utf-8"))
    base_walls: List[Dict[str, Any]] = list((snap.get("walls") or []))
    if not base_walls:
        print(json.dumps({"ok": False, "error": "No walls in snapshot"}, ensure_ascii=False))
        return

    # Build levels map
    lv = unwrap(call_mcp(port, "get_levels", {"skip": 0, "count": 1000}))
    lvl_by_id = {int(L.get("levelId")): str(L.get("name")) for L in (lv.get("levels") or [])}

    # Origin from baseline walls (left-bottom)
    minx = min(float((w.get("start") or {}).get("x", 0.0)) for w in base_walls)
    minx = min(minx, min(float((w.get("end") or {}).get("x", 0.0)) for w in base_walls))
    miny = min(float((w.get("start") or {}).get("y", 0.0)) for w in base_walls)
    miny = min(miny, min(float((w.get("end") or {}).get("y", 0.0)) for w in base_walls))
    origin = (minx, miny)

    # Reference wall original length
    import math
    ref = next((w for w in base_walls if int(w.get("elementId") or w.get("id") or 0) == args.ref_id), None)
    if not ref:
        print(json.dumps({"ok": False, "error": "Ref wall not found in snapshot"}, ensure_ascii=False))
        return
    s = ref.get("start") or {}; e = ref.get("end") or {}
    x1, y1 = float(s.get("x", 0)), float(s.get("y", 0))
    x2, y2 = float(e.get("x", 0)), float(e.get("y", 0))
    L = math.hypot(x2 - x1, y2 - y1)
    if L <= 1e-6:
        print(json.dumps({"ok": False, "error": "Ref length zero"}, ensure_ascii=False))
        return
    target = q10(args.target_length_mm)
    scale = target / L

    def transform_xy(x: float, y: float) -> Tuple[float, float]:
        ox, oy = origin
        nx = ox + scale * (x - ox)
        ny = oy + scale * (y - oy)
        return q10(nx), q10(ny)

    # Expected segments after transform
    expected: List[Dict[str, Any]] = []
    for w in base_walls:
        bs = w.get("start") or {}; be = w.get("end") or {}
        bx1, by1 = float(bs.get("x", 0)), float(bs.get("y", 0))
        bx2, by2 = float(be.get("x", 0)), float(be.get("y", 0))
        ex1, ey1 = transform_xy(bx1, by1)
        ex2, ey2 = transform_xy(bx2, by2)
        expected.append({
            "start": (ex1, ey1),
            "end": (ex2, ey2),
            "z": float(bs.get("z", 0)),
            "levelId": int(w.get("levelId") or 0),
            "levelName": w.get("levelName") or lvl_by_id.get(int(w.get("levelId") or 0)) or "1FL",
            "typeName": w.get("typeName") or None,
            "height": w.get("height") or "level-to-level",
        })

    exp_keys = [seg_key(t["start"], t["end"]) for t in expected]

    # Current walls
    cur = unwrap(call_mcp(port, "get_walls", {"skip": 0, "count": 50000}))
    cur_walls: List[Dict[str, Any]] = list(cur.get("walls") or [])
    cur_map: Dict[Tuple[float, float, float, float], List[int]] = {}
    cur_items: Dict[Tuple[float, float, float, float], Dict[str, Any]] = {}
    for w in cur_walls:
        s = w.get("start") or {}; e = w.get("end") or {}
        cx1, cy1 = float(s.get("x", 0)), float(s.get("y", 0))
        cx2, cy2 = float(e.get("x", 0)), float(e.get("y", 0))
        k = seg_key((cx1, cy1), (cx2, cy2))
        cur_map.setdefault(k, []).append(int(w.get("elementId") or w.get("id") or 0))
        cur_items.setdefault(k, w)

    # Determine duplicates and missing
    duplicates: List[int] = []
    for k, ids in cur_map.items():
        if len(ids) > 1:
            duplicates.extend(ids[1:])  # keep first, delete the rest

    missing: List[int] = []
    to_create: List[Dict[str, Any]] = []
    for idx, k in enumerate(exp_keys):
        if k not in cur_map:
            missing.append(idx)
            to_create.append(expected[idx])

    # Delete duplicates
    deleted = 0
    for wid in duplicates:
        try:
            call_mcp(port, "delete_wall", {"elementId": wid})
            deleted += 1
        except Exception:
            pass

    # Create missing
    created: List[int] = []
    failures: List[Dict[str, Any]] = []
    for t in to_create:
        payload = {
            "start": {"x": t["start"][0], "y": t["start"][1], "z": t["z"]},
            "end":   {"x": t["end"][0],   "y": t["end"][1],   "z": t["z"]},
            "baseLevelName": t["levelName"],
            "heightMm": t["height"] if isinstance(t["height"], (int,float)) else "level-to-level",
        }
        if t.get("typeName"):
            payload["wallTypeName"] = t["typeName"]
        try:
            res = unwrap(call_mcp(port, "create_wall", payload))
            nid = int(res.get("elementId") or 0)
            if nid > 0:
                created.append(nid)
            else:
                failures.append({"payload": payload, "res": res})
        except Exception as ex:
            failures.append({"payload": payload, "error": str(ex)})

    report = {
        "ok": len(failures) == 0,
        "scale": target / L,
        "origin": {"x": origin[0], "y": origin[1]},
        "duplicatesDeleted": deleted,
        "missingCreated": len(created),
        "createdIds": created,
        "failures": failures,
    }
    out_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()

