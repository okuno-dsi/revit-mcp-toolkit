# Build Guide

This document covers build steps for:
- RevitMCPServer (.NET 8)
- RevitMCPAddin (.NET Framework 4.8)

---

## Requirements

- Windows
- Visual Studio 2022
- .NET 8 SDK
- .NET Framework 4.8 Developer Pack
- Autodesk Revit 2023 or 2024 (RevitAPI.dll / RevitAPIUI.dll)

Note: Revit 2025 cannot be built with .NET Framework 4.8. See `Docs/Manuals/Revit2025_NET8_Migration_JA.md`.

---

## RevitMCPServer (.NET 8)

### Build with CLI

```powershell
dotnet build RevitMCPServer/RevitMCPServer.csproj -c Release
```

### Build with Visual Studio
Open `RevitMCPServer/RevitMCPServer.sln` and build.

---

## RevitMCPAddin (.NET Framework 4.8)

Revit API references are required. `RevitMCPAddin/RevitMCPAddin.csproj` auto-detects:

- `C:\Program Files\Autodesk\Revit 2024`
- `C:\Program Files\Autodesk\Revit 2023`

If your Revit install path is different, set `RevitInstallDir`:

```powershell
$env:RevitInstallDir="C:\Program Files\Autodesk\Revit 2024"
```

### Build with Visual Studio (recommended)
Open `RevitMCPAddin/RevitMCPAddin.sln`, restore NuGet packages, and build Release.

### Build with CLI (if needed)

```powershell
nuget restore RevitMCPAddin/RevitMCPAddin.sln
msbuild RevitMCPAddin/RevitMCPAddin.sln /p:Configuration=Release
```

---

## Output

Build outputs go to each project's `bin/Release` folder.  
Do not commit binaries to the repository.
