# @feature: Prefer UTF-8 stdout (best effort; ignore failure) | keywords: 部屋, ビュー
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
    # Prefer UTF-8 stdout (best effort; ignore failure)
    sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
except Exception:
    pass

from send_revit_command_durable import send_request, RevitMcpError  # type: ignore  # noqa: E402


# Room parameter names (Japanese) as Unicode escapes
NAME_PARAM = "\u540d\u524d"  # 名前
NUMBER_PARAM = "\u756a\u53f7"  # 番号
LIVE_LOAD_PARAM = "\u7a4d\u8f09\u8377\u91cd"  # 積載荷重
UNSET_KEY = "UNSET"


def unwrap(payload: Dict[str, Any]) -> Dict[str, Any]:
    """Unwrap JSON-RPC envelope down to { ok, ... }."""
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


def duplicate_view_with_suffix(port: int, base_view_id: int, suffix: str) -> Tuple[int, str]:
    """Duplicate base view, activate it, and clear view template."""
    info = unwrap(send_request(port, "get_view_info", {"viewId": base_view_id}))
    view = info.get("view") or info
    base_name = str(view.get("name") or f"View_{base_view_id}")

    dup = unwrap(send_request(port, "duplicate_view", {"viewId": base_view_id, "__smoke_ok": True}))
    new_vid = int(dup.get("viewId") or dup.get("newViewId") or 0)
    if new_vid <= 0:
        raise RuntimeError("duplicate_view did not return viewId.")

    new_name = f"{base_name} {suffix}".strip()
    try:
        send_request(port, "rename_view", {"viewId": new_vid, "newName": new_name})
    except RevitMcpError:
        # Name conflict etc. can be ignored
        pass

    # Activate new view and clear template
    try:
        send_request(port, "activate_view", {"viewId": new_vid})
    except RevitMcpError:
        pass

    try:
        send_request(port, "set_view_template", {"viewId": new_vid, "clear": True})
    except RevitMcpError:
        pass

    return new_vid, new_name


def extract_live_load_from_room_params(params_list: List[Dict[str, Any]]) -> Tuple[str, str]:
    """Return (roomLabel, liveLoadKey) from get_room_params parameters."""
    name_val = None
    number_val = None
    load_val = None

    for p in params_list:
        n = (p.get("name") or "").strip()
        if n == NAME_PARAM and name_val is None:
            name_val = p.get("display") or p.get("value") or p.get("raw")
        elif n == NUMBER_PARAM and number_val is None:
            number_val = p.get("display") or p.get("value") or p.get("raw")
        elif n == LIVE_LOAD_PARAM and load_val is None:
            load_val = p.get("display") or p.get("value") or p.get("raw")

    parts: List[str] = []
    if name_val:
        parts.append(str(name_val))
    if number_val:
        parts.append(str(number_val))
    room_label = " ".join(parts) if parts else ""

    if load_val is None or (isinstance(load_val, str) and not load_val.strip()):
        load_key = UNSET_KEY
    else:
        load_key = str(load_val)

    return room_label, load_key


def apply_override(
    port: int,
    view_id: int,
    element_ids: List[int],
    rgb: Tuple[int, int, int],
    transparency: int,
) -> None:
    if not element_ids:
        return
    r, g, b = rgb
    params = {
        "viewId": view_id,
        "elementIds": element_ids,
        "autoWorkingView": False,
        "detachViewTemplate": False,
        "r": int(r),
        "g": int(g),
        "b": int(b),
        "transparency": int(transparency),
        "refreshView": True,
        "__smoke_ok": True,
    }
    send_request(port, "set_visual_override", params)


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "Duplicate the active view, clear template, "
            "create masses from Rooms, and color them by live load with 50% transparency."
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP port")
    ap.add_argument(
        "--suffix",
        type=str,
        default="LiveLoadMassColor",
        help="Suffix appended to duplicated view name",
    )
    ap.add_argument(
        "--max-rooms",
        type=int,
        default=200,
        help="Maximum number of Rooms to process (safety limit)",
    )
    ap.add_argument(
        "--height-mode",
        type=str,
        default="bbox",
        choices=["bbox", "fixed"],
        help="Mass height mode: 'bbox' (Room bbox height) or 'fixed'",
    )
    ap.add_argument(
        "--fixed-height-mm",
        type=float,
        default=2800.0,
        help="Height in mm when height-mode=fixed, or fallback when bbox height is 0",
    )
    args = ap.parse_args(argv)

    port = args.port

    try:
        base_view_id, base_view_name = get_current_view(port)

        # 1) Duplicate view and clear template
        new_view_id, new_view_name = duplicate_view_with_suffix(port, base_view_id, args.suffix)

        # 2) Create masses from Rooms in the duplicated view
        masses_outer = send_request(
            port,
            "create_room_masses",
            {
                "viewId": new_view_id,
                "maxRooms": args.max_rooms,
                "heightMode": args.height_mode,
                "fixedHeightMm": float(args.fixed_height_mm),
                "useMassCategory": True,
            },
        )
        masses = unwrap(masses_outer)
        if not masses.get("ok", True):
            print(json.dumps(masses, ensure_ascii=False, indent=2))
            return 1

        created = masses.get("created") or []
        if not created:
            summary = {
                "ok": False,
                "code": "NO_MASSES_CREATED",
                "msg": "No masses were created by create_room_masses.",
                "baseViewId": base_view_id,
                "baseViewName": base_view_name,
                "newViewId": new_view_id,
                "newViewName": new_view_name,
            }
            print(json.dumps(summary, ensure_ascii=False, indent=2))
            return 0

        room_to_mass: Dict[int, int] = {}
        for row in created:
            try:
                rid = int(row.get("roomId"))
                mid = int(row.get("massId"))
            except Exception:
                continue
            if rid > 0 and mid > 0:
                room_to_mass[rid] = mid

        if not room_to_mass:
            summary = {
                "ok": False,
                "code": "NO_VALID_ROOM_MASS_PAIRS",
                "msg": "Could not extract valid roomId/massId pairs from create_room_masses result.",
                "baseViewId": base_view_id,
                "baseViewName": base_view_name,
                "newViewId": new_view_id,
                "newViewName": new_view_name,
            }
            print(json.dumps(summary, ensure_ascii=False, indent=2))
            return 0

        # 3) Group mass IDs by live load value
        groups_masses: Dict[str, List[int]] = defaultdict(list)
        room_summaries: List[Dict[str, Any]] = []

        for room_id, mass_id in room_to_mass.items():
            rp_outer = send_request(port, "get_room_params", {"roomId": room_id})
            rp = unwrap(rp_outer)
            params_list = rp.get("parameters") or []
            room_label, load_key = extract_live_load_from_room_params(params_list)

            groups_masses[load_key].append(mass_id)
            room_summaries.append(
                {
                    "roomId": room_id,
                    "massId": mass_id,
                    "label": room_label,
                    "liveLoad": load_key,
                }
            )

        # 4) Assign index-style colors based on sorted load keys
        keys_all = list(groups_masses.keys())
        special_keys = [k for k in keys_all if k in {UNSET_KEY, "-"}]
        normal_keys = [k for k in keys_all if k not in special_keys]

        try:
            normal_keys_sorted = sorted(normal_keys, key=lambda x: float(x))
        except Exception:
            normal_keys_sorted = sorted(normal_keys)

        # Distinct, index-style colors (roughly similar to AutoCAD color indices)
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

        color_map: Dict[str, Tuple[int, int, int]] = {}
        for idx, key in enumerate(normal_keys_sorted):
            color_map[key] = palette[idx % len(palette)]

        # Special: unset / "-" as gray
        if "-" in special_keys:
            color_map["-"] = (210, 210, 210)
        if UNSET_KEY in special_keys:
            color_map[UNSET_KEY] = (180, 180, 180)

        # 5) Apply colors to masses in the duplicated view (transparency 50%)
        for key, mass_ids in groups_masses.items():
            rgb = color_map.get(key, (255, 255, 255))
            apply_override(port, new_view_id, mass_ids, rgb, transparency=50)

        result = {
            "ok": True,
            "baseViewId": base_view_id,
            "baseViewName": base_view_name,
            "newViewId": new_view_id,
            "newViewName": new_view_name,
            "createdCount": len(room_to_mass),
            "groupCount": len(groups_masses),
            "groups": {k: len(v) for k, v in groups_masses.items()},
        }
        print(json.dumps(result, ensure_ascii=False, indent=2))
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

