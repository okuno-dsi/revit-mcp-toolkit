# @feature: Prefer UTF-8 for stdout (best-effort) | keywords: 部屋, ビュー, レベル, スナップショット
import argparse
import json
import os
import sys
from typing import Any, Dict, List, Tuple


def _add_scripts_to_path() -> None:
    here = os.path.dirname(os.path.abspath(__file__))
    if here not in sys.path:
        sys.path.insert(0, here)


_add_scripts_to_path()

try:
    # Prefer UTF-8 for stdout (best-effort)
    sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
except Exception:
    pass

from send_revit_command_durable import send_request, RevitMcpError  # type: ignore  # noqa: E402
from set_room_mass_comments_from_live_load import (  # type: ignore  # noqa: E402
    find_latest_mapping_json,
    load_room_mass_mapping,
)


NAME_PARAM = "\u540d\u524d"  # 名前
NUMBER_PARAM = "\u756a\u53f7"  # 番号
LIVE_LOAD_PARAM = "\u7a4d\u8f09\u8377\u91cd"  # 積載荷重
COMMENT_PARAM = "\u30b3\u30e1\u30f3\u30c8"  # コメント


def unwrap(payload: Dict[str, Any]) -> Dict[str, Any]:
    obj: Any = payload
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def get_current_view(port: int) -> Tuple[int, str]:
    cur = unwrap(send_request(port, "get_current_view", {}))
    vid = int(cur.get("viewId") or 0)
    name = str(cur.get("name") or "")
    if vid <= 0:
        raise RuntimeError("Could not resolve current viewId.")
    return vid, name


def get_room_labels_and_live_load(
    port: int, room_id: int
) -> Tuple[str, str, str]:
    """Return (name, number, liveLoad) for a Room."""
    rp_outer = send_request(port, "get_room_params", {"roomId": int(room_id)})
    rp = unwrap(rp_outer)
    params = rp.get("parameters") or []

    name_val: Any = None
    number_val: Any = None
    load_val: Any = None

    for p in params:
        n = (p.get("name") or "").strip()
        if n == NAME_PARAM and name_val is None:
            name_val = p.get("display") or p.get("value") or p.get("raw")
        elif n == NUMBER_PARAM and number_val is None:
            number_val = p.get("display") or p.get("value") or p.get("raw")
        elif n == LIVE_LOAD_PARAM and load_val is None:
            load_val = p.get("display") or p.get("value") or p.get("raw")

    name_s = str(name_val).strip() if name_val is not None else ""
    number_s = str(number_val).strip() if number_val is not None else ""
    live_s = str(load_val).strip() if load_val is not None else ""

    return name_s, number_s, live_s


def get_room_boundaries_for_rooms(
    port: int, room_ids: List[int]
) -> Dict[int, Dict[str, Any]]:
    """Call get_room_boundaries once and index by roomId."""
    if not room_ids:
        return {}
    rb_outer = send_request(
        port,
        "get_room_boundaries",
        {
            "roomIds": list(sorted(set(int(r) for r in room_ids))),
            "includeIslands": False,
            "boundaryLocation": "Finish",
        },
    )
    rb = unwrap(rb_outer)
    rooms = rb.get("rooms") or []

    by_id: Dict[int, Dict[str, Any]] = {}
    for r in rooms:
        try:
            rid = int(r.get("roomId"))
        except Exception:
            continue
        if rid > 0:
            by_id[rid] = r
    return by_id


def get_mass_comment(port: int, mass_id: int) -> str:
    mp_outer = send_request(
        port, "get_mass_instance_parameters", {"elementId": int(mass_id)}
    )
    mp = unwrap(mp_outer)
    params = mp.get("parameters") or []
    comment_val: Any = None
    for p in params:
        n = (p.get("name") or "").strip()
        if n == COMMENT_PARAM:
            comment_val = p.get("value") or p.get("display") or p.get("raw")
            break
    return str(comment_val).strip() if comment_val is not None else ""


def get_mass_instances_map(port: int) -> Dict[int, Dict[str, Any]]:
    """Get all mass instances and index by elementId."""
    mi_outer = send_request(
        port,
        "get_mass_instances",
        {
            "summaryOnly": False,
            "includeLocation": True,
        },
    )
    mi = unwrap(mi_outer)
    masses = mi.get("masses") or []

    by_id: Dict[int, Dict[str, Any]] = {}
    for m in masses:
        try:
            eid = int(m.get("elementId"))
        except Exception:
            continue
        if eid > 0:
            by_id[eid] = m
    return by_id


def get_view_overrides_map(
    port: int, view_id: int
) -> Dict[int, Dict[str, Any]]:
    """Get per-element visual overrides (color, transparency) for active view."""
    vo_outer = send_request(port, "get_visual_overrides_in_view", {})
    vo = unwrap(vo_outer)
    # Guard: ensure this is for the expected viewId, but still return data.
    result_view_id = int(vo.get("viewId") or 0)
    if result_view_id != int(view_id):
        # Still usable, but record may not match the intended view.
        pass

    overridden = vo.get("overridden") or []
    by_id: Dict[int, Dict[str, Any]] = {}
    for item in overridden:
        try:
            eid = int(item.get("elementId"))
        except Exception:
            continue
        if eid <= 0:
            continue
        by_id[eid] = {
            "transparency": item.get("transparency"),
            "color": item.get("color") or {},
        }
    return by_id


def get_repo_root() -> str:
    # This script is at Codex/Scripts/Reference/*.py
    # → repo root is three levels up.
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "Export current Room-based Masses (DirectShape) to a JSON snapshot\n"
            "including Room labels, boundaries (mm), mass comments, and view colors."
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP port")
    ap.add_argument(
        "--mapping-json",
        type=str,
        default="",
        help=(
            "Path to create_room_masses_*.json mapping file. "
            "If omitted, the latest file under Projects/Project_<port>/Logs is used."
        ),
    )
    ap.add_argument(
        "--out",
        type=str,
        default="",
        help=(
            "Output JSON path. If omitted, writes to "
            "Projects/Project_<port>/Logs/room_masses_snapshot_<viewId>.json"
        ),
    )
    args = ap.parse_args(argv)

    port = int(args.port)

    try:
        view_id, view_name = get_current_view(port)

        mapping_path = args.mapping_json or find_latest_mapping_json(port)
        pairs = load_room_mass_mapping(mapping_path)

        room_ids = [p["roomId"] for p in pairs]
        mass_ids = [p["massId"] for p in pairs]

        # Fetch shared data
        boundaries_by_room = get_room_boundaries_for_rooms(port, room_ids)
        mass_instances = get_mass_instances_map(port)
        overrides_by_elem = get_view_overrides_map(port, view_id)

        # Build per-mass entries
        entries: List[Dict[str, Any]] = []
        for pair in pairs:
            room_id = int(pair["roomId"])
            mass_id = int(pair["massId"])

            # Room labels and live load
            name_s, number_s, live_s = get_room_labels_and_live_load(port, room_id)

            # Room boundaries (mm, including z)
            boundary = boundaries_by_room.get(room_id, {})

            # Mass instance info (location, level, etc.)
            mass_info = mass_instances.get(mass_id, {})

            # Mass comment
            comment_s = get_mass_comment(port, mass_id)

            # Visual overrides in current view
            vis = overrides_by_elem.get(mass_id, {})

            entries.append(
                {
                    "roomId": room_id,
                    "massId": mass_id,
                    "room": {
                        "name": name_s,
                        "number": number_s,
                        "liveLoad": live_s,
                        "level": boundary.get("level", ""),
                        "uniqueId": boundary.get("uniqueId", ""),
                    },
                    "comment": comment_s,
                    "boundary": boundary.get("loops", []),
                    "massInstance": mass_info,
                    "visualOverride": vis,
                }
            )

        # Compose snapshot
        snapshot = {
            "ok": True,
            "schema": "RoomMassSnapshot.v1",
            "port": port,
            "view": {
                "viewId": view_id,
                "name": view_name,
            },
            "mappingPath": mapping_path,
            "count": len(entries),
            "entries": entries,
        }

        # Resolve output path
        out_path = args.out
        if not out_path:
            repo_root = get_repo_root()
            work_dir = os.path.join(
                repo_root, "Work", f"Project_{port}", "Logs"
            )
            os.makedirs(work_dir, exist_ok=True)
            out_path = os.path.join(
                work_dir, f"room_masses_snapshot_view_{view_id}.json"
            )

        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(snapshot, f, ensure_ascii=False, indent=2)

        print(
            json.dumps(
                {
                    "ok": True,
                    "outPath": out_path,
                    "count": len(entries),
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 0

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





