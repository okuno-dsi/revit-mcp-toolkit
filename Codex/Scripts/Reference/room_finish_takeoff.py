import argparse
import json
import math
import os
import sys
from typing import Any, Dict, List, Tuple

from send_revit_command_durable import send_request, RevitMcpError


def mm_from_feet(feet: float) -> float:
    return float(feet) * 304.8


def _unwrap_result(payload: Dict[str, Any]) -> Dict[str, Any]:
    """
    send_request(...) は JSON-RPC エンベロープをそのまま返すので、
    Revit MCP コマンドの実結果 { ok, ... } まで 2 段階で掘り下げます。
    """
    inner = payload.get("result")
    if isinstance(inner, dict) and "result" in inner:
        inner = inner.get("result")
    if isinstance(inner, dict):
        return inner
    return {}


def _point_in_polygon(point: Tuple[float, float], poly: List[Tuple[float, float]]) -> bool:
    """
    2D の点が多角形内にあるかどうか（射影法）を簡易判定。
    poly: [(x1,y1), (x2,y2), ...] で閉じたループを想定。
    """
    x, y = point
    inside = False
    n = len(poly)
    if n < 3:
        return False
    j = n - 1
    for i in range(n):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y):
            t = (y - yi) / (yj - yi + 1e-9)
            x_int = xi + (xj - xi) * t
            if x < x_int:
                inside = not inside
        j = i
    return inside


def compute_room_finish_takeoff(port: int) -> Dict[str, Any]:
    """
    現在選択中の Room について、室内側の壁面・柱周りの概算仕上げ面積を計算する。

    - 壁仕上げ面積 ≒ 室外周長 × Room高さ
    - 柱周面積     ≒ 室内に立つ柱・コアの周長合計 × 有効高さ
    - 建具開口部の控除は行わない（別途差し引く前提）
    """
    # 1) 選択中 Room とアクティブビュー
    sel_env = send_request(port, "get_selected_element_ids", {})
    sel = _unwrap_result(sel_env)
    ids = sel.get("elementIds") or []
    if not ids:
        return {
            "ok": False,
            "code": "NO_SELECTION",
            "msg": "Revit側でRoomが選択されていません。対象の部屋を1つだけ選択してください。",
        }
    room_id = int(ids[0])
    active_view_id = int(sel.get("activeViewId") or 0)

    # 2) Room の高さ（バウンディングボックス）
    lu_env = send_request(
        port,
        "lookup_element",
        {"elementId": room_id, "includeGeometry": False, "includeRelations": False},
    )
    lu = _unwrap_result(lu_env)
    elem = lu.get("element") or {}

    bbox = elem.get("boundingBox") or {}
    bb_min = bbox.get("min") or {}
    bb_max = bbox.get("max") or {}
    try:
        zmin_ft = float(bb_min["z"])
        zmax_ft = float(bb_max["z"])
    except Exception:
        return {
            "ok": False,
            "code": "NO_BBOX",
            "msg": "Roomのバウンディングボックス高さが取得できませんでした。",
        }
    height_ft = max(0.0, zmax_ft - zmin_ft)
    height_mm = mm_from_feet(height_ft)

    # Room 名
    room_name = None
    for p in elem.get("parameters") or []:
        try:
            if p.get("builtin") == "ROOM_NAME":
                room_name = p.get("displayValue") or p.get("value") or p.get("name")
                break
        except Exception:
            continue

    # 3) Room 境界（mm）と外周ポリゴン
    rb_env = send_request(port, "get_room_boundary", {"elementId": room_id})
    rb = _unwrap_result(rb_env)
    loops = rb.get("loops") or []
    if not loops:
        return {
            "ok": False,
            "code": "NO_BOUNDARY",
            "msg": "get_room_boundary でRoom境界が取得できませんでした。",
        }

    loop_summaries: List[Dict[str, Any]] = []
    outer_loop_index = None
    max_perimeter = -1.0
    room_poly_xy: List[Tuple[float, float]] = []

    for loop in loops:
        segs = loop.get("segments") or []
        perimeter_mm = 0.0
        loop_pts: List[Tuple[float, float]] = []
        for seg in segs:
            start = seg.get("start") or {}
            end = seg.get("end") or {}
            sx = float(start.get("x", 0.0))
            sy = float(start.get("y", 0.0))
            ex = float(end.get("x", 0.0))
            ey = float(end.get("y", 0.0))
            dx = ex - sx
            dy = ey - sy
            perimeter_mm += math.hypot(dx, dy)
            loop_pts.append((sx, sy))
        if segs:
            last_end = segs[-1].get("end") or {}
            ex = float(last_end.get("x", 0.0))
            ey = float(last_end.get("y", 0.0))
            loop_pts.append((ex, ey))

        idx = loop.get("loopIndex")
        loop_summaries.append({"loopIndex": idx, "perimeterMm": perimeter_mm})
        if perimeter_mm > max_perimeter:
            max_perimeter = perimeter_mm
            outer_loop_index = idx
            room_poly_xy = loop_pts

    if outer_loop_index is None:
        wall_perimeter_mm = sum(ls["perimeterMm"] for ls in loop_summaries)
    else:
        wall_perimeter_mm = sum(
            ls["perimeterMm"]
            for ls in loop_summaries
            if ls["loopIndex"] == outer_loop_index
        )
    wall_area_m2 = wall_perimeter_mm * height_mm / 1_000_000.0

    # 4) Room ポリゴン + Z 範囲から、柱候補 FamilyInstance を抽出
    column_shell_area_m2 = 0.0
    column_ids_inside: List[int] = []

    if active_view_id and room_poly_xy:
        candidate_ids: List[int] = []
        for cat_key in ["柱", "column", "Column"]:
            gev_env = send_request(
                port,
                "get_elements_in_view",
                {
                    "viewId": active_view_id,
                    "categoryNameContains": cat_key,
                    "_shape": {"idsOnly": True, "page": {"limit": 2000}},
                },
            )
            gev = _unwrap_result(gev_env)
            for eid in gev.get("elementIds") or []:
                try:
                    eid_int = int(eid)
                except Exception:
                    continue
                if eid_int not in candidate_ids:
                    candidate_ids.append(eid_int)

        for eid_int in candidate_ids:
            lu_col_env = send_request(
                port,
                "lookup_element",
                {
                    "elementId": eid_int,
                    "includeGeometry": False,
                    "includeRelations": False,
                },
            )
            lu_col = _unwrap_result(lu_col_env)
            e_col = lu_col.get("element") or {}

            bbox_c = e_col.get("boundingBox") or {}
            bb_min_c = bbox_c.get("min") or {}
            bb_max_c = bbox_c.get("max") or {}
            loc = (e_col.get("location") or {}).get("point") or {}

            try:
                cx_ft = float(loc.get("x"))
                cy_ft = float(loc.get("y"))
            except Exception:
                continue

            # XY: Room ポリゴンとの位置関係（柱芯 or 柱矩形のいずれかが室内なら採用）
            cx_mm = mm_from_feet(cx_ft)
            cy_mm = mm_from_feet(cy_ft)
            inside_xy = _point_in_polygon((cx_mm, cy_mm), room_poly_xy)
            if not inside_xy:
                try:
                    x_min_mm = mm_from_feet(float(bb_min_c.get("x")))
                    x_max_mm = mm_from_feet(float(bb_max_c.get("x")))
                    y_min_mm = mm_from_feet(float(bb_min_c.get("y")))
                    y_max_mm = mm_from_feet(float(bb_max_c.get("y")))
                    corners = [
                        (x_min_mm, y_min_mm),
                        (x_min_mm, y_max_mm),
                        (x_max_mm, y_min_mm),
                        (x_max_mm, y_max_mm),
                    ]
                    inside_xy = any(_point_in_polygon(pt, room_poly_xy) for pt in corners)
                except Exception:
                    inside_xy = False
            if not inside_xy:
                continue

            # Z: Room の高さ範囲と重なっているか（ft のまま比較）
            try:
                c_zmin_ft = float(bb_min_c.get("z"))
                c_zmax_ft = float(bb_max_c.get("z"))
            except Exception:
                continue
            if c_zmax_ft <= zmin_ft or c_zmin_ft >= zmax_ft:
                continue

            # 柱寸法（mm）: bbox の X/Y 差分
            try:
                c_xmin_ft = float(bb_min_c.get("x"))
                c_xmax_ft = float(bb_max_c.get("x"))
                c_ymin_ft = float(bb_min_c.get("y"))
                c_ymax_ft = float(bb_max_c.get("y"))
            except Exception:
                continue

            width_mm = mm_from_feet(max(0.0, c_xmax_ft - c_xmin_ft))
            depth_mm = mm_from_feet(max(0.0, c_ymax_ft - c_ymin_ft))
            if width_mm <= 0.0 or depth_mm <= 0.0:
                continue

            # 有効高さ（mm）: Room 高さとの重なり区間
            overlap_z_ft = max(0.0, min(c_zmax_ft, zmax_ft) - max(c_zmin_ft, zmin_ft))
            height_eff_mm = mm_from_feet(overlap_z_ft)
            if height_eff_mm <= 0.0:
                continue

            # 背の高い柱らしい形状かどうか（幅・奥行きは2m以下、高さは最大辺の2倍以上）
            max_side = max(width_mm, depth_mm)
            if max_side <= 0.0 or max_side > 2000.0:
                continue
            if height_eff_mm < max_side * 2.0:
                continue

            perimeter_mm_col = 2.0 * (width_mm + depth_mm)
            shell_area_m2 = perimeter_mm_col * height_eff_mm / 1_000_000.0
            column_shell_area_m2 += shell_area_m2
            column_ids_inside.append(eid_int)

    total_area_m2 = wall_area_m2 + column_shell_area_m2

    return {
        "ok": True,
        "roomId": room_id,
        "roomName": room_name,
        "heightMm": height_mm,
        "wallPerimeterMm": wall_perimeter_mm,
        "wallFinishAreaM2": wall_area_m2,
        "columnSurfaceAreaM2": column_shell_area_m2,
        "totalFinishAreaM2": total_area_m2,
        "loops": loop_summaries,
        "columnElementIdsInsideRoom": column_ids_inside,
        "assumptions": {
            "heightBasis": "Roomのバウンディングボックス高さ (min.z〜max.z) を採用",
            "wallAreaFormula": "外周長 × Room高さ",
            "columnDetection": "RoomポリゴンXY内かつZ範囲が重なり、カテゴリ名に「柱/Column」を含む要素を室内柱とみなす",
            "columnHeight": "柱の有効高さ＝Room高さとの重なり区間で計算",
            "columnShapeFilter": "幅・奥行きとも2m以下かつ高さが最大辺の2倍以上の直方体とみなす",
            "openingsSubtracted": False,
            "openingsNote": "面積には建具開口部を差し引いていません（必要に応じて別途控除してください）。",
            "units": {"length": "mm", "area": "m2"},
        },
    }


def main() -> None:
    parser = argparse.ArgumentParser(
        description="選択中のRoomについて、室内側の壁面・柱周りの概算仕上げ面積を計算します。"
    )
    parser.add_argument("--port", type=int, default=5210, help="Revit MCP ポート番号")
    parser.add_argument(
        "--output-file",
        type=str,
        help="結果JSONを書き出すファイルパス（省略時は標準出力にJSONを出力）",
    )
    args = parser.parse_args()

    try:
        result = compute_room_finish_takeoff(args.port)
    except RevitMcpError as e:
        result = {
            "ok": False,
            "code": "MCP_ERROR",
            "msg": str(e),
            "where": e.where,
            "payload": e.payload,
        }
    except Exception as e:  # defensive
        result = {
            "ok": False,
            "code": "UNEXPECTED_ERROR",
            "msg": repr(e),
        }

    text = json.dumps(result, ensure_ascii=False, indent=2)
    if args.output_file:
        outp = os.path.abspath(args.output_file)
        os.makedirs(os.path.dirname(outp), exist_ok=True)
        with open(outp, "w", encoding="utf-8") as f:
            f.write(text)
        print(json.dumps({"ok": True, "savedTo": outp}, ensure_ascii=False))
    else:
        print(text)


if __name__ == "__main__":
    main()
