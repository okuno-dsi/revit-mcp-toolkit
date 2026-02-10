import json
import subprocess
import sys
from typing import Any, Dict, List


def call_revit(command: str, params: Dict[str, Any], port: int = 5210) -> Dict[str, Any]:
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
    return data["result"]["result"]


def main() -> None:
    port = 5210

    # 現在のビューを対象とする
    cur = call_revit("get_current_view", {}, port=port)
    view_id = int(cur.get("viewId") or 0)
    view_name = cur.get("name")
    print(f"current view: id={view_id} name={view_name}")

    if view_id <= 0:
        print("アクティブビューが取得できません。", file=sys.stderr)
        return

    # すべての構造柱情報を取得
    cols_res = call_revit("get_structural_columns", {"skip": 0, "count": 100000}, port=port)
    cols = cols_res.get("structuralColumns", [])

    # 1F 平面図では 2FL が該当しているので、2FL の柱だけを対象にする
    cols_2fl = [c for c in cols if c.get("levelName") == "2FL"]
    ids: List[int] = [int(c["elementId"]) for c in cols_2fl]
    print(f"2FL structural columns: {len(ids)}")

    if not ids:
        print("2FL の構造柱が見つかりません。")
        return

    # アームによる室内／室外判定
    arms_res = call_revit(
        "classify_columns_by_room_arms",
        {"elementIds": ids, "armLengthMm": 300.0, "sampleCount": 3},
        port=port,
    )
    items = arms_res.get("columns", [])
    print(f"classified columns: {len(items)}")

    interior_ids = [int(x["elementId"]) for x in items if x.get("classification") == "interior"]
    exterior_ids = [int(x["elementId"]) for x in items if x.get("classification") == "exterior"]

    print(f"interior={len(interior_ids)} exterior={len(exterior_ids)}")

    # ビューのテンプレートを自動的に解除してから着色する
    if exterior_ids:
        call_revit(
            "set_visual_override",
            {
                "viewId": view_id,
                "elementIds": exterior_ids,
                "r": 255,
                "g": 165,
                "b": 0,
                "transparency": 0,
                "autoWorkingView": False,
                "refreshView": True,
                "detachViewTemplate": True,
            },
            port=port,
        )

    if interior_ids:
        call_revit(
            "set_visual_override",
            {
                "viewId": view_id,
                "elementIds": interior_ids,
                "r": 0,
                "g": 0,
                "b": 255,
                "transparency": 0,
                "autoWorkingView": False,
                "refreshView": True,
                "detachViewTemplate": False,
            },
            port=port,
        )

    # 最後にビューをアクティブにしておく
    call_revit("activate_view", {"viewId": view_id}, port=port)


if __name__ == "__main__":
    main()

