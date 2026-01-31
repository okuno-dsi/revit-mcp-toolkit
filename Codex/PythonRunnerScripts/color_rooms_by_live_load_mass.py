# @feature: color rooms by live load mass | keywords: 部屋, ビュー
import argparse
import json
import sys
import os
from collections import defaultdict
from typing import Any, Dict, List, Tuple


def _add_scripts_to_path() -> None:
    here = os.path.dirname(os.path.abspath(__file__))
    if here not in sys.path:
        sys.path.insert(0, here)


_add_scripts_to_path()

from send_revit_command_durable import send_request, RevitMcpError  # type: ignore  # noqa: E402


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


def duplicate_view_with_suffix(port: int, base_view_id: int, suffix: str) -> Tuple[int, str]:
    """Duplicate base view and clear template; return (newViewId, newViewName)."""
    info = unwrap(send_request(port, "get_view_info", {"viewId": base_view_id}))
    view = info.get("view") or info
    base_name = str(view.get("name") or f"View_{base_view_id}")

    dup = unwrap(
        send_request(port, "duplicate_view", {"viewId": base_view_id, "__smoke_ok": True})
    )
    new_vid = int(dup.get("viewId") or dup.get("newViewId") or 0)
    if new_vid <= 0:
        raise RuntimeError("duplicate_view から viewId が取得できませんでした。")

    new_name = f"{base_name} {suffix}".strip()
    try:
        send_request(port, "rename_view", {"viewId": new_vid, "newName": new_name})
    except RevitMcpError:
        # 名前重複などは無視して続行
        pass

    # Activate and clear template
    try:
        send_request(port, "activate_view", {"viewId": new_vid})
    except RevitMcpError:
        pass

    try:
        send_request(port, "set_view_template", {"viewId": new_vid, "clear": True})
    except RevitMcpError:
        pass

    return new_vid, new_name


def get_current_view(port: int) -> Tuple[int, str]:
    cur = unwrap(send_request(port, "get_current_view", {}))
    vid = int(cur.get("viewId") or 0)
    name = str(cur.get("name") or "")
    if vid <= 0:
        raise RuntimeError("現在のビューIDが取得できませんでした。")
    return vid, name


def collect_rooms_in_view(port: int, view_id: int) -> List[int]:
    outer = send_request(
        port,
        "get_elements_in_view",
        {
            "viewId": view_id,
            "_shape": {"idsOnly": True, "page": {"limit": 200000}},
            "_filter": {"includeClasses": ["Room"]},
        },
    )
    res = unwrap(outer)
    ids = res.get("elementIds") or []
    return [int(x) for x in ids]


def extract_room_label_and_load(
    params_list: List[Dict[str, Any]], room_id: int
) -> Tuple[str, str]:
    """Return (roomLabel, liveLoadKey) from get_room_params parameters."""
    name_val = None
    number_val = None
    load_val = None

    for p in params_list:
        n = (p.get("name") or "").strip()
        if n == "名前" and name_val is None:
            name_val = p.get("display") or p.get("value") or p.get("raw")
        elif n == "番号" and number_val is None:
            number_val = p.get("display") or p.get("value") or p.get("raw")
        elif n == "積載荷重" and load_val is None:
            load_val = p.get("display") or p.get("value") or p.get("raw")

    parts: List[str] = []
    if name_val:
        parts.append(str(name_val))
    if number_val:
        parts.append(str(number_val))
    room_name = " ".join(parts) if parts else f"Room_{room_id}"

    if load_val is None or (isinstance(load_val, str) and not load_val.strip()):
        load_key = "(未設定)"
    else:
        load_key = str(load_val)

    return room_name, load_key


def choose_outer_loop(loops: List[Dict[str, Any]]) -> Dict[str, Any]:
    """Choose outer boundary loop (loopIndex==0, fallback largest perimeter)."""
    if not loops:
        return {}
    for lp in loops:
        if lp.get("loopIndex") == 0:
            return lp
    best = None
    best_perim = -1.0
    for lp in loops:
        segs = lp.get("segments") or []
        perim = 0.0
        for seg in segs:
            s = seg.get("start") or {}
            e = seg.get("end") or {}
            dx = float(e.get("x", 0.0)) - float(s.get("x", 0.0))
            dy = float(e.get("y", 0.0)) - float(s.get("y", 0.0))
            perim += (dx * dx + dy * dy) ** 0.5
        if perim > best_perim:
            best_perim = perim
            best = lp
    return best or {}


def create_mass_for_room(port: int, room_id: int, default_height_mm: float = 2800.0) -> int:
    """Create a DirectShape mass from room footprint and move it to room's base Z."""
    rb_outer = send_request(port, "get_room_boundary", {"elementId": room_id})
    rb = unwrap(rb_outer)
    loops = rb.get("loops") or []
    outer = choose_outer_loop(loops)
    if not outer:
        return 0

    segs = outer.get("segments") or []
    if not segs:
        return 0

    loop_xy: List[Dict[str, float]] = []
    z0 = None
    for seg in segs:
        s = seg.get("start") or {}
        x = float(s.get("x", 0.0))
        y = float(s.get("y", 0.0))
        z = float(s.get("z", 0.0))
        if z0 is None:
            z0 = z
        loop_xy.append({"x": x, "y": y})
    last_end = segs[-1].get("end") or {}
    x_end = float(last_end.get("x", 0.0))
    y_end = float(last_end.get("y", 0.0))
    loop_xy.append({"x": x_end, "y": y_end})

    if z0 is None:
        z0 = 0.0

    # For now use fixed height; can be refined via get_room_params if needed.
    height_mm = float(default_height_mm)

    cdsm_params = {
        "loops": [loop_xy],
        "height": height_mm,
        "useMassCategory": True,
    }
    cdsm_outer = send_request(port, "create_direct_shape_mass", cdsm_params)
    cdsm = unwrap(cdsm_outer)
    mass_id = int(cdsm.get("elementId") or 0)
    if mass_id <= 0:
        return 0

    dz = float(z0)
    if abs(dz) > 1e-3:
        try:
            send_request(
                port,
                "move_mass_instance",
                {"elementId": mass_id, "dx": 0.0, "dy": 0.0, "dz": dz},
            )
        except RevitMcpError:
            # 権限や一時的なエラーは無視して続行
            pass

    return mass_id


def apply_override(
    port: int, view_id: int, element_ids: List[int], rgb: Tuple[int, int, int], transparency: int
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
    }
    send_request(port, "set_visual_override", params)


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "アクティブビューを複製し、テンプレートを外した上で、"
            "各部屋の外形線と高さに合わせたインプレイスマスを作成し、"
            "積載荷重ごとに透過度50%で色分けします。"
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP ポート番号")
    ap.add_argument(
        "--suffix", type=str, default="LiveLoadMass", help="複製ビュー名に付与するサフィックス"
    )
    ap.add_argument(
        "--max-rooms",
        type=int,
        default=0,
        help="処理する最大Room数（0以下で全件）。テスト時に制限するためのオプション。",
    )
    args = ap.parse_args(argv)

    port = args.port

    try:
        base_view_id, base_view_name = get_current_view(port)

        new_view_id, new_view_name = duplicate_view_with_suffix(
            port, base_view_id, args.suffix
        )

        room_ids = collect_rooms_in_view(port, new_view_id)
        if not room_ids:
            result = {
                "ok": False,
                "code": "NO_ROOMS_IN_VIEW",
                "msg": "複製ビュー内に Room 要素がありません。",
                "baseViewId": base_view_id,
                "baseViewName": base_view_name,
                "newViewId": new_view_id,
                "newViewName": new_view_name,
            }
            print(json.dumps(result, ensure_ascii=False, indent=2))
            return 0

        if args.max_rooms > 0:
            room_ids = room_ids[: args.max_rooms]

        groups_masses: Dict[str, List[int]] = defaultdict(list)

        for rid in room_ids:
            rp_outer = send_request(port, "get_room_params", {"roomId": rid})
            rp = unwrap(rp_outer)
            params_list = rp.get("parameters") or []

            _, load_key = extract_room_label_and_load(params_list, rid)
            mass_id = create_mass_for_room(port, rid)
            if mass_id > 0:
                groups_masses[load_key].append(mass_id)

        keys_all = list(groups_masses.keys())
        special_keys = [k for k in keys_all if k in {"(未設定)", "-"}]
        normal_keys = [k for k in keys_all if k not in special_keys]

        try:
            normal_keys_sorted = sorted(normal_keys, key=lambda x: float(x))
        except Exception:
            normal_keys_sorted = sorted(normal_keys)

        palette: List[Tuple[int, int, int]] = [
            (255, 230, 230),
            (255, 240, 200),
            (255, 255, 200),
            (230, 255, 230),
            (200, 230, 255),
            (230, 200, 255),
            (230, 230, 255),
        ]

        color_map: Dict[str, Tuple[int, int, int]] = {}
        for idx, key in enumerate(normal_keys_sorted):
            color_map[key] = palette[idx % len(palette)]

        if "-" in special_keys:
            color_map["-"] = (210, 210, 210)
        if "(未設定)" in special_keys:
            color_map["(未設定)"] = (180, 180, 180)

        for key, masses in groups_masses.items():
            rgb = color_map.get(key, (255, 255, 255))
            apply_override(port, new_view_id, masses, rgb, transparency=50)

        result = {
            "ok": True,
            "baseViewId": base_view_id,
            "baseViewName": base_view_name,
            "newViewId": new_view_id,
            "newViewName": new_view_name,
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
    except Exception as e:  # defensive
        err = {
            "ok": False,
            "code": "UNEXPECTED_ERROR",
            "message": repr(e),
        }
        print(json.dumps(err, ensure_ascii=False, indent=2))
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

