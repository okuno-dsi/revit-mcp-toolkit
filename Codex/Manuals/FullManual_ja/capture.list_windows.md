# capture.list_windows

- カテゴリ: MetaOps
- 種別: read
- 目的: Windows デスクトップ上のトップレベルウィンドウ一覧を取得します（サーバー側のみ／Revitキュー非使用）。

## 概要
Win32 API（`EnumWindows`）でトップレベルウィンドウを列挙し、基本メタ情報を返します。

これは **サーバー側のみ**で完結します：
- Revit API は呼びません。
- Revit がビジーでも動作します（キューに積みません）。

## パラメータ
| 名前 | 型 | 必須 | 既定 |
|---|---|---|---|
| processName | string | no |  |
| titleContains | string | no |  |
| visibleOnly | bool | no | true |

## 例
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "capture.list_windows",
  "params": {
    "processName": "Revit",
    "titleContains": "エラー",
    "visibleOnly": true
  }
}
```

## 結果
- `windows[]`: `{ hwnd, pid, process, title, className, bounds:{x,y,w,h} }`

## 関連
- capture.window
- capture.screen
- capture.revit

