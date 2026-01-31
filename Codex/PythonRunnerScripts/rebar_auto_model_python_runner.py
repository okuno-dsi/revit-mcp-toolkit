# -*- coding: utf-8 -*-
# @feature: rebar auto model python runner | keywords: 鉄筋, 柱, 梁, タグ
"""
Python Runner 用：選択した構造柱/構造フレームを自動配筋（RUG/SIRBIM 両対応）

やること
- 現在選択中の要素を取得し、elementId / category / typeName を表示
- 構造柱(OST_StructuralColumns)・構造フレーム(OST_StructuralFraming) をホストとして、
  `rebar_regenerate_delete_recreate` を実行して鉄筋を作成（タグ付き鉄筋は再生成）

注意（重要）
- 被り厚さが「未確定」または「最小値未満」の場合、デフォルトでは止まります
  （COVER_CONFIRMATION_REQUIRED）。その場合は
  `COVER_CONFIRM_PROCEED = True` にして再実行してください。
"""

import json
import os
import sys
import time
import urllib.error
import urllib.request
from typing import Any, Dict, List, Optional, Tuple


# ========= User settings =========
PORT = int(os.environ.get("REVIT_MCP_PORT", "5210"))

# 選択が取れない時などに、ID直指定したい場合に使う（空なら選択を使う）
HOST_ELEMENT_IDS: List[int] = []  # e.g. [110463, 111855]

TAG = "RevitMcp:AutoRebar"
DELETE_MODE = "tagged_only"  # 現状は tagged_only のみ対応

# 被り厚さポリシー（安全側：デフォルトは止める）
COVER_CONFIRM_ENABLED = True
COVER_CONFIRM_PROCEED = False  # ← COVER_CONFIRMATION_REQUIRED のとき True にして再実行
COVER_MIN_MM = 40.0
COVER_CLAMP_TO_MIN = True

# モデルによっては「かぶり厚-上/下/左/右」が別名のことがあります。
# 必要なら候補名を追記してください（空なら既定の候補で探索します）。
COVER_PARAM_NAMES = {
    # "up": ["かぶり厚-上"],
    # "down": ["かぶり厚-下"],
    # "left": ["かぶり厚-左"],
    # "right": ["かぶり厚-右"],
}

# True にすると、COVER_CONFIRMATION_REQUIRED を検知した場合に自動で再実行します（非推奨）
AUTO_RERUN_ON_COVER_CONFIRM = False


# ========= HTTP/JSON-RPC =========
BASE_URL = f"http://127.0.0.1:{PORT}"
_RPC_CANDIDATES = [f"{BASE_URL}/rpc", f"{BASE_URL}/jsonrpc"]
_RPC_URL = _RPC_CANDIDATES[0]


def _json_dumps(obj: Any) -> str:
    return json.dumps(obj, ensure_ascii=False, indent=2)


def _post_json(url: str, payload: Dict[str, Any], timeout_sec: float = 30.0) -> Dict[str, Any]:
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=data,
        headers={
            "Content-Type": "application/json",
            "Accept": "application/json",
        },
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=timeout_sec) as resp:
        body = resp.read().decode("utf-8")
        return json.loads(body) if body else {}


def _get_json(url: str, timeout_sec: float = 30.0) -> Dict[str, Any]:
    req = urllib.request.Request(url, headers={"Accept": "application/json"}, method="GET")
    with urllib.request.urlopen(req, timeout=timeout_sec) as resp:
        body = resp.read().decode("utf-8")
        return json.loads(body) if body else {}


def _unwrap_jsonrpc(obj: Any) -> Any:
    """
    RevitMCP はレスポンスが多段で返ることがあるため、
    JSON-RPC の envelope (jsonrpc/id/method/agentId/result) を安全に剥がす。

    例:
      {"jsonrpc":"2.0","id":1,"result":{"ok":true,...}}
      {"jsonrpc":"2.0","id":1,"result":{"jsonrpc":"2.0","id":1,"method":"x","result":{"ok":true,...}}}
    """
    cur: Any = obj
    while isinstance(cur, dict) and isinstance(cur.get("result"), dict):
        looks_like_rpc = (cur.get("jsonrpc") == "2.0") or ("method" in cur and "id" in cur)
        if not looks_like_rpc:
            break
        cur = cur["result"]
    return cur


def _poll_job(job_id: str, timeout_sec: float = 120.0) -> Dict[str, Any]:
    deadline = time.time() + timeout_sec
    job_url = f"{BASE_URL}/job/{job_id}"
    last: Optional[Dict[str, Any]] = None
    while time.time() < deadline:
        try:
            row = _get_json(job_url, timeout_sec=15.0)
            last = row
        except urllib.error.HTTPError as e:
            # 202/204 は待機を意味する実装がある
            if e.code in (202, 204):
                time.sleep(0.5)
                continue
            raise

        state = str(row.get("state") or "").upper()
        if state == "SUCCEEDED":
            result_json = row.get("result_json")
            if isinstance(result_json, str) and result_json.strip():
                try:
                    parsed = json.loads(result_json)
                    parsed = _unwrap_jsonrpc(parsed)
                    return parsed if isinstance(parsed, dict) else {"ok": True, "result": parsed}
                except Exception:
                    return {"ok": True, "result_json": result_json}
            return {"ok": True}
        if state in ("FAILED", "TIMEOUT", "DEAD"):
            raise RuntimeError(str(row.get("error_msg") or state))

        time.sleep(0.5)

    raise TimeoutError(f"job polling timed out (jobId={job_id}) last={_json_dumps(last)}")


def rpc(method: str, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    global _RPC_URL

    payload = {
        "jsonrpc": "2.0",
        "id": f"py-{int(time.time() * 1000)}",
        "method": method,
        "params": params or {},
    }

    for i in range(2):
        try:
            env = _post_json(_RPC_URL, payload)
        except urllib.error.HTTPError as e:
            if e.code == 404 and i == 0:
                _RPC_URL = _RPC_CANDIDATES[1]
                continue
            raise

        if isinstance(env, dict) and "error" in env:
            raise RuntimeError(_json_dumps(env["error"]))

        result = _unwrap_jsonrpc(env)
        if isinstance(result, dict) and result.get("queued"):
            job_id = result.get("jobId") or result.get("job_id")
            if not job_id:
                raise RuntimeError("queued=true but jobId missing: " + _json_dumps(result))
            return _poll_job(str(job_id))

        return result if isinstance(result, dict) else {"ok": True, "result": result}

    raise RuntimeError("RPC endpoint detection failed.")


# ========= Rebar helper =========
def _pick_host_ids(selection_ids: List[int], infos: List[Dict[str, Any]]) -> List[int]:
    # rebar側でもフィルタされるが、ここでは「ホストっぽいもの」に絞って見やすくする。
    out: List[int] = []
    for it in infos:
        try:
            eid = int(it.get("elementId") or 0)
        except Exception:
            continue
        if eid <= 0:
            continue
        cat = str(it.get("category") or "")
        if ("構造柱" in cat) or ("構造フレーム" in cat) or ("Structural" in cat):
            out.append(eid)
    if out:
        return out
    # 何も絞れなければ selection をそのまま（rebar側で弾く）
    return selection_ids


def _print_cover_debug(plan: Dict[str, Any]) -> None:
    hosts = plan.get("hosts") or []
    if not isinstance(hosts, list):
        return
    for h in hosts:
        if not isinstance(h, dict):
            continue
        if str(h.get("code") or "") != "COVER_CONFIRMATION_REQUIRED":
            continue
        hid = h.get("hostElementId")
        raw = h.get("coverFacesMmRaw") or {}
        proposed = h.get("coverFacesMmProposed") or {}
        print(f"- host {hid}: COVER_CONFIRMATION_REQUIRED")
        print(f"  raw     : {raw}")
        print(f"  proposed: {proposed}")
        cand = h.get("coverCandidateParamNames")
        if cand is not None:
            print(f"  candidates: {cand}")


def main() -> int:
    t0 = time.time()
    try:
        if HOST_ELEMENT_IDS:
            selection_ids = HOST_ELEMENT_IDS[:]
        else:
            # UI 操作直後は selection の反映が僅かに遅れることがあるため、短いリトライを入れる
            sel = rpc(
                "get_selected_element_ids",
                {"fallbackToStash": True, "maxAgeMs": 5000, "retry": {"maxWaitMs": 1500, "pollMs": 150}},
            )
            selection_ids = [int(x) for x in (sel.get("elementIds") or []) if int(x) > 0]

        if not selection_ids:
            print(_json_dumps({"ok": False, "code": "NO_SELECTION", "msg": "構造柱/構造フレームを選択してください。"}))
            return 1

        info = rpc("get_element_info", {"elementIds": selection_ids, "rich": False})
        elems = info.get("elements") or []
        rows: List[Dict[str, Any]] = []
        for e in elems:
            if not isinstance(e, dict):
                continue
            rows.append(
                {
                    "elementId": e.get("elementId"),
                    "category": e.get("category"),
                    "typeName": e.get("typeName"),
                }
            )

        print("=== Targets ===")
        for r in rows:
            print(f"- {r.get('elementId')} / {r.get('category')} / {r.get('typeName')}")

        host_ids = _pick_host_ids(selection_ids, rows)
        if not host_ids:
            print(_json_dumps({"ok": False, "code": "NO_HOSTS", "msg": "ホスト（柱/梁）が見つかりません。"}))
            return 1

        options: Dict[str, Any] = {
            "tagComments": TAG,
            "coverConfirmEnabled": bool(COVER_CONFIRM_ENABLED),
            "coverConfirmProceed": bool(COVER_CONFIRM_PROCEED),
            "coverMinMm": float(COVER_MIN_MM),
            "coverClampToMin": bool(COVER_CLAMP_TO_MIN),
        }
        if COVER_PARAM_NAMES:
            options["coverParamNames"] = COVER_PARAM_NAMES

        params: Dict[str, Any] = {
            "useSelectionIfEmpty": False,
            "hostElementIds": host_ids,
            "tag": TAG,
            "deleteMode": DELETE_MODE,
            "options": options,
        }

        res = rpc("rebar_regenerate_delete_recreate", params)

        if not bool(res.get("ok")):
            print("")
            print("=== Error ===")
            print(_json_dumps(res))
            code = str(res.get("code") or res.get("errorCode") or "")
            if code == "COVER_CONFIRMATION_REQUIRED":
                print("")
                print("=== Cover confirmation required ===")
                print("被り厚さが最小値未満等のため停止しました。")
                print("続行する場合は、COVER_CONFIRM_PROCEED = True にして再実行してください。")
                _print_cover_debug(res)
                if AUTO_RERUN_ON_COVER_CONFIRM and not COVER_CONFIRM_PROCEED:
                    print("")
                    print("AUTO_RERUN_ON_COVER_CONFIRM=true のため、確認済みとして再実行します。")
                    params["options"]["coverConfirmProceed"] = True
                    res2 = rpc("rebar_regenerate_delete_recreate", params)
                    print("")
                    print("=== Rerun result ===")
                    print(_json_dumps(res2))
                    return 0 if bool(res2.get("ok")) else 2
            return 2

        # success
        results = res.get("results") or []
        created = []
        deleted = []
        for r in results:
            if not isinstance(r, dict):
                continue
            for x in (r.get("createdRebarIds") or []):
                try:
                    v = int(x)
                    if v > 0:
                        created.append(v)
                except Exception:
                    pass
            for x in (r.get("deletedRebarIds") or []):
                try:
                    v = int(x)
                    if v > 0:
                        deleted.append(v)
                except Exception:
                    pass

        created = sorted(set(created))
        deleted = sorted(set(deleted))
        print("")
        print("=== OK ===")
        print(_json_dumps({"ok": True, "createdRebarCount": len(created), "deletedRebarCount": len(deleted)}))
        print(f"elapsedSec={time.time() - t0:.2f}")
        return 0

    except Exception as ex:
        print(_json_dumps({"ok": False, "code": "EXCEPTION", "msg": str(ex)}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
