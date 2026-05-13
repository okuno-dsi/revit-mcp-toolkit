#!/usr/bin/env python3
# -*- coding: utf-8 -*-
from __future__ import annotations

import argparse
import json
import os
import sqlite3
from pathlib import Path
from typing import Any


SCRIPT_DIR = Path(__file__).resolve().parent
CODEX_ROOT = SCRIPT_DIR.parent
ENV_BUNDLE_ROOT = "REVIT_MCP_BUILDING_LAW_SQLITE_ROOT"


def detect_default_bundle_root() -> Path:
    env_root = os.environ.get(ENV_BUNDLE_ROOT, "").strip()
    candidates = []
    if env_root:
        candidates.append(Path(env_root).expanduser())
    candidates.extend(
        [
            CODEX_ROOT.parent / "SQLite" / "Legal" / "BuildingCode",
            Path.home() / "Documents" / "Revit_MCP" / "SQLite" / "Legal" / "BuildingCode",
            CODEX_ROOT / "Work" / "sqlite" / "legal" / "building_code",
        ]
    )
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return candidates[0]


DEFAULT_BUNDLE_ROOT = detect_default_bundle_root()


def xml_db_path(bundle_root: Path) -> Path:
    return bundle_root / "xml_law_index.sqlite"


def gov_text_db_path(bundle_root: Path) -> Path:
    return bundle_root / "gov_document_text_index.sqlite"


def gov_pdf_db_path(bundle_root: Path) -> Path:
    return bundle_root / "gov_document_pdf_index.sqlite"


def manifest_path(bundle_root: Path) -> Path:
    return bundle_root / "bundle_manifest.json"


def connect_db_ro(path: Path) -> sqlite3.Connection:
    con = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
    con.row_factory = sqlite3.Row
    return con


def table_exists(con: sqlite3.Connection, name: str) -> bool:
    row = con.execute(
        "SELECT 1 FROM sqlite_master WHERE type IN ('table','view') AND name = ? LIMIT 1;",
        (name,),
    ).fetchone()
    return row is not None


def build_fts_query(text: str) -> str:
    raw = (text or "").strip()
    if not raw:
        return ""
    return raw


def build_fts_query_fallback(text: str) -> str:
    tokens: list[str] = []
    cur = []
    for ch in text:
        if ch.isalnum() or "\u3040" <= ch <= "\u30ff" or "\u4e00" <= ch <= "\u9fff":
            cur.append(ch)
            continue
        if cur:
            tokens.append("".join(cur))
            cur = []
    if cur:
        tokens.append("".join(cur))
    tokens = [t for t in tokens if t]
    if not tokens:
        return f'"{text.strip()}"'
    return " AND ".join(f'"{t}"' for t in tokens)


def query_with_fallback(con: sqlite3.Connection, sql: str, match_text: str, params: list[Any]) -> list[sqlite3.Row]:
    try:
        return con.execute(sql, [build_fts_query(match_text), *params]).fetchall()
    except sqlite3.OperationalError:
        return con.execute(sql, [build_fts_query_fallback(match_text), *params]).fetchall()


def normalize_snippet(text: str) -> str:
    return (text or "").replace("\r\n", "\n").replace("\r", "\n").strip()


def existing_bundle_info(bundle_root: Path) -> dict[str, Any]:
    info: dict[str, Any] = {
        "bundleRoot": str(bundle_root),
        "exists": bundle_root.exists(),
        "manifestPath": str(manifest_path(bundle_root)),
        "dbs": {
            "xmlLaw": str(xml_db_path(bundle_root)),
            "govText": str(gov_text_db_path(bundle_root)),
            "govPdf": str(gov_pdf_db_path(bundle_root)),
        },
    }
    mf = manifest_path(bundle_root)
    if mf.exists():
        try:
            info["manifest"] = json.loads(mf.read_text(encoding="utf-8"))
        except Exception as ex:
            info["manifestError"] = str(ex)
    return info


def list_laws(bundle_root: Path, limit: int, law_title_contains: str) -> dict[str, Any]:
    path = xml_db_path(bundle_root)
    if not path.exists():
        return {"ok": False, "msg": f"xml law DB not found: {path}"}

    like = f"%{law_title_contains.strip()}%" if law_title_contains.strip() else ""
    con = connect_db_ro(path)
    try:
        rows = con.execute(
            """
            SELECT
                d.id,
                d.law_title,
                d.law_num,
                d.law_type,
                d.file_name,
                d.relative_path,
                d.size_bytes,
                (
                    SELECT COUNT(*)
                    FROM law_chunks AS c
                    WHERE c.document_id = d.id
                ) AS chunk_count
            FROM law_documents AS d
            WHERE (? = '' OR COALESCE(d.law_title, '') LIKE ?)
            ORDER BY COALESCE(d.law_title, ''), COALESCE(d.law_num, '')
            LIMIT ?;
            """,
            ("", like, max(1, int(limit))) if not like else (like, like, max(1, int(limit))),
        ).fetchall()
        return {
            "ok": True,
            "mode": "list-laws",
            "bundleRoot": str(bundle_root),
            "lawCount": len(rows),
            "laws": [dict(r) for r in rows],
        }
    finally:
        con.close()


def search_xml_law(
    bundle_root: Path,
    query: str,
    limit: int,
    law_title_contains: str,
    include_references: bool,
    references_limit: int,
) -> list[dict[str, Any]]:
    path = xml_db_path(bundle_root)
    if not path.exists():
        return []

    like = f"%{law_title_contains.strip()}%" if law_title_contains.strip() else ""
    con = connect_db_ro(path)
    try:
        rows = query_with_fallback(
            con,
            """
            SELECT
                lc.id AS chunk_id,
                lc.law_title,
                lc.law_num,
                lc.number AS article_number,
                lc.title AS article_title,
                lc.caption,
                lc.chunk_kind,
                lc.law_path,
                lc.ordinal,
                ld.relative_path,
                snippet(law_chunks_fts, 5, '[', ']', '...', 18) AS snippet
            FROM law_chunks_fts
            INNER JOIN law_chunks AS lc ON lc.id = law_chunks_fts.rowid
            LEFT JOIN law_documents AS ld ON ld.id = lc.document_id
            WHERE law_chunks_fts MATCH ?
              AND (? = '' OR COALESCE(lc.law_title, '') LIKE ?)
            ORDER BY bm25(law_chunks_fts), COALESCE(lc.law_title, ''), lc.ordinal
            LIMIT ?;
            """,
            query,
            [like, like, max(1, int(limit))],
        )
        results: list[dict[str, Any]] = []
        ref_limit = max(1, int(references_limit))
        for row in rows:
            item = {
                "source": "xml-law",
                "lawTitle": row["law_title"],
                "lawNum": row["law_num"],
                "articleNumber": row["article_number"],
                "articleTitle": row["article_title"],
                "caption": row["caption"],
                "chunkKind": row["chunk_kind"],
                "ordinal": row["ordinal"],
                "lawPath": row["law_path"],
                "relativePath": row["relative_path"],
                "bundlePath": str(bundle_root / row["relative_path"]) if row["relative_path"] else "",
                "chunkId": row["chunk_id"],
                "snippet": normalize_snippet(row["snippet"]),
            }
            if include_references and table_exists(con, "law_chunk_references"):
                refs = con.execute(
                    """
                    SELECT
                        source_article_number,
                        reference_kind,
                        reference_text,
                        target_law_title,
                        target_article,
                        context
                    FROM law_chunk_references
                    WHERE chunk_id = ?
                    ORDER BY ordinal
                    LIMIT ?;
                    """,
                    (int(row["chunk_id"]), ref_limit),
                ).fetchall()
                item["references"] = [dict(r) for r in refs]
            results.append(item)
        return results
    finally:
        con.close()


def search_gov_text(bundle_root: Path, query: str, limit: int) -> list[dict[str, Any]]:
    path = gov_text_db_path(bundle_root)
    if not path.exists():
        return []

    con = connect_db_ro(path)
    try:
        rows = query_with_fallback(
            con,
            """
            SELECT
                td.file_name,
                td.relative_path,
                td.source_kind,
                tc.ordinal,
                snippet(text_chunks_fts, 4, '[', ']', '...', 18) AS snippet
            FROM text_chunks_fts
            INNER JOIN text_chunks AS tc ON tc.id = text_chunks_fts.rowid
            INNER JOIN text_documents AS td ON td.id = tc.document_id
            WHERE text_chunks_fts MATCH ?
            ORDER BY bm25(text_chunks_fts), td.file_name, tc.ordinal
            LIMIT ?;
            """,
            query,
            [max(1, int(limit))],
        )
        return [
            {
                "source": "gov-text",
                "fileName": row["file_name"],
                "relativePath": row["relative_path"],
                "sourceKind": row["source_kind"],
                "ordinal": row["ordinal"],
                "snippet": normalize_snippet(row["snippet"]),
            }
            for row in rows
        ]
    finally:
        con.close()


def search_gov_pdf(bundle_root: Path, query: str, limit: int) -> list[dict[str, Any]]:
    path = gov_pdf_db_path(bundle_root)
    if not path.exists():
        return []

    con = connect_db_ro(path)
    try:
        rows = query_with_fallback(
            con,
            """
            SELECT
                d.file_name,
                d.relative_path,
                d.page_count,
                snippet(document_text_fts, 3, '[', ']', '...', 18) AS snippet
            FROM document_text_fts
            INNER JOIN documents AS d ON d.id = document_text_fts.rowid
            WHERE document_text_fts MATCH ?
            ORDER BY bm25(document_text_fts), d.file_name
            LIMIT ?;
            """,
            query,
            [max(1, int(limit))],
        )
        return [
            {
                "source": "gov-pdf",
                "fileName": row["file_name"],
                "relativePath": row["relative_path"],
                "pageCount": row["page_count"],
                "snippet": normalize_snippet(row["snippet"]),
            }
            for row in rows
        ]
    finally:
        con.close()


def search_bundle(
    bundle_root: Path,
    query: str,
    source: str,
    limit: int,
    law_title_contains: str,
    include_references: bool,
    references_limit: int,
) -> dict[str, Any]:
    query = (query or "").strip()
    if not query:
        return {"ok": False, "msg": "query is empty"}

    src = (source or "all").strip().lower()
    limit = max(1, int(limit))

    if src == "xml-law":
        results = search_xml_law(bundle_root, query, limit, law_title_contains, include_references, references_limit)
    elif src == "gov-text":
        results = search_gov_text(bundle_root, query, limit)
    elif src == "gov-pdf":
        results = search_gov_pdf(bundle_root, query, limit)
    else:
        results: list[dict[str, Any]] = []
        remaining = limit
        for name in ("xml-law", "gov-text", "gov-pdf"):
            if remaining <= 0:
                break
            if name == "xml-law":
                chunk = search_xml_law(
                    bundle_root=bundle_root,
                    query=query,
                    limit=remaining,
                    law_title_contains=law_title_contains,
                    include_references=include_references,
                    references_limit=references_limit,
                )
            elif name == "gov-text":
                chunk = search_gov_text(bundle_root, query, remaining)
            else:
                chunk = search_gov_pdf(bundle_root, query, remaining)
            results.extend(chunk[:remaining])
            remaining = limit - len(results)

    return {
        "ok": True,
        "mode": "search",
        "bundleRoot": str(bundle_root),
        "source": src,
        "query": query,
        "resultCount": len(results),
        "results": results,
    }


def build_arg_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(description="Query the read-only Building Law SQLite bundle.")
    ap.add_argument("--bundle-root", default="", help="Override bundle root directory")
    ap.add_argument("--bundle-info", action="store_true", help="Show bundle/db paths and manifest")
    ap.add_argument("--list-laws", action="store_true", help="List indexed laws from xml_law_index.sqlite")
    ap.add_argument("--query", default="", help="Full-text query")
    ap.add_argument(
        "--source",
        default="all",
        choices=("all", "xml-law", "gov-text", "gov-pdf"),
        help="Search source",
    )
    ap.add_argument("--law-title-contains", default="", help="Optional law title filter for xml-law search")
    ap.add_argument("--include-references", action="store_true", help="Include article references for xml-law hits")
    ap.add_argument("--references-limit", type=int, default=3, help="Maximum references per xml-law result")
    ap.add_argument("--limit", type=int, default=10, help="Maximum results")
    ap.add_argument("--json", action="store_true", help="Reserved for compatibility; output is always JSON")
    return ap


def main() -> int:
    args = build_arg_parser().parse_args()
    bundle_root = Path(args.bundle_root).expanduser() if str(args.bundle_root).strip() else DEFAULT_BUNDLE_ROOT

    if args.bundle_info:
        result = {"ok": True, "mode": "bundle-info", **existing_bundle_info(bundle_root)}
    elif args.list_laws:
        result = list_laws(bundle_root, int(args.limit), args.law_title_contains)
    else:
        result = search_bundle(
            bundle_root=bundle_root,
            query=args.query,
            source=args.source,
            limit=int(args.limit),
            law_title_contains=args.law_title_contains,
            include_references=bool(args.include_references),
            references_limit=int(args.references_limit),
        )

    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result.get("ok") else 1


if __name__ == "__main__":
    raise SystemExit(main())
