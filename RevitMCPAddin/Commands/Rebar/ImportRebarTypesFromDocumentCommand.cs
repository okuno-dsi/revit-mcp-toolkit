// ================================================================
// Command: import_rebar_types_from_document
// Purpose: Import RebarBarType / RebarHookType (and optionally RebarShape)
//          from a source .rvt/.rte into the active document.
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Kind   : write
// ================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rebar
{
    [RpcCommand("import_rebar_types_from_document",
        Category = "Rebar",
        Kind = "write",
        Risk = RiskLevel.Medium,
        Summary = "Import RebarBarType/RebarHookType (and optional RebarShape) from a source .rvt/.rte into the active document.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"import_rebar_types_from_document\", \"params\":{ \"dryRun\":true, \"sourcePath\":\"C:/ProgramData/Autodesk/RVT 2024/Templates/Default_M_JPN.rte\", \"includeHookTypes\":true } }"
    )]
    public sealed class ImportRebarTypesFromDocumentCommand : IRevitCommandHandler
    {
        public string CommandName => "import_rebar_types_from_document";

        private sealed class UseDestinationTypesHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params as JObject ?? new JObject();

            bool dryRun = p.Value<bool?>("dryRun") ?? true;
            bool includeHookTypes = p.Value<bool?>("includeHookTypes") ?? true;
            bool includeShapes = p.Value<bool?>("includeShapes") ?? false;

            string sourcePath = (p.Value<string>("sourcePath") ?? string.Empty).Trim();

            var preferredDiameters = new HashSet<int>();
            try
            {
                var arr = p["diametersMm"] as JArray;
                if (arr != null)
                {
                    foreach (var t in arr)
                    {
                        int v = 0;
                        try { v = t.Value<int>(); } catch { v = 0; }
                        if (v > 0) preferredDiameters.Add(v);
                    }
                }
            }
            catch { /* ignore */ }

            var existingBarTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .ToList();

            var existingHookTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(RebarHookType))
                .Cast<RebarHookType>()
                .ToList();

            var existingShapes = new FilteredElementCollector(doc)
                .OfClass(typeof(RebarShape))
                .Cast<RebarShape>()
                .ToList();

            var result = new JObject
            {
                ["ok"] = true,
                ["dryRun"] = dryRun,
                ["existing"] = new JObject
                {
                    ["rebarBarTypeCount"] = existingBarTypes.Count,
                    ["rebarHookTypeCount"] = existingHookTypes.Count,
                    ["rebarShapeCount"] = existingShapes.Count
                }
            };

            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                candidates.Add(sourcePath);
            }
            else
            {
                // Best-effort: try common installed templates and a user template.
                candidates.Add(@"C:\ProgramData\Autodesk\RVT 2024\Templates\Default_M_JPN.rte");
                candidates.Add(@"C:\ProgramData\Autodesk\RVT 2024\Templates\Default_M_ENU.rte");
                candidates.Add(@"C:\ProgramData\Autodesk\RVT 2024\Templates\Default_M_ENG.rte");
                candidates.Add(@"C:\ProgramData\Autodesk\RVT 2023\Templates\Default_M_JPN.rte");
                candidates.Add(@"C:\ProgramData\Autodesk\RVT 2023\Templates\Default_M_ENU.rte");
                var userTemplate = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "意匠テンプレートVER2024_rev1.15.rte");
                candidates.Add(userTemplate);
            }

            var tried = new JArray();
            result["tried"] = tried;

            Document srcDoc = null;
            string usedPath = string.Empty;

            foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var entry = new JObject { ["path"] = c };
                tried.Add(entry);

                if (string.IsNullOrWhiteSpace(c) || !File.Exists(c))
                {
                    entry["ok"] = false;
                    entry["code"] = "NOT_FOUND";
                    continue;
                }

                try
                {
                    srcDoc = uiapp.Application.OpenDocumentFile(c);
                    usedPath = c;
                    entry["ok"] = true;
                    entry["opened"] = true;

                    int barCount = 0;
                    try
                    {
                        barCount = new FilteredElementCollector(srcDoc).OfClass(typeof(RebarBarType)).GetElementCount();
                    }
                    catch { barCount = 0; }

                    entry["rebarBarTypeCount"] = barCount;

                    if (barCount <= 0)
                    {
                        try { srcDoc.Close(false); } catch { /* ignore */ }
                        srcDoc = null;
                        usedPath = string.Empty;
                        continue;
                    }

                    // Keep this source doc.
                    break;
                }
                catch (Exception ex)
                {
                    entry["ok"] = false;
                    entry["code"] = "OPEN_FAILED";
                    entry["msg"] = ex.Message;
                    try { srcDoc?.Close(false); } catch { /* ignore */ }
                    srcDoc = null;
                    usedPath = string.Empty;
                    continue;
                }
            }

            if (srcDoc == null)
            {
                result["ok"] = false;
                result["code"] = "NO_SOURCE_DOCUMENT";
                result["msg"] = "Source document could not be opened, or it contained no RebarBarType. Provide params.sourcePath (.rvt/.rte) that contains rebar types.";
                return result;
            }

            result["sourceUsed"] = usedPath;

            // Collect source element ids
            var srcBarTypes = new FilteredElementCollector(srcDoc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .ToList();

            var srcHookTypes = includeHookTypes
                ? new FilteredElementCollector(srcDoc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().ToList()
                : new List<RebarHookType>();

            var srcShapes = includeShapes
                ? new FilteredElementCollector(srcDoc).OfClass(typeof(RebarShape)).Cast<RebarShape>().ToList()
                : new List<RebarShape>();

            IList<ElementId> idsBar = srcBarTypes.Select(x => x.Id).ToList();
            IList<ElementId> idsHook = srcHookTypes.Select(x => x.Id).ToList();
            IList<ElementId> idsShape = srcShapes.Select(x => x.Id).ToList();

            // Optional diameter filter
            if (preferredDiameters.Count > 0)
            {
                idsBar = srcBarTypes
                    .Select(t => new { t.Id, d = UnitHelper.FtToMm(t.BarModelDiameter) })
                    .Where(x => preferredDiameters.Contains((int)Math.Round(x.d)))
                    .Select(x => x.Id)
                    .ToList();
            }

            result["sourceCounts"] = new JObject
            {
                ["rebarBarTypeCount"] = idsBar.Count,
                ["rebarHookTypeCount"] = idsHook.Count,
                ["rebarShapeCount"] = idsShape.Count
            };

            if (dryRun)
            {
                try { srcDoc.Close(false); } catch { /* ignore */ }
                return result;
            }

            var copied = new JObject();
            result["copied"] = copied;

            var copyOptions = new CopyPasteOptions();
            copyOptions.SetDuplicateTypeNamesHandler(new UseDestinationTypesHandler());

            int barCopied = 0, hookCopied = 0, shapeCopied = 0;
            var newBarIds = new List<int>();
            var newHookIds = new List<int>();
            var newShapeIds = new List<int>();

            try
            {
                using (var tx = new Transaction(doc, "Import Rebar Types"))
                {
                    tx.Start();

                    if (idsBar.Count > 0)
                    {
                        var newIds = ElementTransformUtils.CopyElements(srcDoc, idsBar, doc, Transform.Identity, copyOptions);
                        if (newIds != null)
                        {
                            foreach (var id in newIds) { newBarIds.Add(id.IntValue()); barCopied++; }
                        }
                    }

                    if (includeHookTypes && idsHook.Count > 0)
                    {
                        var newIds = ElementTransformUtils.CopyElements(srcDoc, idsHook, doc, Transform.Identity, copyOptions);
                        if (newIds != null)
                        {
                            foreach (var id in newIds) { newHookIds.Add(id.IntValue()); hookCopied++; }
                        }
                    }

                    if (includeShapes && idsShape.Count > 0)
                    {
                        var newIds = ElementTransformUtils.CopyElements(srcDoc, idsShape, doc, Transform.Identity, copyOptions);
                        if (newIds != null)
                        {
                            foreach (var id in newIds) { newShapeIds.Add(id.IntValue()); shapeCopied++; }
                        }
                    }

                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                result["ok"] = false;
                result["code"] = "COPY_FAILED";
                result["msg"] = ex.Message;
                try { srcDoc.Close(false); } catch { /* ignore */ }
                return result;
            }
            finally
            {
                try { srcDoc.Close(false); } catch { /* ignore */ }
            }

            copied["rebarBarTypeCopied"] = barCopied;
            copied["rebarHookTypeCopied"] = hookCopied;
            copied["rebarShapeCopied"] = shapeCopied;
            copied["newBarTypeIds"] = new JArray(newBarIds);
            copied["newHookTypeIds"] = new JArray(newHookIds);
            copied["newShapeIds"] = new JArray(newShapeIds);

            // Post state
            int postBar = 0, postHook = 0, postShape = 0;
            try { postBar = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).GetElementCount(); } catch { postBar = 0; }
            try { postHook = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).GetElementCount(); } catch { postHook = 0; }
            try { postShape = new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).GetElementCount(); } catch { postShape = 0; }

            result["post"] = new JObject
            {
                ["rebarBarTypeCount"] = postBar,
                ["rebarHookTypeCount"] = postHook,
                ["rebarShapeCount"] = postShape
            };

            return result;
        }
    }
}
