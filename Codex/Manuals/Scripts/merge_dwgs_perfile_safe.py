"""
汎用 DWG マージスクリプト（per-file layer rename 方式）

- AutoCadMCP の merge_dwgs_perfile_rename を優先的に使用
- outDWG が存在しない場合は accoreconsole.exe を直接起動してフォールバック

使い方（例）:
    python merge_dwgs_perfile_safe.py ^
        --inputs C:/path/A.dwg C:/path/B.dwg ^
        --output C:/path/Merged/merged.dwg ^
        --seed C:/path/Seed/seed.dwg

このスクリプトは、Codex/Work/merge_4F_walls_dwg.py を汎用化したものです。
"""

from __future__ import annotations

import argparse
import json
import subprocess
from datetime import datetime
from pathlib import Path
from typing import Iterable

import requests


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument(
        "--inputs",
        nargs="+",
        required=True,
        help="入力 DWG のフルパス（複数指定可）",
    )
    p.add_argument(
        "--output",
        required=True,
        help="出力 DWG のフルパス",
    )
    p.add_argument(
        "--seed",
        required=True,
        help="CoreConsole で開く seed DWG（空図面など）",
    )
    p.add_argument(
        "--accore",
        default=r"C:/Program Files/Autodesk/AutoCAD 2026/accoreconsole.exe",
        help="accoreconsole.exe のパス",
    )
    p.add_argument(
        "--server",
        default="http://127.0.0.1:5251",
        help="AutoCadMCP サーバーの URL（未起動ならフォールバックのみ）",
    )
    return p.parse_args()


def build_local_script(inputs: Iterable[Path], save_as: Path) -> str:
    lines: list[str] = []

    lines.append("._CMDECHO 0")
    lines.append("._FILEDIA 0")
    lines.append("._CMDDIA 0")
    lines.append("._ATTDIA 0")
    lines.append("._EXPERT 5")
    lines.append("")

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
            '             (or (= include "") (wcmatch name include))',
            '             (or (= exclude "") (not (wcmatch name exclude))))',
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

    for p in inputs:
        norm = str(p).replace("\\", "/")
        stem = p.stem
        suffix = f"_{stem}"
        lines.append(f'.__-INSERT "{norm}" 0,0,0 1 1 0')
        lines.append("._EXPLODE L")
        lines.append(f'(relayer "{stem}" "*" "" "" "{suffix}")')

    lines.append("._-PURGE A * N")
    lines.append("._-PURGE R * N")
    lines.append("._-PURGE A * N")
    lines.append("._AUDIT Y")

    save_path = str(save_as).replace("\\", "/")
    lines.append(f'.__SAVEAS 2018 "{save_path}"')
    lines.append("._QUIT Y")
    lines.append("(princ)")

    return "\n".join(lines)


def try_merge_via_server(
    server: str,
    accore: Path,
    seed: Path,
    inputs: list[Path],
    output: Path,
) -> dict:
    body = {
        "jsonrpc": "2.0",
        "id": "merge_generic",
        "method": "merge_dwgs_perfile_rename",
        "params": {
            "inputs": [str(p) for p in inputs],
            "output": str(output),
            "accorePath": str(accore),
            "seed": str(seed),
            "locale": "ja-JP",
            "timeoutMs": 600000,
            "include": "*",
            "exclude": "",
            "suffix": "_{stem}",
            "layerStrategy": {"prefix": ""},
            "doPurge": True,
            "doAudit": True,
            "stagingPolicy": {
                "root": "C:/CadJobs/Staging",
                "keepTempOnError": True,
                "atomicWrite": True,
            },
        },
    }

    try:
        resp = requests.post(f"{server}/rpc", json=body, timeout=60)
        data = resp.json()
    except Exception as ex:
        return {"ok": False, "source": "AutoCadMCP", "error": str(ex)}

    result = (data or {}).get("result") or {}
    ok = bool(result.get("ok")) and output.is_file()
    return {"ok": ok, "source": "AutoCadMCP", "response": data}


def merge_direct(accore: Path, seed: Path, inputs: list[Path], output: Path) -> dict:
    staging_root = Path(r"C:/CadJobs/Staging")
    staging_root.mkdir(parents=True, exist_ok=True)
    job_dir = staging_root / f"manual_{datetime.now():%Y%m%d_%H%M%S}"
    job_dir.mkdir(parents=True, exist_ok=True)

    script_path = job_dir / "run_manual.scr"
    script = build_local_script(inputs, output)
    script_path.write_text(script, encoding="cp932")

    cmd = [
        str(accore),
        "/i",
        str(seed),
        "/s",
        str(script_path),
        "/l",
        "ja-JP",
    ]

    try:
        proc = subprocess.run(
            cmd,
            capture_output=True,
            encoding="cp932",
            timeout=600,
            check=False,
            cwd=str(job_dir),
        )
    except Exception as ex:
        return {"ok": False, "source": "accoreconsole", "error": str(ex)}

    ok = proc.returncode == 0 and output.is_file()
    return {
        "ok": ok,
        "source": "accoreconsole",
        "exitCode": proc.returncode,
        "stdoutTail": proc.stdout[-4000:] if proc.stdout else "",
        "stderrTail": proc.stderr[-4000:] if proc.stderr else "",
    }


def main() -> None:
    args = parse_args()

    accore = Path(args.accore)
    seed = Path(args.seed)
    inputs = [Path(p) for p in args.inputs]
    output = Path(args.output)

    missing = [str(p) for p in [seed, accore, *inputs] if not p.is_file()]
    if missing:
        print(
            json.dumps(
                {"ok": False, "msg": "Missing files.", "missing": missing},
                ensure_ascii=False,
            )
        )
        return

    server_result = try_merge_via_server(args.server, accore, seed, inputs, output)
    if server_result.get("ok"):
        print(
            json.dumps(
                {
                    "ok": True,
                    "method": server_result["source"],
                    "output": str(output),
                },
                ensure_ascii=False,
            )
        )
        return

    direct_result = merge_direct(accore, seed, inputs, output)
    print(
        json.dumps(
            {
                "ok": bool(direct_result.get("ok")),
                "method": direct_result.get("source"),
                "output": str(output),
                "detail": {
                    "server": server_result,
                    "direct": direct_result,
                },
            },
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()

