# Loading a .NET App with `accoreconsole.exe` to List Layers

This document explains how to use the AutoCAD Core Console (`accoreconsole.exe`) to load a custom .NET application (`LayerLister.dll`) and execute a command to export layer names from a DWG file. This method is significantly faster than using standard AutoCAD commands for scripting, as it leverages a purpose-built application.

## Components

1.  **`accoreconsole.exe` (AutoCAD Core Console):** A command-line tool that allows for running AutoCAD commands and scripts on DWG files without the graphical user interface. It's ideal for batch processing and automation.

2.  **`LayerLister.dll` (Custom .NET Application):** A .NET library built to extend AutoCAD's functionality. It contains a custom command, `LISTLAYERS`, which is programmed to perform a specific task: iterating through all layers in a drawing and writing their names to an external text file.

3.  **`load_and_run.scr` (AutoCAD Script File):** A simple text file containing a sequence of commands that AutoCAD can execute. In this case, it's used to load the .NET application and then run the custom command.

## Process Overview

The process is executed with a single command in the shell, which starts `accoreconsole.exe` and instructs it to run the script on a specific DWG file.

1.  **Start `accoreconsole.exe`:** The core console is launched with the target DWG file (`Merged_B_G.dwg`) as the active drawing and `load_and_run.scr` as the script to run.

2.  **Execute Script:** `accoreconsole.exe` runs the commands in `load_and_run.scr`.

3.  **Load .NET Assembly:** The `NETLOAD` command in the script tells AutoCAD to load our custom `LayerLister.dll` into its environment.

4.  **Run Custom Command:** The script then calls `LISTLAYERS`, the custom command defined in our .NET application.

5.  **Export Layers:** The C# code for the `LISTLAYERS` command runs. It accesses the drawing's database, gets the list of all layers, and writes their names to a new file: `Merged_B_G.layers.utf8.txt`.

## Code Details

### 1. Shell Command

This is the command used to initiate the entire process from the command line.

```powershell
& "C:\Program Files\Autodesk\AutoCAD 2026\accoreconsole.exe" /i "%USERPROFILE%\Documents\VS2022\Ver501\Codex\Projects\\AutoCadOut\Export_20251102_134250\Merged_B_G.dwg" /s "%USERPROFILE%\Documents\VS2022\Ver501\Codex\Projects\\AutoCadOut\Export_20251102_134250\load_and_run.scr"
```

-   `/i`: Specifies the input drawing file.
-   `/s`: Specifies the script file to run after opening the drawing.

### 2. AutoCAD Script (`load_and_run.scr`)

This script loads the DLL and runs the command. It also sets some system variables to ensure the script runs without interruption.

```
_.SETVAR
SECURELOAD
0
_.SETVAR
FILEDIA
0
_.SETVAR
CMDDIA
0
_.SETVAR
TRUSTEDPATHS
"%USERPROFILE%\Documents\VS2022\Ver501\Codex\Projects\\AutoCadOut\Export_20251102_134250;%USERPROFILE%\Documents\VS2022\Ver501\Codex\Projects\\AutoCadOut\Export_20251102_134250\bin\x64\Release\net8.0-windows"
NETLOAD
"%USERPROFILE%\Documents\VS2022\Ver501\Codex\Projects\\AutoCadOut\Export_20251102_134250\bin\x64\Release\net8.0-windows\LayerLister.dll"
LISTLAYERS
```

-   `NETLOAD`: Loads the specified .NET assembly.
-   `LISTLAYERS`: Executes the custom command from the loaded assembly.

### 3. C# Source Code (`LayerLister.cs`)

This is the core logic inside `LayerLister.dll`. The `CommandMethod` attribute exposes the `ListLayers` method as the `LISTLAYERS` command in AutoCAD.

```csharp
using System;
using System.IO;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices.Core;

public class LayerLister
{
    [CommandMethod("LISTLAYERS", CommandFlags.Modal)]
    public void ListLayers()
    {
        var db = HostApplicationServices.WorkingDatabase;
        string dwgPath = db?.Filename ?? string.Empty;
        string dir = string.IsNullOrEmpty(dwgPath) ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(dwgPath);
        string outPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(dwgPath) + ".layers.utf8.txt");

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            using (var sw = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false)))
            {
                foreach (ObjectId id in lt)
                {
                    var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    sw.WriteLine(ltr.Name);
                }
            }
            tr.Commit();
        }
    }
}
```

This approach provides a highly efficient and automatable way to extract data from DWG files, bypassing the slower, more interactive standard AutoCAD commands.

