# @feature: 柱芯線図ワークフロー（通り芯ビュー + 柱別ビュー + シート重ね） | keywords: 柱, 通り芯, グリッド, シート, スケール
# -*- coding: utf-8 -*-
"""
柱芯線図（グリッドのみビュー + 柱別ビュー）を Python Runner で再現するサンプル。

このスクリプトで行うこと:
1) 元ビュー（例: 1/2）から「通り芯だけ」ビューを1つ作成
2) 元ビューの構造柱ごとに「柱別ビュー」を作成（トリミング +100mm）
3) 柱別ビューごとに、通り芯の表示長さを柱中心周りに調整
4) 通り芯記号（バブル）を非表示化
5) （任意）シート1枚に、通り芯ビュー + 柱別ビューを重ね配置
   - 柱ビュー 1/100、通り芯ビュー 1/200
   - グリッド交点アンカーで位置合わせ

前提:
- Revit MCP Server が起動済み（既定: 5210）
- 元ビューに対象柱が表示されている
- 必要ならビューテンプレートを事前作成
  - GRID_TEMPLATE_NAME = "通り心だけ"
  - COLUMN_TEMPLATE_NAME = "構造柱ビュー"
"""

import os
import json
import time
import math
from typing import Any, Dict, List, Optional, Tuple

import requests


# --------------------------
# User editable parameters
# --------------------------
PORT = int(os.environ.get("REVIT_MCP_PORT", "5210"))
SOURCE_VIEW_NAME = "1/2"          # 空文字ならアクティブビュー
USE_ACTIVE_VIEW_IF_NOT_FOUND = True

# 既存テンプレート名（空なら未適用）
GRID_TEMPLATE_NAME = "通り心だけ"
COLUMN_TEMPLATE_NAME = "構造柱ビュー"

# 柱ビュー設定
COLUMN_MARGIN_MM = 100.0          # 柱外周 +100mm
COLUMN_SCALE = 100
GRID_SCALE = 200
GRID_HALF_LENGTH_MM = 1000.0      # 柱中心から±1000mm
HIDE_GRID_BUBBLES = True

# シート配置
CREATE_SHEET = True
SHEET_NUMBER_PREFIX = "CGA-"
SHEET_NAME = "柱芯線図_重ね"
NO_TITLEBLOCK = True

# 安全ガード
MAX_ALLOWED_COLUMNS = 40
MIN_REQUIRED_COLUMNS = 1


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


def get_active_view_context(rpc: RpcClient) -> Dict[str, Any]:
    ctx = rpc.call("help.get_context", {})
    d = (ctx.get("data") if isinstance(ctx, dict) else None) or {}
    return {
        "viewId": int(d.get("activeViewId") or 0),
        "viewName": str(d.get("activeViewName") or ""),
        "docTitle": str(d.get("docTitle") or ""),
    }


def resolve_view_by_name_or_active(rpc: RpcClient, view_name: str) -> Dict[str, Any]:
    if not view_name.strip():
        c = get_active_view_context(rpc)
        if c["viewId"] <= 0:
            raise RuntimeError("アクティブビューを取得できません。")
        return {"viewId": c["viewId"], "name": c["viewName"]}

    resp = rpc.call_any(["view.get_views", "get_views"], {})
    views = get_list(resp, ["views", "items"])
    for v in views:
        if (v.get("name") or "").strip().lower() == view_name.strip().lower():
            return v
    for v in views:
        if view_name.strip().lower() in (v.get("name") or "").lower():
            return v

    if USE_ACTIVE_VIEW_IF_NOT_FOUND:
        c = get_active_view_context(rpc)
        if c["viewId"] > 0:
            return {"viewId": c["viewId"], "name": c["viewName"], "fallback": True}
    raise RuntimeError(f"ビューが見つかりません: {view_name}")


def template_exists(rpc: RpcClient, template_name: str) -> bool:
    if not template_name.strip():
        return False
    resp = rpc.call_any(["view.get_views", "get_views"], {})
    views = get_list(resp, ["views", "items"])
    for v in views:
        if (v.get("name") or "").strip().lower() == template_name.strip().lower():
            if bool(v.get("isTemplate")):
                return True
    return False


def try_apply_template(rpc: RpcClient, view_id: int, template_name: str) -> Dict[str, Any]:
    if not template_name.strip():
        return {"ok": False, "skipped": True, "msg": "template name empty"}
    return rpc.call_any(
        ["view.set_view_template", "set_view_template"],
        {"viewId": int(view_id), "templateName": template_name},
    )


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
        try:
            sx, sy = float(s.get("x")), float(s.get("y"))
            ex, ey = float(e.get("x")), float(e.get("y"))
        except Exception:
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


def duplicate_view(rpc: RpcClient, source_view_id: int, desired_name: str) -> Dict[str, Any]:
    return rpc.call_any(
        ["view.duplicate_view", "duplicate_view"],
        {
            "viewId": int(source_view_id),
            "withDetailing": False,
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


def main() -> int:
    rpc = RpcClient(PORT)
    src = resolve_view_by_name_or_active(rpc, SOURCE_VIEW_NAME)
    source_view_id = int(src.get("viewId") or src.get("id") or 0)
    source_view_name = str(src.get("name") or SOURCE_VIEW_NAME or "(active)")
    if source_view_id <= 0:
        raise RuntimeError("元ビューIDを解決できませんでした。")

    column_ids = collect_columns_in_view(rpc, source_view_id)
    if len(column_ids) < MIN_REQUIRED_COLUMNS:
        raise RuntimeError(f"対象柱が不足しています（count={len(column_ids)}）")
    if len(column_ids) > MAX_ALLOWED_COLUMNS:
        raise RuntimeError(f"安全停止: 柱本数={len(column_ids)} > MAX_ALLOWED_COLUMNS={MAX_ALLOWED_COLUMNS}")

    grids = collect_grids(rpc)
    if len(grids) < 2:
        raise RuntimeError("グリッドが不足しています。")

    summary: Dict[str, Any] = {
        "ok": True,
        "sourceViewId": source_view_id,
        "sourceViewName": source_view_name,
        "columnCount": len(column_ids),
        "gridTemplate": GRID_TEMPLATE_NAME,
        "columnTemplate": COLUMN_TEMPLATE_NAME,
        "items": [],
    }

    # A) 通り芯だけビュー
    gdup = duplicate_view(rpc, source_view_id, f"{source_view_name}_GRID_BASE")
    grid_view_id = int(gdup.get("viewId") or gdup.get("elementId") or 0)
    if grid_view_id <= 0:
        raise RuntimeError("通り芯ビューの複写に失敗しました。")

    grid_template_applied = False
    if template_exists(rpc, GRID_TEMPLATE_NAME):
        r = try_apply_template(rpc, grid_view_id, GRID_TEMPLATE_NAME)
        grid_template_applied = bool(r.get("ok"))
    if not grid_template_applied:
        keep_only_categories(rpc, grid_view_id, [OST_GRIDS])

    set_view_scale(rpc, grid_view_id, GRID_SCALE)
    if HIDE_GRID_BUBBLES:
        set_grid_bubbles_hidden(rpc, grid_view_id)

    # B) 柱別ビュー
    col_views: List[Dict[str, Any]] = []
    col_template_exists = template_exists(rpc, COLUMN_TEMPLATE_NAME)
    for cid in column_ids:
        item: Dict[str, Any] = {"columnId": cid, "ok": True}
        try:
            dup = duplicate_view(rpc, source_view_id, f"{source_view_name}_COL_{cid}")
            col_view_id = int(dup.get("viewId") or dup.get("elementId") or 0)
            if col_view_id <= 0:
                raise RuntimeError("柱ビュー複写失敗")

            item["viewId"] = col_view_id

            # テンプレートがあれば優先。なければカテゴリ keep_only で代替。
            if col_template_exists:
                t = try_apply_template(rpc, col_view_id, COLUMN_TEMPLATE_NAME)
                item["columnTemplateApplied"] = bool(t.get("ok"))
            if not item.get("columnTemplateApplied"):
                keep_only_categories(rpc, col_view_id, [OST_STRUCTURAL_COLUMNS, OST_GRIDS])

            item["crop"] = crop_plan_to_element(rpc, col_view_id, cid, COLUMN_MARGIN_MM)
            item["scale"] = set_view_scale(rpc, col_view_id, COLUMN_SCALE)

            others = [x for x in column_ids if x != cid]
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

            item["gridSegments"] = set_grid_segments_around_column(rpc, col_view_id, cid, GRID_HALF_LENGTH_MM)
            if HIDE_GRID_BUBBLES:
                item["gridBubbles"] = set_grid_bubbles_hidden(rpc, col_view_id)

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

    # C) シート重ね（任意）
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

