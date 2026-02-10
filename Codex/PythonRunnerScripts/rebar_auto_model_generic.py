# -*- coding: utf-8 -*-
# @feature: rebar auto model generic | keywords: 鉄筋, 柱, 梁, タグ, スナップショット
"""
汎用：選択ホスト（構造柱/構造フレーム）を自動配筋（RUG/SIRBIM 両対応）

目的:
- Revit 上で選択したホスト要素（柱/梁）に対して、RevitMCP の配筋ロジックで Rebar を作成します。
- 既存の「ツール生成（タグ付き）」鉄筋がある場合は、ホスト内の該当鉄筋を削除してから再作成します
  （deleteMode=tagged_only のため、安全側）。

前提:
- Revit 起動済み + Revit MCP サーバー稼働中（例: 5210）
- ホスト（柱/梁）を選択してから実行（--host-ids で直接指定も可）

使い方（例）:
- 選択要素で実行（推奨）:
    python Scripts/Reference/rebar_auto_model_generic.py --port 5210
- elementId を直接指定:
    python Scripts/Reference/rebar_auto_model_generic.py --port 5210 --host-ids 110463,111855
- 被り厚さの曖昧さを承認して続行（必要な場合のみ）:
    python Scripts/Reference/rebar_auto_model_generic.py --cover-confirm-proceed

注意:
- 実行コマンドは write で、ホスト内の「タグ付き」鉄筋を削除→再作成します。
  タグ既定: RevitMcp:AutoRebar（options.tagComments と同一にしています）
"""

import argparse
import json
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Optional


def _add_scripts_to_path() -> None:
    here = Path(__file__).resolve().parent
    if str(here) not in sys.path:
        sys.path.insert(0, str(here))


_add_scripts_to_path()

from send_revit_command_durable import RevitMcpError, send_request  # type: ignore  # noqa: E402


DEFAULT_TAG = "RevitMcp:AutoRebar"


def unwrap(envelope: Dict[str, Any]) -> Dict[str, Any]:
    """send_revit_command_durable の返り値の result 二重包みを吸収."""
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


def _parse_ids_csv(s: str) -> List[int]:
    out: List[int] = []
    for part in (s or "").split(","):
        part = part.strip()
        if not part:
            continue
        try:
            v = int(part)
            if v > 0:
                out.append(v)
        except Exception:
            continue
    return out


def get_selected_ids(port: int) -> List[int]:
    env = send_request(port, "get_selected_element_ids", {"fallbackToStash": True, "maxAgeMs": 3000})
    res = unwrap(env)
    return [int(x) for x in (res.get("elementIds") or []) if int(x) > 0]


def get_element_brief(port: int, element_ids: List[int]) -> List[Dict[str, Any]]:
    env = send_request(port, "get_element_info", {"elementIds": element_ids, "rich": False})
    res = unwrap(env)
    out: List[Dict[str, Any]] = []
    for e in (res.get("elements") or []):
        out.append(
            {
                "elementId": e.get("elementId"),
                "category": e.get("category"),
                "typeName": e.get("typeName"),
            }
        )
    return out


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "選択した構造柱/構造フレーム（梁など）に自動配筋を実行します。"
            "RUG/SIRBIM 等は RebarMapping.json のプロファイル自動選択で吸収します。"
        )
    )
    ap.add_argument("--port", type=int, default=_default_port(), help="Revit MCP ポート番号 (env: REVIT_MCP_PORT)")
    ap.add_argument(
        "--host-ids",
        type=str,
        default="",
        help="カンマ区切り elementId。省略時は Revit の選択から取得します。",
    )
    ap.add_argument(
        "--profile",
        type=str,
        default="",
        help="RebarMapping.json の profile 名（省略時は自動選択。RUG は rug_column_attr_v1 / rug_beam_attr_v1 が選ばれます）",
    )
    ap.add_argument("--tag", type=str, default=DEFAULT_TAG, help="ツール生成鉄筋の識別タグ（Comments）")
    ap.add_argument(
        "--cover-confirm-proceed",
        action="store_true",
        help="被り厚さの曖昧さがある場合でも続行する（必要な場合のみ）。",
    )
    ap.add_argument("--store-recipe-snapshot", action="store_true", help="配筋レシピのスナップショットを RVT に保存（肥大化注意）")
    ap.add_argument("--main-bar-type", type=str, default="", help="主筋 BarType 名（例: D29）。空ならマッピング/既定に従う")
    ap.add_argument("--tie-bar-type", type=str, default="", help="帯筋/あばら筋 BarType 名（例: D13）。空ならマッピング/既定に従う")
    args = ap.parse_args(argv)

    host_ids = _parse_ids_csv(args.host_ids)
    use_selection = False
    if not host_ids:
        use_selection = True
        try:
            host_ids = get_selected_ids(args.port)
        except RevitMcpError as ex:
            print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
            return 2

    if not host_ids:
        print(json.dumps({"ok": False, "code": "NO_SELECTION", "msg": "ホスト要素（構造柱/構造フレーム）を選択してください。"}, ensure_ascii=False))
        return 1

    # Show what we will process (helpful for humans).
    try:
        brief = get_element_brief(args.port, host_ids)
        print("=== Targets ===")
        for row in brief:
            print(f"- {row.get('elementId')} / {row.get('category')} / {row.get('typeName')}")
    except Exception:
        pass

    tag = (args.tag or "").strip() or DEFAULT_TAG
    options: Dict[str, Any] = {
        "tagComments": tag,
        "coverConfirmProceed": bool(args.cover_confirm_proceed),
    }
    if args.main_bar_type.strip():
        options["mainBarTypeName"] = args.main_bar_type.strip()
    if args.tie_bar_type.strip():
        options["tieBarTypeName"] = args.tie_bar_type.strip()

    params: Dict[str, Any] = {
        "useSelectionIfEmpty": bool(use_selection),
        "hostElementIds": host_ids,
        "tag": tag,
        "deleteMode": "tagged_only",
        "storeRecipeSnapshot": bool(args.store_recipe_snapshot),
        "options": options,
    }
    if args.profile.strip():
        params["profile"] = args.profile.strip()

    try:
        env = send_request(args.port, "rebar_regenerate_delete_recreate", params)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2

    res = unwrap(env)

    # Save full result for auditing.
    out_dir = _out_dir(args.port)
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / f"rebar_auto_model_{_timestamp()}.json"
    out_path.write_text(json.dumps(res, ensure_ascii=False, indent=2), encoding="utf-8")

    if not bool(res.get("ok")):
        code = res.get("code") or "NOT_OK"
        msg = res.get("msg") or "配筋を実行できませんでした。"
        print(json.dumps({"ok": False, "code": code, "msg": msg, "savedTo": str(out_path)}, ensure_ascii=False, indent=2))
        return 2

    # Human-friendly summary.
    created_all: List[int] = []
    results = res.get("results") or []
    print("")
    print("=== Result summary ===")
    for r in results:
        hid = r.get("hostElementId")
        ok = bool(r.get("ok"))
        code = r.get("code") or ""
        msg = r.get("msg") or ""
        created = [int(x) for x in (r.get("createdRebarIds") or []) if int(x) > 0]
        deleted = [int(x) for x in (r.get("deletedRebarIds") or []) if int(x) > 0]
        created_all.extend(created)
        if ok:
            print(f"- host {hid}: OK (deleted={len(deleted)} created={len(created)})")
        else:
            print(f"- host {hid}: NG code={code} msg={msg}")

    created_all = sorted(set(created_all))
    print("")
    print(json.dumps({"ok": True, "createdRebarCount": len(created_all), "savedTo": str(out_path)}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))



