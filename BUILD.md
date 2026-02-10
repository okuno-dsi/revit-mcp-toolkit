# ビルド手順

本書は以下の主要コンポーネントのビルド手順をまとめたものです。
- RevitMCPServer（.NET 8）
- RevitMCPAddin（.NET Framework 4.8）

---

## 必要環境

- Windows
- Visual Studio 2022
- .NET 8 SDK
- .NET Framework 4.8 Developer Pack
- Autodesk Revit 2023 または 2024（RevitAPI.dll / RevitAPIUI.dll）

※ Revit 2025 は .NET Framework 4.8 ではビルドできないため、別途 `Docs/Manuals/Revit2025_NET8_Migration_JA.md` を参照してください。

---

## RevitMCPServer（.NET 8）

### CLI でビルド

```powershell
dotnet build RevitMCPServer/RevitMCPServer.csproj -c Release
```

### Visual Studio でビルド
`RevitMCPServer/RevitMCPServer.sln` を開いてビルドします。

---

## RevitMCPAddin（.NET Framework 4.8）

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

## 出力先

ビルド成果物は各プロジェクトの `bin/Release` に出力されます。  
公開時はバイナリをリポジトリに含めないでください。
