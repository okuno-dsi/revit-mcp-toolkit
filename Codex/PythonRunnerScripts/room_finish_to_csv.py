# @feature: room finish to csv | keywords: 柱, 壁, 部屋, エリア, 集計表
import argparse
import csv
import json
import os
from typing import Any, Dict, List

from send_revit_command_durable import send_request, RevitMcpError
from room_finish_takeoff import (
    compute_room_finish_takeoff,
    mm_from_feet,
    _unwrap_result,
)


def fetch_column_detail(port: int, room_zmin_ft: float, room_zmax_ft: float, eid_int: int) -> Dict[str, Any]:
    """
    指定した柱要素（elementId）について、幅・奥行き・高さ・座標などの詳細を取得し、
    面積計算に使ったのと同じロジックで周面積を算出する。
    """
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

    # 基本情報
    type_name = e_col.get("typeName")
    cat_name = e_col.get("category")

    # 位置
    cx_ft = float(loc.get("x", 0.0) or 0.0)
    cy_ft = float(loc.get("y", 0.0) or 0.0)
    cz_ft = float(loc.get("z", 0.0) or 0.0)

    cx_mm = mm_from_feet(cx_ft)
    cy_mm = mm_from_feet(cy_ft)
    cz_mm = mm_from_feet(cz_ft)

    # bbox mm
    x_min_ft = float(bb_min_c.get("x", 0.0) or 0.0)
    x_max_ft = float(bb_max_c.get("x", 0.0) or 0.0)
    y_min_ft = float(bb_min_c.get("y", 0.0) or 0.0)
    y_max_ft = float(bb_max_c.get("y", 0.0) or 0.0)
    z_min_ft = float(bb_min_c.get("z", 0.0) or 0.0)
    z_max_ft = float(bb_max_c.get("z", 0.0) or 0.0)

    x_min_mm = mm_from_feet(x_min_ft)
    x_max_mm = mm_from_feet(x_max_ft)
    y_min_mm = mm_from_feet(y_min_ft)
    y_max_mm = mm_from_feet(y_max_ft)
    z_min_mm = mm_from_feet(z_min_ft)
    z_max_mm = mm_from_feet(z_max_ft)

    width_mm = max(0.0, x_max_mm - x_min_mm)
    depth_mm = max(0.0, y_max_mm - y_min_mm)

    # Room との重なり高さ
    overlap_z_ft = max(0.0, min(z_max_ft, room_zmax_ft) - max(z_min_ft, room_zmin_ft))
    height_eff_mm = mm_from_feet(overlap_z_ft)

    perimeter_mm = 2.0 * (width_mm + depth_mm) if width_mm > 0 and depth_mm > 0 else 0.0
    shell_area_m2 = perimeter_mm * height_eff_mm / 1_000_000.0 if height_eff_mm > 0 else 0.0

    return {
        "kind": "Column",
        "elementId": eid_int,
        "category": cat_name,
        "typeName": type_name,
        "widthMm": width_mm,
        "depthMm": depth_mm,
        "heightEffMm": height_eff_mm,
        "perimeterMm": perimeter_mm,
        "shellAreaM2": shell_area_m2,
        "centerXmm": cx_mm,
        "centerYmm": cy_mm,
        "centerZmm": cz_mm,
        "bboxXminMm": x_min_mm,
        "bboxYminMm": y_min_mm,
        "bboxZminMm": z_min_mm,
        "bboxXmaxMm": x_max_mm,
        "bboxYmaxMm": y_max_mm,
        "bboxZmaxMm": z_max_mm,
    }


def main() -> None:
    parser = argparse.ArgumentParser(
        description="room_finish_takeoff の結果をもとに、壁と室内柱の詳細をCSVに書き出します。"
    )
    parser.add_argument("--port", type=int, default=5210, help="Revit MCP ポート番号")
    parser.add_argument(
        "--output-csv",
        type=str,
        required=True,
        help="書き出し先の CSV ファイルパス（例: Projects/Temp_5210/room_finish.csv）",
    )
    args = parser.parse_args()

    try:
        # まず既存ロジックで集計値＋柱ID一覧を取得
        summary = compute_room_finish_takeoff(args.port)
    except RevitMcpError as e:
        print(json.dumps(
            {"ok": False, "code": "MCP_ERROR", "msg": str(e), "where": e.where},
            ensure_ascii=False,
            indent=2,
        ))
        return

    if not summary.get("ok"):
        print(json.dumps(summary, ensure_ascii=False, indent=2))
        return

    room_id = summary.get("roomId")
    room_name = summary.get("roomName")
    height_mm = summary.get("heightMm")
    wall_perimeter_mm = summary.get("wallPerimeterMm")
    wall_area_m2 = summary.get("wallFinishAreaM2")
    column_ids: List[int] = summary.get("columnElementIdsInsideRoom") or []

    # Room の bbox 再取得（ft）: Room のZ範囲が必要
    lu_env = send_request(
        args.port,
        "lookup_element",
        {"elementId": room_id, "includeGeometry": False, "includeRelations": False},
    )
    lu = _unwrap_result(lu_env)
    elem = lu.get("element") or {}
    bbox = elem.get("boundingBox") or {}
    bb_min = bbox.get("min") or {}
    bb_max = bbox.get("max") or {}
    room_zmin_ft = float(bb_min.get("z", 0.0) or 0.0)
    room_zmax_ft = float(bb_max.get("z", 0.0) or 0.0)

    # 各柱の詳細を収集
    column_rows: List[Dict[str, Any]] = []
    for cid in column_ids:
        try:
            detail = fetch_column_detail(args.port, room_zmin_ft, room_zmax_ft, int(cid))
            column_rows.append(detail)
        except Exception:
            continue

    # CSV 出力
    outp = os.path.abspath(args.output_csv)
    os.makedirs(os.path.dirname(outp), exist_ok=True)

    fieldnames = [
        "kind",
        "elementId",
        "category",
        "typeName",
        "widthMm",
        "depthMm",
        "heightEffMm",
        "perimeterMm",
        "shellAreaM2",
        "centerXmm",
        "centerYmm",
        "centerZmm",
        "bboxXminMm",
        "bboxYminMm",
        "bboxZminMm",
        "bboxXmaxMm",
        "bboxYmaxMm",
        "bboxZmaxMm",
    ]

    with open(outp, "w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()

        # 壁の概要行
        writer.writerow(
            {
                "kind": "WallSummary",
                "elementId": "",
                "category": "Walls",
                "typeName": "",
                "widthMm": "",
                "depthMm": "",
                "heightEffMm": height_mm,
                "perimeterMm": wall_perimeter_mm,
                "shellAreaM2": wall_area_m2,
                "centerXmm": "",
                "centerYmm": "",
                "centerZmm": "",
                "bboxXminMm": "",
                "bboxYminMm": "",
                "bboxZminMm": "",
                "bboxXmaxMm": "",
                "bboxYmaxMm": "",
                "bboxZmaxMm": "",
            }
        )

        # 各柱
        for row in column_rows:
            writer.writerow(row)

    print(
        json.dumps(
            {
                "ok": True,
                "roomId": room_id,
                "roomName": room_name,
                "csv": outp,
                "columns": len(column_rows),
            },
            ensure_ascii=False,
            indent=2,
        )
    )


if __name__ == "__main__":
    main()


