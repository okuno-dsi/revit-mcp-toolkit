import argparse
import json
from pathlib import Path
from typing import Any, Dict, List, Tuple

from tools.mcp_safe import call_mcp


def unwrap(res: Dict[str, Any]) -> Dict[str, Any]:
    top = res.get("result") or res
    if isinstance(top, dict) and "result" in top:
        top = top["result"]
    return top if isinstance(top, dict) else {}


def quant(v: float, tol: float = 1.0) -> float:
    try:
        return round(float(v) / tol) * tol
    except Exception:
        return float(v or 0.0)


def cmp_levels(cur: List[Dict[str, Any]], base: List[Dict[str, Any]]) -> Dict[str, Any]:
    byn_cur = {str(x.get("name")): float(x.get("elevation") or 0.0) for x in cur}
    byn_base = {str(x.get("name")): float(x.get("elevation") or 0.0) for x in base}
    missing = sorted([n for n in byn_base.keys() if n not in byn_cur])
    extra = sorted([n for n in byn_cur.keys() if n not in byn_base])
    changed: List[Tuple[str, float, float]] = []
    for n in byn_cur.keys() & byn_base.keys():
        e1, e0 = byn_cur[n], byn_base[n]
        if abs(e1 - e0) > 0.5:  # mm
            changed.append((n, e0, e1))
    return {"missing": missing, "extra": extra, "changed": changed}


def cmp_points(cur: List[Tuple[float, float]], base: List[Tuple[float, float]], tol: float = 5.0) -> Dict[str, Any]:
    def norm(items):
        return {(quant(x, tol), quant(y, tol)) for (x, y) in items}
    A = norm(cur); B = norm(base)
    missing = sorted(list(B - A))
    extra = sorted(list(A - B))
    return {"missing": missing, "extra": extra}


def main() -> None:
    ap = argparse.ArgumentParser(description="Compare current model with a saved snapshot bundle")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--snapshot", type=str, required=True)
    ap.add_argument("--out", type=str, default=str(Path("Work")/"diff_snapshot_report.json"))
    args = ap.parse_args()

    snap = json.loads(Path(args.snapshot).read_text(encoding="utf-8"))

    # Fetch current
    cur_levels = unwrap(call_mcp(args.port, "get_levels", {"skip": 0, "count": 500})).get("levels", [])
    try:
        cur_grids = unwrap(call_mcp(args.port, "get_grids", {"skip": 0, "count": 5000})).get("grids", [])
    except Exception:
        cur_grids = []
    cur_walls = unwrap(call_mcp(args.port, "get_walls", {"skip": 0, "count": 20000})).get("walls", [])
    try:
        cur_rooms = unwrap(call_mcp(args.port, "get_rooms", {"skip": 0, "count": 20000})).get("rooms", [])
    except Exception:
        cur_rooms = []
    try:
        cur_doors = unwrap(call_mcp(args.port, "get_doors", {"skip": 0, "count": 20000})).get("doors", [])
    except Exception:
        cur_doors = []
    try:
        cur_windows = unwrap(call_mcp(args.port, "get_windows", {"skip": 0, "count": 20000})).get("windows", [])
    except Exception:
        cur_windows = []

    # Base
    base_levels = snap.get("levels") or []
    base_grids = snap.get("grids") or []
    base_walls = snap.get("walls") or []
    base_rooms = snap.get("rooms") or []
    base_doors = snap.get("doors") or []
    base_windows = snap.get("windows") or []

    # Levels: by name+elevation
    levels_diff = cmp_levels(cur_levels, base_levels)

    # Grids: compare by axis & intercepts
    def grid_points(lst):
        pts = []
        for g in lst:
            crv = g.get("curve") or {}
            s = crv.get("start") or {}; e = crv.get("end") or {}
            x1,y1 = float(s.get("x",0)), float(s.get("y",0)); x2,y2 = float(e.get("x",0)), float(e.get("y",0))
            if abs(x1-x2) < 1e-6:
                pts.append((x1, 0.0))
            elif abs(y1-y2) < 1e-6:
                pts.append((0.0, y1))
        return pts

    grids_diff = cmp_points(grid_points(cur_grids), grid_points(base_grids), tol=5.0)

    # Walls: compare by midpoint + length approx to reduce combinatorics
    def wall_fprints(lst):
        fps = []
        for w in lst:
            s = w.get("start") or {}; e = w.get("end") or {}
            x1,y1 = float(s.get("x",0)), float(s.get("y",0)); x2,y2 = float(e.get("x",0)), float(e.get("y",0))
            mx,my = (x1+x2)/2.0, (y1+y2)/2.0
            length = ((x2-x1)**2 + (y2-y1)**2) ** 0.5
            fps.append((quant(mx, 5.0), quant(my, 5.0), quant(length, 5.0)))
        return set(fps)

    A = wall_fprints(cur_walls); B = wall_fprints(base_walls)
    walls_diff = {"missing": sorted(list(B - A)), "extra": sorted(list(A - B))}

    # Rooms: by name set
    cur_room_names = {str(r.get("name")) for r in cur_rooms}
    base_room_names = {str(r.get("name")) for r in base_rooms}
    rooms_diff = {"missing": sorted(list(base_room_names - cur_room_names)), "extra": sorted(list(cur_room_names - base_room_names))}

    # Doors/Windows: by location/type footprint (lenient)
    def inst_fps(lst):
        fps = []
        for d in lst:
            loc = d.get("location") or d.get("center") or {}
            x,y = float(loc.get("x",0)), float(loc.get("y",0))
            t = d.get("typeName") or ""
            fps.append((quant(x, 5.0), quant(y, 5.0), t))
        return set(fps)

    doors_diff = {"missing": sorted(list(inst_fps(base_doors) - inst_fps(cur_doors))), "extra": sorted(list(inst_fps(cur_doors) - inst_fps(base_doors)))}
    windows_diff = {"missing": sorted(list(inst_fps(base_windows) - inst_fps(cur_windows))), "extra": sorted(list(inst_fps(cur_windows) - inst_fps(base_windows)))}

    report = {
        "ok": True,
        "levels": levels_diff,
        "grids": grids_diff,
        "walls": walls_diff,
        "rooms": rooms_diff,
        "doors": doors_diff,
        "windows": windows_diff,
    }

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"ok": True, "savedTo": str(out)}, ensure_ascii=False))


if __name__ == "__main__":
    main()

