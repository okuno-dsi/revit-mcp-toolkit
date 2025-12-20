# export_lighting_report

- カテゴリ: LightingOps
- 目的: このコマンドは『export_lighting_report』を書き出しします。

## 概要
このコマンドは JSON-RPC を通じて実行され、目的に記載の処理を行います。使い方のセクションを参考にリクエストを作成してください。

## 使い方
- メソッド: export_lighting_report

- パラメータ: なし

### リクエスト例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "export_lighting_report",
  "params": {}
}
```

## 関連コマンド
## 関連コマンド
- get_lighting_power_summary
- check_lighting_energy
- estimate_illuminance_in_room
- 