# 🧪 Revit MCP スモークテスト手順書

本書は、Revit MCP 環境における **スモークテスト（Smoke Test）** の実施手順をまとめたものです。
AIエージェントやCLI経由のコマンド実行前に、すべての書き込み操作 (`kind: "write"`) に対して安全確認を行うことを目的とします。

---

## 🔧 1. スモークテストの目的

- **誤実行の防止**  
  存在しないコマンドや無効なパラメータを実行する前に検出。

- **高影響操作の警告**  
  `importance: "high"` の書き込み系コマンド（壁作成、要素削除など）は実行前に警告を表示。

- **AIエージェントの安全運用**  
  JSON-RPC 実行前に `smoke_test` メソッドを通すことで、Revitデータを安全に保護。

---

## 🧩 2. 環境構成

| 層 | 役割 | 主要ファイル |
|----|------|--------------|
| **Abstractions** | 共通RPC基盤（Kind/Router） | `IRpcCommand.cs`, `RpcRouter.cs` |
| **Server (.NET6)** | スモーク検証・実行本体 | `SmokeTestCommand.cs`, `CommandRegistry.cs` |
| **Add-in (.NET4.8)** | Revit API 実処理層 | `CommandRegistry.cs` |
| **CLI/AI** | コマンド呼び出し | `..\..\NVIDIA-Nemotron-v3\tool\revit_agent_cli.py` |

---

## ⚙️ 3. コマンドの流れ

1. クライアントは JSON-RPC リクエストを生成。  
2. `smoke_test` を先に実行し、指定メソッドが安全かを検証。  
3. `ok:true` が返れば、本リクエストに `__smoke_ok:true` を付与して再実行。  
4. `RpcRouter.Execute()` が受け取り、`Kind==Write` 且つ `__smoke_ok!=true` の場合は拒否。

---

## 🧠 4. 手動テスト手順

### 4.1 サーバー起動

```bash
C:\RevitMcpServer\bin\Release\net6.0\RevitMcpServer.exe --port 5210
```

### 4.2 スモークテスト実行例

#### 壁タイプ取得（read系）
```bash
python ..\\..\\NVIDIA-Nemotron-v3\\tool\\revit_agent_cli.py --port 5210 --method get_wall_types
```

結果例：
```json
{ "ok": true, "msg": "'get_wall_types' looks valid." }
```

#### 壁パラメータ更新（write系）
```bash
python ..\\..\\NVIDIA-Nemotron-v3\\tool\\revit_agent_cli.py --port 5210 --method update_wall_parameter --params '{"elementId":123,"paramName":"Comments","value":"Test"}'
```

結果例（初回の smoke_test）:
```json
{
  "ok": true,
  "msg": "Command 'update_wall_parameter' is high-impact write. Confirm before execution.",
  "severity": "warn"
}
```
→ プロンプトに `Proceed? [y/N]:` が表示され、`y` を入力後に実行。

---

## 🧩 5. 自動スモーク検証の流れ（CLI 内部）

`..\..\NVIDIA-Nemotron-v3\tool\revit_agent_cli.py` の内部処理：

1. `smoke_test` を呼び出し  
   ```python
   smoke = send_revit(port, "smoke_test", {"method": method, "params": params})
   ```
2. `ok:false` → 実行中止  
3. `severity:"warn"` → ユーザー確認  
4. `ok:true` → 実際のコマンドに `__smoke_ok:true` を注入して実行  

---

## 🛡️ 6. エラーレスポンス一覧

| エラーコード | 意味 | 発生箇所 |
|---------------|------|----------|
| `invalid_method` | method パラメータが欠落 | SmokeTestCommand |
| `unknown_command` | 未登録コマンド | SmokeTestCommand |
| `missing_id` | ID系パラメータ不足 | SmokeTestCommand |
| `smoke_required` | smoke_test 未通過 | RpcRouter |
| `execution_error` | コマンド実行中に例外発生 | RpcRouter |
| `smoke_transport_error` | 通信異常 | CLI |

---

## 📦 7. Add-in 動作確認

1. `RevitMCPAddin.dll` と同フォルダに `commands_index.json` を配置。  
2. Revit 起動時にログを確認：  
   - `RevitMcpAddin.log` に `[INFO] commands_index loaded` が出ていればOK。  
3. smoke_test に合格したコマンドのみ Add-in が受理。

---

## 🧾 8. 結論

- **Revit MCP の全 write コマンドは smoke_test を必須とする。**
- Abstractions の `RpcRouter` が安全ゲート。  
- Add-in 側の CommandRegistry は read-only 参照のみ。  
- CLI (`..\..\NVIDIA-Nemotron-v3\tool\revit_agent_cli.py`) により smoke_test 自動実施が保証される。

---

**更新日:** 2025-10-07  
**作成者:** Revit MCP 開発チーム

