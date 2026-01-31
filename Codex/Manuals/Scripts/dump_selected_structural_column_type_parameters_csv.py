# -*- coding: utf-8 -*-
"""
Dump selected Structural Column type parameters (definitions + values) to CSV.

Purpose
- Help create/verify RebarMapping.json profiles (e.g., SIRBIM/RUG) by listing the actual type parameter names.

How it works
1) get_selected_element_ids
2) get_element_info (pick the first element with category == 構造柱)
3) get_structural_column_types (resolve typeId from familyName + typeName)
4) list_structural_column_parameters (definitions)
5) get_type_parameters_bulk (values; chunked)

Outputs
- Work/<RevitFileName>_<docKey>/Reports/selected_structural_column_type_params_<timestamp>.csv

Run (CLI)
python -X utf8 Manuals/Scripts/dump_selected_structural_column_type_parameters_csv.py --port 5210
"""

import argparse
import csv
import json
import os
import re
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple


def _add_scripts_to_path() -> None:
    here = Path(__file__).resolve().parent
    if str(here) not in sys.path:
        sys.path.insert(0, str(here))


_add_scripts_to_path()

from send_revit_command_durable import RevitMcpError, send_request  # type: ignore  # noqa: E402


def _default_port() -> int:
    v = os.environ.get("REVIT_MCP_PORT", "").strip()
    if not v:
        return 5210
    try:
        return int(v)
    except Exception:
        return 5210


def _timestamp() -> str:
    return time.strftime("%Y%m%d_%H%M%S")


def unwrap(envelope: Dict[str, Any]) -> Dict[str, Any]:
    obj: Any = envelope
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj:
        obj = obj["result"]
    if isinstance(obj, dict):
        return obj
    return {}


def _safe_str(v: Any) -> str:
    if v is None:
        return ""
    return str(v)


def _safe_filename(s: str) -> str:
    t = (s or "").strip()
    if not t:
        return "Untitled"
    # Windows reserved characters
    t = re.sub(r'[\\\\/:*?"<>|]+', "_", t)
    t = re.sub(r"\\s+", " ", t).strip()
    return t[:120]


def _repo_root() -> Path:
    # This script lives under Manuals/Scripts in the Codex repo.
    return Path(__file__).resolve().parents[2]


def get_selected_element_ids(port: int) -> Dict[str, Any]:
    env = send_request(port, "get_selected_element_ids", {})
    res = unwrap(env)
    if not res.get("ok"):
        raise RevitMcpError("get_selected_element_ids", res.get("msg", "Failed"))
    return res


def get_element_info(port: int, element_ids: List[int]) -> Dict[str, Any]:
    env = send_request(port, "get_element_info", {"elementIds": element_ids, "rich": False})
    res = unwrap(env)
    if not res.get("ok"):
        raise RevitMcpError("get_element_info", res.get("msg", "Failed"))
    return res


def resolve_structural_column_type_id(port: int, family_name: str, type_name: str) -> int:
    env = send_request(
        port,
        "get_structural_column_types",
        {"familyName": family_name, "typeName": type_name, "skip": 0, "count": 2000, "namesOnly": False},
    )
    res = unwrap(env)
    if not res.get("ok"):
        raise RevitMcpError("get_structural_column_types", res.get("msg", "Failed"))
    types = res.get("types") or []
    for t in types:
        if (t.get("familyName") == family_name) and (t.get("typeName") == type_name):
            return int(t.get("typeId") or 0)
    if len(types) == 1:
        return int(types[0].get("typeId") or 0)
    return 0


def list_structural_column_type_param_defs(port: int, type_id: int) -> List[Dict[str, Any]]:
    env = send_request(
        port,
        "list_structural_column_parameters",
        {"typeId": int(type_id), "skip": 0, "count": 20000, "namesOnly": False},
    )
    res = unwrap(env)
    if not res.get("ok"):
        raise RevitMcpError("list_structural_column_parameters", res.get("msg", f"Failed typeId={type_id}"))
    return res.get("definitions") or []


def get_type_values(port: int, type_id: int, param_names: List[str]) -> Tuple[Dict[str, Any], Dict[str, Any]]:
    values: Dict[str, Any] = {}
    display: Dict[str, Any] = {}
    chunk_size = 80
    for i in range(0, len(param_names), chunk_size):
        chunk = param_names[i : i + chunk_size]
        env = send_request(
            port,
            "get_type_parameters_bulk",
            {
                "typeIds": [int(type_id)],
                "paramKeys": chunk,
                "page": {"startIndex": 0, "batchSize": 50},
                "failureHandling": {"enabled": True, "mode": "rollback"},
            },
        )
        res = unwrap(env)
        if not res.get("ok"):
            raise RevitMcpError("get_type_parameters_bulk", res.get("msg", "Failed"))
        items = res.get("items") or []
        if not items or not items[0].get("ok"):
            continue
        v = items[0].get("params") or {}
        d = items[0].get("display") or {}
        if isinstance(v, dict):
            values.update(v)
        if isinstance(d, dict):
            display.update(d)
    return values, display


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(description="Dump selected structural column type parameters to CSV.")
    ap.add_argument("--port", type=int, default=_default_port(), help="Revit MCP port (env: REVIT_MCP_PORT)")
    ap.add_argument("--names-only", action="store_true", help="Only output paramName column (skip values).")
    args = ap.parse_args(argv)

    try:
        sel = get_selected_element_ids(args.port)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2

    doc_key = _safe_str(sel.get("docKey"))
    doc_path = _safe_str(sel.get("docPath"))
    doc_title = _safe_str(sel.get("docTitle"))
    if not doc_title:
        try:
            doc_title = Path(doc_path).stem if doc_path else ""
        except Exception:
            doc_title = ""
    element_ids = [int(x) for x in (sel.get("elementIds") or []) if int(x) > 0]

    if not element_ids:
        print(
            json.dumps(
                {
                    "ok": False,
                    "code": "NO_SELECTION",
                    "msg": "構造柱を1本選択してから実行してください（現在の選択が空です）。",
                    "docTitle": doc_title,
                    "docKey": doc_key,
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 1

    try:
        info = get_element_info(args.port, element_ids)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2

    elems = info.get("elements") or []
    target: Optional[Dict[str, Any]] = None
    for e in elems:
        if not isinstance(e, dict):
            continue
        cat = _safe_str(e.get("category")).strip()
        if cat in ("構造柱", "Structural Columns", "Structural Column", "OST_StructuralColumns"):
            target = e
            break
    if target is None and elems:
        # Fallback: use first, but warn in output.
        target = elems[0] if isinstance(elems[0], dict) else None

    if target is None:
        print(
            json.dumps(
                {"ok": False, "code": "INVALID_SELECTION", "msg": "選択要素の情報取得に失敗しました。"},
                ensure_ascii=False,
                indent=2,
            )
        )
        return 1

    category = _safe_str(target.get("category")).strip()
    family_name = _safe_str(target.get("familyName")).strip()
    type_name = _safe_str(target.get("typeName")).strip()
    element_id = int(target.get("elementId") or 0)

    # Resolve typeId for structural columns.
    try:
        type_id = resolve_structural_column_type_id(args.port, family_name, type_name)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2

    if type_id <= 0:
        print(
            json.dumps(
                {
                    "ok": False,
                    "code": "TYPE_NOT_FOUND",
                    "msg": "構造柱タイプ(typeId)の解決に失敗しました。",
                    "elementId": element_id,
                    "category": category,
                    "familyName": family_name,
                    "typeName": type_name,
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 1

    try:
        defs = list_structural_column_type_param_defs(args.port, type_id)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2

    def_by_name = {(_safe_str(d.get("name")).strip()): d for d in defs if _safe_str(d.get("name")).strip()}
    param_names = sorted(def_by_name.keys())

    values: Dict[str, Any] = {}
    display: Dict[str, Any] = {}
    if not args.names_only:
        try:
            values, display = get_type_values(args.port, type_id, param_names)
        except RevitMcpError:
            values, display = {}, {}

    ts = _timestamp()
    base_name = _safe_filename(Path(doc_path).stem if doc_path else doc_title)
    project_dir = _repo_root() / "Work" / f"{base_name}_{doc_key}"
    out_dir = project_dir / "Reports"
    out_dir.mkdir(parents=True, exist_ok=True)
    out_csv = out_dir / f"selected_structural_column_type_params_{_safe_filename(type_name)}_{ts}.csv"

    with out_csv.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        if args.names_only:
            w.writerow(["paramName"])
            for name in param_names:
                w.writerow([name])
        else:
            w.writerow(
                [
                    "category",
                    "familyName",
                    "typeName",
                    "typeId",
                    "paramName",
                    "storageType",
                    "dataType",
                    "isReadOnly",
                    "value",
                    "display",
                ]
            )
            for name in param_names:
                d = def_by_name.get(name) or {}
                w.writerow(
                    [
                        category,
                        family_name,
                        type_name,
                        type_id,
                        name,
                        _safe_str(d.get("storageType")),
                        _safe_str(d.get("dataType")),
                        bool(d.get("isReadOnly")) if d else False,
                        _safe_str(values.get(name)),
                        _safe_str(display.get(name)),
                    ]
                )

    print(
        json.dumps(
            {
                "ok": True,
                "savedCsv": str(out_csv),
                "elementId": element_id,
                "category": category,
                "familyName": family_name,
                "typeName": type_name,
                "typeId": type_id,
                "paramCount": len(param_names),
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

