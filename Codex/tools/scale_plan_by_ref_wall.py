import argparse
import json
import math
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from tools.mcp_safe import call_mcp


def unwrap(x: Dict[str, Any]) -> Dict[str, Any]:
    top = x.get("result") or x
    if isinstance(top, dict) and "result" in top:
        top = top["result"]
    return top if isinstance(top, dict) else {}


def q10(v: float) -> float:
    try:
        return round(float(v) / 10.0) * 10.0
    except Exception:
        return float(v or 0.0)


def fetch_walls(port: int) -> List[Dict[str, Any]]:
    allw: List[Dict[str, Any]] = []
    skip = 0
    page = 1000
    while True:
        tp = unwrap(call_mcp(port, "get_walls", {"skip": skip, "count": page}))
        chunk = list((tp.get("walls") or []))
        allw.extend(chunk)
        if len(chunk) < page:
            break
        skip += page
    return allw


def main() -> None:
    ap = argparse.ArgumentParser(description="Scale entire plan (walls) by ref wall new/old length ratio; XY only; Z ignored")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--target-length-mm", type=float, default=2300.0, help="Target length for the reference wall")
    ap.add_argument("--ref-id", type=int, help="Reference wall elementId. If omitted, use currently selected wall.")
    ap.add_argument("--snap10", action="store_true", help="Snap endpoints to 10mm grid (default on)")
    ap.add_argument("--no-snap10", dest="snap10", action="store_false")
    ap.set_defaults(snap10=True)
    ap.add_argument("--out", type=str, default=str(Path("Work")/"大阪ビル"/"Logs"/"scale_plan_result.json"))
    args = ap.parse_args()

    port = args.port
    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    # Levels map
    levels = unwrap(call_mcp(port, "get_levels", {"skip": 0, "count": 500}))
    lvl_by_id = {int(L.get("levelId")): str(L.get("name")) for L in (levels.get("levels") or [])}

    # Fetch walls
    walls = fetch_walls(port)
    if not walls:
        out_path.write_text(json.dumps({"ok": False, "error": "No walls in model"}, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({"ok": False, "error": "No walls in model"}, ensure_ascii=False))
        return
    by_id = {int(w.get("elementId") or w.get("id") or 0): w for w in walls}

    # Determine reference wall id
    ref_id: Optional[int] = args.ref_id
    if not ref_id:
        sel = unwrap(call_mcp(port, "get_selected_element_ids", {}))
        for e in list(sel.get("elementIds") or []):
            i = int(e)
            if i in by_id:
                ref_id = i
                break
    if not ref_id or ref_id not in by_id:
        out_path.write_text(json.dumps({"ok": False, "error": "Reference wall not found (select a wall or pass --ref-id)."}, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({"ok": False, "error": "Reference wall not found"}, ensure_ascii=False))
        return

    # Compute bottom-left origin across all wall endpoints (XY)
    minx = float("inf"); miny = float("inf")
    def upd_min(pt: Dict[str, Any]):
        nonlocal minx, miny
        x = float(pt.get("x", 0.0)); y = float(pt.get("y", 0.0))
        if x < minx: minx = x
        if y < miny: miny = y

    for w in walls:
        s = w.get("start") or {}; e = w.get("end") or {}
        upd_min(s); upd_min(e)
    origin = (minx, miny)

    # Reference wall length
    rw = by_id[ref_id]
    s = rw.get("start") or {}; e = rw.get("end") or {}
    x1,y1 = float(s.get("x",0)), float(s.get("y",0))
    x2,y2 = float(e.get("x",0)), float(e.get("y",0))
    L = math.hypot(x2-x1, y2-y1)
    if L <= 1e-6:
        out_path.write_text(json.dumps({"ok": False, "error": "Reference wall length is zero"}, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({"ok": False, "error": "Reference wall length is zero"}, ensure_ascii=False))
        return

    target = args.target_length_mm
    # snap target to 10mm grid as well
    target = q10(target)
    scale = target / L

    def transform_xy(x: float, y: float) -> Tuple[float, float]:
        ox, oy = origin
        nx = ox + scale * (x - ox)
        ny = oy + scale * (y - oy)
        if args.snap10:
            nx = q10(nx); ny = q10(ny)
        return nx, ny

    # Delete all walls first
    deleted = 0
    live = fetch_walls(port)
    for w in live:
        wid = int(w.get("elementId") or w.get("id") or 0)
        try:
            call_mcp(port, "delete_wall", {"elementId": wid})
            deleted += 1
        except Exception:
            pass

    # Recreate scaled walls
    created: List[int] = []
    failures: List[Dict[str, Any]] = []
    for w in walls:
        s = w.get("start") or {}; e = w.get("end") or {}
        x1,y1 = float(s.get("x",0)), float(s.get("y",0))
        x2,y2 = float(e.get("x",0)), float(e.get("y",0))
        z  = float(s.get("z",0))
        nx1, ny1 = transform_xy(x1, y1)
        nx2, ny2 = transform_xy(x2, y2)
        level_name = w.get("levelName") or lvl_by_id.get(int(w.get("levelId") or 0)) or "1FL"
        wall_type  = w.get("typeName")
        height     = w.get("height")
        if not isinstance(height, (int,float)) or height <= 0:
            height = "level-to-level"
        payload = {
            "start": {"x": nx1, "y": ny1, "z": z},
            "end":   {"x": nx2, "y": ny2, "z": z},
            "baseLevelName": level_name,
            "heightMm": height,
        }
        if wall_type:
            payload["wallTypeName"] = wall_type
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
        "ok": len(created) > 0 and len(failures) == 0,
        "refId": ref_id,
        "oldLength": L,
        "targetLength": target,
        "scale": scale,
        "origin": {"x": origin[0], "y": origin[1]},
        "deletedWalls": deleted,
        "created": created,
        "failures": failures,
    }
    out_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False))


if __name__ == "__main__":
    main()

