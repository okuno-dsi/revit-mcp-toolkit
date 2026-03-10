# @feature: 柱芯線図フル自動（元柱ビュー不要） | keywords: 柱, 通り芯, 寸法, グリッド, シート
# -*- coding: utf-8 -*-
"""
柱芯線図を「現在ビュー」または「レベル伏図」から一括生成する Python Runner 用スクリプト。

主な処理:
1) 対象ビューを解決（SOURCE_VIEW_NAME / SOURCE_LEVEL_NAME / アクティブビュー）
2) 構造柱を収集し、通り芯ビュー + 柱別ビューを作成
3) 通り芯長さを柱中心周りに調整、バブルを非表示化
4) 寸法テンプレート用ビューを自動作成（withDetailing=true）
5) 選択寸法を参照して Y方向の側を維持、X方向寸法を上側へ寄せる
6) 寸法線オフセットを 100mm 単位に丸めて source ビュー寸法を補正
7) apply_column_grid_dimension_standard_to_views を実行して全柱ビューへ寸法展開
8) 必要に応じてシートへ重ね配置（柱 1/100, 通り芯 1/200）

注意:
- Source 側寸法が 0 本の場合、寸法展開フェーズはスキップされます。
- 「柱基準点が通り芯からズレる」場合も、柱↔通り芯寸法（3参照）で反映されます。
"""

import os
import re
import json
import time
import math
import statistics
from typing import Any, Dict, List, Optional, Tuple

try:
    import requests  # type: ignore
except Exception:
    import urllib.request
    import urllib.error

    class _Resp:
        def __init__(self, status_code: int, text: str, reason: str = ""):
            self.status_code = status_code
            self.text = text
            self.reason = reason or ""

        def json(self) -> Any:
            return json.loads(self.text) if self.text else {}

        def raise_for_status(self) -> None:
            if int(self.status_code) >= 400:
                raise RuntimeError(f"HTTP {self.status_code} {self.reason}: {self.text}")

    class _RequestsCompat:
        @staticmethod
        def post(url: str, json: Optional[Dict[str, Any]] = None, timeout: float = 30) -> "_Resp":
            payload = (json or {})
            data = __import__("json").dumps(payload).encode("utf-8")
            req = urllib.request.Request(
                url=url,
                data=data,
                headers={"Content-Type": "application/json"},
                method="POST",
            )
            try:
                with urllib.request.urlopen(req, timeout=timeout) as r:
                    raw = r.read().decode("utf-8", errors="replace")
                    return _Resp(getattr(r, "status", 200), raw, getattr(r, "reason", ""))
            except urllib.error.HTTPError as e:
                raw = (e.read().decode("utf-8", errors="replace") if e.fp else str(e))
                return _Resp(int(e.code), raw, str(e.reason))

        @staticmethod
        def get(url: str, timeout: float = 30) -> "_Resp":
            req = urllib.request.Request(url=url, method="GET")
            try:
                with urllib.request.urlopen(req, timeout=timeout) as r:
                    raw = r.read().decode("utf-8", errors="replace")
                    return _Resp(getattr(r, "status", 200), raw, getattr(r, "reason", ""))
            except urllib.error.HTTPError as e:
                raw = (e.read().decode("utf-8", errors="replace") if e.fp else str(e))
                return _Resp(int(e.code), raw, str(e.reason))

    requests = _RequestsCompat()  # type: ignore


# --------------------------
# User editable parameters
# --------------------------
PORT = int(os.environ.get("REVIT_MCP_PORT", "5210"))

# ソースビュー解決
SOURCE_VIEW_NAME = ""             # 指定名優先。空なら SOURCE_LEVEL_NAME / アクティブビュー
SOURCE_LEVEL_NAME = ""            # 例: "3FL"
USE_ACTIVE_VIEW_IF_NOT_FOUND = True

# ビューテンプレート（空なら apply しない）
GRID_TEMPLATE_NAME = "通り心だけ"
COLUMN_TEMPLATE_NAME = "柱芯線図用ビュー"
COLUMN_VIEW_TYPE_NAME = "柱芯線図"
GRID_BASE_USE_COLUMN_VIEW_TYPE = True  # True: GRID_BASEにも COLUMN_VIEW_TYPE_NAME を適用

# 柱ビュー設定
COLUMN_MARGIN_MM = 100.0
COLUMN_SCALE = 100
GRID_SCALE = 200
GRID_HALF_LENGTH_MM = 1000.0
HIDE_GRID_BUBBLES = True
PLACE_STRUCTURAL_COLUMN_TAG = True
TAG_OFFSET_RIGHT_MM = 100.0
TAG_OFFSET_UP_MM = 150.0
TAG_ADD_LEADER = False
PREFERRED_COLUMN_TAG_TYPE_NAME = "符号"

# 寸法標準（Runbook既定）
DIM_FACE_TO_GRID_OFFSET_MM = 600.0
DIM_OUTLINE_OFFSET_MM = 1000.0
DIM_CENTER_TO_GRID_OFFSET_MM = 600.0
DIM_CENTER_ZERO_TOLERANCE_MM = 1.0

# 寸法展開設定
APPLY_DIMENSION_STANDARD = True
FORCE_AXIS_X_SIDE = "top"         # X方向ズレ寸法は柱の上側
FORCE_AXIS_Y_SIDE = ""            # "" なら既存（選択寸法/多数決）維持
OFFSET_ROUND_MM = 100.0           # 寸法線オフセット丸め単位
OFFSET_MIN_MM = 100.0             # 丸め後の最小絶対値

# シート配置
CREATE_SHEET = True
SHEET_NUMBER_PREFIX = "CGA-"
SHEET_NAME = "柱芯線図_重ね"
NO_TITLEBLOCK = True

# 安全ガード
MAX_ALLOWED_COLUMNS = 200
MIN_REQUIRED_COLUMNS = 1
BATCH_SIZE = 0                   # 0: 全件, >0: バッチ件数（例: 40）
BATCH_INDEX = 0                  # 0-based（BATCH_SIZE>0 のとき有効）


OST_STRUCTURAL_COLUMNS = -2001330
OST_GRIDS = -2000220

DEFAULT_TIMEOUT = 30
POLL_INTERVAL = 0.3
POLL_TIMEOUT = 180


def detect_rpc_endpoint(base_url: str) -> str:
    for ep in ("/rpc", "/jsonrpc"):
        url = f"{base_url}{ep}"
        try:
            requests.post(url, json={"jsonrpc": "2.0", "id": "ping", "method": "noop"}, timeout=3)
            return url
        except Exception:
            continue
    return f"{base_url}/rpc"


def unwrap_result(obj: Any) -> Any:
    if not isinstance(obj, dict):
        return obj
    if "result" in obj and isinstance(obj["result"], dict):
        inner = obj["result"]
        if "result" in inner and isinstance(inner["result"], dict):
            return inner["result"]
        return inner
    return obj


def poll_job(base_url: str, job_id: str, timeout_sec: float = POLL_TIMEOUT) -> Dict[str, Any]:
    deadline = time.time() + timeout_sec
    job_url = f"{base_url}/job/{job_id}"
    while time.time() < deadline:
        try:
            r = requests.get(job_url, timeout=10)
            if r.status_code in (202, 204):
                time.sleep(POLL_INTERVAL)
                continue
            r.raise_for_status()
            job = r.json()
        except Exception:
            time.sleep(POLL_INTERVAL)
            continue

        state = (job.get("state") or "").upper()
        if state == "SUCCEEDED":
            result_json = job.get("result_json")
            if result_json:
                try:
                    return unwrap_result(json.loads(result_json))
                except Exception:
                    return {"ok": True, "raw": result_json}
            return {"ok": True}
        if state in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(job.get("error_msg") or state)
        time.sleep(POLL_INTERVAL)
    raise TimeoutError(f"job polling timed out (jobId={job_id})")


class RpcClient:
    def __init__(self, port: int):
        self.base_url = f"http://127.0.0.1:{port}"
        self.endpoint = detect_rpc_endpoint(self.base_url)

    def call(self, method: str, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        payload = {
            "jsonrpc": "2.0",
            "id": f"req-{int(time.time() * 1000)}",
            "method": method,
            "params": params or {},
        }
        r = requests.post(self.endpoint, json=payload, timeout=DEFAULT_TIMEOUT)
        r.raise_for_status()
        data = r.json()
        if "error" in data:
            raise RuntimeError(data["error"])
        result = data.get("result", {})
        if isinstance(result, dict) and result.get("queued"):
            job_id = result.get("jobId") or result.get("job_id")
            if not job_id:
                raise RuntimeError("queued but jobId missing")
            return poll_job(self.base_url, job_id)
        return unwrap_result(result)

    def call_any(self, methods: List[str], params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        last_err = None
        for m in methods:
            try:
                return self.call(m, params)
            except Exception as ex:
                last_err = ex
        raise RuntimeError(str(last_err) if last_err else "all methods failed")


def get_list(data: Dict[str, Any], keys: List[str]) -> List[Dict[str, Any]]:
    for k in keys:
        v = data.get(k)
        if isinstance(v, list):
            return v
    return []


def to_float(x: Any) -> Optional[float]:
    try:
        return float(x)
    except Exception:
        return None


def round_step_abs(value_mm: float, step_mm: float, min_mm: float) -> float:
    if step_mm <= 0:
        return max(abs(value_mm), min_mm)
    v = abs(value_mm)
    r = round(v / step_mm) * step_mm
    return max(r, min_mm)


def parse_column_id_from_view_name(view_name: str) -> Optional[int]:
    m = re.search(r"_COL_(\d+)", view_name or "")
    if not m:
        return None
    try:
        return int(m.group(1))
    except Exception:
        return None


def sanitize_view_name_token(name: str) -> str:
    s = str(name or "").strip()
    if not s:
        return "LEVEL"
    s = re.sub(r"[\\/:{}\[\]\|;<>?`~\"]", "_", s)
    s = re.sub(r"\s+", "_", s).strip("_")
    return s or "LEVEL"


def get_element_level_name(rpc: RpcClient, element_id: int) -> Optional[str]:
    if int(element_id or 0) <= 0:
        return None
    try:
        res = rpc.call_any(
            ["element.get_element_info", "get_element_info"],
            {"elementIds": [int(element_id)]},
        )
        rows = get_list(res, ["elements", "items", "rows"])
        if not rows:
            return None
        row = rows[0] or {}
        level_name = str(row.get("level") or row.get("levelName") or "").strip()
        return level_name or None
    except Exception:
        return None


def resolve_source_level_label(rpc: RpcClient, source_view_name: str, source_column_id: int) -> str:
    if SOURCE_LEVEL_NAME.strip():
        return sanitize_view_name_token(SOURCE_LEVEL_NAME.strip())
    lv = get_element_level_name(rpc, source_column_id)
    if lv:
        return sanitize_view_name_token(lv)
    return sanitize_view_name_token(source_view_name or "LEVEL")


def get_active_view_context(rpc: RpcClient) -> Dict[str, Any]:
    ctx = rpc.call("help.get_context", {"includeSelectionIds": True, "maxSelectionIds": 200})
    d = (ctx.get("data") if isinstance(ctx, dict) else None) or {}
    return {
        "viewId": int(d.get("activeViewId") or 0),
        "viewName": str(d.get("activeViewName") or ""),
        "docTitle": str(d.get("docTitle") or ""),
        "selectionIds": list(d.get("selectionIds") or []),
    }


def resolve_view_by_name_or_level_or_active(rpc: RpcClient) -> Dict[str, Any]:
    if SOURCE_VIEW_NAME.strip():
        resp = rpc.call_any(["view.get_views", "get_views"], {})
        views = get_list(resp, ["views", "items"])
        for v in views:
            if (v.get("name") or "").strip().lower() == SOURCE_VIEW_NAME.strip().lower():
                return {"viewId": int(v.get("viewId") or v.get("id") or 0), "name": v.get("name") or SOURCE_VIEW_NAME}

    if SOURCE_LEVEL_NAME.strip():
        # レベル名を含む平面ビューを優先
        resp = rpc.call_any(["view.get_views", "get_views"], {})
        views = get_list(resp, ["views", "items"])
        level_l = SOURCE_LEVEL_NAME.strip().lower()
        cands: List[Dict[str, Any]] = []
        for v in views:
            name = str(v.get("name") or "")
            vtype = str(v.get("viewType") or v.get("type") or "")
            if level_l in name.lower() and ("plan" in vtype.lower() or "engineeringplan" in vtype.lower()):
                cands.append(v)
        if cands:
            cands.sort(key=lambda x: str(x.get("name") or ""))
            vv = cands[0]
            return {"viewId": int(vv.get("viewId") or vv.get("id") or 0), "name": vv.get("name") or ""}

    if USE_ACTIVE_VIEW_IF_NOT_FOUND:
        c = get_active_view_context(rpc)
        if c["viewId"] > 0:
            return {"viewId": c["viewId"], "name": c["viewName"], "fromActive": True}
    raise RuntimeError("対象ビューを解決できません。SOURCE_VIEW_NAME / SOURCE_LEVEL_NAME / ActiveView を確認してください。")


def template_exists(rpc: RpcClient, template_name: str) -> bool:
    if not template_name.strip():
        return False
    resp = rpc.call_any(["view.get_views", "get_views"], {"includeTemplates": True})
    views = get_list(resp, ["views", "items"])
    for v in views:
        if (v.get("name") or "").strip().lower() == template_name.strip().lower() and bool(v.get("isTemplate")):
            return True
    return False


def try_apply_template(rpc: RpcClient, view_id: int, template_name: str) -> Dict[str, Any]:
    if not template_name.strip():
        return {"ok": False, "skipped": True, "msg": "template name empty"}
    return rpc.call_any(
        ["view.set_view_template", "set_view_template"],
        {"viewId": int(view_id), "templateName": template_name},
    )


def try_set_view_type_by_name(rpc: RpcClient, view_id: int, view_type_name: str) -> Dict[str, Any]:
    if not str(view_type_name or "").strip():
        return {"ok": False, "skipped": True, "msg": "view type name empty"}
    try:
        return rpc.call_any(
            ["view.set_view_type", "set_view_type"],
            {"viewId": int(view_id), "newViewTypeName": str(view_type_name).strip()},
        )
    except Exception as ex:
        return {"ok": False, "msg": str(ex)}


def collect_columns_in_view(rpc: RpcClient, view_id: int) -> List[int]:
    resp = rpc.call_any(
        ["element.get_elements_in_view", "get_elements_in_view"],
        {"viewId": int(view_id), "categoryIds": [OST_STRUCTURAL_COLUMNS]},
    )
    rows = get_list(resp, ["rows", "items", "elements"])
    ids: List[int] = []
    for r in rows:
        eid = r.get("elementId") or r.get("id")
        if isinstance(eid, int):
            ids.append(eid)
    return sorted(set(ids))


def collect_grids(rpc: RpcClient) -> List[Dict[str, Any]]:
    resp = rpc.call_any(["element.get_grids", "get_grids"], {})
    grids = get_list(resp, ["grids", "items"])
    out = []
    for g in grids:
        s = g.get("start") or {}
        e = g.get("end") or {}
        name = g.get("name")
        gid = g.get("elementId") or g.get("gridId") or g.get("id")
        if not name or not isinstance(gid, int):
            continue
        sx = to_float(s.get("x"))
        sy = to_float(s.get("y"))
        ex = to_float(e.get("x"))
        ey = to_float(e.get("y"))
        if None in (sx, sy, ex, ey):
            continue
        out.append({"id": gid, "name": name, "sx": sx, "sy": sy, "ex": ex, "ey": ey})
    return out


def get_column_center_mm(rpc: RpcClient, element_id: int) -> Tuple[float, float]:
    bb = rpc.call_any(
        ["element.get_bounding_box", "get_bounding_box"],
        {"elementId": int(element_id)},
    )
    boxes = bb.get("boxes") or []
    if not boxes:
        return 0.0, 0.0
    row = boxes[0]
    if not row.get("ok"):
        return 0.0, 0.0
    b = row.get("boundingBox") or {}
    mn = b.get("min") or {}
    mx = b.get("max") or {}
    cx = (float(mn.get("x")) + float(mx.get("x"))) * 0.5
    cy = (float(mn.get("y")) + float(mx.get("y"))) * 0.5
    return cx, cy


def line_intersection_2d(a: Dict[str, Any], b: Dict[str, Any]) -> Optional[Tuple[float, float]]:
    x1, y1, x2, y2 = a["sx"], a["sy"], a["ex"], a["ey"]
    x3, y3, x4, y4 = b["sx"], b["sy"], b["ex"], b["ey"]
    den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4)
    if abs(den) < 1e-9:
        return None
    px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / den
    py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / den
    return px, py


def choose_nearest_grid_pair(grids: List[Dict[str, Any]], cx: float, cy: float) -> Optional[Tuple[str, str, float, float]]:
    best = None
    best_d = None
    for i in range(len(grids)):
        for j in range(i + 1, len(grids)):
            ip = line_intersection_2d(grids[i], grids[j])
            if ip is None:
                continue
            d = math.hypot(ip[0] - cx, ip[1] - cy)
            if best is None or d < best_d:
                best = (grids[i]["name"], grids[j]["name"], ip[0], ip[1])
                best_d = d
    return best


def set_view_scale(rpc: RpcClient, view_id: int, scale: int) -> Dict[str, Any]:
    attempts = [
        {"viewId": view_id, "paramName": "ビュー スケール", "value": int(scale), "detachViewTemplate": True},
        {"viewId": view_id, "paramName": "View Scale", "value": int(scale), "detachViewTemplate": True},
    ]
    last = {"ok": False, "msg": "set_view_scale not attempted"}
    for p in attempts:
        r = rpc.call_any(["view.set_view_parameter", "set_view_parameter"], p)
        if isinstance(r, dict) and r.get("ok"):
            return r
        if isinstance(r, dict):
            last = r
    return last


def set_crop_region_visibility_off(rpc: RpcClient, view_id: int) -> Dict[str, Any]:
    tries = [
        {"viewId": int(view_id), "paramName": "トリミング領域を表示", "value": False, "detachViewTemplate": True},
        {"viewId": int(view_id), "paramName": "Crop Region Visible", "value": False, "detachViewTemplate": True},
    ]
    last = {"ok": False, "msg": "crop visibility param not found"}
    for t in tries:
        r = rpc.call_any(["view.set_view_parameter", "set_view_parameter"], t)
        if isinstance(r, dict) and r.get("ok"):
            return r
        if isinstance(r, dict):
            last = r
    return last


def keep_only_categories(rpc: RpcClient, view_id: int, category_ids: List[int]) -> Dict[str, Any]:
    return rpc.call_any(
        ["view.set_category_visibility_bulk", "set_category_visibility_bulk"],
        {
            "viewId": int(view_id),
            "mode": "keep_only",
            "categoryType": "All",
            "keepCategoryIds": category_ids,
            "detachViewTemplate": True,
        },
    )


def crop_plan_to_element(rpc: RpcClient, view_id: int, element_id: int, margin_mm: float) -> Dict[str, Any]:
    return rpc.call_any(
        ["view.crop_plan_view_to_element", "crop_plan_view_to_element"],
        {
            "viewId": int(view_id),
            "elementId": int(element_id),
            "marginMm": float(margin_mm),
            "cropActive": True,
            "cropVisible": True,
        },
    )


def set_grid_segments_around_column(rpc: RpcClient, view_id: int, element_id: int, half_length_mm: float) -> Dict[str, Any]:
    return rpc.call_any(
        ["element.set_grid_segments_around_element_in_view", "set_grid_segments_around_element_in_view"],
        {
            "viewId": int(view_id),
            "elementId": int(element_id),
            "halfLengthMm": float(half_length_mm),
            "detachViewTemplate": True,
        },
    )


def set_grid_bubbles_hidden(rpc: RpcClient, view_id: int) -> Dict[str, Any]:
    return rpc.call_any(
        ["element.set_grid_bubbles_visibility", "set_grid_bubbles_visibility"],
        {
            "viewId": int(view_id),
            "bothVisible": False,
            "detachViewTemplate": True,
        },
    )


def get_column_bbox_mm(rpc: RpcClient, element_id: int) -> Optional[Dict[str, float]]:
    bb = rpc.call_any(
        ["element.get_bounding_box", "get_bounding_box"],
        {"elementId": int(element_id)},
    )
    boxes = bb.get("boxes") or []
    if not boxes:
        return None
    row = boxes[0]
    if not row.get("ok"):
        return None
    bbox = row.get("boundingBox") or {}
    mn = bbox.get("min") or {}
    mx = bbox.get("max") or {}
    vals = {
        "minX": to_float(mn.get("x")),
        "minY": to_float(mn.get("y")),
        "minZ": to_float(mn.get("z")),
        "maxX": to_float(mx.get("x")),
        "maxY": to_float(mx.get("y")),
        "maxZ": to_float(mx.get("z")),
    }
    if any(v is None for v in vals.values()):
        return None
    return {k: float(v) for k, v in vals.items()}


def get_tag_symbol_types(rpc: RpcClient, params: Dict[str, Any]) -> List[Dict[str, Any]]:
    res = rpc.call_any(["view.get_tag_symbols", "get_tag_symbols"], params)
    return get_list(res, ["types", "items"])


def resolve_structural_column_tag_type(rpc: RpcClient) -> Optional[Dict[str, Any]]:
    queries: List[Dict[str, Any]] = [
        {"categoryNames": ["構造柱タグ", "Structural Column Tags"], "count": 300},
        {"nameContains": "構造柱", "count": 500},
        {"nameContains": "柱", "count": 500},
        {"nameContains": "Structural Column", "count": 500},
        {"nameContains": "Column", "count": 500},
    ]
    merged: Dict[int, Dict[str, Any]] = {}
    for q in queries:
        try:
            for t in get_tag_symbol_types(rpc, q):
                tid = int(t.get("typeId") or 0)
                if tid > 0 and tid not in merged:
                    merged[tid] = t
        except Exception:
            continue

    if not merged:
        try:
            for t in get_tag_symbol_types(rpc, {"count": 800}):
                tid = int(t.get("typeId") or 0)
                if tid > 0 and tid not in merged:
                    merged[tid] = t
        except Exception:
            pass

    if not merged:
        return None

    def score(t: Dict[str, Any]) -> int:
        cat = str(t.get("categoryName") or "").lower()
        fam = str(t.get("familyName") or "").lower()
        typ = str(t.get("typeName") or "").lower()
        txt = f"{fam} {typ}"
        s = 0
        if ("tag" in cat) or ("タグ" in cat):
            s += 10
        if ("column" in cat) or ("柱" in cat):
            s += 14
        if ("structural" in cat) or ("構造" in cat):
            s += 8
        if ("column" in txt) or ("柱" in txt):
            s += 5
        if ("structural" in txt) or ("構造" in txt):
            s += 2
        return s

    preferred = []
    pref = (PREFERRED_COLUMN_TAG_TYPE_NAME or "").strip().lower()
    if pref:
        for t in merged.values():
            typ = str(t.get("typeName") or "").strip().lower()
            if pref == typ:
                preferred.append(t)

    pool = preferred if preferred else list(merged.values())
    best = max(pool, key=score)
    if score(best) <= 0:
        return None
    return best


def delete_existing_tags_for_host_in_view(rpc: RpcClient, view_id: int, host_element_id: int) -> Dict[str, Any]:
    try:
        res = rpc.call_any(["view.get_tags_in_view", "get_tags_in_view"], {"viewId": int(view_id), "count": 1200})
    except Exception as ex:
        return {"ok": False, "msg": str(ex), "deletedCount": 0}

    tags = get_list(res, ["tags", "items"])
    target_tag_ids: List[int] = []
    for t in tags:
        hid = int(t.get("hostElementId") or 0)
        tid = int(t.get("tagId") or 0)
        if hid == int(host_element_id) and tid > 0:
            target_tag_ids.append(tid)

    deleted = 0
    errors: List[Dict[str, Any]] = []
    for tid in target_tag_ids:
        try:
            rr = rpc.call_any(["view.delete_tag", "delete_tag"], {"tagId": int(tid)})
            if rr.get("ok"):
                deleted += 1
            else:
                errors.append({"tagId": tid, "msg": rr.get("msg")})
        except Exception as ex:
            errors.append({"tagId": tid, "msg": str(ex)})
    return {"ok": len(errors) == 0, "deletedCount": deleted, "errors": errors}


def place_structural_column_tag_top_right(
    rpc: RpcClient,
    view_id: int,
    host_element_id: int,
    tag_type_id: int,
    dx_mm: float,
    dy_mm: float,
    add_leader: bool,
) -> Dict[str, Any]:
    if tag_type_id <= 0:
        return {"ok": False, "msg": "構造柱タグ typeId を解決できません。"}

    bbox = get_column_bbox_mm(rpc, host_element_id)
    if not bbox:
        return {"ok": False, "msg": "柱BoundingBoxを取得できません。"}

    deleted = delete_existing_tags_for_host_in_view(rpc, view_id, host_element_id)
    loc = {
        "x": round(bbox["maxX"] + float(dx_mm), 3),
        "y": round(bbox["maxY"] + float(dy_mm), 3),
        "z": round(bbox["maxZ"], 3),
    }
    created = rpc.call_any(
        ["view.create_tag", "create_tag"],
        {
            "viewId": int(view_id),
            "hostElementId": int(host_element_id),
            "typeId": int(tag_type_id),
            "location": loc,
            "addLeader": bool(add_leader),
            "orientation": "Horizontal",
        },
    )
    return {
        "ok": bool(created.get("ok")),
        "tagId": created.get("tagId"),
        "typeId": int(tag_type_id),
        "locationMm": loc,
        "deletedExistingTags": deleted,
        "raw": created,
    }


def duplicate_view(rpc: RpcClient, source_view_id: int, desired_name: str, with_detailing: bool) -> Dict[str, Any]:
    return rpc.call_any(
        ["view.duplicate_view", "duplicate_view"],
        {
            "viewId": int(source_view_id),
            "withDetailing": bool(with_detailing),
            "desiredName": desired_name,
            "onNameConflict": "increment",
        },
    )


def create_sheet_unique(rpc: RpcClient, sheet_no_prefix: str, sheet_name: str, no_titleblock: bool) -> Dict[str, Any]:
    run_stamp = time.strftime("%m%d%H%M%S")
    base_no = f"{sheet_no_prefix}{run_stamp}"
    for i in range(0, 300):
        no_try = base_no if i == 0 else f"{base_no}-{i:03d}"
        nm_try = sheet_name if i == 0 else f"{sheet_name}_{i:03d}"
        r = rpc.call_any(
            ["sheet.create", "create_sheet"],
            {"sheetNumber": no_try, "sheetName": nm_try, "noTitleBlock": bool(no_titleblock)},
        )
        if isinstance(r, dict) and r.get("sheetId"):
            return r
    return {"ok": False, "msg": "sheet.create retry exhausted"}


def grid_axis_by_orientation(grid_row: Dict[str, Any]) -> str:
    dx = float(grid_row["ex"] - grid_row["sx"])
    dy = float(grid_row["ey"] - grid_row["sy"])
    # 垂直グリッド(X一定) -> X方向ズレ寸法
    return "X" if abs(dy) >= abs(dx) else "Y"


def infer_dim_side(axis: str, origin_x: float, origin_y: float, cx: float, cy: float) -> str:
    if axis == "X":
        return "top" if origin_y >= cy else "bottom"
    return "right" if origin_x >= cx else "left"


def choose_source_column_id(
    source_view_name: str,
    source_view_id: int,
    column_ids: List[int],
    selected_ids: List[int],
    dims_in_source: List[Dict[str, Any]],
    grids_map: Dict[int, Dict[str, Any]],
) -> Tuple[int, Optional[str], Optional[str]]:
    # 1) view name から推定
    c_from_name = parse_column_id_from_view_name(source_view_name)
    if c_from_name and c_from_name in column_ids:
        return c_from_name, None, None

    # 2) 選択寸法から推定
    if selected_ids:
        selected_set = set(int(x) for x in selected_ids if isinstance(x, int))
        for d in dims_in_source:
            did = int(d.get("elementId") or 0)
            if did not in selected_set:
                continue
            refs = [int(r) for r in (d.get("references") or []) if isinstance(r, int)]
            cols = [r for r in refs if r in column_ids]
            if not cols:
                continue
            grid_id = next((r for r in refs if r in grids_map), None)
            axis = grid_axis_by_orientation(grids_map[grid_id]) if grid_id else None
            o = d.get("origin") or {}
            ox = to_float(o.get("x")) or 0.0
            oy = to_float(o.get("y")) or 0.0
            # 側判定は後段で中心点が必要。ここでは axis だけ返し、side は後段再計算。
            return cols[0], axis, None

    # 3) 先頭柱
    if column_ids:
        return int(column_ids[0]), None, None
    raise RuntimeError("source column id を解決できませんでした。")


def adjust_template_dimensions(
    rpc: RpcClient,
    source_template_view_id: int,
    source_column_id: int,
    grids_map: Dict[int, Dict[str, Any]],
    selected_dim_id: Optional[int],
    force_x_side: str,
    force_y_side: str,
    round_mm: float,
    min_mm: float,
) -> Dict[str, Any]:
    dims_res = rpc.call_any(
        ["view.get_dimensions_in_view", "get_dimensions_in_view"],
        {"viewId": int(source_template_view_id), "includeOrigin": True, "includeReferences": True, "includeValue": True},
    )
    dims = get_list(dims_res, ["items", "dimensions"])
    if not dims:
        return {"ok": False, "msg": "source template view has no dimensions.", "moved": 0}

    cx, cy = get_column_center_mm(rpc, source_column_id)

    # Y側（left/right）を決める: 明示 > 選択寸法 > 多数決
    selected_y_side: Optional[str] = None
    axis_y_side_votes: List[str] = []
    for d in dims:
        refs = [int(r) for r in (d.get("references") or []) if isinstance(r, int)]
        if source_column_id not in refs:
            continue
        grid_id = next((r for r in refs if r in grids_map), None)
        if not grid_id:
            continue
        axis = grid_axis_by_orientation(grids_map[grid_id])
        o = d.get("origin") or {}
        ox = to_float(o.get("x"))
        oy = to_float(o.get("y"))
        if ox is None or oy is None:
            continue
        side = infer_dim_side(axis, ox, oy, cx, cy)
        if axis == "Y":
            axis_y_side_votes.append(side)
        if selected_dim_id and int(d.get("elementId") or 0) == int(selected_dim_id) and axis == "Y":
            selected_y_side = side

    if force_y_side in ("left", "right"):
        target_y_side = force_y_side
    elif selected_y_side in ("left", "right"):
        target_y_side = selected_y_side
    elif axis_y_side_votes:
        target_y_side = max(set(axis_y_side_votes), key=axis_y_side_votes.count)
    else:
        target_y_side = "left"

    target_x_side = "top" if force_x_side != "bottom" else "bottom"

    moved = 0
    skipped = 0
    rows: List[Dict[str, Any]] = []
    for d in dims:
        did = int(d.get("elementId") or 0)
        refs = [int(r) for r in (d.get("references") or []) if isinstance(r, int)]
        if source_column_id not in refs:
            skipped += 1
            continue

        o = d.get("origin") or {}
        ox = to_float(o.get("x"))
        oy = to_float(o.get("y"))
        if ox is None or oy is None:
            skipped += 1
            continue

        grid_id = next((r for r in refs if r in grids_map), None)
        axis: Optional[str] = None
        kind = "unknown"

        if grid_id is not None:
            axis = grid_axis_by_orientation(grids_map[grid_id])
            kind = "main"
        else:
            # 幅寸法（柱↔柱）想定
            col_refs = [r for r in refs if r == source_column_id]
            if len(col_refs) >= 2:
                dx = abs(ox - cx)
                dy = abs(oy - cy)
                axis = "X" if dy >= dx else "Y"
                kind = "width"

        if axis not in ("X", "Y"):
            skipped += 1
            continue

        current_offset = (oy - cy) if axis == "X" else (ox - cx)
        rounded_abs = round_step_abs(current_offset, round_mm, min_mm)
        if axis == "X":
            target_offset = rounded_abs if target_x_side == "top" else -rounded_abs
        else:
            target_offset = rounded_abs if target_y_side == "right" else -rounded_abs

        delta = target_offset - current_offset
        dx = delta if axis == "Y" else 0.0
        dy = delta if axis == "X" else 0.0
        ok_move = True
        move_raw = {"ok": True, "skipped": True}
        if abs(dx) > 1e-3 or abs(dy) > 1e-3:
            move_raw = rpc.call_any(
                ["view.move_dimension", "move_dimension"],
                {"elementId": did, "dx": dx, "dy": dy, "dz": 0.0},
            )
            ok_move = bool(move_raw.get("ok"))
            if ok_move:
                moved += 1

        rows.append({
            "dimensionId": did,
            "kind": kind,
            "axis": axis,
            "currentOffsetMm": round(current_offset, 3),
            "targetOffsetMm": round(target_offset, 3),
            "deltaMm": round(delta, 3),
            "moveOk": ok_move,
            "moveRaw": move_raw,
        })

    return {
        "ok": True,
        "targetAxisXSide": target_x_side,
        "targetAxisYSide": target_y_side,
        "moved": moved,
        "skipped": skipped,
        "items": rows,
    }


def main() -> int:
    rpc = RpcClient(PORT)
    run_stamp = time.strftime("%m%d%H%M%S")

    ctx = get_active_view_context(rpc)
    source = resolve_view_by_name_or_level_or_active(rpc)
    source_view_id = int(source.get("viewId") or 0)
    source_view_name = str(source.get("name") or "")
    if source_view_id <= 0:
        raise RuntimeError("source view id を解決できませんでした。")

    all_column_ids = sorted(collect_columns_in_view(rpc, source_view_id))
    total_column_count = len(all_column_ids)
    if total_column_count < MIN_REQUIRED_COLUMNS:
        raise RuntimeError(f"対象柱が不足しています（count={total_column_count}）")

    batch_size = int(BATCH_SIZE or 0)
    batch_index = int(BATCH_INDEX or 0)
    if batch_size > 0:
        if batch_index < 0:
            raise RuntimeError(f"BATCH_INDEX が不正です: {batch_index}")
        batch_start = batch_index * batch_size
        if batch_start >= total_column_count:
            raise RuntimeError(
                f"指定バッチが範囲外です: start={batch_start}, total={total_column_count}, "
                f"BATCH_SIZE={batch_size}, BATCH_INDEX={batch_index}"
            )
        batch_end = min(batch_start + batch_size, total_column_count)
        column_ids = all_column_ids[batch_start:batch_end]
    else:
        if total_column_count > MAX_ALLOWED_COLUMNS:
            raise RuntimeError(
                f"安全停止: 柱本数={total_column_count} > MAX_ALLOWED_COLUMNS={MAX_ALLOWED_COLUMNS} "
                f"(BATCH_SIZE を設定して分割実行してください)"
            )
        batch_start = 0
        batch_end = total_column_count
        column_ids = all_column_ids

    if len(column_ids) > MAX_ALLOWED_COLUMNS:
        raise RuntimeError(
            f"安全停止: バッチ柱本数={len(column_ids)} > MAX_ALLOWED_COLUMNS={MAX_ALLOWED_COLUMNS} "
            f"(BATCH_SIZE を下げてください)"
        )

    grids = collect_grids(rpc)
    if len(grids) < 2:
        raise RuntimeError("グリッドが不足しています。")
    grids_map = {int(g["id"]): g for g in grids}

    # source view の寸法を取得し、source column を決定
    src_dims_res = rpc.call_any(
        ["view.get_dimensions_in_view", "get_dimensions_in_view"],
        {"viewId": source_view_id, "includeOrigin": True, "includeReferences": True, "includeValue": True},
    )
    src_dims = get_list(src_dims_res, ["items", "dimensions"])
    selected_ids = list(ctx.get("selectionIds") or [])
    selected_dim_id = next((int(x) for x in selected_ids if isinstance(x, int)), None)

    source_column_id, selected_axis, _ = choose_source_column_id(
        source_view_name,
        source_view_id,
        column_ids,
        selected_ids,
        src_dims,
        grids_map,
    )
    source_level_label = resolve_source_level_label(rpc, source_view_name, source_column_id)
    prefix = f"CGA_{source_level_label}_{run_stamp}"

    summary: Dict[str, Any] = {
        "ok": True,
        "runStamp": run_stamp,
        "prefix": prefix,
        "sourceLevelLabel": source_level_label,
        "sourceViewId": source_view_id,
        "sourceViewName": source_view_name,
        "sourceColumnId": source_column_id,
        "selectedDimensionId": selected_dim_id,
        "selectedAxisInSource": selected_axis,
        "totalColumnCount": total_column_count,
        "columnCount": len(column_ids),
        "batch": {
            "enabled": bool(batch_size > 0),
            "batchSize": int(batch_size),
            "batchIndex": int(batch_index),
            "startIndex": int(batch_start),
            "endIndexExclusive": int(batch_end),
        },
        "gridTemplate": GRID_TEMPLATE_NAME,
        "columnTemplate": COLUMN_TEMPLATE_NAME,
        "columnViewTypeName": COLUMN_VIEW_TYPE_NAME,
        "gridBaseUseColumnViewType": GRID_BASE_USE_COLUMN_VIEW_TYPE,
        "items": [],
    }
    resolved_tag_type = None
    if PLACE_STRUCTURAL_COLUMN_TAG:
        resolved_tag_type = resolve_structural_column_tag_type(rpc)
    summary["structuralColumnTagType"] = (
        {
            "ok": True,
            "typeId": int(resolved_tag_type.get("typeId") or 0),
            "typeName": resolved_tag_type.get("typeName"),
            "familyName": resolved_tag_type.get("familyName"),
            "categoryName": resolved_tag_type.get("categoryName"),
        }
        if isinstance(resolved_tag_type, dict)
        else {"ok": False, "msg": "構造柱タグタイプを検出できませんでした。"}
    )

    # A) 通り芯だけビュー
    gdup = duplicate_view(rpc, source_view_id, f"{prefix}_GRID_BASE", with_detailing=False)
    grid_view_id = int(gdup.get("viewId") or gdup.get("elementId") or 0)
    if grid_view_id <= 0:
        raise RuntimeError("通り芯ビューの複写に失敗しました。")

    if GRID_BASE_USE_COLUMN_VIEW_TYPE:
        summary["gridBaseViewTypeSet"] = try_set_view_type_by_name(rpc, grid_view_id, COLUMN_VIEW_TYPE_NAME)
    else:
        summary["gridBaseViewTypeSet"] = {"ok": True, "skipped": True, "msg": "GRID_BASE view type sync disabled"}

    grid_template_applied = False
    if template_exists(rpc, GRID_TEMPLATE_NAME):
        r = try_apply_template(rpc, grid_view_id, GRID_TEMPLATE_NAME)
        grid_template_applied = bool(r.get("ok"))
    if not grid_template_applied:
        keep_only_categories(rpc, grid_view_id, [OST_GRIDS])

    set_view_scale(rpc, grid_view_id, GRID_SCALE)
    if HIDE_GRID_BUBBLES:
        set_grid_bubbles_hidden(rpc, grid_view_id)

    # B) 寸法テンプレート兼 source 柱ビュー（withDetailing=true）
    src_col_view_name = f"{prefix}_COL_{source_column_id}"
    src_col_dup = duplicate_view(rpc, source_view_id, src_col_view_name, with_detailing=True)
    src_col_view_id = int(src_col_dup.get("viewId") or src_col_dup.get("elementId") or 0)
    if src_col_view_id <= 0:
        raise RuntimeError("source column view の複写に失敗しました。")

    # C) その他柱ビュー（withDetailing=false）
    col_views: List[Dict[str, Any]] = []
    all_for_loop = [source_column_id] + [cid for cid in column_ids if cid != source_column_id]
    col_template_exists = template_exists(rpc, COLUMN_TEMPLATE_NAME)

    for cid in all_for_loop:
        item: Dict[str, Any] = {"columnId": cid, "ok": True}
        try:
            if cid == source_column_id:
                col_view_id = src_col_view_id
                item["sourceTemplateView"] = True
            else:
                dup = duplicate_view(rpc, source_view_id, f"{prefix}_COL_{cid}", with_detailing=False)
                col_view_id = int(dup.get("viewId") or dup.get("elementId") or 0)
                if col_view_id <= 0:
                    raise RuntimeError("柱ビュー複写失敗")

            item["viewId"] = col_view_id
            item["columnTemplateApplied"] = False
            item["viewTypeSet"] = try_set_view_type_by_name(rpc, col_view_id, COLUMN_VIEW_TYPE_NAME)
            keep_only_categories(rpc, col_view_id, [OST_STRUCTURAL_COLUMNS, OST_GRIDS])

            item["crop"] = crop_plan_to_element(rpc, col_view_id, cid, COLUMN_MARGIN_MM)
            item["cropVisibleOff"] = set_crop_region_visibility_off(rpc, col_view_id)
            item["scale"] = set_view_scale(rpc, col_view_id, COLUMN_SCALE)

            others = [x for x in column_ids if x != cid]
            if others:
                item["hideOthers"] = rpc.call_any(
                    ["view.hide_elements_in_view", "hide_elements_in_view"],
                    {
                        "viewId": int(col_view_id),
                        "elementIds": others,
                        "detachViewTemplate": True,
                        "refreshView": True,
                        "batchSize": 800,
                        "maxMillisPerTx": 4000,
                        "failureHandling": {"enabled": True, "mode": "rollback", "confirmProceed": True},
                    },
                )
            else:
                item["hideOthers"] = {"ok": True, "skipped": True, "msg": "no other columns"}

            item["gridSegments"] = set_grid_segments_around_column(rpc, col_view_id, cid, GRID_HALF_LENGTH_MM)
            if HIDE_GRID_BUBBLES:
                item["gridBubbles"] = set_grid_bubbles_hidden(rpc, col_view_id)

            if PLACE_STRUCTURAL_COLUMN_TAG:
                tid = int((resolved_tag_type or {}).get("typeId") or 0)
                item["columnTag"] = place_structural_column_tag_top_right(
                    rpc=rpc,
                    view_id=col_view_id,
                    host_element_id=cid,
                    tag_type_id=tid,
                    dx_mm=TAG_OFFSET_RIGHT_MM,
                    dy_mm=TAG_OFFSET_UP_MM,
                    add_leader=TAG_ADD_LEADER,
                )

            if col_template_exists:
                t = try_apply_template(rpc, col_view_id, COLUMN_TEMPLATE_NAME)
                item["columnTemplateApplied"] = bool(t.get("ok"))
                item["columnTemplateApplyRaw"] = t

            cx, cy = get_column_center_mm(rpc, cid)
            anchor = choose_nearest_grid_pair(grids, cx, cy)
            if anchor:
                item["anchorGridA"] = anchor[0]
                item["anchorGridB"] = anchor[1]
                item["anchorMm"] = {"x": round(anchor[2], 3), "y": round(anchor[3], 3)}
            col_views.append(item)
        except Exception as ex:
            item["ok"] = False
            item["msg"] = str(ex)
            col_views.append(item)

    summary["items"] = col_views

    # D) source テンプレート寸法を補正（Xは上、100mm丸め）
    if APPLY_DIMENSION_STANDARD:
        dim_adjust = adjust_template_dimensions(
            rpc=rpc,
            source_template_view_id=src_col_view_id,
            source_column_id=source_column_id,
            grids_map=grids_map,
            selected_dim_id=selected_dim_id,
            force_x_side=FORCE_AXIS_X_SIDE,
            force_y_side=FORCE_AXIS_Y_SIDE,
            round_mm=OFFSET_ROUND_MM,
            min_mm=OFFSET_MIN_MM,
        )
        summary["dimensionTemplateAdjust"] = dim_adjust

        # E) 全柱ビューへ寸法展開
        # source寸法が無くても allowDefaultTemplateWhenSourceMissing=true で生成を試行する。
        regex = rf"^{re.escape(prefix)}_COL_(\d+)$"
        apply_res = rpc.call_any(
            ["view.apply_column_grid_dimension_standard_to_views", "apply_column_grid_dimension_standard_to_views"],
            {
                "sourceViewId": src_col_view_id,
                "targetViewNameRegex": regex,
                "replaceExisting": True,
                "includeSourceView": False,
                "allowDefaultTemplateWhenSourceMissing": True,
                "offsetFromColumnFaceMm": float(DIM_FACE_TO_GRID_OFFSET_MM),
                "secondTierGapMm": float(max(0.0, DIM_OUTLINE_OFFSET_MM - DIM_FACE_TO_GRID_OFFSET_MM)),
                "forceAxisXFaceSide": "bottom",
                "forceAxisYFaceSide": "left",
                "createCenterGridDimensions": True,
                "centerGridAxisXSide": "top",
                "centerGridAxisYSide": "right",
                "centerGridOffsetMm": float(DIM_CENTER_TO_GRID_OFFSET_MM),
                "centerGridSkipZeroToleranceMm": float(DIM_CENTER_ZERO_TOLERANCE_MM),
            },
        )
        summary["dimensionApply"] = apply_res

    # F) シート重ね（任意）
    if CREATE_SHEET:
        sh = create_sheet_unique(rpc, SHEET_NUMBER_PREFIX, SHEET_NAME, NO_TITLEBLOCK)
        sheet_id = int(sh.get("sheetId") or 0)
        if sheet_id <= 0:
            summary["sheet"] = {"ok": False, "msg": "sheet.create failed", "raw": sh}
        else:
            p_grid = rpc.call_any(
                ["sheet.place_view", "place_view_on_sheet"],
                {"sheetId": sheet_id, "viewId": grid_view_id, "centerOnSheet": True},
            )
            vp_grid = int(p_grid.get("viewportId") or 0)
            placed = 0
            for cv in col_views:
                if not cv.get("ok") or not cv.get("viewId"):
                    continue
                view_id = int(cv["viewId"])
                p_col = rpc.call_any(
                    ["sheet.place_view", "place_view_on_sheet"],
                    {"sheetId": sheet_id, "viewId": view_id, "centerOnSheet": True},
                )
                vp_col = int(p_col.get("viewportId") or 0)
                align = None
                if vp_grid > 0 and vp_col > 0 and cv.get("anchorGridA") and cv.get("anchorGridB"):
                    align = rpc.call_any(
                        ["sheet.replace_view", "replace_view_on_sheet"],
                        {
                            "viewportId": vp_col,
                            "newViewId": view_id,
                            "keepLocation": True,
                            "copyScale": False,
                            "alignByGridIntersection": {
                                "enabled": True,
                                "referenceViewportId": vp_grid,
                                "gridA": cv["anchorGridA"],
                                "gridB": cv["anchorGridB"],
                            },
                        },
                    )
                cv["placement"] = p_col
                cv["alignment"] = align
                placed += 1

            summary["sheet"] = {
                "ok": True,
                "sheetId": sheet_id,
                "sheetNumber": sh.get("sheetNumber"),
                "sheetName": sh.get("sheetName"),
                "gridViewId": grid_view_id,
                "gridViewportId": vp_grid,
                "placedColumnViews": placed,
                "totalViewsOnSheetExpected": 1 + placed,
            }

    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
