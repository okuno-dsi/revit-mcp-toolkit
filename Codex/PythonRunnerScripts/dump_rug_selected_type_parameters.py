# @feature: dump rug selected type parameters | keywords: 柱, 梁
import argparse
import csv
import json
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Tuple


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


def _safe_str(v: Any) -> str:
    if v is None:
        return ""
    return str(v)


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


def resolve_structural_frame_type_id(port: int, family_name: str, type_name: str) -> int:
    env = send_request(
        port,
        "get_structural_frame_types",
        {"familyName": family_name, "typeName": type_name, "skip": 0, "count": 5000, "namesOnly": False},
    )
    res = unwrap(env)
    if not res.get("ok"):
        raise RevitMcpError("get_structural_frame_types", res.get("msg", "Failed"))
    types = res.get("types") or []
    for t in types:
        if (t.get("familyName") == family_name) and (t.get("typeName") == type_name):
            return int(t.get("typeId") or 0)
    if len(types) == 1:
        return int(types[0].get("typeId") or 0)
    return 0


def list_type_param_defs(port: int, kind: str, type_id: int) -> List[Dict[str, Any]]:
    if kind == "structural_column":
        method = "list_structural_column_parameters"
        params = {"typeId": int(type_id), "skip": 0, "count": 20000, "namesOnly": False}
    elif kind == "structural_frame":
        method = "list_structural_frame_parameters"
        params = {"typeId": int(type_id)}
    else:
        raise ValueError(f"Unknown kind: {kind}")

    env = send_request(port, method, params)
    res = unwrap(env)
    if not res.get("ok"):
        raise RevitMcpError(method, res.get("msg", f"Failed typeId={type_id}"))
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
    ap = argparse.ArgumentParser(
        description=(
            "RUGファミリとして指定された2タイプ（構造柱/構造フレーム）のタイプパラメータ定義と値をCSV/JSONで書き出します。"
        )
    )
    ap.add_argument("--port", type=int, default=_default_port(), help="Revit MCP ポート番号 (env: REVIT_MCP_PORT)")
    ap.add_argument("--column-family", type=str, default="RC_C_B", help="構造柱ファミリ名（既定 RC_C_B）")
    ap.add_argument("--column-type", type=str, default="1C1", help="構造柱タイプ名（既定 1C1）")
    ap.add_argument("--frame-family", type=str, default="RC_B", help="構造フレームファミリ名（既定 RC_B）")
    ap.add_argument("--frame-type", type=str, default="FG13", help="構造フレームタイプ名（既定 FG13）")
    args = ap.parse_args(argv)

    ts = _timestamp()
    out_dir = _out_dir(args.port)
    out_dir.mkdir(parents=True, exist_ok=True)
    out_csv = out_dir / f"rug_type_parameters_with_values_{ts}.csv"
    out_json = out_dir / f"rug_type_parameters_with_values_{ts}.json"

    try:
        col_type_id = resolve_structural_column_type_id(args.port, args.column_family, args.column_type)
        fr_type_id = resolve_structural_frame_type_id(args.port, args.frame_family, args.frame_type)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2

    if col_type_id <= 0 or fr_type_id <= 0:
        print(
            json.dumps(
                {
                    "ok": False,
                    "code": "TYPE_NOT_FOUND",
                    "msg": "typeId の解決に失敗しました。",
                    "column": {"family": args.column_family, "type": args.column_type, "typeId": col_type_id},
                    "frame": {"family": args.frame_family, "type": args.frame_type, "typeId": fr_type_id},
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 1

    targets = [
        ("structural_column", "構造柱", args.column_family, args.column_type, col_type_id),
        ("structural_frame", "構造フレーム", args.frame_family, args.frame_type, fr_type_id),
    ]

    rows: List[List[Any]] = []
    json_targets: List[Dict[str, Any]] = []

    for kind, category_name, fam, typ, tid in targets:
        defs = list_type_param_defs(args.port, kind, tid)
        def_by_name = {(_safe_str(d.get("name")).strip()): d for d in defs if _safe_str(d.get("name")).strip()}
        param_names = sorted(def_by_name.keys())
        values, display = get_type_values(args.port, tid, param_names)

        json_targets.append(
            {
                "category": category_name,
                "kind": kind,
                "familyName": fam,
                "typeName": typ,
                "typeId": tid,
                "definitions": defs,
                "values": values,
                "display": display,
            }
        )

        for name in param_names:
            d = def_by_name.get(name) or {}
            rows.append(
                [
                    category_name,
                    kind,
                    fam,
                    typ,
                    tid,
                    name,
                    d.get("storageType") or "",
                    d.get("dataType") or "",
                    bool(d.get("isReadOnly")) if d else False,
                    values.get(name, ""),
                    display.get(name, ""),
                    "",  # note (for users)
                ]
            )

    with out_csv.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(
            [
                "categoryName",
                "kind",
                "familyName",
                "typeName",
                "typeId",
                "paramName",
                "storageType",
                "dataType",
                "isReadOnly",
                "value",
                "display",
                "note",
            ]
        )
        w.writerows(rows)

    out_json.write_text(
        json.dumps(
            {
                "ok": True,
                "port": args.port,
                "rug": True,
                "targets": json_targets,
            },
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )

    print(json.dumps({"ok": True, "savedCsv": str(out_csv), "savedJson": str(out_json)}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

