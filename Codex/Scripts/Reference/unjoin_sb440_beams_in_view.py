import json
from typing import Any, Dict, List

from send_revit_command_durable import send_request, RevitMcpError


PORT = 5210


def _unwrap(result: Dict[str, Any]) -> Dict[str, Any]:
    """Unwrap {"jsonrpc","id","result":{...,"result":{...}}} to inner result dict."""
    return (result.get("result") or {}).get("result") or {}


def main() -> None:
    try:
        # 1) 現在のビュー ID を取得
        res_view = send_request(PORT, "get_current_view", {})
        view_payload = _unwrap(res_view)
        if not view_payload.get("ok"):
            raise RevitMcpError("get_current_view", f"result not ok: {view_payload!r}")
        view_id = int(view_payload["viewId"])

        # 2) ビュー内の全モデル要素を取得
        res_elems = send_request(
            PORT,
            "get_elements_in_view",
            {
                "viewId": view_id,
                "_shape": {"page": {"limit": 10000}},
                "_filter": {"modelOnly": True},
            },
        )
        elems_payload = _unwrap(res_elems)
        if not elems_payload.get("ok"):
            raise RevitMcpError("get_elements_in_view", f"result not ok: {elems_payload!r}")
        rows = elems_payload.get("rows") or []
        element_ids: List[int] = [int(r["elementId"]) for r in rows if r.get("elementId")]

        if not element_ids:
            print(json.dumps({"ok": False, "msg": "No elements in current view."}, ensure_ascii=False))
            return

        # 3) 各要素の情報から「タイプ名が SB440」の梁だけ抽出
        res_info = send_request(
            PORT,
            "get_element_info",
            {
                "elementIds": element_ids,
                "rich": False,
            },
        )
        info_payload = _unwrap(res_info)
        if not info_payload.get("ok"):
            raise RevitMcpError("get_element_info", f"result not ok: {info_payload!r}")

        candidates: List[int] = []
        for e in info_payload.get("elements", []):
            try:
                if e.get("typeName") == "SB440":
                    cat = (e.get("category") or "").lower()
                    # 構造フレーム系だけをざっくりフィルタ
                    if "structural" in cat or "構造" in cat or "梁" in cat:
                        candidates.append(int(e["elementId"]))
            except Exception:
                continue

        if not candidates:
            print(
                json.dumps(
                    {"ok": True, "msg": "No SB440 beams found in current view.", "unjoinedPairs": []},
                    ensure_ascii=False,
                )
            )
            return

        # 4) 各 SB440 梁について get_joined_elements → unjoin_elements でジオメトリ結合を解除
        unjoined_pairs: List[Dict[str, int]] = []
        for eid in candidates:
            res_join = send_request(PORT, "get_joined_elements", {"elementId": eid})
            join_payload = _unwrap(res_join)
            if not join_payload.get("ok"):
                continue
            joined_ids = join_payload.get("joinedIds") or []
            for jid in joined_ids:
                try:
                    send_request(
                        PORT,
                        "unjoin_elements",
                        {"elementIdA": eid, "elementIdB": int(jid)},
                    )
                    unjoined_pairs.append({"a": int(eid), "b": int(jid)})
                except RevitMcpError:
                    # 個別の失敗は無視して続行
                    continue

        # 5) SB440 梁について、両端の DisallowJoinAtEnd(0,1) を一括設定
        disallow_result: Dict[str, Any] = {}
        try:
            disallow_result = _unwrap(
                send_request(
                    PORT,
                    "disallow_structural_frame_join_at_end",
                    {"elementIds": candidates, "ends": [0, 1]},
                )
            )
        except RevitMcpError as e2:
            disallow_result = {"ok": False, "error": str(e2), "where": e2.where}

        print(
            json.dumps(
                {
                    "ok": True,
                    "targetCount": len(candidates),
                    "unjoinedCount": len(unjoined_pairs),
                    "unjoinedPairs": unjoined_pairs,
                    "disallowJoinAtEnd": disallow_result,
                },
                ensure_ascii=False,
                indent=2,
            )
        )
    except RevitMcpError as e:
        print(
            json.dumps(
                {
                    "ok": False,
                    "where": e.where,
                    "error": str(e),
                    "payload": e.payload,
                },
                ensure_ascii=False,
                indent=2,
            )
        )


if __name__ == "__main__":
    main()

