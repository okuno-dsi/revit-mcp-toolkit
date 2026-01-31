# @feature: rebar mapping dump selected hosts | keywords: 鉄筋, 柱, 梁
import argparse
import json
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List


def _add_scripts_to_path() -> None:
    here = Path(__file__).resolve().parent
    if str(here) not in sys.path:
        sys.path.insert(0, str(here))


_add_scripts_to_path()

from send_revit_command_durable import RevitMcpError, send_request  # type: ignore  # noqa: E402


def unwrap(envelope: Dict[str, Any]) -> Dict[str, Any]:
    obj: Any = envelope
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def _default_port() -> int:
    v = os.environ.get("REVIT_MCP_PORT", "").strip()
    if not v:
        return 5210
    try:
        return int(v)
    except Exception:
        return 5210


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def _out_dir(port: int) -> Path:
    return _repo_root() / "Work" / "RevitMcp" / str(port)


def _timestamp() -> str:
    return time.strftime("%Y%m%d_%H%M%S")


def get_selected_ids(port: int) -> List[int]:
    env = send_request(port, "get_selected_element_ids", {})
    res = unwrap(env)
    return [int(x) for x in (res.get("elementIds") or []) if int(x) > 0]


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "選択中ホスト（柱/梁など）に対して rebar_mapping_resolve を実行し、"
            "RebarMapping.json の論理キー解決結果（values + sources）を JSON で保存します。"
        )
    )
    ap.add_argument("--port", type=int, default=_default_port(), help="Revit MCP ポート番号 (env: REVIT_MCP_PORT)")
    ap.add_argument("--profile", type=str, default="", help="RebarMapping.json プロファイル名（省略時は自動選択）")
    ap.add_argument(
        "--keys",
        type=str,
        default="",
        help="カンマ区切りでキーを指定（省略時は、そのプロファイルの全キーを解決）",
    )
    ap.add_argument("--include-debug", action="store_true", help="keyごとの source 詳細を含める（推奨）")
    args = ap.parse_args(argv)

    try:
        host_ids = get_selected_ids(args.port)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2

    if not host_ids:
        print(json.dumps({"ok": False, "code": "NO_SELECTION", "msg": "ホスト要素を選択してください。"}, ensure_ascii=False))
        return 1

    keys: List[str] = []
    if args.keys.strip():
        keys = [x.strip() for x in args.keys.split(",") if x.strip()]

    params: Dict[str, Any] = {
        "hostElementIds": host_ids,
        "useSelectionIfEmpty": False,
        "includeDebug": bool(args.include_debug),
    }
    if args.profile.strip():
        params["profile"] = args.profile.strip()
    if keys:
        params["keys"] = keys

    try:
        env = send_request(args.port, "rebar_mapping_resolve", params)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2

    res = unwrap(env)
    out_dir = _out_dir(args.port)
    out_dir.mkdir(parents=True, exist_ok=True)
    ts = _timestamp()
    out_path = out_dir / f"rebar_mapping_dump_{ts}.json"
    out_path.write_text(json.dumps(res, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"ok": True, "savedTo": str(out_path), "hostIds": host_ids}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

