"""
各ビューの DWG を DXF に変換しつつ、
すべてのレイヤ名に「ビュー名サフィックス」を付け足すユーティリティです。

想定フロー:
    1) RevitMCP の `export_dwg` で、ビューごとに DWG を出力
       - ファイル名 = ビュー名（あるいはビュー名に準じた識別子）
    2) 本スクリプトを実行
       - 各 DWG を開き、全レイヤ（0 / DEFPOINTS を除く）を
         `<元レイヤ名>_<ビュー名サフィックス>` にリネーム
       - その状態で DXF(2018) を書き出す

使い方の例:

    cd %USERPROFILE%\\Documents\\Revit_MCP\\Codex
    python Scripts/Reference/dwg_to_dxf_with_view_suffix.py ^
        --input-dir Projects/DWG_4F_Walls ^
        --pattern *.dwg ^
        --out-dir Projects/DWG_4F_Walls/DXF

デフォルトではファイル名の stem（拡張子を除いた部分）を
そのままビュー名サフィックスとして使います。
"""

from __future__ import annotations

import argparse
import json
import subprocess
from datetime import datetime
from pathlib import Path
from typing import Iterable


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument(
        "--input-dir",
        required=True,
        help="DWG が置いてあるフォルダ",
    )
    p.add_argument(
        "--pattern",
        default="*.dwg",
        help="対象 DWG のパターン（既定: *.dwg）",
    )
    p.add_argument(
        "--out-dir",
        default=None,
        help="DXF の出力先フォルダ（省略時は input-dir と同じ）",
    )
    p.add_argument(
        "--accore",
        default=r"C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe",
        help="accoreconsole.exe のパス（既定: 2026）",
    )
    p.add_argument(
        "--locale",
        default="ja-JP",
        help="CoreConsole のロケール（既定: ja-JP）",
    )
    return p.parse_args()


def sanitize_view_suffix(stem: str) -> str:
    """
    ビュー名（stem）をレイヤ名サフィックスとして安全に使える形に整形。
    - 英数字と - _ 以外はアンダースコアに置き換え。
    - 先頭・末尾のスペース/アンダースコアはトリム。
    """
    safe_chars = []
    for ch in stem.strip():
        code = ord(ch)
        if (
            48 <= code <= 57  # 0-9
            or 65 <= code <= 90  # A-Z
            or 97 <= code <= 122  # a-z
            or ch in "-_"
        ):
            safe_chars.append(ch)
        else:
            safe_chars.append("_")
    s = "".join(safe_chars).strip("_")
    return s or "VIEW"


def build_script(view_suffix: str, out_dxf: Path) -> str:
    """
    単一の DWG を開いて:
      - 全レイヤ名を `<old>_<view_suffix>` にリネーム
      - DXF(2018) で保存
    する .scr の内容を返す。
    """
    lines: list[str] = []

    lines.append("._CMDECHO 0")
    lines.append("._FILEDIA 0")
    lines.append("._CMDDIA 0")
    lines.append("._ATTDIA 0")
    lines.append("._EXPERT 5")
    lines.append("")

    # レイヤ名をリネームする LISP
    lines.extend(
        [
            '(defun relayer (stem include exclude prefix suffix / tbl name newname up)',
            '  (setq tbl (tblnext "LAYER" T))',
            "  (while tbl",
            '    (setq name (cdr (assoc 2 tbl)))',
            '    (setq up (if name (strcase name) ""))',
            '    (if (and name',
            '             (not (= up "0"))',
            '             (not (= up "DEFPOINTS"))',
            '             (or (= include \"\") (wcmatch name include))',
            '             (or (= exclude \"\") (not (wcmatch name exclude))))',
            "      (progn",
            "        (setq newname (strcat prefix name suffix))",
            "        (if (not (= name newname))",
            '          (command "_.-RENAME" "Layer" name newname)',
            "        )",
            "      )",
            "    )",
            '    (setq tbl (tblnext "LAYER"))',
            "  )",
            "  (princ)",
            ")",
            "",
        ]
    )

    suffix = f"_{view_suffix}"
    lines.append(f'(relayer "{view_suffix}" "*" "" "" "{suffix}")')

    # 軽くクリーンアップ
    lines.append("._-PURGE A * N")
    lines.append("._-PURGE R * N")
    lines.append("._-PURGE A * N")
    lines.append("._AUDIT Y")

    # DXF(2018) で出力
    out_path = str(out_dxf).replace("\\", "/")
    # DXFOUT: ファイル名 → フォーマット(2018) の順に入力
    lines.append(f'._DXFOUT "{out_path}" 2018')
    lines.append("._QUIT Y")
    lines.append("(princ)")

    return "\n".join(lines)


def process_one(accore: Path, locale: str, dwg: Path, out_dir: Path) -> dict:
    # 絶対パスに正規化（CoreConsole からも確実に見えるようにする）
    dwg = dwg.resolve()
    out_dir = out_dir.resolve()
    view_suffix = sanitize_view_suffix(dwg.stem)
    out_dxf = out_dir / f"{dwg.stem}.dxf"

    staging_root = Path(r"C:/CadJobs/Staging")
    staging_root.mkdir(parents=True, exist_ok=True)
    job_dir = staging_root / f"dwg2dxf_{datetime.now():%Y%m%d_%H%M%S}"
    job_dir.mkdir(parents=True, exist_ok=True)

    script_path = job_dir / "run_dwg2dxf.scr"
    script = build_script(view_suffix, out_dxf)
    # AutoCAD Core Console 向けに Shift-JIS で保存
    script_path.write_text(script, encoding="cp932")

    cmd = [
        str(accore),
        "/i",
        str(dwg),
        "/s",
        str(script_path),
        "/l",
        locale,
    ]

    # 出力の日本語ログで UnicodeDecodeError が出ないようにバイトで受ける
    try:
        proc = subprocess.run(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=600,
            check=False,
            cwd=str(job_dir),
        )
    except Exception as ex:
        return {
            "ok": False,
            "dwg": str(dwg),
            "dxf": str(out_dxf),
            "error": str(ex),
        }

    # ログをテキストにして DXF 近くに保存（デバッグ用）
    try:
        stdout_text = (
            proc.stdout.decode("cp932", errors="ignore")
            if isinstance(proc.stdout, (bytes, bytearray))
            else str(proc.stdout)
        )
        stderr_text = (
            proc.stderr.decode("cp932", errors="ignore")
            if isinstance(proc.stderr, (bytes, bytearray))
            else str(proc.stderr)
        )
    except Exception:
        stdout_text = ""
        stderr_text = ""

    log_path = out_dxf.with_suffix(".log")
    try:
        log_path.write_text(
            f"DWG: {dwg}\nDXF: {out_dxf}\nExitCode: {proc.returncode}\n"
            f"Locale: {locale}\nAccore: {accore}\n\n"
            f"--- STDOUT ---\n{stdout_text}\n\n--- STDERR ---\n{stderr_text}\n",
            encoding="utf-8",
        )
    except Exception:
        # ログ書き込み失敗は致命的ではないので無視
        pass

    ok = proc.returncode == 0 and out_dxf.is_file()
    return {
        "ok": ok,
        "dwg": str(dwg),
        "dxf": str(out_dxf),
        "exitCode": proc.returncode,
        "log": str(log_path),
    }


def main() -> None:
    args = parse_args()
    accore = Path(args.accore).resolve()
    input_dir = Path(args.input_dir).resolve()
    out_dir = Path(args.out_dir).resolve() if args.out_dir is not None else input_dir

    out_dir.mkdir(parents=True, exist_ok=True)

    dwgs = sorted(input_dir.glob(args.pattern))

    missing = []
    if not accore.is_file():
        missing.append(str(accore))
    if not input_dir.is_dir():
        missing.append(str(input_dir))
    if missing:
        print(
            json.dumps(
                {"ok": False, "msg": "Required file/dir not found.", "missing": missing},
                ensure_ascii=False,
            )
        )
        return

    results = [process_one(accore, args.locale, dwg, out_dir) for dwg in dwgs]
    ok_all = all(r.get("ok") for r in results) if results else False

    print(
        json.dumps(
            {"ok": ok_all, "results": results},
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()







