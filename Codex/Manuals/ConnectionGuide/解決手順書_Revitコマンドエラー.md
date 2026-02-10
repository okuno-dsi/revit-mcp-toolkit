# Revitコマンド実行時のエラー解決手順書

## 1. 問題の背景

当初の目的は、Revitプロジェクト内の各部屋の基本情報と詳細なパラメータを抽出し、JSONファイルとしてエクスポートすることでした。しかし、このプロセスにおいて、Revitアドインとの連携コマンドが予期せぬエラーで失敗し、原因の特定が困難な状況に陥りました。

### 直面した主なエラー
- `get_rooms`コマンドのタイムアウト
- `get_room_params`、`get_element_info`、`select_elements_by_filter_id`などの詳細情報取得コマンドが、原因不明のエラーで失敗。
- Pythonの`subprocess`モジュールからのエラーメッセージが不明瞭（例: `UnicodeDecodeError`、`Command failed with exit code 1`、`Result: None`）。

## 2. 当初のエラー原因と課題

根本的な原因は、Revitアドインとの通信を担う`Scripts/Reference/send_revit_command_durable.py`スクリプト、およびそれを呼び出すラッパースクリプトの堅牢性不足にありました。

### 2.1. `Scripts/Reference/send_revit_command_durable.py`の堅牢性不足
- **短いタイムアウト設定**: デフォルトの60秒という短いタイムアウトが設定されており、Revit側での処理に時間がかかるコマンドが「ハングアップ」と誤認され、タイムアウトエラーを引き起こしていました。
- **エラー報告の脆さ**: コマンドがRevitアドイン側で内部的に失敗した場合、`Scripts/Reference/send_revit_command_durable.py`が構造化されたエラー情報をPython側に返さず、結果として`subprocess.run`が`UnicodeDecodeError`や`CalledProcessError`を発生させていました。これにより、Revitからの真のエラーメッセージ（例: 「パラメータが見つかりません」など）がPython側で捕捉できず、デバッグが極めて困難でした。
- **`id`の固定値**: JSON-RPCリクエストの`id`フィールドが常に`1`に固定されており、Revit側での重複検知やセッション管理において誤動作を引き起こす可能性がありました。

### 2.2. ラッパースクリプト（`run_revit_command_silent`など）の課題
- **`subprocess.run`の不適切な使用**: `shell=True`と`" ".join(cmd_list)`の組み合わせは、Windows環境におけるパスの空白や日本語文字、特殊文字を含む場合にコマンドラインが正しく解釈されない原因となり、セキュリティリスクも伴います。
- **一時ファイル名の衝突**: `os.getpid()`に依存した一時ファイル名生成は、同一プロセス内での並行実行や、連続実行時のファイルロック（`PermissionError`）問題を引き起こす可能性がありました。
- **エラーハンドリングの不足**: `subprocess.run`がエラーを発生させた際に`None`を返す設計だったため、呼び出し元でエラーの原因を特定し、適切な処理を行うことが困難でした。

## 3. 解決策と修正内容

上記課題を解決するため、`Scripts/Reference/send_revit_command_durable.py`およびラッパースクリプトに以下の堅牢化修正を適用しました。

### 3.1. `Scripts/Reference/send_revit_command_durable.py`の改善
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
3.  **`Scripts/Reference/send_revit_command_durable.py`の出力問題**: `subprocess.DEVNULL`へのリダイレクトで`UnicodeDecodeError`は回避できたものの、`Scripts/Reference/send_revit_command_durable.py`自体がエラー時に適切なJSONを`--output-file`に書き出さないため、根本原因の特定に至らず。
4.  **ユーザーからの重要な指摘**: 「コマンドがアドインに届いていません」という指摘を受け、`ping_server`コマンドでRevitアドインへの基本的な到達性を確認。結果は成功し、コマンド自体は到達していることが判明。
5.  **根本原因の特定**: `ping_server`は成功するが詳細コマンドが失敗するという状況から、コマンドがアドインに到達した後にRevit内部でエラーが発生している可能性が高いと判断。この時点で、`Scripts/Reference/send_revit_command_durable.py`とラッパースクリプトの堅牢性不足がデバッグの大きな障壁となっていることが明確化。
6.  **ユーザーからの具体的な改善方針**: 「主な懸念は以下の4点」という形で、`subprocess.run`の安全な使用、タイムアウト、構造化エラー報告、一時ファイル管理に関する具体的な改善方針が提示される。
7.  **修正の適用と検証**: 提示された方針に基づき、`Scripts/Reference/send_revit_command_durable.py`とラッパースクリプト（`run_revit_command_silent`）を段階的に修正。特に`subprocess.run`の引数渡し方、`tempfile`の使用、エラー返却形式の統一に注力。
8.  **最終的な成功**: 修正後、`get_room_params`が正常に動作することを確認。これにより、当初の目的であった全部屋のパラメータ取得とJSONエクスポートが成功裏に完了。

## 5. 結論

Revitアドインとの連携において、Pythonスクリプト側の堅牢なエラーハンドリングと`subprocess`モジュールの適切な使用が極めて重要であることが再確認されました。特に、Revitアドインからのエラーメッセージが不明瞭な場合でも、Python側でエラーを構造化して捕捉・報告する仕組みを構築することで、デバッグと問題解決の効率が大幅に向上します。これにより、将来的な同様の問題発生時にも、迅速かつ的確な対応が可能となります。




