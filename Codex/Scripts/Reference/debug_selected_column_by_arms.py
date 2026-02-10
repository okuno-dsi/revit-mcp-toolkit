import json
import subprocess
import sys
from typing import Any, Dict


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

    sel = call_revit("get_selected_element_ids", {}, port=port)
    ids = sel.get("elementIds") or []
    if not ids:
        print("選択されている要素がありません。", file=sys.stderr)
        return
    if len(ids) > 1:
        print(f"警告: {len(ids)} 個の要素が選択されています。先頭のみを使用します。", file=sys.stderr)
    col_id = int(ids[0])

    res = call_revit(
        "classify_columns_by_room_arms",
        {
            "elementIds": [col_id],
            "armLengthMm": 300.0,
            "sampleCount": 3,
        },
        port=port,
    )

    cols = res.get("columns") or []
    if not cols:
        print(f"columnId={col_id} に対する結果がありません。")
        return

    c = cols[0]
    print("柱のアーム判定結果:")
    print(f"  elementId    : {c.get('elementId')}")
    print(f"  levelId      : {c.get('levelId')}")
    print(f"  category     : {c.get('category')}")
    print(f"  typeName     : {c.get('typeName')}")
    print(f"  armLengthMm  : {c.get('armLengthMm')}")
    print(f"  sampleCount  : {c.get('sampleCount')}")
    print(f"  totalEndpoints: {c.get('totalEndpoints')}")
    print(f"  insideCount  : {c.get('insideCount')}")
    print(f"  outsideCount : {c.get('outsideCount')}")
    print(f"  classification: {c.get('classification')}")


if __name__ == "__main__":
    main()

