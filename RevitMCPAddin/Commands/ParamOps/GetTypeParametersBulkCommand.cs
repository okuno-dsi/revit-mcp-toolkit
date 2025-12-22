// File: Commands/ParamOps/GetTypeParametersBulkCommand.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ParamOps
{
    public class GetTypeParametersBulkCommand : IRevitCommandHandler
    {
        public string CommandName => "get_type_parameters_bulk";

        private class KeySpec
        {
            public int? BuiltInId;
            public string Guid;
            public string Name;
        }

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            var paramKeys = ParseParamKeys(p["paramKeys"]);
            if (paramKeys.Count == 0) return new { ok = false, msg = "paramKeys is required." };

            // Build the type id list
            var typeIds = new List<ElementId>();
            var typeIdsTok = p["typeIds"] as JArray;
            if (typeIdsTok != null && typeIdsTok.Count > 0)
            {
                foreach (var t in typeIdsTok) { try { typeIds.Add(Autodesk.Revit.DB.ElementIdCompat.From(Convert.ToInt32(t))); } catch { } }
            }
            else
            {
                var catIds = ParseIntArray(p["categories"]);
                if (catIds.Count > 0)
                {
                    foreach (var ci in catIds)
                    {
                        try
                        {
                            var bic = (BuiltInCategory)ci;
                            var ids = new FilteredElementCollector(doc)
                                .WhereElementIsElementType()
                                .OfCategory(bic)
                                .Select(e => e.Id);
                            typeIds.AddRange(ids);
                        }
                        catch { /* unsupported category id; ignore */ }
                    }
                }
                else
                {
                    // All element types (may be large)
                    typeIds.AddRange(new FilteredElementCollector(doc).WhereElementIsElementType().Select(e => e.Id));
                }
            }

            var start = Math.Max(0, p.SelectToken("page.startIndex")?.Value<int?>() ?? 0);
            var batch = Math.Max(1, p.SelectToken("page.batchSize")?.Value<int?>() ?? Math.Min(500, typeIds.Count));
            var total = typeIds.Count;
            var slice = typeIds.Skip(start).Take(batch).ToList();

            var items = new List<object>(slice.Count);
            foreach (var id in slice)
            {
                try
                {
                    var et = doc.GetElement(id) as ElementType;
                    if (et == null)
                    {
                        items.Add(new { ok = false, typeId = id.IntValue(), errors = new[] { "not_found" } });
                        continue;
                    }
                    var map = new Dictionary<string, object>();
                    var disp = new Dictionary<string, string>();
                    var errors = new List<string>();
                    foreach (var k in paramKeys)
                    {
                        var (ok, name, val, shown, err) = TryGetParamNormalized(et, k);
                        if (ok)
                        {
                            map[name] = val ?? "";
                            if (!string.IsNullOrEmpty(shown)) disp[name] = shown;
                        }
                        else if (!string.IsNullOrEmpty(err)) errors.Add(err);
                    }
                    items.Add(new
                    {
                        ok = true,
                        typeId = et.Id.IntValue(),
                        typeName = et.Name,
                        categoryId = et.Category != null ? (int?)et.Category.Id.IntValue() : null,
                        @params = map,
                        display = disp.Count > 0 ? (object)disp : null,
                        errors = errors.Count > 0 ? errors : null
                    });
                }
                catch (Exception ex)
                {
                    items.Add(new { ok = false, typeId = id.IntValue(), errors = new[] { ex.Message } });
                }
            }

            var next = start + slice.Count;
            bool completed = next >= total;
            return new { ok = true, items, nextIndex = completed ? (int?)null : next, completed, totalCount = total };
        }

        private static List<KeySpec> ParseParamKeys(JToken tok)
        {
            var list = new List<KeySpec>();
            if (tok == null) return list;
            if (tok is JArray arr)
            {
                foreach (var x in arr)
                {
                    if (x is JObject o)
                    {
                        var ks = new KeySpec
                        {
                            BuiltInId = o.Value<int?>("builtInId"),
                            Guid = o.Value<string>("guid"),
                            Name = o.Value<string>("name"),
                        };
                        if (ks.BuiltInId.HasValue || !string.IsNullOrWhiteSpace(ks.Guid) || !string.IsNullOrWhiteSpace(ks.Name))
                            list.Add(ks);
                    }
                    else if (x.Type == JTokenType.String)
                    {
                        list.Add(new KeySpec { Name = x.Value<string>() });
                    }
                }
            }
            return list;
        }

        private static List<int> ParseIntArray(JToken tok)
        {
            var list = new List<int>();
            if (tok is JArray arr)
            {
                foreach (var t in arr) { try { list.Add(Convert.ToInt32(t)); } catch { } }
            }
            return list;
        }

        private static (bool ok, string name, object value, string display, string err) TryGetParam(Element e, KeySpec k)
        {
            Parameter p = null;
            string name = k.Name;
            if (k.BuiltInId.HasValue)
            {
                try { p = e.get_Parameter((BuiltInParameter)k.BuiltInId.Value); name = name ?? ((BuiltInParameter)k.BuiltInId.Value).ToString(); }
                catch { }
            }
            if (p == null && !string.IsNullOrWhiteSpace(k.Guid))
            {
                try { p = e.get_Parameter(new Guid(k.Guid)); name = name ?? k.Guid; }
                catch { }
            }
            if (p == null && !string.IsNullOrWhiteSpace(k.Name))
            {
                try { p = e.LookupParameter(k.Name); name = k.Name; }
                catch { }
            }
            if (p == null)
                return (false, name ?? "", null, null, $"param_not_found:{name}");

            object v = null;
            string shown = null;
            try
            {
                // Prefer UnitHelper for consistent SI + display
                try
                {
                    var info = UnitHelper.ParamToSiInfo(p);
                    if (info is JObject jo)
                    {
                        if (jo["value"] != null) v = jo["value"].ToObject<object>();
                        if (jo["display"] != null) shown = jo["display"].ToString();
                    }
                }
                catch { /* fallback below */ }

                if (v == null && string.IsNullOrEmpty(shown))
                {
                    switch (p.StorageType)
                    {
                        case StorageType.Double:
                            v = p.AsDouble(); break;
                        case StorageType.Integer:
                            v = p.AsInteger(); break;
                        case StorageType.String:
                            v = p.AsString(); break;
                        case StorageType.ElementId:
                            v = p.AsElementId()?.IntValue(); break;
                        default:
                            v = p.AsValueString() ?? p.ToString(); break;
                    }
                    try { shown = p.AsValueString(); } catch { }
                }

                // Heuristic: common structural section keys lacking Spec → treat as length (mm)
                if (p.StorageType == StorageType.Double && (string.IsNullOrEmpty(shown) || v is double) && !string.IsNullOrEmpty(name))
                {
                    var key = (name ?? "").Trim();
                    var keyL = key.ToLowerInvariant();
                    if (keyL == "h" || keyL == "b" || keyL == "tw" || keyL == "tf")
                    {
                        try
                        {
                            double raw = p.AsDouble();
                            double mm = UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.Millimeters);
                            v = Math.Round(mm, 3);
                            if (string.IsNullOrEmpty(shown)) shown = mm.ToString("0.###") + " mm";
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, name ?? "", null, null, ex.Message);
            }
            return (true, name ?? "", v, shown, null);
        }

        private static (bool ok, string name, object value, string display, string err) TryGetParamNormalized(Element e, KeySpec k)
        {
            Parameter p = null;
            string name = k.Name;
            if (k.BuiltInId.HasValue)
            {
                try { p = e.get_Parameter((BuiltInParameter)k.BuiltInId.Value); name = name ?? ((BuiltInParameter)k.BuiltInId.Value).ToString(); }
                catch { }
            }
            if (p == null && !string.IsNullOrWhiteSpace(k.Guid))
            {
                try { p = e.get_Parameter(new Guid(k.Guid)); name = name ?? k.Guid; }
                catch { }
            }
            if (p == null && !string.IsNullOrWhiteSpace(k.Name))
            {
                try { p = e.LookupParameter(k.Name); name = k.Name; }
                catch { }
            }
            if (p == null)
                return (false, name ?? "", null, null, $"param_not_found:{name}");

            object v = null;
            string shown = null;
            try
            {
                // Prefer UnitHelper for SI-normalized value + display
                try
                {
                    var info = UnitHelper.ParamToSiInfo(p);
                    var jo = info as JObject ?? JObject.FromObject(info);
                    var valTok = jo["value"];
                    if (valTok != null && valTok.Type != JTokenType.Null)
                        v = valTok.ToObject<object>();
                    var dispTok = jo["display"];
                    if (dispTok != null && dispTok.Type != JTokenType.Null)
                        shown = dispTok.ToString();
                }
                catch
                {
                    // fall back below
                }

                // Fallback for value if UnitHelper did not populate it
                if (v == null)
                {
                    switch (p.StorageType)
                    {
                        case StorageType.Double:
                            v = p.AsDouble(); break;
                        case StorageType.Integer:
                            v = p.AsInteger(); break;
                        case StorageType.String:
                            v = p.AsString(); break;
                        case StorageType.ElementId:
                            v = p.AsElementId()?.IntValue(); break;
                        default:
                            v = p.AsValueString() ?? p.ToString(); break;
                    }
                }

                // Fill display if still empty
                if (string.IsNullOrEmpty(shown))
                {
                    try
                    {
                        shown = p.AsValueString() ?? p.AsString();
                    }
                    catch
                    {
                        shown = null;
                    }

                    if (string.IsNullOrEmpty(shown) && v != null)
                    {
                        try { shown = v.ToString(); } catch { shown = null; }
                    }
                }

                // Heuristic: common structural section keys lacking Spec – treat as length (mm)
                if (p.StorageType == StorageType.Double && (string.IsNullOrEmpty(shown) || v is double) && !string.IsNullOrEmpty(name))
                {
                    var key = (name ?? "").Trim();
                    var keyL = key.ToLowerInvariant();
                    if (keyL == "h" || keyL == "b" || keyL == "tw" || keyL == "tf")
                    {
                        try
                        {
                            double raw = p.AsDouble();
                            double mm = UnitUtils.ConvertFromInternalUnits(raw, UnitTypeId.Millimeters);
                            v = Math.Round(mm, 3);
                            if (string.IsNullOrEmpty(shown)) shown = mm.ToString("0.###") + " mm";
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, name ?? "", null, null, ex.Message);
            }
            return (true, name ?? "", v, shown, null);
        }
    }
}


