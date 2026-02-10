import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Tuple


def _add_scripts_to_path() -> None:
    here = Path(__file__).resolve().parent
    if str(here) not in sys.path:
        sys.path.insert(0, str(here))


_add_scripts_to_path()

from send_revit_command_durable import send_request, RevitMcpError  # type: ignore  # noqa: E402


def unwrap(payload: Dict[str, Any]) -> Dict[str, Any]:
    obj: Any = payload
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def get_active_view_id(port: int) -> int:
    env = send_request(port, "get_selected_element_ids", {"fallbackToStash": True, "maxAgeMs": 600000})
    sel = unwrap(env)
    vid = int(sel.get("activeViewId") or 0)
    return vid


def get_wall_ids_in_view(port: int, view_id: int) -> List[int]:
    env = send_request(
        port,
        "get_elements_in_view",
        {
            "viewId": view_id,
            "_shape": {"idsOnly": True},
            "categoryNames": ["Walls"],
            "summaryOnly": False,
        },
    )
    res = unwrap(env)
    return [int(eid) for eid in res.get("elementIds") or []]


def classify_wall_finish(port: int, wall_id: int) -> Dict[str, Any]:
    env = send_request(port, "get_wall_finish_summary", {"elementId": wall_id})
    res = unwrap(env)
    if not res.get("ok"):
        return {"wallId": wall_id, "status": "ERROR", "reason": res.get("msg", "")}

    summary = res.get("summary") or []
    # 単純なルール:
    # - summary が 0 要素: 境界が取れない or 仕上情報なし -> 要確認
    # - summary が 1 要素 かつ paintedArea == 0: 正常寄り
    # - それ以外（複数仕上 or 塗装あり）は「要確認」とする
    if not summary:
        return {"wallId": wall_id, "status": "NO_FINISH", "summary": summary}

    if len(summary) == 1 and float(summary[0].get("paintedArea") or 0.0) == 0.0:
        return {"wallId": wall_id, "status": "OK", "summary": summary}

    return {"wallId": wall_id, "status": "SUSPECT", "summary": summary}


def apply_color_overrides(
    port: int,
    view_id: int,
    ok_ids: List[int],
    suspect_ids: List[int],
    detach_view_template: bool = False,
) -> Dict[str, Any]:
    results: Dict[str, Any] = {}

    def _call(ids: List[int], r: int, g: int, b: int) -> Dict[str, Any]:
        if not ids:
            return {}
        env = send_request(
            port,
            "set_visual_override",
            {
                "viewId": view_id,
                "elementIds": ids,
                "autoWorkingView": False,
                "detachViewTemplate": detach_view_template,
                "r": r,
                "g": g,
                "b": b,
                "transparency": 0,
            },
        )
        return unwrap(env)

    if suspect_ids:
        results["suspect"] = _call(suspect_ids, 255, 0, 0)  # 赤
    if ok_ids:
        results["ok"] = _call(ok_ids, 0, 0, 255)  # 青

    return results


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "アクティブビュー内の壁について get_wall_finish_summary を実行し、"
            "仕上情報の有無や塗装の有無から簡易的に OK / 要確認 を判定し、"
            "オプションで色分けします。"
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP ポート番号")
    ap.add_argument(
        "--no-colorize",
        action="store_true",
        help="色付けを行わず、判定結果のみ JSON で出力する場合に指定",
    )
    ap.add_argument(
        "--detach-view-template",
        action="store_true",
        help="ビューにテンプレートが適用されている場合、set_visual_override 実行前にテンプレートを外す試行を許可する",
    )
    args = ap.parse_args(argv)

    port = args.port

    try:
        view_id = get_active_view_id(port)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 1

    if view_id <= 0:
        print(json.dumps({"ok": False, "code": "NO_VIEW", "msg": "アクティブビューが取得できませんでした。"}, ensure_ascii=False))
        return 1

    try:
        wall_ids = get_wall_ids_in_view(port, view_id)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 1

    ok_ids: List[int] = []
    suspect_ids: List[int] = []
    no_finish_ids: List[int] = []
    errors: List[Dict[str, Any]] = []

    for wid in wall_ids:
        try:
            res = classify_wall_finish(port, wid)
        except RevitMcpError as ex:
            errors.append({"wallId": wid, "status": "ERROR", "reason": str(ex)})
            continue

        st = res.get("status")
        if st == "OK":
            ok_ids.append(wid)
        elif st == "SUSPECT":
            suspect_ids.append(wid)
        elif st == "NO_FINISH":
            no_finish_ids.append(wid)
        elif st == "ERROR":
            errors.append(res)

    color_result: Dict[str, Any] = {}
    if not args.no_colorize:
        try:
            color_result = apply_color_overrides(
                port,
                view_id,
                ok_ids=ok_ids,
                suspect_ids=suspect_ids + no_finish_ids,
                detach_view_template=args.detach_view_template,
            )
        except RevitMcpError as ex:
            errors.append({"status": "COLOR_ERROR", "reason": str(ex)})

    out = {
        "ok": True,
        "viewId": view_id,
        "wallCount": len(wall_ids),
        "okCount": len(ok_ids),
        "suspectCount": len(suspect_ids),
        "noFinishCount": len(no_finish_ids),
        "errors": errors,
        "colorResult": color_result,
        "okWallIds": ok_ids,
        "suspectWallIds": suspect_ids,
        "noFinishWallIds": no_finish_ids,
    }
    print(json.dumps(out, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

