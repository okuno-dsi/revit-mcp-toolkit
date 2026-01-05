// ================================================================
// Command: move_rebars (alias: move_rebar)
// Purpose: Move Rebar elements by elementId(s) (arbitrary IDs).
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Kind   : write
// Notes  :
//  - Supports dryRun and chunked transactions.
//  - Offset input: offsetMm / offset / dx,dy,dz (mm).
//  - items[] form allows per-element offsets.
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rebar
{
    [RpcCommand("move_rebars",
        Aliases = new[] { "move_rebar" },
        Category = "Rebar",
        Kind = "write",
        Risk = RiskLevel.Medium,
        Summary = "Move Rebar elements by elementId(s). Supports dryRun and chunked transactions.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"move_rebars\", \"params\":{ \"rebarElementIds\":[123456], \"offsetMm\":{ \"x\":100, \"y\":0, \"z\":0 } } }"
    )]
    public sealed class MoveRebarsCommand : IRevitCommandHandler
    {
        public string CommandName => "move_rebars";

        private sealed class MoveItem
        {
            public int elementId;
            public XYZ deltaMm;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params ?? new JObject();

            bool dryRun = p.Value<bool?>("dryRun") ?? p.Value<bool?>("dry_run") ?? false;
            bool useSelectionIfEmpty = p.Value<bool?>("useSelectionIfEmpty") ?? true;
            int batchSize = Math.Max(1, p.Value<int?>("batchSize") ?? 200);
            int maxIds = Math.Max(0, p.Value<int?>("maxIds") ?? 2000);
            int detailLimit = Math.Max(0, p.Value<int?>("detailLimit") ?? 200);

            var items = new List<MoveItem>();

            // 1) items[]: per-element offset
            var itemsArr = p["items"] as JArray;
            if (itemsArr != null && itemsArr.Count > 0)
            {
                foreach (var it in itemsArr.OfType<JObject>())
                {
                    int id = it.Value<int?>("rebarElementId") ?? it.Value<int?>("rebarId") ?? it.Value<int?>("elementId") ?? 0;
                    if (id <= 0) continue;
                    if (!InputPointReader.TryReadOffsetMm(it, out var deltaMm)) continue;
                    items.Add(new MoveItem { elementId = id, deltaMm = deltaMm });
                }
            }
            else
            {
                // 2) id list + shared offset
                var ids = CollectIds(p, "rebarElementIds", "elementIds");
                try
                {
                    int one = p.Value<int?>("rebarElementId") ?? p.Value<int?>("rebarId") ?? p.Value<int?>("elementId") ?? 0;
                    if (one > 0) ids.Add(one);
                }
                catch { /* ignore */ }

                if (ids.Count == 0 && useSelectionIfEmpty && uidoc != null)
                {
                    try
                    {
                        foreach (var id in uidoc.Selection.GetElementIds())
                        {
                            try
                            {
                                int v = id.IntValue();
                                if (v > 0) ids.Add(v);
                            }
                            catch { /* ignore */ }
                        }
                    }
                    catch { /* ignore */ }
                }

                ids = ids.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
                if (ids.Count == 0)
                    return ResultUtil.Err("rebarElementIds/elementIds が空で、選択も空です。", "INVALID_ARGS");

                if (!InputPointReader.TryReadOffsetMm(p, out var sharedDeltaMm))
                    return ResultUtil.Err("offsetMm{ x,y,z } または dx/dy/dz が必要です（mm）。", "INVALID_ARGS");

                foreach (var id in ids)
                    items.Add(new MoveItem { elementId = id, deltaMm = sharedDeltaMm });
            }

            if (items.Count == 0)
                return ResultUtil.Err("移動対象がありません（items[] または rebarElementIds + offset）。", "INVALID_ARGS");

            // Normalize + validate (rebar-like only)
            var targetItems = new List<MoveItem>();
            var skipped = new JArray();
            var missing = new JArray();
            var details = new JArray();

            foreach (var it in items)
            {
                if (it == null || it.elementId <= 0) continue;

                Element e = null;
                try { e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(it.elementId)); } catch { e = null; }
                if (e == null) { missing.Add(it.elementId); continue; }

                if (!DeleteRebarsCommand_IsRebarLike(e))
                {
                    skipped.Add(new JObject
                    {
                        ["elementId"] = it.elementId,
                        ["reason"] = "NOT_REBAR",
                        ["categoryName"] = e.Category != null ? e.Category.Name : null
                    });
                    continue;
                }

                targetItems.Add(it);

                if (detailLimit > 0 && details.Count < detailLimit)
                {
                    details.Add(new JObject
                    {
                        ["elementId"] = it.elementId,
                        ["deltaMm"] = new JObject
                        {
                            ["x"] = it.deltaMm.X,
                            ["y"] = it.deltaMm.Y,
                            ["z"] = it.deltaMm.Z
                        }
                    });
                }
            }

            if (dryRun)
            {
                var targetIds = targetItems.Select(x => x.elementId).Distinct().OrderBy(x => x).ToList();
                var outIds = TruncateIds(targetIds, maxIds, out var truncated);
                return new JObject
                {
                    ["ok"] = true,
                    ["dryRun"] = true,
                    ["requestedCount"] = items.Count,
                    ["targetCount"] = targetItems.Count,
                    ["missingCount"] = missing.Count,
                    ["skippedCount"] = skipped.Count,
                    ["rebarElementIds"] = new JArray(outIds),
                    ["rebarElementIdsTruncated"] = truncated,
                    ["missingElementIds"] = missing,
                    ["skipped"] = skipped,
                    ["details"] = details,
                    ["inputUnits"] = new JObject { ["Length"] = "mm" },
                    ["internalUnits"] = new JObject { ["Length"] = "ft" },
                    ["msg"] = $"[DryRun] Move target Rebar-like elements: {targetItems.Count}."
                };
            }

            var moved = new List<int>();
            var failed = new JArray();

            int processed = 0;
            while (processed < targetItems.Count)
            {
                var chunk = targetItems.Skip(processed).Take(batchSize).ToList();
                using (var tx = new Transaction(doc, "[MCP] Move rebars"))
                {
                    tx.Start();
                    try { TxnUtil.ConfigureProceedWithWarnings(tx); } catch { /* ignore */ }

                    foreach (var it in chunk)
                    {
                        try
                        {
                            var deltaFt = new XYZ(
                                UnitHelper.MmToFt(it.deltaMm.X),
                                UnitHelper.MmToFt(it.deltaMm.Y),
                                UnitHelper.MmToFt(it.deltaMm.Z));

                            ElementTransformUtils.MoveElement(doc, Autodesk.Revit.DB.ElementIdCompat.From(it.elementId), deltaFt);
                            moved.Add(it.elementId);
                        }
                        catch (Exception ex)
                        {
                            failed.Add(new JObject
                            {
                                ["elementId"] = it.elementId,
                                ["error"] = ex.Message
                            });
                        }
                    }

                    tx.Commit();
                }
                processed += chunk.Count;
            }

            moved = moved.Distinct().OrderBy(x => x).ToList();
            var movedOut = TruncateIds(moved, maxIds, out var movedTruncated);

            return new JObject
            {
                ["ok"] = true,
                ["dryRun"] = false,
                ["requestedCount"] = items.Count,
                ["targetCount"] = targetItems.Count,
                ["movedCount"] = moved.Count,
                ["failedCount"] = failed.Count,
                ["movedRebarElementIds"] = new JArray(movedOut),
                ["movedRebarElementIdsTruncated"] = movedTruncated,
                ["missingElementIds"] = missing,
                ["skipped"] = skipped,
                ["failed"] = failed,
                ["inputUnits"] = new JObject { ["Length"] = "mm" },
                ["internalUnits"] = new JObject { ["Length"] = "ft" },
                ["msg"] = failed.Count == 0
                    ? $"Moved {moved.Count} Rebar-like element(s)."
                    : $"Moved {moved.Count} Rebar-like element(s). Failed: {failed.Count}."
            };
        }

        // Reuse the same rebar-like filter as delete (kept local to avoid cross-class coupling).
        private static bool DeleteRebarsCommand_IsRebarLike(Element e)
        {
            if (e == null) return false;
            if (e is Autodesk.Revit.DB.Structure.Rebar) return true;
            if (e is Autodesk.Revit.DB.Structure.RebarInSystem) return true;
            if (e is Autodesk.Revit.DB.Structure.AreaReinforcement) return true;
            if (e is Autodesk.Revit.DB.Structure.PathReinforcement) return true;

            try
            {
                var cat = e.Category;
                int cid = cat != null ? cat.Id.IntValue() : 0;
                if (cid == (int)BuiltInCategory.OST_Rebar) return true;
                if (cid == (int)BuiltInCategory.OST_AreaRein) return true;
                if (cid == (int)BuiltInCategory.OST_PathRein) return true;
            }
            catch { /* ignore */ }

            return false;
        }

        private static List<int> CollectIds(JObject p, params string[] keys)
        {
            var ids = new List<int>();
            if (p == null || keys == null) return ids;

            foreach (var k in keys)
            {
                try
                {
                    var arr = p[k] as JArray;
                    if (arr == null) continue;
                    foreach (var t in arr)
                    {
                        if (t == null) continue;
                        if (t.Type == JTokenType.Integer)
                        {
                            int v = t.Value<int>();
                            if (v > 0) ids.Add(v);
                        }
                        else if (t.Type == JTokenType.Float)
                        {
                            int v = (int)t.Value<double>();
                            if (v > 0) ids.Add(v);
                        }
                        else if (t.Type == JTokenType.String && int.TryParse(t.Value<string>(), out var v2))
                        {
                            if (v2 > 0) ids.Add(v2);
                        }
                    }
                }
                catch { /* ignore */ }
            }

            return ids;
        }

        private static List<int> TruncateIds(List<int> ids, int maxIds, out bool truncated)
        {
            truncated = false;
            if (ids == null) return new List<int>();
            if (maxIds <= 0) return ids;
            if (ids.Count <= maxIds) return ids;
            truncated = true;
            return ids.Take(maxIds).ToList();
        }
    }
}

