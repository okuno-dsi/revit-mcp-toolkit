# @feature: 柱芯線図シートへ未作成の次20本を追加 | keywords: 柱, 通り芯, シート, 追加
# -*- coding: utf-8 -*-
"""
既存の柱芯線図シート（例: 柱芯線図_重ね）に、未作成の柱ビューを次20本だけ追加する。

実行内容:
1) SOURCE_VIEW_NAME の構造柱一覧を取得
2) TARGET_SHEET_NAME 上の既存 _COL_<id> ビューを検出
3) 未作成IDの先頭20件を抽出
4) 柱ビューを作成（トリミング +100mm、柱1/100）
5) 既存 GRID_BASE ビューポートにグリッド交点アンカーで重ね合わせ
6) 既存柱ビューを寸法テンプレートソースにして寸法標準を再適用
"""

import json
import os
import re
import time
import math
from typing import Any, Dict, List, Optional, Tuple

import requests


PORT = int(os.environ.get("REVIT_MCP_PORT", "5210"))
SOURCE_VIEW_NAME = "3FL"
TARGET_SHEET_NAME = "柱芯線図_重ね"
TARGET_APPEND_COUNT = 20

COLUMN_TEMPLATE_NAME = "柱芯線図用ビュー"
COLUMN_MARGIN_MM = 100.0
COLUMN_SCALE = 100
GRID_HALF_LENGTH_MM = 1000.0
HIDE_GRID_BUBBLES = True
DIM_OFFSET_FROM_COLUMN_FACE_MM = 600.0
DIM_SECOND_TIER_GAP_MM = 350.0

OST_STRUCTURAL_COLUMNS = -2001330

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


def get_list(d: Dict[str, Any], keys: List[str]) -> List[Dict[str, Any]]:
    for k in keys:
        v = d.get(k)
        if isinstance(v, list):
            return v
    return []


def resolve_source_view(rpc: RpcClient) -> Dict[str, Any]:
    views = get_list(rpc.call_any(["view.get_views", "get_views"], {}), ["views", "items"])
    # exact
    for v in views:
        if str(v.get("name") or "").strip().lower() == SOURCE_VIEW_NAME.strip().lower():
            return {"viewId": int(v.get("viewId") or v.get("id") or 0), "name": v.get("name") or ""}
    # contains fallback
    for v in views:
        if SOURCE_VIEW_NAME.strip().lower() in str(v.get("name") or "").lower():
            return {"viewId": int(v.get("viewId") or v.get("id") or 0), "name": v.get("name") or ""}
    raise RuntimeError(f"ソースビューが見つかりません: {SOURCE_VIEW_NAME}")


def collect_columns_in_view(rpc: RpcClient, view_id: int) -> List[int]:
    r = rpc.call_any(
        ["element.get_elements_in_view", "get_elements_in_view"],
        {"viewId": int(view_id), "categoryIds": [OST_STRUCTURAL_COLUMNS]},
    )
    rows = get_list(r, ["rows", "items", "elements"])
    ids: List[int] = []
    for x in rows:
        eid = x.get("elementId") or x.get("id")
        if isinstance(eid, int):
            ids.append(eid)
    return sorted(set(ids))


def find_target_sheet(rpc: RpcClient) -> Dict[str, Any]:
    s = rpc.call_any(["sheet.list", "get_sheets"], {})
    sheets = get_list(s, ["sheets", "items"])
    hits = [x for x in sheets if TARGET_SHEET_NAME in str(x.get("sheetName") or x.get("name") or "")]
    if not hits:
        raise RuntimeError(f"対象シートが見つかりません: {TARGET_SHEET_NAME}")
    hits.sort(key=lambda x: int(x.get("sheetId") or x.get("id") or 0))
    h = hits[-1]
    return {
        "sheetId": int(h.get("sheetId") or h.get("id") or 0),
        "sheetNumber": h.get("sheetNumber"),
        "sheetName": h.get("sheetName") or h.get("name"),
    }


def inspect_sheet(rpc: RpcClient, sheet_id: int) -> Dict[str, Any]:
    r = rpc.call_any(["sheet.inspect", "sheet_inspect"], {"sheetId": int(sheet_id)})
    data = r.get("data") if isinstance(r, dict) else None
    if isinstance(data, dict):
        return data
    return {}


def parse_existing_from_viewports(vps: List[Dict[str, Any]]) -> Tuple[Optional[Dict[str, Any]], List[int], str]:
    grid_vp = None
    existing: List[int] = []
    prefix = ""
    for vp in vps:
        name = str(vp.get("viewName") or "")
        if "_GRID_BASE" in name and grid_vp is None:
            grid_vp = vp
            prefix = name.split("_GRID_BASE")[0]
        m = re.search(r"_COL_(\d+)", name)
        if m:
            try:
                existing.append(int(m.group(1)))
            except Exception:
                pass
    if not prefix:
        prefix = f"CGA_{time.strftime('%m%d%H%M%S')}"
    return grid_vp, sorted(set(existing)), prefix


def template_exists(rpc: RpcClient, template_name: str) -> bool:
    if not template_name.strip():
        return False
    views = get_list(rpc.call_any(["view.get_views", "get_views"], {}), ["views", "items"])
    for v in views:
        if str(v.get("name") or "").strip().lower() == template_name.strip().lower() and bool(v.get("isTemplate")):
            return True
    return False


def try_apply_template(rpc: RpcClient, view_id: int, template_name: str) -> Dict[str, Any]:
    return rpc.call_any(
        ["view.set_view_template", "set_view_template"],
        {"viewId": int(view_id), "templateName": template_name},
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


def crop_plan_to_element(rpc: RpcClient, view_id: int, element_id: int, margin_mm: float) -> Dict[str, Any]:
    return rpc.call_any(
        ["view.crop_plan_view_to_element", "crop_plan_view_to_element"],
        {
            "viewId": int(view_id),
            "elementId": int(element_id),
            "marginMm": float(margin_mm),
            "cropActive": True,
            "cropVisible": False,
        },
    )


def set_view_scale(rpc: RpcClient, view_id: int, scale: int) -> Dict[str, Any]:
    for p in (
        {"viewId": view_id, "paramName": "ビュー スケール", "value": int(scale), "detachViewTemplate": True},
        {"viewId": view_id, "paramName": "View Scale", "value": int(scale), "detachViewTemplate": True},
    ):
        r = rpc.call_any(["view.set_view_parameter", "set_view_parameter"], p)
        if isinstance(r, dict) and r.get("ok"):
            return r
    return {"ok": False, "msg": "set scale failed"}


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
        {"viewId": int(view_id), "bothVisible": False, "detachViewTemplate": True},
    )


def hide_other_columns(rpc: RpcClient, view_id: int, all_column_ids: List[int], target_column_id: int) -> Dict[str, Any]:
    others = [x for x in all_column_ids if x != target_column_id]
    if not others:
        return {"ok": True, "count": 0}
    return rpc.call_any(
        ["view.hide_elements_in_view", "hide_elements_in_view"],
        {
            "viewId": int(view_id),
            "elementIds": others,
            "detachViewTemplate": True,
            "refreshView": True,
            "batchSize": 800,
            "maxMillisPerTx": 4000,
            "failureHandling": {"enabled": True, "mode": "rollback", "confirmProceed": True},
        },
    )


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
    bb = rpc.call_any(["element.get_bounding_box", "get_bounding_box"], {"elementId": int(element_id)})
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


def choose_nearest_grid_pair(grids: List[Dict[str, Any]], cx: float, cy: float) -> Optional[Tuple[str, str]]:
    best = None
    best_d = None
    for i in range(len(grids)):
        for j in range(i + 1, len(grids)):
            ip = line_intersection_2d(grids[i], grids[j])
            if ip is None:
                continue
            d = math.hypot(ip[0] - cx, ip[1] - cy)
            if best is None or d < best_d:
                best = (grids[i]["name"], grids[j]["name"])
                best_d = d
    return best


def place_on_sheet(rpc: RpcClient, sheet_id: int, view_id: int) -> Dict[str, Any]:
    return rpc.call_any(
        ["sheet.place_view", "place_view_on_sheet"],
        {"sheetId": int(sheet_id), "viewId": int(view_id), "centerOnSheet": True},
    )


def align_to_grid_base(
    rpc: RpcClient,
    viewport_id: int,
    view_id: int,
    grid_base_viewport_id: int,
    grid_a: str,
    grid_b: str,
) -> Dict[str, Any]:
    return rpc.call_any(
        ["sheet.replace_view", "replace_view_on_sheet"],
        {
            "viewportId": int(viewport_id),
            "newViewId": int(view_id),
            "keepLocation": True,
            "copyScale": False,
            "alignByGridIntersection": {
                "enabled": True,
                "referenceViewportId": int(grid_base_viewport_id),
                "gridA": grid_a,
                "gridB": grid_b,
            },
        },
    )


def main() -> int:
    rpc = RpcClient(PORT)

    src = resolve_source_view(rpc)
    source_view_id = int(src.get("viewId") or 0)
    source_view_name = str(src.get("name") or "")
    if source_view_id <= 0:
        raise RuntimeError("source view not resolved")

    sh = find_target_sheet(rpc)
    sheet_id = int(sh["sheetId"])
    sheet_name = sh["sheetName"]

    inspected = inspect_sheet(rpc, sheet_id)
    viewports = list(inspected.get("viewports") or [])
    grid_vp, existing_col_ids, prefix = parse_existing_from_viewports(viewports)
    if not grid_vp:
        raise RuntimeError("対象シート上に GRID_BASE ビューポートが見つかりません。")

    grid_base_viewport_id = int(grid_vp.get("viewportId") or 0)
    if grid_base_viewport_id <= 0:
        raise RuntimeError("GRID_BASE viewportId が取得できません。")

    all_col_ids = collect_columns_in_view(rpc, source_view_id)
    remaining = [x for x in all_col_ids if x not in set(existing_col_ids)]
    targets = remaining[:TARGET_APPEND_COUNT]
    if not targets:
        print(json.dumps({
            "ok": True,
            "msg": "未作成の柱がありません。",
            "sourceViewId": source_view_id,
            "sourceViewName": source_view_name,
            "sheetId": sheet_id,
            "sheetName": sheet_name,
            "existingCount": len(existing_col_ids),
            "remainingCount": len(remaining),
        }, ensure_ascii=False, indent=2))
        return 0

    grids = collect_grids(rpc)
    if len(grids) < 2:
        raise RuntimeError("グリッドが不足しています。")

    col_template_exists = template_exists(rpc, COLUMN_TEMPLATE_NAME)
    created: List[Dict[str, Any]] = []

    for cid in targets:
        row: Dict[str, Any] = {"columnId": cid, "ok": True}
        try:
            desired_name = f"{prefix}_COL_{cid}"
            dup = duplicate_view(rpc, source_view_id, desired_name)
            view_id = int(dup.get("viewId") or dup.get("elementId") or 0)
            if view_id <= 0:
                raise RuntimeError("duplicate_view failed")
            row["viewId"] = view_id
            row["viewName"] = desired_name

            if col_template_exists:
                row["template"] = try_apply_template(rpc, view_id, COLUMN_TEMPLATE_NAME)

            row["crop"] = crop_plan_to_element(rpc, view_id, cid, COLUMN_MARGIN_MM)
            row["scale"] = set_view_scale(rpc, view_id, COLUMN_SCALE)
            row["hideOthers"] = hide_other_columns(rpc, view_id, all_col_ids, cid)
            row["gridSegments"] = set_grid_segments_around_column(rpc, view_id, cid, GRID_HALF_LENGTH_MM)
            if HIDE_GRID_BUBBLES:
                row["gridBubbles"] = set_grid_bubbles_hidden(rpc, view_id)

            p = place_on_sheet(rpc, sheet_id, view_id)
            vp_id = int(p.get("viewportId") or 0)
            row["placement"] = p
            row["viewportId"] = vp_id

            cx, cy = get_column_center_mm(rpc, cid)
            anchor = choose_nearest_grid_pair(grids, cx, cy)
            if anchor and vp_id > 0:
                row["align"] = align_to_grid_base(
                    rpc, vp_id, view_id, grid_base_viewport_id, anchor[0], anchor[1]
                )
                row["anchor"] = {"gridA": anchor[0], "gridB": anchor[1]}
        except Exception as ex:
            row["ok"] = False
            row["msg"] = str(ex)
        created.append(row)

    # 寸法標準を再適用（既存柱ビューをsourceとして使用）
    source_dim_view_id = 0
    for vp in viewports:
        m = re.search(r"_COL_(\d+)", str(vp.get("viewName") or ""))
        if m:
            source_dim_view_id = int(vp.get("viewId") or 0)
            if source_dim_view_id > 0:
                break

    dim_apply = None
    if source_dim_view_id > 0:
        dim_apply = rpc.call_any(
            ["view.apply_column_grid_dimension_standard_to_views", "apply_column_grid_dimension_standard_to_views"],
            {
                "sourceViewId": source_dim_view_id,
                "targetViewNameRegex": rf"^{re.escape(prefix)}_COL_(\d+)$",
                "replaceExisting": False,
                "includeSourceView": False,
                "offsetFromColumnFaceMm": DIM_OFFSET_FROM_COLUMN_FACE_MM,
                "secondTierGapMm": DIM_SECOND_TIER_GAP_MM,
            },
        )

    ok_count = len([x for x in created if x.get("ok")])
    print(json.dumps({
        "ok": True,
        "sourceViewId": source_view_id,
        "sourceViewName": source_view_name,
        "sheetId": sheet_id,
        "sheetName": sheet_name,
        "prefix": prefix,
        "existingOnSheet": len(existing_col_ids),
        "sourceColumnsTotal": len(all_col_ids),
        "remainingBeforeAppend": len(remaining),
        "requestedAppendCount": TARGET_APPEND_COUNT,
        "appended": ok_count,
        "appendedColumnIds": [x["columnId"] for x in created if x.get("ok")],
        "failed": [x for x in created if not x.get("ok")],
        "dimensionApply": dim_apply,
    }, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
