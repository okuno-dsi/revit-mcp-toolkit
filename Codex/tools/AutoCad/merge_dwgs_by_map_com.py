import argparse
import csv
import fnmatch
import glob
import os
import sys

try:
    import pythoncom
    import win32com.client
except Exception as exc:
    win32com = None
    pythoncom = None


INVALID_LAYER_CHARS = '<>\\/:"?*|='


def get_point(x, y, z):
    return win32com.client.VARIANT(pythoncom.VT_ARRAY | pythoncom.VT_R8, (x, y, z))


def sanitize_layer_name(name):
    text = str(name or "").strip()
    for ch in INVALID_LAYER_CHARS:
        text = text.replace(ch, "_")
    return text if text else "Layer"


def read_map_csv(path):
    for enc in ("utf-8", "cp932"):
        try:
            rows = []
            with open(path, "r", encoding=enc) as f:
                for row in csv.reader(f):
                    if not row:
                        continue
                    head = row[0].strip()
                    if not head or head.startswith("#") or head.lower().startswith("pattern"):
                        continue
                    if len(row) < 2:
                        continue
                    pat = row[0].strip()
                    tgt = row[1].strip()
                    if pat and tgt:
                        rows.append((pat, tgt))
            if rows:
                return rows
        except Exception:
            continue
    raise RuntimeError(f"Failed to read map CSV: {path}")


def should_skip_layer(name):
    if not name:
        return True
    low = name.lower()
    if low in ("0", "defpoints"):
        return True
    if name.startswith("*") or "|" in name:
        return True
    return False


def ensure_layer(doc, name):
    layers = doc.Layers
    try:
        layer = layers.Item(name)
    except Exception:
        layer = layers.Add(name)
    try:
        layer.Lock = False
        layer.Freeze = False
    except Exception:
        pass
    return layer


def move_entities_to_layer(doc, from_name, to_name):
    moved = 0
    for blk in doc.Blocks:
        for ent in blk:
            try:
                if hasattr(ent, "Layer") and ent.Layer.lower() == from_name.lower():
                    ent.Layer = to_name
                    moved += 1
            except Exception:
                continue
    return moved


def apply_layer_map(doc, map_rows):
    total_moves = 0
    total_layers = 0
    for pat, tgt in map_rows:
        target = ensure_layer(doc, sanitize_layer_name(tgt)).Name
        matches = []
        for i in range(doc.Layers.Count):
            layer = doc.Layers.Item(i)
            name = layer.Name
            if should_skip_layer(name):
                continue
            if name.lower() == target.lower():
                continue
            if fnmatch.fnmatchcase(name.lower(), pat.lower()):
                matches.append(name)
        for name in matches:
            total_layers += 1
            total_moves += move_entities_to_layer(doc, name, target)
            try:
                doc.Layers.Item(name).Delete()
            except Exception:
                pass
    return total_layers, total_moves


def collect_dwgs(source_dir, pattern, out_name, seed_name):
    dwgs = sorted(glob.glob(os.path.join(source_dir, pattern)))
    if out_name:
        dwgs = [p for p in dwgs if os.path.basename(p).lower() != out_name.lower()]
    if seed_name:
        dwgs = [p for p in dwgs if os.path.basename(p).lower() != seed_name.lower()]
    return dwgs


def main():
    ap = argparse.ArgumentParser(description="Merge DWGs via AutoCAD COM and apply layer map.")
    ap.add_argument("--source-dir", required=True, help="Folder containing DWG files.")
    ap.add_argument("--pattern", default="*.dwg", help="DWG filename pattern (default: *.dwg).")
    ap.add_argument("--out-dwg", required=True, help="Output merged DWG path.")
    ap.add_argument("--seed-dwg", help="Optional seed DWG to open as base.")
    ap.add_argument("--map-csv", help="CSV map: pattern,targetLayer (optional).")
    ap.add_argument("--visible", action="store_true", help="Show AutoCAD window.")
    args = ap.parse_args()

    if win32com is None or pythoncom is None:
        raise SystemExit("pywin32 is required for COM-based merge.")

    source_dir = os.path.abspath(args.source_dir)
    if not os.path.isdir(source_dir):
        raise SystemExit(f"Source folder not found: {source_dir}")

    out_abs = os.path.abspath(args.out_dwg)
    out_name = os.path.basename(out_abs)
    seed_abs = os.path.abspath(args.seed_dwg) if args.seed_dwg else None
    seed_name = os.path.basename(seed_abs) if seed_abs else None

    dwgs = collect_dwgs(source_dir, args.pattern, out_name, seed_name)
    if not dwgs:
        raise SystemExit("No DWG files found to merge.")

    map_csv = args.map_csv
    if not map_csv:
        candidate = os.path.join(source_dir, "layermap.csv")
        if os.path.exists(candidate):
            map_csv = candidate

    map_rows = read_map_csv(map_csv) if map_csv else []

    pythoncom.CoInitialize()
    try:
        try:
            acad = win32com.client.GetActiveObject("AutoCAD.Application")
        except Exception:
            acad = win32com.client.Dispatch("AutoCAD.Application")
        if args.visible:
            acad.Visible = True

        if seed_abs:
            doc = acad.Documents.Open(seed_abs)
        else:
            doc = acad.Documents.Add("")
        msp = doc.ModelSpace
        insertion_point = get_point(0.0, 0.0, 0.0)

        for path in dwgs:
            block_ref = msp.InsertBlock(insertion_point, os.path.abspath(path), 1.0, 1.0, 1.0, 0.0)
            block_ref.Explode()
            block_ref.Delete()

        if map_rows:
            layer_count, moved = apply_layer_map(doc, map_rows)
            print(f"[OK] layer map applied: layers={layer_count}, moved={moved}")
        else:
            print("[OK] merge completed (no layer map).")

        if os.path.exists(out_abs):
            try:
                os.remove(out_abs)
            except Exception:
                pass
        doc.SaveAs(out_abs)
        print(f"[OK] saved: {out_abs}")
    finally:
        pythoncom.CoUninitialize()


if __name__ == "__main__":
    main()
