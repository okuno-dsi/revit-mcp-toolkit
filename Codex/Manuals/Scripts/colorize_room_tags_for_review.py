import argparse
import json
import sys
from pathlib import Path
from typing import Any, Dict, List


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
    return int(sel.get("activeViewId") or 0)


def duplicate_view_for_review(port: int, view_id: int, suffix: str = "_Review") -> int:
    """
    create_seed_and_type_views_typeid.ps1 等と同様の考え方で、
    単純にビューを複製して名前にサフィックスを付ける。
    Revit 側の既存コマンド 'duplicate_view_safe' 相当があればそれを使いたいが、
    ここでは簡易に 'duplicate_view' を呼び出す前提とする。
    """
    try:
        env = send_request(
            port,
            "duplicate_view",
            {
                "viewId": view_id,
                "withDetailing": True,
                # 元ビュー名 + suffix をベースに Revit 側で名前を決めてもらう
                "namePrefix": suffix,
            },
        )
    except RevitMcpError as ex:
        raise ex

    res = unwrap(env)
    if not res.get("ok"):
        raise RevitMcpError("duplicate_view", res.get("msg", "duplicate_view failed"))
    return int(res.get("newViewId") or res.get("viewId") or 0)


def colorize_room_tags_by_param(
    port: int,
    view_id: int,
    param_name: str,
    detach_view_template: bool = False,
) -> Dict[str, Any]:
    """
    ColorizeTagsByParamCommand を叩いて、指定パラメータでタグを色分けする。
    """
    env = send_request(
        port,
        "colorize_tags_by_param",
        {
            "viewId": view_id,
            "categoryNames": ["Rooms"],
            "paramName": param_name,
            "detachViewTemplate": detach_view_template,
        },
    )
    return unwrap(env)


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "アクティブな平面ビューを複製し、Room タグを指定パラメータで色分けしたレビュー用ビューを作成します。"
        )
    )
    ap.add_argument("--port", type=int, default=5210, help="Revit MCP ポート番号")
    ap.add_argument(
        "--param-name",
        type=str,
        default="仕上 床",
        help="Room タグを色分けするパラメータ名（例: 仕上 床）",
    )
    ap.add_argument(
        "--no-duplicate",
        action="store_true",
        help="ビューを複製せず、アクティブビューに直接色付けしたい場合に指定",
    )
    ap.add_argument(
        "--detach-view-template",
        action="store_true",
        help="レビュー用ビューに色付けする前に View Template を外す場合に指定",
    )
    args = ap.parse_args(argv)

    port = args.port

    try:
        active_view_id = get_active_view_id(port)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 1

    if active_view_id <= 0:
        print(json.dumps({"ok": False, "code": "NO_VIEW", "msg": "アクティブビューが取得できませんでした。"}, ensure_ascii=False))
        return 1

    target_view_id = active_view_id
    duplicated = False

    if not args.no_duplicate:
        try:
            target_view_id = duplicate_view_for_review(port, active_view_id, suffix="_Review")
            duplicated = True
        except RevitMcpError as ex:
            print(
                json.dumps(
                    {"ok": False, "code": "DUPLICATE_FAILED", "msg": str(ex)},
                    ensure_ascii=False,
                    indent=2,
                )
            )
            return 1

    try:
        color_res = colorize_room_tags_by_param(
            port,
            target_view_id,
            param_name=args.param_name,
            detach_view_template=args.detach_view_template,
        )
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 1

    out = {
        "ok": True,
        "sourceViewId": active_view_id,
        "reviewViewId": target_view_id,
        "duplicated": duplicated,
        "paramName": args.param_name,
        "colorizeResult": color_res,
    }
    print(json.dumps(out, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
