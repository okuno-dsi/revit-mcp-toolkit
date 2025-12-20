import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Tuple


def _add_scripts_to_path() -> None:
    here = Path(__file__).resolve().parent
    if str(here) not in sys.path:
        sys.path.insert(0, str(here))


_add_scripts_to_path()

from send_revit_command_durable import RevitMcpError, send_request  # type: ignore  # noqa: E402


CAT_DOORS = -2000023  # OST_Doors
CAT_WINDOWS = -2000014  # OST_Windows
CAT_LEVELS = -2006020  # OST_Levels


def unwrap(payload: Dict[str, Any]) -> Dict[str, Any]:
    obj: Any = payload
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def chunked(items: List[int], size: int) -> Iterable[List[int]]:
    for i in range(0, len(items), size):
        yield items[i : i + size]


def get_current_view(port: int) -> Dict[str, Any]:
    res = send_request(port, "get_current_view", {})
    top = unwrap(res)
    if not top.get("ok"):
        raise RevitMcpError("get_current_view", top.get("msg", "get_current_view failed"))
    return top


def get_element_ids_in_view(port: int, view_id: int, *, category_id: int) -> List[int]:
    payload = {
        "viewId": int(view_id),
        "_shape": {"idsOnly": True, "page": {"limit": 200000}},
        "_filter": {"includeCategoryIds": [int(category_id)], "modelOnly": True, "excludeImports": True},
    }
    res = send_request(port, "get_elements_in_view", payload)
    top = unwrap(res)
    if not top.get("ok"):
        raise RevitMcpError("get_elements_in_view", top.get("msg", "get_elements_in_view failed"))
    return [int(x) for x in (top.get("elementIds") or [])]


def get_element_infos(port: int, element_ids: List[int], *, batch_size: int = 50) -> List[Dict[str, Any]]:
    infos: List[Dict[str, Any]] = []
    for batch in chunked(element_ids, batch_size):
        res = send_request(port, "get_element_info", {"elementIds": batch, "rich": True})
        top = unwrap(res)
        if not top.get("ok"):
            raise RevitMcpError("get_element_info", top.get("msg", "get_element_info failed"))
        infos.extend(list(top.get("elements") or []))
    return infos


def rename_view_unique(port: int, view_id: int, desired_name: str) -> str:
    desired = (desired_name or "").strip()
    if not desired:
        return desired_name

    for suffix in ["", " (2)", " (3)", " (4)", " (5)", " (6)", " (7)", " (8)", " (9)", " (10)"]:
        name_try = f"{desired}{suffix}"
        res = send_request(port, "rename_view", {"viewId": int(view_id), "name": name_try})
        top = unwrap(res)
        if top.get("ok"):
            return name_try
        msg = str(top.get("msg") or "").lower()
        if "already" in msg or "exists" in msg or "同名" in msg or "重複" in msg:
            continue
        return name_try
    return desired


def create_elev_views_for_elements(port: int, element_ids: List[int]) -> List[Dict[str, Any]]:
    if not element_ids:
        return []
    payload = {
        "elementIds": [int(x) for x in element_ids],
        "mode": "ElevationMarker",
        "orientation": {"mode": "Front"},
        "isolateTargets": False,
        "viewScale": 50,
        "cropMargin_mm": 200,
        "offsetDistance_mm": 1500,
    }
    res = send_request(port, "create_element_elevation_views", payload)
    top = unwrap(res)
    if not top.get("ok"):
        raise RevitMcpError("create_element_elevation_views", top.get("msg", "create_element_elevation_views failed"))
    return list(top.get("views") or [])


def keep_only_categories(port: int, view_id: int, keep_ids: List[int]) -> Dict[str, Any]:
    res = send_request(
        port,
        "set_category_visibility_bulk",
        {
            "viewId": int(view_id),
            "mode": "keep_only",
            "categoryType": "All",
            "keepCategoryIds": [int(x) for x in keep_ids],
            "detachViewTemplate": True,
        },
    )
    return unwrap(res)


def hide_elements(port: int, view_id: int, element_ids: List[int]) -> Dict[str, Any]:
    if not element_ids:
        return {"ok": True, "viewId": int(view_id), "hidden": 0}
    res = send_request(
        port,
        "hide_elements_in_view",
        {
            "viewId": int(view_id),
            "elementIds": [int(x) for x in element_ids],
            "detachViewTemplate": True,
            "refreshView": True,
            "batchSize": 800,
            "maxMillisPerTx": 4000,
        },
    )
    return unwrap(res)


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "アクティブビューに表示されているドア/窓をタイプ別に分類し、各タイプの代表要素から立面(要素ビュー)を作成します。"
        )
    )
    ap.add_argument("--port", type=int, default=5210)
    ap.add_argument("--view-id", type=int, default=0, help="対象ビューID（省略時はアクティブビュー）")
    ap.add_argument("--output", type=str, default="", help="結果JSONの保存先（任意）")
    args = ap.parse_args(argv)

    port = int(args.port)

    try:
        cv = get_current_view(port)
        source_view_id = int(args.view_id or 0) or int(cv.get("viewId") or 0)
        source_view_name = str(cv.get("name") or "")
        source_view_type = str(cv.get("viewType") or "")

        door_ids = get_element_ids_in_view(port, source_view_id, category_id=CAT_DOORS)
        window_ids = get_element_ids_in_view(port, source_view_id, category_id=CAT_WINDOWS)

        door_infos = get_element_infos(port, door_ids) if door_ids else []
        window_infos = get_element_infos(port, window_ids) if window_ids else []

        def group_by_type(infos: List[Dict[str, Any]]) -> Dict[int, Dict[str, Any]]:
            groups: Dict[int, Dict[str, Any]] = {}
            for info in infos:
                try:
                    type_id = int(info.get("typeId") or 0)
                except Exception:
                    type_id = 0
                if type_id <= 0:
                    continue
                eid = int(info.get("elementId") or 0)
                if eid <= 0:
                    continue
                g = groups.get(type_id)
                if not g:
                    g = {
                        "typeId": type_id,
                        "familyName": str(info.get("familyName") or ""),
                        "typeName": str(info.get("typeName") or ""),
                        "elementIds": [],
                    }
                    groups[type_id] = g
                g["elementIds"].append(eid)
            return groups

        door_groups = group_by_type(door_infos)
        window_groups = group_by_type(window_infos)

        reps: List[int] = []
        rep_meta: Dict[int, Dict[str, Any]] = {}
        for g in door_groups.values():
            rep = int(g["elementIds"][0])
            reps.append(rep)
            rep_meta[rep] = {"kind": "door", **g}
        for g in window_groups.values():
            rep = int(g["elementIds"][0])
            reps.append(rep)
            rep_meta[rep] = {"kind": "window", **g}

        created = create_elev_views_for_elements(port, reps)

        created_views: List[Dict[str, Any]] = []
        for item in created:
            rep_eid = int(item.get("elementId") or 0)
            view_id = int(item.get("viewId") or 0)
            if rep_eid <= 0 or view_id <= 0:
                continue
            meta = rep_meta.get(rep_eid) or {}
            kind = str(meta.get("kind") or "")
            fam = str(meta.get("familyName") or "")
            typ = str(meta.get("typeName") or "")

            if kind == "door":
                view_name = f"ElemElev_DoorType {fam} {typ}".strip()
                keep_ids = [CAT_DOORS, CAT_LEVELS]
                all_ids = door_ids
                this_type_ids = [int(x) for x in (meta.get("elementIds") or [])]
            else:
                view_name = f"ElemElev_WindowType {fam} {typ}".strip()
                keep_ids = [CAT_WINDOWS, CAT_LEVELS]
                all_ids = window_ids
                this_type_ids = [int(x) for x in (meta.get("elementIds") or [])]

            final_name = rename_view_unique(port, view_id, view_name)
            cat_res = keep_only_categories(port, view_id, keep_ids)

            # Hide same-category elements that are not this type (within the source view set).
            hide_ids = [int(x) for x in all_ids if int(x) not in set(this_type_ids)]
            hide_res = hide_elements(port, view_id, hide_ids)

            created_views.append(
                {
                    "repElementId": rep_eid,
                    "viewId": view_id,
                    "name": final_name,
                    "kind": kind,
                    "typeId": int(meta.get("typeId") or 0),
                    "familyName": fam,
                    "typeName": typ,
                    "categoryVisibility": cat_res,
                    "hiddenOtherSameCategoryCount": len(hide_ids),
                    "hideResult": hide_res,
                }
            )

        out = {
            "ok": True,
            "sourceView": {
                "viewId": source_view_id,
                "name": source_view_name,
                "viewType": source_view_type,
            },
            "counts": {
                "doorsInView": len(door_ids),
                "windowsInView": len(window_ids),
                "doorTypes": len(door_groups),
                "windowTypes": len(window_groups),
                "createdViews": len(created_views),
            },
            "views": created_views,
        }

        txt = json.dumps(out, ensure_ascii=False, indent=2)
        if args.output:
            Path(args.output).write_text(txt, encoding="utf-8")
        print(txt)
        return 0

    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2
    except Exception as ex:
        print(json.dumps({"ok": False, "code": "UNEXPECTED", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 3


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

