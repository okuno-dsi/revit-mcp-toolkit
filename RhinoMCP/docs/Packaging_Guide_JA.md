# RhinoMCP 配布パッケージング手順（Windows）

本ドキュメントは、RhinoMCP を第三者へ配布可能な形にまとめるための手順です。対象は Rhino 7 用プラグイン（.rhp）と .NET 6 サーバ（Kestrel）です。

## 配布物の全体像

- プラグイン（RhinoMcpPlugin）
  - 配布単位: `.rhp`（必須）、オプションで `.rui`（ツールバー）
  - 依存: Rhino 7 本体（`RhinoCommon.dll` は Rhino 側に同梱）
- サーバ（RhinoMcpServer）
  - 配布単位: フォルダ（フレームワーク依存版）または 自己完結（Self‑contained）
  - 依存: .NET 6 ランタイム（フレームワーク依存の場合のみ）

## バージョニングの指針

- プラグイン: `RhinoMcpPlugin/Properties/AssemblyInfo.cs` の `AssemblyVersion`/`AssemblyFileVersion` を更新
- サーバ: `RhinoMcpServer.csproj` に `<Version>` を付与するか、CIで `-p:Version=` を注入
- 配布アーカイブ名に `vX.Y.Z` を含める（例: `RhinoMCP-v1.0.0-win-x64.zip`）

## ビルド（Release）

### 1) プラグイン（.rhp）

- Visual Studio 2022 で `RhinoMcpPlugin` を Release/AnyCPU でビルド
- PostBuild で `RhinoMcpPlugin.rhp` が `bin\\Release` にコピーされる設定がない場合は手動コピー
- 配布に含めるファイルの目安:
  - `RhinoMcpPlugin\\bin\\Release\\RhinoMcpPlugin.rhp`
  - `RhinoMcpPlugin\\UI\\Toolbar.rui`（任意）
  - `README_Plugin.txt`（後述テンプレート例）

### 2) サーバ（publish）

- フレームワーク依存版（.NET ランタイム別途）：
  - `dotnet publish RhinoMcpServer\\RhinoMcpServer.csproj -c Release -o publish\\fd`
- 自己完結版（推奨、ユーザーがランタイム不要）：
  - `dotnet publish RhinoMcpServer\\RhinoMcpServer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\\sc`
- 出力例:
  - `publish\\fd\\`（`RhinoMcpServer.dll` 他）
  - `publish\\sc\\`（単一EXEまたは最小構成）

## 既定ポートと設定

- 既定ポート
  - サーバ: 5200（`RhinoMcpServer/Program.cs`）
  - プラグインIPC: 5201（`RhinoMcpPlugin/Core/PluginIpcServer.cs`）
  - Revit MCP: 5210（`RhinoMcpPlugin/Plugin.cs` 既定値）
- サーバの待受ポートを変更する場合:
  - 実行時に `ASPNETCORE_URLS` を指定（例）
    - Windows CMD: `set ASPNETCORE_URLS=http://127.0.0.1:5300`
    - PowerShell: `$env:ASPNETCORE_URLS='http://127.0.0.1:5300'`
- プラグイン側のURLはプラグイン読み込み後にコマンドや設定パネルで出力される既定値を参照（必要ならコードで初期値を変更）

## 配布パッケージの構成例

zip 1本に同梱する想定（自己完結版を例示）

```
RhinoMCP-v1.0.0-win-x64.zip
  /server/
    RhinoMcpServer.exe            ← publish/sc のEXE
    start_server.cmd              ← 例: set ASPNETCORE_URLS & 起動
    README_Server.txt
  /plugin/
    RhinoMcpPlugin.rhp            ← bin/Release
    Toolbar.rui                   ← 任意
    README_Plugin.txt
  /docs/
    RhinoMCP_Implementation_Overview.md
    RhinoMCP_JSONRPC_API.md
    RhinoMCP_Operations_Runbook.md
    VS2022_Build_Notes_JA.md
    Packaging_Guide_JA.md
```

### start_server.cmd（例）

```
@echo off
set ASPNETCORE_URLS=http://127.0.0.1:5200
start "RhinoMCP Server" "%~dp0RhinoMcpServer.exe"
```

### README_Plugin.txt（例）

```
[Install]
1) Rhino 7 を起動
2) Tools > Options > Plug-ins > Install…
3) plugin\RhinoMcpPlugin.rhp を選択

[Usage]
- サーバ(server/start_server.cmd)を起動
- 最小テスト: scripts/test_rpc.ps1 を実行（要 PowerShell 7）
```

## Rhinoプラグインの配布形式

- .rhp 直接配布（本手順）
  - 長所: 最小・簡易
  - 短所: 手動インストール手順が必要
- .rhi（zipを`.rhi`にリネーム）
  - zipに `.rhp` や関連ファイルを含め `.rhi` 拡張子へ変更
  - ダブルクリックで Rhino のRHIインストーラが走り、自動配置
  - 注意: WindowsのSmartScreenやゾーン情報でブロックされた場合は “ブロック解除” が必要
- Yak（Rhino Package Manager）での配布
  - Yak CLI でパッケージ化し、社内/公開フィードへ配布
  - 参照: https://www.rhino3d.com/features/package-manager/

## 署名・セキュリティ（任意）

- 企業配布ではコード署名を検討（自己完結EXE と .rhp）
- zip 配布時は `SHA256` ハッシュを併記

## 受入テスト（出荷前チェック）

- クリーンWindows上で以下を確認
  - サーバ起動（5200）と `/healthz` 応答
  - プラグインインストール → IPC待受（5201）
  - `scripts/test_rpc.ps1` にて import / selection / commit（Revit MCP起動時）

## トラブルシューティング（配布後）

- 「HTTP 500」: 旧サーバが動作中 → 全ての `dotnet` を停止 → 最新に差し替え
- 「IPC接続失敗」: プラグイン未ロード/ポート不一致 → プラグイン読み込みとポート確認
- 「ビルドできない」: VS2022ワークロード不足 / RhinoCommonの参照不一致 → `VS2022_Build_Notes_JA.md` を参照

---

必要に応じて NSIS / WiX などのインストーラで上記フォルダ構成を1クリック導入にまとめられます。初期段階は zip 配布 + 手順書で十分です。

