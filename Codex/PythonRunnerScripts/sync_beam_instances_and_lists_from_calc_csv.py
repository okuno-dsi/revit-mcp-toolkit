# @feature: 電算CSVから梁インスタンス＋梁リストファミリを同期（必要ならRC柱リストも） | keywords: CSV, 梁, インスタンス, リストファミリ, SRC, RC
# -*- coding: utf-8 -*-

"""
CSV同期（梁優先）実行ラッパー。

実行内容（既定）
- frames タイプ同期（コア側）
- frames インスタンス同期（--sync-frame-instances）
- 梁リスト用ファミリ同期（--beam-list-mode auto）

任意
- RC柱リストも同時同期したい場合は `COLUMN_LIST_MODE = "rc"` に変更。
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

# 梁同期を主対象にする
KINDS = "frames"

# 梁インスタンス同期を有効化
SYNC_FRAME_INSTANCES = True

# 梁リスト同期: auto | rc | src | both | none
BEAM_LIST_MODE = "auto"

# 柱リスト同期: auto | rc | src | both | none
# 梁同期だけなら "none" 推奨
COLUMN_LIST_MODE = "none"

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
        str(COLUMN_LIST_MODE).strip().lower(),
        "--beam-list-mode",
        str(BEAM_LIST_MODE).strip().lower(),
    ]
    if SYNC_FRAME_INSTANCES:
        cmd.append("--sync-frame-instances")
    if str(CSV_PATH or "").strip():
        cmd += ["--csv-path", str(CSV_PATH).strip()]

    print("RUN:", " ".join(cmd))
    p = subprocess.run(cmd, check=False)
    return int(p.returncode)


if __name__ == "__main__":
    raise SystemExit(main())

