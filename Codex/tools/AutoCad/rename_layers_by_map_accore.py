import argparse
import csv
import os
import subprocess
import sys
import tempfile


def read_map(path):
    for enc in ("utf-8", "cp932"):
        try:
            with open(path, "r", encoding=enc) as f:
                rows = []
                for row in csv.reader(f):
                    if not row:
                        continue
                    if row[0].startswith("#"):
                        continue
                    if row[0].lower().startswith("pattern"):
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


def build_script(map_rows):
    lines = []
    lines.append('(setvar "CMDECHO" 0)')
    lines.append('(setvar "FILEDIA" 0)')
    lines.append('(setvar "CMDDIA" 0)')
    lines.append('(setvar "ATTDIA" 0)')
    lines.append('(setvar "EXPERT" 5)')
    lines.append('(setvar "NOMUTT" 1)')
    lines.append('(vl-load-com)')
    lines.append('(setvar "CLAYER" "0")')

    lines.append('(defun _ensure-layer (doc name / layers lay)')
    lines.append('  (if (and name (> (strlen name) 0))')
    lines.append('    (progn')
    lines.append('      (setq layers (vla-get-Layers doc))')
    lines.append('      (setq lay (vl-catch-all-apply \'vla-Item (list layers name)))')
    lines.append('      (if (vl-catch-all-error-p lay)')
    lines.append('        (progn (vla-Add layers name) (setq lay (vla-Item layers name))))')
    lines.append('      (vla-put-Lock lay :vlax-false)')
    lines.append('      (vla-put-Freeze lay :vlax-false)')
    lines.append('      lay)))')

    lines.append('(defun _foreach-entity-in-all-blocks (doc fn)')
    lines.append('  (vlax-for blk (vla-get-Blocks doc) (vlax-for e blk (apply fn (list e)))))')

    lines.append('(defun _move-pattern-to (pat target / doc layers lay name)')
    lines.append('  (setq doc (vla-get-ActiveDocument (vlax-get-Acad-Object)))')
    lines.append('  (_ensure-layer doc target)')
    lines.append('  (_foreach-entity-in-all-blocks doc')
    lines.append('    (function (lambda (ent)')
    lines.append('      (if (and (vlax-property-available-p ent \'Layer)')
    lines.append('               (wcmatch (vla-get-Layer ent) pat))')
    lines.append('        (vl-catch-all-apply \'vla-put-Layer (list ent target))))))')
    lines.append('  (vlax-for lay (vla-get-Layers doc)')
    lines.append('    (setq name (vla-get-Name lay))')
    lines.append('    (if (and name (wcmatch name pat))')
    lines.append('      (vl-catch-all-apply (function (lambda () (vla-Delete lay))))))')
    lines.append('  (princ))')

    for pat, tgt in map_rows:
        lines.append(f'(_move-pattern-to "{pat}" "{tgt}")')

    lines.append('_.QSAVE')
    lines.append('_.QUIT')
    lines.append('(princ)')
    return "\r\n".join(lines)


def main():
    ap = argparse.ArgumentParser(description="Merge/rename DWG layers by map via accoreconsole.")
    ap.add_argument("--dwg", required=True, help="Target DWG path.")
    ap.add_argument("--map-csv", required=True, help="CSV map: pattern,targetLayer")
    ap.add_argument("--accore", default=r"C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe")
    ap.add_argument("--locale", default="ja-JP")
    ap.add_argument("--timeout-sec", type=int, default=300)
    args = ap.parse_args()

    if not os.path.exists(args.dwg):
        raise SystemExit(f"DWG not found: {args.dwg}")
    if not os.path.exists(args.map_csv):
        raise SystemExit(f"Map CSV not found: {args.map_csv}")
    if not os.path.exists(args.accore):
        raise SystemExit(f"accoreconsole not found: {args.accore}")

    rows = read_map(args.map_csv)
    script = build_script(rows)

    with tempfile.TemporaryDirectory(prefix="dwg_map_") as tmpdir:
        scr = os.path.join(tmpdir, "run.scr")
        with open(scr, "w", encoding="cp932", newline="\r\n") as f:
            f.write(script)

        cmd = [args.accore, "/i", args.dwg, "/s", scr, "/l", args.locale]
        proc = subprocess.run(cmd, capture_output=True, encoding="cp932", errors="ignore", timeout=args.timeout_sec, cwd=tmpdir)
        if proc.returncode != 0:
            err = proc.stderr[-2000:] if proc.stderr else proc.stdout[-2000:]
            raise SystemExit(f"accoreconsole failed: {err}")


if __name__ == "__main__":
    main()
