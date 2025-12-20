# Revitデータ取得 完全ガイド

## 1. 概要

このドキュメントは、Revit MCPサーバーと通信し、プロジェクトから要素情報（部屋、ドアなど）を網羅的に取得してファイルに保存するための一連のベストプラクティスと手順をまとめたものです。
これまでの対話で発生したタイムアウトや文字化けの問題を解決するための知識が集約されています。

---


## Step 1: 接続とサーバーの確認

まず、RevitとMCPサーバーが正常に動作しているかを確認します。

1.  **Revitを起動し、対象のプロジェクトファイルを開きます。**
2.  **pingコマンドでサーバーの応答を確認します。**

    ```bash
    python Python/Manuals/Scripts/send_revit_command_durable.py --port 5210 --command ping_server
    ```

    **成功時の応答例:**
    ```json
    {
      "ok": true,
      "msg": "MCP Server is running"
    }
    ```

3.  **応答がない、またはエラーになる場合:**
    *   `--port` を `5211`, `5212` と変更して試してください。
    *   Revitが起動しているか、アドインが正しくロードされているかを確認してください。

---


## Step 2: 要素リストの取得 (例: 全てのドア)

次に、対象となる要素の基本情報リストを取得し、JSONファイルとして保存します。ここではドアを例に取ります。

### 重要: 文字化け対策

Windowsのコマンドプロンプトで作業する場合、日本語などのデータが文字化けするのを防ぐため、**必ずコマンドの先頭に `chcp 65001` を付けてください。** これにより、シェルのエンコーディングがUTF-8になり、ファイルが正しく保存されます。

**実行コマンド:**
```bash
chcp 65001 && python Python/Manuals/Scripts/send_revit_command_durable.py --port 5210 --command get_doors > doors_list.json
```

*   `--command get_doors`: `get_rooms`、`get_walls` など、取得したい要素に応じて変更してください。
*   `> doors_list.json`: 出力ファイル名を指定します。

このコマンドにより、全てのドアの基本情報（ID, 名前, レベルなど）が含まれた `doors_list.json` が生成されます。

---


## Step 3: 全要素の詳細情報取得 (ループ処理)

Step 2で作成したリストファイルを元に、各要素の詳細なパラメータを一つずつ取得します。この処理は要素数が多いため、専用のPythonスクリプトを作成して自動化するのが最適です。

以下に、`doors_list.json` を読み込み、全てのドアの詳細パラメータを取得して `doors_details.json` に保存するスクリプトのテンプレートを示します。

**スクリプト例 (`get_all_door_details.py`):**
```python
import json
import subprocess
import sys
import locale

def get_and_save_door_details():
    # Windowsのデフォルトエンコーディング(cp932など)を自動的に取得
    preferred_encoding = locale.getpreferredencoding()
    print(f"システムの推奨エンコーディングを使用: {preferred_encoding}")

    # Step 2で作成したリストファイルを読み込む
    try:
        with open('doors_list.json', 'r', encoding=preferred_encoding) as f:
            data = json.load(f)
    except Exception as e:
        print(f"doors_list.jsonの読み込みに失敗しました: {e}", file=sys.stderr)
        return

    doors = data.get('doors', [])
    if not doors:
        print("ドアが見つかりません。", file=sys.stderr)
        return

    all_door_details = []
    print(f"{len(doors)}個のドア全ての詳細情報を取得します...")

    # 各ドアに対してループ処理を実行
    for i, door in enumerate(doors):
        door_id = door.get('elementId')
        if not door_id:
            continue

        print(f"  ({i+1}/{len(doors)}) ドアID: {door_id} のパラメータを取得中...")
        
        params_str = json.dumps({'elementId': door_id})

        # Manuals/Scripts/send_revit_command_durable.pyを呼び出すコマンドをリストとして構築
        command = [
            'python',
            'Python/Manuals/Scripts/send_revit_command_durable.py',
            '--port', '5210',
            '--command', 'get_door_parameters',
            '--params', params_str
        ]
        
        try:
            # shell=Trueを避け、引数をリストで渡すことでクォーテーション問題を回避
            result = subprocess.run(command, capture_output=True, check=False)
            
            # Revitからの出力もシステムのエンコーディングでデコード
            stdout = result.stdout.decode(preferred_encoding, errors='ignore')
            stderr = result.stderr.decode(preferred_encoding, errors='ignore')

            if result.returncode != 0:
                print(f"    エラー: ドアID {door_id} のパラメータ取得に失敗しました。\n{stderr}", file=sys.stderr)
                continue

            if not stdout.strip():
                print(f"    警告: ドアID {door_id} からの応答が空です。", file=sys.stderr)
                continue

            # 取得したJSON文字列をパースして詳細情報を結合
            params_data = json.loads(stdout)
            detailed_info = {**door, 'parameters': params_data.get('parameters', [])}
            all_door_details.append(detailed_info)

        except Exception as e:
            print(f"    例外発生: ドアID {door_id} の処理中にエラー: {e}", file=sys.stderr)
            continue

    # 最終結果をUTF-8でファイルに保存
    try:
        with open('doors_details.json', 'w', encoding='utf-8') as f:
            json.dump(all_door_details, f, indent=2, ensure_ascii=False)
        print(f"\n正常に {len(all_door_details)} 個のドアの詳細情報を doors_details.json に保存しました。")
    except Exception as e:
        print(f"\nファイル保存エラー: {e}", file=sys.stderr)

if __name__ == '__main__':
    get_and_save_door_details()

```

### スクリプトの実行

上記の内容でPythonファイル（例: `get_all_door_details.py`）を作成し、実行します。

```bash
python get_all_door_details.py
```

---


## 4. トラブルシューティング

-   **タイムアウトエラー:** `Manuals/Scripts/send_revit_command_durable.py` のポーリングロジックが古い可能性があります。「`手順書/01_最重要_Revitポーリングロジック.md`」を参照して、`commandId` を使用しない最新のロジックに更新してください。
-   **文字化け:** このガイドのStep 2で説明した `chcp 65001` の使用や、Step 3のスクリプトテンプレート内のエンコーディング処理を確認してください。
-   **unrecognized arguments エラー:** Step 3のスクリプトテンプレートのように、`subprocess.run` にコマンドをリスト形式で渡し、`shell=True` を避けることで、コマンドライン引数のパース問題を回避できます。


---

## 5. 用途最適化の推奨（パラメータ取得とビュー操作）

パラメータ取得やビュー名変更には複数のコマンドが存在します。壊れている/非推奨という意味ではなく、「用途に最適なもの」を使い分けることで、速度・堅牢性・再現性が向上します。

- パラメータ取得（大量・堅牢・一貫性）
  - 推奨: get_instance_parameters_bulk
    - 理由: 一括取得＋ページングにより往復数を削減。paramKeys に uiltInId/guid を指定でき、ロケール差や名称揺れの影響を受けにくい。
    - 真偽（オン/オフ）判定のベストプラクティス:
      - 値が boolean → そのまま True/False
      - 値が int/double → 0 以外を ON
      - 値が string → trim+小文字化し、	rue/yes/on/はい/checked を ON
      - 値が欠落している場合は display[paramName] も同様に判定（ローカライズ文字列のみ出るケースを吸収）
  - 使い分け:
    - list_*_parameters: まず対象パラメータの存在や ID/型を調査する用途（初回の地ならし）
    - get_*_parameters / get_*_parameter: 単一要素の素早い確認やデバッグ用途
    - 型パラメータは get_type_parameters_bulk を併用

- ビュー名の変更（明示的な改名）
  - 推奨: ename_view { viewId, newName }
    - 代替: 複製時に duplicate_view { namePrefix } を使って分かりやすい名前を最初から付与
  - 状態復元やテンプレート適用は save_view_state / estore_view_state を併用（pply で template/categories/filters/worksets を選択、hiddenElements は要件に応じて）

ポイントは「大量・安定・言語非依存」では bulk、「スポット確認」では単発系という使い分けです。これにより、取得スループットと判定の安定性が両立します。

---

## 6. パラメータ入出力依頼時の事前確認ルール（Snoop 推奨）

ユーザーから「このパラメータを読み書きしてほしい」と依頼された場合は、**実行前に必ずパラメータの実体を確認**してください。とくに以下の手順を徹底することで、誤ったパラメータや型違いによる不具合を防げます。

1. **Snoop コマンドでパラメータを確認する**
   - 対象要素を Revit 上で選択したうえで、Snoop 系ツール（Snoop DB / Snoop Current Selection など）を使い、  
     - パラメータ名（BuiltInParameter 名、Shared Parameter 名、表示名）  
     - ストレージ型（String / Double / Integer / ElementId / YesNo など）  
     - 単位系（長さ、面積、角度など）  
   を事前に確認してください。

2. **ユーザー指定が曖昧な場合は必ず問い合わせる**
   - 例えば、次のような状態では **推測で実装しない** ことを推奨します:
     - 表示名と内部名が複数候補あり、どれを指しているか特定できない。
     - 同名パラメータがタイプ／インスタンスの両方に存在する。
     - 型や単位が不明で、どの形式で値を渡してよいか判断できない。
   - この場合は、  
     - 「Snoop で確認した実際のパラメータ名・型・レベル（Type/Instance）」  
     - 想定している値の例（例: `1200 mm`, `"タイルカーペット"`, `Yes/No` など）  
     をユーザーに確認し、**仕様が明らかになってから** コマンド実行やスクリプト実装を行ってください。

3. **ログやマニュアルに前提を書き残す**
   - 一度確定したパラメータ仕様（名前・型・単位・読み取り専用かどうか）は、  
     - スクリプト先頭のコメント  
     - チーム内向けのメモ / マニュアル  
   に明記しておくと、後から別のメンバーが同じ誤解を繰り返すことを防げます。

この「Snoop で必ず実体確認 → 不明点があればユーザーに問い合わせ」の流れを、**パラメータ入出力に関わる全てのコマンド／スクリプトの共通ルール**として運用することを推奨します。
