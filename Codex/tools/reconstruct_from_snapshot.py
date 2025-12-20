import argparse
import json
from pathlib import Path
from typing import Any, Dict, List

from tools.mcp_safe import call_mcp


def unwrap(x: Dict[str, Any]) -> Dict[str, Any]:
    top = x.get("result") or x
    if isinstance(top, dict) and "result" in top:
        top = top["result"]
    return top if isinstance(top, dict) else {}


def main() -> None:
    ap = argparse.ArgumentParser(description="Reconstruct model from snapshot bundle (levels, grids, walls, doors, rooms)")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--snapshot", type=str, default=str(Path("Work")/"snapshot_bundle.json"))
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    bundle = json.loads(Path(args.snapshot).read_text(encoding="utf-8"))
    port = args.port

    # 1) Levels: ensure names/elevations
    existing_levels = unwrap(call_mcp(port, "get_levels", {"skip":0,"count":200})).get("levels", [])
    by_name = { str(l.get("name")): l for l in existing_levels }
    for L in bundle.get("levels", []):
        name = str(L.get("name"))
        elev = float(L.get("elevation") or 0.0)
        if name in by_name:
            cur = by_name[name]
            lid = int(cur.get("levelId"))
            if not args.dry_run:
                try:
                    call_mcp(port, "update_level_elevation", {"levelId": lid, "elevation": elev})
                except Exception:
                    pass
        else:
            if not args.dry_run:
                call_mcp(port, "create_level", {"name": name, "elevation": elev})

    # 2) Grids: try to recreate by axis/name/position if present
    grids = bundle.get("grids", [])
    if grids:
        # heuristic: separate X and Y by orientation if available; else by extents
        xs: List[float] = []
        ys: List[float] = []
        names_x: List[str] = []
        names_y: List[str] = []
        for g in grids:
            name = str(g.get("name") or g.get("label") or "")
            crv = g.get("curve") or {}
            s = crv.get("start") or {}
            e = crv.get("end") or {}
            x1,y1 = float(s.get("x",0)), float(s.get("y",0))
            x2,y2 = float(e.get("x",0)), float(e.get("y",0))
            if abs(x1-x2) < 1e-6:
                xs.append(x1); names_x.append(name)
            elif abs(y1-y2) < 1e-6:
                ys.append(y1); names_y.append(name)
        if not args.dry_run:
            if xs:
                call_mcp(port, "create_grids", {"axis": "X", "positions": xs, "names": names_x})
            if ys:
                call_mcp(port, "create_grids", {"axis": "Y", "positions": ys, "names": names_y})

    # 3) Walls: create by start/end, base level name (if present) and type name
    for w in bundle.get("walls", []):
        start = w.get("start") or {}
        end = w.get("end") or {}
        baseLevelName = w.get("levelName") or "1FL"
        wallTypeName = w.get("typeName") or None
        payload = {
            "start": {"x": start.get("x",0), "y": start.get("y",0), "z": start.get("z",0)},
            "end": {"x": end.get("x",0), "y": end.get("y",0), "z": end.get("z",0)},
            "baseLevelName": baseLevelName,
            "heightMm": "level-to-level",
        }
        if wallTypeName:
            payload["wallTypeName"] = wallTypeName
        if not args.dry_run:
            try:
                call_mcp(port, "create_wall", payload)
            except Exception:
                pass

    # 4) Doors: create on host wall by location and type
    for d in bundle.get("doors", []):
        wid = d.get("hostWallId") or d.get("wallId") or None
        loc = d.get("location") or d.get("center") or {}
        tname = d.get("typeName") or None
        if not wid or not tname:
            continue
        payload = {"wallId": int(wid), "location": {"x": loc.get("x",0), "y": loc.get("y",0), "z": loc.get("z",0)}, "typeName": tname, "opTimeoutMs": 180000}
        if not args.dry_run:
            try:
                call_mcp(port, "create_door_on_wall", payload)
            except Exception:
                pass

    # 5) Rooms: place by level and center, then set name if available
    for r in bundle.get("rooms", []):
        name = r.get("name") or None
        lvl = r.get("levelId") or r.get("level") or None
        center = r.get("center") or {}
        # resolve levelId by name when string
        if isinstance(lvl, str):
            try:
                levels = unwrap(call_mcp(port, "get_levels", {"skip":0,"count":500})).get("levels", [])
                for L in levels:
                    if str(L.get("name")) == lvl:
                        lvl = int(L.get("levelId"))
                        break
            except Exception:
                pass
        if not isinstance(lvl, int):
            continue
        payload = {"levelId": int(lvl), "x": center.get("x",0), "y": center.get("y",0)}
        if not args.dry_run:
            try:
                res = unwrap(call_mcp(port, "create_room", payload))
                rid = int(res.get("elementId") or 0)
                if rid > 0 and name:
                    try:
                        call_mcp(port, "set_room_param", {"elementId": rid, "paramName": "Name", "value": name})
                    except Exception:
                        pass
            except Exception:
                pass

    print(json.dumps({"ok": True, "reconstructed": True}, ensure_ascii=False))


if __name__ == "__main__":
    main()

