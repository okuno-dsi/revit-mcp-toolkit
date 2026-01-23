// ================================================================
// File: Commands/ViewFilterOps/ViewFilterCommands.cs
// Purpose:
//   Manage Revit View Filters (ParameterFilterElement / SelectionFilterElement)
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// ================================================================
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewFilterOps
{
    internal static class ViewFilterUtil
    {
        public static (bool ok, Autodesk.Revit.DB.View view, string errorCode, string msg, object data) ResolveView(
            Autodesk.Revit.DB.Document doc,
            Newtonsoft.Json.Linq.JObject viewObj)
        {
            if (doc == null) return (false, null, "NO_DOC", "No active document.", null);
            if (viewObj == null) return (false, null, "MISSING_VIEW", "view is required.", null);

            try
            {
                var vidTok = viewObj["elementId"] ?? viewObj["id"];
                if (vidTok != null && vidTok.Type != JTokenType.Null)
                {
                    var idLong = TryReadLong(vidTok, out var v) ? v : 0L;
                    if (idLong > 0)
                    {
                        var vElem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idLong)) as Autodesk.Revit.DB.View;
                        if (vElem != null) return (true, vElem, null, null, null);
                        return (false, null, "VIEW_NOT_FOUND", $"viewId={idLong} not found.", new { viewId = idLong });
                    }
                }

                var uid = (viewObj.Value<string>("uniqueId") ?? viewObj.Value<string>("uid") ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(uid))
                {
                    var vElem = doc.GetElement(uid) as Autodesk.Revit.DB.View;
                    if (vElem != null) return (true, vElem, null, null, null);
                    return (false, null, "VIEW_NOT_FOUND", $"viewUniqueId not found: {uid}", new { uniqueId = uid });
                }

                return (false, null, "MISSING_VIEW", "view.elementId or view.uniqueId is required.", null);
            }
            catch (Exception ex)
            {
                return (false, null, "VIEW_RESOLVE_ERROR", ex.Message, null);
            }
        }

        public static (bool ok, Autodesk.Revit.DB.FilterElement filter, string errorCode, string msg, object data) ResolveFilter(
            Autodesk.Revit.DB.Document doc,
            Newtonsoft.Json.Linq.JObject filterObj)
        {
            if (doc == null) return (false, null, "NO_DOC", "No active document.", null);
            if (filterObj == null) return (false, null, "MISSING_FILTER", "filter is required.", null);

            try
            {
                var idTok = filterObj["elementId"] ?? filterObj["id"];
                if (idTok != null && idTok.Type != JTokenType.Null)
                {
                    var idLong = TryReadLong(idTok, out var v) ? v : 0L;
                    if (idLong > 0)
                    {
                        var elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(idLong)) as Autodesk.Revit.DB.FilterElement;
                        if (elem != null) return (true, elem, null, null, null);
                        return (false, null, "FILTER_NOT_FOUND", $"filterId={idLong} not found.", new { filterId = idLong });
                    }
                }

                var name = (filterObj.Value<string>("name") ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var found = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.FilterElement))
                        .Cast<Autodesk.Revit.DB.FilterElement>()
                        .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (found != null) return (true, found, null, null, null);
                    return (false, null, "FILTER_NOT_FOUND", $"filter name not found: {name}", new { name });
                }

                return (false, null, "MISSING_FILTER", "filter.elementId or filter.name is required.", null);
            }
            catch (Exception ex)
            {
                return (false, null, "FILTER_RESOLVE_ERROR", ex.Message, null);
            }
        }

        public static object CheckViewTemplateLock(Autodesk.Revit.DB.View view, bool detachViewTemplate, string opName, out bool templateDetached)
        {
            templateDetached = false;
            try
            {
                if (view == null) return RpcResultEnvelope.Fail("VIEW_NOT_FOUND", "View not found.");
                if (view.IsTemplate) return null; // templates are editable

                var tmplId = view.ViewTemplateId;
                bool applied = tmplId != null && tmplId != Autodesk.Revit.DB.ElementId.InvalidElementId;
                if (!applied) return null;

                if (!detachViewTemplate)
                {
                    return new
                    {
                        ok = false,
                        code = "VIEW_TEMPLATE_LOCK",
                        msg = "View has a template; detach it or target the template view instead.",
                        templateApplied = true,
                        templateViewId = tmplId.IntValue(),
                        operation = opName
                    };
                }

                try
                {
                    view.ViewTemplateId = Autodesk.Revit.DB.ElementId.InvalidElementId;
                    templateDetached = true;
                }
                catch (Exception ex)
                {
                    return new
                    {
                        ok = false,
                        code = "VIEW_TEMPLATE_DETACH_FAILED",
                        msg = "Failed to detach view template.",
                        templateApplied = true,
                        templateViewId = tmplId.IntValue(),
                        detail = ex.Message,
                        operation = opName
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "VIEW_TEMPLATE_CHECK_ERROR", msg = ex.Message, operation = opName };
            }
        }

        public static bool TryReadLong(Newtonsoft.Json.Linq.JToken tok, out long value)
        {
            value = 0;
            try
            {
                if (tok == null) return false;
                if (tok.Type == Newtonsoft.Json.Linq.JTokenType.Integer || tok.Type == Newtonsoft.Json.Linq.JTokenType.Float)
                {
                    value = tok.Value<long>();
                    return true;
                }
                if (tok.Type == Newtonsoft.Json.Linq.JTokenType.String)
                {
                    var s = (tok.Value<string>() ?? string.Empty).Trim();
                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                        return true;
                }
            }
            catch { /* ignore */ }
            return false;
        }

        public static IList<Autodesk.Revit.DB.ElementId> ReadElementIdsArray(Newtonsoft.Json.Linq.JToken tok)
        {
            var list = new List<Autodesk.Revit.DB.ElementId>();
            if (tok == null || tok.Type == Newtonsoft.Json.Linq.JTokenType.Null) return list;

            if (tok is Newtonsoft.Json.Linq.JArray arr)
            {
                foreach (var t in arr)
                {
                    if (TryReadLong(t, out var v) && v != 0)
                        list.Add(Autodesk.Revit.DB.ElementIdCompat.From(v));
                }
                return list;
            }

            if (TryReadLong(tok, out var one) && one != 0)
            {
                list.Add(Autodesk.Revit.DB.ElementIdCompat.From(one));
            }
            return list;
        }

        public static (bool ok, IList<Autodesk.Revit.DB.ElementId> categoryIds, string msg, object details) ResolveCategories(
            Autodesk.Revit.DB.Document doc,
            Newtonsoft.Json.Linq.JArray categoriesArr)
        {
            var list = new List<Autodesk.Revit.DB.ElementId>();
            if (categoriesArr == null || categoriesArr.Count == 0)
                return (false, list, "categories[] is required.", null);

            foreach (var t in categoriesArr)
            {
                var s = (t?.Value<string>() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(s)) continue;
                if (!Enum.TryParse(s, ignoreCase: true, out Autodesk.Revit.DB.BuiltInCategory bic))
                    return (false, list, $"Unknown BuiltInCategory: {s}", new { category = s });
                list.Add(Autodesk.Revit.DB.ElementIdCompat.From(bic));
            }

            list = list.Distinct().ToList();
            if (list.Count == 0) return (false, list, "categories[] resolved to empty.", null);
            return (true, list, null, null);
        }

        public static string BuiltInCategoryName(Autodesk.Revit.DB.ElementId catId)
        {
            try
            {
                var iv = catId.IntValue();
                return ((Autodesk.Revit.DB.BuiltInCategory)iv).ToString();
            }
            catch
            {
                return catId.IntValue().ToString(CultureInfo.InvariantCulture);
            }
        }

        public static (bool ok, Autodesk.Revit.DB.ElementId parameterId, string msg, object details) ResolveParameterId(
            Autodesk.Revit.DB.Document doc,
            IList<Autodesk.Revit.DB.ElementId> categoryIds,
            Newtonsoft.Json.Linq.JObject parameterObj)
        {
            if (doc == null) return (false, Autodesk.Revit.DB.ElementId.InvalidElementId, "No active document.", null);
            if (parameterObj == null) return (false, Autodesk.Revit.DB.ElementId.InvalidElementId, "parameter is required.", null);

            var pType = (parameterObj.Value<string>("type") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(pType)) pType = "builtInParameter";

            try
            {
                if (pType.Equals("builtInParameter", StringComparison.OrdinalIgnoreCase))
                {
                    var bipName = (parameterObj.Value<string>("builtInParameter") ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(bipName))
                        return (false, Autodesk.Revit.DB.ElementId.InvalidElementId, "parameter.builtInParameter is required.", null);
                    if (!Enum.TryParse(bipName, ignoreCase: true, out Autodesk.Revit.DB.BuiltInParameter bip))
                        return (false, Autodesk.Revit.DB.ElementId.InvalidElementId, $"Unknown BuiltInParameter: {bipName}", new { builtInParameter = bipName });
                    return (true, Autodesk.Revit.DB.ElementIdCompat.From(bip), null, null);
                }

                if (pType.Equals("sharedParameterGuid", StringComparison.OrdinalIgnoreCase))
                {
                    var guidStr = (parameterObj.Value<string>("guid") ?? string.Empty).Trim();
                    if (!Guid.TryParse(guidStr, out var guid))
                        return (false, Autodesk.Revit.DB.ElementId.InvalidElementId, "parameter.guid must be a GUID string.", new { guid = guidStr });
                    var spe = Autodesk.Revit.DB.SharedParameterElement.Lookup(doc, guid);
                    if (spe == null)
                        return (false, Autodesk.Revit.DB.ElementId.InvalidElementId, $"Shared parameter GUID not found in document: {guid}", new { guid = guid.ToString() });
                    return (true, spe.Id, null, null);
                }

                if (pType.Equals("parameterName", StringComparison.OrdinalIgnoreCase))
                {
                    var name = (parameterObj.Value<string>("name") ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        return (false, Autodesk.Revit.DB.ElementId.InvalidElementId, "parameter.name is required for parameterName.", null);

                    // Resolve only from the "filterable in common" parameter set.
                    ICollection<Autodesk.Revit.DB.ElementId> common;
                    try
                    {
                        common = Autodesk.Revit.DB.ParameterFilterUtilities.GetFilterableParametersInCommon(doc, categoryIds);
                    }
                    catch (Exception ex)
                    {
                        return (false, Autodesk.Revit.DB.ElementId.InvalidElementId, "Failed to get filterable parameters in common for categories.", new { detail = ex.Message });
                    }

                    var matches = new List<Autodesk.Revit.DB.ElementId>();
                    foreach (var pid in common ?? new List<Autodesk.Revit.DB.ElementId>())
                    {
                        if (pid == null || pid == Autodesk.Revit.DB.ElementId.InvalidElementId) continue;

                        // Shared/Project parameter: ParameterElement exists.
                        try
                        {
                            var pe = doc.GetElement(pid) as Autodesk.Revit.DB.ParameterElement;
                            if (pe != null && string.Equals(pe.Name, name, StringComparison.OrdinalIgnoreCase))
                            {
                                matches.Add(pid);
                                continue;
                            }
                        }
                        catch { /* ignore */ }

                        // Built-in parameter: match by enum name or localized label (best-effort).
                        try
                        {
                            var iv = pid.IntValue();
                            var bip = (Autodesk.Revit.DB.BuiltInParameter)iv;
                            if (string.Equals(bip.ToString(), name, StringComparison.OrdinalIgnoreCase))
                            {
                                matches.Add(pid);
                                continue;
                            }
                            try
                            {
                                var label = Autodesk.Revit.DB.LabelUtils.GetLabelFor(bip);
                                if (!string.IsNullOrWhiteSpace(label) && string.Equals(label, name, StringComparison.OrdinalIgnoreCase))
                                    matches.Add(pid);
                            }
                            catch { /* ignore */ }
                        }
                        catch { /* ignore */ }
                    }

                    matches = matches.Distinct().ToList();
                    if (matches.Count == 1) return (true, matches[0], null, null);
                    if (matches.Count == 0)
                    {
                        return (false, Autodesk.Revit.DB.ElementId.InvalidElementId,
                            $"parameterName '{name}' not resolved in filterable parameters in common. Use builtInParameter or sharedParameterGuid.",
                            new { parameterName = name, categoryCount = categoryIds.Count, commonCount = common != null ? common.Count : 0 });
                    }

                    return (false, Autodesk.Revit.DB.ElementId.InvalidElementId,
                        $"parameterName '{name}' is ambiguous ({matches.Count} matches). Use builtInParameter or sharedParameterGuid.",
                        new { parameterName = name, matchCount = matches.Count, matchIds = matches.Select(x => x.IntValue()).ToArray() });
                }

                return (false, Autodesk.Revit.DB.ElementId.InvalidElementId, $"Unsupported parameter.type: {pType}", new { type = pType });
            }
            catch (Exception ex)
            {
                return (false, Autodesk.Revit.DB.ElementId.InvalidElementId, "ResolveParameterId failed.", new { detail = ex.Message });
            }
        }

        private static Autodesk.Revit.DB.Color ParseHexColor(string hex)
        {
            try
            {
                var s = (hex ?? string.Empty).Trim();
                if (s.StartsWith("#")) s = s.Substring(1);
                if (s.Length != 6) return null;
                byte r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                byte b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return new Autodesk.Revit.DB.Color(r, g, b);
            }
            catch { return null; }
        }

        public static Autodesk.Revit.DB.OverrideGraphicSettings BuildOverrides(Newtonsoft.Json.Linq.JObject overridesObj)
        {
            var ogs = new Autodesk.Revit.DB.OverrideGraphicSettings();
            if (overridesObj == null) return ogs;

            // halftone
            try
            {
                var ht = overridesObj.Value<bool?>("halftone");
                if (ht.HasValue) ogs.SetHalftone(ht.Value);
            }
            catch { /* ignore */ }

            // transparency (surface)
            try
            {
                var tr = overridesObj.Value<int?>("transparency");
                if (tr.HasValue)
                {
                    int v = tr.Value;
                    if (v < 0) v = 0;
                    if (v > 100) v = 100;
                    try { ogs.SetSurfaceTransparency(v); } catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }

            // projectionLine
            try
            {
                if (overridesObj["projectionLine"] is Newtonsoft.Json.Linq.JObject proj)
                {
                    var c = ParseHexColor(proj.Value<string>("color"));
                    if (c != null)
                    {
                        try { ogs.SetProjectionLineColor(c); } catch { /* ignore */ }
                    }
                    var w = proj.Value<int?>("weight");
                    if (w.HasValue)
                    {
                        try { ogs.SetProjectionLineWeight(w.Value); } catch { /* ignore */ }
                    }
                }
            }
            catch { /* ignore */ }

            // cutLine
            try
            {
                if (overridesObj["cutLine"] is Newtonsoft.Json.Linq.JObject cut)
                {
                    var c = ParseHexColor(cut.Value<string>("color"));
                    if (c != null)
                    {
                        try { ogs.SetCutLineColor(c); } catch { /* ignore */ }
                    }
                    var w = cut.Value<int?>("weight");
                    if (w.HasValue)
                    {
                        try { ogs.SetCutLineWeight(w.Value); } catch { /* ignore */ }
                    }
                }
            }
            catch { /* ignore */ }

            return ogs;
        }

        public static (bool ok, Autodesk.Revit.DB.FilterRule rule, string msg, object details) BuildFilterRule(
            Autodesk.Revit.DB.Document doc,
            Autodesk.Revit.DB.ElementId parameterId,
            string op,
            Newtonsoft.Json.Linq.JToken valueTok,
            Newtonsoft.Json.Linq.JObject ruleObj)
        {
            op = (op ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(op)) return (false, null, "operator is required.", null);
            if (valueTok == null || valueTok.Type == Newtonsoft.Json.Linq.JTokenType.Null) return (false, null, "value is required.", null);

            // Determine typed value
            string kind = string.Empty;
            var valueObj = valueTok as Newtonsoft.Json.Linq.JObject;
            if (valueObj != null)
            {
                kind = (valueObj.Value<string>("kind") ?? string.Empty).Trim().ToLowerInvariant();
            }

            bool caseSensitive = false;
            try
            {
                if (ruleObj != null) caseSensitive = ruleObj.Value<bool?>("caseSensitive") ?? false;
                if (valueObj != null && valueObj["caseSensitive"] != null) caseSensitive = valueObj.Value<bool?>("caseSensitive") ?? caseSensitive;
            }
            catch { /* ignore */ }

            // 1) string rules
            if (kind == "string" || valueTok.Type == Newtonsoft.Json.Linq.JTokenType.String ||
                op == "contains" || op == "not_contains" || op == "begins_with" || op == "ends_with")
            {
                var text = valueObj != null ? (valueObj.Value<string>("text") ?? string.Empty) : (valueTok.Value<string>() ?? string.Empty);
                if (text == null) text = string.Empty;

                try
                {
                    // Revit 2023+ deprecates the string factory overloads with `caseSensitive`.
                    // Prefer the newer overloads (without caseSensitive). When caseSensitive=true,
                    // try to apply it via reflection if the overload still exists; otherwise fall back.
                    if (!caseSensitive)
                    {
                        switch (op)
                        {
                            case "equals":
                                return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateEqualsRule(parameterId, text), null, null);
                            case "not_equals":
                                return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, text), null, null);
                            case "contains":
                                return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateContainsRule(parameterId, text), null, null);
                            case "not_contains":
                                return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateNotContainsRule(parameterId, text), null, null);
                            case "begins_with":
                                return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateBeginsWithRule(parameterId, text), null, null);
                            case "ends_with":
                                return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateEndsWithRule(parameterId, text), null, null);
                            default:
                                return (false, null, $"Unsupported operator for string: {op}", new { op });
                        }
                    }

                    var csRule = TryCreateStringRuleCaseSensitive(parameterId, op, text, caseSensitive);
                    if (csRule != null)
                        return (true, csRule, null, new { caseSensitiveApplied = true });

                    // Fallback: caseSensitive could not be applied (missing overload / API changed).
                    switch (op)
                    {
                        case "equals":
                            return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateEqualsRule(parameterId, text), null, new { caseSensitiveApplied = false });
                        case "not_equals":
                            return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, text), null, new { caseSensitiveApplied = false });
                        case "contains":
                            return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateContainsRule(parameterId, text), null, new { caseSensitiveApplied = false });
                        case "not_contains":
                            return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateNotContainsRule(parameterId, text), null, new { caseSensitiveApplied = false });
                        case "begins_with":
                            return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateBeginsWithRule(parameterId, text), null, new { caseSensitiveApplied = false });
                        case "ends_with":
                            return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateEndsWithRule(parameterId, text), null, new { caseSensitiveApplied = false });
                        default:
                            return (false, null, $"Unsupported operator for string: {op}", new { op });
                    }
                }
                catch (Exception ex)
                {
                    return (false, null, $"Failed to create string rule ({op}).", new { op, detail = ex.Message });
                }
            }

            // 2) elementId rules
            if (kind == "elementid")
            {
                if (!(valueObj?["elementId"] is Newtonsoft.Json.Linq.JToken eidTok) || !TryReadLong(eidTok, out var eidLong) || eidLong == 0)
                    return (false, null, "value.elementId is required for kind=elementId.", null);
                var eid = Autodesk.Revit.DB.ElementIdCompat.From(eidLong);
                try
                {
                    switch (op)
                    {
                        case "equals":
                            return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateEqualsRule(parameterId, eid), null, null);
                        case "not_equals":
                            return (true, Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, eid), null, null);
                        default:
                            return (false, null, $"Unsupported operator for elementId: {op}", new { op });
                    }
                }
                catch (Exception ex)
                {
                    return (false, null, $"Failed to create elementId rule ({op}).", new { op, detail = ex.Message });
                }
            }

            // 3) numeric rules (int/double/bool)
            bool isBool = (kind == "boolean");
            bool isInt = (kind == "integer");
            bool isDouble = (kind == "double");

            // Scalar inference
            if (kind.Length == 0)
            {
                if (valueTok.Type == Newtonsoft.Json.Linq.JTokenType.Boolean) isBool = true;
                else if (valueTok.Type == Newtonsoft.Json.Linq.JTokenType.Integer) isInt = true;
                else if (valueTok.Type == Newtonsoft.Json.Linq.JTokenType.Float) isDouble = true;
            }

            if (isBool)
            {
                int b = 0;
                try
                {
                    b = valueObj != null ? ((valueObj.Value<bool?>("value") ?? false) ? 1 : 0) : (valueTok.Value<bool>() ? 1 : 0);
                }
                catch { b = 0; }

                isInt = true;
                isBool = false;
                if (valueObj == null) valueObj = new Newtonsoft.Json.Linq.JObject();
                valueObj["number"] = b;
            }

            if (isInt || isDouble || valueTok.Type == Newtonsoft.Json.Linq.JTokenType.Integer || valueTok.Type == Newtonsoft.Json.Linq.JTokenType.Float)
            {
                double number = 0.0;
                bool isWholeNumber = false;
                try
                {
                    if (valueObj != null && valueObj["number"] != null) number = valueObj.Value<double?>("number") ?? 0.0;
                    else number = valueTok.Value<double>();
                    isWholeNumber = Math.Abs(number - Math.Round(number)) < 1e-9;
                }
                catch
                {
                    return (false, null, "value.number must be numeric.", null);
                }

                // unit conversion (best-effort)
                try
                {
                    var unit = (valueObj != null ? (valueObj.Value<string>("unit") ?? string.Empty) : string.Empty).Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(unit))
                    {
                        Autodesk.Revit.DB.ForgeTypeId ut;
                        switch (unit)
                        {
                            case "mm": ut = Autodesk.Revit.DB.UnitTypeId.Millimeters; break;
                            case "cm": ut = Autodesk.Revit.DB.UnitTypeId.Centimeters; break;
                            case "m": ut = Autodesk.Revit.DB.UnitTypeId.Meters; break;
                            case "ft": ut = Autodesk.Revit.DB.UnitTypeId.Feet; break;
                            case "in": ut = Autodesk.Revit.DB.UnitTypeId.Inches; break;
                            case "deg": ut = Autodesk.Revit.DB.UnitTypeId.Degrees; break;
                            case "rad": ut = Autodesk.Revit.DB.UnitTypeId.Radians; break;
                            case "m2": ut = Autodesk.Revit.DB.UnitTypeId.SquareMeters; break;
                            case "ft2": ut = Autodesk.Revit.DB.UnitTypeId.SquareFeet; break;
                            case "m3": ut = Autodesk.Revit.DB.UnitTypeId.CubicMeters; break;
                            case "ft3": ut = Autodesk.Revit.DB.UnitTypeId.CubicFeet; break;
                            default: ut = null; break;
                        }
                        if (ut != null) number = Autodesk.Revit.DB.UnitUtils.ConvertToInternalUnits(number, ut);
                    }
                }
                catch { /* ignore */ }

                double tol = 0.0;
                try { tol = valueObj != null ? (valueObj.Value<double?>("tolerance") ?? 0.0) : 0.0; } catch { tol = 0.0; }
                if (tol < 0) tol = 0;
                if (tol == 0.0) tol = 1e-6;

                Exception last = null;
                Autodesk.Revit.DB.FilterRule created = null;

                if (isInt || (isWholeNumber && !isDouble))
                {
                    try
                    {
                        int iv = (int)Math.Round(number);
                        created = CreateIntRule(parameterId, op, iv);
                        if (created != null) return (true, created, null, null);
                    }
                    catch (Exception ex) { last = ex; }
                }

                try
                {
                    created = CreateDoubleRule(parameterId, op, number, tol);
                    if (created != null) return (true, created, null, null);
                }
                catch (Exception ex) { last = ex; }

                if (isWholeNumber)
                {
                    try
                    {
                        int iv = (int)Math.Round(number);
                        created = CreateIntRule(parameterId, op, iv);
                        if (created != null) return (true, created, null, null);
                    }
                    catch (Exception ex) { last = ex; }
                }

                return (false, null, $"Failed to create numeric rule ({op}).", new { op, detail = last != null ? last.Message : "unknown" });
            }

            return (false, null, $"Unsupported rule value type for operator={op}.", new { op, valueType = valueTok.Type.ToString() });
        }

        private static Autodesk.Revit.DB.FilterRule TryCreateStringRuleCaseSensitive(
            Autodesk.Revit.DB.ElementId parameterId,
            string op,
            string text,
            bool caseSensitive)
        {
            try
            {
                if (!caseSensitive) return null;
                if (parameterId == null || parameterId == Autodesk.Revit.DB.ElementId.InvalidElementId) return null;

                op = (op ?? string.Empty).Trim().ToLowerInvariant();
                var t = typeof(Autodesk.Revit.DB.ParameterFilterRuleFactory);
                System.Reflection.MethodInfo mi = null;

                switch (op)
                {
                    case "equals":
                        mi = t.GetMethod("CreateEqualsRule", new[] { typeof(Autodesk.Revit.DB.ElementId), typeof(string), typeof(bool) });
                        break;
                    case "not_equals":
                        mi = t.GetMethod("CreateNotEqualsRule", new[] { typeof(Autodesk.Revit.DB.ElementId), typeof(string), typeof(bool) });
                        break;
                    case "contains":
                        mi = t.GetMethod("CreateContainsRule", new[] { typeof(Autodesk.Revit.DB.ElementId), typeof(string), typeof(bool) });
                        break;
                    case "not_contains":
                        mi = t.GetMethod("CreateNotContainsRule", new[] { typeof(Autodesk.Revit.DB.ElementId), typeof(string), typeof(bool) });
                        break;
                    case "begins_with":
                        mi = t.GetMethod("CreateBeginsWithRule", new[] { typeof(Autodesk.Revit.DB.ElementId), typeof(string), typeof(bool) });
                        break;
                    case "ends_with":
                        mi = t.GetMethod("CreateEndsWithRule", new[] { typeof(Autodesk.Revit.DB.ElementId), typeof(string), typeof(bool) });
                        break;
                    default:
                        return null;
                }

                if (mi == null) return null;
                var res = mi.Invoke(null, new object[] { parameterId, text ?? string.Empty, true });
                return res as Autodesk.Revit.DB.FilterRule;
            }
            catch
            {
                return null;
            }
        }

        private static Autodesk.Revit.DB.FilterRule CreateIntRule(Autodesk.Revit.DB.ElementId parameterId, string op, int value)
        {
            switch (op)
            {
                case "equals": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateEqualsRule(parameterId, value);
                case "not_equals": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, value);
                case "greater_than": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateGreaterRule(parameterId, value);
                case "greater_or_equal": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateGreaterOrEqualRule(parameterId, value);
                case "less_than": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateLessRule(parameterId, value);
                case "less_or_equal": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateLessOrEqualRule(parameterId, value);
                default: return null;
            }
        }

        private static Autodesk.Revit.DB.FilterRule CreateDoubleRule(Autodesk.Revit.DB.ElementId parameterId, string op, double value, double tolerance)
        {
            switch (op)
            {
                case "equals": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateEqualsRule(parameterId, value, tolerance);
                case "not_equals": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateNotEqualsRule(parameterId, value, tolerance);
                case "greater_than": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateGreaterRule(parameterId, value, tolerance);
                case "greater_or_equal": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateGreaterOrEqualRule(parameterId, value, tolerance);
                case "less_than": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateLessRule(parameterId, value, tolerance);
                case "less_or_equal": return Autodesk.Revit.DB.ParameterFilterRuleFactory.CreateLessOrEqualRule(parameterId, value, tolerance);
                default: return null;
            }
        }
    }

    [RpcCommand("view_filter.list",
        Category = "ViewFilterOps",
        Tags = new[] { "View", "Filter" },
        Kind = "read",
        Risk = RiskLevel.Low,
        Summary = "List all view filters (parameter and selection filters) in the current project.")]
    public sealed class ViewFilterListCommand : RevitMCPAddin.Core.IRevitCommandHandler
    {
        public string CommandName => "view_filter.list";

        public object Execute(Autodesk.Revit.UI.UIApplication uiapp, RevitMCPAddin.Core.RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = cmd.Params as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            try
            {
                var kindsArr = p["kinds"] as Newtonsoft.Json.Linq.JArray;
                var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (kindsArr != null && kindsArr.Count > 0)
                {
                    foreach (var t in kindsArr)
                    {
                        var s = (t?.Value<string>() ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(s)) kinds.Add(s);
                    }
                }
                if (kinds.Count == 0)
                {
                    kinds.Add("parameter");
                    kinds.Add("selection");
                }

                var prefix = (p.Value<string>("namePrefix") ?? string.Empty).Trim();

                var filters = new List<object>();
                var all = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.FilterElement))
                    .Cast<Autodesk.Revit.DB.FilterElement>()
                    .ToList();

                foreach (var f in all)
                {
                    if (f == null) continue;
                    var k = (f is Autodesk.Revit.DB.ParameterFilterElement)
                        ? "parameter"
                        : (f is Autodesk.Revit.DB.SelectionFilterElement ? "selection" : "unknown");
                    if (!kinds.Contains(k)) continue;

                    var fname = f.Name ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(prefix) && !fname.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string[] cats = new string[0];
                    int ruleCount = 0;
                    int elementIdCount = 0;

                    if (f is Autodesk.Revit.DB.ParameterFilterElement pfe)
                    {
                        try
                        {
                            var catIds = pfe.GetCategories() ?? new List<Autodesk.Revit.DB.ElementId>();
                            cats = catIds.Select(ViewFilterUtil.BuiltInCategoryName).Distinct().ToArray();
                        }
                        catch { cats = new string[0]; }

                        try
                        {
                            var ef = pfe.GetElementFilter();
                            if (ef is Autodesk.Revit.DB.ElementParameterFilter epf)
                            {
                                try { ruleCount = (epf.GetRules() ?? new List<Autodesk.Revit.DB.FilterRule>()).Count; } catch { ruleCount = 0; }
                            }
                        }
                        catch { ruleCount = 0; }
                    }
                    else if (f is Autodesk.Revit.DB.SelectionFilterElement sfe)
                    {
                        try
                        {
                            var ids = sfe.GetElementIds();
                            elementIdCount = ids != null ? ids.Count : 0;
                        }
                        catch { elementIdCount = 0; }
                    }

                    filters.Add(new
                    {
                        elementId = f.Id.IntValue(),
                        uniqueId = f.UniqueId,
                        kind = k,
                        name = fname,
                        categories = cats,
                        ruleCount,
                        elementIdCount
                    });
                }

                return new { ok = true, filters };
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "LIST_FAILED", msg = ex.Message };
            }
        }
    }

    [RpcCommand("view_filter.get_order",
        Category = "ViewFilterOps",
        Tags = new[] { "View", "Filter" },
        Kind = "read",
        Risk = RiskLevel.Low,
        Summary = "Get the actual view filter order (priority) in a view/template.")]
    public sealed class ViewFilterGetOrderCommand : RevitMCPAddin.Core.IRevitCommandHandler
    {
        public string CommandName => "view_filter.get_order";

        public object Execute(Autodesk.Revit.UI.UIApplication uiapp, RevitMCPAddin.Core.RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = cmd.Params as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            var viewObj = p["view"] as Newtonsoft.Json.Linq.JObject;
            if (viewObj == null) return new { ok = false, code = "MISSING_VIEW", msg = "view is required." };

            var viewRes = ViewFilterUtil.ResolveView(doc, viewObj);
            if (!viewRes.ok || viewRes.view == null) return new { ok = false, code = viewRes.errorCode ?? "VIEW_NOT_FOUND", msg = viewRes.msg, details = viewRes.data };

            try
            {
                var order = new List<int>();
                foreach (var id in viewRes.view.GetOrderedFilters() ?? new List<Autodesk.Revit.DB.ElementId>())
                {
                    order.Add(id.IntValue());
                }
                return new { ok = true, viewId = viewRes.view.Id.IntValue(), orderedFilterIds = order };
            }
            catch (Exception ex)
            {
                return new { ok = false, code = "GET_ORDER_FAILED", msg = ex.Message };
            }
        }
    }

    [RpcCommand("view_filter.upsert",
        Category = "ViewFilterOps",
        Tags = new[] { "View", "Filter" },
        Kind = "write",
        Risk = RiskLevel.Medium,
        Summary = "Create or update a view filter definition (parameter filter or selection filter).")]
    public sealed class ViewFilterUpsertCommand : RevitMCPAddin.Core.IRevitCommandHandler
    {
        public string CommandName => "view_filter.upsert";

        public object Execute(Autodesk.Revit.UI.UIApplication uiapp, RevitMCPAddin.Core.RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = cmd.Params as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            var def = p["definition"] as Newtonsoft.Json.Linq.JObject ?? p;
            var kind = (def.Value<string>("kind") ?? "parameter").Trim().ToLowerInvariant();
            var name = (def.Value<string>("name") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return new { ok = false, code = "MISSING_NAME", msg = "definition.name is required." };

            var warnings = new List<string>();

            using (var tx = new Autodesk.Revit.DB.Transaction(doc, "[MCP] view_filter.upsert"))
            {
                tx.Start();
                try
                {
                    var existingAny = new Autodesk.Revit.DB.FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.FilterElement))
                        .Cast<Autodesk.Revit.DB.FilterElement>()
                        .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

                    if (kind == "parameter")
                    {
                        var catsArr = def["categories"] as Newtonsoft.Json.Linq.JArray;
                        var catsRes = ViewFilterUtil.ResolveCategories(doc, catsArr);
                        if (!catsRes.ok)
                        {
                            tx.RollBack();
                            return new { ok = false, code = "INVALID_CATEGORIES", msg = catsRes.msg, details = catsRes.details };
                        }

                        var rulesArr = def["rules"] as Newtonsoft.Json.Linq.JArray;
                        if (rulesArr == null || rulesArr.Count == 0)
                        {
                            tx.RollBack();
                            return new { ok = false, code = "MISSING_RULES", msg = "definition.rules[] is required for parameter filters." };
                        }

                        // MVP: AND-only
                        var logic = (def.Value<string>("logic") ?? "and").Trim().ToLowerInvariant();
                        if (logic != "and")
                        {
                            tx.RollBack();
                            return new { ok = false, code = "UNSUPPORTED_LOGIC", msg = "Only logic='and' is supported for MVP." };
                        }

                        ICollection<Autodesk.Revit.DB.ElementId> commonParams;
                        try
                        {
                            commonParams = Autodesk.Revit.DB.ParameterFilterUtilities.GetFilterableParametersInCommon(doc, catsRes.categoryIds);
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            return new { ok = false, code = "COMMON_PARAMS_FAILED", msg = ex.Message };
                        }

                        var commonSet = new HashSet<long>((commonParams ?? new List<Autodesk.Revit.DB.ElementId>()).Select(x => x.LongValue()));

                        var filterRules = new List<Autodesk.Revit.DB.FilterRule>();
                        int idx = 0;
                        foreach (var rt in rulesArr)
                        {
                            idx++;
                            if (!(rt is Newtonsoft.Json.Linq.JObject ro))
                            {
                                tx.RollBack();
                                return new { ok = false, code = "INVALID_RULE", msg = $"rules[{idx - 1}] must be an object." };
                            }

                            if (!(ro["parameter"] is Newtonsoft.Json.Linq.JObject paramObj))
                            {
                                tx.RollBack();
                                return new { ok = false, code = "INVALID_RULE", msg = $"rules[{idx - 1}].parameter is required." };
                            }

                            var pidRes = ViewFilterUtil.ResolveParameterId(doc, catsRes.categoryIds, paramObj);
                            if (!pidRes.ok)
                            {
                                tx.RollBack();
                                return new { ok = false, code = "PARAM_RESOLVE_FAILED", msg = pidRes.msg, details = pidRes.details };
                            }

                            var pid = pidRes.parameterId;
                            if (!commonSet.Contains(pid.LongValue()))
                            {
                                tx.RollBack();
                                return new
                                {
                                    ok = false,
                                    code = "PARAM_NOT_FILTERABLE_FOR_CATEGORIES",
                                    msg = "Parameter is not filterable in common for the specified categories.",
                                    details = new
                                    {
                                        parameterId = pid.IntValue(),
                                        categories = catsRes.categoryIds.Select(x => x.IntValue()).ToArray()
                                    }
                                };
                            }

                            var op = (ro.Value<string>("operator") ?? string.Empty).Trim();
                            var valTok = ro["value"];
                            var ruleRes = ViewFilterUtil.BuildFilterRule(doc, pid, op, valTok, ro);
                            if (!ruleRes.ok || ruleRes.rule == null)
                            {
                                tx.RollBack();
                                return new { ok = false, code = "RULE_BUILD_FAILED", msg = ruleRes.msg, details = ruleRes.details };
                            }

                            filterRules.Add(ruleRes.rule);
                        }

                        var elemFilter = new Autodesk.Revit.DB.ElementParameterFilter(filterRules);

                        string action;
                        var target = existingAny as Autodesk.Revit.DB.ParameterFilterElement;
                        if (existingAny != null && target == null)
                        {
                            tx.RollBack();
                            return new
                            {
                                ok = false,
                                code = "NAME_CONFLICT",
                                msg = $"A different filter element with the same name already exists: {existingAny.GetType().Name}",
                                details = new { name, existingType = existingAny.GetType().Name, existingId = existingAny.Id.IntValue() }
                            };
                        }

                        if (target == null)
                        {
                            target = Autodesk.Revit.DB.ParameterFilterElement.Create(doc, name, catsRes.categoryIds, elemFilter);
                            action = "created";
                        }
                        else
                        {
                            target.SetCategories(catsRes.categoryIds);
                            target.SetElementFilter(elemFilter);
                            action = "updated";
                        }

                        tx.Commit();
                        return new
                        {
                            ok = true,
                            action,
                            filter = new { elementId = target.Id.IntValue(), name = target.Name, kind = "parameter" },
                            warnings
                        };
                    }

                    if (kind == "selection")
                    {
                        var uidsArr = def["elementUniqueIds"] as Newtonsoft.Json.Linq.JArray;
                        var idsArr = def["elementIds"] as Newtonsoft.Json.Linq.JArray;

                        var ids = new HashSet<Autodesk.Revit.DB.ElementId>();
                        if (idsArr != null && idsArr.Count > 0)
                        {
                            foreach (var t in idsArr)
                            {
                                if (ViewFilterUtil.TryReadLong(t, out var v) && v != 0)
                                    ids.Add(Autodesk.Revit.DB.ElementIdCompat.From(v));
                            }
                        }
                        else if (uidsArr != null && uidsArr.Count > 0)
                        {
                            foreach (var t in uidsArr)
                            {
                                var uid = (t?.Value<string>() ?? string.Empty).Trim();
                                if (string.IsNullOrWhiteSpace(uid)) continue;
                                try
                                {
                                    var e = doc.GetElement(uid);
                                    if (e != null) ids.Add(e.Id);
                                    else warnings.Add($"uniqueId not found: {uid}");
                                }
                                catch { warnings.Add($"uniqueId resolve failed: {uid}"); }
                            }
                        }
                        else
                        {
                            tx.RollBack();
                            return new { ok = false, code = "MISSING_SELECTION_SET", msg = "definition.elementUniqueIds[] or definition.elementIds[] is required for selection filters." };
                        }

                        string action;
                        var target = existingAny as Autodesk.Revit.DB.SelectionFilterElement;
                        if (existingAny != null && target == null)
                        {
                            tx.RollBack();
                            return new
                            {
                                ok = false,
                                code = "NAME_CONFLICT",
                                msg = $"A different filter element with the same name already exists: {existingAny.GetType().Name}",
                                details = new { name, existingType = existingAny.GetType().Name, existingId = existingAny.Id.IntValue() }
                            };
                        }

                        if (target == null)
                        {
                            target = Autodesk.Revit.DB.SelectionFilterElement.Create(doc, name);
                            target.SetElementIds(ids.ToList());
                            action = "created";
                        }
                        else
                        {
                            target.SetElementIds(ids.ToList());
                            action = "updated";
                        }

                        tx.Commit();
                        return new
                        {
                            ok = true,
                            action,
                            filter = new { elementId = target.Id.IntValue(), name = target.Name, kind = "selection" },
                            warnings
                        };
                    }

                    tx.RollBack();
                    return new { ok = false, code = "UNSUPPORTED_KIND", msg = $"Unsupported kind: {kind}" };
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { /* ignore */ }
                    return new { ok = false, code = "UPSERT_FAILED", msg = ex.Message, detail = ex.ToString() };
                }
            }
        }
    }

    [RpcCommand("view_filter.delete",
        Category = "ViewFilterOps",
        Tags = new[] { "View", "Filter" },
        Kind = "write",
        Risk = RiskLevel.High,
        Summary = "Delete a view filter definition from the project.")]
    public sealed class ViewFilterDeleteCommand : RevitMCPAddin.Core.IRevitCommandHandler
    {
        public string CommandName => "view_filter.delete";

        public object Execute(Autodesk.Revit.UI.UIApplication uiapp, RevitMCPAddin.Core.RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = cmd.Params as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            var filterObj = p["filter"] as Newtonsoft.Json.Linq.JObject;
            if (filterObj == null) return new { ok = false, code = "MISSING_FILTER", msg = "filter is required." };

            var res = ViewFilterUtil.ResolveFilter(doc, filterObj);
            if (!res.ok || res.filter == null) return new { ok = false, code = res.errorCode ?? "FILTER_NOT_FOUND", msg = res.msg, details = res.data };

            using (var tx = new Autodesk.Revit.DB.Transaction(doc, "[MCP] view_filter.delete"))
            {
                tx.Start();
                try
                {
                    doc.Delete(res.filter.Id);
                    tx.Commit();
                    return new { ok = true, deleted = true, filterId = res.filter.Id.IntValue() };
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { /* ignore */ }
                    return new { ok = false, code = "DELETE_FAILED", msg = ex.Message };
                }
            }
        }
    }

    [RpcCommand("view_filter.apply_to_view",
        Category = "ViewFilterOps",
        Tags = new[] { "View", "Filter" },
        Kind = "write",
        Risk = RiskLevel.Medium,
        Summary = "Apply a filter to a view or view template (visibility/enabled/overrides).")]
    public sealed class ViewFilterApplyToViewCommand : RevitMCPAddin.Core.IRevitCommandHandler
    {
        public string CommandName => "view_filter.apply_to_view";

        public object Execute(Autodesk.Revit.UI.UIApplication uiapp, RevitMCPAddin.Core.RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = cmd.Params as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            var appObj = p["application"] as Newtonsoft.Json.Linq.JObject;
            if (appObj == null) return new { ok = false, code = "MISSING_APPLICATION", msg = "application is required." };

            var viewObj = appObj["view"] as Newtonsoft.Json.Linq.JObject;
            var filterObj = appObj["filter"] as Newtonsoft.Json.Linq.JObject;
            if (viewObj == null) return new { ok = false, code = "MISSING_VIEW", msg = "application.view is required." };
            if (filterObj == null) return new { ok = false, code = "MISSING_FILTER", msg = "application.filter is required." };

            var viewRes = ViewFilterUtil.ResolveView(doc, viewObj);
            if (!viewRes.ok || viewRes.view == null) return new { ok = false, code = viewRes.errorCode ?? "VIEW_NOT_FOUND", msg = viewRes.msg, details = viewRes.data };

            var filterRes = ViewFilterUtil.ResolveFilter(doc, filterObj);
            if (!filterRes.ok || filterRes.filter == null) return new { ok = false, code = filterRes.errorCode ?? "FILTER_NOT_FOUND", msg = filterRes.msg, details = filterRes.data };

            bool detachViewTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            bool enabledHas = appObj["enabled"] != null && appObj["enabled"].Type != Newtonsoft.Json.Linq.JTokenType.Null;
            bool visibleHas = appObj["visible"] != null && appObj["visible"].Type != Newtonsoft.Json.Linq.JTokenType.Null;
            bool enabled = appObj.Value<bool?>("enabled") ?? true;
            bool visible = appObj.Value<bool?>("visible") ?? true;
            var overridesObj = appObj["overrides"] as Newtonsoft.Json.Linq.JObject;

            using (var tx = new Autodesk.Revit.DB.Transaction(doc, "[MCP] view_filter.apply_to_view"))
            {
                tx.Start();
                try
                {
                    bool templateDetached;
                    var tmplCheck = ViewFilterUtil.CheckViewTemplateLock(viewRes.view, detachViewTemplate, "view_filter.apply_to_view", out templateDetached);
                    if (tmplCheck != null)
                    {
                        tx.RollBack();
                        return tmplCheck;
                    }

                    var view = viewRes.view;
                    var fid = filterRes.filter.Id;
                    bool wasAdded = false;

                    var applied = new HashSet<long>();
                    try
                    {
                        foreach (var id in view.GetFilters() ?? new List<Autodesk.Revit.DB.ElementId>())
                            applied.Add(id.LongValue());
                    }
                    catch { /* ignore */ }

                    if (!applied.Contains(fid.LongValue()))
                    {
                        view.AddFilter(fid);
                        wasAdded = true;
                    }

                    if (overridesObj != null)
                    {
                        var ogs = ViewFilterUtil.BuildOverrides(overridesObj);
                        view.SetFilterOverrides(fid, ogs);
                    }

                    if (visibleHas)
                    {
                        view.SetFilterVisibility(fid, visible);
                    }

                    if (enabledHas)
                    {
                        view.SetIsFilterEnabled(fid, enabled);
                    }

                    tx.Commit();
                    return new
                    {
                        ok = true,
                        applied = true,
                        wasAdded,
                        templateDetached,
                        viewId = view.Id.IntValue(),
                        filterId = fid.IntValue()
                    };
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { /* ignore */ }
                    return new { ok = false, code = "APPLY_FAILED", msg = ex.Message, detail = ex.ToString() };
                }
            }
        }
    }

    [RpcCommand("view_filter.remove_from_view",
        Category = "ViewFilterOps",
        Tags = new[] { "View", "Filter" },
        Kind = "write",
        Risk = RiskLevel.Medium,
        Summary = "Remove a filter from a view or view template.")]
    public sealed class ViewFilterRemoveFromViewCommand : RevitMCPAddin.Core.IRevitCommandHandler
    {
        public string CommandName => "view_filter.remove_from_view";

        public object Execute(Autodesk.Revit.UI.UIApplication uiapp, RevitMCPAddin.Core.RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = cmd.Params as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            var viewObj = p["view"] as Newtonsoft.Json.Linq.JObject;
            var filterObj = p["filter"] as Newtonsoft.Json.Linq.JObject;
            if (viewObj == null) return new { ok = false, code = "MISSING_VIEW", msg = "view is required." };
            if (filterObj == null) return new { ok = false, code = "MISSING_FILTER", msg = "filter is required." };

            var viewRes = ViewFilterUtil.ResolveView(doc, viewObj);
            if (!viewRes.ok || viewRes.view == null) return new { ok = false, code = viewRes.errorCode ?? "VIEW_NOT_FOUND", msg = viewRes.msg, details = viewRes.data };

            var filterRes = ViewFilterUtil.ResolveFilter(doc, filterObj);
            if (!filterRes.ok || filterRes.filter == null) return new { ok = false, code = filterRes.errorCode ?? "FILTER_NOT_FOUND", msg = filterRes.msg, details = filterRes.data };

            bool detachViewTemplate = p.Value<bool?>("detachViewTemplate") ?? false;

            using (var tx = new Autodesk.Revit.DB.Transaction(doc, "[MCP] view_filter.remove_from_view"))
            {
                tx.Start();
                try
                {
                    bool templateDetached;
                    var tmplCheck = ViewFilterUtil.CheckViewTemplateLock(viewRes.view, detachViewTemplate, "view_filter.remove_from_view", out templateDetached);
                    if (tmplCheck != null)
                    {
                        tx.RollBack();
                        return tmplCheck;
                    }

                    var view = viewRes.view;
                    var fid = filterRes.filter.Id;
                    bool removed = false;

                    try
                    {
                        var applied = new HashSet<long>((view.GetFilters() ?? new List<Autodesk.Revit.DB.ElementId>()).Select(x => x.LongValue()));
                        if (applied.Contains(fid.LongValue()))
                        {
                            view.RemoveFilter(fid);
                            removed = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return new { ok = false, code = "REMOVE_FAILED", msg = ex.Message };
                    }

                    tx.Commit();
                    return new { ok = true, removed, templateDetached, viewId = view.Id.IntValue(), filterId = fid.IntValue() };
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { /* ignore */ }
                    return new { ok = false, code = "REMOVE_FAILED", msg = ex.Message };
                }
            }
        }
    }

    [RpcCommand("view_filter.set_order",
        Category = "ViewFilterOps",
        Tags = new[] { "View", "Filter" },
        Kind = "write",
        Risk = RiskLevel.Medium,
        Summary = "Set view filter order (priority) while preserving overrides/visibility/enabled.")]
    public sealed class ViewFilterSetOrderCommand : RevitMCPAddin.Core.IRevitCommandHandler
    {
        public string CommandName => "view_filter.set_order";

        private sealed class FilterState
        {
            public bool WasApplied;
            public bool Visible;
            public bool Enabled;
            public Autodesk.Revit.DB.OverrideGraphicSettings Overrides = new Autodesk.Revit.DB.OverrideGraphicSettings();
        }

        public object Execute(Autodesk.Revit.UI.UIApplication uiapp, RevitMCPAddin.Core.RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, code = "NO_DOC", msg = "No active document." };

            var p = cmd.Params as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            var viewObj = p["view"] as Newtonsoft.Json.Linq.JObject;
            if (viewObj == null) return new { ok = false, code = "MISSING_VIEW", msg = "view is required." };

            var viewRes = ViewFilterUtil.ResolveView(doc, viewObj);
            if (!viewRes.ok || viewRes.view == null) return new { ok = false, code = viewRes.errorCode ?? "VIEW_NOT_FOUND", msg = viewRes.msg, details = viewRes.data };

            var orderedTok = p["orderedFilterIds"] ?? p["elementIds"]; // allow elementIds alias
            var desiredIds = ViewFilterUtil.ReadElementIdsArray(orderedTok);
            if (desiredIds.Count == 0) return new { ok = false, code = "MISSING_ORDER", msg = "orderedFilterIds[] is required." };

            bool preserveUnlisted = p.Value<bool?>("preserveUnlisted") ?? true;
            bool detachViewTemplate = p.Value<bool?>("detachViewTemplate") ?? false;

            using (var tx = new Autodesk.Revit.DB.Transaction(doc, "[MCP] view_filter.set_order"))
            {
                tx.Start();
                try
                {
                    bool templateDetached;
                    var tmplCheck = ViewFilterUtil.CheckViewTemplateLock(viewRes.view, detachViewTemplate, "view_filter.set_order", out templateDetached);
                    if (tmplCheck != null)
                    {
                        tx.RollBack();
                        return tmplCheck;
                    }

                    var view = viewRes.view;
                    var currentOrder = (view.GetOrderedFilters() ?? new List<Autodesk.Revit.DB.ElementId>()).ToList();

                    // Final order
                    var final = new List<Autodesk.Revit.DB.ElementId>();
                    var seen = new HashSet<long>();
                    foreach (var id in desiredIds)
                    {
                        if (id == null || id == Autodesk.Revit.DB.ElementId.InvalidElementId) continue;
                        var k = id.LongValue();
                        if (seen.Add(k)) final.Add(id);
                    }
                    if (preserveUnlisted)
                    {
                        foreach (var id in currentOrder)
                        {
                            if (id == null || id == Autodesk.Revit.DB.ElementId.InvalidElementId) continue;
                            var k = id.LongValue();
                            if (seen.Add(k)) final.Add(id);
                        }
                    }

                    // Applied set
                    var appliedSet = new HashSet<long>();
                    try
                    {
                        foreach (var id in view.GetFilters() ?? new List<Autodesk.Revit.DB.ElementId>())
                            appliedSet.Add(id.LongValue());
                    }
                    catch { /* ignore */ }

                    // Snapshot states (safe defaults for not-applied yet)
                    var states = new Dictionary<long, FilterState>();
                    foreach (var id in final)
                    {
                        var k = id.LongValue();
                        var st = new FilterState();
                        st.WasApplied = appliedSet.Contains(k);
                        st.Visible = true;
                        st.Enabled = true;
                        st.Overrides = new Autodesk.Revit.DB.OverrideGraphicSettings();

                        if (st.WasApplied)
                        {
                            try { st.Visible = view.GetFilterVisibility(id); } catch { st.Visible = true; }
                            try { st.Enabled = view.GetIsFilterEnabled(id); } catch { st.Enabled = true; }
                            try { st.Overrides = view.GetFilterOverrides(id); } catch { st.Overrides = new Autodesk.Revit.DB.OverrideGraphicSettings(); }
                        }
                        states[k] = st;
                    }

                    // Remove filters (only those currently applied)
                    foreach (var id in final)
                    {
                        var k = id.LongValue();
                        if (!appliedSet.Contains(k)) continue;
                        try { view.RemoveFilter(id); } catch { /* ignore */ }
                    }

                    // Add back in desired order
                    foreach (var id in final)
                    {
                        view.AddFilter(id);
                    }

                    // Restore state
                    foreach (var id in final)
                    {
                        var k = id.LongValue();
                        if (!states.TryGetValue(k, out var st)) continue;
                        try { view.SetFilterOverrides(id, st.Overrides ?? new Autodesk.Revit.DB.OverrideGraphicSettings()); } catch { /* ignore */ }
                        try { view.SetFilterVisibility(id, st.Visible); } catch { /* ignore */ }
                        try { view.SetIsFilterEnabled(id, st.Enabled); } catch { /* ignore */ }
                    }

                    var verified = (view.GetOrderedFilters() ?? new List<Autodesk.Revit.DB.ElementId>()).Select(x => x.IntValue()).ToList();

                    tx.Commit();
                    return new
                    {
                        ok = true,
                        reordered = true,
                        templateDetached,
                        viewId = view.Id.IntValue(),
                        finalOrder = verified
                    };
                }
                catch (Exception ex)
                {
                    try { tx.RollBack(); } catch { /* ignore */ }
                    return new { ok = false, code = "SET_ORDER_FAILED", msg = ex.Message, detail = ex.ToString() };
                }
            }
        }
    }
}
