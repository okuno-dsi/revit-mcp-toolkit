# @feature: SRC柱リスト用ファミリを電算CSVから同期（Dynamo代替） | keywords: SRC柱, 柱リスト, CSV, 同期
# -*- coding: utf-8 -*-

"""
SRC柱リスト用ファミリだけを同期する Python Runner 用スクリプト。
既存の sync_type_params_from_calc_csv.py を呼び出すラッパです。
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

    cmd = [
        sys.executable,
        str(core),
        "--port",
        str(port),
        "--mode",
        mode,
        "--kinds",
        "columns",
        "--only-column-list-sync",
        "--column-list-mode",
        "src",
    ]

    print("RUN:", " ".join(cmd))
    p = subprocess.run(cmd, check=False)
    return int(p.returncode)


if __name__ == "__main__":
    raise SystemExit(main())

