# @feature: 柱リストのインスタンス生成・配置・ページ割付（Dynamo代替） | keywords: 柱リスト, 生成, 配置, ページ割付, RC, SRC
# -*- coding: utf-8 -*-
"""
柱リスト用詳細項目ファミリのインスタンスを自動生成し、
配置（行列化）とページ割付を行う Python Runner 用スクリプト。

目的:
- Dynamo の「インスタンス生成・配置・ページ割付」相当を
  外部Pythonで再現し、デバッグしやすくする。

前提:
- Revit MCP (port 5210 既定) が起動中
- 柱リスト用ファミリタイプ（RC/SRC）がプロジェクトにロード済み
- typeName は概ね `レベル_符号_区分`（区分: 全断面/柱頭/柱脚/同上）

注意:
- 本スクリプトは「詳細項目（ビュー専用）」の生成に `element.create_family_instance` を使います。
  Addin 側が `viewId` 指定に対応していない古い版では失敗します。
"""

import json
import os
import re
import time
from collections import defaultdict
from typing import Any, Dict, List, Optional, Tuple

import requests


# ----------------------------
# User Config (Python Runner向け)
# ----------------------------
PORT = int(os.environ.get("REVIT_MCP_PORT", "5210"))

# 対象ビュー:
# - None: 現在アクティブビュー
# - 数値: viewId
REF_VIEW_ID: Optional[int] = None
LIST_VIEW_ID: Optional[int] = None

# リスト種別:
# - "auto": SRC系ファミリが存在すればSRC優先、なければRC
# - "src" / "rc"
LIST_MODE = "auto"

# ページ割付
BUNKATSU = True
X_SIZE_MM = 37000.0
Y_SIZE_MM = 24500.0
PAGE_GAP_Y_MM = 2000.0

# 表示制御
BIKORAN = True
TEKKIN_BAIRITSU = 1.0
SET_TEKKIN_BAIRITSU = True

# 生成位置オフセット（mm）
BASE_X_MM = 0.0
BASE_Y_MM = 0.0
BASE_Z_MM = 0.0

# 生成前に既存を消すか（安全側で既定False）
DELETE_OLD_BY_COMMENT = False
COMMENT_TAG = "MCP:ColumnListAuto"

# 不足タイプの自動作成（Dynamo移植の要点）
AUTO_CREATE_MISSING_TYPES = True

# 新規作成タイプへ、元の構造柱タイプから同名パラメータを極力転記
COPY_COMMON_TYPE_PARAMS = True

# 進捗ログ
VERBOSE = True

# SRC の「同上」タイプはこのファミリを優先して複製元に使う
PREFERRED_SRC_DOUJOU_FAMILY = "s【SRC】SRC柱リスト改(同上)"
STRICT_PREFERRED_SRC_DOUJOU_FAMILY = True


# ----------------------------
# RPC helpers
# ----------------------------
DEFAULT_TIMEOUT = 30
POLL_INTERVAL = 0.5
POLL_TIMEOUT = 600
TYPE_PARAM_POLL_TIMEOUT = 120
LEVEL_FETCH_POLL_TIMEOUT = 60


def _log(msg: str) -> None:
    if VERBOSE:
        print(msg)


def detect_rpc_endpoint(base_url: str) -> str:
    for ep in ("/rpc", "/jsonrpc"):
        url = f"{base_url}{ep}"
        try:
            requests.post(
                url,
                json={"jsonrpc": "2.0", "id": "ping", "method": "noop"},
                timeout=3,
            )
            return url
        except Exception:
            continue
    return f"{base_url}/rpc"


def unwrap_result(obj: Any) -> Any:
    cur = obj
    while isinstance(cur, dict) and isinstance(cur.get("result"), dict):
        cur = cur.get("result")
    return cur


def poll_job(base_url: str, job_id: str, timeout_sec: int = POLL_TIMEOUT) -> Dict[str, Any]:
    deadline = time.time() + timeout_sec
    job_url = f"{base_url}/job/{job_id}"
    while time.time() < deadline:
        r = requests.get(job_url, timeout=10)
        if r.status_code in (202, 204):
            time.sleep(POLL_INTERVAL)
            continue
        r.raise_for_status()
        job = r.json()
        state = str(job.get("state") or "").upper()
        if state == "SUCCEEDED":
            result_json = job.get("result_json")
            if not result_json:
                return {"ok": True}
            parsed = json.loads(result_json)
            return unwrap_result(parsed)
        if state in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(job.get("error_msg") or state)
        time.sleep(POLL_INTERVAL)
    raise TimeoutError(f"job polling timed out (jobId={job_id})")


def rpc(base_url: str, method: str, params: Optional[dict] = None, poll_timeout_sec: Optional[int] = None) -> Any:
    endpoint = detect_rpc_endpoint(base_url)
    payload = {
        "jsonrpc": "2.0",
        "id": f"req-{int(time.time() * 1000)}",
        "method": method,
        "params": params or {},
    }
    r = requests.post(endpoint, json=payload, timeout=DEFAULT_TIMEOUT)
    r.raise_for_status()
    data = r.json()
    if "error" in data:
        raise RuntimeError(data["error"])
    result = data.get("result", {})
    if isinstance(result, dict) and result.get("queued"):
        job_id = result.get("jobId") or result.get("job_id")
        if not job_id:
            raise RuntimeError("queued but jobId missing")
        return poll_job(base_url, job_id, timeout_sec=int(poll_timeout_sec or POLL_TIMEOUT))
    return unwrap_result(result)


def _extract_list(env: Dict[str, Any], keys: List[str]) -> List[Dict[str, Any]]:
    if not isinstance(env, dict):
        return []
    for k in keys:
        v = env.get(k)
        if isinstance(v, list):
            return v
    return []


def _to_num(v: Any) -> Optional[float]:
    if v is None:
        return None
    if isinstance(v, (int, float)):
        return float(v)
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


def _norm_level(s: str) -> str:
    t = str(s or "").strip().upper()
    t = t.replace("　", "").replace(" ", "")
    t = t.replace("Ｌ", "L")
    return t


def _norm_symbol(s: str) -> str:
    t = str(s or "").strip().upper()
    t = t.replace("　", "").replace(" ", "")
    return t


def _sym_sort_key(sym: str):
    parts = re.split(r"(\d+)", str(sym or "").upper())
    key = []
    for p in parts:
        if p == "":
            continue
        if p.isdigit():
            key.append((0, int(p)))
        else:
            key.append((1, p))
    return key


def _eq(a: Any, b: Any) -> bool:
    na = _to_num(a)
    nb = _to_num(b)
    if na is not None and nb is not None:
        return abs(na - nb) <= 1e-6
    return str(a or "").strip() == str(b or "").strip()


def _parse_list_type_name(type_name: str) -> Optional[Dict[str, str]]:
    zones = ("全断面", "柱頭", "柱脚", "同上")
    s = str(type_name or "").strip()
    s = s.replace("（", "(").replace("）", ")")
    s = s.replace("＿", "_").replace("-", "_")
    zone = None
    for z in zones:
        if s.endswith("_" + z):
            zone = z
            s = s[: -(len(z) + 1)]
            break
        if s.endswith(z):
            zone = z
            s = s[: -len(z)]
            break
    if not zone:
        return None
    if "_" not in s:
        return None
    level_raw, symbol_raw = s.split("_", 1)
    level_raw = str(level_raw or "").strip()
    symbol_raw = str(symbol_raw or "").strip()
    if not level_raw or not symbol_raw:
        return None
    return {
        "levelRaw": level_raw,
        "symbolRaw": symbol_raw,
        "levelNorm": _norm_level(level_raw),
        "symbolNorm": _norm_symbol(symbol_raw),
        "zone": zone,
    }


def _parse_list_type_from_values(type_name: str, values: Dict[str, Any]) -> Optional[Dict[str, str]]:
    """
    Dynamo運用では、Revitの「タイプ名(name)」ではなく、
    タイプパラメータ「タイプ名」に `レベル_符号_区分` を持つ場合がある。
    そのため、候補文字列を複数試す。
    """
    candidates = []
    base = str(type_name or "").strip()
    if base:
        candidates.append(base)
    for k in ("タイプ名", "Type Name", "タイプ名(表示)", "TypeName"):
        v = values.get(k) if isinstance(values, dict) else None
        if v is None:
            continue
        s = str(v).strip()
        if s and s not in candidates:
            candidates.append(s)

    for c in candidates:
        p = _parse_list_type_name(c)
        if p:
            return p
    return None


def _pick_value(values: Dict[str, Any], names: List[str], default: Any = None) -> Any:
    for n in names:
        if n in values and values[n] not in (None, ""):
            return values[n]
    return default


def _has_top_bottom_split(values: Dict[str, Any]) -> bool:
    for k, v in values.items():
        name = str(k or "")
        if not name.startswith("柱脚"):
            continue
        top = "柱頭" + name[2:]
        if top in values and not _eq(v, values.get(top)):
            return True
    return False


def _extract_symbol_from_column(col: Dict[str, Any], type_values: Dict[str, Any]) -> str:
    # 1) typeパラメータ優先
    s = _pick_value(type_values, ["符号", "マーク(タイプ)", "タイプ記号"], "")
    s = str(s or "").strip()
    if s:
        return s

    # 2) instance情報
    for k in ("symbol", "mark", "typeMark", "tag", "code"):
        v = str(col.get(k) or "").strip()
        if v:
            return v

    # 3) 最後のフォールバック: typeName
    tn = str(col.get("typeName") or "").strip()
    if tn:
        return tn

    return ""


def _is_target_rc_src_column(col: Dict[str, Any], type_values: Dict[str, Any]) -> bool:
    """
    柱リスト生成の対象を RC/SRC 柱に限定する。
    S柱(純鉄骨)は除外。
    """
    fam = str(col.get("familyName") or "")
    fam_up = fam.upper()

    # 明示除外（純鉄骨）
    if ("S柱" in fam) and ("SRC" not in fam_up):
        return False
    if ("鉄骨" in fam) and ("SRC" not in fam_up):
        return False
    if "STEEL" in fam_up and "SRC" not in fam_up:
        return False

    # 明示採用（RC/SRC 系）
    if "SRC" in fam_up:
        return True
    if "RC" in fam_up:
        return True
    if ("角柱" in fam) or ("円柱" in fam):
        return True

    # 符号ヒントで純鉄骨柱を除外（例: S1, SC1 など）
    sym = str(_pick_value(type_values, ["符号", "マーク(タイプ)", "タイプ記号"], "") or "").strip().upper()
    if not sym:
        sym = str(col.get("symbol") or col.get("mark") or "").strip().upper()
    if sym:
        if sym.startswith("SRC"):
            return True
        if sym.startswith("S"):
            return False

    # パラメータ名ヒントで判定（RC/SRC 柱タイプに出やすい）
    keys = [str(k or "") for k in (type_values or {}).keys()]
    has_rebar_keys = any(("主筋" in k) or ("フープ" in k) or ("帯筋" in k) for k in keys)
    has_steel_only = any("鉄骨材種" in k for k in keys) and (not has_rebar_keys)
    if has_steel_only:
        return False
    if has_rebar_keys:
        return True

    return False


def _set_instance_param(base_url: str, eid: int, name: str, value: Any) -> bool:
    try:
        ret = rpc(
            base_url,
            "element.update_family_instance_parameter",
            {"elementId": int(eid), "paramName": name, "value": value},
        )
        return bool(ret.get("ok", True))
    except Exception:
        return False


def _set_type_param(base_url: str, tid: int, name: str, value: Any) -> bool:
    try:
        ret = rpc(
            base_url,
            "element.set_family_type_parameter",
            {"typeId": int(tid), "paramName": name, "value": value},
        )
        return bool(ret.get("ok", True))
    except Exception:
        return False


def _is_preferred_src_doujou_family_name(fam: str) -> bool:
    f = str(fam or "")
    return f == PREFERRED_SRC_DOUJOU_FAMILY or (PREFERRED_SRC_DOUJOU_FAMILY in f)


def _update_parameters_batch(
    base_url: str,
    items: List[Dict[str, Any]],
    batch_size: int = 300,
    max_millis_per_tx: int = 2500,
    suppress_items: bool = True,
) -> Dict[str, Any]:
    if not items:
        return {"ok": True, "updatedCount": 0, "failedCount": 0, "completed": True}

    start_index = 0
    updated_total = 0
    failed_total = 0
    rounds = 0
    while start_index < len(items):
        rounds += 1
        payload = {
            "items": items,
            "startIndex": int(start_index),
            "batchSize": int(batch_size),
            "maxMillisPerTx": int(max_millis_per_tx),
            "suppressItems": bool(suppress_items),
        }
        ret = None
        last_err = None
        for m in ("element.update_parameters_batch", "update_parameters_batch"):
            try:
                ret = rpc(base_url, m, payload, poll_timeout_sec=TYPE_PARAM_POLL_TIMEOUT)
                break
            except Exception as ex:
                last_err = ex
                continue
        if ret is None:
            return {"ok": False, "msg": f"update_parameters_batch failed: {last_err}"}

        updated_total += int(ret.get("updatedCount") or 0)
        failed_total += int(ret.get("failedCount") or 0)
        next_index = ret.get("nextIndex")
        completed = bool(ret.get("completed", False))
        if completed:
            break
        if next_index is None:
            start_index += int(batch_size)
        else:
            ni = int(next_index)
            if ni <= start_index:
                start_index += int(batch_size)
            else:
                start_index = ni
        if rounds > 10000:
            return {
                "ok": False,
                "msg": "update_parameters_batch runaway guard",
                "updatedCount": updated_total,
                "failedCount": failed_total,
            }

    return {"ok": True, "updatedCount": updated_total, "failedCount": failed_total, "completed": True}


def _set_type_params_bulk(base_url: str, type_id: int, param_values: Dict[str, Any]) -> Dict[str, Any]:
    items = []
    for k, v in param_values.items():
        items.append({"typeId": int(type_id), "target": "type", "paramName": str(k), "value": v})
    res = _update_parameters_batch(base_url, items)
    if not bool(res.get("ok", False)):
        # fallback
        applied = 0
        for k, v in param_values.items():
            if _set_type_param(base_url, int(type_id), str(k), v):
                applied += 1
        return {"ok": False, "fallback": True, "updatedCount": applied, "failedCount": max(0, len(items) - applied)}
    return res


def _set_instance_params_bulk(base_url: str, element_id: int, param_values: Dict[str, Any]) -> Dict[str, Any]:
    items = []
    for k, v in param_values.items():
        items.append({"elementId": int(element_id), "target": "instance", "paramName": str(k), "value": v})
    res = _update_parameters_batch(base_url, items)
    if not bool(res.get("ok", False)):
        # fallback
        applied = 0
        for k, v in param_values.items():
            if _set_instance_param(base_url, int(element_id), str(k), v):
                applied += 1
        return {"ok": False, "fallback": True, "updatedCount": applied, "failedCount": max(0, len(items) - applied)}
    return res


def _duplicate_family_type(base_url: str, source_type_id: int, new_name: str) -> Dict[str, Any]:
    last_err = None
    for method in ("element.duplicate_family_type", "duplicate_family_type"):
        try:
            ret = rpc(
                base_url,
                method,
                {"sourceTypeId": int(source_type_id), "newName": str(new_name), "allowExisting": True},
            )
            if isinstance(ret, dict) and (ret.get("ok", False) or int(ret.get("typeId") or 0) > 0):
                return ret
            last_err = ret
        except Exception as ex:
            last_err = str(ex)
    return {"ok": False, "msg": f"duplicate_family_type failed: {last_err}"}


def _is_circle_type_record(rec: Dict[str, Any]) -> bool:
    fam = str(rec.get("familyName") or "")
    tname = str(rec.get("typeName") or "")
    vals = rec.get("typeValues") if isinstance(rec.get("typeValues"), dict) else {}
    if "円柱" in fam or "円柱" in tname:
        return True
    shape = str(_pick_value(vals, ["柱断面形状", "断面形状"], "") or "")
    if "円" in shape:
        return True
    return False


def _is_blank_type_record(rec: Dict[str, Any]) -> bool:
    fam = str(rec.get("familyName") or "")
    tname = str(rec.get("typeName") or "")
    dname = str(rec.get("displayTypeName") or "")
    return ("空欄" in fam) or ("空欄" in tname) or ("空欄" in dname)


def _is_src_type_record(rec: Dict[str, Any]) -> bool:
    fam = str(rec.get("familyName") or "")
    return ("SRC" in fam) or ("【SRC】" in fam)


def _is_doujou_type_record(rec: Dict[str, Any]) -> bool:
    dname = str(rec.get("displayTypeName") or "")
    tname = str(rec.get("typeName") or "")
    return dname.endswith("_同上") or tname.endswith("_同上") or ("同上" in dname)


def _pick_template_type(
    list_type_records: List[Dict[str, Any]],
    mode: str,
    zone: str,
    want_circle: bool,
) -> Optional[Dict[str, Any]]:
    cands = [r for r in list_type_records if not _is_blank_type_record(r)]
    if not cands:
        return None

    if zone == "同上":
        dz = [r for r in cands if _is_doujou_type_record(r)]
        if dz:
            cands = dz

    if want_circle:
        cc = [r for r in cands if _is_circle_type_record(r)]
        if cc:
            cands = cc
    else:
        nn = [r for r in cands if not _is_circle_type_record(r)]
        if nn:
            cands = nn

    if mode == "src":
        src_only = [r for r in cands if _is_src_type_record(r)]
        if src_only:
            cands = src_only
    elif mode == "rc":
        rc_only = [r for r in cands if not _is_src_type_record(r)]
        if rc_only:
            cands = rc_only

    # 要件: SRC の「同上」は s【SRC】SRC柱リスト改(同上) を最優先
    if mode == "src" and zone == "同上":
        pref_exact = [r for r in cands if str(r.get("familyName") or "") == PREFERRED_SRC_DOUJOU_FAMILY]
        if pref_exact:
            cands = pref_exact
        else:
            pref_contains = [r for r in cands if PREFERRED_SRC_DOUJOU_FAMILY in str(r.get("familyName") or "")]
            if pref_contains:
                cands = pref_contains
            elif STRICT_PREFERRED_SRC_DOUJOU_FAMILY:
                return None

    # 安定化: family/type 名順
    cands = sorted(
        cands,
        key=lambda r: (
            str(r.get("familyName") or ""),
            str(r.get("displayTypeName") or r.get("typeName") or ""),
            int(r.get("typeId") or 0),
        ),
    )
    return cands[0] if cands else None


def _copy_common_type_params(
    base_url: str,
    src_values: Dict[str, Any],
    dst_type_id: int,
    dst_values: Dict[str, Any],
) -> Dict[str, int]:
    if not COPY_COMMON_TYPE_PARAMS:
        return {"attempted": 0, "applied": 0}

    skip = {
        "タイプ名",
        "Type Name",
        "レベル名",
        "符号",
        "柱頭断面",
        "柱脚断面",
        "柱頭パネル表示",
        "柱脚パネル表示",
        "寸法凡例表示",
        "備考表示",
        "左欄",
        "階表示",
        "符号表示",
        "枠W",
        "枠H",
        "コメント",
    }
    attempted = 0
    param_values: Dict[str, Any] = {}
    for name in sorted(set(src_values.keys()) & set(dst_values.keys())):
        if name in skip:
            continue
        sval = src_values.get(name)
        if sval in (None, ""):
            continue
        dval = dst_values.get(name)
        if _eq(sval, dval):
            continue
        attempted += 1
        param_values[str(name)] = sval
    if attempted == 0:
        return {"attempted": 0, "applied": 0}
    res = _set_type_params_bulk(base_url, int(dst_type_id), param_values)
    applied = int(res.get("updatedCount") or 0)
    return {"attempted": attempted, "applied": applied}


def _collect_src_shape_tokens(source_family_name: str, source_type_values: Dict[str, Any]) -> str:
    parts: List[str] = [str(source_family_name or "")]
    keys = [
        "鉄骨断面",
        "鉄骨断面形状",
        "柱脚断面形状1",
        "柱頭断面形状1",
        "断面形状1",
        "柱脚断面形状",
        "柱頭断面形状",
        "断面形状",
        "柱脚断面",
        "柱頭断面",
    ]
    for k in keys:
        v = source_type_values.get(k)
        if v in (None, ""):
            continue
        parts.append(str(v))
    return " | ".join(parts)


def _infer_src_steel_shape_mode(source_family_name: str, source_type_values: Dict[str, Any]) -> str:
    """
    Dynamo準拠で SRC柱リストの鋼形状モードを推定。
    返り値:
      c, txu, txd, tyl, tyr, hrx, hry, brx, bry, pipe, unknown
    """
    tok = _collect_src_shape_tokens(source_family_name, source_type_values)
    up = tok.upper()

    def _has_any(cands: List[str]) -> bool:
        for c in cands:
            if c and c in tok:
                return True
        return False

    def _has_any_upper(cands: List[str]) -> bool:
        for c in cands:
            if c and c in up:
                return True
        return False

    # Dynamoの分岐順に近い優先順位
    if _has_any(["S柱T型XU", "T型XU"]):
        return "txu"
    if _has_any(["S柱T型XD", "T型XD"]):
        return "txd"
    if _has_any(["S柱T型YL", "T型YL"]):
        return "tyl"
    if _has_any(["S柱T型YR", "T型YR"]):
        return "tyr"
    if _has_any(["S柱HR型X", "HR型X"]):
        return "hrx"
    if _has_any(["S柱HR型Y", "HR型Y"]):
        return "hry"
    if _has_any(["S柱BR型X", "S柱B型X", "BR型X", "B型X"]):
        return "brx"
    if _has_any(["S柱BR型Y", "S柱B型Y", "BR型Y", "B型Y"]):
        return "bry"
    if _has_any(["S柱P型", "P型", "円形鋼管"]) or _has_any_upper(["PIPE"]):
        return "pipe"
    if _has_any(["S柱C型", "C型", "角形鋼管", "Ｈ形鋼", "H形鋼"]) or _has_any_upper(["BOX"]):
        return "c"

    # 緩いフォールバック
    if _has_any_upper(["P", "PIPE"]):
        return "pipe"
    if _has_any_upper(["HR", "H"]) or _has_any(["□", "B□"]):
        return "c"
    return "unknown"


def _apply_src_pipe_box_flags(
    base_url: str,
    dst_type_id: int,
    source_family_name: str,
    source_type_values: Dict[str, Any],
) -> Dict[str, Any]:
    """
    Dynamoの表示フラグに寄せて設定。
    対象:
      鉄骨「ボックス」, 鉄骨「パイプ」, 鉄骨「HX」, 鉄骨「HY」, 鉄骨「┻」, 鉄骨「┣」, 鉄骨「┳」, 鉄骨「┫」
    """
    mode = _infer_src_steel_shape_mode(source_family_name, source_type_values)

    # Dynamo抽出コード準拠
    profile_map: Dict[str, Dict[str, int]] = {
        "c": {"box": 0, "pipe": 0, "hx": 1, "hy": 1, "t_bottom": 0, "t_left": 0, "t_top": 0, "t_right": 0},
        "txu": {"box": 0, "pipe": 0, "hx": 0, "hy": 0, "t_bottom": 1, "t_left": 0, "t_top": 0, "t_right": 0},
        "txd": {"box": 0, "pipe": 0, "hx": 0, "hy": 0, "t_bottom": 0, "t_left": 0, "t_top": 1, "t_right": 0},
        "tyl": {"box": 0, "pipe": 0, "hx": 0, "hy": 0, "t_bottom": 0, "t_left": 0, "t_top": 0, "t_right": 1},
        "tyr": {"box": 0, "pipe": 0, "hx": 0, "hy": 0, "t_bottom": 0, "t_left": 1, "t_top": 0, "t_right": 0},
        "hrx": {"box": 0, "pipe": 0, "hx": 1, "hy": 0, "t_bottom": 0, "t_left": 0, "t_top": 0, "t_right": 0},
        "hry": {"box": 0, "pipe": 0, "hx": 0, "hy": 1, "t_bottom": 0, "t_left": 0, "t_top": 0, "t_right": 0},
        "brx": {"box": 1, "pipe": 0, "hx": 0, "hy": 0, "t_bottom": 0, "t_left": 0, "t_top": 0, "t_right": 0},
        "bry": {"box": 1, "pipe": 0, "hx": 0, "hy": 0, "t_bottom": 0, "t_left": 0, "t_top": 0, "t_right": 0},
        "pipe": {"box": 0, "pipe": 1, "hx": 0, "hy": 0, "t_bottom": 0, "t_left": 0, "t_top": 0, "t_right": 0},
        # 不明は誤表示防止で全OFF
        "unknown": {"box": 0, "pipe": 0, "hx": 0, "hy": 0, "t_bottom": 0, "t_left": 0, "t_top": 0, "t_right": 0},
    }
    prof = profile_map.get(mode, profile_map["unknown"])

    write_map = {
        "鉄骨「ボックス」": prof["box"],
        "鉄骨「パイプ」": prof["pipe"],
        "鉄骨「HX」": prof["hx"],
        "鉄骨「HY」": prof["hy"],
        "鉄骨「┻」": prof["t_bottom"],
        "鉄骨「┣」": prof["t_left"],
        "鉄骨「┳」": prof["t_top"],
        "鉄骨「┫」": prof["t_right"],
    }
    # 記号の最低限整合（Dynamoでは枝ごとに細かく設定）
    if mode == "pipe":
        write_map["鉄骨X方向記号（H形のみ）"] = "〇"
    elif mode in ("brx", "bry"):
        write_map["鉄骨X方向記号（H形のみ）"] = "□"
    res = _set_type_params_bulk(base_url, int(dst_type_id), write_map)

    return {
        "mode": mode,
        "values": write_map,
        "setResults": {
            "ok": bool(res.get("ok", False)),
            "updatedCount": int(res.get("updatedCount") or 0),
            "failedCount": int(res.get("failedCount") or 0),
            "fallback": bool(res.get("fallback", False)),
        },
        "shapeTokens": _collect_src_shape_tokens(source_family_name, source_type_values)[:300],
    }


def _ensure_missing_types_from_columns(
    base_url: str,
    mode: str,
    list_type_records: List[Dict[str, Any]],
    type_by_key_zone: Dict[Tuple[str, str, str], Dict[str, Any]],
    cell_seed: Dict[Tuple[str, str], Dict[str, Any]],
) -> Dict[str, Any]:
    created = []
    errors = []

    def _required_zones(split: bool) -> List[str]:
        return ["全断面", "柱頭", "柱脚"] if split else ["全断面", "同上"]

    for _, seed in sorted(
        cell_seed.items(),
        key=lambda kv: (str(kv[0][0]), _sym_sort_key(str(kv[0][1]))),
    ):
        level_norm = seed["levelNorm"]
        symbol_norm = seed["symbolNorm"]
        level_raw = str(seed.get("levelRaw") or level_norm)
        symbol_raw = str(seed.get("symbolRaw") or symbol_norm)
        split = bool(seed.get("split"))
        src_vals = seed.get("srcTypeValues") if isinstance(seed.get("srcTypeValues"), dict) else {}
        src_fam = str(seed.get("srcFamilyName") or "")
        src_shape = str(_pick_value(src_vals, ["柱断面形状", "断面形状"], "") or "")
        want_circle = ("円柱" in src_fam) or ("円" in src_shape)

        for zone in _required_zones(split):
            k = (level_norm, symbol_norm, zone)
            if k in type_by_key_zone:
                existing = type_by_key_zone.get(k) or {}
                if not (mode == "src" and zone == "同上" and not _is_preferred_src_doujou_family_name(str(existing.get("familyName") or ""))):
                    continue

            tpl = _pick_template_type(list_type_records, mode=mode, zone=zone, want_circle=want_circle)
            if not tpl:
                errors.append(
                    {
                        "level": level_raw,
                        "symbol": symbol_raw,
                        "zone": zone,
                        "msg": "template type not found",
                    }
                )
                continue

            new_type_name = f"{level_raw}_{symbol_raw}_{zone}"
            dup = _duplicate_family_type(base_url, int(tpl.get("typeId") or 0), new_type_name)
            new_tid = int(dup.get("typeId") or 0)
            if new_tid <= 0:
                errors.append(
                    {
                        "level": level_raw,
                        "symbol": symbol_raw,
                        "zone": zone,
                        "templateTypeId": int(tpl.get("typeId") or 0),
                        "msg": str(dup.get("msg") or "duplicate failed"),
                    }
                )
                continue

            # 重要パラメータを整形
            header_params = {
                "タイプ名": new_type_name,
                "レベル名": str(level_raw).replace("L", ""),
                "符号": symbol_raw,
            }
            if zone == "柱頭":
                header_params["柱頭断面"] = 1
                header_params["柱脚断面"] = 0
            elif zone == "柱脚":
                header_params["柱頭断面"] = 0
                header_params["柱脚断面"] = 1
            elif zone == "全断面":
                header_params["柱頭断面"] = 1
                header_params["柱脚断面"] = 1
            elif zone == "同上":
                header_params["柱頭断面"] = 0
                header_params["柱脚断面"] = 1
            _set_type_params_bulk(base_url, new_tid, header_params)

            new_vals = _get_type_params(base_url, new_tid)
            copy_stat = _copy_common_type_params(base_url, src_vals, new_tid, new_vals)

            # Dynamo移植補正: SRCの鋼表示フラグ（特に「鉄骨「パイプ」」誤ON防止）
            src_pipe_box = None
            if mode == "src":
                src_pipe_box = _apply_src_pipe_box_flags(
                    base_url=base_url,
                    dst_type_id=new_tid,
                    source_family_name=src_fam,
                    source_type_values=src_vals,
                )

            new_vals = _get_type_params(base_url, new_tid)

            rec = {
                "typeId": new_tid,
                "typeName": new_type_name,
                "displayTypeName": str(_pick_value(new_vals, ["タイプ名", "Type Name"], new_type_name) or new_type_name),
                "familyName": str(dup.get("familyName") or tpl.get("familyName") or ""),
                "levelNorm": level_norm,
                "symbolNorm": symbol_norm,
                "zone": zone,
                "typeValues": new_vals,
            }
            type_by_key_zone[k] = rec
            list_type_records.append(rec)
            created.append(
                {
                    "level": level_raw,
                    "symbol": symbol_raw,
                    "zone": zone,
                    "typeId": new_tid,
                    "typeName": new_type_name,
                    "familyName": rec["familyName"],
                    "templateTypeId": int(tpl.get("typeId") or 0),
                    "copiedParams": copy_stat,
                    "srcPipeBox": src_pipe_box,
                }
            )

    return {"created": created, "errors": errors}


def _resolve_active_view_id(base_url: str) -> int:
    ctx = rpc(base_url, "help.get_context", {"includeSelectionIds": False, "maxSelectionIds": 0})
    data = ctx.get("data") if isinstance(ctx.get("data"), dict) else ctx
    vid = data.get("activeViewId") if isinstance(data, dict) else None
    if not isinstance(vid, int) or vid <= 0:
        raise RuntimeError("activeViewId を取得できませんでした。")
    return vid


def _fetch_levels(base_url: str) -> Dict[str, float]:
    env = rpc(base_url, "element.list_levels_simple", {}, poll_timeout_sec=LEVEL_FETCH_POLL_TIMEOUT)
    items = _extract_list(env, ["items", "levels"])
    out: Dict[str, float] = {}
    for it in items:
        if not isinstance(it, dict):
            continue
        name = str(it.get("name") or "").strip()
        elev = _to_num(it.get("elevation"))
        if not name or elev is None:
            continue
        # list_levels_simple の elevation は m を想定
        out[_norm_level(name)] = float(elev) * 1000.0
    return out


def _fetch_structural_columns(base_url: str, ref_view_id: Optional[int]) -> List[Dict[str, Any]]:
    items: List[Dict[str, Any]] = []
    skip = 0
    count = 2000
    seen_ids = set()
    max_pages = 200
    page_no = 0
    while True:
        page_no += 1
        if page_no > max_pages:
            _log(f"[WARN] structuralColumns paging reached max_pages={max_pages}; stop.")
            break
        params = {"skip": skip, "count": count}
        if isinstance(ref_view_id, int) and ref_view_id > 0:
            params["viewId"] = int(ref_view_id)
        env = rpc(base_url, "element.get_structural_columns", params)
        page = _extract_list(env, ["structuralColumns", "items", "elements"])
        if not page:
            break
        page_items = [x for x in page if isinstance(x, dict)]
        new_items = []
        for x in page_items:
            eid = int(x.get("elementId") or x.get("id") or 0)
            if eid <= 0:
                continue
            if eid in seen_ids:
                continue
            seen_ids.add(eid)
            new_items.append(x)
        items.extend(new_items)
        _log(
            f"[INFO] structuralColumns page={page_no} raw={len(page_items)} new={len(new_items)} total={len(items)} skip={skip}"
        )
        if len(new_items) == 0:
            _log("[WARN] structuralColumns pagination returned no new ids; stop.")
            break
        if len(page) < count:
            break
        skip += len(page)
    return items


def _fetch_family_types_detail(base_url: str) -> List[Dict[str, Any]]:
    env = rpc(
        base_url,
        "element.get_family_types",
        {"categoryName": "詳細項目", "skip": 0, "count": 20000, "namesOnly": False},
    )
    return _extract_list(env, ["types", "items", "elements"])


def _get_type_params(base_url: str, type_id: int) -> Dict[str, Any]:
    env = rpc(
        base_url,
        "element.get_family_type_parameters",
        {"typeId": int(type_id)},
        poll_timeout_sec=TYPE_PARAM_POLL_TIMEOUT,
    )
    plist = _extract_list(env, ["parameters", "params"])
    values: Dict[str, Any] = {}
    for p in plist:
        if not isinstance(p, dict):
            continue
        n = str(p.get("name") or "").strip()
        if not n:
            continue
        if n not in values or values[n] in ("", None):
            values[n] = p.get("value")
    return values


def _pick_mode_from_families(list_types: List[Dict[str, Any]], override: str) -> str:
    o = str(override or "auto").strip().lower()
    if o in ("src", "rc"):
        return o
    has_src = False
    has_rc = False
    for t in list_types:
        fam = str(t.get("familyName") or "")
        if "柱リスト" not in fam:
            continue
        if ("SRC" in fam) or ("【SRC】" in fam):
            has_src = True
        if ("［RC］" in fam) or ("[RC]" in fam) or ("RC柱リスト" in fam):
            has_rc = True
    if has_src:
        return "src"
    if has_rc:
        return "rc"
    return "src"


def _matches_mode(family_name: str, mode: str) -> bool:
    fam = str(family_name or "")
    if "柱リスト" not in fam:
        return False
    is_src = ("SRC" in fam) or ("【SRC】" in fam)
    is_rc = ("［RC］" in fam) or ("[RC]" in fam) or ("RC柱リスト" in fam)
    if mode == "src":
        return is_src
    if mode == "rc":
        return is_rc and (not is_src)
    return is_src or is_rc


def _build_layout_cells(
    symbols_sorted: List[str],
    rows: List[Dict[str, Any]],
    type_by_key_zone: Dict[Tuple[str, str, str], Dict[str, Any]],
    blank_type: Optional[Dict[str, Any]],
    n_rc_src: int,
    bikoran: bool,
) -> Tuple[List[List[Dict[str, Any]]], List[float], List[float], List[List[int]]]:
    nx = len(symbols_sorted)
    ny = len(rows)
    cells: List[List[Dict[str, Any]]] = [[{} for _ in range(ny)] for _ in range(nx)]
    occupied = [["KUU" for _ in range(ny + 1)] for _ in range(nx)]

    for ix, sym in enumerate(symbols_sorted):
        for iy, row in enumerate(rows):
            level = row["levelNorm"]
            row_kind = row["rowKind"]
            zone_pref = ["全断面", "柱頭"] if row_kind == "upper" else ["同上", "柱脚"]
            chosen = None
            chosen_zone = "空欄"
            for z in zone_pref:
                k = (level, sym, z)
                if k in type_by_key_zone:
                    chosen = type_by_key_zone[k]
                    chosen_zone = z
                    break
            if chosen is None and blank_type is not None:
                chosen = blank_type

            info = {
                "ix": ix,
                "iy": iy,
                "levelNorm": level,
                "levelRaw": row["levelRaw"],
                "symbolNorm": sym,
                "symbolRaw": row["symbolRawByNorm"].get(sym, sym),
                "rowKind": row_kind,
                "zone": chosen_zone,
                "type": chosen,
            }
            cells[ix][iy] = info
            if chosen is not None and "空欄" not in str(chosen.get("familyName") or ""):
                occupied[ix][iy] = "ARI"

    saikakai_y = [0] * ny
    for iy in range(ny):
        for ix in range(nx):
            if occupied[ix][iy] == "ARI" and occupied[ix][iy + 1] == "KUU":
                saikakai_y[iy] = 1
                break

    max_w = [2400.0] * nx
    max_h = [2000.0 + 400.0 * max(1, n_rc_src)] * ny

    def _dim_of_type(t: Optional[Dict[str, Any]]) -> Tuple[float, float]:
        if not t:
            return 500.0, 500.0
        fam = str(t.get("familyName") or "")
        vals = t.get("typeValues") if isinstance(t.get("typeValues"), dict) else {}
        if "空欄" in fam:
            return 500.0, 500.0
        if "円柱" in fam:
            d = _to_num(_pick_value(vals, ["柱せい", "Dx", "Dy"], 500.0)) or 500.0
            return float(d), float(d)
        dx = _to_num(_pick_value(vals, ["Dx", "B", "柱幅", "幅"], 500.0)) or 500.0
        dy = _to_num(_pick_value(vals, ["Dy", "D", "柱せい", "せい"], dx)) or dx
        return float(dx), float(dy)

    for ix in range(nx):
        for iy in range(ny):
            t = cells[ix][iy].get("type")
            dx, dy = _dim_of_type(t)
            w = round((dx + 1400.0) / 100.0, 0) * 100.0 + 400.0
            if w < 2400.0:
                w = 2400.0
            h = round((dy + 1400.0) / 100.0, 0) * 100.0 + 400.0 * max(1, n_rc_src)
            if h < 2400.0:
                h = 2000.0 + 400.0 * max(1, n_rc_src)
            cells[ix][iy]["wakuW"] = w
            cells[ix][iy]["wakuH"] = h
            if w > max_w[ix]:
                max_w[ix] = w
            if h > max_h[iy]:
                max_h[iy] = h

    box_w = [0.0] * nx
    box_h = [0.0] * ny
    for ix in range(nx):
        for iy in range(ny):
            y_add = 600.0 * (max(1, n_rc_src) - 1) + 1200.0 + 600.0
            if bikoran:
                y_add += 300.0
            if saikakai_y[iy] == 1:
                y_add += 300.0
            box_w_i = max_w[ix]
            box_h_i = max_h[iy] + y_add
            if box_w[ix] < box_w_i:
                box_w[ix] = box_w_i
            if box_h[iy] < box_h_i:
                box_h[iy] = box_h_i

    page_map = [[0 for _ in range(ny)] for _ in range(nx)]
    if BUNKATSU:
        total_x = sum(box_w)
        n_x_max = int(total_x / X_SIZE_MM) + 1
        cum_x = 0.0
        for ix in range(nx):
            cum_x += box_w[ix]
            n_x = int(cum_x / X_SIZE_MM)
            cum_y = 0.0
            for iy in range(ny):
                cum_y += box_h[iy]
                n_y = int(cum_y / Y_SIZE_MM)
                page_map[ix][iy] = n_x + n_x_max * n_y
    return cells, box_w, box_h, page_map


def main() -> int:
    base_url = f"http://127.0.0.1:{PORT}"
    _log(f"[INFO] base_url={base_url}")

    active_view_id = _resolve_active_view_id(base_url)
    ref_view_id = REF_VIEW_ID if isinstance(REF_VIEW_ID, int) and REF_VIEW_ID > 0 else active_view_id
    list_view_id = LIST_VIEW_ID if isinstance(LIST_VIEW_ID, int) and LIST_VIEW_ID > 0 else active_view_id
    _log(f"[INFO] refViewId={ref_view_id}, listViewId={list_view_id}")

    all_detail_types = _fetch_family_types_detail(base_url)
    column_list_types = [t for t in all_detail_types if "柱リスト" in str(t.get("familyName") or "")]
    if not column_list_types:
        print(json.dumps({"ok": False, "msg": "柱リスト用ファミリタイプ（詳細項目）が見つかりません。"}, ensure_ascii=False, indent=2))
        return 1

    mode = _pick_mode_from_families(column_list_types, LIST_MODE)
    use_types = [t for t in column_list_types if _matches_mode(str(t.get("familyName") or ""), mode)]
    if not use_types:
        print(json.dumps({"ok": False, "msg": f"mode={mode} に一致する柱リスト用タイプがありません。"}, ensure_ascii=False, indent=2))
        return 1
    _log(f"[INFO] listMode={mode}, listTypes={len(use_types)}")

    type_by_key_zone: Dict[Tuple[str, str, str], Dict[str, Any]] = {}
    blank_type = None
    type_value_cache: Dict[int, Dict[str, Any]] = {}
    parse_fail_examples: List[Dict[str, Any]] = []
    type_param_errors: List[Dict[str, Any]] = []
    list_type_records: List[Dict[str, Any]] = []
    t0_types = time.time()
    _log("[INFO] scanning list type parameters...")
    for t in use_types:
        tid = int(t.get("typeId") or t.get("id") or 0)
        if tid <= 0:
            continue
        fam = str(t.get("familyName") or "")
        tname = str(t.get("typeName") or t.get("name") or "")
        vals: Dict[str, Any] = {}
        parsed_fast = _parse_list_type_name(tname)
        if parsed_fast is None:
            try:
                vals = _get_type_params(base_url, tid)
            except Exception as ex:
                vals = {}
                if len(type_param_errors) < 24:
                    type_param_errors.append(
                        {
                            "typeId": tid,
                            "familyName": fam,
                            "typeName": tname,
                            "error": str(ex),
                        }
                    )
        display_tname = str(_pick_value(vals, ["タイプ名", "Type Name"], tname) or tname)
        raw_rec = {
            "typeId": tid,
            "typeName": tname,
            "displayTypeName": display_tname,
            "familyName": fam,
            "typeValues": vals,
        }
        list_type_records.append(raw_rec)
        if "空欄" in fam and blank_type is None:
            blank_type = {"typeId": tid, "typeName": display_tname, "familyName": fam, "typeValues": vals}
        parsed = parsed_fast if parsed_fast is not None else _parse_list_type_from_values(tname, vals)
        if not parsed:
            if len(parse_fail_examples) < 12:
                parse_fail_examples.append(
                    {
                        "typeId": tid,
                        "familyName": fam,
                        "typeName": tname,
                        "param_タイプ名": vals.get("タイプ名"),
                    }
                )
            type_value_cache[tid] = vals
            continue
        rec = {
            "typeId": tid,
            "typeName": display_tname,
            "displayTypeName": display_tname,
            "familyName": fam,
            "levelNorm": parsed["levelNorm"],
            "symbolNorm": parsed["symbolNorm"],
            "zone": parsed["zone"],
            "typeValues": vals,
        }
        k_zone = (parsed["levelNorm"], parsed["symbolNorm"], parsed["zone"])
        prev = type_by_key_zone.get(k_zone)
        if prev is None:
            type_by_key_zone[k_zone] = rec
        else:
            # 同キー重複時の優先順位（特に SRC 同上）
            use_new = False
            if mode == "src" and parsed["zone"] == "同上":
                prev_ok = _is_preferred_src_doujou_family_name(str(prev.get("familyName") or ""))
                new_ok = _is_preferred_src_doujou_family_name(str(rec.get("familyName") or ""))
                if new_ok and (not prev_ok):
                    use_new = True
            elif (not prev.get("typeValues")) and rec.get("typeValues"):
                use_new = True
            if use_new:
                type_by_key_zone[k_zone] = rec
        type_value_cache[tid] = vals
        if len(list_type_records) % 50 == 0:
            _log(
                f"[INFO] listType scan progress: scanned={len(list_type_records)} parsed={len(type_by_key_zone)} elapsed={round(time.time()-t0_types,1)}s"
            )
    _log(
        f"[INFO] listType scan done: scanned={len(list_type_records)} parsed={len(type_by_key_zone)} elapsed={round(time.time()-t0_types,1)}s"
    )

    cols = _fetch_structural_columns(base_url, ref_view_id)
    if not cols:
        print(json.dumps({"ok": False, "msg": "構造柱を取得できませんでした。参照ビューを確認してください。"}, ensure_ascii=False, indent=2))
        return 1
    _log(f"[INFO] structuralColumns={len(cols)}")

    _log("[INFO] fetching levels...")
    try:
        levels_mm = _fetch_levels(base_url)
    except Exception as ex:
        levels_mm = {}
        _log(f"[WARN] level fetch failed: {ex}")
    _log(f"[INFO] levels fetched: {len(levels_mm)}")
    type_split_cache: Dict[int, bool] = {}
    type_symbol_cache: Dict[int, str] = {}

    cell_seed: Dict[Tuple[str, str], Dict[str, Any]] = {}
    filtered_non_rcsrc = 0
    unique_type_fetch = 0
    for i_col, c in enumerate(cols, 1):
        tid = int(c.get("typeId") or 0)
        if tid <= 0:
            continue
        level_raw = str(c.get("level") or c.get("levelName") or "").strip()
        if not level_raw:
            continue
        level_norm = _norm_level(level_raw)

        if tid not in type_value_cache:
            unique_type_fetch += 1
            _log(f"[INFO] fetch column type params: tid={tid} uniqueFetch={unique_type_fetch}")
            try:
                type_value_cache[tid] = _get_type_params(base_url, tid)
            except Exception as ex:
                type_value_cache[tid] = {}
                if len(type_param_errors) < 48:
                    type_param_errors.append(
                        {
                            "typeId": tid,
                            "familyName": str(c.get("familyName") or ""),
                            "typeName": str(c.get("typeName") or ""),
                            "error": str(ex),
                            "stage": "column_type_cache",
                        }
                    )
        tvals = type_value_cache[tid]

        if not _is_target_rc_src_column(c, tvals):
            filtered_non_rcsrc += 1
            continue

        if tid not in type_symbol_cache:
            type_symbol_cache[tid] = _extract_symbol_from_column(c, tvals)
        symbol_raw = str(type_symbol_cache[tid] or "").strip()
        if not symbol_raw:
            continue
        symbol_norm = _norm_symbol(symbol_raw)

        if tid not in type_split_cache:
            type_split_cache[tid] = _has_top_bottom_split(tvals)
        split = type_split_cache[tid]

        k = (level_norm, symbol_norm)
        if k not in cell_seed:
            cell_seed[k] = {
                "levelNorm": level_norm,
                "levelRaw": level_raw,
                "symbolNorm": symbol_norm,
                "symbolRaw": symbol_raw,
                "split": bool(split),
                "elevMm": levels_mm.get(level_norm, 0.0),
                "srcTypeId": tid,
                "srcFamilyName": str(c.get("familyName") or ""),
                "srcTypeValues": tvals,
            }
        else:
            # どちらかが split なら split 扱い
            cell_seed[k]["split"] = bool(cell_seed[k]["split"] or split)
        if i_col % 100 == 0:
            _log(
                f"[INFO] column scan progress: {i_col}/{len(cols)} cellSeeds={len(cell_seed)} typeCache={len(type_value_cache)}"
            )
    _log(f"[INFO] filtered non RC/SRC columns: {filtered_non_rcsrc}")

    if not cell_seed:
        print(json.dumps({"ok": False, "msg": "符号付きの構造柱が取得できませんでした。"}, ensure_ascii=False, indent=2))
        return 1

    # Dynamo移植: 必要な(レベル,符号,区分)タイプが無ければ自動作成
    type_create_summary = {"created": [], "errors": []}
    if AUTO_CREATE_MISSING_TYPES:
        type_create_summary = _ensure_missing_types_from_columns(
            base_url=base_url,
            mode=mode,
            list_type_records=list_type_records,
            type_by_key_zone=type_by_key_zone,
            cell_seed=cell_seed,
        )

    if not type_by_key_zone:
        print(
            json.dumps(
                {
                    "ok": False,
                    "msg": "レベル_符号_区分 を持つ柱リストタイプが見つかりません。",
                    "hint": "テンプレートとなる柱リストタイプ（RC/SRC, 円柱/矩形, 同上/空欄）のロードを確認してください。",
                    "mode": mode,
                    "candidatesSample": parse_fail_examples,
                    "typeCreate": type_create_summary,
                },
                ensure_ascii=False,
                indent=2,
            )
        )
        return 1

    type_family_summary: Dict[str, int] = {}
    doujou_family_summary: Dict[str, int] = {}
    for (lv, sym, zone), rec in type_by_key_zone.items():
        fam = str(rec.get("familyName") or "")
        k = f"{zone} | {fam}"
        type_family_summary[k] = int(type_family_summary.get(k, 0)) + 1
        if zone == "同上":
            doujou_family_summary[fam] = int(doujou_family_summary.get(fam, 0)) + 1

    symbols_sorted = sorted({v["symbolNorm"] for v in cell_seed.values()}, key=_sym_sort_key)
    symbol_raw_by_norm = {v["symbolNorm"]: v["symbolRaw"] for v in cell_seed.values()}

    level_items = {}
    for v in cell_seed.values():
        ln = v["levelNorm"]
        if ln not in level_items:
            level_items[ln] = {"levelNorm": ln, "levelRaw": v["levelRaw"], "elevMm": float(v.get("elevMm") or 0.0)}
    levels_sorted = sorted(level_items.values(), key=lambda x: x["elevMm"], reverse=True)

    rows: List[Dict[str, Any]] = []
    for lv in levels_sorted:
        rows.append({"levelNorm": lv["levelNorm"], "levelRaw": lv["levelRaw"], "rowKind": "upper", "symbolRawByNorm": symbol_raw_by_norm})
        rows.append({"levelNorm": lv["levelNorm"], "levelRaw": lv["levelRaw"], "rowKind": "lower", "symbolRawByNorm": symbol_raw_by_norm})

    n_rc_src = 2 if mode == "src" else 1
    cells, box_w, box_h, page_map = _build_layout_cells(
        symbols_sorted=symbols_sorted,
        rows=rows,
        type_by_key_zone=type_by_key_zone,
        blank_type=blank_type,
        n_rc_src=n_rc_src,
        bikoran=BIKORAN,
    )

    # typeパラメータ: 鉄筋表示倍率
    if SET_TEKKIN_BAIRITSU:
        touched_type_ids = {cells[ix][iy]["type"]["typeId"] for ix in range(len(symbols_sorted)) for iy in range(len(rows)) if cells[ix][iy].get("type")}
        items = []
        for tid in touched_type_ids:
            items.append({"typeId": int(tid), "target": "type", "paramName": "鉄筋表示倍率", "value": TEKKIN_BAIRITSU})
        _update_parameters_batch(base_url, items)

    # インスタンス生成
    created = []
    errors = []
    t0_create = time.time()
    cum_x = [0.0]
    for w in box_w[:-1]:
        cum_x.append(cum_x[-1] + w)
    cum_y = [0.0]
    for h in box_h[:-1]:
        cum_y.append(cum_y[-1] + h)

    nx = len(symbols_sorted)
    ny = len(rows)
    for ix in range(nx):
        for iy in range(ny):
            cell = cells[ix][iy]
            t = cell.get("type")
            if not t:
                continue
            tid = int(t.get("typeId") or 0)
            if tid <= 0:
                continue

            page = int(page_map[ix][iy]) if BUNKATSU else 0
            x_mm = BASE_X_MM + cum_x[ix]
            y_mm = BASE_Y_MM - cum_y[iy] - page * (Y_SIZE_MM + PAGE_GAP_Y_MM)
            z_mm = BASE_Z_MM

            c = None
            first_error = None
            try:
                c = rpc(
                    base_url,
                    "element.create_family_instance",
                    {
                        "typeId": tid,
                        "location": {"x": x_mm, "y": y_mm, "z": z_mm},
                        "viewId": int(list_view_id),
                    },
                )
            except Exception as ex:
                first_error = ex

            # 旧版Addin向けフォールバック（viewId未対応）
            if c is None:
                try:
                    c = rpc(
                        base_url,
                        "element.create_family_instance",
                        {
                            "typeId": tid,
                            "location": {"x": x_mm, "y": y_mm, "z": z_mm},
                        },
                    )
                except Exception as ex2:
                    errors.append(
                        {
                            "ix": ix,
                            "iy": iy,
                            "typeId": tid,
                            "typeName": t.get("typeName"),
                            "msg": f"create failed (with viewId: {first_error}, without viewId: {ex2})",
                        }
                    )
                    continue

            eid = int(c.get("elementId") or 0)
            if eid <= 0:
                errors.append(
                    {
                        "ix": ix,
                        "iy": iy,
                        "typeId": tid,
                        "typeName": t.get("typeName"),
                        "msg": c.get("msg", "create_family_instance returned no elementId"),
                    }
                )
                continue

            left_col = 1 if (ix == 0 or page_map[ix - 1][iy] != page) else 0
            symbol_show = 1 if (iy == 0 or page_map[ix][iy - 1] != page) else 0

            inst_updates: Dict[str, Any] = {
                "枠W": box_w[ix],
                "枠H": box_h[iy],
                "左欄": left_col,
                "階表示": left_col,
                "符号表示": symbol_show,
            }
            if BIKORAN:
                inst_updates["備考表示"] = 1

            # コメントタグ（任意 cleanup 用）
            inst_updates["コメント"] = COMMENT_TAG

            # 空欄タイプの補助パラメータ
            if "空欄" in str(t.get("familyName") or ""):
                inst_updates["符号"] = cell.get("symbolRaw") or cell.get("symbolNorm")
                inst_updates["レベル名"] = str(cell.get("levelRaw") or "").replace("L", "")
                if cell.get("zone") == "柱頭":
                    inst_updates["柱頭断面"] = 1
                    inst_updates["柱脚断面"] = 0
                elif cell.get("zone") == "柱脚":
                    inst_updates["柱頭断面"] = 0
                    inst_updates["柱脚断面"] = 1
                else:
                    inst_updates["柱頭断面"] = 0
                    inst_updates["柱脚断面"] = 0
            else:
                inst_updates["寸法凡例表示"] = 0

            _set_instance_params_bulk(base_url, eid, inst_updates)

            created.append(
                {
                    "elementId": eid,
                    "typeId": tid,
                    "typeName": t.get("typeName"),
                    "familyName": t.get("familyName"),
                    "ix": ix,
                    "iy": iy,
                    "page": page,
                    "xMm": round(x_mm, 3),
                    "yMm": round(y_mm, 3),
                    "zone": cell.get("zone"),
                }
            )
            if len(created) % 50 == 0:
                _log(
                    f"[INFO] create progress: created={len(created)} errors={len(errors)} elapsed={round(time.time()-t0_create,1)}s"
                )

    out = {
        "ok": len(errors) == 0,
        "mode": mode,
        "refViewId": ref_view_id,
        "listViewId": list_view_id,
        "columns": len(cols),
        "filteredNonRcSrcColumns": filtered_non_rcsrc,
        "symbols": len(symbols_sorted),
        "rows": len(rows),
        "cells": len(symbols_sorted) * len(rows),
        "createdCount": len(created),
        "created": created,
        "errors": errors,
        "typeParamErrors": type_param_errors,
        "typeCreate": type_create_summary,
        "typeFamilySummary": type_family_summary,
        "doujouFamilySummary": doujou_family_summary,
        "note": "Dynamoの『生成・配置・ページ割付』相当。必要に応じて配置パラメータを冒頭定数で調整してください。",
    }
    print(json.dumps(out, ensure_ascii=False, indent=2))
    return 0 if out["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
