# @feature: dump structural frame type param definitions | keywords: 柱, 梁
import argparse
import csv
import json
import os
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Set, Tuple


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


def _load_annotation_notes(path: Path) -> Tuple[Dict[Tuple[str, str, str], str], Dict[str, str]]:
    per_row: Dict[Tuple[str, str, str], str] = {}
    per_param: Dict[str, str] = {}
    if not path.exists():
        return per_row, per_param

    with path.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        header = next(reader, [])
        if not header:
            return per_row, per_param

        note_idx = None
        for i, h in enumerate(header):
            if (h or "").strip().lower() in ("note", "memo", "description", "desc", "備考", "説明"):
                note_idx = i
                break
        if note_idx is None and len(header) >= 10:
            note_idx = 9  # legacy: 10th column (J)
        if note_idx is None:
            return per_row, per_param

        try:
            family_idx = header.index("familyName")
            type_idx = header.index("typeName")
            param_idx = header.index("paramName")
        except ValueError:
            return per_row, per_param

        for row in reader:
            if len(row) <= note_idx:
                continue
            note = (row[note_idx] or "").strip()
            if not note:
                continue
            fam = (row[family_idx] or "").strip()
            typ = (row[type_idx] or "").strip()
            param = (row[param_idx] or "").strip()
            if not param:
                continue
            per_param.setdefault(param, note)
            if fam or typ:
                per_row[(fam, typ, param)] = note
    return per_row, per_param


def get_structural_frame_types(port: int) -> Dict[str, Any]:
    env = send_request(
        port,
        "get_structural_frame_types",
        {
            "skip": 0,
            "count": 10000,
            "namesOnly": False,
        },
    )
    res = unwrap(env)
    if not res.get("ok"):
        raise RevitMcpError("get_structural_frame_types", res.get("msg", "Failed"))
    return res


def list_structural_frame_type_param_defs(port: int, type_id: int) -> Dict[str, Any]:
    env = send_request(
        port,
        "list_structural_frame_parameters",
        {
            "typeId": int(type_id),
        },
    )
    res = unwrap(env)
    if not res.get("ok"):
        raise RevitMcpError("list_structural_frame_parameters", res.get("msg", f"Failed typeId={type_id}"))
    return res


def get_type_params_bulk(port: int, type_ids: List[int], param_names: List[str]) -> Dict[int, Dict[str, Any]]:
    by_type: Dict[int, Dict[str, Any]] = {}
    start = 0
    batch = 500
    while True:
        env = send_request(
            port,
            "get_type_parameters_bulk",
            {
                "typeIds": type_ids,
                "paramKeys": param_names,
                "page": {"startIndex": start, "batchSize": batch},
                "failureHandling": {"enabled": True, "mode": "rollback"},
            },
        )
        res = unwrap(env)
        if not res.get("ok"):
            raise RevitMcpError("get_type_parameters_bulk", res.get("msg", "Failed"))
        items = res.get("items") or []
        for it in items:
            if not it or not it.get("ok"):
                continue
            tid = int(it.get("typeId") or 0)
            if tid <= 0:
                continue
            by_type[tid] = it
        if res.get("completed") or res.get("nextIndex") is None:
            break
        start = int(res.get("nextIndex") or 0)
    return by_type


def get_type_params_bulk_chunked(
    port: int,
    type_ids: List[int],
    param_names: List[str],
    *,
    param_chunk_size: int = 80,
) -> Dict[int, Dict[str, Any]]:
    merged: Dict[int, Dict[str, Any]] = {}
    if param_chunk_size <= 0:
        param_chunk_size = 80

    for i in range(0, len(param_names), param_chunk_size):
        chunk = param_names[i : i + param_chunk_size]
        chunk_map = get_type_params_bulk(port, type_ids, chunk)
        for tid, it in chunk_map.items():
            ent = merged.get(tid)
            if ent is None:
                ent = {"typeId": tid, "params": {}, "display": {}}
                merged[tid] = ent
            params = it.get("params") or {}
            disp = it.get("display") or {}
            if isinstance(ent.get("params"), dict) and isinstance(params, dict):
                ent["params"].update(params)
            if isinstance(ent.get("display"), dict) and isinstance(disp, dict):
                ent["display"].update(disp)
    return merged


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(
        description=(
            "構造フレーム（OST_StructuralFraming）の全タイプについて、タイプパラメータ定義と（任意で）値をダンプします。"
        )
    )
    ap.add_argument("--port", type=int, default=_default_port(), help="Revit MCP ポート番号 (env: REVIT_MCP_PORT)")
    ap.add_argument("--out-json", type=str, default="", help="出力JSONパス（省略時は Work/RevitMcp/<port>/ に保存）")
    ap.add_argument("--out-csv", type=str, default="", help="出力CSVパス（省略時は Work/RevitMcp/<port>/ に保存）")
    ap.add_argument("--include-values", action="store_true", help="タイプパラメータの値も取得して別CSV/JSONに出力します。")
    ap.add_argument(
        "--param-chunk-size",
        type=int,
        default=80,
        help="値取得時の paramKeys 分割数（大きいと遅い/タイムアウトの原因になり得るため既定80）。",
    )
    ap.add_argument(
        "--annotation-csv",
        type=str,
        default="",
        help="既存のCSV（J列/Note列に説明を書き込んだもの）を指定すると、説明を新しい出力に引き継ぎます。",
    )
    args = ap.parse_args(argv)

    ts = _timestamp()
    out_dir = _out_dir(args.port)
    out_dir.mkdir(parents=True, exist_ok=True)

    out_json = Path(args.out_json) if args.out_json else (out_dir / f"structural_frame_type_param_definitions_{ts}.json")
    out_csv = Path(args.out_csv) if args.out_csv else (out_dir / f"structural_frame_type_param_definitions_{ts}.csv")

    try:
        type_res = get_structural_frame_types(args.port)
    except RevitMcpError as ex:
        print(json.dumps({"ok": False, "code": "MCP_ERROR", "msg": str(ex)}, ensure_ascii=False, indent=2))
        return 2

    types = type_res.get("types") or []
    if not isinstance(types, list) or not types:
        print(json.dumps({"ok": False, "code": "NO_TYPES", "msg": "構造フレームタイプが取得できませんでした。"}, ensure_ascii=False))
        return 1

    all_param_names: Set[str] = set()
    index: Dict[str, Dict[str, Any]] = {}
    per_type: List[Dict[str, Any]] = []

    for t in types:
        type_id = int(t.get("typeId") or 0)
        if type_id <= 0:
            continue
        try:
            defs_res = list_structural_frame_type_param_defs(args.port, type_id)
        except RevitMcpError as ex:
            per_type.append(
                {
                    "ok": False,
                    "typeId": type_id,
                    "typeName": t.get("typeName"),
                    "familyName": t.get("familyName"),
                    "msg": str(ex),
                }
            )
            continue

        defs = defs_res.get("definitions") or []
        per_type.append(
            {
                "ok": True,
                "typeId": type_id,
                "typeName": t.get("typeName"),
                "familyName": t.get("familyName"),
                "familyId": t.get("familyId"),
                "uniqueId": t.get("uniqueId"),
                "paramCount": len(defs),
                "definitions": defs,
            }
        )

        for d in defs:
            name = _safe_str(d.get("name")).strip()
            if not name:
                continue
            all_param_names.add(name)
            agg = index.get(name)
            if agg is None:
                agg = {
                    "name": name,
                    "occurrenceCount": 0,
                    "storageTypes": set(),
                    "dataTypes": set(),
                    "readOnlyCount": 0,
                }
                index[name] = agg
            agg["occurrenceCount"] += 1
            st = _safe_str(d.get("storageType")).strip()
            dt = _safe_str(d.get("dataType")).strip()
            if st:
                agg["storageTypes"].add(st)
            if dt:
                agg["dataTypes"].add(dt)
            if bool(d.get("isReadOnly")):
                agg["readOnlyCount"] += 1

    summary = {
        "ok": True,
        "port": args.port,
        "totalTypes": len([t for t in types if int(t.get("typeId") or 0) > 0]),
        "uniqueParamNameCount": len(all_param_names),
        "types": per_type,
        "paramIndex": [
            {
                "name": k,
                "occurrenceCount": v["occurrenceCount"],
                "storageTypes": sorted(v["storageTypes"]),
                "dataTypes": sorted(v["dataTypes"]),
                "readOnlyCount": v["readOnlyCount"],
            }
            for k, v in sorted(index.items(), key=lambda kv: kv[0])
        ],
    }

    out_json.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    with out_csv.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(["paramName", "occurrenceCount", "storageTypes", "dataTypes", "readOnlyCount"])
        for row in summary["paramIndex"]:
            w.writerow(
                [
                    row["name"],
                    row["occurrenceCount"],
                    "|".join(row["storageTypes"]),
                    "|".join(row["dataTypes"]),
                    row["readOnlyCount"],
                ]
            )

    saved: Dict[str, Any] = {"ok": True, "savedJson": str(out_json), "savedCsv": str(out_csv)}

    if args.include_values:
        type_ids = sorted({int(t.get("typeId") or 0) for t in types if int(t.get("typeId") or 0) > 0})
        param_names = sorted({n for n in all_param_names if n.strip()})
        values_by_type: Dict[int, Dict[str, Any]] = {}
        try:
            values_by_type = get_type_params_bulk_chunked(
                args.port, type_ids, param_names, param_chunk_size=int(args.param_chunk_size or 80)
            )
        except RevitMcpError as ex:
            saved["valuesOk"] = False
            saved["valuesMsg"] = str(ex)
            print(json.dumps(saved, ensure_ascii=False))
            return 0

        out_json2 = out_dir / f"structural_frame_type_parameters_with_values_{ts}.json"
        out_csv2 = out_dir / f"structural_frame_type_parameters_with_values_{ts}.csv"

        per_row_notes: Dict[Tuple[str, str, str], str] = {}
        per_param_notes: Dict[str, str] = {}
        if args.annotation_csv.strip():
            try:
                per_row_notes, per_param_notes = _load_annotation_notes(Path(args.annotation_csv))
            except Exception:
                per_row_notes, per_param_notes = {}, {}

        combined: Dict[str, Any] = {
            "ok": True,
            "port": args.port,
            "totalTypes": summary["totalTypes"],
            "paramNames": param_names,
            "types": [],
        }

        rows: List[List[Any]] = []
        for tinfo in per_type:
            if not tinfo.get("ok"):
                continue
            type_id = int(tinfo.get("typeId") or 0)
            defs = tinfo.get("definitions") or []
            vinfo = values_by_type.get(type_id) or {}
            vmap = vinfo.get("params") or {}
            vdisp = vinfo.get("display") or {}

            combined["types"].append(
                {
                    "typeId": type_id,
                    "familyName": tinfo.get("familyName"),
                    "typeName": tinfo.get("typeName"),
                    "values": vmap,
                    "display": vdisp,
                    "definitions": defs,
                }
            )

            def_by_name = {(_safe_str(d.get("name")).strip()): d for d in defs if _safe_str(d.get("name")).strip()}
            for name in param_names:
                d = def_by_name.get(name) or {}
                note = ""
                key = ((tinfo.get("familyName") or "").strip(), (tinfo.get("typeName") or "").strip(), name)
                if key in per_row_notes:
                    note = per_row_notes[key]
                elif name in per_param_notes:
                    note = per_param_notes[name]
                rows.append(
                    [
                        tinfo.get("familyName") or "",
                        tinfo.get("typeName") or "",
                        type_id,
                        name,
                        d.get("storageType") or "",
                        d.get("dataType") or "",
                        bool(d.get("isReadOnly")) if d else False,
                        vmap.get(name, ""),
                        vdisp.get(name, ""),
                        note,
                    ]
                )

        out_json2.write_text(json.dumps(combined, ensure_ascii=False, indent=2), encoding="utf-8")
        with out_csv2.open("w", newline="", encoding="utf-8-sig") as f:
            w = csv.writer(f)
            w.writerow(
                [
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

        saved["savedValuesJson"] = str(out_json2)
        saved["savedValuesCsv"] = str(out_csv2)

    print(json.dumps(saved, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
