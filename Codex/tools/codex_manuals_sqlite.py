#!/usr/bin/env python3
# -*- coding: utf-8 -*-
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import re
import sqlite3
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, List, Sequence


SCRIPT_DIR = Path(__file__).resolve().parent
CODEX_ROOT = SCRIPT_DIR.parent
DEFAULT_EXTS = (".md", ".txt")
MAX_CHUNK_CHARS = 1800


def detect_default_manual_root() -> Path:
    candidates = [
        CODEX_ROOT / "Manuals",
        CODEX_ROOT.parent / "Docs" / "Manuals",
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return candidates[0]


def detect_default_db_path() -> Path:
    if (CODEX_ROOT.parent / "Docs" / "Manuals").exists():
        return CODEX_ROOT.parent / "SQLite" / "Manuals" / "codex_manuals.sqlite"
    return CODEX_ROOT / "Work" / "sqlite" / "codex_manuals.sqlite"


DEFAULT_ROOT = detect_default_manual_root()
DEFAULT_DB_PATH = detect_default_db_path()


@dataclass
class Chunk:
    chunk_index: int
    heading_path: str
    content: str
    line_start: int
    line_end: int


def utc_now_text() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat()


def sha1_text(text: str) -> str:
    return hashlib.sha1(text.encode("utf-8", errors="replace")).hexdigest()


def sha1_file(path: Path) -> str:
    h = hashlib.sha1()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def ensure_parent(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)


def connect_db(path: Path) -> sqlite3.Connection:
    ensure_parent(path)
    con = sqlite3.connect(str(path))
    con.row_factory = sqlite3.Row
    con.execute("PRAGMA journal_mode=WAL;")
    con.execute("PRAGMA synchronous=NORMAL;")
    con.execute("PRAGMA foreign_keys=ON;")
    return con


def create_schema(con: sqlite3.Connection) -> bool:
    con.executescript(
        """
        CREATE TABLE IF NOT EXISTS documents (
            doc_id INTEGER PRIMARY KEY AUTOINCREMENT,
            relative_path TEXT NOT NULL UNIQUE,
            abs_path TEXT NOT NULL,
            file_name TEXT NOT NULL,
            ext TEXT NOT NULL,
            size_bytes INTEGER NOT NULL,
            mtime_utc TEXT NOT NULL,
            file_sha1 TEXT NOT NULL,
            indexed_at_utc TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS chunks (
            chunk_id INTEGER PRIMARY KEY AUTOINCREMENT,
            doc_id INTEGER NOT NULL REFERENCES documents(doc_id) ON DELETE CASCADE,
            chunk_index INTEGER NOT NULL,
            heading_path TEXT NOT NULL,
            content TEXT NOT NULL,
            content_sha1 TEXT NOT NULL,
            line_start INTEGER NOT NULL,
            line_end INTEGER NOT NULL,
            UNIQUE(doc_id, chunk_index)
        );

        CREATE INDEX IF NOT EXISTS idx_chunks_doc_id ON chunks(doc_id);
        CREATE INDEX IF NOT EXISTS idx_chunks_heading_path ON chunks(heading_path);
        CREATE INDEX IF NOT EXISTS idx_documents_file_name ON documents(file_name);
        """
    )

    fts_enabled = True
    try:
        con.execute(
            """
            CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts
            USING fts5(
                content,
                heading_path,
                relative_path,
                tokenize='unicode61 remove_diacritics 2'
            );
            """
        )
    except sqlite3.OperationalError:
        fts_enabled = False
    return fts_enabled


def rebuild_fts(con: sqlite3.Connection) -> None:
    if not table_exists(con, "chunks_fts"):
        return
    con.execute("DELETE FROM chunks_fts;")
    con.execute(
        """
        INSERT INTO chunks_fts(rowid, content, heading_path, relative_path)
        SELECT c.chunk_id, c.content, c.heading_path, d.relative_path
        FROM chunks AS c
        INNER JOIN documents AS d ON d.doc_id = c.doc_id;
        """
    )


def table_exists(con: sqlite3.Connection, name: str) -> bool:
    row = con.execute(
        "SELECT 1 FROM sqlite_master WHERE type IN ('table','view') AND name = ?;",
        (name,),
    ).fetchone()
    return row is not None


def discover_files(root: Path, exts: Sequence[str]) -> List[Path]:
    allow = {ext.lower() for ext in exts}
    files: List[Path] = []
    for path in root.rglob("*"):
        if not path.is_file():
            continue
        if path.suffix.lower() not in allow:
            continue
        files.append(path)
    files.sort(key=lambda p: str(p.relative_to(root)).lower())
    return files


def read_text_file(path: Path) -> str:
    encodings = ("utf-8", "utf-8-sig", "cp932", "utf-16")
    for enc in encodings:
        try:
            return path.read_text(encoding=enc)
        except Exception:
            continue
    return path.read_text(encoding="utf-8", errors="replace")


def normalized_join(lines: Iterable[str]) -> str:
    text = "\n".join(lines).strip()
    text = re.sub(r"\n{3,}", "\n\n", text)
    return text.strip()


def flush_paragraph_blocks(
    blocks: List[tuple[int, str]],
    heading_path: str,
    chunks: List[Chunk],
) -> None:
    if not blocks:
        return

    current_lines: List[str] = []
    current_start = blocks[0][0]
    current_end = blocks[0][0]

    def emit() -> None:
        nonlocal current_lines, current_start, current_end
        content = normalized_join(current_lines)
        if not content:
            return
        chunks.append(
            Chunk(
                chunk_index=len(chunks),
                heading_path=heading_path,
                content=content,
                line_start=current_start,
                line_end=current_end,
            )
        )
        current_lines = []

    for line_no, text in blocks:
        candidate = normalized_join(current_lines + [text]) if current_lines else text.strip()
        if current_lines and len(candidate) > MAX_CHUNK_CHARS:
            emit()
            current_start = line_no
        if not current_lines:
            current_start = line_no
        current_lines.append(text)
        current_end = line_no

    emit()


def chunk_markdown(text: str, default_heading: str) -> List[Chunk]:
    lines = text.splitlines()
    chunks: List[Chunk] = []
    heading_stack: List[str] = []
    paragraph: List[tuple[int, str]] = []

    def heading_path() -> str:
        return " > ".join(heading_stack) if heading_stack else default_heading

    def flush_paragraph() -> None:
        nonlocal paragraph
        flush_paragraph_blocks(paragraph, heading_path(), chunks)
        paragraph = []

    for line_no, line in enumerate(lines, start=1):
        m = re.match(r"^(#{1,6})\s+(.*\S)\s*$", line)
        if m:
            flush_paragraph()
            level = len(m.group(1))
            title = m.group(2).strip()
            heading_stack[:] = heading_stack[: level - 1]
            heading_stack.append(title)
            continue

        if not line.strip():
            flush_paragraph()
            continue

        paragraph.append((line_no, line.rstrip()))

    flush_paragraph()

    if chunks:
        return chunks

    raw = normalized_join(lines)
    if not raw:
        return []
    return [
        Chunk(
            chunk_index=0,
            heading_path=default_heading,
            content=raw,
            line_start=1,
            line_end=max(1, len(lines)),
        )
    ]


def chunk_plain_text(text: str, default_heading: str) -> List[Chunk]:
    lines = text.splitlines()
    chunks: List[Chunk] = []
    paragraph: List[tuple[int, str]] = []

    def flush_paragraph() -> None:
        nonlocal paragraph
        flush_paragraph_blocks(paragraph, default_heading, chunks)
        paragraph = []

    for line_no, line in enumerate(lines, start=1):
        if not line.strip():
            flush_paragraph()
            continue
        paragraph.append((line_no, line.rstrip()))

    flush_paragraph()
    if chunks:
        return chunks

    raw = normalized_join(lines)
    if not raw:
        return []
    return [
        Chunk(
            chunk_index=0,
            heading_path=default_heading,
            content=raw,
            line_start=1,
            line_end=max(1, len(lines)),
        )
    ]


def chunk_file(path: Path, root: Path) -> List[Chunk]:
    text = read_text_file(path)
    default_heading = str(path.relative_to(root)).replace("\\", "/")
    if path.suffix.lower() == ".md":
        return chunk_markdown(text, default_heading)
    return chunk_plain_text(text, default_heading)


def reset_database(con: sqlite3.Connection) -> None:
    con.executescript(
        """
        DELETE FROM chunks;
        DELETE FROM documents;
        """
    )
    if table_exists(con, "chunks_fts"):
        con.execute("DELETE FROM chunks_fts;")


def upsert_document(con: sqlite3.Connection, root: Path, path: Path, indexed_at_utc: str) -> int:
    relative_path = str(path.relative_to(root)).replace("\\", "/")
    stat = path.stat()
    file_sha1 = sha1_file(path)
    row = con.execute(
        "SELECT doc_id FROM documents WHERE relative_path = ?;",
        (relative_path,),
    ).fetchone()

    if row is None:
        cur = con.execute(
            """
            INSERT INTO documents(
                relative_path, abs_path, file_name, ext, size_bytes, mtime_utc, file_sha1, indexed_at_utc
            )
            VALUES(?, ?, ?, ?, ?, ?, ?, ?);
            """,
            (
                relative_path,
                str(path.resolve()),
                path.name,
                path.suffix.lower(),
                int(stat.st_size),
                dt.datetime.fromtimestamp(stat.st_mtime, tz=dt.timezone.utc).replace(microsecond=0).isoformat(),
                file_sha1,
                indexed_at_utc,
            ),
        )
        return int(cur.lastrowid)

    doc_id = int(row["doc_id"])
    con.execute(
        """
        UPDATE documents
        SET abs_path = ?,
            file_name = ?,
            ext = ?,
            size_bytes = ?,
            mtime_utc = ?,
            file_sha1 = ?,
            indexed_at_utc = ?
        WHERE doc_id = ?;
        """,
        (
            str(path.resolve()),
            path.name,
            path.suffix.lower(),
            int(stat.st_size),
            dt.datetime.fromtimestamp(stat.st_mtime, tz=dt.timezone.utc).replace(microsecond=0).isoformat(),
            file_sha1,
            indexed_at_utc,
            doc_id,
        ),
    )
    con.execute("DELETE FROM chunks WHERE doc_id = ?;", (doc_id,))
    return doc_id


def insert_chunks(con: sqlite3.Connection, doc_id: int, chunks: Sequence[Chunk]) -> None:
    con.executemany(
        """
        INSERT INTO chunks(
            doc_id, chunk_index, heading_path, content, content_sha1, line_start, line_end
        )
        VALUES(?, ?, ?, ?, ?, ?, ?);
        """,
        [
            (
                doc_id,
                chunk.chunk_index,
                chunk.heading_path,
                chunk.content,
                sha1_text(chunk.content),
                chunk.line_start,
                chunk.line_end,
            )
            for chunk in chunks
        ],
    )


def run_index(root: Path, db_path: Path, exts: Sequence[str], rebuild: bool) -> dict:
    if not root.exists():
        raise SystemExit(f"Root not found: {root}")

    indexed_at_utc = utc_now_text()
    con = connect_db(db_path)
    try:
        fts_enabled = create_schema(con)
        if rebuild:
            reset_database(con)

        files = discover_files(root, exts)
        current_relative_paths = {str(path.relative_to(root)).replace("\\", "/") for path in files}
        doc_count = 0
        chunk_count = 0
        for path in files:
            doc_id = upsert_document(con, root, path, indexed_at_utc)
            chunks = chunk_file(path, root)
            insert_chunks(con, doc_id, chunks)
            doc_count += 1
            chunk_count += len(chunks)

        if current_relative_paths:
            placeholders = ",".join("?" for _ in current_relative_paths)
            con.execute(
                f"DELETE FROM documents WHERE relative_path NOT IN ({placeholders});",
                tuple(sorted(current_relative_paths)),
            )

        rebuild_fts(con)
        con.commit()

        return {
            "ok": True,
            "mode": "index",
            "root": str(root),
            "dbPath": str(db_path),
            "documentCount": doc_count,
            "chunkCount": chunk_count,
            "ftsEnabled": fts_enabled,
            "indexedAtUtc": indexed_at_utc,
            "extensions": list(exts),
        }
    finally:
        con.close()


def query_manuals(db_path: Path, query_text: str, limit: int) -> dict:
    if not db_path.exists():
        raise SystemExit(f"Database not found: {db_path}")

    con = connect_db(db_path)
    try:
        results = []
        fts_enabled = table_exists(con, "chunks_fts")

        if fts_enabled:
            try:
                rows = con.execute(
                    """
                    SELECT
                        c.chunk_id,
                        d.relative_path,
                        c.heading_path,
                        c.line_start,
                        c.line_end,
                        snippet(chunks_fts, 0, '[', ']', ' ... ', 24) AS snippet,
                        bm25(chunks_fts) AS score
                    FROM chunks_fts
                    INNER JOIN chunks AS c ON c.chunk_id = chunks_fts.rowid
                    INNER JOIN documents AS d ON d.doc_id = c.doc_id
                    WHERE chunks_fts MATCH ?
                    ORDER BY score, d.relative_path, c.chunk_index
                    LIMIT ?;
                    """,
                    (query_text, int(limit)),
                ).fetchall()
                results = [dict(row) for row in rows]
            except sqlite3.OperationalError:
                results = []

        if not results:
            like = f"%{query_text}%"
            rows = con.execute(
                """
                SELECT
                    c.chunk_id,
                    d.relative_path,
                    c.heading_path,
                    c.line_start,
                    c.line_end,
                    substr(c.content, 1, 280) AS snippet,
                    NULL AS score
                FROM chunks AS c
                INNER JOIN documents AS d ON d.doc_id = c.doc_id
                WHERE c.content LIKE ?
                   OR c.heading_path LIKE ?
                   OR d.relative_path LIKE ?
                ORDER BY d.relative_path, c.chunk_index
                LIMIT ?;
                """,
                (like, like, like, int(limit)),
            ).fetchall()
            results = [dict(row) for row in rows]

        return {
            "ok": True,
            "mode": "query",
            "dbPath": str(db_path),
            "query": query_text,
            "resultCount": len(results),
            "results": results,
        }
    finally:
        con.close()


def build_arg_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(
        description="Index Codex manuals into SQLite, or query the indexed database."
    )
    ap.add_argument("--root", default=str(DEFAULT_ROOT), help="Manual root folder")
    ap.add_argument("--db-path", default=str(DEFAULT_DB_PATH), help="SQLite database path")
    ap.add_argument(
        "--ext",
        action="append",
        dest="exts",
        help="File extension to index. Repeatable. Default: .md, .txt",
    )
    ap.add_argument("--rebuild", action="store_true", help="Clear existing indexed rows first")
    ap.add_argument("--query", default="", help="Query the database instead of indexing")
    ap.add_argument("--limit", type=int, default=20, help="Query result limit")
    return ap


def main() -> int:
    args = build_arg_parser().parse_args()
    root = Path(args.root).expanduser()
    db_path = Path(args.db_path).expanduser()
    exts = tuple(args.exts) if args.exts else DEFAULT_EXTS

    if (args.query or "").strip():
        result = query_manuals(db_path, args.query.strip(), max(1, int(args.limit)))
    else:
        result = run_index(root, db_path, exts, bool(args.rebuild))

    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result.get("ok") else 1


if __name__ == "__main__":
    raise SystemExit(main())
