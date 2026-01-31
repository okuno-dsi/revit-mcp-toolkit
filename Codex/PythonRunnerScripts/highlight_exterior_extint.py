# @feature: highlight exterior extint | keywords: 柱, 壁, ビュー, 床
import argparse
import json
import subprocess
import sys
from typing import Any, Dict


def call_revit(port: int, command: str, params: Dict[str, Any]) -> Dict[str, Any]:
    """send_revit_command_durable.py をサブプロセス経由で呼び出し、result.result を返す。"""
    args = [
        sys.executable,
        "send_revit_command_durable.py",
        "--port",
        str(port),
        "--command",
        command,
        "--params",
        json.dumps(params, ensure_ascii=False),
    ]
    out = subprocess.check_output(args, text=True)
    data = json.loads(out)
    # send_revit_command_durable の正常応答は data["result"]["result"] に Revit 側の result オブジェクトが入る
    return data["result"]["result"]


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Create a 3D view and color exterior elements orange and interior elements light blue (geometry-based)."
    )
    parser.add_argument("--port", type=int, default=5210, help="Revit MCP port (default: 5210)")
    parser.add_argument(
        "--view-name",
        type=str,
        default="MCP_ExteriorExtInt_3D",
        help="Name of the 3D view to create",
    )
    args = parser.parse_args()

    port = args.port

    # 1) 3Dビュー作成
    view_res = call_revit(port, "create_3d_view", {"name": args.view_name})
    view_id = view_res.get("viewId")
    if not isinstance(view_id, int):
        print("Failed to create 3D view.", file=sys.stderr)
        sys.exit(1)

    # 2) 壁・床・構造柱・建築柱だけ表示
    keep_cat_ids = [
        -2000011,  # OST_Walls
        -2000032,  # OST_Floors
        -2001330,  # OST_StructuralColumns
        -2000010,  # OST_Columns
    ]
    call_revit(
        port,
        "set_category_visibility_bulk",
        {
            "viewId": view_id,
            "mode": "keep_only",
            "categoryType": "Model",
            "keepCategoryIds": keep_cat_ids,
            "detachViewTemplate": True,
        },
    )

    # 3) 壁・構造柱・床の全要素IDを取得
    walls_res = call_revit(port, "get_walls", {"skip": 0, "count": 100000})
    wall_ids = [int(w["elementId"]) for w in walls_res.get("walls", [])]

    cols_res = call_revit(
        port,
        "get_structural_columns",
        {"skip": 0, "count": 100000, "namesOnly": False, "withParameters": False},
    )
    col_ids = [int(c["elementId"]) for c in cols_res.get("structuralColumns", [])]

    floors_res = call_revit(port, "get_floors", {"skip": 0, "count": 100000})
    floor_ids = [int(f["elementId"]) for f in floors_res.get("floors", [])]

    all_ids = sorted(set(wall_ids + col_ids + floor_ids))

    # 4) 外周候補の要素IDを取得（純粋に幾何ロジックに基づく）
    ext_walls_res = call_revit(port, "get_candidate_exterior_walls", {})
    ext_wall_ids = [int(w["elementId"]) for w in ext_walls_res.get("walls", [])]

    ext_cols_res = call_revit(port, "get_candidate_exterior_columns", {})
    ext_col_ids = [int(c["elementId"]) for c in ext_cols_res.get("columns", [])]

    ext_floors_res = call_revit(port, "get_candidate_exterior_floors", {})
    ext_floor_ids = [int(f["elementId"]) for f in ext_floors_res.get("floors", [])]

    ext_set = set(ext_wall_ids) | set(ext_col_ids) | set(ext_floor_ids)
    ext_ids = [eid for eid in all_ids if eid in ext_set]
    int_ids = [eid for eid in all_ids if eid not in ext_set]

    print(f"viewId={view_id} exterior={len(ext_ids)} interior={len(int_ids)}")

    # 5) 内部側（青） → 外周側（オレンジ）の順に要素単位 override
    if int_ids:
        call_revit(
            port,
            "set_visual_override",
            {
                "viewId": view_id,
                "elementIds": int_ids,
                "r": 135,
                "g": 206,
                "b": 250,
                "transparency": 50,
                "autoWorkingView": False,
                "refreshView": False,
                "detachViewTemplate": False,
            },
        )

    if ext_ids:
        call_revit(
            port,
            "set_visual_override",
            {
                "viewId": view_id,
                "elementIds": ext_ids,
                "r": 255,
                "g": 165,
                "b": 0,
                "transparency": 50,
                "autoWorkingView": False,
                "refreshView": True,
                "detachViewTemplate": False,
            },
        )

    # 6) ビューをアクティブ化
    call_revit(port, "activate_view", {"viewId": view_id})


if __name__ == "__main__":
    main()
