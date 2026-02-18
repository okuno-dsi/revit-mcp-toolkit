# @feature: 柱芯線図（1シート重ね配置プロトタイプ） | keywords: 柱, グリッド, シート, スケール, アンカー
# -*- coding: utf-8 -*-
"""
目的:
- 「1/2」ビューを元に、柱ごとトリミングビュー + グリッドビューを1シートに重ね配置する。
- 一括コマンド化せず、Pythonで後から調整しやすい形に限定。

今回仕様:
- 1シートに「柱ビューN個 + グリッドビュー1個」を配置
- 柱ビューは 1/100、グリッドビューは 1/200
- グリッド交点アンカーで重ね整合
- 大量生成防止の安全ガード付き
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
SOURCE_VIEW_NAME = "1/2"
TARGET_LEVEL_NAME = ""  # 空ならビューの「関連したレベル」を使用

COLUMN_MARGIN_MM = 100.0
COLUMN_SCALE = 100
GRID_SCALE = 200

SHEET_NUMBER_PREFIX = "CGA-"
SHEET_NAME = "柱芯線図_重ね"
NO_TITLEBLOCK = True

# 安全ガード
# - プロジェクト差分に対応するため、本数固定チェックはデフォルト無効（0）
# - 必要時だけ EXPECTED_COLUMN_COUNT を正数にして厳格運用できる
EXPECTED_COLUMN_COUNT = 0        # 0=固定本数チェック無効, >0=厳格チェック
MAX_ALLOWED_COLUMNS = 120        # 異常な大量生成を防ぐ上限
MIN_REQUIRED_COLUMNS = 1         # 対象柱がこれ未満なら停止


OST_STRUCTURAL_COLUMNS = -2001330
OST_GRIDS = -2000220

DEFAULT_TIMEOUT = 30
POLL_INTERVAL = 0.3
POLL_TIMEOUT = 120


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


def resolve_view_by_name(rpc: RpcClient, view_name: str) -> Dict[str, Any]:
    resp = rpc.call("view.get_views", {})
    views = get_list(resp, ["views", "items"])
    for v in views:
        if (v.get("name") or "").strip().lower() == view_name.strip().lower():
            return v
    for v in views:
        if view_name.strip().lower() in (v.get("name") or "").lower():
            return v
    raise RuntimeError(f"View not found: {view_name}")


def get_view_related_level_name(rpc: RpcClient, view_id: int) -> str:
    p = rpc.call("view.get_view_parameters", {"viewId": int(view_id)})
    for prm in p.get("parameters") or []:
        name = (prm.get("name") or "").strip()
        if name in ("関連したレベル", "Associated Level"):
            # display/refName/value の順に採用
            for k in ("display", "refName", "value"):
                v = prm.get(k)
                if isinstance(v, str) and v.strip():
                    return v.strip()
    return ""


def collect_columns_in_view(rpc: RpcClient, view_id: int) -> List[int]:
    resp = rpc.call("element.get_elements_in_view", {"viewId": int(view_id), "categoryIds": [OST_STRUCTURAL_COLUMNS]})
    rows = get_list(resp, ["rows", "items", "elements"])
    ids: List[int] = []
    for r in rows:
        eid = r.get("elementId") or r.get("id")
        if isinstance(eid, int):
            ids.append(eid)
    return sorted(set(ids))


def get_structural_columns_all(rpc: RpcClient) -> List[Dict[str, Any]]:
    r = rpc.call("element.get_structural_columns", {})
    return get_list(r, ["structuralColumns", "items", "rows", "elements"])


def collect_grids(rpc: RpcClient) -> List[Dict[str, Any]]:
    resp = rpc.call("element.get_grids", {})
    grids = get_list(resp, ["grids", "items"])
    out = []
    for g in grids:
        s = g.get("start") or {}
        e = g.get("end") or {}
        name = g.get("name")
        if not name:
            continue
        try:
            sx, sy = float(s.get("x")), float(s.get("y"))
            ex, ey = float(e.get("x")), float(e.get("y"))
        except Exception:
            continue
        out.append({"name": name, "sx": sx, "sy": sy, "ex": ex, "ey": ey})
    return out


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
        r = rpc.call("view.set_view_parameter", p)
        if isinstance(r, dict) and r.get("ok"):
            return r
        if isinstance(r, dict):
            last = r
    return last


def create_sheet_unique(rpc: RpcClient, sheet_no_prefix: str, sheet_name: str, no_titleblock: bool) -> Dict[str, Any]:
    run_stamp = time.strftime("%m%d%H%M%S")
    base_no = f"{sheet_no_prefix}{run_stamp}"
    for i in range(0, 300):
        no_try = base_no if i == 0 else f"{base_no}-{i:03d}"
        nm_try = sheet_name if i == 0 else f"{sheet_name}_{i:03d}"
        r = rpc.call("sheet.create", {"sheetNumber": no_try, "sheetName": nm_try, "noTitleBlock": bool(no_titleblock)})
        if isinstance(r, dict) and r.get("sheetId"):
            return r
    return {"ok": False, "msg": "sheet.create retry exhausted"}


def crop_plan_to_element(rpc: RpcClient, view_id: int, element_id: int, margin_mm: float) -> Dict[str, Any]:
    return rpc.call_any(
        ["view.crop_plan_view_to_element", "crop_plan_view_to_element"],
        {"viewId": int(view_id), "elementId": int(element_id), "marginMm": float(margin_mm), "cropActive": True, "cropVisible": True},
    )


def main() -> int:
    rpc = RpcClient(PORT)

    source_view = resolve_view_by_name(rpc, SOURCE_VIEW_NAME)
    source_view_id = int(source_view.get("viewId") or source_view.get("id") or 0)
    if source_view_id <= 0:
        raise RuntimeError("source view id を解決できませんでした。")

    all_col_ids = collect_columns_in_view(rpc, source_view_id)
    all_cols = get_structural_columns_all(rpc)
    level_by_id: Dict[int, str] = {}
    for c in all_cols:
        try:
            eid = int(c.get("elementId"))
        except Exception:
            continue
        lv = (c.get("levelName") or c.get("level") or "").strip()
        level_by_id[eid] = lv

    target_level = TARGET_LEVEL_NAME.strip()
    if not target_level:
        target_level = get_view_related_level_name(rpc, source_view_id)

    col_ids = [eid for eid in all_col_ids if (not target_level or (level_by_id.get(eid, "") == target_level))]
    col_ids = sorted(set(col_ids))

    if len(col_ids) < MIN_REQUIRED_COLUMNS:
        raise RuntimeError(f"対象柱が不足しています（count={len(col_ids)} < MIN_REQUIRED_COLUMNS={MIN_REQUIRED_COLUMNS}）")
    if len(col_ids) > MAX_ALLOWED_COLUMNS:
        raise RuntimeError(f"安全停止: 対象柱が {len(col_ids)} 本です。MAX_ALLOWED_COLUMNS={MAX_ALLOWED_COLUMNS} を超えています。")
    if EXPECTED_COLUMN_COUNT > 0 and len(col_ids) != EXPECTED_COLUMN_COUNT:
        raise RuntimeError(
            f"安全停止: 想定本数 {EXPECTED_COLUMN_COUNT} 本に対して実際は {len(col_ids)} 本です。"
            "EXPECTED_COLUMN_COUNT=0 にすると固定本数チェックを無効化できます。"
        )

    grids = collect_grids(rpc)
    if len(grids) < 2:
        raise RuntimeError("グリッドが不足しています。")

    summary: Dict[str, Any] = {
        "ok": True,
        "sourceViewName": SOURCE_VIEW_NAME,
        "sourceViewId": source_view_id,
        "targetLevel": target_level,
        "columnCount": len(col_ids),
        "items": [],
    }

    # 1) グリッドビューを1つ作成
    gdup = rpc.call(
        "view.duplicate_view",
        {"viewId": source_view_id, "withDetailing": False, "desiredName": f"{SOURCE_VIEW_NAME}_GRID_BASE", "onNameConflict": "increment"},
    )
    grid_view_id = int(gdup.get("viewId") or gdup.get("elementId") or 0)
    if grid_view_id <= 0:
        raise RuntimeError("グリッドビューの複写に失敗しました。")

    rpc.call(
        "view.set_category_visibility_bulk",
        {
            "viewId": grid_view_id,
            "mode": "keep_only",
            "categoryType": "All",
            "keepCategoryIds": [OST_GRIDS],
            "detachViewTemplate": True,
        },
    )
    set_view_scale(rpc, grid_view_id, GRID_SCALE)

    # 2) 柱ビューを作成（各柱1ビュー）
    col_views: List[Dict[str, Any]] = []
    for col_id in col_ids:
        dup = rpc.call(
            "view.duplicate_view",
            {
                "viewId": source_view_id,
                "withDetailing": False,
                "desiredName": f"{SOURCE_VIEW_NAME}_COL_{col_id}",
                "onNameConflict": "increment",
            },
        )
        col_view_id = int(dup.get("viewId") or dup.get("elementId") or 0)
        if col_view_id <= 0:
            summary["items"].append({"columnId": col_id, "ok": False, "msg": "柱ビュー複写失敗"})
            continue

        rpc.call(
            "view.set_category_visibility_bulk",
            {
                "viewId": col_view_id,
                "mode": "keep_only",
                "categoryType": "All",
                "keepCategoryIds": [OST_STRUCTURAL_COLUMNS, OST_GRIDS],
                "detachViewTemplate": True,
            },
        )

        crop_res = crop_plan_to_element(rpc, col_view_id, col_id, COLUMN_MARGIN_MM)
        scale_res = set_view_scale(rpc, col_view_id, COLUMN_SCALE)

        # 他の対象柱を非表示
        others = [x for x in col_ids if x != col_id]
        hide_res = rpc.call(
            "view.hide_elements_in_view",
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

        bb = rpc.call("element.get_bounding_box", {"elementId": col_id})
        boxes = bb.get("boxes") or []
        if boxes and boxes[0].get("ok"):
            b = boxes[0]["boundingBox"]
            cx = (float(b["min"]["x"]) + float(b["max"]["x"])) * 0.5
            cy = (float(b["min"]["y"]) + float(b["max"]["y"])) * 0.5
        else:
            cx = 0.0
            cy = 0.0
        anchor = choose_nearest_grid_pair(grids, cx, cy)
        if anchor:
            g1, g2, ax, ay = anchor
        else:
            g1, g2, ax, ay = "", "", 0.0, 0.0

        col_views.append(
            {
                "columnId": col_id,
                "viewId": col_view_id,
                "crop": crop_res,
                "scale": scale_res,
                "hideOthers": hide_res,
                "anchorGridA": g1,
                "anchorGridB": g2,
                "anchorMm": {"x": round(ax, 3), "y": round(ay, 3)},
            }
        )

    if not col_views:
        raise RuntimeError("柱ビューが1つも作成できませんでした。")

    # 3) シート1枚作成
    sh = create_sheet_unique(rpc, SHEET_NUMBER_PREFIX, SHEET_NAME, NO_TITLEBLOCK)
    sheet_id = int(sh.get("sheetId") or 0)
    if sheet_id <= 0:
        raise RuntimeError("シート作成に失敗しました。")

    # 4) まずグリッドビューを配置（基準）
    p_grid = rpc.call("sheet.place_view", {"sheetId": sheet_id, "viewId": grid_view_id, "centerOnSheet": True})
    vp_grid = int(p_grid.get("viewportId") or 0)

    # 5) 柱ビューを同シートに順次配置して、グリッド交点で整合
    placed_count = 0
    for cv in col_views:
        view_id = int(cv["viewId"])
        p_col = rpc.call("sheet.place_view", {"sheetId": sheet_id, "viewId": view_id, "centerOnSheet": True})
        vp_col = int(p_col.get("viewportId") or 0)
        align = None
        if vp_grid > 0 and vp_col > 0 and cv.get("anchorGridA") and cv.get("anchorGridB"):
            align = rpc.call(
                "sheet.replace_view",
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
        placed_count += 1

    summary["sheet"] = {
        "sheetId": sheet_id,
        "sheetNumber": sh.get("sheetNumber"),
        "sheetName": sh.get("sheetName"),
        "gridViewId": grid_view_id,
        "gridViewportId": vp_grid,
        "placedColumnViews": placed_count,
        "totalViewsOnSheetExpected": 1 + placed_count,
    }
    summary["items"] = col_views

    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
