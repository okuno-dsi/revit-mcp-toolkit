# ビルド手順

本書は以下の主要コンポーネントのビルド手順をまとめたものです。
- RevitMCPServer（.NET 8）
- RevitMCPAddin 2024（.NET Framework 4.8）
- RevitMCPAddin 2025 / 2026（.NET 8）
- SmartOpen 2024（.NET Framework 4.8）
- SmartOpen 2025 / 2026（.NET 8）

---

## 必要環境

- Windows
- Visual Studio 2022
- .NET 8 SDK
- .NET Framework 4.8 Developer Pack
- Autodesk Revit 2024 / 2025 / 2026

---

## RevitMCPServer（.NET 8）

### CLI でビルド

```powershell
dotnet build RevitMCPServer/RevitMCPServer.csproj -c Release
```

### Visual Studio でビルド
`RevitMCPServer/RevitMCPServer.sln` を開いてビルドします。

---

## RevitMCPAddin 2024（.NET Framework 4.8）

Revit API の参照が必要です。`RevitMCPAddin/RevitMCPAddin.csproj` は以下を自動検出します。

- `C:\Program Files\Autodesk\Revit 2024`
- `C:\Program Files\Autodesk\Revit 2023`

Revit のインストール先が異なる場合は環境変数 `RevitInstallDir` を指定してください。

```powershell
$env:RevitInstallDir="C:\Program Files\Autodesk\Revit 2024"
```

### Visual Studio でビルド（推奨）
`RevitMCPAddin/RevitMCPAddin.sln` を開いて NuGet を復元し、Release でビルドします。

### CLI でビルド（必要な場合）

```powershell
nuget restore RevitMCPAddin/RevitMCPAddin.sln
msbuild RevitMCPAddin/RevitMCPAddin.sln /p:Configuration=Release
```

---

## RevitMCPAddin 2025 / 2026（.NET 8）

`RevitMCPAddin/RevitMCPAddin.Net8.csproj` を使用します。`RevitYear` により参照先 API と出力先を切り替えます。

### CLI でビルド

```powershell
dotnet build RevitMCPAddin/RevitMCPAddin.Net8.csproj -c Release -p:RevitYear=2025
dotnet build RevitMCPAddin/RevitMCPAddin.Net8.csproj -c Release -p:RevitYear=2026
```

### 出力先

- `Artifacts\RevitMCPAddin\2025\bin\Release\net8.0-windows`
- `Artifacts\RevitMCPAddin\2026\bin\Release\net8.0-windows`

### インストール

```powershell
pwsh -ExecutionPolicy Bypass -File Codex\Tools\PowerShellScripts\install_revitmcp_safe.ps1 -RevitYears 2025,2026
```

インストール先は `%APPDATA%\Autodesk\Revit\Addins\<year>\RevitMCPAddin` です。manifest は `%APPDATA%\Autodesk\Revit\Addins\<year>\RevitMCPAddin.addin` に生成されます。

## SmartOpen 2025 / 2026（.NET 8）

`Menu/SmartOpen/SmartOpen.Net8.csproj` を使用します。`RevitYear` により参照先 API と出力先を切り替えます。

### CLI でビルド

```powershell
dotnet build Menu/SmartOpen/SmartOpen.Net8.csproj -c Release -p:RevitYear=2025
dotnet build Menu/SmartOpen/SmartOpen.Net8.csproj -c Release -p:RevitYear=2026
```

### 出力先

- `Artifacts\SmartOpen\2025\bin\Release\net8.0-windows`
- `Artifacts\SmartOpen\2026\bin\Release\net8.0-windows`

### 配置先

- `%APPDATA%\Autodesk\Revit\Addins\2025\SmartOpen`
- `%APPDATA%\Autodesk\Revit\Addins\2026\SmartOpen`

manifest は各年の Addins 直下に `SmartOpen.addin` として配置します。

## 出力先

ビルド成果物は、2024 系は各プロジェクトの `bin/Release`、2025/2026 系は `Artifacts` 配下に出力されます。  
公開時はバイナリをリポジトリに含めないでください。
