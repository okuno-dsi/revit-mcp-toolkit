# @feature: 電算CSVを正としてレベル+符号でタイプ属性を差分同期 | keywords: CSV, 構造, 柱, 梁, タイプパラメータ
# -*- coding: utf-8 -*-

"""
電算CSV（SIRBIM変換元）を正とし、Revitタイプパラメータを差分同期するスクリプト。

目的
- 位置情報は追わず、レベル+符号で期待属性を特定。
- Revit側のタイプ属性を照合し、不一致のみ更新（apply時）。

既定仕様
- 対象: 構造柱 / 構造フレーム（梁）
- モード: plan（既定）/ apply
- 設定: 同名JSON(config)でCSV列→Revitパラメータ対応を編集可能

注意
- タイプパラメータはモデル全体共通のため、同一符号が複数レベルで異値を持つ場合は
  競合として既定でスキップします（conflictPolicy=skip）。
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Tuple

try:
    import requests  # type: ignore

    _HAS_REQUESTS = True
except Exception:
    requests = None
    _HAS_REQUESTS = False


# -----------------------------
# Defaults / Config
# -----------------------------

DEFAULT_CSV = r"C:\Users\<user>\Documents\Codex\入力（Revit変換時）.csv"


DEFAULT_CONFIG: Dict[str, Any] = {
    "version": 1,
    "description": "level+symbol で電算CSVをRevitタイプ属性へ差分同期する設定",
    "conflictPolicy": "skip",  # skip | first
    "kinds": {
        "columns": {
            "enabled": True,
            "label": "構造柱",
            "onlyUsedTypes": True,
            # レベルが取れている場合は symbol-only fallback を行わない
            "allowSymbolFallbackWhenLevelPresent": False,
            "placementSections": ["柱配置"],
            "placementLevelKey": "階",
            "placementSymbolKey": "符号",
            "sectionCandidates": ["RC柱断面", "S柱断面", "SRC柱断面"],
            "sectionLevelKey": "階",
            "sectionSymbolKey": "柱符号",
            "listTypesCommand": "get_structural_column_types",
            "updateCommand": "update_structural_column_type_parameter",
            "symbolParamCandidates": ["符号", "タイプ名", "typeName"],
            "paramMap": [
                {"revit": "符号", "csv": ["柱符号"], "converter": "str"},
                {"revit": "断面形状", "csv": ["コンクリート_形状"], "converter": "shape_rc_column"},
                {"revit": "B", "csv": ["コンクリート_Dx"], "converter": "num"},
                {"revit": "D", "csv": ["コンクリート_Dy"], "converter": "num"},
                {"revit": "柱頭主筋X1段筋太径本数", "csv": ["主筋本数_柱頭X"], "converter": "count"},
                {"revit": "柱頭主筋Y1段筋太径本数", "csv": ["主筋本数_柱頭Y"], "converter": "count"},
                {"revit": "柱脚主筋X1段筋太径本数", "csv": ["主筋本数_柱脚X"], "converter": "count"},
                {"revit": "柱脚主筋Y1段筋太径本数", "csv": ["主筋本数_柱脚Y"], "converter": "count"},
                {"revit": "柱頭主筋太径", "csv": ["主筋径_柱頭X", "主筋径_柱頭Y"], "converter": "bar_dia"},
                {"revit": "柱脚主筋太径", "csv": ["主筋径_柱脚X", "主筋径_柱脚Y"], "converter": "bar_dia"},
                {"revit": "柱頭主筋太径種別", "csv": ["主筋材料_柱頭X", "主筋材料_柱頭Y"], "converter": "str"},
                {"revit": "柱脚主筋太径種別", "csv": ["主筋材料_柱脚X", "主筋材料_柱脚Y"], "converter": "str"},

                {"revit": "柱頭芯鉄筋X太径本数", "csv": ["芯鉄筋本数_柱頭X"], "converter": "count"},
                {"revit": "柱頭芯鉄筋Y太径本数", "csv": ["芯鉄筋本数_柱頭Y"], "converter": "count"},
                {"revit": "柱脚芯鉄筋X太径本数", "csv": ["芯鉄筋本数_柱脚X"], "converter": "count"},
                {"revit": "柱脚芯鉄筋Y太径本数", "csv": ["芯鉄筋本数_柱脚Y"], "converter": "count"},

                {"revit": "柱頭芯鉄筋太径", "csv": ["芯鉄筋径_柱頭X", "芯鉄筋径_柱頭Y"], "converter": "bar_dia"},
                {"revit": "柱脚芯鉄筋太径", "csv": ["芯鉄筋径_柱脚X", "芯鉄筋径_柱脚Y"], "converter": "bar_dia"},
                {"revit": "柱頭芯鉄筋太径種別", "csv": ["芯鉄筋材料_柱頭X"], "converter": "str"},
                {"revit": "柱脚芯鉄筋太径種別", "csv": ["芯鉄筋材料_柱脚X"], "converter": "str"},
                {"revit": "柱頭芯鉄筋X方向かぶり厚", "csv": ["芯鉄筋位置_柱頭X"], "converter": "nonneg_num"},
                {"revit": "柱頭芯鉄筋Y方向かぶり厚", "csv": ["芯鉄筋位置_柱頭Y"], "converter": "nonneg_num"},
                {"revit": "柱脚芯鉄筋X方向かぶり厚", "csv": ["芯鉄筋位置_柱脚X"], "converter": "nonneg_num"},
                {"revit": "柱脚芯鉄筋Y方向かぶり厚", "csv": ["芯鉄筋位置_柱脚Y"], "converter": "nonneg_num"},

                {"revit": "柱頭フープX本数", "csv": ["帯筋本数_X"], "converter": "count"},
                {"revit": "柱脚フープX本数", "csv": ["帯筋本数_X"], "converter": "count"},
                {"revit": "柱頭フープY本数", "csv": ["帯筋本数_Y"], "converter": "count"},
                {"revit": "柱脚フープY本数", "csv": ["帯筋本数_Y"], "converter": "count"},
                {"revit": "柱脚フープ径", "csv": ["帯筋径"], "converter": "bar_dia"},
                {"revit": "柱脚フープピッチ", "csv": ["帯筋ピッチ"], "converter": "num"},
                {"revit": "柱頭フープ径", "csv": ["帯筋径"], "converter": "bar_dia"},
                {"revit": "柱頭フープピッチ", "csv": ["帯筋ピッチ"], "converter": "num"},
                {"revit": "柱頭フープ種別", "csv": ["帯筋材料"], "converter": "str"},
                {"revit": "柱脚フープ種別", "csv": ["帯筋材料"], "converter": "str"},
            ],
        },
        "steel_columns": {
            "enabled": True,
            "label": "鉄骨柱",
            "onlyUsedTypes": True,
            # section行はレベル一致を必須にする（symbolのみ一致の誤マップ防止）
            "allowSymbolFallback": False,
            # レベルが取得できる場合は、symbol-only fallback を行わない（誤マッチ防止）
            "allowSymbolFallbackWhenLevelPresent": False,
            "placementSections": ["柱配置"],
            "placementLevelKey": "階",
            "placementSymbolKey": "符号",
            "sectionCandidates": ["S柱断面"],
            "sectionLevelKey": "階",
            "sectionSymbolKey": "柱符号",
            "listTypesCommand": "get_structural_column_types",
            "updateCommand": "update_structural_column_type_parameter",
            "familyNameContainsAny": ["S柱", "SRC", "鋼"],
            "symbolParamCandidates": ["符号", "マーク(タイプ)", "タイプ名"],
        },
        "src_columns": {
            "enabled": True,
            "label": "SRC柱",
            "onlyUsedTypes": True,
            "strictPairRequired": True,
            "pairByLocation": True,
            "pairLocationToleranceMm": 20.0,
            "placementSections": ["柱配置"],
            "placementLevelKey": "階",
            "placementSymbolKey": "符号",
            "sectionCandidates": ["SRC柱断面"],
            "sectionLevelKey": "階",
            "sectionSymbolKey": "柱符号",
            "listTypesCommand": "get_structural_column_types",
            "updateCommand": "update_structural_column_type_parameter",
            "familyNameContainsAny": ["柱", "SRC", "S柱", "RC"],
            "symbolParamCandidates": ["符号", "マーク(タイプ)", "タイプ名"],
            "rcParamMapRef": "columns",
        },
        "frames": {
            "enabled": True,
            "label": "構造フレーム",
            "onlyUsedTypes": True,
            # レベルが取れている場合は symbol-only fallback を行わない
            "allowSymbolFallbackWhenLevelPresent": False,
            "placementSections": ["大梁配置", "小梁配置"],
            "placementLevelKey": "層",
            "placementSymbolKey": "符号",
            "sectionCandidates": ["RC梁断面", "S梁断面", "SRC梁断面", "RC小梁断面", "S小梁断面"],
            "sectionLevelKey": "層",
            "sectionSymbolKey": "梁符号",
            "listTypesCommand": "get_structural_frame_types",
            "updateCommand": "update_structural_frame_type_parameter",
            "symbolParamCandidates": ["符号", "タイプ名", "typeName"],
            "paramMap": [
                {"revit": "符号", "csv": ["梁符号"], "converter": "str"},
                {"revit": "B", "csv": ["コンクリート_中央B", "コンクリート_中央b", "B", "b"], "converter": "num"},
                {"revit": "D", "csv": ["コンクリート_中央D", "D"], "converter": "num"},
                {"revit": "左B", "csv": ["コンクリート_左端B", "コンクリート_左端b", "左端B", "左端b"], "converter": "num"},
                {"revit": "左D", "csv": ["コンクリート_左端D", "左端D"], "converter": "num"},
                {"revit": "右B", "csv": ["コンクリート_右端B", "コンクリート_右端b", "右端B", "右端b"], "converter": "num"},
                {"revit": "右D", "csv": ["コンクリート_右端D", "右端D"], "converter": "num"},
                {"revit": "左Len", "csv": ["ハンチ_左端", "ハンチ 左端"], "converter": "num"},
                {"revit": "右Len", "csv": ["ハンチ_右端", "ハンチ 右端"], "converter": "num"},

                {"revit": "始点側上端1段筋太径本数", "csv": ["主筋本数_左上", "主筋上端_左端_本数"], "converter": "count"},
                {"revit": "中央上端1段筋太径本数", "csv": ["主筋本数_中央上", "主筋上端_中央_本数"], "converter": "count"},
                {"revit": "終点側上端1段筋太径本数", "csv": ["主筋本数_右上", "主筋上端_右端_本数"], "converter": "count"},
                {"revit": "始点側下端1段筋太径本数", "csv": ["主筋本数_左下", "主筋下端_左端_本数"], "converter": "count"},
                {"revit": "中央下端1段筋太径本数", "csv": ["主筋本数_中央下", "主筋下端_中央_本数"], "converter": "count"},
                {"revit": "終点側下端1段筋太径本数", "csv": ["主筋本数_右下", "主筋下端_右端_本数"], "converter": "count"},

                {"revit": "始点側主筋太径", "csv": ["主筋径_左上", "主筋上端_左端_径"], "converter": "bar_dia"},
                {"revit": "中央主筋太径", "csv": ["主筋径_中央上", "主筋上端_中央_径"], "converter": "bar_dia"},
                {"revit": "終点側主筋太径", "csv": ["主筋径_右上", "主筋上端_右端_径"], "converter": "bar_dia"},

                {"revit": "始点側あばら筋径", "csv": ["あばら筋左端_径", "あばら筋_径"], "converter": "bar_dia"},
                {"revit": "中央あばら筋径", "csv": ["あばら筋中央_径", "あばら筋_径"], "converter": "bar_dia"},
                {"revit": "終点側あばら筋径", "csv": ["あばら筋右端_径", "あばら筋_径"], "converter": "bar_dia"},
                {"revit": "始点側あばら筋ピッチ", "csv": ["あばら筋左端_ピッチ", "あばら筋_ピッチ"], "converter": "num"},
                {"revit": "中央あばら筋ピッチ", "csv": ["あばら筋中央_ピッチ", "あばら筋_ピッチ"], "converter": "num"},
                {"revit": "終点側あばら筋ピッチ", "csv": ["あばら筋右端_ピッチ", "あばら筋_ピッチ"], "converter": "num"},
            ],
            "instanceParamMap": [
                {"revit": "主筋dt1_左上", "csv": ["主筋dt1_左上"], "converter": "num"},
                {"revit": "主筋dt1_中央上", "csv": ["主筋dt1_中央上"], "converter": "num"},
                {"revit": "主筋dt1_右上", "csv": ["主筋dt1_右上"], "converter": "num"},
                {"revit": "主筋dt1_左下", "csv": ["主筋dt1_左下"], "converter": "num"},
                {"revit": "主筋dt1_中央下", "csv": ["主筋dt1_中央下"], "converter": "num"},
                {"revit": "主筋dt1_右下", "csv": ["主筋dt1_右下"], "converter": "num"},
                {"revit": "主筋かぶり_左上", "csv": ["主筋かぶり_左上"], "converter": "num"},
                {"revit": "主筋かぶり_中央上", "csv": ["主筋かぶり_中央上"], "converter": "num"},
                {"revit": "主筋かぶり_右上", "csv": ["主筋かぶり_右上"], "converter": "num"},
                {"revit": "主筋かぶり_左下", "csv": ["主筋かぶり_左下"], "converter": "num"},
                {"revit": "主筋かぶり_中央下", "csv": ["主筋かぶり_中央下"], "converter": "num"},
                {"revit": "主筋かぶり_右下", "csv": ["主筋かぶり_右下"], "converter": "num"},
                {"revit": "コンクリート_材料", "csv": ["コンクリート_材料"], "converter": "str"}
            ],
        },
    },
}


# -----------------------------
# RPC helpers
# -----------------------------


def _detect_endpoint(base_url: str) -> str:
    for ep in ("/rpc", "/jsonrpc"):
        url = f"{base_url}{ep}"
        try:
            _http_post_json(url, {"jsonrpc": "2.0", "id": "ping", "method": "help.ping_server", "params": {}}, timeout_sec=2)
            return url
        except Exception:
            continue
    return f"{base_url}/rpc"


def _unwrap(payload: Any) -> Dict[str, Any]:
    obj = payload
    if isinstance(obj, dict) and "result" in obj and isinstance(obj["result"], dict):
        obj = obj["result"]
    if isinstance(obj, dict) and "result" in obj and isinstance(obj["result"], dict):
        obj = obj["result"]
    return obj if isinstance(obj, dict) else {}


def _poll_job(base_url: str, job_id: str, timeout_sec: int = 300) -> Dict[str, Any]:
    deadline = time.time() + timeout_sec
    url = f"{base_url}/job/{job_id}"
    while time.time() < deadline:
        status, row = _http_get_json(url, timeout_sec=20)
        if status in (202, 204):
            time.sleep(0.5)
            continue
        if status >= 400:
            raise RuntimeError(f"job poll failed HTTP {status}")
        st = str(row.get("state") or "").upper()
        if st == "SUCCEEDED":
            rj = row.get("result_json")
            if isinstance(rj, str) and rj.strip():
                try:
                    return _unwrap(json.loads(rj))
                except Exception:
                    return {"ok": True, "result_json": rj}
            return {"ok": True}
        if st in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(str(row.get("error_msg") or st))
        time.sleep(0.5)
    raise TimeoutError(f"job polling timed out: {job_id}")


def rpc(base_url: str, method: str, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    endpoint = _detect_endpoint(base_url)
    payload = {
        "jsonrpc": "2.0",
        "id": f"req-{int(time.time() * 1000)}",
        "method": method,
        "params": params or {},
    }
    status, data = _http_post_json(endpoint, payload, timeout_sec=60)
    if status >= 400:
        raise RuntimeError(f"HTTP {status} when calling {method}")
    if isinstance(data, dict) and data.get("error"):
        raise RuntimeError(str(data["error"]))
    result = data.get("result") if isinstance(data, dict) else {}
    if isinstance(result, dict) and result.get("queued"):
        job_id = result.get("jobId") or result.get("job_id")
        if not job_id:
            raise RuntimeError("queued=true but jobId missing")
        return _poll_job(base_url, str(job_id))
    return _unwrap(data)


# -----------------------------
# CSV parsing
# -----------------------------


def _norm(s: Any) -> str:
    return re.sub(r"\s+", "", str(s or "").strip())


def normalize_symbol_token(s: Any) -> str:
    return _norm(s).upper()


def _symbol_contains_or_equal(actual_symbol: Any, expected_symbol: Any) -> bool:
    a = normalize_symbol_token(actual_symbol)
    e = normalize_symbol_token(expected_symbol)
    if not a or not e:
        return False
    if a == e:
        return True
    # Revit側にサフィックスが付くケース（例: C5_MIRROR -> C5）を同値扱い。
    # ただし C51 と C5 のような部分一致誤判定を避けるため、英数境界を要求する。
    start = 0
    while True:
        idx = a.find(e, start)
        if idx < 0:
            return False
        left_ok = (idx == 0) or (not a[idx - 1].isalnum())
        ridx = idx + len(e)
        right_ok = (ridx >= len(a)) or (not a[ridx].isalnum())
        if left_ok and right_ok:
            return True
        start = idx + 1


def normalize_level_token(s: Any) -> str:
    x = _norm(s).upper()
    x = x.replace("Ｆ", "F").replace("Ｌ", "L")
    x = x.replace("階", "").replace("層", "")
    # Dynamo側は level文字列から "L" を除去して比較しているため合わせる
    x = x.replace("L", "")
    # 例: 1F / B1F / E10F / RF を 1 / B1 / E10 / R に寄せる
    if x.endswith("F"):
        x = x[:-1]
    # 3F'' などの派生表記を吸収
    x = x.replace("'", "").replace("’", "").replace("＇", "")
    return x


def parse_level_symbol_from_type_name(type_name: str) -> Tuple[str, str, str]:
    t = str(type_name or "").strip()
    if not t:
        return "", "", ""
    parts = [p.strip() for p in t.split("_") if p.strip()]
    if len(parts) < 2:
        return "", "", ""
    level_raw = parts[0]
    symbol_raw = parts[1]
    suffix = "_".join(parts[2:]) if len(parts) > 2 else ""
    return level_raw, symbol_raw, suffix


def _extract_level_symbol_hint_from_type_name(type_name: str) -> Tuple[str, str, bool]:
    """
    型名にレベル情報が含まれているかを判定し、可能なら (level, symbol, True) を返す。
    例:
      B1FL_C1 / 1FL_G1 / B1C1 / 2G1 / 4G6A'
    """
    t = str(type_name or "").strip()
    if not t:
        return "", "", False

    lv1, sy1, _ = parse_level_symbol_from_type_name(t)
    if lv1 and sy1:
        return lv1, sy1, True

    m = re.match(r"^\s*([A-Za-z]?\d+(?:FL|F)?)(?:[_\-\s]*)(([A-Za-z]+[0-9A-Za-z_\-\'’＇]*))\s*$", t, re.IGNORECASE)
    if m:
        return str(m.group(1) or "").strip(), str(m.group(2) or "").strip(), True

    return "", "", False


def to_dia_text(v: Any) -> Optional[str]:
    if v is None:
        return None
    if isinstance(v, (int, float)):
        return f"D{int(round(float(v)))}"
    s = str(v).strip()
    if not s:
        return None
    m = re.search(r"[dD]\s*(\d+)", s)
    if m:
        return f"D{int(m.group(1))}"
    n = _to_num(s)
    if n is not None:
        return f"D{int(round(n))}"
    return s


def _sanitize_header_name(s: str) -> str:
    x = str(s or "").strip()
    x = x.replace("\u3000", " ")
    x = re.sub(r"\s+", "", x)
    x = x.replace("/", "_").replace("\\", "_")
    x = x.replace("-", "-")
    return x


def _is_marker_row(row: List[str], marker: str) -> bool:
    m = marker.lower()
    for c in row:
        if str(c or "").strip().lower() == m:
            return True
    return False


def parse_sections(csv_path: Path, encoding_hint: str = "") -> Dict[str, Dict[str, Any]]:
    encodings = [encoding_hint] if encoding_hint else []
    # UTF-8系を優先し、失敗時にcp932へフォールバック
    # （cp932先行だとUTF-8 CSVの記号文字が化けるケースがある）
    encodings += ["utf-8-sig", "utf-8", "cp932"]

    rows: Optional[List[List[str]]] = None
    used_enc = ""
    for enc in encodings:
        if not enc:
            continue
        try:
            with csv_path.open("r", encoding=enc, errors="strict", newline="") as f:
                import csv

                rows = list(csv.reader(f))
            used_enc = enc
            break
        except Exception:
            rows = None
    if rows is None:
        raise RuntimeError("CSVを読み込めませんでした。encodingを確認してください。")

    sections: Dict[str, Dict[str, Any]] = {}
    starts: List[Tuple[str, int]] = []
    for i, row in enumerate(rows):
        if row and str(row[0]).startswith("name="):
            starts.append((str(row[0])[5:], i))

    for idx, (name, start_i) in enumerate(starts):
        end_i = starts[idx + 1][1] if idx + 1 < len(starts) else len(rows)
        body = rows[start_i + 1 : end_i]
        if not body:
            sections[name] = {"name": name, "headers": [], "rows": [], "encoding": used_enc}
            continue

        data_idx = -1
        unit_idx = -1
        for i, r in enumerate(body):
            if data_idx < 0 and _is_marker_row(r, "<data>"):
                data_idx = i
            if unit_idx < 0 and _is_marker_row(r, "<unit>"):
                unit_idx = i
        if data_idx < 0:
            sections[name] = {"name": name, "headers": [], "rows": [], "encoding": used_enc}
            continue

        header_end = unit_idx if (0 <= unit_idx < data_idx) else data_idx
        header_rows = body[:header_end]
        data_rows = body[data_idx + 1 :]

        max_cols = 0
        for r in header_rows + data_rows:
            max_cols = max(max_cols, len(r))
        if max_cols <= 0:
            sections[name] = {"name": name, "headers": [], "rows": [], "encoding": used_enc}
            continue

        # blankセルを左隣の見出しで補完（多段ヘッダ対策）
        expanded_headers: List[List[str]] = []
        for ridx, hr in enumerate(header_rows):
            ex: List[str] = []
            last = ""
            allow_propagate = ridx < (len(header_rows) - 1)
            for c in range(max_cols):
                if c < len(hr):
                    raw = hr[c]
                    v = _sanitize_header_name(raw)
                    if allow_propagate and (not v) and last:
                        v = last
                    elif v:
                        last = v
                else:
                    v = ""
                ex.append(v)
            expanded_headers.append(ex)

        headers: List[str] = []
        used_keys: Dict[str, int] = {}
        for c in range(max_cols):
            parts: List[str] = []
            for ex in expanded_headers:
                v = ex[c] if c < len(ex) else ""
                if not v:
                    continue
                if v.startswith("<") and v.endswith(">"):
                    continue
                if v not in parts:
                    parts.append(v)
            key = "_".join(parts) if parts else f"col{c+1}"
            n = used_keys.get(key, 0)
            if n > 0:
                key = f"{key}__{n+1}"
            used_keys[key] = n + 1
            headers.append(key)

        parsed_rows: List[Dict[str, str]] = []
        for r in data_rows:
            if not any(str(c or "").strip() for c in r):
                continue
            rr = list(r)
            while rr and _norm(rr[-1]) in ("<RE>", "<END>"):
                rr.pop()
            obj: Dict[str, str] = {}
            for c, h in enumerate(headers):
                obj[h] = str(rr[c]).strip() if c < len(rr) else ""
            parsed_rows.append(obj)

        sections[name] = {
            "name": name,
            "headers": headers,
            "rows": parsed_rows,
            "encoding": used_enc,
        }

    return sections


def _http_post_json(url: str, payload: Dict[str, Any], timeout_sec: int = 30) -> Tuple[int, Dict[str, Any]]:
    if _HAS_REQUESTS:
        assert requests is not None
        r = requests.post(url, json=payload, timeout=timeout_sec)
        status = int(getattr(r, "status_code", 0) or 0)
        try:
            data = r.json() if status or r.content else {}
        except Exception:
            data = {}
        return status, (data if isinstance(data, dict) else {})

    data_bytes = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(url, data=data_bytes, headers={"Content-Type": "application/json; charset=utf-8"})
    try:
        with urllib.request.urlopen(req, timeout=timeout_sec) as resp:
            status = int(getattr(resp, "status", 200) or 200)
            body = resp.read().decode("utf-8", errors="ignore")
            obj = json.loads(body) if body else {}
            return status, (obj if isinstance(obj, dict) else {})
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="ignore") if hasattr(e, "read") else ""
        obj = json.loads(body) if body else {}
        return int(e.code), (obj if isinstance(obj, dict) else {})


def _http_get_json(url: str, timeout_sec: int = 30) -> Tuple[int, Dict[str, Any]]:
    if _HAS_REQUESTS:
        assert requests is not None
        r = requests.get(url, timeout=timeout_sec)
        status = int(getattr(r, "status_code", 0) or 0)
        try:
            data = r.json() if status or r.content else {}
        except Exception:
            data = {}
        return status, (data if isinstance(data, dict) else {})

    req = urllib.request.Request(url, headers={"Accept": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=timeout_sec) as resp:
            status = int(getattr(resp, "status", 200) or 200)
            body = resp.read().decode("utf-8", errors="ignore")
            obj = json.loads(body) if body else {}
            return status, (obj if isinstance(obj, dict) else {})
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="ignore") if hasattr(e, "read") else ""
        obj = json.loads(body) if body else {}
        return int(e.code), (obj if isinstance(obj, dict) else {})


# -----------------------------
# Value conversion / compare
# -----------------------------


def _to_num(v: Any) -> Optional[float]:
    if v is None:
        return None
    s = str(v).strip()
    if not s:
        return None
    m = re.search(r"[-+]?\d+(?:\.\d+)?", s)
    if not m:
        return None
    try:
        return float(m.group(0))
    except Exception:
        return None


def _to_count(v: Any) -> Optional[int]:
    if v is None:
        return None
    s = str(v).strip()
    if not s:
        return None
    m = re.match(r"\s*(\d+)", s)
    if m:
        return int(m.group(1))
    n = _to_num(s)
    return int(round(n)) if n is not None else None


def _to_bar_dia(v: Any) -> Optional[int]:
    if v is None:
        return None
    s = str(v).strip()
    if not s:
        return None
    m = re.search(r"[dD]\s*(\d+)", s)
    if m:
        return int(m.group(1))
    n = _to_num(s)
    return int(round(n)) if n is not None else None


def _to_shape_rc_column(v: Any) -> Optional[int]:
    s = str(v or "").strip()
    if not s:
        return None
    direct = {
        "□": 0,
        "四角": 0,
        "矩形": 0,
        "RECT": 0,
        "○": 1,
        "丸": 1,
        "円": 1,
        "CIRCLE": 1,
    }
    key = s.upper()
    if s in direct:
        return direct[s]
    if key in direct:
        return direct[key]
    n = _to_count(s)
    return n


def convert(v: Any, conv: str) -> Any:
    c = (conv or "str").lower()
    if c == "str":
        x = str(v or "").strip()
        return x if x != "" else None
    if c == "num":
        return _to_num(v)
    if c == "nonneg_num":
        n = _to_num(v)
        if n is None:
            return None
        return 0.0 if n <= 0 else n
    if c == "count":
        return _to_count(v)
    if c == "bar_dia":
        return _to_bar_dia(v)
    if c == "shape_rc_column":
        return _to_shape_rc_column(v)
    return str(v or "").strip() or None


def almost_equal(a: Any, b: Any, tol: float = 1e-6) -> bool:
    if a is None and b is None:
        return True
    if a is None or b is None:
        return False
    if isinstance(a, (int, float)) and isinstance(b, (int, float)):
        return abs(float(a) - float(b)) <= tol
    return str(a) == str(b)


def _normalize_steel_shape_code(v: Any) -> str:
    s = str(v or "").strip().upper()
    if not s:
        return ""
    s = s.replace("Ｈ", "H").replace("Ｉ", "I").replace("Ｓ", "S")
    # BOX系コードは □ と同義
    if s in ("BX", "BY", "B", "BOX", "□"):
        return "□"
    # Revit側で SH/H/HX/HY として保持されるケースを同値扱い
    if s in ("SH", "H", "HX", "HY"):
        return "H"
    return s


def equal_with_param(param_name: str, a: Any, b: Any) -> bool:
    if almost_equal(a, b):
        return True

    n = str(param_name or "").strip()
    if n:
        # レベル表記差（例: B1FL / B1F）を同値扱い
        if ("階" in n) or ("層" in n) or ("レベル" in n) or ("LEVEL" in n.upper()):
            return normalize_level_token(a) == normalize_level_token(b)
        # 断面形状コードの軽微差（SH/H）を同値扱い
        if "断面形状" in n:
            return _normalize_steel_shape_code(a) == _normalize_steel_shape_code(b)
        # 符号系: Revit側にCSV符号が含まれていれば同値扱い（例: FG3A_MIRROR / FG3A）
        nu = n.upper()
        if ("符号" in n) or ("MARK" in nu) or ("マーク" in n):
            return _symbol_contains_or_equal(a, b)

    return False


# -----------------------------
# Extraction logic
# -----------------------------


def pick_first_key(obj: Dict[str, Any], keys: Iterable[str]) -> str:
    norm_map = {_norm(k): k for k in obj.keys()}
    for k in keys:
        kk = norm_map.get(_norm(k))
        if kk:
            return kk
    return ""


CORE_COUNT_CSV_KEYS = [
    "芯鉄筋本数_柱頭X",
    "芯鉄筋本数_柱頭Y",
    "芯鉄筋本数_柱脚X",
    "芯鉄筋本数_柱脚Y",
]

CORE_ZERO_FORCE_REBAR_PARAMS = [
    "柱頭芯鉄筋X太径本数",
    "柱頭芯鉄筋X細径本数",
    "柱頭芯鉄筋Y太径本数",
    "柱頭芯鉄筋Y細径本数",
    "柱脚芯鉄筋X太径本数",
    "柱脚芯鉄筋X細径本数",
    "柱脚芯鉄筋Y太径本数",
    "柱脚芯鉄筋Y細径本数",
]

CORE_SKIP_CSV_PREFIXES_WHEN_ZERO = (
    "芯鉄筋径_",
    "芯鉄筋材料_",
)


def is_core_count_zero_row(row: Dict[str, Any]) -> bool:
    vals: List[Optional[int]] = []
    for k in CORE_COUNT_CSV_KEYS:
        rk = pick_first_key(row, [k])
        if not rk:
            return False
        vals.append(_to_count(row.get(rk)))
    if any(v is None for v in vals):
        return False
    return all(int(v) == 0 for v in vals if v is not None)


def aggregate_expected_for_kind(kind_cfg: Dict[str, Any], sections: Dict[str, Dict[str, Any]]) -> Dict[str, Any]:
    placements: List[Tuple[str, str]] = []
    p_level_key_cfg = str(kind_cfg.get("placementLevelKey") or "")
    p_symbol_key_cfg = str(kind_cfg.get("placementSymbolKey") or "")
    for psec in kind_cfg.get("placementSections") or []:
        st = sections.get(psec)
        if not st:
            continue
        for row in st.get("rows") or []:
            p_level_key = pick_first_key(row, [p_level_key_cfg, "階", "層", "level", "Level"])
            p_symbol_key = pick_first_key(row, [p_symbol_key_cfg, "符号", "柱符号", "梁符号", "壁符号", "床符号", "type", "Type"])
            lv = str(row.get(p_level_key, "")).strip() if p_level_key else ""
            sy = str(row.get(p_symbol_key, "")).strip() if p_symbol_key else ""
            if lv and sy:
                placements.append((lv, sy))

    placements = sorted(set(placements))

    sec_level_cfg = str(kind_cfg.get("sectionLevelKey") or "")
    sec_symbol_cfg = str(kind_cfg.get("sectionSymbolKey") or "")
    allow_symbol_fallback = bool(kind_cfg.get("allowSymbolFallback", True))
    records_by_symbol: Dict[str, List[Dict[str, Any]]] = {}
    records_by_level_symbol: Dict[Tuple[str, str], List[Dict[str, Any]]] = {}

    for sname in kind_cfg.get("sectionCandidates") or []:
        st = sections.get(sname)
        if not st:
            continue
        for row in st.get("rows") or []:
            k_symbol = pick_first_key(row, [sec_symbol_cfg, "柱符号", "梁符号", "壁符号", "床符号", "符号"]) if sec_symbol_cfg else ""
            if not k_symbol:
                continue
            symbol = str(row.get(k_symbol, "")).strip()
            if not symbol:
                continue

            k_level = pick_first_key(row, [sec_level_cfg, "階", "層", "level", "Level"]) if sec_level_cfg else ""
            level = str(row.get(k_level, "")).strip() if k_level else ""

            rec = dict(row)
            rec["__section"] = sname
            rec["__symbol"] = symbol
            rec["__level"] = level
            rec["__symbol_norm"] = normalize_symbol_token(symbol)
            rec["__level_norm"] = normalize_level_token(level) if level else ""

            records_by_symbol.setdefault(symbol, []).append(rec)
            records_by_symbol.setdefault(str(rec["__symbol_norm"]), []).append(rec)
            if level:
                records_by_level_symbol.setdefault((level, symbol), []).append(rec)
                records_by_level_symbol.setdefault((str(rec["__level_norm"]), str(rec["__symbol_norm"])), []).append(rec)

    chosen_rows: List[Dict[str, Any]] = []
    missing_rows: List[Dict[str, Any]] = []
    for lv, sy in placements:
        lvn = normalize_level_token(lv)
        syn = normalize_symbol_token(sy)
        cands = records_by_level_symbol.get((lv, sy), [])
        if not cands:
            cands = records_by_level_symbol.get((lvn, syn), [])
        if allow_symbol_fallback and not cands:
            cands = records_by_symbol.get(sy, [])
        if allow_symbol_fallback and not cands:
            cands = records_by_symbol.get(syn, [])
        if not cands:
            missing_rows.append({"level": lv, "symbol": sy, "reason": "section_row_not_found"})
            continue
        chosen = dict(cands[0])
        chosen["__placement_level"] = lv
        chosen["__placement_symbol"] = sy
        chosen["__placement_level_norm"] = lvn
        chosen["__placement_symbol_norm"] = syn
        chosen_rows.append(chosen)

    expected_by_symbol: Dict[str, Dict[str, Any]] = {}
    expected_by_level_symbol: Dict[Tuple[str, str], Dict[str, Any]] = {}
    conflicts: List[Dict[str, Any]] = []
    conflicts_by_level_symbol: List[Dict[str, Any]] = []

    param_map = kind_cfg.get("paramMap") or []
    tmp: Dict[Tuple[str, str], List[Any]] = {}
    tmp_lv: Dict[Tuple[str, str, str], List[Any]] = {}

    for row in chosen_rows:
        sy = str(row.get("__symbol") or "").strip()
        sy_norm = normalize_symbol_token(sy)
        lv_norm = str(row.get("__level_norm") or normalize_level_token(row.get("__level") or ""))
        if not sy:
            continue
        core_zero = is_core_count_zero_row(row)
        for m in param_map:
            revit_name = str(m.get("revit") or "").strip()
            csv_candidates = [str(x) for x in (m.get("csv") or []) if str(x).strip()]
            conv = str(m.get("converter") or "str")
            if not revit_name or not csv_candidates:
                continue
            if core_zero:
                skip_this = False
                for ck in csv_candidates:
                    nck = _norm(ck)
                    if any(nck.startswith(_norm(pfx)) for pfx in CORE_SKIP_CSV_PREFIXES_WHEN_ZERO):
                        skip_this = True
                        break
                if skip_this:
                    continue
            value = None
            for ck in csv_candidates:
                rk = pick_first_key(row, [ck])
                if rk and str(row.get(rk, "")).strip() != "":
                    value = convert(row.get(rk), conv)
                    break
            if value is None:
                continue
            tmp.setdefault((sy, revit_name), []).append(value)
            tmp.setdefault((sy_norm, revit_name), []).append(value)
            if lv_norm:
                tmp_lv.setdefault((lv_norm, sy_norm, revit_name), []).append(value)

        if core_zero:
            for rp in CORE_ZERO_FORCE_REBAR_PARAMS:
                tmp.setdefault((sy, rp), []).append(0)
                tmp.setdefault((sy_norm, rp), []).append(0)
                if lv_norm:
                    tmp_lv.setdefault((lv_norm, sy_norm, rp), []).append(0)

    for (sy, rp), vals in tmp.items():
        uniq: List[Any] = []
        for v in vals:
            if not any(almost_equal(v, u) for u in uniq):
                uniq.append(v)
        if len(uniq) > 1:
            conflicts.append({"symbol": sy, "param": rp, "values": uniq})
            continue
        expected_by_symbol.setdefault(sy, {})[rp] = uniq[0]

    for (lv, sy, rp), vals in tmp_lv.items():
        uniq: List[Any] = []
        for v in vals:
            if not any(almost_equal(v, u) for u in uniq):
                uniq.append(v)
        if len(uniq) > 1:
            conflicts_by_level_symbol.append({"level": lv, "symbol": sy, "param": rp, "values": uniq})
            continue
        expected_by_level_symbol.setdefault((lv, sy), {})[rp] = uniq[0]

    return {
        "placements": placements,
        "chosenRowsCount": len(chosen_rows),
        "chosenRows": chosen_rows,
        "missingRows": missing_rows,
        "expectedBySymbol": expected_by_symbol,
        "expectedByLevelSymbol": expected_by_level_symbol,
        "conflicts": conflicts,
        "conflictsByLevelSymbol": conflicts_by_level_symbol,
    }


def _extract_types(obj: Dict[str, Any]) -> List[Dict[str, Any]]:
    for k in ("types", "items", "structuralColumnTypes", "structuralFrameTypes", "wallTypes", "floorTypes"):
        v = obj.get(k)
        if isinstance(v, list):
            return [x for x in v if isinstance(x, dict)]
    return []


def _type_id_of(t: Dict[str, Any]) -> int:
    for k in ("typeId", "id", "elementId"):
        v = t.get(k)
        try:
            n = int(v)
            if n > 0:
                return n
        except Exception:
            pass
    return 0


def _type_name_of(t: Dict[str, Any]) -> str:
    for k in ("typeName", "name"):
        v = str(t.get(k) or "").strip()
        if v:
            return v
    return ""


def get_type_params_bulk(base_url: str, type_id: int, param_names: List[str]) -> Dict[str, Any]:
    if not param_names:
        return {"params": {}, "display": {}}
    env = rpc(
        base_url,
        "get_type_parameters_bulk",
        {
            "typeIds": [int(type_id)],
            "paramKeys": param_names,
            "page": {"startIndex": 0, "batchSize": 20},
            "failureHandling": {"enabled": True, "mode": "rollback"},
        },
    )
    items = env.get("items") or []
    if not items:
        return {"params": {}, "display": {}}
    it = items[0] if isinstance(items[0], dict) else {}
    return {
        "params": it.get("params") or {},
        "display": it.get("display") or {},
        "ok": bool(it.get("ok", True)),
    }


def select_symbol_from_type(type_item: Dict[str, Any], type_param_maps: Dict[str, Any], candidates: List[str]) -> str:
    display = type_param_maps.get("display") or {}
    params = type_param_maps.get("params") or {}
    for n in candidates:
        if n in display and str(display.get(n) or "").strip():
            return str(display[n]).strip()
        if n in params and str(params.get(n) or "").strip():
            return str(params[n]).strip()
    return _type_name_of(type_item)


def read_actual_param(type_param_maps: Dict[str, Any], param_name: str, conv: str) -> Any:
    display = type_param_maps.get("display") or {}
    params = type_param_maps.get("params") or {}
    if param_name in display and str(display.get(param_name) or "").strip():
        return convert(display.get(param_name), conv)
    if param_name in params and str(params.get(param_name) or "").strip() != "":
        return convert(params.get(param_name), conv)
    return None


def update_param(base_url: str, cmd: str, type_id: int, param_name: str, value: Any) -> Dict[str, Any]:
    return rpc(base_url, cmd, {"typeId": int(type_id), "paramName": param_name, "value": value})


def get_family_type_params(base_url: str, type_id: int) -> Dict[str, Any]:
    env = rpc(base_url, "element.get_family_type_parameters", {"typeId": int(type_id)})
    items = env.get("parameters") if isinstance(env.get("parameters"), list) else []
    values: Dict[str, Any] = {}
    for p in items:
        if not isinstance(p, dict):
            continue
        n = str(p.get("name") or "").strip()
        if not n:
            continue
        # 同名重複があるので、空より非空を優先して採用
        v = p.get("value")
        if n not in values or (values.get(n) in ("", None) and v not in ("", None)):
            values[n] = v
    return {"ok": bool(env.get("ok", True)), "values": values, "parameters": items}


def _column_source_zone_for_list_type(type_name: str) -> str:
    t = str(type_name or "")
    if "柱頭" in t:
        return "柱頭"
    if "柱脚" in t:
        return "柱脚"
    # 全断面など明示なしは柱脚側を既定にする（Dynamo運用寄り）
    return "柱脚"


def _column_dnmn_for_list_type(type_name: str) -> str:
    z = _column_source_zone_for_list_type(type_name)
    t = str(type_name or "")
    # Dynamo運用: 全断面は柱脚側データで評価
    if "全断面" in t:
        return "柱脚"
    return z


def _zone_param_value(exp: Dict[str, Any], zone: str, suffix: str) -> Any:
    k = f"{zone}{suffix}"
    if k in exp:
        return exp.get(k)
    alt = "柱頭" if zone == "柱脚" else "柱脚"
    return exp.get(f"{alt}{suffix}")


def _pick_first_nonempty(values: Dict[str, Any], keys: List[str]) -> Any:
    for k in keys:
        if k in values:
            v = values.get(k)
            if v not in (None, ""):
                return v
    return None


def _steel_mat_for_pipe(v: Any) -> Any:
    s = str(v or "").strip().upper()
    if not s:
        return v
    if s == "SS400":
        return "STK400"
    if s in ("SM490", "SM490A", "SM490B", "SM490C"):
        return "STK490"
    if s.startswith("SN400"):
        return "STKN400B"
    if s.startswith("SN490"):
        return "STKN490B"
    return v


def _dia_text_nonzero(v: Any) -> Optional[str]:
    n = convert(v, "num")
    if n is not None and abs(float(n)) <= 1e-9:
        return None
    s = to_dia_text(v)
    if s in (None, "", "D0", "0"):
        return None
    return s


def build_column_list_expected(
    exp: Dict[str, Any],
    level_raw: str,
    symbol_raw: str,
    type_name: str,
    family_name: str = "",
    source_rc: Optional[Dict[str, Any]] = None,
    source_steel: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    zone = _column_source_zone_for_list_type(type_name)
    dnmn = _column_dnmn_for_list_type(type_name)
    src_rc = source_rc or {}
    src_steel = source_steel or {}

    def _rcv(keys: List[str], fallback_from_exp: bool = True) -> Any:
        v = _pick_first_nonempty(src_rc, keys)
        if v not in (None, ""):
            return v
        if fallback_from_exp:
            for k in keys:
                if k in exp and exp.get(k) not in (None, ""):
                    return exp.get(k)
        return None

    def _steelv(keys: List[str]) -> Any:
        v = _pick_first_nonempty(src_steel, keys)
        if v not in (None, ""):
            return v
        # 柱頭/柱脚付き・無しの相互フォールバック
        alt_keys: List[str] = []
        for k in keys:
            if k.startswith("柱脚"):
                alt_keys.append(k.replace("柱脚", "", 1))
            elif k.startswith("柱頭"):
                alt_keys.append(k.replace("柱頭", "", 1))
        v = _pick_first_nonempty(src_steel, alt_keys)
        return v

    out: Dict[str, Any] = {}
    out["符号"] = symbol_raw
    out["レベル"] = normalize_level_token(level_raw)

    # 1) RC角柱 / RC円柱 共通
    v = _rcv(["B"])
    if v is not None:
        out["Dx"] = v
    v = _rcv(["D"])
    if v is not None:
        out["Dy"] = v

    # RC円柱は (R) を優先し、無ければ B を柱せいへ
    if ("円柱" in family_name) or ("CIR" in str(exp.get("断面形状") or "").upper()):
        v = _rcv(["(R)", "R", "B"])
        if v is not None:
            out["柱せい"] = v

    v = _rcv(["備考（断面リスト）"])
    if v is not None:
        out["備考（断面リスト）"] = v

    # 鉄筋（dnmnプレフィックス）
    v = _dia_text_nonzero(_rcv([f"{dnmn}主筋太径"]))
    if v is not None:
        out["柱頭_主筋径"] = v
    v = _dia_text_nonzero(_rcv([f"{dnmn}主筋細径"]))
    if v is not None:
        out["柱頭_副主筋径"] = v

    v = _rcv([f"{dnmn}主筋X1段筋太径本数"])
    if v is not None:
        out["柱頭_X方向1段主筋本数"] = v
    v = _rcv([f"{dnmn}主筋X1段筋細径本数"])
    if v is not None:
        out["柱頭_X方向1段副主筋本数"] = v
    v = _rcv([f"{dnmn}主筋Y1段筋太径本数"])
    if v is not None:
        out["柱頭_Y方向1段主筋本数"] = v
    v = _rcv([f"{dnmn}主筋Y1段筋細径本数"])
    if v is not None:
        out["柱頭_Y方向1段副主筋本数"] = v

    v = _dia_text_nonzero(_rcv([f"{dnmn}芯鉄筋太径"]))
    if v is not None:
        out["芯鉄筋径"] = v
    v = _rcv([f"{dnmn}芯鉄筋X太径本数"])
    if v is not None:
        out["X方向芯鉄筋本数"] = v
    v = _rcv([f"{dnmn}芯鉄筋Y太径本数"])
    if v is not None:
        out["Y方向芯鉄筋本数"] = v
    v = _rcv([f"{dnmn}芯鉄筋X方向かぶり厚"])
    if v is not None:
        out["躯体面から芯鉄筋X方向までの距離"] = v
    v = _rcv([f"{dnmn}芯鉄筋Y方向かぶり厚"])
    if v is not None:
        out["躯体面から芯鉄筋Y方向までの距離"] = v

    v = _dia_text_nonzero(_rcv([f"{dnmn}フープ径"]))
    if v is not None:
        out["柱頭_帯筋径"] = v
    v = _rcv([f"{dnmn}フープX本数"])
    if v is not None:
        out["柱頭_帯筋X方向本数"] = v
    v = _rcv([f"{dnmn}フープY本数"])
    if v is not None:
        out["柱頭_帯筋Y方向本数"] = v
    v = _rcv([f"{dnmn}フープピッチ"])
    if v is not None:
        out["柱頭_帯筋ピッチ"] = v
    v = _rcv([f"{dnmn}フープ種別"])
    if v is not None:
        out["柱脚フープ種別"] = v
    v = _rcv([f"{dnmn}フープ記号"])
    if v is not None:
        out["柱脚フープ記号"] = v

    # 鉄筋「種別」系（柱リストファミリ）
    # 追加要件:
    # - 柱頭フープ種別 / 柱脚フープ種別
    # - 柱頭太径種別 / 柱脚太径種別
    # - 柱頭芯鉄筋太径種別 / 柱脚芯鉄筋太径種別
    for key in (
        "柱頭フープ種別",
        "柱脚フープ種別",
        "柱頭太径種別",
        "柱脚太径種別",
        "柱頭芯鉄筋太径種別",
        "柱脚芯鉄筋太径種別",
    ):
        vv = exp.get(key)
        if vv not in (None, ""):
            out[key] = vv

    # 要素側の名称（柱頭主筋太径種別/柱脚主筋太径種別）も、
    # リスト側の「柱頭太径種別/柱脚太径種別」へフォールバックで連携
    if out.get("柱頭太径種別") in (None, ""):
        vv = exp.get("柱頭主筋太径種別")
        if vv not in (None, ""):
            out["柱頭太径種別"] = vv
    if out.get("柱脚太径種別") in (None, ""):
        vv = exp.get("柱脚主筋太径種別")
        if vv not in (None, ""):
            out["柱脚太径種別"] = vv

    # 2) SRC鉄骨部（family分岐）
    fam = str(family_name or "")
    if ("SRC" in fam or "S柱" in fam) and src_steel:
        # dnmn: 柱頭/柱脚（全断面は柱脚）
        # S柱C型
        if "S柱C型" in fam:
            m = {
                "HX_H": [f"{dnmn}Hx"],
                "HX_B": [f"{dnmn}Bx"],
                "HX_tw": [f"{dnmn}twx"],
                "HX_tf": [f"{dnmn}tfx"],
                "HYorCT_H": [f"{dnmn}Hy"],
                "HYorCT_B": [f"{dnmn}By"],
                "HYorCT_tw": [f"{dnmn}twy"],
                "HYorCT_tf": [f"{dnmn}tfy"],
                "鉄骨材料": [f"{dnmn}鉄骨材種x"],
                "鉄骨X方向記号（H形のみ）": [f"{dnmn}断面形状x"],
                "鉄骨Y方向記号（H形またはCT）": [f"{dnmn}断面形状y"],
            }
            for lp, ks in m.items():
                vv = _steelv(ks)
                if vv not in (None, ""):
                    out[lp] = vv
            out["HX_R"] = 0
            out["HYorCT_R"] = 0

        # S柱T型XU/XD/YL/YR
        elif "S柱T型XU" in fam:
            m = {
                "HX_H": [f"{dnmn}Hx"], "HX_B": [f"{dnmn}Bx"], "HX_tw": [f"{dnmn}twx"], "HX_tf": [f"{dnmn}tfx"],
                "HYorCT_H": [f"{dnmn}U_Hy"], "HYorCT_B": [f"{dnmn}U_By"], "HYorCT_tw": [f"{dnmn}U_twy"], "HYorCT_tf": [f"{dnmn}U_tfy"],
                "鉄骨材料": [f"{dnmn}鉄骨材種x"],
                "鉄骨X方向記号（H形のみ）": [f"{dnmn}断面形状x"],
                "鉄骨Y方向記号（H形またはCT）": [f"{dnmn}断面形状y"],
            }
            for lp, ks in m.items():
                vv = _steelv(ks)
                if vv not in (None, ""):
                    out[lp] = vv
        elif "S柱T型XD" in fam:
            m = {
                "HX_H": [f"{dnmn}Hx"], "HX_B": [f"{dnmn}Bx"], "HX_tw": [f"{dnmn}twx"], "HX_tf": [f"{dnmn}tfx"],
                "HYorCT_H": [f"{dnmn}D_Hy"], "HYorCT_B": [f"{dnmn}D_By"], "HYorCT_tw": [f"{dnmn}D_twy"], "HYorCT_tf": [f"{dnmn}D_tfy"],
                "鉄骨材料": [f"{dnmn}鉄骨材種x"],
                "鉄骨X方向記号（H形のみ）": [f"{dnmn}断面形状x"],
                "鉄骨Y方向記号（H形またはCT）": [f"{dnmn}断面形状y"],
            }
            for lp, ks in m.items():
                vv = _steelv(ks)
                if vv not in (None, ""):
                    out[lp] = vv
        elif "S柱T型YL" in fam:
            m = {
                "HX_H": [f"{dnmn}Hy"], "HX_B": [f"{dnmn}By"], "HX_tw": [f"{dnmn}twy"], "HX_tf": [f"{dnmn}tfy"],
                "HYorCT_H": [f"{dnmn}L_Hx"], "HYorCT_B": [f"{dnmn}L_Bx"], "HYorCT_tw": [f"{dnmn}L_twx"], "HYorCT_tf": [f"{dnmn}L_tfx"],
                "鉄骨材料": [f"{dnmn}鉄骨材種x"],
                "鉄骨X方向記号（H形のみ）": [f"{dnmn}断面形状x"],
                "鉄骨Y方向記号（H形またはCT）": [f"{dnmn}断面形状y"],
            }
            for lp, ks in m.items():
                vv = _steelv(ks)
                if vv not in (None, ""):
                    out[lp] = vv
        elif "S柱T型YR" in fam:
            m = {
                "HX_H": [f"{dnmn}Hy"], "HX_B": [f"{dnmn}By"], "HX_tw": [f"{dnmn}twy"], "HX_tf": [f"{dnmn}tfy"],
                "HYorCT_H": [f"{dnmn}R_Hx"], "HYorCT_B": [f"{dnmn}R_Bx"], "HYorCT_tw": [f"{dnmn}R_twx"], "HYorCT_tf": [f"{dnmn}R_tfx"],
                "鉄骨材料": [f"{dnmn}鉄骨材種x"],
                "鉄骨X方向記号（H形のみ）": [f"{dnmn}断面形状x"],
                "鉄骨Y方向記号（H形またはCT）": [f"{dnmn}断面形状y"],
            }
            for lp, ks in m.items():
                vv = _steelv(ks)
                if vv not in (None, ""):
                    out[lp] = vv

        # S柱HR型X / Y
        elif "S柱HR型X" in fam:
            m = {
                "HX_H": [f"{dnmn}Hx"], "HX_B": [f"{dnmn}Bx"], "HX_tw": [f"{dnmn}twx"], "HX_tf": [f"{dnmn}tfx"],
                "鉄骨材料": [f"{dnmn}鉄骨材種x"],
                "鉄骨X方向記号（H形のみ）": [f"{dnmn}断面形状x"],
            }
            for lp, ks in m.items():
                vv = _steelv(ks)
                if vv not in (None, ""):
                    out[lp] = vv
        elif "S柱HR型Y" in fam:
            m = {
                "HYorCT_H": [f"{dnmn}Hy"], "HYorCT_B": [f"{dnmn}By"], "HYorCT_tw": [f"{dnmn}twy"], "HYorCT_tf": [f"{dnmn}tfy"],
                "鉄骨材料": [f"{dnmn}鉄骨材種y"],
                "鉄骨Y方向記号（H形またはCT）": [f"{dnmn}断面形状y"],
            }
            for lp, ks in m.items():
                vv = _steelv(ks)
                if vv not in (None, ""):
                    out[lp] = vv

        # S柱BR/B型X, S柱BR/B型Y
        elif ("S柱BR型X" in fam) or ("S柱B型X" in fam):
            m = {
                "HX_B": [f"{dnmn}Bx_B"], "HX_H": [f"{dnmn}Bx_B"],
                "HX_tf": [f"{dnmn}tfx_B"], "HX_tw": [f"{dnmn}tfx_B"],
                "HYorCT_B": [f"{dnmn}Hx_B"], "HYorCT_H": [f"{dnmn}Hx_B"],
                "HYorCT_tf": [f"{dnmn}twx_B"], "HYorCT_tw": [f"{dnmn}twx_B"],
                "鉄骨材料": [f"{dnmn}鉄骨材種"],
            }
            for lp, ks in m.items():
                vv = _steelv(ks)
                if vv not in (None, ""):
                    out[lp] = vv
            fill = _steelv(["中詰コンクリート材種"])
            out["鉄骨「充填コンクリート材種」"] = fill if str(fill or "").strip() else "なし"
        elif ("S柱BR型Y" in fam) or ("S柱B型Y" in fam):
            m = {
                "HX_B": [f"{dnmn}By_B"], "HX_H": [f"{dnmn}By_B"],
                "HX_tf": [f"{dnmn}tfy_B"], "HX_tw": [f"{dnmn}tfy_B"],
                "HYorCT_B": [f"{dnmn}Hy_B"], "HYorCT_H": [f"{dnmn}Hy_B"],
                "HYorCT_tf": [f"{dnmn}twy_B"], "HYorCT_tw": [f"{dnmn}twy_B"],
                "鉄骨材料": [f"{dnmn}鉄骨材種"],
            }
            for lp, ks in m.items():
                vv = _steelv(ks)
                if vv not in (None, ""):
                    out[lp] = vv
            fill = _steelv(["中詰コンクリート材種"])
            out["鉄骨「充填コンクリート材種」"] = fill if str(fill or "").strip() else "なし"

        # S柱P型
        elif "S柱P型" in fam:
            m = {
                "HX_B": [f"{dnmn}Hx"], "HX_H": [f"{dnmn}Hx"],
                "HX_tf": [f"{dnmn}twx"], "HX_tw": [f"{dnmn}twx"],
                "鉄骨材料": [f"{dnmn}鉄骨材種"],
            }
            for lp, ks in m.items():
                vv = _steelv(ks)
                if vv not in (None, ""):
                    if lp == "鉄骨材料":
                        vv = _steel_mat_for_pipe(vv)
                    out[lp] = vv
            fill = _steelv(["中詰コンクリート材種"])
            out["鉄骨「充填コンクリート材種」"] = fill if str(fill or "").strip() else "なし"

    # 断面フラグは固定で書き換えない（Dynamo運用と競合しやすいため）
    return out


def _column_list_param_conv(param_name: str) -> str:
    if param_name in (
        "Dx",
        "Dy",
        "柱せい",
        "HX_B",
        "HX_H",
        "HX_R",
        "HX_tf",
        "HX_tw",
        "HYorCT_B",
        "HYorCT_H",
        "HYorCT_R",
        "HYorCT_tf",
        "HYorCT_tw",
        "躯体面から芯鉄筋X方向までの距離",
        "躯体面から芯鉄筋Y方向までの距離",
        "柱頭_帯筋ピッチ",
    ):
        return "num"
    if param_name in (
        "柱頭_X方向1段主筋本数",
        "柱頭_Y方向1段主筋本数",
        "X方向芯鉄筋本数",
        "Y方向芯鉄筋本数",
        "柱頭_帯筋X方向本数",
        "柱頭_帯筋Y方向本数",
        "柱頭断面",
        "柱脚断面",
    ):
        return "count"
    if param_name in ("柱頭_主筋径", "芯鉄筋径", "柱頭_帯筋径"):
        return "dia_text"
    return "str"


def _read_actual_for_list(values: Dict[str, Any], param_name: str) -> Any:
    if param_name not in values:
        return None
    conv = _column_list_param_conv(param_name)
    raw = values.get(param_name)
    if conv == "dia_text":
        return to_dia_text(raw)
    if conv == "str":
        if ("レベル" in param_name) or ("階" in param_name):
            return normalize_level_token(raw)
        if "断面形状" in param_name:
            return _normalize_steel_shape_code(raw)
        s = str(raw).strip() if raw is not None else ""
        return s if s != "" else None
    return convert(raw, conv)


def _convert_expected_for_list(param_name: str, expected: Any) -> Any:
    conv = _column_list_param_conv(param_name)
    if conv == "dia_text":
        return to_dia_text(expected)
    if conv == "str":
        if ("レベル" in param_name) or ("階" in param_name):
            return normalize_level_token(expected)
        if "断面形状" in param_name:
            return _normalize_steel_shape_code(expected)
        s = str(expected).strip() if expected is not None else ""
        return s if s != "" else None
    return convert(expected, conv)


def _normalize_kind_name_for_logic(kind_name: str) -> str:
    return str(kind_name or "").strip().lower()


def _column_list_family_group(fam: str) -> str:
    f = str(fam or "")
    has_src = ("SRC" in f) or ("【SRC】" in f)
    # RC専用表記は最優先でRC扱い
    if ("［RC］" in f) or ("[RC]" in f):
        return "rc"
    # SRC案件でも「...-RC角柱」はRCリスト群として扱う
    if ("-RC角柱" in f) or ("柱リスト改-RC角柱" in f):
        return "rc"
    if has_src:
        return "src"
    if ("RC柱リスト" in f) or (("RC" in f) and (not has_src)):
        return "rc"
    return "other"


def _column_expected_row_group(row: Dict[str, Any]) -> str:
    sec = str(row.get("__section") or "")
    if "SRC柱断面" in sec:
        return "src"
    if "RC柱断面" in sec:
        return "rc"
    return ""


def _collect_column_type_sources_by_level_symbol(base_url: str) -> Dict[str, Dict[Tuple[str, str], Dict[str, Any]]]:
    out: Dict[str, Dict[Tuple[str, str], Dict[str, Any]]] = {"rc": {}, "steel": {}}
    env = rpc(
        base_url,
        "element.get_structural_column_types",
        {"skip": 0, "count": 10000, "namesOnly": False, "failureHandling": {"enabled": True, "mode": "rollback"}},
    )
    types = _extract_types(env)
    for t in types:
        tid = _type_id_of(t)
        if tid <= 0:
            continue
        tname = _type_name_of(t)
        fam = str(t.get("familyName") or "")
        try:
            prm = rpc(base_url, "element.get_structural_column_type_parameters", {"typeId": int(tid)})
        except Exception:
            continue
        plist = prm.get("parameters") if isinstance(prm.get("parameters"), list) else []
        values: Dict[str, Any] = {}
        for p in plist:
            if not isinstance(p, dict):
                continue
            n = str(p.get("name") or "").strip()
            if not n:
                continue
            v = p.get("value")
            if n not in values or (values.get(n) in ("", None) and v not in ("", None)):
                values[n] = v
        lv_raw, sy_raw = _infer_level_symbol_from_type_values(tname, values)
        hlv, hsy, _ = _extract_level_symbol_hint_from_type_name(tname)
        if not lv_raw and hlv:
            lv_raw = hlv
        if not sy_raw and hsy:
            sy_raw = hsy
        lv = normalize_level_token(lv_raw)
        sy = normalize_symbol_token(sy_raw)
        if not lv or not sy:
            continue
        cls = _classify_src_column_type(fam, plist)
        if cls not in ("rc", "steel"):
            continue
        key = (lv, sy)
        rec = {"typeId": int(tid), "typeName": tname, "familyName": fam, "values": values}
        if key not in out[cls]:
            out[cls][key] = rec
    return out


def _frame_adjust_expected_by_counts(param_name: str, expected_val: Any, exp_map: Dict[str, Any]) -> Any:
    """
    梁系: 本数=0 のとき径は 0 扱いに寄せる。
    （CSV側は本数0でも径欄が埋まるケースがあり、Revit側0との不一致を抑止）
    """
    n = str(param_name or "").strip()

    if n == "始点側主筋太径":
        c = convert(exp_map.get("始点側上端1段筋太径本数"), "count")
        if c == 0:
            return 0
    elif n == "中央主筋太径":
        c = convert(exp_map.get("中央上端1段筋太径本数"), "count")
        if c == 0:
            return 0
    elif n == "終点側主筋太径":
        c = convert(exp_map.get("終点側上端1段筋太径本数"), "count")
        if c == 0:
            return 0
    elif n in ("始点側あばら筋径", "中央あばら筋径", "終点側あばら筋径"):
        stir_count = None
        for k in ("始点側あばら筋本数", "中央あばら筋本数", "終点側あばら筋本数", "あばら筋本数"):
            v = convert(exp_map.get(k), "count")
            if v is not None:
                stir_count = int(v)
                break
        if stir_count == 0:
            return 0
        # 本数パラメータが無い型でも、全ゾーンのピッチ=0なら実質「配置なし」とみなす
        pitch_vals = []
        for k in ("始点側あばら筋ピッチ", "中央あばら筋ピッチ", "終点側あばら筋ピッチ"):
            pv = convert(exp_map.get(k), "num")
            if pv is not None:
                pitch_vals.append(float(pv))
        if pitch_vals and all(abs(x) <= 1e-9 for x in pitch_vals):
            return 0

    return expected_val


def sync_column_list_families(
    base_url: str,
    expected_info_columns: Dict[str, Any],
    mode_apply: bool,
    family_mode: str = "auto",
) -> Dict[str, Any]:
    ret: Dict[str, Any] = {
        "ok": True,
        "label": "柱リスト用ファミリ",
        "types": 0,
        "candidateTypes": 0,
        "matchedTypes": 0,
        "diffs": [],
        "updated": [],
        "errors": [],
        "skippedNoLevelSymbol": 0,
        "skippedNoExpected": 0,
        "missingListTypeCount": 0,
        "missingListTypes": [],
        "srcFamilyDetected": False,
        "rcFamilyDetected": False,
    }

    exp_by_level_symbol: Dict[Tuple[str, str], Dict[str, Any]] = expected_info_columns.get("expectedByLevelSymbol") or {}
    exp_by_symbol: Dict[str, Dict[str, Any]] = expected_info_columns.get("expectedBySymbol") or {}
    src_maps = _collect_column_type_sources_by_level_symbol(base_url)
    rc_src_map = src_maps.get("rc") or {}
    steel_src_map = src_maps.get("steel") or {}
    # 期待行（section由来）をRC/SRCへ分ける
    expected_keys_by_group: Dict[str, Dict[Tuple[str, str], Dict[str, Any]]] = {"rc": {}, "src": {}}
    for r in expected_info_columns.get("chosenRows") or []:
        if not isinstance(r, dict):
            continue
        grp = _column_expected_row_group(r)
        if grp not in ("rc", "src"):
            continue
        lv = normalize_level_token(r.get("__placement_level") or r.get("__level") or "")
        sy = normalize_symbol_token(r.get("__placement_symbol") or r.get("__symbol") or "")
        if not lv or not sy:
            continue
        expected_keys_by_group[grp].setdefault((lv, sy), r)

    # モデル上で使用中の柱タイプ（RC/SRC判定用）
    # 既に取得済み rc_src_map/steel_src_map を使って軽量に判定する。
    used_keys_by_group: Dict[str, set] = {"rc": set(), "src": set()}
    try:
        used_type_ids = _collect_used_type_ids_for_kind(base_url, "columns") or set()
        key_classes: Dict[Tuple[str, str], set] = {}
        for key, rec in rc_src_map.items():
            tid = _to_count((rec or {}).get("typeId"))
            if tid is None or int(tid) not in used_type_ids:
                continue
            key_classes.setdefault((str(key[0]), str(key[1])), set()).add("rc")
        for key, rec in steel_src_map.items():
            tid = _to_count((rec or {}).get("typeId"))
            if tid is None or int(tid) not in used_type_ids:
                continue
            key_classes.setdefault((str(key[0]), str(key[1])), set()).add("steel")

        for key, classes in key_classes.items():
            if "rc" in classes and "steel" in classes:
                used_keys_by_group["src"].add(key)
            elif "rc" in classes:
                used_keys_by_group["rc"].add(key)
    except Exception as ex:
        ret["errors"].append({"stage": "collect_used_columns", "msg": str(ex)})

    types_env = rpc(
        base_url,
        "element.get_family_types",
        {"categoryName": "詳細項目", "skip": 0, "count": 10000, "namesOnly": False},
    )
    types = _extract_types(types_env)
    ret["types"] = len(types)
    existing_keys_by_group: Dict[str, set] = {"rc": set(), "src": set()}

    mode = str(family_mode or "auto").strip().lower()

    def _family_matches(fam: str) -> bool:
        f = str(fam or "")
        if "柱リスト" not in f:
            return False
        is_src = ("SRC" in f) or ("【SRC】" in f)
        is_rc = ("［RC］" in f) or ("[RC]" in f) or ("RC柱リスト" in f)

        if mode in ("both", "all"):
            return is_src or is_rc or ("柱リスト" in f)
        if mode == "src":
            return is_src
        if mode == "rc":
            return is_rc and (not is_src)
        # auto: SRC/RCの明示を優先し、どちらでもない「柱リスト」は対象に含める
        return is_src or is_rc or ("柱リスト" in f)

    for t in types:
        family_name = str(t.get("familyName") or "")
        if not _family_matches(family_name):
            continue
        if "SRC" in family_name or "【SRC】" in family_name:
            ret["srcFamilyDetected"] = True
        if "［RC］" in family_name or "[RC]" in family_name or "RC" in family_name:
            ret["rcFamilyDetected"] = True

        type_id = _type_id_of(t)
        type_name = _type_name_of(t)
        if type_id <= 0 or not type_name:
            continue

        ret["candidateTypes"] += 1

        level_raw, symbol_raw, _ = parse_level_symbol_from_type_name(type_name)
        if not level_raw or not symbol_raw:
            ret["skippedNoLevelSymbol"] += 1
            continue
        key_cur = (normalize_level_token(level_raw), normalize_symbol_token(symbol_raw))
        fam_grp = _column_list_family_group(family_name)
        if fam_grp in ("rc", "src"):
            existing_keys_by_group[fam_grp].add(key_cur)

        key = key_cur
        rc_src_rec = rc_src_map.get(key)
        steel_src_rec = steel_src_map.get(key)
        exp = _lookup_expected_by_level_symbol(exp_by_level_symbol, key_cur[0], key_cur[1])
        if not exp:
            exp = _lookup_expected_by_symbol(exp_by_symbol, symbol_raw, key_cur[1])
        if not exp:
            ret["skippedNoExpected"] += 1
            continue

        ret["matchedTypes"] += 1
        expected_values = build_column_list_expected(
            exp=exp,
            level_raw=level_raw,
            symbol_raw=symbol_raw,
            type_name=type_name,
            family_name=family_name,
            source_rc=(rc_src_rec.get("values") if isinstance(rc_src_rec, dict) else None),
            source_steel=(steel_src_rec.get("values") if isinstance(steel_src_rec, dict) else None),
        )

        try:
            cur = get_family_type_params(base_url, type_id)
            cur_values = cur.get("values") if isinstance(cur.get("values"), dict) else {}
        except Exception as ex:
            ret["errors"].append({"typeId": type_id, "typeName": type_name, "op": "read_params", "msg": str(ex)})
            continue

        for param_name, exp_v0 in expected_values.items():
            if param_name not in cur_values:
                continue
            expected_v = _convert_expected_for_list(param_name, exp_v0)
            if expected_v is None:
                continue
            actual_v = _read_actual_for_list(cur_values, param_name)
            if equal_with_param(param_name, actual_v, expected_v):
                continue
            diff = {
                "typeId": type_id,
                "typeName": type_name,
                "familyName": family_name,
                "level": normalize_level_token(level_raw),
                "symbol": normalize_symbol_token(symbol_raw),
                "param": param_name,
                "actual": actual_v,
                "expected": expected_v,
            }
            ret["diffs"].append(diff)
            if mode_apply:
                try:
                    u = rpc(
                        base_url,
                        "element.set_family_type_parameter",
                        {"typeId": int(type_id), "paramName": param_name, "value": expected_v},
                    )
                    if bool(u.get("ok", True)):
                        ret["updated"].append(diff)
                    else:
                        ret["errors"].append({"op": "update", "diff": diff, "msg": u.get("msg", "update failed")})
                except Exception as ex:
                    ret["errors"].append({"op": "update", "diff": diff, "msg": str(ex)})

    miss_seen: set = set()
    for grp in ("rc", "src"):
        for key in sorted(used_keys_by_group.get(grp) or set()):
            if key in (existing_keys_by_group.get(grp) or set()):
                continue
            row = expected_keys_by_group.get(grp, {}).get(key)
            if not row:
                continue
            mk = (grp, key[0], key[1])
            if mk in miss_seen:
                continue
            miss_seen.add(mk)
            lv_disp = str(row.get("__placement_level") or row.get("__level") or key[0])
            sy_disp = str(row.get("__placement_symbol") or row.get("__symbol") or key[1])
            ret["missingListTypes"].append(
                {
                    "group": grp,
                    "level": lv_disp,
                    "symbol": sy_disp,
                    "section": str(row.get("__section") or ""),
                    "reason": "LIST_TYPE_NOT_FOUND_FOR_USED_MEMBER",
                    "suggestedTypeName": f"{lv_disp}_{sy_disp}",
                }
            )
    ret["missingListTypeCount"] = len(ret["missingListTypes"])
    return ret


def _build_row_lookup(expected_info: Dict[str, Any]) -> Tuple[Dict[Tuple[str, str], Dict[str, Any]], Dict[str, List[Dict[str, Any]]]]:
    by_lv_sym: Dict[Tuple[str, str], Dict[str, Any]] = {}
    by_sym: Dict[str, List[Dict[str, Any]]] = {}
    rows = expected_info.get("chosenRows") or []
    for r in rows:
        if not isinstance(r, dict):
            continue
        lv = normalize_level_token(r.get("__placement_level") or r.get("__level") or "")
        sy = normalize_symbol_token(r.get("__placement_symbol") or r.get("__symbol") or "")
        if lv and sy and (lv, sy) not in by_lv_sym:
            by_lv_sym[(lv, sy)] = r
        if sy:
            by_sym.setdefault(sy, []).append(r)
    return by_lv_sym, by_sym


def _best_contained_symbol_key(candidate_keys: Iterable[str], revit_symbol_norm: str) -> str:
    rev = normalize_symbol_token(revit_symbol_norm)
    if not rev:
        return ""
    hit: List[str] = []
    for k in candidate_keys:
        kk = normalize_symbol_token(k)
        if kk and kk in rev:
            hit.append(kk)
    if not hit:
        return ""
    hit = sorted(set(hit), key=lambda x: len(x), reverse=True)
    if len(hit) >= 2 and len(hit[0]) == len(hit[1]):
        # 同長候補が複数ある場合は曖昧とみなして無効
        return ""
    return hit[0]


def _lookup_row_by_level_symbol(
    row_by_lv_sym: Dict[Tuple[str, str], Dict[str, Any]],
    level_norm: str,
    revit_symbol_norm: str,
) -> Optional[Dict[str, Any]]:
    lv = normalize_level_token(level_norm)
    sy = normalize_symbol_token(revit_symbol_norm)
    if not lv or not sy:
        return None
    row = row_by_lv_sym.get((lv, sy))
    if row is not None:
        return row
    # Revit符号にCSV符号が含まれるケース（例: C1_mirror -> C1）
    keys_same_level = [k2 for (lv2, k2) in row_by_lv_sym.keys() if lv2 == lv]
    best = _best_contained_symbol_key(keys_same_level, sy)
    if not best:
        return None
    return row_by_lv_sym.get((lv, best))


def _lookup_row_candidates_by_symbol(
    rows_by_sym: Dict[str, List[Dict[str, Any]]],
    revit_symbol_norm: str,
) -> List[Dict[str, Any]]:
    sy = normalize_symbol_token(revit_symbol_norm)
    if not sy:
        return []
    exact = rows_by_sym.get(sy) or []
    if exact:
        return exact
    best = _best_contained_symbol_key(rows_by_sym.keys(), sy)
    if not best:
        return []
    return rows_by_sym.get(best) or []


def _lookup_expected_by_level_symbol(
    expected_by_level_symbol: Dict[Tuple[str, str], Dict[str, Any]],
    level_norm: str,
    revit_symbol_norm: str,
) -> Optional[Dict[str, Any]]:
    lv = normalize_level_token(level_norm)
    sy = normalize_symbol_token(revit_symbol_norm)
    if not lv or not sy:
        return None
    exp = expected_by_level_symbol.get((lv, sy))
    if exp is not None:
        return exp
    keys_same_level = [k2 for (lv2, k2) in expected_by_level_symbol.keys() if lv2 == lv]
    best = _best_contained_symbol_key(keys_same_level, sy)
    if not best:
        return None
    return expected_by_level_symbol.get((lv, best))


def _lookup_expected_by_symbol(
    expected_by_symbol: Dict[str, Dict[str, Any]],
    symbol_raw: str,
    symbol_norm: str,
) -> Optional[Dict[str, Any]]:
    if symbol_raw in expected_by_symbol:
        return expected_by_symbol.get(symbol_raw)
    sy = normalize_symbol_token(symbol_norm)
    if sy in expected_by_symbol:
        return expected_by_symbol.get(sy)
    # キーを正規化して再評価
    norm_map: Dict[str, Dict[str, Any]] = {}
    for k, v in expected_by_symbol.items():
        kk = normalize_symbol_token(k)
        if kk and kk not in norm_map:
            norm_map[kk] = v
    if sy in norm_map:
        return norm_map.get(sy)
    best = _best_contained_symbol_key(norm_map.keys(), sy)
    if not best:
        return None
    return norm_map.get(best)


def _pick_first_row_value(row: Dict[str, Any], keys: List[str]) -> Any:
    for k in keys:
        rk = pick_first_key(row, [k])
        if rk and str(row.get(rk, "")).strip() != "":
            return row.get(rk)
    return None


def _beam_list_zone_from_type_name(type_name: str) -> str:
    t = str(type_name or "")
    if "始端" in t:
        return "始端"
    if "終端" in t:
        return "終端"
    if "中央" in t:
        return "中央"
    return "全断面"


def _row_num(row: Dict[str, Any], keys: List[str]) -> Optional[float]:
    return convert(_pick_first_row_value(row, keys), "num")


def _row_count(row: Dict[str, Any], keys: List[str]) -> Optional[int]:
    return convert(_pick_first_row_value(row, keys), "count")


def _row_str(row: Dict[str, Any], keys: List[str]) -> Optional[str]:
    v = convert(_pick_first_row_value(row, keys), "str")
    return str(v).strip() if v is not None else None


def _row_dia_text(row: Dict[str, Any], keys: List[str]) -> Optional[str]:
    return to_dia_text(_pick_first_row_value(row, keys))


def _set_if(out: Dict[str, Any], key: str, value: Any) -> None:
    if value is None:
        return
    if isinstance(value, str) and value.strip() == "":
        return
    out[key] = value


def _set_many_if(out: Dict[str, Any], keys: List[str], value: Any) -> None:
    for k in keys:
        _set_if(out, k, value)


def _join_stirrup_mark_and_dia(mark: Optional[str], dia_text: Optional[str]) -> Optional[str]:
    if not dia_text:
        return None
    m = str(mark or "").strip()
    if not m:
        return dia_text
    n = _to_bar_dia(dia_text)
    if n is not None:
        return f"{m}{int(n)}"
    return f"{m}{dia_text}"


def _beam_steel_side_shape(row: Dict[str, Any], side: str) -> Dict[str, Any]:
    s = str(side or "").strip().lower()
    if s == "left":
        reg = _pick_first_row_value(row, ["鉄骨登録形状_左端", "鉄骨登録形状_左"])
        typ = _pick_first_row_value(row, ["鉄骨登録形状_タイプ左", "始点側断面形状", "左端断面形状"])
    elif s == "right":
        reg = _pick_first_row_value(row, ["鉄骨登録形状_右端", "鉄骨登録形状_右"])
        typ = _pick_first_row_value(row, ["鉄骨登録形状_タイプ右", "終点側断面形状", "右端断面形状"])
    else:
        reg = _pick_first_row_value(row, ["鉄骨登録形状_中央", "鉄骨登録形状"])
        typ = _pick_first_row_value(row, ["鉄骨登録形状_タイプ中", "中央断面形状", "S梁断面形状"])
    return parse_registered_steel_shape(reg, str(typ or ""))


def _beam_steel_side_material(row: Dict[str, Any], side: str) -> Optional[str]:
    s = str(side or "").strip().lower()
    if s == "left":
        return _row_str(row, ["フランジ材料_左端", "ウェブ材料_左端", "始点側鉄骨材種"])
    if s == "right":
        return _row_str(row, ["フランジ材料_右端", "ウェブ材料_右端", "終点側鉄骨材種"])
    return _row_str(row, ["フランジ材料_中央", "ウェブ材料_中央", "鉄骨材種"])


def _fill_beam_list_steel_expected(out: Dict[str, Any], row: Dict[str, Any]) -> None:
    l = _beam_steel_side_shape(row, "left")
    c = _beam_steel_side_shape(row, "center")
    r = _beam_steel_side_shape(row, "right")

    # 鉄骨形状が中央しか無いケースは左右へ展開（Dynamoの同一断面分岐相当）
    if not l and c:
        l = dict(c)
    if not r and c:
        r = dict(c)

    ml = _beam_steel_side_material(row, "left")
    mc = _beam_steel_side_material(row, "center")
    mr = _beam_steel_side_material(row, "right")
    if not ml:
        ml = mc
    if not mr:
        mr = mc

    for prefix, shp, mat in (("始端", l, ml), ("中央", c, mc), ("終端", r, mr)):
        if not isinstance(shp, dict) or not shp:
            continue
        _set_if(out, f"{prefix}_鉄骨H", convert(shp.get("H"), "num"))
        _set_if(out, f"{prefix}_鉄骨B", convert(shp.get("B"), "num"))
        _set_if(out, f"{prefix}_鉄骨tw", convert(shp.get("tw"), "num"))
        _set_if(out, f"{prefix}_鉄骨tf", convert(shp.get("tf"), "num"))
        _set_if(out, f"{prefix}_鉄骨r", convert(shp.get("R"), "num"))
        _set_if(out, f"{prefix}_鉄骨記号", convert(shp.get("code"), "str"))
        _set_if(out, f"{prefix}_鉄骨材料", mat)
        _set_if(out, f"{prefix}_鉄骨あり", 1)


def build_beam_list_expected(row: Dict[str, Any], level_raw: str, symbol_raw: str, type_name: str) -> Dict[str, Any]:
    zone = _beam_list_zone_from_type_name(type_name)
    out: Dict[str, Any] = {
        "符号": symbol_raw,
        "レベル": str(level_raw or "").strip(),
    }

    # 1) 梁断面寸法（左/中/右）
    b_l = _row_num(row, ["コンクリート_左端B", "コンクリート_左端b", "左B", "B", "b", "コンクリート_中央B", "コンクリート_中央b"])
    b_c = _row_num(row, ["コンクリート_中央B", "コンクリート_中央b", "B", "b"])
    b_r = _row_num(row, ["コンクリート_右端B", "コンクリート_右端b", "右B", "B", "b", "コンクリート_中央B", "コンクリート_中央b"])
    d_l = _row_num(row, ["コンクリート_左端D", "左D", "D", "コンクリート_中央D"])
    d_c = _row_num(row, ["コンクリート_中央D", "D"])
    d_r = _row_num(row, ["コンクリート_右端D", "右D", "D", "コンクリート_中央D"])

    if b_l is not None:
        out["B_s"] = b_l
    if b_c is not None:
        out["B_c"] = b_c
    if b_r is not None:
        out["B_e"] = b_r
    if d_l is not None:
        out["D_s"] = d_l
    if d_c is not None:
        out["D_c"] = d_c
    if d_r is not None:
        out["D_e"] = d_r

    # 2) ハンチ長
    l_len = _row_num(row, ["ハンチ_左端", "ハンチ 左端", "左Len"])
    r_len = _row_num(row, ["ハンチ_右端", "ハンチ 右端", "右Len"])
    if l_len is not None:
        out["左Len"] = l_len
    if r_len is not None:
        out["右Len"] = r_len

    # 3) 同名/基本コピー
    _set_if(out, "備考（断面リスト）", _row_str(row, ["備考（断面リスト）"]))
    _set_if(out, "中央主筋太径種別", _row_str(row, ["主筋材料_中央上", "中央主筋太径種別"]))
    _set_if(out, "始点側主筋太径種別", _row_str(row, ["主筋材料_左上", "始点側主筋太径種別"]))
    _set_if(out, "終点側主筋太径種別", _row_str(row, ["主筋材料_右上", "終点側主筋太径種別"]))
    _set_if(out, "中央主筋細径種別", _row_str(row, ["主筋2材料_中央上", "中央主筋細径種別"]))
    _set_if(out, "始点側主筋細径種別", _row_str(row, ["主筋2材料_左上", "始点側主筋細径種別"]))
    _set_if(out, "終点側主筋細径種別", _row_str(row, ["主筋2材料_右上", "終点側主筋細径種別"]))
    _set_if(out, "中央あばら筋種別", _row_str(row, ["あばら筋材料_中央", "中央あばら筋種別"]))
    _set_if(out, "始点側あばら筋種別", _row_str(row, ["あばら筋材料_左端", "始点側あばら筋種別"]))
    _set_if(out, "終点側あばら筋種別", _row_str(row, ["あばら筋材料_右端", "終点側あばら筋種別"]))
    _set_if(out, "中央あばら筋記号", _row_str(row, ["あばら筋記号_中央", "中央あばら筋記号"]))
    _set_if(out, "始点側あばら筋記号", _row_str(row, ["あばら筋記号_左端", "始点側あばら筋記号"]))
    _set_if(out, "終点側あばら筋記号", _row_str(row, ["あばら筋記号_右端", "終点側あばら筋記号"]))

    # 4) 主筋本数（1/2/3段）
    def _set_rebar_counts(prefix: str, side_token: str) -> None:
        _set_if(out, f"{prefix}_上端筋_1段主筋本数", _row_count(row, [f"主筋本数_{side_token}上", f"{prefix}上端1段筋太径本数"]))
        _set_if(out, f"{prefix}_下端筋_1段主筋本数", _row_count(row, [f"主筋本数_{side_token}下", f"{prefix}下端1段筋太径本数"]))
        _set_if(out, f"{prefix}_上端筋_2段主筋本数", _row_count(row, [f"主筋2本数_{side_token}上", f"{prefix}上端2段筋太径本数"]))
        _set_if(out, f"{prefix}_下端筋_2段主筋本数", _row_count(row, [f"主筋2本数_{side_token}下", f"{prefix}下端2段筋太径本数"]))
        _set_if(out, f"{prefix}_上端筋_3段主筋本数", _row_count(row, [f"主筋3本数_{side_token}上", f"{prefix}上端3段筋太径本数"]))
        _set_if(out, f"{prefix}_下端筋_3段主筋本数", _row_count(row, [f"主筋3本数_{side_token}下", f"{prefix}下端3段筋太径本数"]))

    _set_rebar_counts("始端", "左")
    _set_rebar_counts("中央", "中央")
    _set_rebar_counts("終端", "右")

    # 5) 主筋径（D表記）
    _set_if(out, "中央_上端筋径", _row_dia_text(row, ["主筋径_中央上", "中央主筋太径"]))
    _set_if(out, "中央_下端筋径", _row_dia_text(row, ["主筋径_中央下", "中央主筋太径"]))
    _set_if(out, "始端_上端筋径", _row_dia_text(row, ["主筋径_左上", "始点側主筋太径"]))
    _set_if(out, "始端_下主筋径", _row_dia_text(row, ["主筋径_左下", "始点側主筋太径"]))
    _set_if(out, "終端_上端筋径", _row_dia_text(row, ["主筋径_右上", "終点側主筋太径"]))
    _set_if(out, "終端_下端筋径", _row_dia_text(row, ["主筋径_右下", "終点側主筋太径"]))

    # 6) 主筋種類
    _set_if(out, "主筋_種類と記号", _row_str(row, ["主筋材料_中央上", "中央主筋太径種別"]))

    # 7) あばら筋（径は記号を付加。記号なしならDxx）
    for tgt_prefix, csv_side, mark_param_name in (
        ("始端", "左端", "始点側あばら筋記号"),
        ("中央", "中央", "中央あばら筋記号"),
        ("終端", "右端", "終点側あばら筋記号"),
    ):
        mark = _row_str(row, [f"あばら筋記号_{csv_side}", mark_param_name])
        dia = _row_dia_text(row, [f"あばら筋{csv_side}_径", f"{'始点側' if tgt_prefix=='始端' else '終点側' if tgt_prefix=='終端' else '中央'}あばら筋径"])
        _set_if(out, f"{tgt_prefix}_肋筋径", _join_stirrup_mark_and_dia(mark, dia))
        _set_if(out, f"{tgt_prefix}_肋筋ピッチ", _row_num(row, [f"あばら筋{csv_side}_ピッチ"]))
        _set_if(out, f"{tgt_prefix}_肋筋本数", _row_count(row, [f"あばら筋{csv_side}_本数"]))

    # 8) 腹筋（アナログ入力）
    f_dia = _row_dia_text(row, ["腹筋径"])
    f_cnt = _row_count(row, ["腹筋本数"])
    _set_if(out, "端部腹筋径_アナログ入力", f_dia)
    if f_cnt is not None:
        _set_if(out, "端部腹筋本数_アナログ入力", int(f_cnt) * 2)

    # 9) SRC鉄骨部（SRC梁リストでもRC梁リストでも、存在する項目だけ更新される）
    _fill_beam_list_steel_expected(out, row)

    sym_u = normalize_symbol_token(symbol_raw)
    out["基礎梁"] = 1 if sym_u.startswith("FG") else 0

    # ゾーンヒント
    out["端部・中央"] = 1 if zone in ("始端", "終端", "中央") else 0
    return out


def _beam_list_family_matches(fam: str, mode: str) -> bool:
    f = str(fam or "")
    if "梁リスト" not in f:
        return False
    is_src = ("SRC" in f) or ("【SRC】" in f)
    is_rc = ("［RC］" in f) or ("[RC]" in f) or ("RC梁" in f)
    md = str(mode or "auto").strip().lower()
    if md in ("both", "all"):
        return is_src or is_rc or ("梁リスト" in f)
    if md == "src":
        return is_src
    if md == "rc":
        return is_rc and (not is_src)
    return is_src or is_rc or ("梁リスト" in f)


def _beam_list_family_group(fam: str) -> str:
    f = str(fam or "")
    has_src = ("SRC" in f) or ("【SRC】" in f)
    if ("［RC］" in f) or ("[RC]" in f):
        return "rc"
    # SRC案件でも「...-RC梁」はRCリスト群として扱う
    if ("-RC梁" in f) or ("梁リスト改-RC梁" in f):
        return "rc"
    if has_src:
        return "src"
    if (("RC梁" in f) or ("RC" in f)) and (not has_src):
        return "rc"
    return "other"


def _beam_expected_row_group(row: Dict[str, Any]) -> str:
    sec = str(row.get("__section") or "")
    if "SRC梁断面" in sec:
        return "src"
    if ("RC梁断面" in sec) or ("RC小梁断面" in sec):
        return "rc"
    return ""


def _beam_list_param_conv(param_name: str) -> str:
    n = str(param_name or "")
    if n in ("B_s", "B_c", "B_e", "D_s", "D_c", "D_e", "左Len", "右Len"):
        return "num"
    if "鉄骨" in n:
        if "あり" in n:
            return "count"
        if ("材料" in n) or ("記号" in n):
            return "str"
        return "num"
    if "本数" in n or n in ("基礎梁", "端部・中央"):
        return "count"
    if ("種別" in n) or ("材料" in n) or ("記号" in n):
        return "str"
    if "径" in n:
        return "dia_text"
    if "ピッチ" in n or "かぶり" in n or "あき" in n or "dt" in n:
        return "num"
    return "str"


def _read_actual_for_beam_list(values: Dict[str, Any], param_name: str) -> Any:
    if param_name not in values:
        return None
    conv = _beam_list_param_conv(param_name)
    raw = values.get(param_name)
    if conv == "dia_text":
        return to_dia_text(raw)
    if conv == "str":
        s = str(raw).strip() if raw is not None else ""
        return s if s != "" else None
    return convert(raw, conv)


def _convert_expected_for_beam_list(param_name: str, expected: Any) -> Any:
    conv = _beam_list_param_conv(param_name)
    if conv == "dia_text":
        return to_dia_text(expected)
    if conv == "str":
        s = str(expected).strip() if expected is not None else ""
        return s if s != "" else None
    return convert(expected, conv)


def sync_beam_list_families(
    base_url: str,
    expected_info_frames: Dict[str, Any],
    mode_apply: bool,
    family_mode: str = "auto",
) -> Dict[str, Any]:
    ret: Dict[str, Any] = {
        "ok": True,
        "label": "梁リスト用ファミリ",
        "types": 0,
        "candidateTypes": 0,
        "matchedTypes": 0,
        "diffs": [],
        "updated": [],
        "errors": [],
        "skippedNoLevelSymbol": 0,
        "skippedNoExpected": 0,
        "missingListTypeCount": 0,
        "missingListTypes": [],
        "srcFamilyDetected": False,
        "rcFamilyDetected": False,
    }

    row_by_lv_sym, rows_by_sym = _build_row_lookup(expected_info_frames)
    # 実際にモデル上で使われている梁タイプ（レベル+符号）の集合
    used_frame_keys_by_group: Dict[str, set] = {"rc": set(), "src": set(), "all": set()}
    try:
        used_type_ids = _collect_used_type_ids_for_kind(base_url, "frames")
        env_used_types = rpc(
            base_url,
            "element.get_structural_frame_types",
            {"skip": 0, "count": 10000, "failureHandling": {"enabled": True, "mode": "rollback"}},
        )
        used_types = _extract_types(env_used_types)
        for ut in used_types:
            tid = _type_id_of(ut)
            if tid <= 0 or (used_type_ids is not None and int(tid) not in used_type_ids):
                continue
            lv_raw, sy_raw, _ = parse_level_symbol_from_type_name(_type_name_of(ut))
            lv = normalize_level_token(lv_raw)
            sy = normalize_symbol_token(sy_raw)
            if not lv or not sy:
                continue
            key = (lv, sy)
            grp = _beam_list_family_group(str(ut.get("familyName") or ""))
            used_frame_keys_by_group["all"].add(key)
            if grp in ("rc", "src"):
                used_frame_keys_by_group[grp].add(key)
    except Exception as ex:
        ret["errors"].append({"stage": "collect_used_frames", "msg": str(ex)})

    types_env = rpc(
        base_url,
        "element.get_family_types",
        {"categoryName": "詳細項目", "skip": 0, "count": 10000, "namesOnly": False},
    )
    types = _extract_types(types_env)
    ret["types"] = len(types)
    existing_keys_by_group: Dict[str, set] = {"rc": set(), "src": set(), "all": set()}

    mode = str(family_mode or "auto").strip().lower()

    for t in types:
        family_name = str(t.get("familyName") or "")
        if not _beam_list_family_matches(family_name, mode):
            continue
        if "SRC" in family_name or "【SRC】" in family_name:
            ret["srcFamilyDetected"] = True
        if "［RC］" in family_name or "[RC]" in family_name or "RC" in family_name:
            ret["rcFamilyDetected"] = True

        type_id = _type_id_of(t)
        type_name = _type_name_of(t)
        if type_id <= 0:
            continue

        ret["candidateTypes"] += 1

        try:
            cur = get_family_type_params(base_url, type_id)
            cur_values = cur.get("values") if isinstance(cur.get("values"), dict) else {}
        except Exception as ex:
            ret["errors"].append({"typeId": type_id, "typeName": type_name, "op": "read_params", "msg": str(ex)})
            continue

        alias_name = str(cur_values.get("タイプ名") or cur_values.get("Type Name") or "").strip()
        src_name = alias_name or type_name
        level_raw, symbol_raw, _suffix = parse_level_symbol_from_type_name(src_name)
        if not level_raw or not symbol_raw:
            ret["skippedNoLevelSymbol"] += 1
            continue
        key_cur = (normalize_level_token(level_raw), normalize_symbol_token(symbol_raw))
        fam_grp = _beam_list_family_group(family_name)
        existing_keys_by_group["all"].add(key_cur)
        if fam_grp in ("rc", "src"):
            existing_keys_by_group[fam_grp].add(key_cur)

        key = key_cur
        row = _lookup_row_by_level_symbol(row_by_lv_sym, key[0], key[1])
        if not row:
            cands = _lookup_row_candidates_by_symbol(rows_by_sym, normalize_symbol_token(symbol_raw))
            row = cands[0] if len(cands) == 1 else None
        if not row:
            ret["skippedNoExpected"] += 1
            continue

        ret["matchedTypes"] += 1
        expected_values = build_beam_list_expected(row, level_raw, symbol_raw, src_name)

        for param_name, exp_v0 in expected_values.items():
            if param_name not in cur_values:
                continue
            expected_v = _convert_expected_for_beam_list(param_name, exp_v0)
            if expected_v is None:
                continue
            actual_v = _read_actual_for_beam_list(cur_values, param_name)
            if equal_with_param(param_name, actual_v, expected_v):
                continue
            diff = {
                "typeId": type_id,
                "typeName": type_name,
                "familyName": family_name,
                "level": normalize_level_token(level_raw),
                "symbol": normalize_symbol_token(symbol_raw),
                "param": param_name,
                "actual": actual_v,
                "expected": expected_v,
            }
            ret["diffs"].append(diff)
            if mode_apply:
                try:
                    u = rpc(
                        base_url,
                        "element.set_family_type_parameter",
                        {"typeId": int(type_id), "paramName": param_name, "value": expected_v},
                    )
                    if bool(u.get("ok", True)):
                        ret["updated"].append(diff)
                    else:
                        ret["errors"].append({"op": "update", "diff": diff, "msg": u.get("msg", "update failed")})
                except Exception as ex:
                    ret["errors"].append({"op": "update", "diff": diff, "msg": str(ex)})

    # RC/SRC部材が存在するのに、対応する梁リストタイプが無いケースを明示化
    miss_seen: set = set()
    for r in expected_info_frames.get("chosenRows") or []:
        if not isinstance(r, dict):
            continue
        grp = _beam_expected_row_group(r)
        if grp not in ("rc", "src"):
            continue
        lv = normalize_level_token(r.get("__placement_level") or r.get("__level") or "")
        sy = normalize_symbol_token(r.get("__placement_symbol") or r.get("__symbol") or "")
        if not lv or not sy:
            continue
        key = (lv, sy)
        # モデル上で使用中の梁に限定して欠落判定
        used_keys = used_frame_keys_by_group.get(grp) or set()
        if key not in used_keys:
            continue
        existing_keys = existing_keys_by_group.get(grp) or set()
        if key in existing_keys:
            continue
        mk = (grp, lv, sy)
        if mk in miss_seen:
            continue
        miss_seen.add(mk)
        level_disp = str(r.get("__placement_level") or r.get("__level") or lv)
        symbol_disp = str(r.get("__placement_symbol") or r.get("__symbol") or sy)
        ret["missingListTypes"].append(
            {
                "group": grp,
                "level": level_disp,
                "symbol": symbol_disp,
                "section": str(r.get("__section") or ""),
                "reason": "LIST_TYPE_NOT_FOUND_FOR_USED_MEMBER",
                "suggestedTypeName": f"{level_disp}_{symbol_disp}",
            }
        )

    ret["missingListTypeCount"] = len(ret["missingListTypes"])
    return ret


def sync_frame_instances_from_expected(
    base_url: str,
    kind_cfg: Dict[str, Any],
    expected_info_frames: Dict[str, Any],
    mode_apply: bool,
) -> Dict[str, Any]:
    ret: Dict[str, Any] = {
        "ok": True,
        "label": "構造フレーム インスタンス",
        "instances": 0,
        "resolvedInstances": 0,
        "matchedInstances": 0,
        "skippedNoExpected": 0,
        "skippedNoSymbol": 0,
        "skippedNoTargetParams": 0,
        "diffs": [],
        "updated": [],
        "errors": [],
    }

    inst_map = kind_cfg.get("instanceParamMap") if isinstance(kind_cfg.get("instanceParamMap"), list) else []
    if not inst_map:
        ret["ok"] = True
        ret["skipped"] = True
        ret["reason"] = "instanceParamMap_not_configured"
        return ret

    row_by_lv_sym, rows_by_sym = _build_row_lookup(expected_info_frames)
    sym_cands = [str(x) for x in (kind_cfg.get("symbolParamCandidates") or []) if str(x).strip()]

    frames: List[Dict[str, Any]] = []
    skip = 0
    count = 1000
    max_pages = 500
    for _ in range(max_pages):
        env = rpc(
            base_url,
            "element.get_structural_frames",
            {"skip": int(skip), "count": int(count), "failureHandling": {"enabled": True, "mode": "rollback"}},
        )
        page = env.get("structuralFrames") if isinstance(env.get("structuralFrames"), list) else []
        if not page:
            break
        frames.extend([x for x in page if isinstance(x, dict)])
        got = len(page)
        total = _to_count(env.get("totalCount"))
        skip += got
        if got < count:
            break
        if total is not None and skip >= total:
            break
    ret["instances"] = len(frames)

    type_cache: Dict[int, Dict[str, Any]] = {}

    for it in frames:
        eid = _to_count(it.get("elementId"))
        tid = _to_count(it.get("typeId"))
        if not eid or not tid:
            continue

        tvals = type_cache.get(int(tid))
        if tvals is None:
            read_params = sorted(set(sym_cands + [str(m.get("revit") or "") for m in inst_map if str(m.get("revit") or "")]))
            tvals = get_type_params_bulk(base_url, int(tid), read_params)
            type_cache[int(tid)] = tvals

        sym = select_symbol_from_type({"typeName": str(it.get("typeName") or "")}, tvals, sym_cands)
        if not sym:
            ret["skippedNoSymbol"] += 1
            continue
        lv_raw = str(it.get("level") or "")
        key = (normalize_level_token(lv_raw), normalize_symbol_token(sym))
        row = _lookup_row_by_level_symbol(row_by_lv_sym, key[0], key[1])
        if not row:
            cands = _lookup_row_candidates_by_symbol(rows_by_sym, normalize_symbol_token(sym))
            row = cands[0] if len(cands) == 1 else None
        if not row:
            ret["skippedNoExpected"] += 1
            continue
        ret["resolvedInstances"] += 1

        try:
            prm_env = rpc(
                base_url,
                "element.get_structural_frame_parameters",
                {"elementId": int(eid), "namesOnly": False, "skip": 0, "count": 5000},
            )
            plist = prm_env.get("parameters") if isinstance(prm_env.get("parameters"), list) else []
        except Exception as ex:
            ret["errors"].append({"elementId": int(eid), "op": "read_instance_params", "msg": str(ex)})
            continue

        cur_values: Dict[str, Any] = {}
        ro_flags: Dict[str, bool] = {}
        for p in plist:
            if not isinstance(p, dict):
                continue
            pn = str(p.get("name") or "").strip()
            if not pn:
                continue
            cur_values[pn] = p.get("value")
            ro_flags[pn] = bool(p.get("isReadOnly", False))

        matched_for_this = False
        has_target_param = False
        for m in inst_map:
            rname = str(m.get("revit") or "").strip()
            csv_candidates = [str(x) for x in (m.get("csv") or []) if str(x).strip()]
            conv = str(m.get("converter") or "str")
            if not rname or not csv_candidates:
                continue
            if rname not in cur_values:
                continue
            has_target_param = True
            if ro_flags.get(rname, False):
                continue
            expected_raw = _pick_first_row_value(row, csv_candidates)
            if expected_raw is None:
                continue
            expected = convert(expected_raw, conv)
            actual = convert(cur_values.get(rname), conv)
            if equal_with_param(rname, actual, expected):
                continue
            matched_for_this = True
            diff = {
                "elementId": int(eid),
                "typeId": int(tid),
                "typeName": str(it.get("typeName") or ""),
                "familyName": str(it.get("familyName") or ""),
                "symbol": normalize_symbol_token(sym),
                "level": normalize_level_token(lv_raw),
                "param": rname,
                "actual": actual,
                "expected": expected,
            }
            ret["diffs"].append(diff)
            if mode_apply:
                try:
                    u = rpc(
                        base_url,
                        "element.update_structural_frame_parameter",
                        {"elementId": int(eid), "paramName": rname, "value": expected},
                    )
                    if bool(u.get("ok", True)):
                        ret["updated"].append(diff)
                    else:
                        ret["errors"].append({"op": "update", "diff": diff, "msg": u.get("msg", "update failed")})
                except Exception as ex:
                    ret["errors"].append({"op": "update", "diff": diff, "msg": str(ex)})
        if matched_for_this:
            ret["matchedInstances"] += 1
        if not has_target_param:
            ret["skippedNoTargetParams"] += 1

    return ret


def _pick_text(values: Dict[str, Any], keys: List[str]) -> str:
    for k in keys:
        v = values.get(k)
        if v is None:
            continue
        s = str(v).strip()
        if s != "":
            return s
    return ""


def parse_registered_steel_shape(text: Any, code_hint: str = "") -> Dict[str, Any]:
    s = str(text or "").strip()
    if not s:
        return {}
    if "-" in s:
        code_raw, rest = s.split("-", 1)
    else:
        code_raw, rest = "", s
    code = str(code_raw).strip()
    hint = str(code_hint or "").strip()
    if not code and hint in ("□", "○"):
        code = hint
    code_u = code.upper()
    nums = [_to_num(x) for x in re.findall(r"[-+]?\d+(?:\.\d+)?", rest)]
    vals = [float(x) for x in nums if x is not None]
    out: Dict[str, Any] = {"raw": s, "code": code or code_u}

    if code in ("□",):
        if len(vals) >= 1:
            out["H"] = vals[0]
        if len(vals) >= 2:
            out["B"] = vals[1]
        if len(vals) >= 3:
            out["tw"] = vals[2]
            out["tf"] = vals[2]
        if len(vals) >= 4:
            out["R"] = vals[3]
        return out

    if code in ("○",):
        if len(vals) >= 1:
            out["H"] = vals[0]
            out["B"] = vals[0]
        if len(vals) >= 2:
            out["tw"] = vals[1]
            out["tf"] = vals[1]
        return out

    # H / SH / BH / I / Ｉ 系
    if len(vals) >= 1:
        out["H"] = vals[0]
    if len(vals) >= 2:
        out["B"] = vals[1]
    if len(vals) >= 3:
        out["tw"] = vals[2]
    if len(vals) >= 4:
        out["tf"] = vals[3]
    if len(vals) >= 5:
        out["R"] = vals[4]
    return out


def _shape_for_axis(row: Dict[str, Any], axis: str, pos: str = "") -> Dict[str, Any]:
    a = axis.lower()
    p = str(pos or "").strip().lower()
    pos_suffix = ""
    if p in ("top", "head", "start", "柱頭"):
        pos_suffix = "柱頭"
    elif p in ("bottom", "foot", "end", "柱脚"):
        pos_suffix = "柱脚"

    def _k(base: str) -> List[str]:
        if pos_suffix:
            return [f"{base}_{pos_suffix}", f"{base}{pos_suffix}", base]
        return [base]

    pos_shape_keys_x: List[str] = []
    pos_shape_keys_y: List[str] = []
    if pos_suffix == "柱脚":
        pos_shape_keys_x = ["柱脚断面_登録形状X", "柱脚断面X", "柱脚断面_登録形状", "柱脚断面"]
        pos_shape_keys_y = ["柱脚断面_登録形状Y", "柱脚断面Y", "柱脚断面_登録形状", "柱脚断面"]
    elif pos_suffix == "柱頭":
        pos_shape_keys_x = ["柱頭断面_登録形状X", "柱頭断面X", "柱頭断面_登録形状", "柱頭断面"]
        pos_shape_keys_y = ["柱頭断面_登録形状Y", "柱頭断面Y", "柱頭断面_登録形状", "柱頭断面"]

    reg = ""
    shape_hint = str(row.get("鉄骨形状") or "").strip()
    if a == "x":
        reg = _pick_text(
            row,
            pos_shape_keys_x
            + pos_shape_keys_y
            + _k("鉄骨断面_登録形状X")
            + _k("鉄骨断面X")
            + _k("鉄骨断面_登録形状")
            + _k("鉄骨断面"),
        )
        if not reg:
            reg = _pick_text(row, _k("鉄骨断面_登録形状Y") + _k("鉄骨断面Y"))
    else:
        reg = _pick_text(
            row,
            pos_shape_keys_y
            + pos_shape_keys_x
            + _k("鉄骨断面_登録形状Y")
            + _k("鉄骨断面Y")
            + _k("鉄骨断面_登録形状")
            + _k("鉄骨断面"),
        )
        if not reg:
            reg = _pick_text(row, _k("鉄骨断面_登録形状X") + _k("鉄骨断面X"))
    return parse_registered_steel_shape(reg, shape_hint)


def _steel_material_for_axis(row: Dict[str, Any], axis: str, pos: str = "") -> str:
    a = axis.lower()
    p = str(pos or "").strip().lower()
    pos_suffix = ""
    if p in ("top", "head", "start", "柱頭"):
        pos_suffix = "柱頭"
    elif p in ("bottom", "foot", "end", "柱脚"):
        pos_suffix = "柱脚"

    def _mk(base: str) -> List[str]:
        if pos_suffix:
            return [f"{base}_{pos_suffix}", f"{base}{pos_suffix}", base]
        return [base]

    pos_keys_x: List[str] = []
    pos_keys_y: List[str] = []
    if pos_suffix == "柱脚":
        pos_keys_x = ["柱脚材料_フランジX", "柱脚材料_ウェブX"]
        pos_keys_y = ["柱脚材料_フランジY", "柱脚材料_ウェブY"]
    elif pos_suffix == "柱頭":
        pos_keys_x = ["柱頭材料_フランジX", "柱頭材料_ウェブX"]
        pos_keys_y = ["柱頭材料_フランジY", "柱頭材料_ウェブY"]

    if a == "x":
        keys = pos_keys_x + pos_keys_y + _mk("鉄骨材料_フランジX") + _mk("鉄骨材料_ウェブX") + _mk("鉄骨材料_フランジY") + _mk("鉄骨材料_ウェブY")
    else:
        keys = pos_keys_y + pos_keys_x + _mk("鉄骨材料_フランジY") + _mk("鉄骨材料_ウェブY") + _mk("鉄骨材料_フランジX") + _mk("鉄骨材料_ウェブX")
    for k in keys:
        v = str(row.get(k) or "").strip()
        if v:
            return v
    return ""


def _shape_code_for_param(row: Dict[str, Any], axis: str, shape: Dict[str, Any]) -> str:
    c = str(shape.get("code") or "").strip()
    if c:
        return c
    fallback = str(row.get("鉄骨形状") or "").strip()
    if axis.lower() == "x":
        alt = str(row.get("鉄骨断面_タイプX") or "").strip()
    else:
        alt = str(row.get("鉄骨断面_タイプY") or "").strip()
    return c or fallback or alt


def _infer_level_symbol_from_type_values(type_name: str, values: Dict[str, Any]) -> Tuple[str, str]:
    level = _pick_text(values, ["階1", "階2", "レベル", "階", "層", "階層", "フロア", "Level", "level"])
    symbol = _pick_text(values, ["符号", "マーク(タイプ)", "タイプ記号"])
    if level and symbol:
        return level, symbol

    # 例: 13500C34, B1FL_C3, B1C1 のような型名から補完
    tn = str(type_name or "").strip()
    m = re.match(r"^([A-Za-z]?\d+(?:FL|F)?)_?([A-Za-z]+[0-9A-Za-z_\\-\\'’＇]*)$", tn, re.IGNORECASE)
    if m and (not level):
        level = m.group(1)
        symbol = symbol or m.group(2)
    if not symbol:
        m2 = re.search(r"([A-Za-z]+[0-9][0-9A-Za-z_-]*)$", tn)
        if m2:
            symbol = m2.group(1)
    return level, symbol


def _build_steel_expected_params_for_type(type_params: List[Dict[str, Any]], row: Dict[str, Any]) -> Dict[str, Any]:
    expected: Dict[str, Any] = {}
    shape_x = _shape_for_axis(row, "x")
    shape_y = _shape_for_axis(row, "y")
    mat_x = _steel_material_for_axis(row, "x")
    mat_y = _steel_material_for_axis(row, "y")
    fill_concrete = _pick_text(
        row,
        [
            "充填コンクリート_材料",
            "中詰コンクリート_材料",
            "中詰コンクリート材種",
            "充填コンクリート材種",
        ],
    )

    for p in type_params:
        if not isinstance(p, dict):
            continue
        if bool(p.get("isReadOnly", False)):
            continue
        name = str(p.get("name") or "").strip()
        if not name:
            continue

        n_low = name.lower()
        axis = ""
        if "x" in n_low:
            axis = "x"
        elif "y" in n_low:
            axis = "y"

        pos = ""
        if ("柱頭" in name) or ("head" in n_low) or ("top" in n_low) or ("start" in n_low):
            pos = "top"
        elif ("柱脚" in name) or ("foot" in n_low) or ("bottom" in n_low) or ("end" in n_low):
            pos = "bottom"

        shp = shape_x if axis == "x" else shape_y
        if pos:
            shp_pos = _shape_for_axis(row, axis or "x", pos)
            if (not shp_pos) and axis:
                shp_pos = _shape_for_axis(row, "y" if axis == "x" else "x", pos)
            if shp_pos:
                shp = shp_pos

        # 断面形状
        if "断面形状" in name:
            v = _shape_code_for_param(row, axis or "y", shp if shp else shape_y)
            if v:
                expected[name] = v
            continue

        # 鉄骨材種
        if "鉄骨材種" in name or name == "鉄骨材種":
            if axis == "x":
                v = _steel_material_for_axis(row, "x", pos) if pos else mat_x
            elif axis == "y":
                v = _steel_material_for_axis(row, "y", pos) if pos else mat_y
            else:
                v = (_steel_material_for_axis(row, "y", pos) or _steel_material_for_axis(row, "x", pos)) if pos else (mat_y or mat_x)
            if v:
                expected[name] = v
            continue

        # CFT: 充填(中詰)コンクリート材種
        if fill_concrete:
            is_fill_material_param = (
                ("中詰コンクリート" in name)
                or ("充填コンクリート" in name)
                or (("中詰" in name or "充填" in name) and ("材種" in name or "材料" in name))
                or ("cft" in n_low and ("材種" in name or "材料" in name or "material" in n_low))
            )
            if is_fill_material_param:
                expected[name] = fill_concrete
                continue

        metric = ""
        if "hx" in n_low or "hy" in n_low:
            metric = "H"
        elif "bx" in n_low or "by" in n_low:
            metric = "B"
        elif "twx" in n_low or "twy" in n_low:
            metric = "tw"
        elif "tfx" in n_low or "tfy" in n_low:
            metric = "tf"
        elif "tx" in n_low or "ty" in n_low:
            # 角形/円形鋼管系で板厚を tX/tY で持つケース
            metric = "tw"
        elif "板厚" in name:
            metric = "tw"
        elif "rx" in n_low or "ry" in n_low:
            metric = "R"

        if metric:
            v = shp.get(metric) if isinstance(shp, dict) else None
            if v is not None:
                expected[name] = float(v)

    return expected


def _collect_unapplied_steel_sources(
    type_params: List[Dict[str, Any]],
    row: Dict[str, Any],
    expected_map: Dict[str, Any],
) -> List[Dict[str, Any]]:
    """
    CSVに値があるが、Revit側ターゲットパラメータ不足などで反映できなかった
    可能性がある項目を列挙する（未反映レポート用）。
    """
    writable_names: List[str] = []
    for p in type_params:
        if not isinstance(p, dict):
            continue
        if bool(p.get("isReadOnly", False)):
            continue
        n = str(p.get("name") or "").strip()
        if n:
            writable_names.append(n)
    exp_names = [str(k) for k in (expected_map or {}).keys()]

    def _has_name(names: List[str], pred) -> bool:
        for n in names:
            if pred(n):
                return True
        return False

    def _shape_metric_pred(n: str) -> bool:
        nl = n.lower()
        if "断面形状" in n:
            return True
        return (
            ("hx" in nl)
            or ("hy" in nl)
            or ("bx" in nl)
            or ("by" in nl)
            or ("twx" in nl)
            or ("twy" in nl)
            or ("tfx" in nl)
            or ("tfy" in nl)
            or ("tx" in nl)
            or ("ty" in nl)
            or ("rx" in nl)
            or ("ry" in nl)
            or ("板厚" in n)
        )

    def _fill_pred(n: str) -> bool:
        nl = n.lower()
        return (
            ("中詰コンクリート" in n)
            or ("充填コンクリート" in n)
            or (("中詰" in n or "充填" in n) and ("材種" in n or "材料" in n))
            or ("cft" in nl and ("材種" in n or "材料" in n or "material" in nl))
        )

    def _steel_mat_pred(n: str) -> bool:
        return "鉄骨材種" in n or n == "鉄骨材種"

    src_items = _extract_steel_source_items(row)

    out: List[Dict[str, Any]] = []
    seen: set = set()
    for sk, sv, grp in src_items:
        if grp == "fill":
            target_exists = _has_name(writable_names, _fill_pred)
            mapped = _has_name(exp_names, _fill_pred)
            reason = "NO_TARGET_PARAM" if not target_exists else "NOT_MAPPED"
        elif grp == "mat":
            target_exists = _has_name(writable_names, _steel_mat_pred)
            mapped = _has_name(exp_names, _steel_mat_pred)
            reason = "NO_TARGET_PARAM" if not target_exists else "NOT_MAPPED"
        else:
            target_exists = _has_name(writable_names, _shape_metric_pred)
            mapped = _has_name(exp_names, _shape_metric_pred)
            reason = "NO_TARGET_PARAM" if not target_exists else "NOT_MAPPED"

        if mapped:
            continue
        key = (sk, sv, reason)
        if key in seen:
            continue
        seen.add(key)
        out.append(
            {
                "itemName": sk,
                "itemValue": sv,
                "reason": reason,
            }
        )
    return out


def _extract_steel_source_items(row: Dict[str, Any]) -> List[Tuple[str, str, str]]:
    src_items: List[Tuple[str, str, str]] = []
    for k in (
        "鉄骨断面_登録形状X",
        "鉄骨断面_登録形状Y",
        "鉄骨断面_登録形状X_柱頭",
        "鉄骨断面_登録形状Y_柱頭",
        "鉄骨断面_登録形状X_柱脚",
        "鉄骨断面_登録形状Y_柱脚",
        "柱頭断面_登録形状X",
        "柱頭断面_登録形状Y",
        "柱脚断面_登録形状X",
        "柱脚断面_登録形状Y",
        "鉄骨形状",
        "鉄骨材料_フランジX",
        "鉄骨材料_フランジY",
        "鉄骨材料_ウェブX",
        "鉄骨材料_ウェブY",
        "鉄骨材料_フランジX_柱頭",
        "鉄骨材料_フランジY_柱頭",
        "鉄骨材料_ウェブX_柱頭",
        "鉄骨材料_ウェブY_柱頭",
        "鉄骨材料_フランジX_柱脚",
        "鉄骨材料_フランジY_柱脚",
        "鉄骨材料_ウェブX_柱脚",
        "鉄骨材料_ウェブY_柱脚",
        "柱頭材料_フランジX",
        "柱頭材料_フランジY",
        "柱頭材料_ウェブX",
        "柱頭材料_ウェブY",
        "柱脚材料_フランジX",
        "柱脚材料_フランジY",
        "柱脚材料_ウェブX",
        "柱脚材料_ウェブY",
        "充填コンクリート_材料",
    ):
        v = str(row.get(k) or "").strip()
        if not v:
            continue
        if k == "充填コンクリート_材料":
            grp = "fill"
        elif k.startswith("鉄骨材料_"):
            grp = "mat"
        else:
            grp = "shape"
        src_items.append((k, v, grp))
    return src_items


def _choose_best_steel_row_for_current_type(
    type_params: List[Dict[str, Any]],
    candidates: List[Dict[str, Any]],
) -> Tuple[Optional[Dict[str, Any]], Dict[str, Any]]:
    """
    レベルが取れない場合、符号一致候補の中から「現在タイプ値に最も近い行」を選ぶ。
    既存値との一致度を使うため、誤った先頭行固定より安全に選別できる。
    """
    best_row: Optional[Dict[str, Any]] = None
    best_key: Optional[Tuple[int, int, float, str]] = None
    best_diag: Dict[str, Any] = {}

    for row in candidates:
        exp_map = _build_steel_expected_params_for_type(type_params, row)
        compared = 0
        matched = 0
        numeric_error = 0.0

        for p in type_params:
            if not isinstance(p, dict):
                continue
            name = str(p.get("name") or "").strip()
            if not name or name not in exp_map:
                continue
            compared += 1
            expected = exp_map[name]

            if isinstance(expected, (int, float)):
                actual_v = convert(p.get("value"), "num")
                expected_v = convert(expected, "num")
                if almost_equal(actual_v, expected_v):
                    matched += 1
                else:
                    if isinstance(actual_v, (int, float)) and isinstance(expected_v, (int, float)):
                        numeric_error += abs(float(actual_v) - float(expected_v))
                    else:
                        numeric_error += 1_000_000.0
            else:
                actual_s = convert(p.get("value"), "str")
                expected_s = convert(expected, "str")
                if almost_equal(actual_s, expected_s):
                    matched += 1
                else:
                    numeric_error += 1_000.0

        mismatch = compared - matched
        level_norm = str(row.get("__placement_level_norm") or row.get("__level_norm") or "")
        has_compared = 0 if compared > 0 else 1
        key = (has_compared, mismatch, numeric_error, level_norm)

        if best_key is None or key < best_key:
            best_key = key
            best_row = row
            best_diag = {
                "compared": compared,
                "matched": matched,
                "mismatch": mismatch,
                "numericError": round(float(numeric_error), 6),
                "level": str(row.get("__placement_level") or row.get("__level") or ""),
                "symbol": str(row.get("__placement_symbol") or row.get("__symbol") or ""),
            }

    return best_row, best_diag


def _build_expected_from_param_map_for_type(
    type_params: List[Dict[str, Any]],
    row: Dict[str, Any],
    param_map: List[Dict[str, Any]],
) -> Dict[str, Any]:
    writable_names = {
        str(p.get("name") or "").strip()
        for p in type_params
        if isinstance(p, dict) and not bool(p.get("isReadOnly", False)) and str(p.get("name") or "").strip()
    }
    out: Dict[str, Any] = {}
    core_zero = is_core_count_zero_row(row)

    for m in param_map or []:
        revit_name = str(m.get("revit") or "").strip()
        csv_candidates = [str(x) for x in (m.get("csv") or []) if str(x).strip()]
        conv = str(m.get("converter") or "str")
        if not revit_name or not csv_candidates or revit_name not in writable_names:
            continue
        if core_zero:
            skip_this = False
            for ck in csv_candidates:
                nck = _norm(ck)
                if any(nck.startswith(_norm(pfx)) for pfx in CORE_SKIP_CSV_PREFIXES_WHEN_ZERO):
                    skip_this = True
                    break
            if skip_this:
                continue
        value = None
        for ck in csv_candidates:
            rk = pick_first_key(row, [ck])
            if rk and str(row.get(rk, "")).strip() != "":
                value = convert(row.get(rk), conv)
                break
        if value is not None:
            out[revit_name] = value

    if core_zero:
        for rp in CORE_ZERO_FORCE_REBAR_PARAMS:
            if rp in writable_names:
                out[rp] = 0
    return out


def _choose_best_row_for_param_map_type(
    type_params: List[Dict[str, Any]],
    candidates: List[Dict[str, Any]],
    param_map: List[Dict[str, Any]],
) -> Tuple[Optional[Dict[str, Any]], Dict[str, Any]]:
    best_row: Optional[Dict[str, Any]] = None
    best_key: Optional[Tuple[int, int, float, str]] = None
    best_diag: Dict[str, Any] = {}

    for row in candidates:
        exp_map = _build_expected_from_param_map_for_type(type_params, row, param_map)
        compared = 0
        matched = 0
        numeric_error = 0.0

        for p in type_params:
            if not isinstance(p, dict):
                continue
            name = str(p.get("name") or "").strip()
            if not name or name not in exp_map:
                continue
            compared += 1
            expected = exp_map[name]

            if isinstance(expected, (int, float)):
                actual_v = convert(p.get("value"), "num")
                expected_v = convert(expected, "num")
                if almost_equal(actual_v, expected_v):
                    matched += 1
                else:
                    if isinstance(actual_v, (int, float)) and isinstance(expected_v, (int, float)):
                        numeric_error += abs(float(actual_v) - float(expected_v))
                    else:
                        numeric_error += 1_000_000.0
            else:
                actual_s = convert(p.get("value"), "str")
                expected_s = convert(expected, "str")
                if almost_equal(actual_s, expected_s):
                    matched += 1
                else:
                    numeric_error += 1_000.0

        mismatch = compared - matched
        level_norm = str(row.get("__placement_level_norm") or row.get("__level_norm") or "")
        has_compared = 0 if compared > 0 else 1
        key = (has_compared, mismatch, numeric_error, level_norm)
        if best_key is None or key < best_key:
            best_key = key
            best_row = row
            best_diag = {
                "compared": compared,
                "matched": matched,
                "mismatch": mismatch,
                "numericError": round(float(numeric_error), 6),
                "level": str(row.get("__placement_level") or row.get("__level") or ""),
                "symbol": str(row.get("__placement_symbol") or row.get("__symbol") or ""),
            }

    return best_row, best_diag


def _classify_src_column_type(family_name: str, type_params: List[Dict[str, Any]]) -> str:
    names = {str(p.get("name") or "").strip() for p in type_params if isinstance(p, dict)}
    steel_keys = {
        "柱脚Hx",
        "柱頭Hx",
        "柱脚断面形状x",
        "柱脚断面形状y",
        "柱頭断面形状x",
        "柱頭断面形状y",
        "柱脚鉄骨材種x",
        "柱頭鉄骨材種x",
        "鉄骨材種",
    }
    rc_keys = {
        "断面形状",
        "B",
        "D",
        "柱頭主筋X1段筋太径本数",
        "柱脚主筋X1段筋太径本数",
        "柱頭フープ径",
        "柱脚フープ径",
    }
    steel_score = sum(1 for k in steel_keys if k in names)
    rc_score = sum(1 for k in rc_keys if k in names)

    fam = str(family_name or "")
    fam_u = fam.upper()
    if steel_score > rc_score and steel_score > 0:
        return "steel"
    if rc_score > steel_score and rc_score > 0:
        return "rc"
    if any(tok in fam for tok in ("S柱", "鋼")):
        return "steel"
    if "RC" in fam_u or any(tok in fam for tok in ("円柱", "角柱")):
        return "rc"
    if steel_score > 0:
        return "steel"
    if rc_score > 0:
        return "rc"
    return "unknown"


def _collect_used_type_ids_for_kind(base_url: str, kind_name: str) -> Optional[set]:
    """
    プロジェクト内で実際に配置されているインスタンスから使用中 typeId 集合を取得する。
    取得不能時は None を返し、呼び出し側でフィルタなし継続する。
    """
    if kind_name in ("columns", "steel_columns", "src_columns"):
        method = "element.get_structural_columns"
        list_key = "structuralColumns"
    elif kind_name == "frames":
        method = "element.get_structural_frames"
        list_key = "structuralFrames"
    else:
        return None

    used: set = set()
    skip = 0
    count = 2000
    max_pages = 500

    for _ in range(max_pages):
        env = rpc(
            base_url,
            method,
            {
                "skip": int(skip),
                "count": int(count),
                "failureHandling": {"enabled": True, "mode": "rollback"},
            },
        )
        items = env.get(list_key) if isinstance(env.get(list_key), list) else []
        if not items:
            break

        for it in items:
            if not isinstance(it, dict):
                continue
            tid = _to_count(it.get("typeId"))
            if tid is not None and tid > 0:
                used.add(int(tid))

        got = len(items)
        total = _to_count(env.get("totalCount"))
        skip += got
        if got < count:
            break
        if total is not None and skip >= total:
            break

    return used


def sync_steel_column_types(
    base_url: str,
    kind_cfg: Dict[str, Any],
    expected_info: Dict[str, Any],
    mode_apply: bool,
    used_type_ids: Optional[set] = None,
) -> Dict[str, Any]:
    ret: Dict[str, Any] = {
        "ok": True,
        "label": kind_cfg.get("label") or "鉄骨柱",
        "types": 0,
        "candidateTypes": 0,
        "usedTypeIdsCount": len(used_type_ids or []),
        "filteredOutUnused": 0,
        "matchedTypes": 0,
        "missingExpected": 0,
        "diffs": [],
        "updated": [],
        "errors": [],
        "resolvedByLevelSymbol": 0,
        "resolvedBySymbolSingle": 0,
        "resolvedBySymbolBestMatch": 0,
        "strictLevelBlockedFallback": 0,
        "unresolvedTypes": 0,
        "unapplied": [],
    }

    rows = expected_info.get("chosenRows") or []
    row_by_level_symbol: Dict[Tuple[str, str], Dict[str, Any]] = {}
    row_candidates_by_symbol: Dict[str, List[Dict[str, Any]]] = {}
    row_seen_by_symbol: Dict[str, set] = {}
    for r in rows:
        if not isinstance(r, dict):
            continue
        ln = str(r.get("__placement_level_norm") or "")
        sn = str(r.get("__placement_symbol_norm") or "")
        if ln and sn and (ln, sn) not in row_by_level_symbol:
            row_by_level_symbol[(ln, sn)] = r
        if sn:
            seen = row_seen_by_symbol.setdefault(sn, set())
            sig = (
                str(r.get("__placement_level_norm") or ""),
                str(r.get("__placement_symbol_norm") or ""),
                str(r.get("__section") or ""),
            )
            if sig not in seen:
                seen.add(sig)
                row_candidates_by_symbol.setdefault(sn, []).append(r)
    used_row_keys: set = set()

    cmd_list = str(kind_cfg.get("listTypesCommand") or "get_structural_column_types")
    cmd_update = str(kind_cfg.get("updateCommand") or "update_structural_column_type_parameter")
    fam_tokens = [str(x) for x in (kind_cfg.get("familyNameContainsAny") or []) if str(x).strip()]
    allow_symbol_fallback_with_level = bool(kind_cfg.get("allowSymbolFallbackWhenLevelPresent", False))
    strict_level_when_type_name_has_level = bool(kind_cfg.get("strictLevelWhenTypeNameHasLevel", True))

    env = rpc(base_url, cmd_list, {"skip": 0, "count": 10000, "failureHandling": {"enabled": True, "mode": "rollback"}})
    types = _extract_types(env)
    ret["types"] = len(types)

    for t in types:
        tid = _type_id_of(t)
        if tid <= 0:
            continue
        if used_type_ids is not None and int(tid) not in used_type_ids:
            ret["filteredOutUnused"] += 1
            continue
        fam = str(t.get("familyName") or "")
        if fam_tokens and not any(tok in fam for tok in fam_tokens):
            continue
        ret["candidateTypes"] += 1

        try:
            prm = rpc(base_url, "element.get_structural_column_type_parameters", {"typeId": int(tid)})
        except Exception as ex:
            ret["errors"].append({"typeId": tid, "op": "get_type_parameters", "msg": str(ex)})
            continue
        plist = prm.get("parameters") if isinstance(prm.get("parameters"), list) else []
        values: Dict[str, Any] = {}
        for p in plist:
            if not isinstance(p, dict):
                continue
            n = str(p.get("name") or "").strip()
            if not n:
                continue
            if n not in values or (values.get(n) in ("", None) and p.get("value") not in ("", None)):
                values[n] = p.get("value")

        tname = _type_name_of(t)
        lv_raw, sy_raw = _infer_level_symbol_from_type_values(tname, values)
        hint_lv_raw, hint_sy_raw, has_level_hint = _extract_level_symbol_hint_from_type_name(tname)
        if not lv_raw and hint_lv_raw:
            lv_raw = hint_lv_raw
        if not sy_raw and hint_sy_raw:
            sy_raw = hint_sy_raw
        lv = normalize_level_token(lv_raw)
        sy = normalize_symbol_token(sy_raw)
        row: Optional[Dict[str, Any]] = _lookup_row_by_level_symbol(row_by_level_symbol, lv, sy) if lv and sy else None
        if row is not None:
            ret["resolvedByLevelSymbol"] += 1
        elif sy:
            can_fallback = ((not lv) or allow_symbol_fallback_with_level)
            if strict_level_when_type_name_has_level and has_level_hint and lv:
                can_fallback = False
                ret["strictLevelBlockedFallback"] += 1
            if can_fallback:
                candidates = _lookup_row_candidates_by_symbol(row_candidates_by_symbol, sy)
                if len(candidates) == 1:
                    row = candidates[0]
                    ret["resolvedBySymbolSingle"] += 1
                elif len(candidates) > 1:
                    row, diag = _choose_best_steel_row_for_current_type(plist, candidates)
                    if row is not None:
                        ret["resolvedBySymbolBestMatch"] += 1
                    else:
                        ret["errors"].append(
                            {
                                "typeId": tid,
                                "typeName": tname,
                                "op": "resolve_by_symbol_best_match",
                                "msg": "候補行の選別に失敗しました。",
                            }
                        )
        if row is None:
            ret["missingExpected"] += 1
            ret["unresolvedTypes"] += 1
            continue
        used_row_keys.add((str(row.get("__placement_level_norm") or ""), str(row.get("__placement_symbol_norm") or "")))

        ret["matchedTypes"] += 1
        exp_map = _build_steel_expected_params_for_type(plist, row)
        unapplied = _collect_unapplied_steel_sources(plist, row, exp_map)
        for ua in unapplied:
            ret["unapplied"].append(
                {
                    "typeId": tid,
                    "typeName": tname,
                    "familyName": fam,
                    "level": str(row.get("__placement_level") or row.get("__level") or ""),
                    "symbol": str(row.get("__placement_symbol") or row.get("__symbol") or ""),
                    "itemName": ua.get("itemName"),
                    "itemValue": ua.get("itemValue"),
                    "reason": ua.get("reason"),
                }
            )

        for p in plist:
            if not isinstance(p, dict):
                continue
            name = str(p.get("name") or "").strip()
            if not name or name not in exp_map:
                continue
            if bool(p.get("isReadOnly", False)):
                continue
            actual = convert(p.get("value"), "num" if isinstance(exp_map[name], (int, float)) else "str")
            expected = convert(exp_map[name], "num" if isinstance(exp_map[name], (int, float)) else "str")
            if equal_with_param(name, actual, expected):
                continue

            diff = {
                "typeId": tid,
                "typeName": tname,
                "familyName": fam,
                "level": str(row.get("__placement_level") or row.get("__level") or lv_raw or ""),
                "symbol": str(row.get("__placement_symbol") or row.get("__symbol") or sy_raw or ""),
                "param": name,
                "actual": actual,
                "expected": expected,
            }
            ret["diffs"].append(diff)
            if mode_apply:
                try:
                    u = rpc(base_url, cmd_update, {"typeId": int(tid), "paramName": name, "value": expected})
                    if bool(u.get("ok", True)):
                        ret["updated"].append(diff)
                    else:
                        ret["errors"].append({"op": "update", "diff": diff, "msg": u.get("msg", "update failed")})
                except Exception as ex:
                    ret["errors"].append({"op": "update", "diff": diff, "msg": str(ex)})

    # Revit側に該当タイプが存在しないCSV行も未反映として記録
    seen_nm: set = set()
    for row in rows:
        ln = str(row.get("__placement_level_norm") or "")
        sn = str(row.get("__placement_symbol_norm") or "")
        if not ln or not sn:
            continue
        if (ln, sn) in used_row_keys:
            continue
        for sk, sv, _grp in _extract_steel_source_items(row):
            key = (ln, sn, sk, sv)
            if key in seen_nm:
                continue
            seen_nm.add(key)
            ret["unapplied"].append(
                {
                    "typeId": None,
                    "typeName": "",
                    "familyName": "",
                    "level": str(row.get("__placement_level") or row.get("__level") or ""),
                    "symbol": str(row.get("__placement_symbol") or row.get("__symbol") or ""),
                    "itemName": sk,
                    "itemValue": sv,
                    "reason": "NO_MATCHED_TYPE",
                }
            )

    return ret


def sync_src_column_types(
    base_url: str,
    kind_cfg: Dict[str, Any],
    expected_info: Dict[str, Any],
    mode_apply: bool,
    config: Dict[str, Any],
    used_type_ids: Optional[set] = None,
) -> Dict[str, Any]:
    ret: Dict[str, Any] = {
        "ok": True,
        "label": kind_cfg.get("label") or "SRC柱",
        "types": 0,
        "candidateTypes": 0,
        "usedTypeIdsCount": len(used_type_ids or []),
        "filteredOutUnused": 0,
        "filteredOutUnknownClass": 0,
        "filteredOutNoPair": 0,
        "strictPairRequired": bool(kind_cfg.get("strictPairRequired", True)),
        "pairKeysCount": 0,
        "matchedTypes": 0,
        "missingExpected": 0,
        "unresolvedTypes": 0,
        "resolvedByLevelSymbol": 0,
        "resolvedBySymbolSingle": 0,
        "resolvedBySymbolBestMatch": 0,
        "strictLevelBlockedFallback": 0,
        "rcCandidates": 0,
        "steelCandidates": 0,
        "unapplied": [],
        "diffs": [],
        "updated": [],
        "errors": [],
    }

    rows = expected_info.get("chosenRows") or []
    row_by_level_symbol: Dict[Tuple[str, str], Dict[str, Any]] = {}
    row_candidates_by_symbol: Dict[str, List[Dict[str, Any]]] = {}
    row_seen_by_symbol: Dict[str, set] = {}
    for r in rows:
        if not isinstance(r, dict):
            continue
        ln = str(r.get("__placement_level_norm") or "")
        sn = str(r.get("__placement_symbol_norm") or "")
        if ln and sn and (ln, sn) not in row_by_level_symbol:
            row_by_level_symbol[(ln, sn)] = r
        if sn:
            seen = row_seen_by_symbol.setdefault(sn, set())
            sig = (
                str(r.get("__placement_level_norm") or ""),
                str(r.get("__placement_symbol_norm") or ""),
                str(r.get("__section") or ""),
            )
            if sig not in seen:
                seen.add(sig)
                row_candidates_by_symbol.setdefault(sn, []).append(r)
    used_row_keys: set = set()

    ref_kind = str(kind_cfg.get("rcParamMapRef") or "columns")
    kinds_cfg = config.get("kinds") if isinstance(config.get("kinds"), dict) else {}
    rc_map = (
        ((kinds_cfg.get(ref_kind) or {}).get("paramMap") if isinstance(kinds_cfg.get(ref_kind), dict) else None)
        or ((kinds_cfg.get("columns") or {}).get("paramMap") if isinstance(kinds_cfg.get("columns"), dict) else None)
        or []
    )

    cmd_list = str(kind_cfg.get("listTypesCommand") or "get_structural_column_types")
    cmd_update = str(kind_cfg.get("updateCommand") or "update_structural_column_type_parameter")
    fam_tokens = [str(x) for x in (kind_cfg.get("familyNameContainsAny") or []) if str(x).strip()]
    allow_symbol_fallback_with_level = bool(kind_cfg.get("allowSymbolFallbackWhenLevelPresent", False))
    strict_level_when_type_name_has_level = bool(kind_cfg.get("strictLevelWhenTypeNameHasLevel", True))

    env = rpc(base_url, cmd_list, {"skip": 0, "count": 10000, "failureHandling": {"enabled": True, "mode": "rollback"}})
    types = _extract_types(env)
    ret["types"] = len(types)

    records: List[Dict[str, Any]] = []
    rec_by_type_id: Dict[int, Dict[str, Any]] = {}

    for t in types:
        tid = _type_id_of(t)
        if tid <= 0:
            continue
        if used_type_ids is not None and int(tid) not in used_type_ids:
            ret["filteredOutUnused"] += 1
            continue
        fam = str(t.get("familyName") or "")
        if fam_tokens and not any(tok in fam for tok in fam_tokens):
            continue
        ret["candidateTypes"] += 1

        try:
            prm = rpc(base_url, "element.get_structural_column_type_parameters", {"typeId": int(tid)})
        except Exception as ex:
            ret["errors"].append({"typeId": tid, "op": "get_type_parameters", "msg": str(ex)})
            continue
        plist = prm.get("parameters") if isinstance(prm.get("parameters"), list) else []

        cls = _classify_src_column_type(fam, plist)
        if cls == "unknown":
            ret["filteredOutUnknownClass"] += 1
            continue
        if cls == "rc":
            ret["rcCandidates"] += 1
        elif cls == "steel":
            ret["steelCandidates"] += 1

        values: Dict[str, Any] = {}
        for p in plist:
            if not isinstance(p, dict):
                continue
            n = str(p.get("name") or "").strip()
            if not n:
                continue
            if n not in values or (values.get(n) in ("", None) and p.get("value") not in ("", None)):
                values[n] = p.get("value")

        tname = _type_name_of(t)
        lv_raw, sy_raw = _infer_level_symbol_from_type_values(tname, values)
        hint_lv_raw, hint_sy_raw, has_level_hint = _extract_level_symbol_hint_from_type_name(tname)
        if not lv_raw and hint_lv_raw:
            lv_raw = hint_lv_raw
        if not sy_raw and hint_sy_raw:
            sy_raw = hint_sy_raw
        lv = normalize_level_token(lv_raw)
        sy = normalize_symbol_token(sy_raw)

        row: Optional[Dict[str, Any]] = _lookup_row_by_level_symbol(row_by_level_symbol, lv, sy) if lv and sy else None
        if row is not None:
            ret["resolvedByLevelSymbol"] += 1
        elif sy:
            can_fallback = ((not lv) or allow_symbol_fallback_with_level)
            if strict_level_when_type_name_has_level and has_level_hint and lv:
                can_fallback = False
                ret["strictLevelBlockedFallback"] += 1
            if can_fallback:
                candidates = _lookup_row_candidates_by_symbol(row_candidates_by_symbol, sy)
                if len(candidates) == 1:
                    row = candidates[0]
                    ret["resolvedBySymbolSingle"] += 1
                elif len(candidates) > 1:
                    if cls == "steel":
                        row, _diag = _choose_best_steel_row_for_current_type(plist, candidates)
                    else:
                        row, _diag = _choose_best_row_for_param_map_type(plist, candidates, rc_map)
                    if row is not None:
                        ret["resolvedBySymbolBestMatch"] += 1

        if row is None:
            ret["missingExpected"] += 1
            ret["unresolvedTypes"] += 1
            continue

        exp_map: Dict[str, Any] = (
            _build_steel_expected_params_for_type(plist, row)
            if cls == "steel"
            else _build_expected_from_param_map_for_type(plist, row, rc_map)
        )
        if cls == "steel":
            unapplied = _collect_unapplied_steel_sources(plist, row, exp_map)
            for ua in unapplied:
                ret["unapplied"].append(
                    {
                        "typeId": int(tid),
                        "typeName": tname,
                        "familyName": fam,
                        "class": cls,
                        "level": str(row.get("__placement_level") or row.get("__level") or ""),
                        "symbol": str(row.get("__placement_symbol") or row.get("__symbol") or ""),
                        "itemName": ua.get("itemName"),
                        "itemValue": ua.get("itemValue"),
                        "reason": ua.get("reason"),
                    }
                )

        rec = {
            "typeId": int(tid),
            "typeName": tname,
            "familyName": fam,
            "class": cls,
            "levelNorm": lv,
            "symbolNorm": sy,
            "key": (lv, sy),
            "plist": plist,
            "expMap": exp_map,
        }
        used_row_keys.add((str(row.get("__placement_level_norm") or ""), str(row.get("__placement_symbol_norm") or "")))
        records.append(rec)
        rec_by_type_id[int(tid)] = rec

    pair_keys: Optional[set] = None
    if bool(kind_cfg.get("strictPairRequired", True)):
        pair_keys = set()
        try:
            inst_env = rpc(
                base_url,
                "element.get_structural_columns",
                {"skip": 0, "count": 10000, "failureHandling": {"enabled": True, "mode": "rollback"}},
            )
            insts = inst_env.get("structuralColumns") if isinstance(inst_env.get("structuralColumns"), list) else []
            key_classes: Dict[Tuple[str, str], set] = {}
            pair_by_location = bool(kind_cfg.get("pairByLocation", True))
            tol_mm = _to_num(kind_cfg.get("pairLocationToleranceMm"))
            if tol_mm is None or tol_mm <= 0:
                tol_mm = 20.0
            loc_classes: Dict[Tuple[str, str, int, int], set] = {}
            for it in insts:
                if not isinstance(it, dict):
                    continue
                tid = _to_count(it.get("typeId"))
                if tid is None:
                    continue
                rec = rec_by_type_id.get(int(tid))
                if not rec:
                    continue
                key = rec.get("key")
                if not key or not key[0] or not key[1]:
                    continue
                k2 = (str(key[0]), str(key[1]))
                cls = str(rec.get("class"))
                key_classes.setdefault(k2, set()).add(cls)

                if pair_by_location:
                    loc = it.get("location") if isinstance(it.get("location"), dict) else {}
                    x = _to_num(loc.get("x")) if isinstance(loc, dict) else None
                    y = _to_num(loc.get("y")) if isinstance(loc, dict) else None
                    if x is None or y is None:
                        continue
                    bx = int(round(float(x) / float(tol_mm)))
                    by = int(round(float(y) / float(tol_mm)))
                    lk = (k2[0], k2[1], bx, by)
                    loc_classes.setdefault(lk, set()).add(cls)

            if pair_by_location:
                for lk, classes in loc_classes.items():
                    if "rc" in classes and "steel" in classes:
                        pair_keys.add((lk[0], lk[1]))
            else:
                for k, classes in key_classes.items():
                    if "rc" in classes and "steel" in classes:
                        pair_keys.add(k)
            ret["pairKeysCount"] = len(pair_keys)
        except Exception as ex:
            ret["errors"].append({"stage": "build_pair_keys", "msg": str(ex)})
            pair_keys = None

    for rec in records:
        key = rec.get("key")
        if pair_keys is not None:
            if not key or (str(key[0]), str(key[1])) not in pair_keys:
                ret["filteredOutNoPair"] += 1
                continue

        ret["matchedTypes"] += 1
        exp_map = rec.get("expMap") or {}
        plist = rec.get("plist") or []
        tid = int(rec.get("typeId"))
        tname = str(rec.get("typeName") or "")
        fam = str(rec.get("familyName") or "")

        for p in plist:
            if not isinstance(p, dict):
                continue
            name = str(p.get("name") or "").strip()
            if not name or name not in exp_map:
                continue
            if bool(p.get("isReadOnly", False)):
                continue
            actual = convert(p.get("value"), "num" if isinstance(exp_map[name], (int, float)) else "str")
            expected = convert(exp_map[name], "num" if isinstance(exp_map[name], (int, float)) else "str")
            if equal_with_param(name, actual, expected):
                continue

            diff = {
                "typeId": tid,
                "typeName": tname,
                "familyName": fam,
                "class": rec.get("class"),
                "level": str((rec.get("key") or ("", ""))[0] or ""),
                "symbol": str((rec.get("key") or ("", ""))[1] or ""),
                "param": name,
                "actual": actual,
                "expected": expected,
            }
            ret["diffs"].append(diff)
            if mode_apply:
                try:
                    u = rpc(base_url, cmd_update, {"typeId": int(tid), "paramName": name, "value": expected})
                    if bool(u.get("ok", True)):
                        ret["updated"].append(diff)
                    else:
                        ret["errors"].append({"op": "update", "diff": diff, "msg": u.get("msg", "update failed")})
                except Exception as ex:
                    ret["errors"].append({"op": "update", "diff": diff, "msg": str(ex)})

    # Revit側に該当タイプが存在しないCSV行も未反映として記録
    seen_nm: set = set()
    for row in rows:
        ln = str(row.get("__placement_level_norm") or "")
        sn = str(row.get("__placement_symbol_norm") or "")
        if not ln or not sn:
            continue
        if (ln, sn) in used_row_keys:
            continue
        for sk, sv, _grp in _extract_steel_source_items(row):
            key = (ln, sn, sk, sv)
            if key in seen_nm:
                continue
            seen_nm.add(key)
            ret["unapplied"].append(
                {
                    "typeId": None,
                    "typeName": "",
                    "familyName": "",
                    "class": "steel",
                    "level": str(row.get("__placement_level") or row.get("__level") or ""),
                    "symbol": str(row.get("__placement_symbol") or row.get("__symbol") or ""),
                    "itemName": sk,
                    "itemValue": sv,
                    "reason": "NO_MATCHED_TYPE",
                }
            )

    return ret


# -----------------------------
# Output path helpers
# -----------------------------


def _safe_name(s: str) -> str:
    t = re.sub(r'[\\/:*?"<>|]+', "_", (s or "").strip())
    t = re.sub(r"\s+", " ", t).strip()
    return t[:120] if t else "Untitled"


def _revit_mcp_root() -> Path:
    # 1) 明示指定があれば最優先
    env_root = os.environ.get("REVIT_MCP_ROOT", "").strip()
    if env_root:
        p = Path(env_root).expanduser()
        if (p / "Scripts" / "PythonRunnerScripts").exists():
            return p
        if p.exists():
            return p

    here = Path(__file__).resolve()

    # 2) run_latest.py など Projects/<project>/python_script 配下からの実行に対応
    for p in [here.parent] + list(here.parents):
        if p.name.lower() == "projects":
            cand = p.parent
            if (cand / "Scripts" / "PythonRunnerScripts").exists():
                return cand

    # 3) 自身または親から Revit_MCP ルートを探索
    for p in [here.parent] + list(here.parents):
        if (p / "Scripts" / "PythonRunnerScripts").exists() and (p / "Projects").exists():
            return p

    # 4) 既定候補
    home_root = Path.home() / "Documents" / "Revit_MCP"
    if (home_root / "Scripts" / "PythonRunnerScripts").exists():
        return home_root

    # 5) 最後のフォールバック
    return here.parents[2]


def _default_output_path(base_url: str, user_output: str = "") -> Path:
    if user_output:
        return Path(user_output)
    ts = time.strftime("%Y%m%d_%H%M%S")
    root = _revit_mcp_root()
    # 誤って root が Projects フォルダを指した場合の安全補正
    if root.name.lower() == "projects":
        root = root.parent
    proj_dir = root / "Projects" / "UnknownProject"
    try:
        ctx = rpc(base_url, "help.get_context", {"includeSelectionIds": False, "maxSelectionIds": 0})
        data = ctx.get("data") if isinstance(ctx.get("data"), dict) else ctx
        title = _safe_name(str(data.get("docTitle") or "Untitled"))
        key = _safe_name(str(data.get("docGuid") or data.get("contextToken") or "UnknownKey"))
        proj_dir = root / "Projects" / f"{title}_{key}"
    except Exception:
        pass
    out = proj_dir / "Reports" / f"calc_csv_type_sync_{ts}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    return out


def _report_level_symbol(diff: Dict[str, Any]) -> Tuple[str, str]:
    level = str(diff.get("level") or diff.get("levelNorm") or "").strip()
    symbol = str(diff.get("symbol") or diff.get("symbolNorm") or "").strip()
    if (not level or not symbol):
        tname = str(diff.get("typeName") or "").strip()
        if tname:
            lv2, sy2, _ = parse_level_symbol_from_type_name(tname)
            if not level:
                level = str(lv2 or "").strip()
            if not symbol:
                symbol = str(sy2 or "").strip()
    return level, symbol


def _to_report_text(v: Any) -> str:
    if v is None:
        return ""
    if isinstance(v, (dict, list, tuple)):
        try:
            return json.dumps(v, ensure_ascii=False)
        except Exception:
            return str(v)
    return str(v)


def _collect_diff_report_rows(result: Dict[str, Any]) -> List[Dict[str, str]]:
    rows: List[Dict[str, str]] = []

    def _append(scope: str, diff: Dict[str, Any], kind: str = "") -> None:
        level, symbol = _report_level_symbol(diff)
        row = {
            "scope": scope,
            "kind": kind,
            "level": level,
            "symbol": symbol,
            "familyName": str(diff.get("familyName") or "").strip(),
            "typeName": str(diff.get("typeName") or "").strip(),
            "typeId": _to_report_text(diff.get("typeId")),
            "elementId": _to_report_text(diff.get("elementId")),
            "paramName": str(diff.get("param") or "").strip(),
            "actual": _to_report_text(diff.get("actual")),
            "expected": _to_report_text(diff.get("expected")),
            "class": str(diff.get("class") or "").strip(),
        }
        rows.append(row)

    for kind_name, kret in (result.get("kinds") or {}).items():
        if not isinstance(kret, dict):
            continue
        for d in (kret.get("diffs") or []):
            if isinstance(d, dict):
                _append("kind", d, str(kind_name))

    col = result.get("columnListFamilies")
    if isinstance(col, dict):
        for d in (col.get("diffs") or []):
            if isinstance(d, dict):
                _append("columnListFamilies", d, "columns")

    beam = result.get("beamListFamilies")
    if isinstance(beam, dict):
        for d in (beam.get("diffs") or []):
            if isinstance(d, dict):
                _append("beamListFamilies", d, "frames")

    frame_inst = result.get("frameInstances")
    if isinstance(frame_inst, dict):
        for d in (frame_inst.get("diffs") or []):
            if isinstance(d, dict):
                _append("frameInstances", d, "frames_instance")

    return rows


def _write_diff_report_csv(path: Path, rows: List[Dict[str, str]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    cols = [
        "scope",
        "kind",
        "level",
        "symbol",
        "familyName",
        "typeName",
        "typeId",
        "elementId",
        "paramName",
        "actual",
        "expected",
        "class",
    ]
    with path.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=cols, extrasaction="ignore")
        w.writeheader()
        for r in rows:
            w.writerow(r)


# -----------------------------
# Main
# -----------------------------


def merge_defaults(current: Any, default: Any) -> Any:
    if isinstance(default, dict):
        cur = current if isinstance(current, dict) else {}
        out: Dict[str, Any] = {}
        for k, dv in default.items():
            if k in cur:
                out[k] = merge_defaults(cur[k], dv)
            else:
                out[k] = dv
        # defaultに無い既存キーは保持（ユーザー拡張を壊さない）
        for k, cv in cur.items():
            if k not in out:
                out[k] = cv
        return out
    if isinstance(default, list):
        # リストは既存優先（順序やカスタム定義を尊重）
        if isinstance(current, list) and len(current) > 0:
            return current
        return default
    return current if current is not None else default


def load_or_create_config(path: Path) -> Tuple[Dict[str, Any], bool]:
    if path.exists():
        with path.open("r", encoding="utf-8") as f:
            loaded = json.load(f)
        merged = merge_defaults(loaded, DEFAULT_CONFIG)
        # 新規キーが追加された場合は保存して自己修復
        if merged != loaded:
            with path.open("w", encoding="utf-8") as f:
                json.dump(merged, f, ensure_ascii=False, indent=2)
        return merged, False
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        json.dump(DEFAULT_CONFIG, f, ensure_ascii=False, indent=2)
    return DEFAULT_CONFIG, True


def main() -> int:
    ap = argparse.ArgumentParser(description="電算CSVを正としてレベル+符号でRevitタイプ属性を差分同期")
    ap.add_argument("--port", type=int, default=int(os.environ.get("REVIT_MCP_PORT", "5210") or 5210))
    ap.add_argument("--csv-path", type=str, default=DEFAULT_CSV)
    ap.add_argument("--encoding", type=str, default="")
    ap.add_argument("--mode", choices=["plan", "apply"], default="plan")
    ap.add_argument("--kinds", type=str, default="columns,steel_columns,src_columns,frames", help="対象kindをカンマ区切りで指定")
    ap.add_argument("--config", type=str, default="")
    ap.add_argument("--output", type=str, default="")
    ap.add_argument("--skip-column-list-sync", action="store_true", help="柱リスト用ファミリ同期をスキップ")
    ap.add_argument(
        "--only-column-list-sync",
        action="store_true",
        help="構造部材タイプの同期は行わず、柱リスト用ファミリ同期のみ実行",
    )
    ap.add_argument(
        "--column-list-mode",
        choices=["auto", "rc", "src", "both", "none"],
        default="auto",
        help="柱リスト用ファミリ同期の対象: auto|rc|src|both|none",
    )
    ap.add_argument("--sync-frame-instances", action="store_true", help="構造フレームのインスタンスパラメータもCSVから同期")
    ap.add_argument("--skip-beam-list-sync", action="store_true", help="梁リスト用ファミリ同期をスキップ")
    ap.add_argument(
        "--beam-list-mode",
        choices=["auto", "rc", "src", "both", "none"],
        default="auto",
        help="梁リスト用ファミリ同期の対象: auto|rc|src|both|none",
    )
    args = ap.parse_args()

    base_url = f"http://127.0.0.1:{args.port}"

    csv_path = Path(args.csv_path)
    if not csv_path.exists():
        print(json.dumps({"ok": False, "code": "CSV_NOT_FOUND", "csvPath": str(csv_path)}, ensure_ascii=False, indent=2))
        return 1

    cfg_path = Path(args.config) if args.config else (Path(__file__).with_suffix(".config.json"))
    config, created = load_or_create_config(cfg_path)
    if created:
        print(
            json.dumps(
                {
                    "ok": False,
                    "code": "CONFIG_CREATED",
                    "msg": "設定テンプレートを作成しました。必要に応じてCSV列名/Revitパラメータ名を調整して再実行してください。",
                    "configPath": str(cfg_path),
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 1

    sections = parse_sections(csv_path, encoding_hint=args.encoding)

    enabled_kind_names = [k.strip() for k in str(args.kinds).split(",") if k.strip()]
    mode_apply = args.mode == "apply"

    result: Dict[str, Any] = {
        "ok": True,
        "mode": args.mode,
        "csvPath": str(csv_path),
        "configPath": str(cfg_path),
        "kinds": {},
        "sectionsCount": len(sections),
    }
    expected_cache: Dict[str, Dict[str, Any]] = {}

    for kind_name, kind_cfg in (config.get("kinds") or {}).items():
        if kind_name not in enabled_kind_names:
            continue
        if not bool(kind_cfg.get("enabled", True)):
            result["kinds"][kind_name] = {"ok": True, "skipped": True, "reason": "disabled_in_config"}
            continue

        kret: Dict[str, Any] = {
            "ok": True,
            "label": kind_cfg.get("label") or kind_name,
            "placements": 0,
            "expectedSymbols": 0,
            "types": 0,
            "usedTypeIdsCount": 0,
            "filteredOutUnused": 0,
            "skippedNoTargetParams": 0,
            "matchedTypes": 0,
            "resolvedByLevelSymbol": 0,
            "resolvedBySymbolFallback": 0,
            "strictLevelBlockedFallback": 0,
            "unresolvedTypes": 0,
            "missingExpected": [],
            "conflicts": [],
            "diffs": [],
            "updated": [],
            "errors": [],
        }

        try:
            expected_info = aggregate_expected_for_kind(kind_cfg, sections)
            expected_cache[kind_name] = expected_info
            expected_by_symbol = expected_info.get("expectedBySymbol") or {}
            expected_by_level_symbol = expected_info.get("expectedByLevelSymbol") or {}
            chosen_rows = expected_info.get("chosenRows") or []
            row_candidates_by_symbol: Dict[str, List[Dict[str, Any]]] = {}
            row_seen_by_symbol: Dict[str, set] = {}
            for rr in chosen_rows:
                if not isinstance(rr, dict):
                    continue
                sn = normalize_symbol_token(rr.get("__placement_symbol") or rr.get("__symbol") or "")
                if not sn:
                    continue
                sig = (
                    str(rr.get("__placement_level_norm") or rr.get("__level_norm") or ""),
                    sn,
                    str(rr.get("__section") or ""),
                )
                seen = row_seen_by_symbol.setdefault(sn, set())
                if sig in seen:
                    continue
                seen.add(sig)
                row_candidates_by_symbol.setdefault(sn, []).append(rr)
            kret["placements"] = len(expected_info.get("placements") or [])
            kret["expectedSymbols"] = len(expected_by_symbol)
            kret["missingExpected"] = expected_info.get("missingRows") or []
            kret["conflicts"] = expected_info.get("conflicts") or []

            if args.only_column_list_sync:
                kret["skipped"] = True
                kret["reason"] = "only_column_list_sync"
                result["kinds"][kind_name] = kret
                continue

            used_type_ids: Optional[set] = None
            if bool(kind_cfg.get("onlyUsedTypes", True)):
                try:
                    used_type_ids = _collect_used_type_ids_for_kind(base_url, kind_name)
                    if used_type_ids is not None:
                        kret["usedTypeIdsCount"] = len(used_type_ids)
                except Exception as ex:
                    used_type_ids = None
                    kret["errors"].append(
                        {"stage": "collect_used_type_ids", "msg": str(ex), "kind": kind_name}
                    )

            if kind_name == "steel_columns":
                steel_ret = sync_steel_column_types(
                    base_url=base_url,
                    kind_cfg=kind_cfg,
                    expected_info=expected_info,
                    mode_apply=mode_apply,
                    used_type_ids=used_type_ids,
                )
                for kk in (
                    "types",
                    "candidateTypes",
                    "usedTypeIdsCount",
                    "filteredOutUnused",
                    "matchedTypes",
                    "missingExpected",
                    "diffs",
                    "updated",
                    "errors",
                    "label",
                    "resolvedByLevelSymbol",
                    "resolvedBySymbolSingle",
                    "resolvedBySymbolBestMatch",
                    "unresolvedTypes",
                ):
                    if kk in steel_ret:
                        if kk == "errors":
                            kret["errors"] = (kret.get("errors") or []) + (steel_ret.get("errors") or [])
                        else:
                            kret[kk] = steel_ret[kk]
                result["kinds"][kind_name] = kret
                continue

            if kind_name == "src_columns":
                src_ret = sync_src_column_types(
                    base_url=base_url,
                    kind_cfg=kind_cfg,
                    expected_info=expected_info,
                    mode_apply=mode_apply,
                    config=config,
                    used_type_ids=used_type_ids,
                )
                for kk in (
                    "types",
                    "candidateTypes",
                    "usedTypeIdsCount",
                    "filteredOutUnused",
                    "filteredOutUnknownClass",
                    "filteredOutNoPair",
                    "strictPairRequired",
                    "pairKeysCount",
                    "matchedTypes",
                    "missingExpected",
                    "diffs",
                    "updated",
                    "errors",
                    "label",
                    "resolvedByLevelSymbol",
                    "resolvedBySymbolSingle",
                    "resolvedBySymbolBestMatch",
                    "unresolvedTypes",
                    "rcCandidates",
                    "steelCandidates",
                ):
                    if kk in src_ret:
                        if kk == "errors":
                            kret["errors"] = (kret.get("errors") or []) + (src_ret.get("errors") or [])
                        else:
                            kret[kk] = src_ret[kk]
                result["kinds"][kind_name] = kret
                continue

            types_env = rpc(base_url, str(kind_cfg.get("listTypesCommand") or ""), {"skip": 0, "count": 5000, "namesOnly": False})
            types = _extract_types(types_env)
            kret["types"] = len(types)

            symbol_params = [str(x) for x in (kind_cfg.get("symbolParamCandidates") or []) if str(x).strip()]
            param_map = kind_cfg.get("paramMap") or []
            target_params = [str(m.get("revit") or "").strip() for m in param_map if str(m.get("revit") or "").strip()]
            level_infer_params = ["階1", "階2", "レベル", "階", "層", "階層", "フロア", "Level", "level"]
            read_params = sorted(set(symbol_params + target_params + level_infer_params))
            allow_symbol_fallback_with_level = bool(kind_cfg.get("allowSymbolFallbackWhenLevelPresent", True))
            strict_level_when_type_name_has_level = bool(kind_cfg.get("strictLevelWhenTypeNameHasLevel", True))
            kind_logic = _normalize_kind_name_for_logic(kind_name)

            for t in types:
                tid = _type_id_of(t)
                if tid <= 0:
                    continue
                if used_type_ids is not None and int(tid) not in used_type_ids:
                    kret["filteredOutUnused"] += 1
                    continue
                tname = _type_name_of(t)

                type_vals = get_type_params_bulk(base_url, tid, read_params)
                params_map = type_vals.get("params") if isinstance(type_vals.get("params"), dict) else {}
                display_map = type_vals.get("display") if isinstance(type_vals.get("display"), dict) else {}
                available_param_names = set(params_map.keys()) | set(display_map.keys())
                plist_for_match: List[Dict[str, Any]] = []
                for pn in sorted(available_param_names):
                    if not pn:
                        continue
                    pv = params_map.get(pn, display_map.get(pn))
                    plist_for_match.append({"name": pn, "value": pv, "isReadOnly": False})
                params_for_infer = type_vals.get("params") if isinstance(type_vals.get("params"), dict) else {}
                lv_raw, sy_raw = _infer_level_symbol_from_type_values(tname, params_for_infer)
                hint_lv_raw, hint_sy_raw, has_level_hint = _extract_level_symbol_hint_from_type_name(tname)
                if not lv_raw and hint_lv_raw:
                    lv_raw = hint_lv_raw
                if not sy_raw and hint_sy_raw:
                    sy_raw = hint_sy_raw
                sym = str(sy_raw or "").strip()
                if not sym:
                    sym = select_symbol_from_type(t, type_vals, symbol_params)

                lv_norm = normalize_level_token(lv_raw)
                sym_norm = normalize_symbol_token(sym)

                exp = None
                if lv_norm and sym_norm:
                    exp = _lookup_expected_by_level_symbol(expected_by_level_symbol, lv_norm, sym_norm)
                    if exp:
                        kret["resolvedByLevelSymbol"] += 1
                if not exp:
                    can_fallback = ((not lv_norm) or allow_symbol_fallback_with_level)
                    if strict_level_when_type_name_has_level and has_level_hint and lv_norm and sym_norm:
                        can_fallback = False
                        kret["strictLevelBlockedFallback"] += 1
                    if can_fallback:
                        cands = _lookup_row_candidates_by_symbol(row_candidates_by_symbol, sym_norm)
                        if len(cands) == 1:
                            exp = _build_expected_from_param_map_for_type(plist_for_match, cands[0], param_map)
                        elif len(cands) > 1:
                            best_row, _diag = _choose_best_row_for_param_map_type(plist_for_match, cands, param_map)
                            if best_row is not None:
                                exp = _build_expected_from_param_map_for_type(plist_for_match, best_row, param_map)
                        if not exp:
                            exp = _lookup_expected_by_symbol(expected_by_symbol, sym, sym_norm)
                        if exp:
                            kret["resolvedBySymbolFallback"] += 1
                if not exp:
                    kret["unresolvedTypes"] += 1
                    continue

                # 異種ファミリ混在（例: SRCでRC/S同名タイプ）時、
                # 対象パラメータが1つも存在しない型は差分対象外にする。
                if target_params and not any(tp in available_param_names for tp in target_params):
                    kret["skippedNoTargetParams"] += 1
                    continue
                # 梁: SRCの鉄骨側タイプ（B等のみ保持で鉄筋系を持たない型）は除外。
                if kind_logic == "frames":
                    rebar_targets = [tp for tp in target_params if ("主筋" in tp) or ("あばら" in tp)]
                    if rebar_targets and not any(tp in available_param_names for tp in rebar_targets):
                        kret["skippedNoTargetParams"] += 1
                        continue

                kret["matchedTypes"] += 1

                for m in param_map:
                    rname = str(m.get("revit") or "").strip()
                    conv = str(m.get("converter") or "str")
                    if not rname or rname not in exp:
                        continue
                    # 任意パラメータ（例: 左B/右B）が型に無い場合はスキップ
                    if rname not in available_param_names:
                        continue
                    expected_val = exp.get(rname)
                    if kind_logic == "frames":
                        expected_val = _frame_adjust_expected_by_counts(rname, expected_val, exp)
                    actual_val = read_actual_param(type_vals, rname, conv)

                    if equal_with_param(rname, actual_val, expected_val):
                        continue

                    diff = {
                        "typeId": tid,
                        "typeName": tname,
                        "familyName": str(t.get("familyName") or ""),
                        "symbol": sym_norm or sym,
                        "level": lv_norm,
                        "param": rname,
                        "actual": actual_val,
                        "expected": expected_val,
                    }
                    kret["diffs"].append(diff)

                    if mode_apply:
                        try:
                            u = update_param(base_url, str(kind_cfg.get("updateCommand") or ""), tid, rname, expected_val)
                            if bool(u.get("ok", True)):
                                kret["updated"].append(diff)
                            else:
                                kret["errors"].append({"op": "update", "diff": diff, "msg": u.get("msg", "update failed")})
                        except Exception as ex:
                            kret["errors"].append({"op": "update", "diff": diff, "msg": str(ex)})

        except Exception as ex:
            kret["ok"] = False
            kret["errors"].append({"stage": "kind", "msg": str(ex)})

        result["kinds"][kind_name] = kret

    if not args.skip_column_list_sync and str(args.column_list_mode).lower() != "none":
        col_info = expected_cache.get("columns")
        if col_info:
            try:
                result["columnListFamilies"] = sync_column_list_families(
                    base_url=base_url,
                    expected_info_columns=col_info,
                    mode_apply=mode_apply,
                    family_mode=str(args.column_list_mode).lower(),
                )
            except Exception as ex:
                result["columnListFamilies"] = {"ok": False, "errors": [{"stage": "column_list_sync", "msg": str(ex)}]}

    if bool(args.sync_frame_instances):
        frame_info = expected_cache.get("frames")
        frame_kind = (config.get("kinds") or {}).get("frames") if isinstance(config.get("kinds"), dict) else {}
        if frame_info and isinstance(frame_kind, dict):
            try:
                result["frameInstances"] = sync_frame_instances_from_expected(
                    base_url=base_url,
                    kind_cfg=frame_kind,
                    expected_info_frames=frame_info,
                    mode_apply=mode_apply,
                )
            except Exception as ex:
                result["frameInstances"] = {"ok": False, "errors": [{"stage": "frame_instance_sync", "msg": str(ex)}]}

    if not args.skip_beam_list_sync and str(args.beam_list_mode).lower() != "none":
        frame_info = expected_cache.get("frames")
        if frame_info:
            try:
                result["beamListFamilies"] = sync_beam_list_families(
                    base_url=base_url,
                    expected_info_frames=frame_info,
                    mode_apply=mode_apply,
                    family_mode=str(args.beam_list_mode).lower(),
                )
            except Exception as ex:
                result["beamListFamilies"] = {"ok": False, "errors": [{"stage": "beam_list_sync", "msg": str(ex)}]}

    out_path = _default_output_path(base_url, args.output)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with out_path.open("w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    diff_report_rows = _collect_diff_report_rows(result)
    diff_report_path = out_path.with_name(f"{out_path.stem}_diff_report.csv")
    _write_diff_report_csv(diff_report_path, diff_report_rows)

    print(
        json.dumps(
            {
                "ok": True,
                "mode": args.mode,
                "savedTo": str(out_path),
                "diffReportPath": str(diff_report_path),
                "diffReportRows": len(diff_report_rows),
                "summary": {
                    k: {
                        "placements": v.get("placements", 0),
                        "expectedSymbols": v.get("expectedSymbols", 0),
                        "matchedTypes": v.get("matchedTypes", 0),
                        "diffs": len(v.get("diffs") or []),
                        "updated": len(v.get("updated") or []),
                        "unapplied": len(v.get("unapplied") or []),
                        "errors": len(v.get("errors") or []),
                    }
                    for k, v in (result.get("kinds") or {}).items()
                },
                "columnListFamilies": {
                    "matchedTypes": (result.get("columnListFamilies") or {}).get("matchedTypes", 0),
                    "diffs": len(((result.get("columnListFamilies") or {}).get("diffs") or [])),
                    "updated": len(((result.get("columnListFamilies") or {}).get("updated") or [])),
                    "missingListTypeCount": (result.get("columnListFamilies") or {}).get("missingListTypeCount", 0),
                    "errors": len(((result.get("columnListFamilies") or {}).get("errors") or [])),
                }
                if isinstance(result.get("columnListFamilies"), dict)
                else {},
                "beamListFamilies": {
                    "matchedTypes": (result.get("beamListFamilies") or {}).get("matchedTypes", 0),
                    "diffs": len(((result.get("beamListFamilies") or {}).get("diffs") or [])),
                    "updated": len(((result.get("beamListFamilies") or {}).get("updated") or [])),
                    "missingListTypeCount": (result.get("beamListFamilies") or {}).get("missingListTypeCount", 0),
                    "errors": len(((result.get("beamListFamilies") or {}).get("errors") or [])),
                }
                if isinstance(result.get("beamListFamilies"), dict)
                else {},
                "frameInstances": {
                    "instances": (result.get("frameInstances") or {}).get("instances", 0),
                    "resolvedInstances": (result.get("frameInstances") or {}).get("resolvedInstances", 0),
                    "matchedInstances": (result.get("frameInstances") or {}).get("matchedInstances", 0),
                    "diffs": len(((result.get("frameInstances") or {}).get("diffs") or [])),
                    "updated": len(((result.get("frameInstances") or {}).get("updated") or [])),
                    "errors": len(((result.get("frameInstances") or {}).get("errors") or [])),
                }
                if isinstance(result.get("frameInstances"), dict)
                else {},
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
