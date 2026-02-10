# @feature: ブレース配置（選択した構造フレームのグリッド）
# @keywords: ブレース, 構造フレーム, グリッド
# -*- coding: utf-8 -*-
"""
選択中の構造フレーム要素をグリッドとしてブレース配置UIを起動する。
- 選択要素のレベルを基準レベルに採用（ビュー名は使わない）
- ブレースタイプは typeName に "L" を含むものに限定
"""
import json
import time
import statistics
import requests

DEFAULT_PORT = 5210
DEFAULT_TIMEOUT = 30
POLL_INTERVAL = 0.5
POLL_TIMEOUT = 60


def detect_rpc_endpoint(base_url: str) -> str:
    for ep in ("/rpc", "/jsonrpc"):
        url = f"{base_url}{ep}"
        try:
            requests.post(url, json={"jsonrpc": "2.0", "id": "ping", "method": "noop"}, timeout=3)
            return url
        except Exception:
            continue
    return f"{base_url}/rpc"


def unwrap_result(obj):
    if not isinstance(obj, dict):
        return obj
    if "result" in obj and isinstance(obj["result"], dict):
        inner = obj["result"]
        if "result" in inner and isinstance(inner["result"], dict):
            return inner["result"]
        return inner
    return obj


def poll_job(base_url: str, job_id: str, timeout_sec: int = POLL_TIMEOUT):
    deadline = time.time() + timeout_sec
    job_url = f"{base_url}/job/{job_id}"
    while time.time() < deadline:
        r = requests.get(job_url, timeout=10)
        if r.status_code in (202, 204):
            time.sleep(POLL_INTERVAL)
            continue
        r.raise_for_status()
        job = r.json()
        state = (job.get("state") or "").upper()
        if state == "SUCCEEDED":
            result_json = job.get("result_json")
            return unwrap_result(json.loads(result_json)) if result_json else {"ok": True}
        if state in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(job.get("error_msg") or state)
        time.sleep(POLL_INTERVAL)
    raise TimeoutError(f"job polling timed out (jobId={job_id})")


def rpc(base_url: str, method: str, params=None):
    endpoint = detect_rpc_endpoint(base_url)
    payload = {
        "jsonrpc": "2.0",
        "id": f"req-{int(time.time()*1000)}",
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
        return poll_job(base_url, job_id)
    return unwrap_result(result)


def main():
    base_url = f"http://127.0.0.1:{DEFAULT_PORT}"

    # 1) selection
    sel = rpc(base_url, "element.get_selected_element_ids", {})
    ids = sel.get("elementIds") if isinstance(sel, dict) else []
    if not ids:
        print("選択要素がありません。構造フレームを選択してください。")
        return

    # 2) element info (bulk)
    info = rpc(base_url, "element.get_element_info", {
        "elementIds": ids,
        "failureHandling": {"enabled": True, "mode": "rollback"}
    })

    elements = info.get("elements", []) if isinstance(info, dict) else []
    frames = [e for e in elements if (e.get("category") or "").find("構造フレーム") >= 0 or (e.get("category") or "").lower().find("structural") >= 0]
    if not frames:
        print("選択内に構造フレームが見つかりません。")
        return

    frame_ids = [e["elementId"] for e in frames if e.get("elementId")]

    # 3) decide level from selected frames
    level_names = [e.get("level") for e in frames if e.get("level")]
    level_name = None
    if level_names:
        # most common
        level_name = max(set(level_names), key=level_names.count)
    else:
        # fallback: nearest level by Z (meters) to median Z (meters)
        z_vals_mm = [e.get("coordinatesMm", {}).get("z") for e in frames if e.get("coordinatesMm")]
        z_vals_mm = [z for z in z_vals_mm if isinstance(z, (int, float))]
        if z_vals_mm:
            z_med_m = statistics.median(z_vals_mm) / 1000.0
            levels = rpc(base_url, "element.list_levels_simple", {})
            items = levels.get("items", []) if isinstance(levels, dict) else []
            if items:
                level_name = min(items, key=lambda lv: abs((lv.get("elevation") or 0) - z_med_m)).get("name")

    if not level_name:
        print("レベルが特定できませんでした。選択要素のレベルを確認してください。")
        return

    # 4) invoke brace UI
    params = {
        "levelName": level_name,
        "gridSource": "selection",
        "gridLabelMode": "coord",
        "gridElementIds": frame_ids,
        "gridSnapTolMm": 200,\n        "markContains": ["G","G","B"],\n        "braceTypeFamilyContains": ["ﾌﾞﾚｰｽ"]
    }

    result = rpc(base_url, "element.place_roof_brace_from_prompt", params)
    print(json.dumps({
        "ok": True,
        "levelName": level_name,
        "selectedFrames": len(frame_ids),
        "result": result
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()


