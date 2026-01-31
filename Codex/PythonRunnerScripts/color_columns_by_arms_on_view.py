# @feature: color columns by arms on view | keywords: 柱, 部屋, ビュー, レベル
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
    view_id = 11121440  # 1F_Columns_ArmsTest

    cols_res = call_revit("get_structural_columns", {"skip": 0, "count": 100000}, port=port)
    cols = cols_res.get("structuralColumns", [])

    # このビュー（3FL 相当）の柱だけ対象にする
    cols_3fl = [c for c in cols if c.get("levelName") == "3FL"]
    ids: List[int] = [int(c["elementId"]) for c in cols_3fl]
    print(f"3FL structural columns: {len(ids)}")

    if not ids:
        return

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
                "detachViewTemplate": False,
            },
            port=port,
        )

    call_revit("activate_view", {"viewId": view_id}, port=port)


if __name__ == "__main__":
    main()
