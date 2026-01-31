# @feature: Prefer UTF-8 for stdout (best-effort) | keywords: 部屋, ビュー
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
    # Prefer UTF-8 for stdout (best-effort)
    sys.stdout.reconfigure(encoding="utf-8")  # type: ignore[attr-defined]
except Exception:
    pass

from send_revit_command_durable import send_request, RevitMcpError  # type: ignore  # noqa: E402


NAME_PARAM = "\u540d\u524d"  # 名前
NUMBER_PARAM = "\u756a\u53f7"  # 番号
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


def get_current_view(port: int) -> Tuple[int, str]:
    cur = unwrap(send_request(port, "get_current_view", {}))
    vid = int(cur.get("viewId") or 0)
    name = str(cur.get("name") or "")
    if vid <= 0:
        raise RuntimeError("Could not resolve current viewId.")
    return vid, name


def create_room_masses_for_view(
    port: int,
    view_id: int,
    max_rooms: int,
    height_mode: str,
    fixed_height_mm: float,
) -> Dict[int, int]:
    masses_outer = send_request(
        port,
        "create_room_masses",
        {
            "viewId": view_id,
            "maxRooms": int(max_rooms),
            "heightMode": height_mode,
            "fixedHeightMm": float(fixed_height_mm),
            "useMassCategory": True,
        },
    )
    masses = unwrap(masses_outer)
    if not masses.get("ok", True):
        raise RuntimeError(f"create_room_masses failed: {json.dumps(masses, ensure_ascii=False)}")

    created = masses.get("created") or []
    room_to_mass: Dict[int, int] = {}
    for row in created:
        try:
            rid = int(row.get("roomId"))
            mid = int(row.get("massId"))
        except Exception:
            continue
        if rid > 0 and mid > 0:
            room_to_mass[rid] = mid
    return room_to_mass


def create_mass_only_3d_view(port: int, name: str) -> int:
    v3_outer = send_request(port, "create_3d_view", {"name": name, "templateViewId": 0})
    v3 = unwrap(v3_outer)
    view3d_id = int(v3.get("viewId") or 0)
    if view3d_id <= 0:
        raise RuntimeError("create_3d_view did not return viewId.")
    return view3d_id


def hide_non_mass_categories(port: int, view_id: int) -> None:
    cats_outer = send_request(port, "get_project_categories", {})
    cats = unwrap(cats_outer)
    categories = cats.get("categories") or []

    mass_cat_ids: List[int] = []
    hide_cat_ids: List[int] = []

    for c in categories:
        try:
            cid = int(c.get("categoryId"))
        except Exception:
            continue
        name = str(c.get("name") or "")
        ctype = c.get("categoryType")
        if name == "マス" and ctype == "Model":
            mass_cat_ids.append(cid)
        else:
            if ctype in ("Model", "Annotation", "Internal"):
                hide_cat_ids.append(cid)

    if hide_cat_ids:
        send_request(
            port,
            "set_category_visibility",
            {"viewId": view_id, "categoryIds": hide_cat_ids, "visible": False},
        )
    if mass_cat_ids:
        send_request(
            port,
            "set_category_visibility",
            {"viewId": view_id, "categoryIds": mass_cat_ids, "visible": True},
        )


def color_masses_by_live_load(
    port: int,
    view_id: int,
    room_to_mass: Dict[int, int],
) -> Dict[str, int]:
    groups: Dict[str, List[int]] = defaultdict(list)

    for room_id, mass_id in room_to_mass.items():
        rp_outer = send_request(port, "get_room_params", {"roomId": room_id})
        rp = unwrap(rp_outer)
        params = rp.get("parameters") or []

        load_val: Any = None
        for p in params:
            n = (p.get("name") or "").strip()
            if n == LIVE_LOAD_PARAM and load_val is None:
                load_val = p.get("display") or p.get("value") or p.get("raw")
        if load_val is None or (isinstance(load_val, str) and not load_val.strip()):
            key = UNSET_KEY
        else:
            key = str(load_val)
        groups[key].append(mass_id)

    # Index-style palette
    palette: List[Tuple[int, int, int]] = [
        (255, 0, 0),
        (255, 255, 0),
        (0, 255, 0),
        (0, 255, 255),
        (0, 0, 255),
        (255, 0, 255),
        (255, 128, 0),
        (128, 0, 255),
        (0, 128, 128),
        (128, 128, 0),
    ]

    keys_all = list(groups.keys())
    special = [k for k in keys_all if k in {UNSET_KEY, "-"}]
    normal = [k for k in keys_all if k not in special]

    try:
        normal_sorted = sorted(normal, key=lambda x: float(x))
    except Exception:
        normal_sorted = sorted(normal)

    color_map: Dict[str, Tuple[int, int, int]] = {}
    for idx, key in enumerate(normal_sorted):
        color_map[key] = palette[idx % len(palette)]

    if "-" in special:
        color_map["-"] = (210, 210, 210)
    if UNSET_KEY in special:
        color_map[UNSET_KEY] = (180, 180, 180)

    for key, mass_ids in groups.items():
        rgb = color_map.get(key, (255, 255, 255))
        send_request(
            port,
            "set_visual_override",
            {
                "viewId": view_id,
                "elementIds": list(mass_ids),
                "autoWorkingView": False,
                "detachViewTemplate": False,
                "r": int(rgb[0]),
                "g": int(rgb[1]),
                "b": int(rgb[2]),
                "transparency": 50,
                "refreshView": True,
                "__smoke_ok": True,
            },
        )

    return {k: len(v) for k, v in groups.items()}


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "Create a new 3D view without template, "
            "show only Mass elements, and color masses "
            "by live load (積載荷重) with 50% transparency."
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP port")
    ap.add_argument(
        "--max-rooms",
        type=int,
        default=200,
        help="Maximum number of Rooms to process when creating masses",
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
    ap.add_argument(
        "--view-name",
        type=str,
        default="RoomMass_LiveLoad_3D",
        help="Name for the new 3D view",
    )
    args = ap.parse_args(argv)

    port = args.port

    try:
        base_view_id, base_view_name = get_current_view(port)

        room_to_mass = create_room_masses_for_view(
            port,
            base_view_id,
            args.max_rooms,
            args.height_mode,
            args.fixed_height_mm,
        )
        if not room_to_mass:
            print(
                json.dumps(
                    {
                        "ok": False,
                        "code": "NO_MASSES",
                        "msg": "create_room_masses did not create any masses (or no valid roomId/massId pairs).",
                    },
                    ensure_ascii=False,
                    indent=2,
                )
            )
            return 0

        view3d_id = create_mass_only_3d_view(port, args.view_name)
        hide_non_mass_categories(port, view3d_id)

        groups_summary = color_masses_by_live_load(port, view3d_id, room_to_mass)

        result = {
            "ok": True,
            "baseViewId": base_view_id,
            "baseViewName": base_view_name,
            "view3dId": view3d_id,
            "view3dName": args.view_name,
            "createdCount": len(room_to_mass),
            "groupCount": len(groups_summary),
            "groups": groups_summary,
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

