using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ParamOps
{
    /// <summary>
    /// Bulk parameter write for a single parameter/value across many elements.
    /// Command: set_parameter_for_elements
    /// </summary>
    public class SetParameterForElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "set_parameter_for_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();

            // --- elementIds ---
            var idsTok = p["elementIds"] as JArray;
            if (idsTok == null || idsTok.Count == 0)
                return new { ok = false, msg = "elementIds[] が必要です。" };

            var elementIds = idsTok
                .Select(t => (t.Type == JTokenType.Integer) ? t.Value<int>() : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (elementIds.Count == 0)
                return new { ok = false, msg = "有効な elementIds がありません。" };

            int totalRequested = elementIds.Count;

            // --- param spec ---
            var paramSpec = p["param"] as JObject ?? new JObject();
            string name = (paramSpec.Value<string>("name") ?? string.Empty).Trim();
            string builtInName = (paramSpec.Value<string>("builtIn") ?? string.Empty).Trim();
            string sharedGuid = (paramSpec.Value<string>("sharedGuid") ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(name)
                && string.IsNullOrWhiteSpace(builtInName)
                && string.IsNullOrWhiteSpace(sharedGuid))
            {
                return new
                {
                    ok = false,
                    msg = "param.name / param.builtIn / param.sharedGuid のいずれか 1 つ以上が必要です。",
                    items = Array.Empty<object>()
                };
            }

            // --- value spec ---
            var valSpec = p["value"] as JObject;
            if (valSpec == null)
                return new { ok = false, msg = "value オブジェクトが必要です。", items = Array.Empty<object>() };

            if (!TryBuildValueObject(valSpec, out var valueObj, out var valueError))
                return new { ok = false, msg = valueError ?? "value の解釈に失敗しました。", items = Array.Empty<object>() };

            // --- options ---
            var opt = p["options"] as JObject;
            bool stopOnFirstError = opt?.Value<bool?>("stopOnFirstError") ?? false;
            bool skipReadOnly = opt?.Value<bool?>("skipReadOnly") ?? true;
            bool ignoreMissingOnElement = opt?.Value<bool?>("ignoreMissingOnElement") ?? true;

            int successCount = 0;
            int failureCount = 0;
            var results = new List<object>(elementIds.Count);
            bool fatalError = false;

            var tx = new Transaction(doc, "Set Parameter For Elements (bulk)");
            var started = false;

            try
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                started = true;

                foreach (var id in elementIds)
                {
                    var eid = Autodesk.Revit.DB.ElementIdCompat.From(id);
                    Element? e = null;
                    try { e = doc.GetElement(eid); } catch { }

                    if (e == null)
                    {
                        failureCount++;
                        results.Add(new { elementId = id, ok = false, scope = "Unknown", msg = "Element not found." });
                        if (stopOnFirstError) break;
                        continue;
                    }

                    try
                    {
                        string resolvedBy;
                        var param = ParamResolver.Resolve(e, name, builtInName, null, sharedGuid, out resolvedBy);
                        if (param == null)
                        {
                            failureCount++;
                            results.Add(new
                            {
                                elementId = id,
                                ok = false,
                                scope = "Unknown",
                                msg = "Parameter not found on element.",
                                resolvedBy
                            });
                            if (stopOnFirstError && !ignoreMissingOnElement) break;
                            continue;
                        }

                        if (param.IsReadOnly)
                        {
                            failureCount++;
                            results.Add(new
                            {
                                elementId = id,
                                ok = false,
                                scope = GetScope(param),
                                msg = $"Parameter '{param.Definition?.Name}' is read-only.",
                                resolvedBy
                            });
                            if (stopOnFirstError && !skipReadOnly) break;
                            continue;
                        }

                        if (!UnitHelper.TrySetParameterByExternalValue(param, valueObj, out var err))
                        {
                            failureCount++;
                            results.Add(new
                            {
                                elementId = id,
                                ok = false,
                                scope = GetScope(param),
                                msg = err ?? "Failed to set value.",
                                resolvedBy
                            });
                            if (stopOnFirstError) break;
                            continue;
                        }

                        successCount++;
                        results.Add(new
                        {
                            elementId = id,
                            ok = true,
                            scope = GetScope(param),
                            msg = "Updated",
                            resolvedBy
                        });
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        results.Add(new { elementId = id, ok = false, scope = "Unknown", msg = ex.Message });
                        if (stopOnFirstError)
                        {
                            fatalError = true;
                            break;
                        }
                    }
                }

                tx.Commit();
                started = false;
            }
            catch (Exception ex)
            {
                try { if (started) tx.RollBack(); } catch { }
                return new
                {
                    ok = false,
                    msg = ex.Message,
                    stats = new { totalRequested, successCount, failureCount },
                    results
                };
            }

            bool okOverall = successCount > 0 && !fatalError;
            string msg;
            if (successCount > 0 && failureCount == 0)
                msg = $"Updated {successCount} elements.";
            else if (successCount > 0 && failureCount > 0)
                msg = $"Updated {successCount} elements. {failureCount} elements failed.";
            else
                msg = $"No elements were updated. {failureCount} elements failed.";

            return new
            {
                ok = okOverall,
                msg,
                stats = new { totalRequested, successCount, failureCount },
                results
            };
        }

        private static string GetScope(Parameter p)
        {
            try
            {
                var owner = p.Element;
                if (owner is ElementType) return "Type";
                if (owner is Element) return "Instance";
            }
            catch { }
            return "Unknown";
        }

        private static bool TryBuildValueObject(JObject valSpec, out object valueObj, out string? error)
        {
            error = null;
            valueObj = null!;

            var storageType = (valSpec.Value<string>("storageType") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(storageType))
            {
                error = "value.storageType が必要です。String/Integer/Double/ElementId のいずれかを指定してください。";
                return false;
            }

            switch (storageType.ToLowerInvariant())
            {
                case "string":
                    valueObj = valSpec.Value<string>("stringValue") ?? string.Empty;
                    return true;

                case "integer":
                    if (valSpec.TryGetValue("intValue", out var ivTok))
                    {
                        if (ivTok.Type == JTokenType.Integer)
                        {
                            valueObj = ivTok.Value<int>();
                            return true;
                        }
                        if (ivTok.Type == JTokenType.String && int.TryParse(ivTok.Value<string>(), out var iv))
                        {
                            valueObj = iv;
                            return true;
                        }
                    }
                    error = "value.intValue (整数) が必要です。";
                    return false;

                case "double":
                    if (valSpec.TryGetValue("doubleValue", out var dvTok))
                    {
                        if (dvTok.Type == JTokenType.Float || dvTok.Type == JTokenType.Integer)
                        {
                            valueObj = dvTok.Value<double>();
                            return true;
                        }
                        if (dvTok.Type == JTokenType.String && double.TryParse(dvTok.Value<string>(), out var dv))
                        {
                            valueObj = dv;
                            return true;
                        }
                    }
                    error = "value.doubleValue (数値) が必要です。";
                    return false;

                case "elementid":
                    if (valSpec.TryGetValue("elementIdValue", out var eidTok))
                    {
                        if (eidTok.Type == JTokenType.Integer)
                        {
                            valueObj = eidTok.Value<int>();
                            return true;
                        }
                        if (eidTok.Type == JTokenType.String && int.TryParse(eidTok.Value<string>(), out var eid))
                        {
                            valueObj = eid;
                            return true;
                        }
                    }
                    error = "value.elementIdValue (整数) が必要です。";
                    return false;

                default:
                    error = $"Unsupported value.storageType='{storageType}'. String/Integer/Double/ElementId を使用してください。";
                    return false;
            }
        }
    }
}


