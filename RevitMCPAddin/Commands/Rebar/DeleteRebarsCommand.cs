// ================================================================
// Command: delete_rebars (alias: delete_rebar)
// Purpose: Delete Rebar elements by elementId(s) (arbitrary IDs).
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Kind   : write (high risk)
// Notes  :
//  - Supports dryRun and chunked transactions.
//  - Only deletes Rebar-ish elements (Rebar / RebarInSystem / AreaReinforcement / PathReinforcement).
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rebar
{
    [RpcCommand("delete_rebars",
        Aliases = new[] { "delete_rebar" },
        Category = "Rebar",
        Kind = "write",
        Risk = RiskLevel.High,
        Summary = "Delete Rebar elements by elementId(s). Supports dryRun and chunked transactions.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"delete_rebars\", \"params\":{ \"rebarElementIds\":[123456], \"dryRun\":true } }"
    )]
    public sealed class DeleteRebarsCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_rebars";

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

            var requestedIds = CollectIds(p, "rebarElementIds", "elementIds");
            // scalar support
            try
            {
                int one = p.Value<int?>("rebarElementId") ?? p.Value<int?>("rebarId") ?? p.Value<int?>("elementId") ?? 0;
                if (one > 0) requestedIds.Add(one);
            }
            catch { /* ignore */ }

            if (requestedIds.Count == 0 && useSelectionIfEmpty && uidoc != null)
            {
                try
                {
                    foreach (var id in uidoc.Selection.GetElementIds())
                    {
                        try
                        {
                            int v = id.IntValue();
                            if (v > 0) requestedIds.Add(v);
                        }
                        catch { /* ignore */ }
                    }
                }
                catch { /* ignore */ }
            }

            requestedIds = requestedIds.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
            if (requestedIds.Count == 0)
                return ResultUtil.Err("rebarElementIds/elementIds が空で、選択も空です。", "INVALID_ARGS");

            var targetIds = new List<int>();
            var skipped = new JArray();
            var missing = new JArray();
            var details = new JArray();

            foreach (var id in requestedIds)
            {
                Element e = null;
                try { e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)); } catch { e = null; }

                if (e == null)
                {
                    missing.Add(id);
                    continue;
                }

                if (!IsRebarLike(e))
                {
                    skipped.Add(new JObject
                    {
                        ["elementId"] = id,
                        ["reason"] = "NOT_REBAR",
                        ["categoryName"] = e.Category != null ? e.Category.Name : null
                    });
                    continue;
                }

                targetIds.Add(id);

                if (detailLimit > 0 && details.Count < detailLimit)
                {
                    string typeName = string.Empty;
                    try { typeName = doc.GetElement(e.GetTypeId())?.Name ?? string.Empty; } catch { typeName = string.Empty; }
                    details.Add(new JObject
                    {
                        ["elementId"] = id,
                        ["uniqueId"] = e.UniqueId ?? string.Empty,
                        ["categoryName"] = e.Category != null ? e.Category.Name : null,
                        ["typeName"] = typeName
                    });
                }
            }

            targetIds = targetIds.Distinct().OrderBy(x => x).ToList();

            if (dryRun)
            {
                var targetOut = TruncateIds(targetIds, maxIds, out var targetTruncated);
                return new JObject
                {
                    ["ok"] = true,
                    ["dryRun"] = true,
                    ["requestedCount"] = requestedIds.Count,
                    ["targetCount"] = targetIds.Count,
                    ["missingCount"] = missing.Count,
                    ["skippedCount"] = skipped.Count,
                    ["rebarElementIds"] = new JArray(targetOut),
                    ["rebarElementIdsTruncated"] = targetTruncated,
                    ["missingElementIds"] = missing,
                    ["skipped"] = skipped,
                    ["details"] = details,
                    ["msg"] = $"[DryRun] Delete target Rebar-like elements: {targetIds.Count}."
                };
            }

            var deletedTargetIds = new List<int>();
            var deletedAllIds = new HashSet<int>();
            var failed = new JArray();

            int processed = 0;
            while (processed < targetIds.Count)
            {
                var chunk = targetIds.Skip(processed).Take(batchSize).ToList();
                using (var tx = new Transaction(doc, "[MCP] Delete rebars"))
                {
                    tx.Start();
                    try { TxnUtil.ConfigureProceedWithWarnings(tx); } catch { /* ignore */ }

                    foreach (var rid in chunk)
                    {
                        try
                        {
                            var del = doc.Delete(Autodesk.Revit.DB.ElementIdCompat.From(rid));
                            if (del != null)
                            {
                                bool deletedSelf = false;
                                foreach (var did in del)
                                {
                                    int v = 0;
                                    try { v = did.IntValue(); } catch { v = 0; }
                                    if (v > 0) deletedAllIds.Add(v);
                                    if (v == rid) deletedSelf = true;
                                }
                                if (deletedSelf) deletedTargetIds.Add(rid);
                            }
                        }
                        catch (Exception ex)
                        {
                            failed.Add(new JObject
                            {
                                ["elementId"] = rid,
                                ["error"] = ex.Message
                            });
                        }
                    }

                    tx.Commit();
                }

                processed += chunk.Count;
            }

            deletedTargetIds = deletedTargetIds.Distinct().OrderBy(x => x).ToList();
            var deletedAllList = deletedAllIds.OrderBy(x => x).ToList();

            var deletedTargetsOut = TruncateIds(deletedTargetIds, maxIds, out var deletedTargetsTruncated);
            var deletedAllOut = TruncateIds(deletedAllList, maxIds, out var deletedAllTruncated);

            return new JObject
            {
                ["ok"] = true,
                ["dryRun"] = false,
                ["requestedCount"] = requestedIds.Count,
                ["targetCount"] = targetIds.Count,
                ["deletedTargetCount"] = deletedTargetIds.Count,
                ["failedCount"] = failed.Count,
                ["deletedRebarElementIds"] = new JArray(deletedTargetsOut),
                ["deletedRebarElementIdsTruncated"] = deletedTargetsTruncated,
                ["deletedElementIds"] = new JArray(deletedAllOut),
                ["deletedElementIdsTruncated"] = deletedAllTruncated,
                ["missingElementIds"] = missing,
                ["skipped"] = skipped,
                ["failed"] = failed,
                ["msg"] = failed.Count == 0
                    ? $"Deleted {deletedTargetIds.Count} Rebar-like element(s)."
                    : $"Deleted {deletedTargetIds.Count} Rebar-like element(s). Failed: {failed.Count}."
            };
        }

        private static bool IsRebarLike(Element e)
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

