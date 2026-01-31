# @feature: fill rooms by live load with filled regions | keywords: 部屋, ビュー
import argparse
import json
import os
import sys
from collections import defaultdict
from typing import Any, Dict, List, Tuple


def _add_scripts_to_path() -> None:
    here = os.path.dirname(os.path.abspath(__file__))
    if here not in sys.path:
        sys.path.insert(0, here)


_add_scripts_to_path()

try:
    sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
except Exception:
    pass

from send_revit_command_durable import send_request, RevitMcpError  # type: ignore  # noqa: E402


LIVE_LOAD_PARAM = "\u7a4d\u8f09\u8377\u91cd"  # 積載荷重
UNSET_KEY = "UNSET"


def unwrap(payload: Dict[str, Any]) -> Dict[str, Any]:
    obj: Any = payload
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def get_current_view(port: int) -> Tuple[int, str, str]:
    cur = unwrap(send_request(port, "get_current_view", {}))
    vid = int(cur.get("viewId") or 0)
    name = str(cur.get("name") or "")
    vtype = str(cur.get("viewType") or "")
    if vid <= 0:
        raise RuntimeError("Could not resolve current viewId.")
    return vid, name, vtype


def get_room_boundaries_for_view(
    port: int, view_id: int
) -> List[Dict[str, Any]]:
    rb_outer = send_request(
        port,
        "get_room_boundaries",
        {
            "viewId": int(view_id),
            "includeIslands": False,
            "boundaryLocation": "Finish",
        },
    )
    rb = unwrap(rb_outer)
    rooms = rb.get("rooms") or []
    return rooms


def get_room_live_load(port: int, room_id: int) -> str:
    rp_outer = send_request(port, "get_room_params", {"roomId": int(room_id)})
    rp = unwrap(rp_outer)
    params = rp.get("parameters") or []

    load_val: Any = None
    for p in params:
        n = (p.get("name") or "").strip()
        if n == LIVE_LOAD_PARAM and load_val is None:
            load_val = p.get("display") or p.get("value") or p.get("raw")

    if load_val is None:
        return UNSET_KEY
    s = str(load_val).strip()
    return s if s else UNSET_KEY


def build_outer_loop_points(room_entry: Dict[str, Any]) -> List[Dict[str, float]]:
    """
    From get_room_boundaries room entry, build a single polygon loop
    as [{x,y,z},...] in mm, using the first loop's segment starts.
    """
    loops = room_entry.get("loops") or []
    if not loops:
        return []
    first = loops[0]
    segs = first.get("segments") or []
    pts: List[Dict[str, float]] = []
    for seg in segs:
        st = seg.get("start") or {}
        pts.append(
            {
                "x": float(st.get("x", 0.0)),
                "y": float(st.get("y", 0.0)),
                "z": float(st.get("z", 0.0)),
            }
        )
    # ensure at least 3 points
    if len(pts) < 3:
        return []
    return pts


def get_filled_region_types(port: int) -> List[Dict[str, Any]]:
    ft_outer = send_request(port, "get_filled_region_types", {})
    ft = unwrap(ft_outer)
    return ft.get("items") or []


def choose_pattern_types_by_load(
    types: List[Dict[str, Any]],
    load_keys: List[str],
) -> Dict[str, int]:
    """
    Assign a FilledRegionType (typeId) per load key, preferring
    distinct fill patterns (foregroundPatternId) so that groups
    differ by hatch pattern as much as possible.
    """
    if not types:
        return {}

    # Deduplicate by foregroundPatternId so we get as many distinct patterns as possible
    pattern_types: Dict[int, Dict[str, Any]] = {}
    for it in types:
        pid = int(it.get("foregroundPatternId") or 0)
        if pid <= 0:
            continue
        if pid not in pattern_types:
            pattern_types[pid] = it

    candidates = list(pattern_types.values()) or types

    # sort load keys for stable assignment (try numeric)
    normal_keys = [k for k in load_keys if k not in {UNSET_KEY, "-"}]
    special_keys = [k for k in load_keys if k in {UNSET_KEY, "-"}]
    try:
        normal_sorted = sorted(normal_keys, key=lambda x: float(x))
    except Exception:
        normal_sorted = sorted(normal_keys)

    ordered_keys = normal_sorted + sorted(special_keys)

    mapping: Dict[str, int] = {}
    for idx, key in enumerate(ordered_keys):
        it = candidates[idx % len(candidates)]
        tid = int(it.get("typeId") or 0)
        if tid > 0:
            mapping[key] = tid
    return mapping


def apply_index_colors_to_regions(
    port: int,
    view_id: int,
    created: List[Dict[str, Any]],
) -> Dict[str, Tuple[int, int, int]]:
    """
    Color FilledRegions by live load group using an index-style palette.
    """
    groups: Dict[str, List[int]] = defaultdict(list)
    for row in created:
        try:
            key = str(row.get("loadKey"))
            eid = int(row.get("elementId") or 0)
        except Exception:
            continue
        if eid > 0:
            groups[key].append(eid)

    if not groups:
        return {}

    # Palette (index-style colors)
    palette: List[Tuple[int, int, int]] = [
        (255, 0, 0),      # red
        (255, 255, 0),    # yellow
        (0, 255, 0),      # green
        (0, 255, 255),    # cyan
        (0, 0, 255),      # blue
        (255, 0, 255),    # magenta
        (255, 128, 0),    # orange
        (128, 0, 255),    # purple
        (0, 128, 128),    # teal
        (128, 128, 0),    # olive
    ]

    keys_all = list(groups.keys())
    special_keys = [k for k in keys_all if k in {UNSET_KEY, "-"}]
    normal_keys = [k for k in keys_all if k not in special_keys]

    try:
        normal_sorted = sorted(normal_keys, key=lambda x: float(x))
    except Exception:
        normal_sorted = sorted(normal_keys)

    ordered_keys = normal_sorted + sorted(special_keys)

    color_map: Dict[str, Tuple[int, int, int]] = {}
    for idx, key in enumerate(ordered_keys):
        color_map[key] = palette[idx % len(palette)]

    # Apply overrides per group
    for key, elem_ids in groups.items():
        rgb = color_map.get(key, (255, 255, 255))
        if not elem_ids:
            continue
        send_request(
            port,
            "set_visual_override",
            {
                "viewId": int(view_id),
                "elementIds": list(elem_ids),
                "autoWorkingView": False,
                "detachViewTemplate": False,
                "r": int(rgb[0]),
                "g": int(rgb[1]),
                "b": int(rgb[2]),
                "transparency": 0,
                "refreshView": True,
                "__smoke_ok": True,
            },
        )

    return color_map


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "In the current plan view, create FilledRegion per Room by live load "
            "(積載荷重) and fill with hatch patterns (e.g. 平行線 45 度 1 mm)."
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP port")
    args = ap.parse_args(argv)

    port = int(args.port)

    try:
        view_id, view_name, view_type = get_current_view(port)

        rooms_b = get_room_boundaries_for_view(port, view_id)
        if not rooms_b:
            print(
                json.dumps(
                    {
                        "ok": False,
                        "code": "NO_ROOMS",
                        "msg": "No rooms found in current view for boundaries.",
                        "viewId": view_id,
                        "viewName": view_name,
                    },
                    ensure_ascii=False,
                    indent=2,
                )
            )
            return 0

        # Group rooms by live load
        groups: Dict[str, List[Dict[str, Any]]] = defaultdict(list)
        for r in rooms_b:
            rid = int(r.get("roomId") or 0)
            if rid <= 0:
                continue
            loops_pts = build_outer_loop_points(r)
            if not loops_pts:
                continue
            load_key = get_room_live_load(port, rid)
            rec = {
                "roomId": rid,
                "loops": [loops_pts],
            }
            groups[load_key].append(rec)

        if not groups:
            print(
                json.dumps(
                    {
                        "ok": False,
                        "code": "NO_VALID_ROOMS",
                        "msg": "No rooms with valid outer loops found.",
                        "viewId": view_id,
                        "viewName": view_name,
                    },
                    ensure_ascii=False,
                    indent=2,
                )
            )
            return 0

        load_keys = list(groups.keys())

        # Choose FilledRegionType per load key
        types = get_filled_region_types(port)
        type_map = choose_pattern_types_by_load(types, load_keys)

        created: List[Dict[str, Any]] = []
        errors: List[Dict[str, Any]] = []

        for load_key, room_recs in groups.items():
            type_id = int(type_map.get(load_key, 0))
            for rec in room_recs:
                rid = rec["roomId"]
                loops = rec["loops"]
                params: Dict[str, Any] = {"viewId": view_id, "loops": loops}
                if type_id > 0:
                    params["typeId"] = type_id
                try:
                    fr_outer = send_request(port, "create_filled_region", params)
                    fr = unwrap(fr_outer)
                    if not fr.get("ok", True):
                        errors.append(
                            {
                                "roomId": rid,
                                "loadKey": load_key,
                                "error": fr,
                            }
                        )
                    else:
                        created.append(
                            {
                                "roomId": rid,
                                "loadKey": load_key,
                                "elementId": fr.get("elementId"),
                                "typeId": fr.get("typeId"),
                            }
                        )
                except RevitMcpError as e:
                    errors.append(
                        {
                            "roomId": rid,
                            "loadKey": load_key,
                            "error": {
                                "where": e.where,
                                "message": str(e),
                                "payload": e.payload,
                            },
                        }
                    )
                except Exception as e:
                    errors.append(
                        {
                            "roomId": rid,
                            "loadKey": load_key,
                            "error": {"message": repr(e)},
                        }
                    )

        # Apply index colors by live load group to the created regions
        color_map = {}
        if created:
            color_map = apply_index_colors_to_regions(port, view_id, created)

        # Save summary log
        repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        work_dir = os.path.join(repo_root, "Work", f"Project_{port}", "Logs")
        os.makedirs(work_dir, exist_ok=True)
        out_path = os.path.join(
            work_dir, f"fill_rooms_by_live_load_patterns_view_{view_id}.json"
        )
        summary = {
            "ok": len(errors) == 0,
            "viewId": view_id,
            "viewName": view_name,
            "viewType": view_type,
            "groupCount": len(groups),
            "createdCount": len(created),
            "colorMap": {k: list(v) for k, v in color_map.items()},
            "created": created,
            "errors": errors,
        }
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(summary, f, ensure_ascii=False, indent=2)

        print(json.dumps(summary, ensure_ascii=False, indent=2))
        return 0 if summary["ok"] else 1

    except RevitMcpError as e:
        err = {
            "ok": False,
            "code": "MCP_ERROR",
            "where": e.where,
            "message": str(e),
            "payload": e.payload,
        }
        print(json.dumps(err, ensure_ascii=False, indent=2))
        return 1
    except Exception as e:
        err = {
            "ok": False,
            "code": "UNEXPECTED_ERROR",
            "message": repr(e),
        }
        print(json.dumps(err, ensure_ascii=False, indent=2))
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
