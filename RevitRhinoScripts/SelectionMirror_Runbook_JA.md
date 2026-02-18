# Revit 選択 → Rhino 再現（Selection Mirror）実行ランブック（JA）

この文書は、別のAIエージェントでも同じ手順を再現できるように、準備、実行コマンド、Pythonスクリプト、結果、課題、次アクションを一つにまとめたものです。

## 目的と概要
- 目的: Revit で選択したオブジェクトを Rhino 上に再現（可視化）する。
- 構成:
  - Revit MCP（JSON-RPCブリッジ）: 例 `http://127.0.0.1:5210`
  - RhinoMcpServer（JSON-RPC）: 例 `http://127.0.0.1:5200`
  - RhinoMcpPlugin（Rhino内IPC）: `http://127.0.0.1:5201`

## 前提条件
- Windows + PowerShell + Python 3 が利用可能。
- Revit 起動済み、MCPアドインが稼働（`/enqueue` と `/get_result` が応答）。
- Rhino 7 起動済み、RhinoMcpPlugin が読み込み済み（`http://127.0.0.1:5201/rpc` 応答）。
- RhinoMcpServer を起動（`scripts/start_server.ps1` or 手動 `dotnet`）。

## Rhino プラグイン登録（RhinoMcpPlugin.rhp）
1) ビルド
- Visual Studio 2022 でソリューションを開き、`RhinoMCP/RhinoMcpPlugin/RhinoMcpPlugin.csproj` を Debug もしくは Release でビルド。
- PostBuild で `.rhp` が生成されます。出力先の例:
  - `RhinoMCP\RhinoMcpPlugin\bin\Debug\RhinoMcpPlugin.rhp`
  - `RhinoMCP\RhinoMcpPlugin\bin\Release\RhinoMcpPlugin.rhp`

2) Rhino への登録
- 方法A（ドラッグ&ドロップ）: Rhino を起動し、生成された `RhinoMcpPlugin.rhp` を Rhino ウィンドウへドラッグ。
- 方法B（プラグインマネージャ）:
  - Rhino メニュー → ツール → オプション → プラグイン → インストール
  - `RhinoMcpPlugin.rhp` を選択 → 有効化（読み込み）

3) （任意）ツールバーの読み込み
- ツールバー定義: `RhinoMCP/RhinoMcpPlugin/UI/Toolbar.rui`
- Rhino メニュー → ツール → ツールバー レイアウト → 開く から `.rui` を読み込み。

4) プラグインの起動確認（IPC 5201）
- 読み込み後、プラグインは `http://127.0.0.1:5201/rpc` をリッスンします。
```
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://127.0.0.1:5201/rpc -Body '{"jsonrpc":"2.0","id":1,"method":"rhino_get_selection","params":{}}' -ContentType 'application/json; charset=utf-8'
```
- HTTP 200 で JSON が返れば OK。

## クイックスタート（PowerShell）
1) RhinoMcpServer 起動（必要時）
```
pwsh -NoProfile -File .\RhinoMCP\scripts\start_server.ps1 -Url "http://127.0.0.1:5200" -Config Release
```

2) Revit の選択を Rhino に再現（自動フォールバック付き）
```
pwsh -NoProfile -File .\scripts\import_selected_to_rhino.ps1 -RevitPort 5210 -RhinoUrl http://127.0.0.1:5200
```

3) Python で同等動作（本書末尾のスクリプト参照）
```
python .\scripts\import_selected_to_rhino.py --revit-port 5210 --rhino-url http://127.0.0.1:5200
```

## 詳細手順（手動コマンド）
- 疎通確認
```
Test-NetConnection localhost -Port 5210   # Revit MCP
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://127.0.0.1:5200/rpc -Body '{"jsonrpc":"2.0","id":1,"method":"__unknown__","params":{}}' -ContentType 'application/json; charset=utf-8'
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://127.0.0.1:5201/rpc -Body '{"jsonrpc":"2.0","id":1,"method":"rhino_get_selection","params":{}}' -ContentType 'application/json; charset=utf-8'
```

- Revit で選択ID取得 → UniqueId 解決
```
python .\Codex\Scripts\Reference\send_revit_command_durable.py --port 5210 --command get_selected_element_ids --output-file .\selected_ids.json
python .\Codex\Scripts\Reference\send_revit_command_durable.py --port 5210 --command get_element_info --params '{"elementIds":[<ID1>,<ID2>],"rich":true}' --output-file .\selected_info.json
```

- RhinoMCP で取り込み（通常経路）
```
$body = '{"jsonrpc":"2.0","id":1,"method":"rhino_import_by_ids","params":{"uniqueIds":["<UniqueId>"],"revitBaseUrl":"http://127.0.0.1:5210"}}'
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://127.0.0.1:5200/rpc -Body $body -ContentType 'application/json; charset=utf-8'
```

- フォールバック1：Revit からメッシュ取得 → Rhino プラグインへ直送
```
python .\Codex\Scripts\Reference\send_revit_command_durable.py --port 5210 --command get_instance_geometry --params '{"uniqueId":"<UniqueId>"}' --output-file .\geom.json
<geom.json の result をそのまま params として>
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://127.0.0.1:5201/rpc -Body '{"jsonrpc":"2.0","id":1,"method":"rhino_import_snapshot","params":<result>}' -ContentType 'application/json; charset=utf-8'
```

- フォールバック2：BBox 代理ジオメトリ（mm→ft 変換） → Rhino プラグインへ直送
```
python .\Codex\Scripts\Reference\send_revit_command_durable.py --port 5210 --command get_element_info --params '{"uniqueIds":["<UniqueId>"],"rich":true}' --output-file .\info.json
# info.json の bboxMm(min/max) を feet に変換して、vertices + intIndices を組み立て params として rhino_import_snapshot に POST
```

## 実行結果（本環境）
- 選択取得・UniqueId 解決: OK
- Revit `get_instance_geometry`: 実装済み・応答OK
- RhinoMCP `rhino_import_by_ids`: NG（result: ok:false, imported:0, errors:1）
- フォールバック:
  - BBox 直送: 成功（UniqueId にサフィックスで衝突回避）
  - 実メッシュ直送（get_instance_geometry→rhino_import_snapshot）も実行可能（手動検証済）

## 課題（Issues）
- RhinoMcpServer の `rhino_import_by_ids` が失敗。Revit 側からメッシュ取得は成功しているため、サーバ側のハンドラ（`RhinoMcpServer/Rpc/Rhino/ImportByIdsCommand.cs`）もしくはプラグインIPC応答の扱いに課題がある可能性。
- 既存ブロック定義とインスタンス配置の競合時の扱い（同一 UniqueId で再取り込み）が弱く、失敗しやすい。

## 次のアクション（Next Actions）
1) RhinoMcpServer の詳細ログを強化
   - `ImportByIdsCommand` 内で Revit 応答（errors 含む）と Plugin IPC 応答（error/ok）をログ出力。
   - 既にネスト `result.result` 展開と enqueue/get_result 待機、`indices`/`intIndices` 両対応は追加済み。
2) プラグイン側の競合処理
   - 既存ブロック定義がある場合の再利用/置換パスを追加。
3) 安定化
   - リトライ/タイムアウト、名前衝突回避のサフィックス運用、最終エラーメッセージの呼び出し元返却。

## 付録: 自動実行用 Python スクリプト
ファイルにも保存済み: `scripts/import_selected_to_rhino.py`

使用例:
```
python scripts/import_selected_to_rhino.py --revit-port 5210 --rhino-url http://127.0.0.1:5200 --plugin-url http://127.0.0.1:5201
```

コード:
```python
#!/usr/bin/env python3
import argparse, json, time, sys
from typing import Any, Dict, List, Optional
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
    # enqueue
    requests.post(base + "/enqueue?force=1", data=json.dumps(call), headers=HEADERS, timeout=(5,60)).raise_for_status()
    # poll
    t0 = time.time()
    while True:
        gr = requests.get(base + "/get_result", timeout=(5,60))
        if gr.status_code in (202,204):
            if time.time() - t0 > wait_s:
                raise TimeoutError(f"Timed out waiting for {method}")
            time.sleep(0.2)
            continue
        obj = gr.json()
        return obj

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

def bbox_snapshot_from_info(el: Dict[str, Any]) -> Optional[Dict[str, Any]]:
    uid = el.get("uniqueId")
    bbox = el.get("bboxMm")
    if not uid or not bbox:
        return None
    mn = bbox.get("min"); mx = bbox.get("max")
    if not mn or not mx:
        return None
    minx, miny, minz = mm_to_ft(mn["x"]), mm_to_ft(mn["y"]), mm_to_ft(mn["z"])
    maxx, maxy, maxz = mm_to_ft(mx["x"]), mm_to_ft(mx["y"]), mm_to_ft(mx["z"])
    verts = [
        [minx,miny,minz],[maxx,miny,minz],[maxx,maxy,minz],[minx,maxy,minz],
        [minx,miny,maxz],[maxx,miny,maxz],[maxx,maxy,maxz],[minx,maxy,maxz]
    ]
    idx = [0,1,2,0,2,3, 4,5,6,4,6,7, 0,1,5,0,5,4, 1,2,6,1,6,5, 2,3,7,2,7,6, 3,0,4,3,4,7]
    return {
        "uniqueId": uid,
        "units": "feet",
        "vertices": verts,
        "submeshes": [{"materialKey":"bbox","intIndices": idx}],
        "snapshotStamp": time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())
    }

def main():
    ap = argparse.ArgumentParser(description="Mirror Revit selection to Rhino via RhinoMCP, with fallbacks")
    ap.add_argument("--revit-port", type=int, required=True)
    ap.add_argument("--rhino-url", type=str, required=True, help="RhinoMcpServer base URL, e.g. http://127.0.0.1:5200")
    ap.add_argument("--plugin-url", type=str, default="http://127.0.0.1:5201", help="Rhino plugin IPC URL")
    args = ap.parse_args()

    # 1) get selection ids
    sel = send_revit(args.revit_port, "get_selected_element_ids", {})
    leaf = get_result_leaf(sel)
    ids = leaf.get("elementIds") or []
    if not ids:
        print(json.dumps({"ok": True, "msg": "No selection in Revit"}, ensure_ascii=False))
        return
    # 2) resolve uniqueIds
    info = send_revit(args.revit_port, "get_element_info", {"elementIds": ids, "rich": True})
    info_leaf = get_result_leaf(info)
    elements = info_leaf.get("elements") or []
    uids = [e.get("uniqueId") for e in elements if e.get("uniqueId")]
    if not uids:
        print(json.dumps({"ok": False, "error": "Could not resolve UniqueIds"}, ensure_ascii=False))
        sys.exit(1)

    # 3) try rhino_import_by_ids on server
    try:
        res = rhino_server_rpc(args.rhino_url, "rhino_import_by_ids", {"uniqueIds": uids, "revitBaseUrl": f"http://127.0.0.1:{args.revit_port}"})
        res_leaf = get_result_leaf(res)
        if res_leaf.get("ok") and res_leaf.get("imported", 0) > 0:
            print(json.dumps({"ok": True, "path": "server", "result": res_leaf}, ensure_ascii=False))
            return
    except Exception as e:
        # fall through to plugin IPC fallback
        pass

    # 4) fallback A: try full geometry via get_instance_geometry + plugin IPC
    imported = 0; errors = 0
    for uid in uids:
        try:
            geom = send_revit(args.revit_port, "get_instance_geometry", {"uniqueId": uid})
            gleaf = get_result_leaf(geom)
            if not gleaf.get("ok"):
                raise RuntimeError("get_instance_geometry not ok")
            snap = gleaf
            # normalize indices naming for plugin tolerance (intIndices preferred)
            subs = snap.get("submeshes") or []
            for sm in subs:
                if "intIndices" not in sm and "indices" in sm:
                    sm["intIndices"] = sm["indices"]
            out = plugin_ipc_snapshot(args.plugin_url, snap)
            if out.get("result",{}).get("ok"):
                imported += 1
            else:
                errors += 1
        except Exception:
            # 5) fallback B: bbox proxy mesh
            bbox = bbox_snapshot_from_info(next((e for e in elements if e.get("uniqueId") == uid), {}))
            if not bbox:
                errors += 1
                continue
            # avoid name collision
            bbox["uniqueId"] = f"{uid}-bbox-{int(time.time()*1000)}"
            try:
                out = plugin_ipc_snapshot(args.plugin_url, bbox)
                if out.get("result",{}).get("ok"):
                    imported += 1
                else:
                    errors += 1
            except Exception:
                errors += 1

    print(json.dumps({"ok": errors == 0, "path": "fallback", "imported": imported, "errors": errors}, ensure_ascii=False))

if __name__ == "__main__":
    main()
```

以上。
