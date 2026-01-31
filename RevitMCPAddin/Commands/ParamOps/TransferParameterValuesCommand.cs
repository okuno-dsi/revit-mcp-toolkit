// File: Commands/ParamOps/TransferParameterValuesCommand.cs
// Purpose: Transfer parameter values between parameters on selected/category/all elements.
// Notes:
//  - Supports string ops: overwrite/append/replace/search (search is a match gate).
//  - Null (missing param or null value) is treated as error by default.
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ParamOps
{
    [RpcCommand("param.transfer_values",
        Aliases = new[] { "param_transfer_values", "transfer_parameter_values" },
        Category = "ParamOps",
        Kind = "write",
        Risk = RiskLevel.Medium,
        Summary = "Transfer parameter values from source to target across elements (selection/ids/categories/all) with string operations.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"param.transfer_values\", \"params\":{ \"scope\":\"selection\", \"source\":{ \"paramName\":\"ホスト カテゴリ\" }, \"target\":{ \"paramName\":\"コメント\" }, \"stringOp\":\"overwrite\" } }"
    )]
    public sealed class TransferParameterValuesCommand : IRevitCommandHandler
    {
        public string CommandName => "param_transfer_values";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("No active document.", "NO_DOC");

            var p = cmd.Params as JObject ?? new JObject();
            bool dryRun = p.Value<bool?>("dryRun") ?? false;
            bool nullAsError = p.Value<bool?>("nullAsError") ?? true;
            int maxElements = Math.Max(0, p.Value<int?>("maxElements") ?? 0);
            int maxMillis = Math.Max(100, p.Value<int?>("maxMillisPerTx") ?? 3000);

            var srcObj = (p["source"] as JObject) ?? (p["from"] as JObject) ?? new JObject();
            var dstObj = (p["target"] as JObject) ?? (p["to"] as JObject) ?? new JObject();
            if (!srcObj.HasValues) return ResultUtil.Err("source is required.", "INVALID_ARGS");
            if (!dstObj.HasValues) return ResultUtil.Err("target is required.", "INVALID_ARGS");

            // string ops
            var stringOp = (p.Value<string>("stringOp") ?? "overwrite").Trim().ToLowerInvariant();
            var appendSep = p.Value<string>("appendSeparator") ?? "";
            var searchText = p.Value<string>("searchText") ?? p.Value<string>("find") ?? "";
            var replaceText = p.Value<string>("replaceText") ?? p.Value<string>("replace") ?? "";
            var searchScope = (p.Value<string>("searchScope") ?? "source").Trim().ToLowerInvariant();
            var replaceScope = (p.Value<string>("replaceScope") ?? "source").Trim().ToLowerInvariant();
            bool caseSensitive = p.Value<bool?>("caseSensitive") ?? false;

            // resolve target (instance/type/auto)
            var sourceTarget = (p.Value<string>("sourceTarget") ?? p.Value<string>("sourceScope") ?? "auto").Trim().ToLowerInvariant();
            var targetTarget = (p.Value<string>("targetTarget") ?? p.Value<string>("targetScope") ?? "auto").Trim().ToLowerInvariant();
            var preferSource = (p.Value<string>("preferSource") ?? "instance").Trim().ToLowerInvariant();
            var preferTarget = (p.Value<string>("preferTarget") ?? "instance").Trim().ToLowerInvariant();

            var ids = ResolveTargetElementIds(doc, uidoc, p, out var scopeUsed, out var truncated);
            if (ids.Count == 0) return ResultUtil.Err("No target elements resolved.", "NO_TARGETS");

            if (maxElements > 0 && ids.Count > maxElements)
            {
                ids = ids.Take(maxElements).ToList();
                truncated = true;
            }

            int okCount = 0, failCount = 0, skipCount = 0;
            int srcInstanceCount = 0, srcTypeCount = 0, srcBothCount = 0;
            int dstInstanceCount = 0, dstTypeCount = 0, dstBothCount = 0;
            var results = new List<object>(ids.Count);

            Transaction tx = null;
            bool started = false;
            var startAt = DateTime.UtcNow;
            try
            {
                if (!dryRun)
                {
                    tx = new Transaction(doc, "Transfer Parameter Values");
                    tx.Start();
                    TxnUtil.ConfigureProceedWithWarnings(tx);
                    started = true;
                }

                foreach (var id in ids)
                {
                    try
                    {
                        var e = doc.GetElement(id);
                        if (e == null)
                            throw new InvalidOperationException($"Element not found: {id.IntValue()}");

                        var typeElem = TryGetElementType(doc, e);

                        var src = ResolveParam(e, typeElem, srcObj, sourceTarget, preferSource,
                            out var srcResolved, out var srcResolvedOn, out var srcBoth);
                        if (src == null)
                            throw new InvalidOperationException("Source parameter not found.");

                        var dst = ResolveParam(e, typeElem, dstObj, targetTarget, preferTarget,
                            out var dstResolved, out var dstResolvedOn, out var dstBoth);
                        if (dst == null)
                            throw new InvalidOperationException("Target parameter not found.");

                        if (dst.IsReadOnly)
                            throw new InvalidOperationException($"Target parameter '{dst.Definition?.Name}' is read-only.");

                        if (srcBoth) srcBothCount++;
                        if (dstBoth) dstBothCount++;
                        if (string.Equals(srcResolvedOn, "instance", StringComparison.OrdinalIgnoreCase)) srcInstanceCount++;
                        if (string.Equals(srcResolvedOn, "type", StringComparison.OrdinalIgnoreCase)) srcTypeCount++;
                        if (string.Equals(dstResolvedOn, "instance", StringComparison.OrdinalIgnoreCase)) dstInstanceCount++;
                        if (string.Equals(dstResolvedOn, "type", StringComparison.OrdinalIgnoreCase)) dstTypeCount++;

                        // Prepare source value
                        object sourceValue = null;
                        string sourceString = null;
                        if (src.StorageType == StorageType.String)
                        {
                            sourceString = src.AsString();
                            if (sourceString == null && nullAsError)
                                throw new InvalidOperationException("Source parameter value is null.");
                            sourceValue = sourceString ?? "";
                        }
                        else
                        {
                            var si = UnitHelper.ParamToSiInfo(src);
                            string display;
                            sourceValue = GetValueAndDisplayFromSiInfo(si, out display);
                            if (string.IsNullOrWhiteSpace(display) && sourceValue is int intVal)
                            {
                                try
                                {
                                    var cat = Category.GetCategory(doc, new ElementId(intVal));
                                    if (cat != null) display = cat.Name;
                                    else
                                    {
                                        try
                                        {
                                            var bic = (BuiltInCategory)intVal;
                                            cat = Category.GetCategory(doc, bic);
                                            if (cat != null) display = cat.Name;
                                        }
                                        catch { }
                                    }
                                }
                                catch { }
                            }
                            var hasValue = sourceValue != null;
                            if (!hasValue && string.IsNullOrWhiteSpace(display) && nullAsError)
                                throw new InvalidOperationException("Source parameter value is null.");
                            sourceString = !string.IsNullOrWhiteSpace(display) ? display : ValueToString(sourceValue, src);
                        }

                        // Determine write value
                        object writeValue = sourceValue;
                        bool wrote = true;

                        if (dst.StorageType == StorageType.String)
                        {
                            var dstStr = dst.AsString() ?? "";
                            if (stringOp == "append")
                            {
                                writeValue = (dstStr ?? "") + appendSep + (sourceString ?? "");
                            }
                            else if (stringOp == "replace")
                            {
                                if (string.IsNullOrEmpty(searchText))
                                    throw new InvalidOperationException("replace requires searchText/find.");

                                var baseStr = (replaceScope == "target") ? dstStr : (sourceString ?? "");
                                writeValue = ReplaceString(baseStr ?? "", searchText, replaceText, caseSensitive);
                            }
                            else if (stringOp == "search")
                            {
                                if (string.IsNullOrEmpty(searchText))
                                    throw new InvalidOperationException("search requires searchText/find.");

                                var srcMatch = Contains(sourceString ?? "", searchText, caseSensitive);
                                var dstMatch = Contains(dstStr ?? "", searchText, caseSensitive);
                                bool matched = (searchScope == "target") ? dstMatch :
                                               (searchScope == "either") ? (srcMatch || dstMatch) : srcMatch;
                                if (!matched)
                                {
                                    skipCount++;
                                    results.Add(new
                                    {
                                        ok = true,
                                        skipped = true,
                                        reason = "search_not_matched",
                                        elementId = e.Id.IntValue(),
                                        sourceResolvedBy = srcResolved,
                                        targetResolvedBy = dstResolved
                                    });
                                    continue;
                                }
                                writeValue = sourceString ?? "";
                            }
                            else // overwrite (default)
                            {
                                writeValue = sourceString ?? "";
                            }
                        }
                        else
                        {
                            if (stringOp != "overwrite")
                                throw new InvalidOperationException("stringOp is only valid for String target parameters.");
                        }

                        if (!dryRun)
                        {
                            if (!UnitHelper.TrySetParameterByExternalValue(dst, writeValue, out var reason))
                                throw new InvalidOperationException(reason ?? "Failed to set value.");
                        }

                        okCount++;
                        results.Add(new
                        {
                            ok = true,
                            elementId = e.Id.IntValue(),
                            sourceResolvedBy = srcResolved,
                            sourceResolvedOn = srcResolvedOn,
                            targetResolvedBy = dstResolved,
                            targetResolvedOn = dstResolvedOn,
                            bothFoundSource = srcBoth ? (bool?)true : null,
                            bothFoundTarget = dstBoth ? (bool?)true : null,
                            wrote
                        });

                        if (!dryRun && (DateTime.UtcNow - startAt).TotalMilliseconds > maxMillis)
                        {
                            tx.Commit();
                            tx = new Transaction(doc, "Transfer Parameter Values [cont]");
                            tx.Start();
                            TxnUtil.ConfigureProceedWithWarnings(tx);
                            startAt = DateTime.UtcNow;
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        results.Add(new { ok = false, msg = ex.Message });
                    }
                }

                if (!dryRun && started)
                {
                    tx.Commit();
                    started = false;
                }
            }
            catch (Exception ex)
            {
                try { if (started) tx.RollBack(); } catch { }
                var errorNotes = new List<string>();
                if (sourceTarget == "auto" || targetTarget == "auto")
                    errorNotes.Add("auto resolution prefers instance when both instance/type parameters exist (preferSource/Target default: instance).");
                return new
                {
                    ok = false,
                    msg = ex.Message,
                    updatedCount = okCount,
                    failedCount = failCount,
                    skippedCount = skipCount,
                    scopeUsed,
                    truncated,
                    notes = errorNotes.Count > 0 ? errorNotes : null,
                    items = results
                };
            }

            var notes = new List<string>();
            if (sourceTarget == "auto" || targetTarget == "auto")
            {
                notes.Add("auto resolution prefers instance when both instance/type parameters exist (preferSource/Target default: instance).");
                notes.Add($"sourceResolved: instance={srcInstanceCount}, type={srcTypeCount}, both={srcBothCount}; targetResolved: instance={dstInstanceCount}, type={dstTypeCount}, both={dstBothCount}");
            }

            return new
            {
                ok = true,
                updatedCount = okCount,
                failedCount = failCount,
                skippedCount = skipCount,
                scopeUsed,
                truncated,
                notes = notes.Count > 0 ? notes : null,
                items = results
            };
        }

        private static List<ElementId> ResolveTargetElementIds(Document doc, UIDocument uidoc, JObject p, out string scopeUsed, out bool truncated)
        {
            truncated = false;
            scopeUsed = "selection";

            var ids = new List<ElementId>();
            var elementIdsTok = p["elementIds"] as JArray;
            if (elementIdsTok != null && elementIdsTok.Count > 0)
            {
                foreach (var t in elementIdsTok)
                {
                    if (t == null) continue;
                    try { ids.Add(Autodesk.Revit.DB.ElementIdCompat.From(Convert.ToInt32(t))); } catch { }
                }
                scopeUsed = "elementIds";
                return ids.Distinct().ToList();
            }

            // selection
            if (uidoc != null)
            {
                var sel = uidoc.Selection.GetElementIds();
                if (sel != null && sel.Count > 0)
                {
                    scopeUsed = "selection";
                    return sel.Distinct().ToList();
                }
            }

            // categories
            var catIds = new HashSet<int>();
            var catIdTok = p["categoryIds"] as JArray;
            if (catIdTok != null)
            {
                foreach (var t in catIdTok)
                {
                    if (t == null) continue;
                    if (t.Type == JTokenType.Integer) catIds.Add(t.Value<int>());
                    else if (t.Type == JTokenType.Float) catIds.Add((int)t.Value<double>());
                }
            }
            var catNamesTok = p["categoryNames"] as JArray;
            if (catNamesTok != null)
            {
                foreach (var name in catNamesTok.Values<string>())
                {
                    if (CategoryResolver.TryResolveCategory(name, out var bic))
                        catIds.Add((int)bic);
                }
            }

            if (catIds.Count > 0)
            {
                foreach (var cid in catIds)
                {
                    try
                    {
                        var bic = (BuiltInCategory)cid;
                        var found = new FilteredElementCollector(doc)
                            .WhereElementIsNotElementType()
                            .OfCategory(bic)
                            .Select(e => e.Id);
                        ids.AddRange(found);
                    }
                    catch { }
                }
                scopeUsed = "categories";
                return ids.Distinct().ToList();
            }

            // all
            bool allowAll = p.Value<bool?>("allowAll") ?? false;
            if (allowAll)
            {
                scopeUsed = "all";
                return new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .Distinct()
                    .ToList();
            }

            return ids;
        }

        private static object GetValueAndDisplayFromSiInfo(object siInfo, out string display)
        {
            display = null;
            try
            {
                var obj = JObject.FromObject(siInfo);
                var v = obj["value"];
                var d = obj["display"];
                if (d != null && d.Type != JTokenType.Null)
                    display = d.ToString();
                if (v == null || v.Type == JTokenType.Null) return null;
                if (v.Type == JTokenType.Integer) return v.Value<int>();
                if (v.Type == JTokenType.Float) return v.Value<double>();
                if (v.Type == JTokenType.String) return v.Value<string>();
                return v.Value<object>();
            }
            catch { return null; }
        }

        private static string ValueToString(object value, Parameter srcParam)
        {
            if (value == null) return null;
            if (srcParam != null && srcParam.StorageType == StorageType.Double)
            {
                // prefer display string for doubles
                try { return srcParam.AsValueString(); } catch { }
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool Contains(string haystack, string needle, bool caseSensitive)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return false;
            if (!caseSensitive)
                return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
            return haystack.IndexOf(needle, StringComparison.Ordinal) >= 0;
        }

        private static string ReplaceString(string input, string find, string replace, bool caseSensitive)
        {
            if (string.IsNullOrEmpty(find)) return input ?? "";
            if (caseSensitive) return (input ?? "").Replace(find ?? "", replace ?? "");
            return Regex.Replace(input ?? "", Regex.Escape(find ?? ""), replace ?? "", RegexOptions.IgnoreCase);
        }

        private static ElementType TryGetElementType(Document doc, Element e)
        {
            try
            {
                var tid = e?.GetTypeId();
                if (tid == null || tid == ElementId.InvalidElementId) return null;
                return doc.GetElement(tid) as ElementType;
            }
            catch { return null; }
        }

        private static Parameter ResolveParam(Element inst, ElementType typeElem, JObject payload,
            string mode, string prefer, out string resolvedBy, out string resolvedOn, out bool bothPresent)
        {
            resolvedBy = null;
            resolvedOn = null;
            bothPresent = false;

            bool wantInst = !string.Equals(mode, "type", StringComparison.OrdinalIgnoreCase);
            bool wantType = !string.Equals(mode, "instance", StringComparison.OrdinalIgnoreCase);

            Parameter pInst = null;
            string byInst = null;
            if (wantInst)
                pInst = ParamResolver.ResolveByPayload(inst, payload, out byInst);

            Parameter pType = null;
            string byType = null;
            if (wantType && typeElem != null)
                pType = ParamResolver.ResolveByPayload(typeElem, payload, out byType);

            if (string.Equals(mode, "instance", StringComparison.OrdinalIgnoreCase))
            {
                resolvedBy = byInst;
                resolvedOn = "instance";
                return pInst;
            }
            if (string.Equals(mode, "type", StringComparison.OrdinalIgnoreCase))
            {
                resolvedBy = byType;
                resolvedOn = "type";
                return pType;
            }

            if (pInst != null && pType != null)
            {
                bothPresent = true;
                if (string.Equals(prefer, "type", StringComparison.OrdinalIgnoreCase))
                {
                    resolvedBy = byType;
                    resolvedOn = "type";
                    return pType;
                }
                resolvedBy = byInst;
                resolvedOn = "instance";
                return pInst;
            }

            if (pInst != null)
            {
                resolvedBy = byInst;
                resolvedOn = "instance";
                return pInst;
            }
            if (pType != null)
            {
                resolvedBy = byType;
                resolvedOn = "type";
                return pType;
            }

            return null;
        }
    }
}
