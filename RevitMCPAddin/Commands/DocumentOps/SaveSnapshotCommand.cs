// RevitMCPAddin/Commands/DocOps/SaveSnapshotCommand.cs
// Target: .NET Framework 4.8 / Revit 2023+
// Purpose: Keep current doc as-is, save a timestamped snapshot alongside.

using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.DocOps
{
    /// <summary>
    /// Create a snapshot file without changing the current document path.
    /// Params:
    ///   - dir (string?, optional): 保存先ディレクトリ。未指定は元ファイルと同じ
    ///   - prefix (string?, optional): 先頭に付ける文字列（例: "SNAP_"）
    ///   - baseName (string?, optional): ベース名。未指定は元ファイル名（拡張子なし）
    ///   - autoTimestamp (bool?, optional): 末尾に "_yyyyMMddHHmmss" を付与（既定: true）
    ///   - timestampFormat (string?, optional): 既定 "yyyyMMddHHmmss"（数字のみ）
    /// Returns:
    ///   { ok:true, path:"C:/.../SNAP_Model_20251106143207.rvt" } or { ok:false, msg:"..." }
    /// </summary>
    public class SaveSnapshotCommand : IRevitCommandHandler
    {
        public string CommandName => "save_snapshot";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return ResultUtil.Err("No active document.");

                var currentPath = doc.PathName;
                if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
                    return ResultUtil.Err("Document has not been saved yet. Please save the document first.");

                // Params
                var p = (JObject)(cmd.Params ?? new JObject());
                var targetDir = p.Value<string>("dir");
                var prefix = p.Value<string>("prefix") ?? "";
                var baseName = p.Value<string>("baseName"); // null → 元ファイル名
                var autoTimestamp = p.Value<bool?>("autoTimestamp") ?? true; // 既定で付与
                var timestampFormat = p.Value<string>("timestampFormat") ?? "yyyyMMddHHmmss";

                if (string.IsNullOrWhiteSpace(targetDir))
                    targetDir = Path.GetDirectoryName(currentPath) ?? Environment.CurrentDirectory;
                Directory.CreateDirectory(targetDir);

                var stem = string.IsNullOrWhiteSpace(baseName)
                    ? Path.GetFileNameWithoutExtension(currentPath)
                    : baseName.Trim();

                // ファイル名を組み立て（アンダースコア + 数字のみの時刻）
                string nameCore = prefix + stem;
                if (autoTimestamp)
                {
                    var ts = DateTime.Now.ToString(timestampFormat);
                    nameCore += "_" + ts;
                }
                var snapshotName = nameCore + ".rvt";
                var snapshotPath = Path.Combine(targetDir, snapshotName);

                var isWorkshared = doc.IsWorkshared;

                if (!isWorkshared)
                {
                    // --- Non-workshared: Save then File.Copy ---
                    if (doc.IsModified)
                        doc.Save(); // 画面上の変更を反映

                    File.Copy(currentPath, snapshotPath, overwrite: true);
                    RevitLogger.Info($"save_snapshot: copied '{currentPath}' -> '{snapshotPath}'");
                    return new { ok = true, path = snapshotPath };
                }
                else
                {
                    // --- Workshared: open central as detached, SaveAs to snapshot, then close ---
                    var app = uiapp.Application;

                    // Try to get central model path
                    ModelPath centralPath = null;
                    try { centralPath = doc.GetWorksharingCentralModelPath(); } catch { /* ignore */ }

                    // Detect cloud (BIM 360 / Autodesk Docs) by user-visible central path prefix
                    bool isCloud = false;
                    try
                    {
                        if (centralPath != null)
                        {
                            var uv = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralPath) ?? "";
                            if (uv.StartsWith("BIM 360://", StringComparison.OrdinalIgnoreCase) ||
                                uv.StartsWith("Autodesk Docs://", StringComparison.OrdinalIgnoreCase))
                            {
                                isCloud = true;
                            }
                        }
                    }
                    catch { /* ignore */ }

                    if (isCloud)
                        return ResultUtil.Err("Cloud workshared models (BIM 360 / Autodesk Docs) are not supported by this snapshot method.");

                    // Fallback: build model path from current local path if centralPath is null
                    if (centralPath == null)
                    {
                        try
                        {
                            centralPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(currentPath);
                        }
                        catch (Exception ex)
                        {
                            return ResultUtil.Err("Cannot resolve central model path: " + ex.Message);
                        }
                    }

                    Document detached = null;
                    try
                    {
                        var openOps = new OpenOptions
                        {
                            DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets,
                            Audit = false
                        };
                        try { openOps.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets)); } catch { }

                        detached = app.OpenDocumentFile(centralPath, openOps);

                        var saveOps = new SaveAsOptions
                        {
                            OverwriteExistingFile = true,
                            Compact = false,
                            MaximumBackups = 1
                        };
                        var wops = new WorksharingSaveAsOptions { SaveAsCentral = false };
                        saveOps.SetWorksharingOptions(wops);

                        var snapModelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(snapshotPath);
                        detached.SaveAs(snapModelPath, saveOps);

                        RevitLogger.Info($"save_snapshot: detached-save '{snapshotPath}' from central.");
                        return new { ok = true, path = snapshotPath };
                    }
                    catch (Exception ex)
                    {
                        return ResultUtil.Err("Failed to create snapshot from workshared model: " + ex.Message);
                    }
                    finally
                    {
                        try { detached?.Close(false); } catch { /* ignore */ }
                    }
                }
            }
            catch (Exception ex)
            {
                return ResultUtil.Err("Unexpected error in save_snapshot: " + ex.Message);
            }
        }
    }
}
