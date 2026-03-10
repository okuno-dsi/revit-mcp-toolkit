# @feature: 電算CSVからタイプ属性+柱リストファミリを一括同期（RC/SRC自動判別） | keywords: CSV, 同期, 柱, 梁, 柱リスト, RC, SRC
# -*- coding: utf-8 -*-

"""
CSV同期の実行用ラッパー（Python Runner向け）。

実行内容
- 構造部材タイプ同期:
  - columns
  - steel_columns
  - src_columns
  - frames
- 柱リスト用ファミリ同期:
  - 自動判別（RC/SRC）

注意
- 既存の sync_type_params_from_calc_csv.py を呼び出すだけなので、
  同スクリプト側のマッピング設定に従います。
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path
from typing import List, Optional


# True: 反映（apply） / False: 差分確認のみ（plan）
APPLY = True

# 同期対象 kind（必要なら編集）
KINDS = "columns,steel_columns,src_columns,frames"

# 柱リスト同期モード: auto | rc | src | both
COLUMN_LIST_MODE = "auto"

# CSVパス（空ならコアスクリプト既定値）
CSV_PATH = ""

# 空欄なら自動解決（server_state.json -> REVIT_MCP_PORT -> 5210）
PORT_OVERRIDE: Optional[int] = None


def _resolve_port() -> int:
    if PORT_OVERRIDE and int(PORT_OVERRIDE) > 0:
        return int(PORT_OVERRIDE)
    p = os.environ.get("REVIT_MCP_PORT", "").strip()
    if p.isdigit():
        return int(p)
    ss = Path(os.environ.get("LOCALAPPDATA", "")) / "RevitMCP" / "server_state.json"
    if ss.exists():
        try:
            row = json.loads(ss.read_text(encoding="utf-8"))
            v = int(row.get("port") or 0)
            if v > 0:
                return v
        except Exception:
            pass
    return 5210


def _candidate_roots() -> List[Path]:
    roots: List[Path] = []
    env_root = os.environ.get("REVIT_MCP_ROOT", "").strip()
    if env_root:
        roots.append(Path(env_root))
    roots.append(Path.home() / "Documents" / "Revit_MCP")
    roots.append(Path(__file__).resolve().parents[2])
    return roots


def _resolve_core_script() -> Path:
    for root in _candidate_roots():
        c = root / "Scripts" / "PythonRunnerScripts" / "sync_type_params_from_calc_csv.py"
        if c.exists():
            return c
    raise FileNotFoundError("sync_type_params_from_calc_csv.py が見つかりません。")


def main() -> int:
    core = _resolve_core_script()
    port = _resolve_port()
    mode = "apply" if APPLY else "plan"

    fm = str(COLUMN_LIST_MODE or "auto").strip().lower()
    if fm not in ("auto", "rc", "src", "both"):
        fm = "auto"

    cmd = [
        sys.executable,
        str(core),
        "--port",
        str(port),
        "--mode",
        mode,
        "--kinds",
        str(KINDS),
        "--column-list-mode",
        fm,
    ]

    if str(CSV_PATH or "").strip():
        cmd += ["--csv-path", str(CSV_PATH).strip()]

    print("RUN:", " ".join(cmd))
    p = subprocess.run(cmd, check=False)
    return int(p.returncode)


if __name__ == "__main__":
    raise SystemExit(main())

