import argparse
import csv
import json
import shutil
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Tuple


def _timestamp() -> str:
    return time.strftime("%Y%m%d_%H%M%S")


def _read_csv(path: Path) -> Tuple[List[str], List[List[str]]]:
    with path.open("r", encoding="utf-8-sig", newline="") as f:
        r = csv.reader(f)
        header = next(r, [])
        rows = list(r)
    return header, rows


def _write_csv(path: Path, header: List[str], rows: List[List[Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(header)
        for row in rows:
            w.writerow(list(row))


def _normalize_header(header: List[str]) -> List[str]:
    # Remove trailing empty columns
    h = list(header)
    while h and not (h[-1] or "").strip():
        h.pop()
    # Ensure we have a note column
    if "note" not in h:
        h.append("note")
    return h


def _row_to_dict(header: List[str], row: List[str]) -> Dict[str, str]:
    d: Dict[str, str] = {}
    for i, key in enumerate(header):
        if i < len(row):
            d[key] = row[i]
        else:
            d[key] = ""
    return d


def _merge_notes(header: List[str], row: List[str]) -> str:
    # Merge note-like columns:
    # - if 'note' exists: use it
    # - also merge any extra columns beyond known schema (often Excel adds blank header cols)
    note_parts: List[str] = []
    try:
        note_idx = header.index("note")
    except ValueError:
        note_idx = -1
    if note_idx >= 0 and note_idx < len(row):
        v = (row[note_idx] or "").strip()
        if v:
            note_parts.append(v)

    # Merge extra columns (even if header is empty)
    for i in range(len(header), len(row)):
        v = (row[i] or "").strip()
        if v:
            note_parts.append(v)

    # Also merge any blank-header columns that still exist in header list
    for i, h in enumerate(header):
        if (h or "").strip():
            continue
        if i < len(row):
            v = (row[i] or "").strip()
            if v:
                note_parts.append(v)

    # De-dup while preserving order
    seen = set()
    uniq: List[str] = []
    for p in note_parts:
        if p in seen:
            continue
        seen.add(p)
        uniq.append(p)
    return " | ".join(uniq)


def normalize_and_split(
    src: Path,
    *,
    split_key: str,
    out_dir: Path,
    base_name: str,
    copy_to: Path | None = None,
) -> Dict[str, Any]:
    header_raw, rows_raw = _read_csv(src)
    header_norm = _normalize_header(header_raw)

    # Map original rows into normalized dict form, merge notes
    out_rows: List[Dict[str, str]] = []
    for row in rows_raw:
        d = _row_to_dict(header_raw, row)
        d["note"] = _merge_notes(header_raw, row)
        out_rows.append(d)

    # Split
    groups: Dict[str, List[Dict[str, str]]] = {}
    for d in out_rows:
        k = (d.get(split_key) or "").strip() or "UNKNOWN"
        groups.setdefault(k, []).append(d)

    written: Dict[str, str] = {}
    out_dir.mkdir(parents=True, exist_ok=True)
    for k, items in groups.items():
        safe = "".join(c if c.isalnum() or c in ("-", "_") else "_" for c in k)
        out_path = out_dir / f"{base_name}_{safe}.csv"
        # Emit standard schema header (first 12 known cols + note)
        std_header = [
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
        csv_rows = [[it.get(h, "") for h in std_header] for it in items]
        _write_csv(out_path, std_header, csv_rows)
        written[k] = str(out_path)

        if copy_to is not None:
            copy_to.mkdir(parents=True, exist_ok=True)
            shutil.copy2(out_path, copy_to / out_path.name)

    # Save summary json
    summary = {
        "ok": True,
        "src": str(src),
        "splitKey": split_key,
        "written": written,
    }
    (out_dir / f"{base_name}_split_summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    return summary


def main(argv: List[str]) -> int:
    ap = argparse.ArgumentParser(description="Normalize a type-parameter CSV (merge note columns) and split into separate CSVs.")
    ap.add_argument("--src", type=str, required=True, help="Source CSV path")
    ap.add_argument("--split-key", type=str, default="kind", help="Column name to split by (default: kind)")
    ap.add_argument("--out-dir", type=str, required=True, help="Output directory")
    ap.add_argument("--base-name", type=str, default=f"split_{_timestamp()}", help="Base output filename prefix")
    ap.add_argument("--copy-to", type=str, default="", help="Optional second directory to copy split files into")
    args = ap.parse_args(argv)

    src = Path(args.src)
    if not src.exists():
        print(json.dumps({"ok": False, "msg": f"src not found: {src}"}, ensure_ascii=False))
        return 1

    out_dir = Path(args.out_dir)
    copy_to = Path(args.copy_to) if args.copy_to.strip() else None
    summary = normalize_and_split(src, split_key=args.split_key, out_dir=out_dir, base_name=args.base_name, copy_to=copy_to)
    print(json.dumps(summary, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

