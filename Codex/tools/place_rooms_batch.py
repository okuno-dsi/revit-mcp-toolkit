import argparse
import json
from pathlib import Path
from typing import Any, Dict, List

from tools.mcp_safe import call_mcp


def unwrap(top: Dict[str, Any]) -> Dict[str, Any]:
    if not isinstance(top, dict):
        return {}
    r = top.get("result") or top
    if isinstance(r, dict) and "result" in r:
        r = r["result"]
    return r if isinstance(r, dict) else {}


def main() -> None:
    ap = argparse.ArgumentParser(description="Place rooms in all empty regions across levels with numbering")
    ap.add_argument("--port", type=int, required=True)
    ap.add_argument("--prefix", type=str, default="AutoRoom")
    ap.add_argument("--start", type=int, default=1)
    ap.add_argument("--max-per-level", type=int, default=0, help="0 = unlimited")
    ap.add_argument("--out-dir", type=str, default=str(Path("Manuals")/"Logs"))
    args = ap.parse_args()

    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    # 1) levels
    lv = call_mcp(args.port, "get_levels", {"skip": 0, "count": 200})
    lvt = unwrap(lv)
    levels: List[Dict[str, Any]] = list(lvt.get("levels") or [])
    levels.sort(key=lambda x: str(x.get("name", "")))

    regions_all: List[Dict[str, Any]] = []
    placed: List[Dict[str, Any]] = []
    failures: List[Dict[str, Any]] = []

    # 2) find regions per level (onlyEmpty)
    for L in levels:
        lid = int(L.get("levelId"))
        lname = str(L.get("name"))
        fr = call_mcp(args.port, "find_room_placeable_regions", {"levelId": lid, "onlyEmpty": True, "includeLabelPoint": True, "includeLoops": False})
        ft = unwrap(fr)
        regs: List[Dict[str, Any]] = list(ft.get("regions") or [])
        for r in regs:
            regions_all.append({
                "levelId": lid,
                "levelName": lname,
                "circuitIndex": int(r.get("circuitIndex", -1)),
                "isClosed": bool(r.get("isClosed", False)),
                "hasRoom": bool(r.get("hasRoom", False)),
                "area_m2": r.get("area_m2"),
            })

    # 3) place rooms
    counter = int(args.start)
    per_level: Dict[int, int] = {}
    for row in regions_all:
        if not row.get("isClosed", False) or row.get("hasRoom", False):
            continue
        lid = int(row["levelId"])
        per_level.setdefault(lid, 0)
        if args.max_per_level > 0 and per_level[lid] >= args.max_per_level:
            continue
        name = f"{args.prefix}-{counter:03d}"
        counter += 1
        payload = {"levelId": lid, "circuitIndex": int(row["circuitIndex"]), "name": name}
        try:
            pr = call_mcp(args.port, "place_room_in_circuit", payload)
            pt = unwrap(pr)
            if bool(pt.get("ok")):
                placed.append({
                    "ok": True,
                    "levelId": lid,
                    "levelName": row.get("levelName"),
                    "circuitIndex": int(row["circuitIndex"]),
                    "name": name,
                    "roomId": pt.get("roomId"),
                })
                per_level[lid] += 1
            else:
                failures.append({"ok": False, **payload, "error": pt})
        except Exception as e:
            failures.append({"ok": False, **payload, "error": str(e)})

    # 4) save
    (out_dir/"room_placeable_regions_all.json").write_text(json.dumps({"ok": True, "regions": regions_all}, ensure_ascii=False, indent=2), encoding="utf-8")
    (out_dir/"place_rooms_batch_results.json").write_text(json.dumps({
        "ok": True,
        "placed": placed,
        "failures": failures,
        "summary": {
            "totalRegions": len(regions_all),
            "placed": len(placed),
            "failed": len(failures)
        }
    }, ensure_ascii=False, indent=2), encoding="utf-8")

    print(json.dumps({"ok": True, "placed": len(placed), "failed": len(failures)}, ensure_ascii=False))


if __name__ == "__main__":
    main()

