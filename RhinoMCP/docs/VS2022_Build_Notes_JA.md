# VS2022 でのビルド注意点（オープンソース公開を想定）

このドキュメントは、公開後に各ユーザーが自分のPC環境で VS2022 を用いて RhinoMCP をビルド・動作させる際の注意点をまとめたものです。Rhino 7（.NET Framework 4.8）と .NET 6（サーバ）の混在構成ならではのポイントに留意してください。

## 前提ソフトウェア

- Windows 10/11（管理者権限推奨）
- Visual Studio 2022（Community可）
  - .NET デスクトップ開発 ワークロード（MSBuild含む）
- .NET SDK 6.x 以上（8/9が入っていても可）
- Rhino 7（ライセンス必要）
- Revit MCP（任意。commit動作確認には必要）
- PowerShell 7（推奨、スクリプト実行用）

## プロジェクト構成とターゲット

- RhinoMcpPlugin
  - .NET Framework 4.8（古い形式のcsproj）
  - 参照: RhinoCommon.dll（Rhino 7 同梱）
  - 出力: `bin\\Debug\\RhinoMcpPlugin.dll` + PostBuildで `.rhp` を同フォルダにコピー

- RhinoMcpServer
  - .NET 6（`Microsoft.NET.Sdk.Web`）
  - 依存: `Microsoft.AspNetCore.Mvc.NewtonsoftJson`
  - 出力: `bin\\Debug\\net6.0\\RhinoMcpServer.dll`

## RhinoCommon の参照パス

- 既定は以下を自動検出するようにしています（csproj内の`HintPath`条件）。
  - `C:\\Program Files\\Rhino 7\\System\\RhinoCommon.dll`
  - `C:\\Program Files (x86)\\Rhino 7\\System\\RhinoCommon.dll`
- 上記以外の場所に Rhino をインストールしている場合、`RhinoMcpPlugin\\RhinoMcpPlugin.csproj` の該当 `HintPath` を修正してください。

## Newtonsoft.Json の参照

- プラグインは .NET Framework 4.8 用に `Newtonsoft.Json` を参照しています。
- 既定ではユーザーの NuGet キャッシュ（例: `C:\\Users\\<USER>\\.nuget\\packages\\newtonsoft.json\\13.0.3\\lib\\net45\\Newtonsoft.Json.dll`）を参照する設定にしています。
- 該当フォルダが存在しない場合、VSのNuGet パッケージマネージャーで `Newtonsoft.Json` を追加インストールするか、参照パスを環境に合わせて変更してください。

## 出力とPostBuild（.rhp生成）

- `RhinoMcpPlugin.csproj` には PostBuild イベントで `RhinoMcpPlugin.rhp` を `bin\\Debug` にコピーする設定があります。
- セキュリティソフトやアクセス権限の影響でコピーに失敗する場合は、VSを管理者モードで実行するか、出力先を書き換えてください。

## VS2022でのビルド方法

- それぞれの `.csproj` を VS2022 で開いてビルド可能です（ソリューションファイルが無くてもOK）。
- 構成: Debug / AnyCPU（プラグイン側は OutputPath が条件付きで設定済み）
- ビルド順序:
  1) `RhinoMcpServer`（`dotnet build` でも可）
  2) `RhinoMcpPlugin`（VS2022のビルドでOK）

## 実行・検証の流れ

1) サーバ起動
- PowerShell: `pwsh scripts/start_server.ps1 -Url http://127.0.0.1:5200`
- ヘルス: `GET http://127.0.0.1:5200/healthz`

2) Rhino 7 プラグインをRhinoにインストール
- Rhino 7 → Tools → Options → Plug-ins → Install…
- `RhinoMcpPlugin\\bin\\Debug\\RhinoMcpPlugin.rhp` を選択

3) テスト実行
- `pwsh scripts/test_rpc.ps1 -Base http://127.0.0.1:5200`
- 最小スナップショット `testdata/snapshot_min.json` を使って import/selection/commit を確認
- commit確認には Revit MCP (http://127.0.0.1:5210) の起動が必要

## ポートとファイアウォール

- 既定ポート
  - サーバ: 5200
  - プラグインIPC: 5201
  - Revit MCP: 5210
- 変更する場合は以下を修正
  - `RhinoMcpServer/Program.cs`（UseUrls）
  - `RhinoMcpServer/Rpc/Rhino/PluginIpcClient.cs`（IPC先）
  - `RhinoMcpPlugin/Core/PluginIpcServer.cs`（IPC元）
- Windows ファイアウォールやセキュリティソフトでブロックされる場合は、許可設定が必要です。

## よくある問題と対処

- `bin`配下のDLLがロックされてビルドに失敗
  - 既に起動中の `dotnet` プロセスがファイルを掴んでいる可能性 → タスクマネージャで停止、または `Stop-Process -Name dotnet -Force`
- HTTP 500 が返る
  - 本実装はJSON-RPCのエラーも原則HTTP 200で返す設計です。500が出る場合はサーバの旧バイナリが動作中の可能性 → プロセスを終了し最新ビルドに差し替え
- RhinoCommon 参照解決エラー
  - Rhino のインストールパスに合わせて `HintPath` を修正
- Rhino 8 を使う場合
  - RhinoCommon のバージョンが変わるため、参照先をRhino 8のパスに変更。API差分があればコード側の微調整が必要です。

## ログの場所

- サーバ: `%LOCALAPPDATA%\\RhinoMCP\\logs\\RhinoMcpServer.log`
- プラグイン: `%LOCALAPPDATA%\\RhinoMCP\\logs\\RhinoMcpPlugin.log`

## ランタイム要件とSDK

- サーバ: .NET 6 互換。手元のSDKが8/9でもビルド可（ターゲットはnet6.0）
- プラグイン: .NET Framework 4.8（VS2022 + .NETデスクトップワークロード）

## 配布に向けて

- プラグインは `.rhp` の単一配布でも動作しますが、ユーザー環境差（RhinoCommonの場所等）に留意してください。
- サーバは `dotnet publish -c Release -r win-x64` で自己完結型の配布物を用意可能です（必要に応じて）。

以上の点を満たせば、ユーザーは VS2022 上で自身の環境に合わせてビルド・実行できます。

