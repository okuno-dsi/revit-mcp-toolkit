# 汎用的なスナップショット保存手順書

## 目的
Revitモデル内の特定のカテゴリに属する要素の情報を、Revit MCPサーバーを介してJSON形式で取得し、ファイルとして保存する手順を説明します。これにより、モデルの特定部分の「スナップショット」を作成し、後で分析や比較に利用できます。

## 前提条件
- Revit MCPサーバーが稼働していること。
- Python実行環境がセットアップされており、`requests`ライブラリがインストールされていること。

## 手順

### 1. スナップショット取得用Pythonスクリプトの準備
以下のPythonスクリプトは、Revit MCPサーバーにコマンドを送信し、その結果をポーリングして取得する汎用的な関数`send_command_and_get_result`を含んでいます。この関数は、エラーハンドリング、リトライ、および`?force=1`オプションによる再送ロジックを内包しています。

```python
import requests
import json
import time
import os
import sys
from datetime import datetime

PORT = 5210 # Revit MCPサーバーのポート番号
OUTPUT_FILE_PREFIX = "snapshot_" # 出力ファイル名のプレフィックス
MAX_RETRIES = 30
RETRY_DELAY_SECONDS = 3
INITIAL_DELAY_SECONDS = 5

def send_command_and_get_result(command_name, params=None):
    payload = {
        "jsonrpc": "2.0",
        "method": command_name,
        "id": 1
    }
    if params:
        payload["params"] = params

    headers = {"Content-Type": "application/json; charset=utf-8", "Accept": "application/json; charset=utf-8"}
    
    enqueue_url_base = f"http://localhost:{PORT}/enqueue"
    
    for attempt_enqueue in range(2): # Try twice: once without force, once with force
        current_enqueue_url = enqueue_url_base
        if attempt_enqueue == 1: # Second attempt, add force=1
            current_enqueue_url = f"{enqueue_url_base}?force=1"
            print(f"Retrying enqueue with force=1 for {command_name}...")

        try:
            response = requests.post(current_enqueue_url, json=payload, headers=headers)
            response.raise_for_status() # Raise HTTPError for bad responses (4xx or 5xx)
            enqueue_result = response.json()
            print(f"Enqueue result for {command_name}: {enqueue_result}")

            if enqueue_result.get("ok"):
                command_id = enqueue_result.get("commandId")
                if command_id:
                    print(f"Command enqueued successfully. Command ID: {command_id}")
                    
                    print(f"Waiting for {INITIAL_DELAY_SECONDS} seconds before polling...")
                    time.sleep(INITIAL_DELAY_SECONDS)

                    get_result_url = f"http://localhost:{PORT}/get_result"
                    for attempt_poll in range(MAX_RETRIES):
                        print(f"Polling for result... (Attempt {attempt_poll + 1}/{MAX_RETRIES})")
                        try:
                            result_response = requests.get(get_result_url, params={"commandId": command_id}, headers=headers, timeout=5)
                            result_response.raise_for_status()
                            
                            if result_response.text:
                                result_data = result_response.json()
                                print(f"Polling result for {command_name}: {result_data}")
                            else:
                                print(f"Empty response received during polling for {command_name}. Retrying...")
                                time.sleep(RETRY_DELAY_SECONDS)
                                continue # Continue polling if response is empty

                        except json.JSONDecodeError as e:
                            print(f"JSON Decode Error during polling for {command_name}: {e}. Response text: {result_response.text}", file=sys.stderr)
                            time.sleep(RETRY_DELAY_SECONDS)
                            continue # Continue polling on JSON decode error, hoping for a valid response later
                        except requests.exceptions.RequestException as e:
                            print(f"HTTP Request failed during polling for {command_name}: {e}", file=sys.stderr)
                            time.sleep(RETRY_DELAY_SECONDS)
                            continue # Continue polling on request error

                        if result_data and "result" in result_data and result_data["result"] is not None:
                            command_result = result_data["result"]
                            if command_result.get("ok"):
                                print(f"Successfully executed command: {command_name}")
                                return command_result # Return the 'result' object
                            else:
                                error_message = command_result.get("error", "Unknown error from Revit.")
                                print(f"Command '{command_name}' failed with error: {error_message}", file=sys.stderr)
                                return None # Command failed, return None
                        elif result_data and "error" in result_data:
                            print(f"Received an error response from server: {result_data['error']}", file=sys.stderr)
                            return None # Server returned a definitive error, stop polling

                        time.sleep(RETRY_DELAY_SECONDS)
                    
                    print(f"Max polling retries reached for command '{command_name}'. No result received.", file=sys.stderr)
                    return None
                else:
                    print(f"No commandId received from enqueue for {command_name}.", file=sys.stderr)
                    return None
            else:
                print(f"Failed to enqueue command {command_name}: {enqueue_result.get('error')}", file=sys.stderr)
                return None

        except requests.exceptions.HTTPError as e:
            if e.response.status_code == 409 and attempt_enqueue == 0:
                print(f"Received 409 Conflict for {command_name}. Retrying with ?force=1...", file=sys.stderr)
                continue
            else:
                print(f"HTTP Error for {command_name}: {e}", file=sys.stderr)
                return None
        except requests.exceptions.RequestException as e:
            print(f"HTTP Request failed for {command_name}: {e}", file=sys.stderr)
            return None
        except json.JSONDecodeError as e:
            print(f"JSON Decode Error for {command_name}: {e}", file=sys.stderr)
            return None
        except Exception as e:
            print(f"An unexpected error occurred for {command_name}: {e}", file=sys.stderr)
            return None
    
    print(f"Failed to enqueue command {command_name} after all retries.", file=sys.stderr)
    return None

# このスクリプトを直接実行する部分 (例)
if __name__ == "__main__":
    # 例: 壁の情報を取得するスナップショット
    # コマンド名とパラメータは、Revit MCPサーバーのコマンドハンドラ一覧で確認してください。
    # 例: get_walls, get_rooms, get_structural_columns, get_structural_frames など
    
    # 取得したいカテゴリのコマンド名と、必要に応じてパラメータを指定
    # 例1: すべての壁の情報を取得
    command_name_1 = "get_walls"
    params_1 = {"skip": 0, "count": 10000, "namesOnly": False} # 全件取得を試みる

    # 例2: すべての部屋の情報を取得
    # command_name_2 = "get_rooms"
    # params_2 = {"skip": 0, "count": 10000, "namesOnly": False}

    # 例3: 構造柱の情報を取得 (先ほど成功した例)
    # command_name_3 = "get_structural_columns"
    # params_3 = {"skip": 0, "count": 10000, "namesOnly": False}

    # 実行するコマンドを選択
    selected_command_name = command_name_1
    selected_params = params_1
    output_filename = f"{OUTPUT_FILE_PREFIX}{selected_command_name}.json"

    print(f"Attempting to get snapshot for: {selected_command_name}...")
    result = send_command_and_get_result(selected_command_name, selected_params)

    if result and result.get("ok"):
        # 結果のキーはコマンドによって異なるため、柔軟に対応
        # 例: get_walls なら "walls", get_rooms なら "rooms", get_structural_columns なら "structuralColumns"
        # コマンドハンドラ一覧で返されるJSONの構造を確認してください。
        data_key = None
        if "walls" in result:
            data_key = "walls"
        elif "rooms" in result:
            data_key = "rooms"
        elif "structuralColumns" in result:
            data_key = "structuralColumns"
        elif "structuralFrames" in result:
            data_key = "structuralFrames"
        # 他のカテゴリもここに追加

        if data_key:
            snapshot_data = {
                "timestamp": datetime.now().isoformat(),
                "command": selected_command_name,
                "params": selected_params,
                data_key: result.get(data_key, [])
            }
            print(f"Successfully retrieved {len(snapshot_data[data_key])} elements for {selected_command_name}.")
        else:
            # 汎用的なキーが見つからない場合、結果全体を保存
            snapshot_data = {
                "timestamp": datetime.now().isoformat(),
                "command": selected_command_name,
                "params": selected_params,
                "raw_result": result # 結果全体をそのまま保存
            }
            print(f"Successfully retrieved data for {selected_command_name}. No specific data key found, saving raw result.")
        
        # JSONファイルとして保存
        try:
            with open(output_filename, "w", encoding="utf-8") as f:
                json.dump(snapshot_data, f, indent=2, ensure_ascii=False)
            print(f"Snapshot saved to {output_filename}")
        except Exception as e:
            print(f"Error saving snapshot to file: {e}", file=sys.stderr)
    else:
        print(f"Failed to retrieve snapshot for {selected_command_name}.")

```

## 注意点
- **コマンド名とパラメータ:** `selected_command_name` と `selected_params` は、Revit MCPサーバーの[コマンドハンドラ一覧]で確認してください。各コマンドがどのようなパラメータを必要とし、どのような結果を返すかが記載されています。
- **データキーの特定:** `result` オブジェクトから実際の要素リストを取り出すためのキー（例: `"walls"`, `"rooms"`, `"structuralColumns"` など）は、コマンドによって異なります。スクリプト内の`data_key`を特定するロジックを、使用するコマンドに合わせて適宜更新してください。
- **大量データの取得:** `skip` と `count` パラメータを使用することで、大量のデータをページングして取得できます。全件取得する場合は、`count` に十分大きな値を指定してください。
- **エラーハンドリング:** スクリプトには基本的なエラーハンドリングとリトライロジックが含まれていますが、Revitの状況やネットワークの状態によっては、さらに詳細な対応が必要になる場合があります。
- **単位:** 入力単位はmm/degで指定してください。サーバー側で内部的にft/radに変換されます。

