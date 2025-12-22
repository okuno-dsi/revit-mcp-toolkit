# Revit 2025 対応（.NET 8 移行メモ）

## 結論（重要）
- Revit 2025 の `RevitAPI.dll` / `RevitAPIUI.dll` は **`.NETCoreApp,Version=v8.0`** 向けです。
- 現行の `RevitMCPAddin` は **.NET Framework 4.8** プロジェクトのため、**Revit 2025 の API を参照してビルドできません**（WPF/参照解決でビルドエラーになります）。
- したがって Revit 2025 対応は **`net8.0-windows` の別プロジェクト（別ビルド）** が必要です。

## 現状のビルド方針
- `RevitMCPAddin`（net48）は Revit 2024/2023 を参照してビルドします。
  - `RevitMCPAddin.csproj` の `RevitInstallDir` は 2024→2023 の順で自動解決します（2025 は参照しません）。

## 事前対応として行ったこと（API 非推奨の吸収）
Revit 2024+ の API 変更に備えて、コード側は以下の互換層に寄せました。
- `ElementId.IntegerValue` → `.IntValue()`（互換拡張）
- `new ElementId(int)` → `Autodesk.Revit.DB.ElementIdCompat.From(...)`（互換生成）

実体:
- `RevitMCPAddin/Core/Compat/ElementIdCompat.cs`

この互換層は、将来 `ElementId.IntegerValue` が削除された場合でも影響範囲を最小化できます。

## Revit 2025（net8）版で必要になる作業（ToDo）
- SDK スタイルの `net8.0-windows` クラスライブラリとして新規 csproj を作成
- Revit 2025 の `RevitAPI(.UI).dll` 参照に切替
- 依存パッケージ（ClosedXML / OpenXml / netDxf / Office Interop 等）の **net8 対応可否**を精査し、必要なら置換/分離
- 既存コードの共有方針（リンク/共有プロジェクト/共通ライブラリ化）を決める

