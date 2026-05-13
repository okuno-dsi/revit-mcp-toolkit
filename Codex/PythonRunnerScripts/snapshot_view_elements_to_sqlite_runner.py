# @feature: Revitビュー要素スナップショットをSQLite保存 | keywords: SQLite, snapshot, view, elements, diff, cache
# @arg: name=--port; type=int; default=5210; hint=Revit MCP ポート
# @arg: name=--view-id; type=int; default=0; hint=対象ビューID（0なら現在のアクティブビュー）
# @arg: name=--db-path; type=string; default=; hint=SQLite保存先（空ならプロジェクトフォルダの revit_snapshots.sqlite）
# @arg: name=--batch-size; type=int; default=1000; hint=ページ取得サイズ
# @arg: name=--include-analytic; action=store_true; hint=LocationCurve の端点情報も含める
# @arg: name=--include-view-metadata; action=store_true; hint=カテゴリ名・レベル名を get_elements_in_view で補完
# @arg: name=--type-param; type=string; default=; hint=保存したいタイプパラメータ名（複数指定可）
# -*- coding: utf-8 -*-
"""
Python Script Runner 用:
現在ビュー、または指定ビューの要素スナップショットを SQLite に保存する。

説明:
- 既存の `snapshot_view_elements` をページングしながら取得する
- 可能であれば `get_elements_in_view` を併用し、カテゴリ名/レベル名を補完する
- 1 回の実行結果は同一 SQLite に snapshot レコードとして追記される
- 複雑な比較・監査・再検索は SQLite 側で行える
"""

from __future__ import annotations

import argparse
import json
import re
import sqlite3
import time
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


DEFAULT_PORT = 5210
REQUEST_TIMEOUT_SEC = 30.0
POLL_INTERVAL_SEC = 0.5
POLL_TIMEOUT_SEC = 180.0


def _post_json(url: str, payload: Dict[str, Any], timeout: float = REQUEST_TIMEOUT_SEC) -> Dict[str, Any]:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = Request(url, data=body, headers={"Content-Type": "application/json"})
    with urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def _get_json(url: str, timeout: float = REQUEST_TIMEOUT_SEC) -> Dict[str, Any]:
    with urlopen(url, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def _unwrap(obj: Any) -> Any:
    cur = obj
    for _ in range(6):
        if isinstance(cur, dict) and "result" in cur:
            cur = cur.get("result")
        else:
            break
    return cur


def detect_rpc_endpoint(base_url: str) -> str:
    for suffix in ("/rpc", "/jsonrpc"):
        url = f"{base_url}{suffix}"
        try:
            _post_json(url, {"jsonrpc": "2.0", "id": "ping", "method": "noop", "params": {}}, timeout=3.0)
            return url
        except Exception:
            continue
    return f"{base_url}/rpc"


def poll_job(base_url: str, job_id: str, timeout_sec: float = POLL_TIMEOUT_SEC) -> Any:
    deadline = time.time() + timeout_sec
    job_url = f"{base_url}/job/{job_id}"
    while time.time() < deadline:
        job = _get_json(job_url, timeout=10.0)
        state = str(job.get("state") or "").upper()
        if state == "SUCCEEDED":
            result_json = job.get("result_json")
            if result_json:
                return _unwrap(json.loads(result_json))
            return {"ok": True}
        if state in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(job.get("error_msg") or state)
        time.sleep(POLL_INTERVAL_SEC)
    raise TimeoutError(f"job polling timed out: {job_id}")


def rpc(base_url: str, method: str, params: Optional[Dict[str, Any]] = None) -> Any:
    endpoint = detect_rpc_endpoint(base_url)
    payload = {
        "jsonrpc": "2.0",
        "id": f"req-{int(time.time() * 1000)}",
        "method": method,
        "params": params or {},
    }
    data = _post_json(endpoint, payload)
    if "error" in data:
        raise RuntimeError(json.dumps(data["error"], ensure_ascii=False))
    result = data.get("result", {})
    if isinstance(result, dict) and result.get("queued"):
        job_id = result.get("jobId") or result.get("job_id")
        if not job_id:
            raise RuntimeError("queued response does not contain jobId")
        return poll_job(base_url, str(job_id))
    return _unwrap(result)


def safe_name(text: str) -> str:
    t = re.sub(r'[\\/:*?"<>|]+', "_", (text or "").strip())
    t = re.sub(r"\s+", " ", t).strip()
    return t[:120] if t else "Untitled"


def revit_mcp_root() -> Path:
    here = Path(__file__).resolve()
    for p in [here.parent] + list(here.parents):
        if p.name.lower() == "projects":
            cand = p.parent
            if (cand / "Scripts" / "PythonRunnerScripts").exists():
                return cand
    for p in [here.parent] + list(here.parents):
        if (p / "Scripts" / "PythonRunnerScripts").exists() and (p / "Projects").exists():
            return p
    home_root = Path.home() / "Documents" / "Revit_MCP"
    if (home_root / "Scripts" / "PythonRunnerScripts").exists():
        return home_root
    return here.parents[2]


def default_project_dir(ctx_data: Dict[str, Any]) -> Path:
    root = revit_mcp_root()
    if root.name.lower() == "projects":
        root = root.parent
    title = safe_name(str(ctx_data.get("docTitle") or "UnknownProject"))
    key = safe_name(str(ctx_data.get("docGuid") or ctx_data.get("docKey") or "UnknownKey"))
    project_dir = root / "Projects" / f"{title}_{key}"
    project_dir.mkdir(parents=True, exist_ok=True)
    return project_dir


def connect_db(path: Path) -> sqlite3.Connection:
    path.parent.mkdir(parents=True, exist_ok=True)
    con = sqlite3.connect(str(path))
    con.row_factory = sqlite3.Row
    con.execute("PRAGMA journal_mode=WAL;")
    con.execute("PRAGMA synchronous=NORMAL;")
    con.execute("PRAGMA foreign_keys=ON;")
    return con


def create_schema(con: sqlite3.Connection) -> None:
    con.executescript(
        """
        CREATE TABLE IF NOT EXISTS snapshots (
            snapshot_id INTEGER PRIMARY KEY AUTOINCREMENT,
            created_at_utc TEXT NOT NULL,
            port INTEGER NOT NULL,
            doc_title TEXT NOT NULL,
            doc_guid TEXT NOT NULL,
            view_id INTEGER NOT NULL,
            view_name TEXT NOT NULL,
            total_count INTEGER NOT NULL,
            source_json TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS elements (
            snapshot_id INTEGER NOT NULL REFERENCES snapshots(snapshot_id) ON DELETE CASCADE,
            ordinal INTEGER NOT NULL,
            element_id INTEGER NOT NULL,
            unique_id TEXT NOT NULL,
            category_id INTEGER NOT NULL,
            category_name TEXT,
            level_id INTEGER,
            level_name TEXT,
            family_name TEXT,
            type_name TEXT,
            type_id INTEGER,
            x_mm REAL,
            y_mm REAL,
            z_mm REAL,
            bbox_min_x_mm REAL,
            bbox_min_y_mm REAL,
            bbox_min_z_mm REAL,
            bbox_max_x_mm REAL,
            bbox_max_y_mm REAL,
            bbox_max_z_mm REAL,
            analytic_ax_mm REAL,
            analytic_ay_mm REAL,
            analytic_az_mm REAL,
            analytic_bx_mm REAL,
            analytic_by_mm REAL,
            analytic_bz_mm REAL,
            raw_json TEXT NOT NULL,
            PRIMARY KEY(snapshot_id, ordinal)
        );

        CREATE TABLE IF NOT EXISTS type_parameters (
            snapshot_id INTEGER NOT NULL REFERENCES snapshots(snapshot_id) ON DELETE CASCADE,
            type_id INTEGER NOT NULL,
            param_name TEXT NOT NULL,
            value_json TEXT,
            display_text TEXT,
            PRIMARY KEY(snapshot_id, type_id, param_name)
        );

        CREATE INDEX IF NOT EXISTS idx_elements_snapshot_element ON elements(snapshot_id, element_id);
        CREATE INDEX IF NOT EXISTS idx_elements_snapshot_unique ON elements(snapshot_id, unique_id);
        CREATE INDEX IF NOT EXISTS idx_elements_snapshot_category ON elements(snapshot_id, category_id);
        CREATE INDEX IF NOT EXISTS idx_elements_snapshot_type ON elements(snapshot_id, type_id);
        """
    )


def get_nested_number(obj: Dict[str, Any], *keys: str) -> Optional[float]:
    cur: Any = obj
    for key in keys:
        if not isinstance(cur, dict):
            return None
        cur = cur.get(key)
    if cur is None:
        return None
    try:
        return float(cur)
    except Exception:
        return None


def fetch_view_metadata(base_url: str, view_id: int) -> Dict[int, Dict[str, Any]]:
    result = rpc(base_url, "get_elements_in_view", {"viewId": view_id})
    rows = result if isinstance(result, list) else (result or {}).get("rows") or result
    if isinstance(rows, dict):
        rows = rows.get("elements") or rows.get("items") or []
    mapping: Dict[int, Dict[str, Any]] = {}
    if not isinstance(rows, list):
        return mapping
    for row in rows:
        if not isinstance(row, dict):
            continue
        try:
            element_id = int(row.get("elementId") or 0)
        except Exception:
            element_id = 0
        if element_id <= 0:
            continue
        mapping[element_id] = {
            "categoryName": row.get("categoryName"),
            "levelId": row.get("levelId"),
            "levelName": row.get("levelName"),
        }
    return mapping


def fetch_snapshot_pages(
    base_url: str,
    view_id: int,
    batch_size: int,
    include_analytic: bool,
    include_view_metadata: bool,
    type_param_names: Iterable[str],
) -> Dict[str, Any]:
    meta_by_element_id: Dict[int, Dict[str, Any]] = {}
    warnings: List[str] = []
    if include_view_metadata:
        try:
            meta_by_element_id = fetch_view_metadata(base_url, view_id)
        except Exception as exc:
            warnings.append(f"get_elements_in_view failed: {exc}")

    include_type_params = [{"name": name} for name in type_param_names if name.strip()]

    start_index = 0
    total_count = None
    all_elements: List[Dict[str, Any]] = []
    merged_type_params: Dict[str, Any] = {}
    source_pages: List[Dict[str, Any]] = []

    while True:
        params: Dict[str, Any] = {
            "viewId": view_id,
            "includeAnalytic": bool(include_analytic),
            "page": {"startIndex": start_index, "batchSize": batch_size},
        }
        if include_type_params:
            params["includeTypeParams"] = include_type_params
        page_result = rpc(base_url, "snapshot_view_elements", params)
        if not isinstance(page_result, dict) or not page_result.get("ok", True):
            raise RuntimeError(str(page_result))

        source_pages.append(page_result)
        page_elements = page_result.get("elements") or []
        if total_count is None:
            total_count = int(page_result.get("count") or 0)

        for element in page_elements:
            if not isinstance(element, dict):
                continue
            try:
                element_id = int(element.get("elementId") or 0)
            except Exception:
                element_id = 0
            meta = meta_by_element_id.get(element_id) or {}
            if meta:
                element = dict(element)
                if meta.get("categoryName") and not element.get("categoryName"):
                    element["categoryName"] = meta.get("categoryName")
                if meta.get("levelId") is not None and element.get("levelId") is None:
                    element["levelId"] = meta.get("levelId")
                if meta.get("levelName") and not element.get("levelName"):
                    element["levelName"] = meta.get("levelName")
            all_elements.append(element)

        type_params = page_result.get("typeParameters") or {}
        if isinstance(type_params, dict):
            merged_type_params.update(type_params)

        start_index += len(page_elements)
        if total_count is None or start_index >= total_count or not page_elements:
            break

    return {
        "count": int(total_count or len(all_elements)),
        "project": (source_pages[0].get("project") if source_pages else {}) or {},
        "view": (source_pages[0].get("view") if source_pages else {}) or {},
        "elements": all_elements,
        "typeParameters": merged_type_params,
        "warnings": warnings,
        "sourcePages": source_pages,
    }


def insert_snapshot(
    con: sqlite3.Connection,
    port: int,
    ctx_data: Dict[str, Any],
    snapshot_result: Dict[str, Any],
) -> int:
    created_at_utc = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    cur = con.execute(
        """
        INSERT INTO snapshots(
            created_at_utc, port, doc_title, doc_guid, view_id, view_name, total_count, source_json
        )
        VALUES(?, ?, ?, ?, ?, ?, ?, ?);
        """,
        (
            created_at_utc,
            int(port),
            str(ctx_data.get("docTitle") or ""),
            str(ctx_data.get("docGuid") or ctx_data.get("docKey") or ""),
            int(snapshot_result.get("view", {}).get("id") or ctx_data.get("activeViewId") or 0),
            str(snapshot_result.get("view", {}).get("name") or ctx_data.get("activeViewName") or ""),
            int(snapshot_result.get("count") or 0),
            json.dumps(snapshot_result, ensure_ascii=False),
        ),
    )
    return int(cur.lastrowid)


def insert_elements(con: sqlite3.Connection, snapshot_id: int, elements: List[Dict[str, Any]]) -> None:
    rows = []
    for ordinal, element in enumerate(elements):
        bbox = element.get("bboxMm") or {}
        bbox_min = bbox.get("min") or {}
        bbox_max = bbox.get("max") or {}
        analytic_wire = ((element.get("analytic") or {}).get("wire") or {}) if isinstance(element.get("analytic"), dict) else {}
        analytic_a = analytic_wire.get("a") or {}
        analytic_b = analytic_wire.get("b") or {}
        coords = element.get("coordinatesMm") or {}
        rows.append(
            (
                snapshot_id,
                ordinal,
                int(element.get("elementId") or 0),
                str(element.get("uniqueId") or ""),
                int(element.get("categoryId") or 0),
                element.get("categoryName"),
                element.get("levelId"),
                element.get("levelName"),
                element.get("familyName"),
                element.get("typeName"),
                element.get("typeId"),
                get_nested_number({"root": coords}, "root", "x"),
                get_nested_number({"root": coords}, "root", "y"),
                get_nested_number({"root": coords}, "root", "z"),
                get_nested_number({"root": bbox_min}, "root", "x"),
                get_nested_number({"root": bbox_min}, "root", "y"),
                get_nested_number({"root": bbox_min}, "root", "z"),
                get_nested_number({"root": bbox_max}, "root", "x"),
                get_nested_number({"root": bbox_max}, "root", "y"),
                get_nested_number({"root": bbox_max}, "root", "z"),
                get_nested_number({"root": analytic_a}, "root", "x"),
                get_nested_number({"root": analytic_a}, "root", "y"),
                get_nested_number({"root": analytic_a}, "root", "z"),
                get_nested_number({"root": analytic_b}, "root", "x"),
                get_nested_number({"root": analytic_b}, "root", "y"),
                get_nested_number({"root": analytic_b}, "root", "z"),
                json.dumps(element, ensure_ascii=False),
            )
        )

    con.executemany(
        """
        INSERT INTO elements(
            snapshot_id, ordinal, element_id, unique_id, category_id, category_name, level_id, level_name,
            family_name, type_name, type_id, x_mm, y_mm, z_mm,
            bbox_min_x_mm, bbox_min_y_mm, bbox_min_z_mm, bbox_max_x_mm, bbox_max_y_mm, bbox_max_z_mm,
            analytic_ax_mm, analytic_ay_mm, analytic_az_mm, analytic_bx_mm, analytic_by_mm, analytic_bz_mm,
            raw_json
        )
        VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
        """,
        rows,
    )


def insert_type_parameters(con: sqlite3.Connection, snapshot_id: int, type_parameters: Dict[str, Any]) -> int:
    rows = []
    for type_id_text, payload in (type_parameters or {}).items():
        try:
            type_id = int(type_id_text)
        except Exception:
            continue
        if not isinstance(payload, dict):
            continue
        params = payload.get("params") or {}
        display = payload.get("display") or {}
        if not isinstance(params, dict):
            continue
        for param_name, value in params.items():
            rows.append(
                (
                    snapshot_id,
                    type_id,
                    str(param_name),
                    json.dumps(value, ensure_ascii=False),
                    str(display.get(param_name) or ""),
                )
            )

    if rows:
        con.executemany(
            """
            INSERT OR REPLACE INTO type_parameters(snapshot_id, type_id, param_name, value_json, display_text)
            VALUES(?, ?, ?, ?, ?);
            """,
            rows,
        )
    return len(rows)


def main() -> int:
    ap = argparse.ArgumentParser(description="現在ビューの要素スナップショットを SQLite に保存します。")
    ap.add_argument("--port", type=int, default=DEFAULT_PORT, help="Revit MCP port")
    ap.add_argument("--view-id", type=int, default=0, help="対象ビューID。0 なら現在のアクティブビュー")
    ap.add_argument("--db-path", default="", help="SQLite 保存先。空ならプロジェクトフォルダの revit_snapshots.sqlite")
    ap.add_argument("--batch-size", type=int, default=1000, help="ページ取得サイズ")
    ap.add_argument("--include-analytic", action="store_true", help="LocationCurve の端点情報も含める")
    ap.add_argument("--include-view-metadata", action="store_true", help="get_elements_in_view でカテゴリ名/レベル名を補完する")
    ap.add_argument("--type-param", action="append", default=[], help="保存したいタイプパラメータ名。複数指定可")
    args = ap.parse_args()

    base_url = f"http://127.0.0.1:{args.port}"
    try:
        ctx = rpc(base_url, "get_context", {})
        ctx_data = ((ctx or {}).get("data") or {}) if isinstance(ctx, dict) else {}
    except (HTTPError, URLError, TimeoutError, RuntimeError) as exc:
        print(json.dumps({"ok": False, "msg": str(exc)}, ensure_ascii=False, indent=2))
        return 2

    view_id = int(args.view_id or ctx_data.get("activeViewId") or 0)
    if view_id <= 0:
        print(json.dumps({"ok": False, "msg": "active view not found"}, ensure_ascii=False, indent=2))
        return 1

    if (args.db_path or "").strip():
        db_path = Path(args.db_path.strip()).expanduser()
    else:
        db_path = default_project_dir(ctx_data) / "revit_snapshots.sqlite"

    try:
        snapshot_fetch = fetch_snapshot_pages(
            base_url=base_url,
            view_id=view_id,
            batch_size=max(1, int(args.batch_size)),
            include_analytic=bool(args.include_analytic),
            include_view_metadata=bool(args.include_view_metadata),
            type_param_names=args.type_param or [],
        )
    except (HTTPError, URLError, TimeoutError, RuntimeError) as exc:
        print(json.dumps({"ok": False, "msg": str(exc)}, ensure_ascii=False, indent=2))
        return 2

    snapshot_result = {
        "ok": True,
        "project": snapshot_fetch.get("project") or {
            "name": str(ctx_data.get("docTitle") or ""),
            "guid": str(ctx_data.get("docGuid") or ctx_data.get("docKey") or ""),
        },
        "port": int(args.port),
        "view": snapshot_fetch.get("view") or {"id": view_id, "name": str(ctx_data.get("activeViewName") or "")},
        "count": int(snapshot_fetch.get("count") or 0),
        "elements": snapshot_fetch.get("elements") or [],
        "typeParameters": snapshot_fetch.get("typeParameters") or {},
        "warnings": snapshot_fetch.get("warnings") or [],
    }

    con = connect_db(db_path)
    try:
        create_schema(con)
        snapshot_id = insert_snapshot(con, int(args.port), ctx_data, snapshot_result)
        insert_elements(con, snapshot_id, snapshot_result["elements"])
        type_param_count = insert_type_parameters(con, snapshot_id, snapshot_result["typeParameters"])
        con.commit()
    finally:
        con.close()

    result_obj = {
        "ok": True,
        "dbPath": str(db_path),
        "snapshotId": snapshot_id,
        "docTitle": str(ctx_data.get("docTitle") or ""),
        "docGuid": str(ctx_data.get("docGuid") or ctx_data.get("docKey") or ""),
        "viewId": int(snapshot_result["view"].get("id") or view_id),
        "viewName": str(snapshot_result["view"].get("name") or ctx_data.get("activeViewName") or ""),
        "elementCount": len(snapshot_result["elements"]),
        "totalCount": int(snapshot_result["count"]),
        "typeParameterRowCount": type_param_count,
        "warnings": snapshot_result["warnings"],
        "msg": "Saved snapshot_view_elements result into SQLite.",
    }
    print(json.dumps(result_obj, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
