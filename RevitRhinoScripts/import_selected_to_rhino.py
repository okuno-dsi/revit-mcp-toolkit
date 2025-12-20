#!/usr/bin/env python3
import argparse, json, time, sys
from typing import Any, Dict, Optional
import requests

HEADERS = {"Content-Type": "application/json; charset=utf-8", "Accept": "application/json"}

def post_json(url: str, body: Dict[str, Any], timeout=(5, 60)) -> Dict[str, Any]:
    r = requests.post(url, data=json.dumps(body), headers=HEADERS, timeout=timeout)
    r.raise_for_status()
    return r.json()

def send_revit(port: int, method: str, params: Optional[Dict[str, Any]] = None, wait_s: float = 60.0) -> Dict[str, Any]:
    if params is None:
        params = {}
    base = f"http://127.0.0.1:{port}"
    call = {"jsonrpc":"2.0","id":int(time.time()*1000),"method":method,"params":params}
    requests.post(base + "/enqueue?force=1", data=json.dumps(call), headers=HEADERS, timeout=(5,60)).raise_for_status()
    t0 = time.time()
    while True:
        gr = requests.get(base + "/get_result", timeout=(5,60))
        if gr.status_code in (202,204):
            if time.time() - t0 > wait_s:
                raise TimeoutError(f"Timed out waiting for {method}")
            time.sleep(0.2)
            continue
        return gr.json()

def get_result_leaf(obj: Dict[str, Any]) -> Dict[str, Any]:
    cur = obj
    for _ in range(4):
        if isinstance(cur, dict) and "result" in cur and isinstance(cur["result"], dict):
            cur = cur["result"]
        else:
            break
    return cur

def rhino_server_rpc(base: str, method: str, params: Dict[str, Any]) -> Dict[str, Any]:
    url = base.rstrip('/') + "/rpc"
    body = {"jsonrpc":"2.0","id":int(time.time()*1000),"method":method,"params":params}
    return post_json(url, body)

def plugin_ipc_snapshot(plugin_url: str, snapshot: Dict[str, Any]) -> Dict[str, Any]:
    url = plugin_url.rstrip('/') + "/rpc"
    body = {"jsonrpc":"2.0","id":int(time.time()*1000),"method":"rhino_import_snapshot","params":snapshot}
    return post_json(url, body)

def mm_to_ft(v: float) -> float:
    return v / 304.8

def main():
    ap = argparse.ArgumentParser(description="Mirror Revit selection to Rhino via RhinoMCP, with fallbacks")
    ap.add_argument("--revit-port", type=int, required=True)
    ap.add_argument("--rhino-url", type=str, required=True, help="RhinoMcpServer base URL, e.g. http://127.0.0.1:5200")
    ap.add_argument("--plugin-url", type=str, default="http://127.0.0.1:5201", help="Rhino plugin IPC URL")
    args = ap.parse_args()

    sel = send_revit(args.revit_port, "get_selected_element_ids", {})
    leaf = get_result_leaf(sel)
    ids = leaf.get("elementIds") or []
    if not ids:
        print(json.dumps({"ok": True, "msg": "No selection in Revit"}, ensure_ascii=False))
        return

    info = send_revit(args.revit_port, "get_element_info", {"elementIds": ids, "rich": True})
    info_leaf = get_result_leaf(info)
    elements = info_leaf.get("elements") or []
    uids = [e.get("uniqueId") for e in elements if e.get("uniqueId")]
    if not uids:
        print(json.dumps({"ok": False, "error": "Could not resolve UniqueIds"}, ensure_ascii=False))
        sys.exit(1)

    # Try server path first. Treat any ok:true as success to avoid double import.
    try:
        res = rhino_server_rpc(
            args.rhino_url,
            "rhino_import_by_ids",
            {"uniqueIds": uids, "revitBaseUrl": f"http://127.0.0.1:{args.revit_port}"}
        )
        res_leaf = get_result_leaf(res)
        if res_leaf.get("ok") is True:
            # Do not fall back if server reports ok, regardless of imported count.
            print(json.dumps({"ok": True, "path": "server", "result": res_leaf}, ensure_ascii=False))
            return
    except Exception:
        # Proceed to fallback only on transport/protocol failure
        pass

    imported = 0; errors = 0
    for uid in uids:
        try:
            geom = send_revit(args.revit_port, "get_instance_geometry", {"uniqueId": uid})
            gleaf = get_result_leaf(geom)
            if not gleaf.get("ok"):
                raise RuntimeError("get_instance_geometry not ok")
            snap = gleaf
            subs = snap.get("submeshes") or []
            for sm in subs:
                if "intIndices" not in sm and "indices" in sm:
                    sm["intIndices"] = sm["indices"]
            # Ensure stable uniqueId to prevent duplicate blocks on re-run
            if isinstance(snap, dict) and "uniqueId" in snap and isinstance(snap["uniqueId"], str):
                snap["uniqueId"] = snap["uniqueId"]
            out = plugin_ipc_snapshot(args.plugin_url, snap)
            if out.get("result",{}).get("ok"):
                imported += 1
            else:
                errors += 1
        except Exception:
            # final fallback: bbox proxy
            try:
                info_one = send_revit(args.revit_port, "get_element_info", {"uniqueIds": [uid], "rich": True})
                e_leaf = get_result_leaf(info_one)
                arr = e_leaf.get("elements") or []
                if not arr:
                    errors += 1
                    continue
                el = arr[0]
                bbox = el.get("bboxMm") or {}
                mn = bbox.get("min"); mx = bbox.get("max")
                if not mn or not mx:
                    errors += 1
                    continue
                minx, miny, minz = mn["x"]/304.8, mn["y"]/304.8, mn["z"]/304.8
                maxx, maxy, maxz = mx["x"]/304.8, mx["y"]/304.8, mx["z"]/304.8
                verts = [
                    [minx,miny,minz],[maxx,miny,minz],[maxx,maxy,minz],[minx,maxy,minz],
                    [minx,miny,maxz],[maxx,miny,maxz],[maxx,maxy,maxz],[minx,maxy,maxz]
                ]
                idx = [0,1,2,0,2,3, 4,5,6,4,6,7, 0,1,5,0,5,4, 1,2,6,1,6,5, 2,3,7,2,7,6, 3,0,4,3,4,7]
                # Use stable uniqueId (without timestamp) so repeated runs update instead of duplicating
                bbox_snap = {
                    "uniqueId": uid,
                    "units": "feet",
                    "vertices": verts,
                    "submeshes": [{"materialKey": "bbox", "intIndices": idx}],
                    "snapshotStamp": time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())
                }
                out = plugin_ipc_snapshot(args.plugin_url, bbox_snap)
                if out.get("result",{}).get("ok"):
                    imported += 1
                else:
                    errors += 1
            except Exception:
                errors += 1

    print(json.dumps({"ok": errors == 0, "path": "fallback", "imported": imported, "errors": errors}, ensure_ascii=False))

if __name__ == "__main__":
    main()
