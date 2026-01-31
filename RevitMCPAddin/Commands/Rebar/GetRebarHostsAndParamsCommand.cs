#nullable enable
// ================================================================
// Command: get_rebar_hosts_and_params
// Purpose: Collect host info and key parameters for multiple Rebar elements.
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
    [RpcCommand("get_rebar_hosts_and_params",
        Category = "Rebar",
        Kind = "read",
        Summary = "Get host information and selected parameters for Rebar elements.",
        ExampleJsonRpc = "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"get_rebar_hosts_and_params\", \"params\":{ \"elementIds\":[123,456], \"includeHost\":true, \"includeParameters\":[\"モデル鉄筋径\",\"鉄筋番号\",\"ホスト カテゴリ\"], \"includeTypeInfo\":true, \"includeErrors\":true } }"
    )]
    public sealed class GetRebarHostsAndParamsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_rebar_hosts_and_params";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。", "NO_DOC");

            var p = cmd.Params as JObject ?? new JObject();

            bool includeHost = p.Value<bool?>("includeHost") ?? true;
            bool includeTypeInfo = p.Value<bool?>("includeTypeInfo") ?? true;
            bool includeErrors = p.Value<bool?>("includeErrors") ?? true;

            var ids = CollectIds(p, "elementIds");
            if (ids.Count == 0)
            {
                return ResultUtil.Err("elementIds が空です。", "INVALID_ARGS");
            }

            var includeParams = new List<string>();
            try
            {
                var arr = p["includeParameters"] as JArray;
                if (arr != null)
                {
                    foreach (var t in arr)
                    {
                        var s = (t?.ToString() ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(s)) includeParams.Add(s);
                    }
                }
            }
            catch { /* ignore */ }

            if (includeParams.Count == 0)
            {
                includeParams.AddRange(new[] { "モデル鉄筋径", "鉄筋番号", "ホスト カテゴリ" });
            }

            var items = new JArray();
            var errors = new JArray();

            foreach (var id in ids)
            {
                try
                {
                    var elem = doc.GetElement(new ElementId(id));
                    if (elem == null)
                    {
                        if (includeErrors)
                        {
                            errors.Add(new JObject
                            {
                                ["elementId"] = id,
                                ["code"] = "NOT_FOUND",
                                ["msg"] = "Element not found."
                            });
                        }
                        continue;
                    }

                    if (!IsRebarLike(elem))
                    {
                        if (includeErrors)
                        {
                            errors.Add(new JObject
                            {
                                ["elementId"] = id,
                                ["code"] = "NOT_REBAR",
                                ["msg"] = "Element is not Rebar/RebarInSystem/AreaReinforcement/PathReinforcement."
                            });
                        }
                        continue;
                    }

                    var item = new JObject
                    {
                        ["elementId"] = id,
                        ["uniqueId"] = elem.UniqueId ?? ""
                    };

                    if (includeTypeInfo)
                    {
                        try
                        {
                            var tid = elem.GetTypeId();
                            if (tid != null && tid != ElementId.InvalidElementId)
                            {
                                var typeElem = doc.GetElement(tid) as ElementType;
                                if (typeElem != null)
                                {
                                    item["typeId"] = tid.IntValue();
                                    item["typeName"] = typeElem.Name ?? "";
                                    item["familyName"] = typeElem.FamilyName ?? "";
                                }
                            }
                        }
                        catch { /* ignore */ }
                    }

                    if (includeHost)
                    {
                        var hostId = TryGetRebarHostId(elem);
                        if (hostId != null && hostId != ElementId.InvalidElementId)
                        {
                            item["hostId"] = hostId.IntValue();
                            try
                            {
                                var host = doc.GetElement(hostId);
                                if (host != null && host.Category != null)
                                {
                                    item["hostCategory"] = host.Category.Name ?? "";
                                }
                            }
                            catch { /* ignore */ }
                        }
                    }

                    if (includeParams.Count > 0)
                    {
                        var paramObj = new JObject();
                        var missing = new JArray();
                        foreach (var name in includeParams)
                        {
                            var prm = GetParam(elem, name);
                            if (prm == null)
                            {
                                var typeElem = elem is Element e ? doc.GetElement(e.GetTypeId()) as ElementType : null;
                                prm = GetParam(typeElem, name);
                            }

                            if (prm == null)
                            {
                                missing.Add(name);
                                continue;
                            }

                            paramObj[name] = FormatParamValue(doc, prm);
                        }

                        item["parameters"] = paramObj;
                        if (missing.Count > 0) item["missingParameters"] = missing;
                    }

                    items.Add(item);
                }
                catch (Exception ex)
                {
                    if (includeErrors)
                    {
                        errors.Add(new JObject
                        {
                            ["elementId"] = id,
                            ["code"] = "EXCEPTION",
                            ["msg"] = ex.Message
                        });
                    }
                }
            }

            var result = new JObject
            {
                ["count"] = items.Count,
                ["items"] = items
            };

            if (includeErrors)
            {
                result["errors"] = errors;
            }

            return ResultUtil.Ok(result);
        }

        private static List<int> CollectIds(JObject p, string key)
        {
            var list = new List<int>();
            try
            {
                var arr = p[key] as JArray;
                if (arr != null)
                {
                    foreach (var t in arr)
                    {
                        if (t == null || t.Type != JTokenType.Integer) continue;
                        int v = t.Value<int>();
                        if (v > 0) list.Add(v);
                    }
                }
            }
            catch { /* ignore */ }
            return list.Distinct().ToList();
        }

        private static bool IsRebarLike(Element e)
        {
            if (e is Autodesk.Revit.DB.Structure.Rebar) return true;
            if (e is Autodesk.Revit.DB.Structure.RebarInSystem) return true;
            if (e is Autodesk.Revit.DB.Structure.AreaReinforcement) return true;
            if (e is Autodesk.Revit.DB.Structure.PathReinforcement) return true;

            try
            {
                var cat = e.Category;
                if (cat == null) return false;
                var cid = cat.Id != null ? cat.Id.IntValue() : -1;
                if (cid == (int)BuiltInCategory.OST_Rebar) return true;
                if (cid == (int)BuiltInCategory.OST_AreaRein) return true;
                if (cid == (int)BuiltInCategory.OST_PathRein) return true;
            }
            catch { /* ignore */ }
            return false;
        }

        private static ElementId TryGetRebarHostId(Element e)
        {
            try
            {
                var mi = e.GetType().GetMethod("GetHostId", Type.EmptyTypes);
                if (mi != null)
                {
                    var obj = mi.Invoke(e, null);
                    if (obj is ElementId id) return id;
                }
            }
            catch { /* ignore */ }
            return ElementId.InvalidElementId;
        }

        private static Parameter? GetParam(Element? e, string name)
        {
            if (e == null) return null;
            if (string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                return e.LookupParameter(name);
            }
            catch { return null; }
        }

        private static string FormatParamValue(Document doc, Parameter prm)
        {
            try
            {
                var info = UnitHelper.ParamToSiInfo(prm) as JObject;
                var display = info?["display"]?.ToString();
                if (!string.IsNullOrWhiteSpace(display)) return display;
                var val = info?["value"]?.ToString();
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
            catch { /* ignore */ }

            try
            {
                if (prm.StorageType == StorageType.ElementId)
                {
                    var id = prm.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        var elem = doc.GetElement(id);
                        if (elem != null) return elem.Name ?? "";
                        var cat = Category.GetCategory(doc, id);
                        if (cat != null) return cat.Name ?? "";
                        return id.IntValue().ToString();
                    }
                }
            }
            catch { /* ignore */ }

            try
            {
                var s = prm.AsValueString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            catch { /* ignore */ }
            try
            {
                var s = prm.AsString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            catch { /* ignore */ }
            try
            {
                if (prm.StorageType == StorageType.Double) return prm.AsDouble().ToString("0.###");
                if (prm.StorageType == StorageType.Integer) return prm.AsInteger().ToString();
            }
            catch { /* ignore */ }

            return string.Empty;
        }
    }
}
