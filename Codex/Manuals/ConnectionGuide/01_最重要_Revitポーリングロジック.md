### Revitアドイン通信ポーリングロジック手順書

#### 1. 概要

RevitアドインとのHTTP通信は、コマンドの**キュー登録 (`/enqueue`)**と結果の**ポーリング取得 (`/get_result`)** の2段階で行います。この手順により、時間のかかる処理でも安定して結果を取得できます。

**【重要: IDの取り扱いについて】**
Revit要素の識別には、`Element ID`（整数値）と`Unique ID`（GUID文字列）の2種類があります。`Unique ID`はプロジェクト間で一意であり、より堅牢な識別子として推奨されます。Revit MCPアドインは、`Unique ID`が提供された場合、自動的に対応する`Element ID`に変換して処理する機能を備えています。可能な限り`Unique ID`の使用を優先してください。

#### 2. 通信フロー

1.  **コマンドのキュー登録:**
    *   実行したいコマンドとパラメータをJSON形式で作成します。
    *   `http://localhost:{port}/enqueue` エンドポイントにHTTP POSTリクエストを送信します。
    *   サーバーはリクエストを受け付け、すぐに`{"ok": true, "commandId": "..."}`のような応答を返します。

2.  **結果のポーリング取得:**
    *   コマンドの処理には時間がかかる場合があるため、結果が準備できるまで待つ必要があります。
    *   `http://localhost:{port}/get_result` エンドポイントにHTTP GETリクエストを定期的に（例: 0.5秒ごと）送信します。
    *   **注意:** `/get_result` の呼び出しに `commandId` は**含めません**。サーバーは最後にキュー登録されたコマンドの結果を返します。
    *   処理が完了すると、サーバーはコマンドの実行結果をJSON形式で返します。処理中はステータスコード `204 No Content` または空の応答が返されることがあります。

#### 3. 実装例

以下は、プロジェクト情報を取得する `get_project_info` コマンドを実行するPythonスクリプトの完全な実装例です。この例は、現在推奨される正しいポーリング方式を示しています。

```python
import requests
import time
import json
import argparse
import os
import sys
from typing import Any, Dict, Tuple, Optional, Mapping

# --- Revit Communication ---
POLLING_INTERVAL_SECONDS = 0.5
MAX_POLLING_ATTEMPTS = 600  # ~300 seconds total (0.5s * 600)
HEADERS = {
    "Content-Type": "application/json; charset=utf-8",
    "Accept-Charset": "utf-8",
    "Accept": "application/json",
}

class RevitMcpError(Exception):
    """Structured error for MCP/JSON-RPC failures."""
    def __init__(self, where: str, message: str, *, http_status: Optional[int] = None, payload: Optional[dict] = None):
        super().__init__(f"[{where}] {message}")
        self.where = where
        self.http_status = http_status
        self.payload = payload or {}

def _json_or_raise(resp: requests.Response, where: str) -> Any:
    """Return JSON (of any shape) or raise with clear message including HTTP and body."""
    try:
        return resp.json()
    except Exception:
        # If JSONパースに失敗→HTTPステータスで判断しつつ本文を付けて明示
        text = (resp.text or "").strip()
        if resp.status_code >= 400:
            raise RevitMcpError(where, f"HTTP {resp.status_code} {resp.reason}; body={text}", http_status=resp.status_code)
        raise RevitMcpError(where, f"Invalid JSON body; body={text}", http_status=resp.status_code)

def _raise_if_jsonrpc_error(obj: Any, where: str):
    """Top-level JSON-RPC error を検知したら即例外（どんな形でも安全に扱う）."""
    if isinstance(obj, Mapping) and obj.get("error"):
        err = obj["error"]
        if isinstance(err, Mapping):
            code = err.get("code")
            message = err.get("message") or "JSON-RPC error"
            data = err.get("data")
            raise RevitMcpError(where, f"JSON-RPC error code={code} message={message}", payload={"error": err, "data": data})
        # error が文字列/配列などでも落とす
        raise RevitMcpError(where, f"JSON-RPC error: {err!r}", payload={"error": err})

def _is_immediate_success(x: Any) -> bool:
    """enqueue が即時に最終結果を返す実装に対応."""
    if isinstance(x, Mapping):
        if x.get("ok") is True:
            return True
        # JSON-RPC result envelope をそのまま返す実装
        if "result" in x and "error" not in x:
            return True
    return False

def send_revit_request(
    port: int,
    method: str,
    params: Optional[Dict[str, Any]] = None,
    *,
    force: bool = False,
    timeout: Tuple[float, float] = (3.0, 120.0)
) -> Any:
    """
    Send a JSON-RPC command to MCP Add-in and wait for completion.
    Returns the 'final' payload (either {ok: true, ...} or JSON-RPC result object).
    Raises RevitMcpError on failure.
    """
    if params is None:
        params = {}

    base = f"http://localhost:{port}"
    enqueue_url = f"{base}/enqueue"
    get_result_url = f"{base}/get_result"
    # 衝突検知の誤作動を避けるため id は時刻ベースでユニーク化
    rpc_id = int(time.time() * 1000)
    payload = {"jsonrpc": "2.0", "method": method, "params": params, "id": rpc_id}

    os.environ["PYTHONIOENCODING"] = "utf-8"

    # --- Enqueue ---
    try:
        r = requests.post(
            enqueue_url,
            params={"force": 1} if force else None,
            json=payload,
            headers=HEADERS,
            timeout=timeout
        )
    except requests.RequestException as e:
        raise RevitMcpError("enqueue", f"HTTP request failed: {e}")

    if r.status_code == 409:
        # 競合は例外にせず、呼び出し側で使えるように返す（互換維持）
        # サーバーがJSONを返す場合/返さない場合の両方に対応
        try:
            data = r.json()
        except Exception:
            data = {"msg": (r.text or "").strip()}
        merged: Dict[str, Any] = {}
        if isinstance(data, dict):
            merged.update(data)
        # 標準キーで上書き（上書き防止のため最後に設定）
        merged["ok"] = merged.get("ok", False)
        merged["httpStatus"] = 409
        if "code" not in merged:
            merged["code"] = "HTTP_409"
        return merged

    # 他のHTTPエラーは JSON-RPC error の可能性に配慮
    if r.status_code >= 400:
        try:
            data = r.json()
        except Exception:
            r.raise_for_status()  # 非JSONのHTTPエラー
        _raise_if_jsonrpc_error(data, "enqueue")
        # JSONにerrorは無いが4xx/5xx → 明示エラー
        raise RevitMcpError("enqueue", f"HTTP {r.status_code} {r.reason}", http_status=r.status_code, payload=data)

    # 200系 → JSONを確認
    data = _json_or_raise(r, "enqueue")
    _raise_if_jsonrpc_error(data, "enqueue")

    # enqueue 成功でもサーバー実装によっては {ok:true} や JSON-RPC result を即返す
    if isinstance(data, dict) and data.get("ok") is False:
        # ビジネスロジック系のエラー
        msg = data.get("error") or data.get("msg") or "enqueue failed"
        raise RevitMcpError("enqueue", str(msg), payload=data)
    if _is_immediate_success(data):
        return data  # 即時完了

    # --- Poll for result ---
    attempts = 0
    while attempts < MAX_POLLING_ATTEMPTS:
        time.sleep(POLLING_INTERVAL_SECONDS)
        try:
            gr = requests.get(get_result_url, headers=HEADERS, timeout=timeout)
        except requests.RequestException as e:
            raise RevitMcpError("get_result", f"HTTP request failed: {e}")

        # 202=Accepted, 204=No Content → 未完了
        if gr.status_code in (202, 204):
            attempts += 1
            continue

        # 同様にHTTPエラーでもJSON-RPCの可能性に配慮
        if gr.status_code >= 400:
            try:
                data_err = gr.json()
            except Exception:
                gr.raise_for_status()  # 非JSONのHTTPエラー
            _raise_if_jsonrpc_error(data_err, "get_result")
            raise RevitMcpError("get_result", f"HTTP {gr.status_code} {gr.reason}", http_status=gr.status_code, payload=data_err)

        # 200系 → JSON
        data_res = _json_or_raise(gr, "get_result")
        _raise_if_jsonrpc_error(data_res, "get_result")

        # パターン1: { ok:true/false, ... }
        if isinstance(data_res, Mapping) and "ok" in data_res:
            if data_res.get("ok") is True:
                return data_res
            else:
                # サーバー独自のビジネスエラー
                msg = data_res.get("error") or data_res.get("msg") or "Command failed"
                raise RevitMcpError("get_result", str(msg), payload=data_res)

        # パターン2: JSON-RPC resultオブジェクト（okキーなし）
        # → 成功としてそのまま返す
        if isinstance(data_res, Mapping):
            return data_res

        # それ以外はおかしい
        raise RevitMcpError("get_result", f"Unexpected payload shape: {data_res!r}", payload=data_res)

    # タイムアウト
    raise RevitMcpError("get_result", f"Polling timed out for '{method}' "
                                      f"after {MAX_POLLING_ATTEMPTS * POLLING_INTERVAL_SECONDS:.1f} sec.")

# --- Main Logic ---
def main():
    parser = argparse.ArgumentParser(description="Send a generic JSON-RPC command to Revit MCP add-in.")
    parser.add_argument("--port", type=int, required=True, help="Port number of the Revit instance.")
    parser.add_argument("--command", type=str, required=True, help="Command name (method) to send.")
    parser.add_argument("--params", type=str, default=None, help="JSON string of parameters (default {}).")
    parser.add_argument("--params-file", type=str, help="Path to a JSON file containing parameters.")
    parser.add_argument("--output-file", type=str, help="Path to the output JSON file.")
    parser.add_argument("--force", action="store_true", help="Use /enqueue?force=1 to overwrite a running job.")
    args = parser.parse_args()

    # params の読み込み
    if args.params_file:
        try:
            with open(args.params_file, 'r', encoding='utf-8') as f:
                params_dict = json.load(f)
        except (json.JSONDecodeError, FileNotFoundError) as e:
            print(json.dumps({"ok": False, "error": f"Error reading params file: {e}"}, ensure_ascii=False), file=sys.stderr)
            sys.exit(1)
    elif args.params:
        try:
            params_dict = json.loads(args.params)
        except json.JSONDecodeError:
            print(json.dumps({"ok": False, "error": "Invalid JSON string for --params."}, ensure_ascii=False), file=sys.stderr)
            sys.exit(1)
    else:
        params_dict = {}

    try:
        result = send_revit_request(args.port, args.command, params_dict, force=args.force)
        if args.output_file:
            # 明示的に相対パスを許容（呼び出し側で制御しやすいように __file__ は使わない）
            output_file_path = os.path.abspath(args.output_file)
            with open(output_file_path, 'w', encoding='utf-8') as f:
                json.dump(result, f, indent=2, ensure_ascii=False)
            print(json.dumps({"ok": True, "savedTo": output_file_path}, ensure_ascii=False))
        else:
            print(json.dumps(result, indent=2, ensure_ascii=False))
    except RevitMcpError as e:
        # 構造化エラーをそのままJSON化（LLMにも親切）
        err_json = {
            "ok": False,
            "where": e.where,
            "httpStatus": e.http_status,
            "error": str(e),
            "payload": e.payload
        }
        if args.output_file:
            output_file_path = os.path.abspath(args.output_file)
            with open(output_file_path, 'w', encoding='utf-8') as f:
                json.dump(err_json, f, indent=2, ensure_ascii=False)
        print(json.dumps(err_json, indent=2, ensure_ascii=False))
        sys.exit(1)
    except Exception as e:
        # 予期しない例外
        err_json = {"ok": False, "error": f"Unexpected failure: {e}"}
        if args.output_file:
            output_file_path = os.path.abspath(args.output_file)
            with open(output_file_path, 'w', encoding='utf-8') as f:
                json.dump(err_json, f, indent=2, ensure_ascii=False)
        print(json.dumps(err_json, indent=2, ensure_ascii=False))
        sys.exit(1)

if __name__ == "__main__":
    main()


```

#### 4. タイムアウト対策: チャンク化の推奨

長時間の値取得（大量のパラメータキーや要素を一度に読む処理）は、Revit UI の処理や他ジョブの影響でタイムアウトしやすくなります。以下のベストプラクティスで安定性を高めてください。

- 事前にID一覧を取得して分割する
  - 例: まず `get_param_meta` で対象要素の全パラメータ（name/paramId 等）を列挙し、`paramKeys` を作成。
  - 次に `get_instance_parameters_bulk` / `get_type_parameters_bulk` へ 40〜80 件程度のスライスに分けて投入し、各呼び出しを 8〜12 秒以内に収める。
- 呼び出しの待機/タイムアウトを小さく保つ
  - `wait-seconds`: 3〜5、`timeout-sec`: 8〜12 程度。チャンクサイズで調整して各リクエストがタイムアウトしないよう制御する。
- 集約と再試行
  - 取得した `items[].params/display` をクライアント側で集約。失敗したチャンクのみを再試行でき、全体の壁時計時間を抑えつつ信頼性が向上する。
- キーの安定性
  - `paramKeys` は `builtInId`/`guid` を優先し、`name` は最終手段とする（ロケール差異を回避）。

このチャンク化パターンは、複数Revitインスタンスや大型モデルでの一時的なUIビジー状態においても、タイムアウトの発生を顕著に抑制します。

#### 4. Revitの情報は容量が非常に大きい

情報を取りこぼさないためとトラフィックの増大防止やパフォーマンスを低下させないように必ずJSON形式のファイルに保存してから次の処理を行うこと。

#### 5. 環境設定に関する注意点

`PYTHONIOENCODING=utf-8`環境変数を設定すると、Pythonの標準入出力での文字化けを防ぐことができます。スクリプト内で `os.environ["PYTHONIOENCODING"] = "utf-8"` を設定するか、実行環境で事前に設定してください。


**失敗条件**
HTTP ステータスが 4xx/5xx なら即失敗。
JSON に error が含まれていれば即失敗。
error.data.ok === false なら即失敗。

**再試行の可否**
error.data.retriable === true の時だけ自動再試行。
それ以外は人間の確認を待つ。

**自己修正ガイド**
error.data.expectedParams を見て不足を補う。
error.data.aliasesTried を見て、今後は正式名（elementId）を使う。
error.data.error.code を分類キーにして、既知の修正パターンを適用（例：ERR_PARAM_INVALID → パラメータ名のスペルチェック/別名禁止）。


# 環境設定と接続確認

## 1. 概要
このドキュメントでは、Revitアドインが正しく動作しているかを確認し、基本的な接続テストを行う手順を説明します。
最も基本的なテストとして、現在開いているプロジェクトの情報を取得する `get_open_documents` コマンドを実行します。

**【重要: IDの取り扱いについて】**
Revit要素の識別には、`Element ID`（整数値）と`Unique ID`（GUID文字列）の2種類があります。`Unique ID`はプロジェクト間で一意であり、より堅牢な識別子として推奨されます。

## 2. 前提条件
-   Revitが起動しており、対象のプロジェクトファイルが開かれていること。
-   Revit MCPアドインがロードされ、サーバーが動作していること。
-   `Manuals/Scripts/send_revit_command_durable.py` スクリプトが利用可能であること。

## 3. 接続テストの実行

### 3.1. コマンド
`get_open_documents` コマンドは、現在Revitで開かれているすべてのドキュメント（ホストとリンク）の情報を返します。

**実行コマンド:** 
```bash
python Python/Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_open_documents
```
-   `--port`: 必要に応じて、Revitアドインが動作しているポート番号に変更してください。

### 3.2. 実行結果の確認
コマンドが成功すると、以下のようなJSON形式のデータが出力されます。`"ok": true` と、開いているドキュメントの情報が含まれていれば、接続は正常です。

**出力例:**
```json
{
  "ok": true,
  "count": 2,
  "documents": [
    {
      "title": "サンプル構造",
      "path": "C:\\...\\サンプル構造.rvt",
      "projectName": "サンプル構造",
      "isLinked": true,
      "role": "link",
      "uniqueId": "doc-unique-id-linked"
    },
    {
      "title": "サンプル意匠",
      "path": "C:\\...\\サンプル意匠.rvt",
      "projectName": "サンプル意匠",
      "isLinked": false,
      "role": "host",
      "uniqueId": "doc-unique-id-host"
    }
  ],
  "issues": {
    "failures": [],
    "dialogs": []
  }
}
```

サーバー稼働確認用の ping_server コマンド
{"method":"ping_server","desc":"Check if the MCP server is running.","params_example":{},"result_example":{"ok":true,"msg":"MCP Server is running"}}

## 4. トラブルシューティング
-   `ConnectionRefusedError` やタイムアウトエラーが発生した場合、Revitが起動しているか、アドインが正しくロードされているか、また指定したポート番号が正しいかを確認してください。

-   `"ok": false` が返された場合は、Revitアドイン側でエラーが発生しています。Revitのダイアログやログを確認してください。

## 5. 【最重要】文字化けを確実に防ぐためのコマンド実行方法 (Windows)

Windows環境で `Manuals/Scripts/send_revit_command_durable.py` を実行し、日本語を含むJSON結果をファイルに保存する際に、深刻な文字化け問題が発生します。これを回避するには、以下のルールを**絶対に守ってください**。

### 根本原因
Windowsのコマンドプロンプトが使うデフォルトの文字コード（cp932）と、プログラムが期待する文字コード（UTF-8）が異なるためです。

### 禁止事項：リダイレクト(`>`)の使用
以下のコマンドは、**絶対に実行しないでください**。リダイレクト演算子 `>` を使うと、出力ファイルが `cp932` でエンコードされ、100%文字化けします。

```bash
# 間違い：この方法は必ず文字化けを引き起こす
python Python/Manuals/Scripts/send_revit_command_durable.py --command get_rooms > rooms.json  # 絶対に禁止！
```

### 唯一の正しい方法：`--output-file` オプションの使用
文字化けを防ぐ、唯一の信頼できる方法は、`Manuals/Scripts/send_revit_command_durable.py` の `--output-file` オプションを使用することです。これにより、スクリプトが直接UTF-8でファイルを書き込むため、エンコーディングの問題を完全に回避できます。

**正しいコマンドの例:**
```bash
python Python/Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_rooms --output-file C:\Users\user\path\to\rooms.json
```

パラメータを渡す必要がある場合は、`--params-file` オプションを併用します。

**パラメータファイルを使った正しいコマンドの例:**
```bash
# 1. パラメータをJSONファイルとして用意する (例: params.json)
# {"skip": 0, "count": 5000}

# 2. コマンドを実行する
python Python/Manuals/Scripts/send_revit_command_durable.py ^
    --port 5210 ^
    --command get_walls ^
    --params-file C:\path\to\params.json ^
    --output-file C:\path\to\walls.json
```

**このルールは、日本語データを含む可能性のあるすべてのコマンド実行に適用されます。徹底してください。**





# Revitコマンド実行時のエラー解決手順書

## 1. 問題の背景

当初の目的は、Revitプロジェクト内の各部屋の基本情報と詳細なパラメータを抽出し、JSONファイルとしてエクスポートすることでした。しかし、このプロセスにおいて、Revitアドインとの連携コマンドが予期せぬエラーで失敗し、原因の特定が困難な状況に陥りました。

### 直面した主なエラー
- `get_rooms`コマンドのタイムアウト
- `get_room_params`、`get_element_info`、`select_elements_by_filter_id`などの詳細情報取得コマンドが、原因不明のエラーで失敗。
- Pythonの`subprocess`モジュールからのエラーメッセージが不明瞭（例: `UnicodeDecodeError`、`Command failed with exit code 1`、`Result: None`）。

## 2. 当初のエラー原因と課題

根本的な原因は、Revitアドインとの通信を担う`Manuals/Scripts/send_revit_command_durable.py`スクリプト、およびそれを呼び出すラッパースクリプトの堅牢性不足にありました。

### 2.1. `Manuals/Scripts/send_revit_command_durable.py`の堅牢性不足
- **短いタイムアウト設定**: デフォルトの60秒という短いタイムアウトが設定されており、Revit側での処理に時間がかかるコマンドが「ハングアップ」と誤認され、タイムアウトエラーを引き起こしていました。
- **エラー報告の脆さ**: コマンドがRevitアドイン側で内部的に失敗した場合、`Manuals/Scripts/send_revit_command_durable.py`が構造化されたエラー情報をPython側に返さず、結果として`subprocess.run`が`UnicodeDecodeError`や`CalledProcessError`を発生させていました。これにより、Revitからの真のエラーメッセージ（例: 「パラメータが見つかりません」など）がPython側で捕捉できず、デバッグが極めて困難でした。
- **`id`の固定値**: JSON-RPCリクエストの`id`フィールドが常に`1`に固定されており、Revit側での重複検知やセッション管理において誤動作を引き起こす可能性がありました。

### 2.2. ラッパースクリプト（`run_revit_command_silent`など）の課題
- **`subprocess.run`の不適切な使用**: `shell=True`と`" ".join(cmd_list)`の組み合わせは、Windows環境におけるパスの空白や日本語文字、特殊文字を含む場合にコマンドラインが正しく解釈されない原因となり、セキュリティリスクも伴います。
- **一時ファイル名の衝突**: `os.getpid()`に依存した一時ファイル名生成は、同一プロセス内での並行実行や、連続実行時のファイルロック（`PermissionError`）問題を引き起こす可能性がありました。
- **エラーハンドリングの不足**: `subprocess.run`がエラーを発生させた際に`None`を返す設計だったため、呼び出し元でエラーの原因を特定し、適切な処理を行うことが困難でした。

## 3. 解決策と修正内容

上記課題を解決するため、`Manuals/Scripts/send_revit_command_durable.py`およびラッパースクリプトに以下の堅牢化修正を適用しました。

### 3.1. `Manuals/Scripts/send_revit_command_durable.py`の改善
- **タイムアウト延長**: `MAX_POLLING_ATTEMPTS`を`120`から`600`に増やし、ポーリングタイムアウトを300秒（5分）に延長しました。
- **エラー報告の堅牢化**: 
    - `_json_or_raise`および`_raise_if_jsonrpc_error`関数を修正し、Revitからのエラー応答がどのような形式であっても安全に処理し、構造化された`RevitMcpError`を発生させるようにしました。
    - HTTPステータスコード`202 (Accepted)`を`204 (No Content)`と同様に「未完了」として正しく処理するようにしました。
    - `enqueue`後の即時完了ケース（Revitがすぐに結果を返す場合）に対応し、無駄なポーリングを回避するようにしました。
- **`id`の動的生成**: JSON-RPCリクエストの`id`フィールドを`int(time.time()*1000)`で動的に生成するように変更し、重複検知の誤作動リスクを低減しました。
- **HTTPヘッダーの追加**: `HEADERS`に`"Accept": "application/json"`を追加し、HTTP通信におけるContent-Negotiationの安定性を向上させました。
- **`force`パラメータの安全な渡し方**: `requests.post`の`params`引数を使用して`force`パラメータを渡すように変更し、URL文字列連結の脆弱性を排除しました。
- **docstringの整合性**: `send_revit_request`関数のdocstringを、実際の挙動に合わせて`RevitMcpError`を送出することを明記しました。

### 3.2. ラッパースクリプト（`run_revit_command_silent`関数）の改善
- **`subprocess.run`の安全な使用**: 
    - `cmd_list`を直接`subprocess.run`に渡し、`shell=False`を設定することで、コマンドラインの解釈問題を回避し、セキュリティを向上させました。
    - `env=os.environ.copy()`で環境変数をコピーし、`env["PYTHONIOENCODING"] = "utf-8"`を設定することで、サブプロセスでの文字化け問題を根本的に解決しました。
- **一時ファイルの安全な管理**: `tempfile.mkstemp()`を使用してユニークな一時ファイルを生成するように変更しました。これにより、ファイル名の衝突を防ぎ、`os.fdopen`でファイルオブジェクトを開いた直後にファイルディスクリプタを明示的に閉じることで、`PermissionError`（ファイルロック）問題を回避しました。
- **構造化されたエラー返却**: `run_revit_command_silent`関数が、成功時には`{"ok": True, ...}`、失敗時には`{"ok": False, "error": ...}`のような構造化された辞書を常に返すように変更しました。これにより、呼び出し元でのエラーハンドリングと原因特定が容易になりました。
- **タイムアウト設定**: `subprocess.run`に`timeout`引数を追加し、ラッパースクリプトレベルでのタイムアウト制御を可能にしました。

## 4. 解決までの経緯（デバッグプロセス）

1.  **初期のタイムアウト問題**: `get_rooms`コマンドが頻繁にタイムアウトし、処理が完了しない問題が発生。
2.  **エラーメッセージの不明瞭さ**: `get_room_params`などの詳細コマンドが失敗する際、`UnicodeDecodeError`や`Command failed with exit code 1`といった汎用的なエラーしか得られず、Revitからの具体的なエラー原因が不明。
3.  **`Manuals/Scripts/send_revit_command_durable.py`の出力問題**: `subprocess.DEVNULL`へのリダイレクトで`UnicodeDecodeError`は回避できたものの、`Manuals/Scripts/send_revit_command_durable.py`自体がエラー時に適切なJSONを`--output-file`に書き出さないため、根本原因の特定に至らず。
4.  **ユーザーからの重要な指摘**: 「コマンドがアドインに届いていません」という指摘を受け、`ping_server`コマンドでRevitアドインへの基本的な到達性を確認。結果は成功し、コマンド自体は到達していることが判明。
5.  **根本原因の特定**: `ping_server`は成功するが詳細コマンドが失敗するという状況から、コマンドがアドインに到達した後にRevit内部でエラーが発生している可能性が高いと判断。この時点で、`Manuals/Scripts/send_revit_command_durable.py`とラッパースクリプトの堅牢性不足がデバッグの大きな障壁となっていることが明確化。
6.  **ユーザーからの具体的な改善方針**: 「主な懸念は以下の4点」という形で、`subprocess.run`の安全な使用、タイムアウト、構造化エラー報告、一時ファイル管理に関する具体的な改善方針が提示される。
7.  **修正の適用と検証**: 提示された方針に基づき、`Manuals/Scripts/send_revit_command_durable.py`とラッパースクリプト（`run_revit_command_silent`）を段階的に修正。特に`subprocess.run`の引数渡し方、`tempfile`の使用、エラー返却形式の統一に注力。
8.  **最終的な成功**: 修正後、`get_room_params`が正常に動作することを確認。これにより、当初の目的であった全部屋のパラメータ取得とJSONエクスポートが成功裏に完了。

## 5. 結論

Revitアドインとの連携において、Pythonスクリプト側の堅牢なエラーハンドリングと`subprocess`モジュールの適切な使用が極めて重要であることが再確認されました。特に、Revitアドインからのエラーメッセージが不明瞭な場合でも、Python側でエラーを構造化して捕捉・報告する仕組みを構築することで、デバッグと問題解決の効率が大幅に向上します。これにより、将来的な同様の問題発生時にも、迅速かつ的確な対応が可能となります。

