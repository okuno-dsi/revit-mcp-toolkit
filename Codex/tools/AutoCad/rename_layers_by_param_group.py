import argparse
import os
import re
import subprocess
import sys
import tempfile

try:
    import win32com.client
except Exception:
    win32com = None


INVALID_LAYER_CHARS = '<>\\/:"?*|='


def sanitize_layer_name(value):
    if value is None:
        return "Layer"
    text = str(value).strip()
    for ch in INVALID_LAYER_CHARS:
        text = text.replace(ch, "_")
    text = re.sub(r"\s+", " ", text).strip()
    return text if text else "Layer"


def extract_param_value(path, prefix):
    stem = os.path.splitext(os.path.basename(path))[0]
    if prefix and stem.lower().startswith(prefix.lower() + "_"):
        val = stem[len(prefix) + 1:]
    else:
        val = stem
    return re.sub(r"\s+\(\d+\)$", "", val)


def should_skip_layer(layer):
    name = layer.Name
    if not name:
        return True
    if name.lower() in ("0", "defpoints"):
        return True
    if name.startswith("*"):
        return True
    if "|" in name:
        return True
    try:
        if hasattr(layer, "IsDependent") and layer.IsDependent:
            return True
    except Exception:
        pass
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


def merge_layers_to_target(doc, target_name, dry_run=False):
    target = ensure_layer(doc, target_name)
    target_name = target.Name

    layer_names = []
    for i in range(doc.Layers.Count):
        layer = doc.Layers.Item(i)
        if should_skip_layer(layer):
            continue
        if layer.Name.lower() == target_name.lower():
            continue
        layer_names.append(layer.Name)

    total_moved = 0
    for name in layer_names:
        if dry_run:
            print(f"[DRY] merge {name} -> {target_name}")
            continue
        moved = move_entities_to_layer(doc, name, target_name)
        total_moved += moved
        try:
            doc.Layers.Item(name).Delete()
        except Exception:
            pass

    if not dry_run:
        try:
            doc.Regen(1)
        except Exception:
            pass
    return total_moved, len(layer_names)


def iter_dwg_paths(args):
    if args.dwg:
        yield os.path.abspath(args.dwg)
        return
    prefix = args.file_prefix or ""
    pattern = args.pattern or f"{prefix}_*.dwg"
    for name in sorted(os.listdir(args.dir)):
        if not name.lower().endswith(".dwg"):
            continue
        if pattern != "*.dwg" and not fnmatch(name, pattern):
            continue
        yield os.path.abspath(os.path.join(args.dir, name))


def fnmatch(name, pattern):
    regex = "^" + re.escape(pattern).replace("\\*", ".*").replace("\\?", ".") + "$"
    return re.match(regex, name, re.IGNORECASE) is not None


def process_dwg(acad, path, category, param_name, param_value, dry_run=False):
    if not os.path.exists(path):
        print(f"[SKIP] missing: {path}")
        return False
    target = f"CAT_{category}__PARAM_{param_name}__VAL_{param_value}"
    target = sanitize_layer_name(target)

    doc = acad.Documents.Open(path)
    try:
        moved, layer_count = merge_layers_to_target(doc, target, dry_run=dry_run)
        if dry_run:
            print(f"[DRY] {os.path.basename(path)} -> {target} (layers={layer_count})")
        else:
            doc.Save()
            print(f"[OK] {os.path.basename(path)} -> {target} (layers={layer_count}, moved={moved})")
        return True
    finally:
        try:
            doc.Close(False)
        except Exception:
            pass


def run_accore(path, target, accore_path, locale, timeout_sec, dry_run=False):
    if dry_run:
        print(f"[DRY] accore rename: {os.path.basename(path)} -> {target}")
        return True

    if not accore_path or not os.path.exists(accore_path):
        raise RuntimeError(f"accoreconsole not found: {accore_path}")

    script_lines = []
    script_lines.append('(setvar "CMDECHO" 0)')
    script_lines.append('(setvar "FILEDIA" 0)')
    script_lines.append('(setvar "CMDDIA" 0)')
    script_lines.append('(setvar "ATTDIA" 0)')
    script_lines.append('(setvar "EXPERT" 5)')
    script_lines.append('(setvar "NOMUTT" 1)')
    script_lines.append('(vl-load-com)')

    script_lines.append('(defun _ensure-layer (doc name / layers lay)')
    script_lines.append('  (if (and name (> (strlen name) 0))')
    script_lines.append('    (progn')
    script_lines.append('      (setq layers (vla-get-Layers doc))')
    script_lines.append('      (setq lay (vl-catch-all-apply \'vla-Item (list layers name)))')
    script_lines.append('      (if (vl-catch-all-error-p lay)')
    script_lines.append('        (progn (vla-Add layers name) (setq lay (vla-Item layers name))))')
    script_lines.append('      (vla-put-Lock lay :vlax-false)')
    script_lines.append('      (vla-put-Freeze lay :vlax-false)')
    script_lines.append('      lay)))')

    script_lines.append('(defun _foreach-entity-in-all-blocks (doc fn)')
    script_lines.append('  (vlax-for blk (vla-get-Blocks doc) (vlax-for e blk (apply fn (list e)))))')

    script_lines.append('(defun merge-all-to-target (dst / doc layers lay name up)')
    script_lines.append('  (setq doc (vla-get-ActiveDocument (vlax-get-Acad-Object)))')
    script_lines.append('  (_ensure-layer doc dst)')
    script_lines.append('  (setq layers (vla-get-Layers doc))')
    script_lines.append('  (vlax-for lay layers')
    script_lines.append('    (setq name (vla-get-Name lay))')
    script_lines.append('    (setq up (if name (strcase name) ""))')
    script_lines.append('    (if (and name')
    script_lines.append('             (/= up "0")')
    script_lines.append('             (/= up "DEFPOINTS")')
    script_lines.append('             (/= up (strcase dst))')
    script_lines.append('             (not (wcmatch name "*|*"))')
    script_lines.append('             (not (wcmatch name "`**")))')
    script_lines.append('      (progn')
    script_lines.append('        (_foreach-entity-in-all-blocks doc')
    script_lines.append('          (function (lambda (ent)')
    script_lines.append('            (if (and (vlax-property-available-p ent \'Layer)')
    script_lines.append('                     (= (strcase (vla-get-Layer ent)) (strcase name)))')
    script_lines.append('                (vl-catch-all-apply \'vla-put-Layer (list ent dst))))))')
    script_lines.append('        (vl-catch-all-apply (function (lambda () (vla-Delete lay))))')
    script_lines.append('      )')
    script_lines.append('    )')
    script_lines.append('  )')
    script_lines.append('  (princ))')

    script_lines.append(f'(merge-all-to-target "{target}")')
    script_lines.append('_.QSAVE')
    script_lines.append('_.QUIT')
    script_lines.append('(princ)')

    with tempfile.TemporaryDirectory(prefix="cad_layers_") as tmpdir:
        scr_path = os.path.join(tmpdir, "run.scr")
        with open(scr_path, "w", encoding="cp932", newline="\r\n") as f:
            f.write("\r\n".join(script_lines))

        cmd = [accore_path, "/i", path, "/s", scr_path, "/l", locale]
        proc = subprocess.run(
            cmd,
            capture_output=True,
            encoding="cp932",
            errors="ignore",
            timeout=timeout_sec,
            cwd=tmpdir,
        )
        if proc.returncode != 0:
            raise RuntimeError(f"accoreconsole failed: {proc.stderr[-2000:] if proc.stderr else proc.stdout[-2000:]}")
    return True


def main():
    ap = argparse.ArgumentParser(
        description="Rename/merge layers in DWG files by export_dwg_by_param_groups metadata."
    )
    ap.add_argument("--dwg", help="Single DWG path.")
    ap.add_argument("--dir", help="Folder containing DWG files.")
    ap.add_argument("--pattern", help="Filename pattern (default: <prefix>_*.dwg).")
    ap.add_argument("--file-prefix", dest="file_prefix", help="DWG prefix from export_dwg_by_param_groups.")
    ap.add_argument("--category", required=True, help="Category name.")
    ap.add_argument("--param-name", dest="param_name", required=True, help="Parameter name.")
    ap.add_argument("--param-value", dest="param_value", help="Override param value for single DWG.")
    ap.add_argument("--dry-run", action="store_true", help="Show actions without modifying files.")
    ap.add_argument("--force-accore", action="store_true", help="Use accoreconsole instead of COM.")
    ap.add_argument("--accore", default=r"C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe", help="accoreconsole.exe path.")
    ap.add_argument("--locale", default="ja-JP", help="accoreconsole locale.")
    ap.add_argument("--timeout-sec", type=int, default=300, help="accoreconsole timeout in seconds.")

    args = ap.parse_args()
    if not args.dwg and not args.dir:
        ap.error("Specify --dwg or --dir.")
    if args.dir and not args.file_prefix and not args.pattern:
        ap.error("--dir requires --file-prefix or --pattern to identify DWGs.")

    if args.force_accore or win32com is None:
        acad = None
    else:
        try:
            acad = win32com.client.gencache.EnsureDispatch("AutoCAD.Application")
        except Exception:
            try:
                acad = win32com.client.GetActiveObject("AutoCAD.Application")
            except Exception as exc:
                print("WARN: AutoCAD COM unavailable; falling back to accoreconsole.")
                print(f"Details: {exc}")
                acad = None

    if args.dwg:
        value = args.param_value or extract_param_value(args.dwg, args.file_prefix)
        if acad is None:
            run_accore(
                os.path.abspath(args.dwg),
                sanitize_layer_name(f"CAT_{args.category}__PARAM_{args.param_name}__VAL_{value}"),
                args.accore,
                args.locale,
                args.timeout_sec,
                dry_run=args.dry_run,
            )
        else:
            try:
                process_dwg(acad, os.path.abspath(args.dwg), args.category, args.param_name, value, dry_run=args.dry_run)
            except Exception:
                run_accore(
                    os.path.abspath(args.dwg),
                    sanitize_layer_name(f"CAT_{args.category}__PARAM_{args.param_name}__VAL_{value}"),
                    args.accore,
                    args.locale,
                    args.timeout_sec,
                    dry_run=args.dry_run,
                )
        return

    for path in iter_dwg_paths(args):
        value = args.param_value or extract_param_value(path, args.file_prefix)
        if acad is None:
            run_accore(
                path,
                sanitize_layer_name(f"CAT_{args.category}__PARAM_{args.param_name}__VAL_{value}"),
                args.accore,
                args.locale,
                args.timeout_sec,
                dry_run=args.dry_run,
            )
        else:
            try:
                process_dwg(acad, path, args.category, args.param_name, value, dry_run=args.dry_run)
            except Exception:
                run_accore(
                    path,
                    sanitize_layer_name(f"CAT_{args.category}__PARAM_{args.param_name}__VAL_{value}"),
                    args.accore,
                    args.locale,
                    args.timeout_sec,
                    dry_run=args.dry_run,
                )


if __name__ == "__main__":
    main()
