#!/usr/bin/env python3
# -*- coding: utf-8 -*-
from __future__ import annotations

import argparse
import json
import sqlite3
from pathlib import Path


def connect_db(path: Path) -> sqlite3.Connection:
    con = sqlite3.connect(str(path))
    con.row_factory = sqlite3.Row
    return con


def latest_snapshot_id(con: sqlite3.Connection) -> int | None:
    row = con.execute("SELECT snapshot_id FROM snapshots ORDER BY snapshot_id DESC LIMIT 1;").fetchone()
    return None if row is None else int(row["snapshot_id"])


def list_snapshots(con: sqlite3.Connection, limit: int) -> dict:
    rows = con.execute(
        """
        SELECT snapshot_id, created_at_utc, port, doc_title, doc_guid, view_id, view_name, total_count
        FROM snapshots
        ORDER BY snapshot_id DESC
        LIMIT ?;
        """,
        (int(limit),),
    ).fetchall()
    return {"ok": True, "mode": "list", "snapshots": [dict(row) for row in rows]}


def query_elements(
    con: sqlite3.Connection,
    snapshot_id: int,
    element_id: int | None,
    unique_id: str,
    category_name_contains: str,
    family_name_contains: str,
    type_name_contains: str,
    limit: int,
) -> dict:
    sql = [
        """
        SELECT
            s.snapshot_id,
            s.doc_title,
            s.view_name,
            e.ordinal,
            e.element_id,
            e.unique_id,
            e.category_id,
            e.category_name,
            e.level_id,
            e.level_name,
            e.family_name,
            e.type_name,
            e.type_id,
            e.x_mm,
            e.y_mm,
            e.z_mm,
            e.raw_json
        FROM elements AS e
        INNER JOIN snapshots AS s ON s.snapshot_id = e.snapshot_id
        WHERE e.snapshot_id = ?
        """
    ]
    params: list[object] = [int(snapshot_id)]

    if element_id is not None:
        sql.append("AND e.element_id = ?")
        params.append(int(element_id))
    if unique_id.strip():
        sql.append("AND e.unique_id = ?")
        params.append(unique_id.strip())
    if category_name_contains.strip():
        sql.append("AND COALESCE(e.category_name, '') LIKE ?")
        params.append(f"%{category_name_contains.strip()}%")
    if family_name_contains.strip():
        sql.append("AND COALESCE(e.family_name, '') LIKE ?")
        params.append(f"%{family_name_contains.strip()}%")
    if type_name_contains.strip():
        sql.append("AND COALESCE(e.type_name, '') LIKE ?")
        params.append(f"%{type_name_contains.strip()}%")

    sql.append("ORDER BY e.ordinal LIMIT ?")
    params.append(int(limit))

    rows = con.execute("\n".join(sql), params).fetchall()
    return {
        "ok": True,
        "mode": "query",
        "snapshotId": snapshot_id,
        "resultCount": len(rows),
        "elements": [dict(row) for row in rows],
    }


def build_arg_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(description="Query a Revit snapshot SQLite database.")
    ap.add_argument("--db-path", required=True, help="SQLite database path created by snapshot_view_elements_to_sqlite_runner.py")
    ap.add_argument("--list-snapshots", action="store_true", help="List stored snapshots instead of querying elements")
    ap.add_argument("--snapshot-id", type=int, default=0, help="Snapshot ID to query. Default: latest")
    ap.add_argument("--element-id", type=int, default=0, help="Exact element ID filter")
    ap.add_argument("--unique-id", default="", help="Exact unique ID filter")
    ap.add_argument("--category-name-contains", default="", help="Substring filter for category name")
    ap.add_argument("--family-name-contains", default="", help="Substring filter for family name")
    ap.add_argument("--type-name-contains", default="", help="Substring filter for type name")
    ap.add_argument("--limit", type=int, default=50, help="Maximum rows to return")
    return ap


def main() -> int:
    args = build_arg_parser().parse_args()
    db_path = Path(args.db_path).expanduser()
    if not db_path.exists():
        raise SystemExit(f"Database not found: {db_path}")

    con = connect_db(db_path)
    try:
        if args.list_snapshots:
            result = list_snapshots(con, max(1, int(args.limit)))
        else:
            snapshot_id = int(args.snapshot_id) if int(args.snapshot_id) > 0 else (latest_snapshot_id(con) or 0)
            if snapshot_id <= 0:
                result = {"ok": False, "msg": "No snapshots found."}
            else:
                result = query_elements(
                    con=con,
                    snapshot_id=snapshot_id,
                    element_id=int(args.element_id) if int(args.element_id) > 0 else None,
                    unique_id=args.unique_id,
                    category_name_contains=args.category_name_contains,
                    family_name_contains=args.family_name_contains,
                    type_name_contains=args.type_name_contains,
                    limit=max(1, int(args.limit)),
                )
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0 if result.get("ok") else 1
    finally:
        con.close()


if __name__ == "__main__":
    raise SystemExit(main())
