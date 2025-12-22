#nullable enable
// ================================================================
// File: Commands/ParamOps/GetParamValuesCommand.cs
// Purpose : JSON-RPC "get_param_values" をピンポイント高速実装
// Inputs  : mode("element"|"type"|"category"), elementId/typeId/category,
//           selector([{param,name/bip,equals}]), params([name|bip]), scope("auto"|"instance"|"type"),
//           includeMeta(bool)
// Output  : { ok, result:{ target{...}, scope, values:[{name,id,storage,isReadOnly,spec?,value,display,where}], missing:[...] } }
// Notes   : 単位変換は UnitHelper.ParamToSiInfo(Parameter) を使用（value=SI正規化, display=AsValueString）
// Error   : ResultUtil.Err("...") で親切に
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // UnitHelper, ResultUtil
using static System.StringComparison;

namespace RevitMCPAddin.Commands.ParamOps
{
    public class GetParamValuesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_param_values";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("アクティブドキュメントがありません。");

            var p = cmd.Params as JObject ?? new JObject();

            // -------- 入力取得 --------
            var mode = (p.Value<string>("mode") ?? "element").ToLowerInvariant();
            var scope = (p.Value<string>("scope") ?? "auto").ToLowerInvariant();  // "auto"|"instance"|"type"
            var includeMeta = p.Value<bool?>("includeMeta") ?? true;

            // ターゲット解決
            Element? instElem = null;
            Element? typeElem = null;

            try
            {
                switch (mode)
                {
                    case "element":
                        {
                            var eid = ToElementId(p["elementId"]);
                            if (eid == null) return ResultUtil.Err("mode=element には elementId が必要です");
                            instElem = doc.GetElement(eid);
                            if (instElem == null) return ResultUtil.Err($"要素が見つかりません: elementId={eid.IntValue()}");
                            typeElem = doc.GetElement(instElem.GetTypeId());
                            break;
                        }
                    case "type":
                        {
                            var tid = ToElementId(p["typeId"]);
                            if (tid == null) return ResultUtil.Err("mode=type には typeId が必要です");
                            typeElem = doc.GetElement(tid);
                            if (typeElem == null) return ResultUtil.Err($"タイプ要素が見つかりません: typeId={tid.IntValue()}");
                            break;
                        }
                    case "category":
                        {
                            var catTok = p["category"];
                            if (catTok == null) return ResultUtil.Err("mode=category には category が必要です（'OST_Rooms' など）");
                            var bic = TryParseBuiltInCategory(catTok);
                            if (bic == null) return ResultUtil.Err("category は 'OST_xxx' 文字列 または BuiltInCategory の int を指定してください。");

                            var col = new FilteredElementCollector(doc).WhereElementIsNotElementType().OfCategory((BuiltInCategory)bic);
                            col = ApplySelectors(doc, col, (p["selector"] as JArray)?.OfType<JObject>().ToList());
                            instElem = col.FirstOrDefault();
                            if (instElem == null) return ResultUtil.Err("selector に一致する要素が見つかりません。");
                            typeElem = doc.GetElement(instElem.GetTypeId());
                            break;
                        }
                    default:
                        return ResultUtil.Err($"未知の mode: {mode}");
                }
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"ターゲット解決中に例外: {ex.Message}");
            }

            // 取得したい param 群
            var reqParams = (p["params"] as JArray)?.ToList() ?? new List<JToken>();
            if (reqParams.Count == 0) return ResultUtil.Err("params が空です。");

            try
            {
                var results = new List<JObject>();
                var missing = new List<JToken>();

                foreach (var t in reqParams)
                {
                    var resolved = ResolveOne(instElem, typeElem, t, scope, includeMeta);
                    if (resolved == null || resolved.Value<bool?>("found") == false)
                        missing.Add(t);
                    else
                        results.Add(resolved);
                }

                var targetInfo = BuildTargetInfo(instElem ?? typeElem);

                var root = new JObject
                {
                    ["target"] = targetInfo,
                    ["scope"] = scope,
                    ["values"] = new JArray(results),
                    ["missing"] = new JArray(missing)
                };
                return ResultUtil.Ok(root);
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"取得処理中に例外: {ex.Message}");
            }
        }

        // ----------------- 内部ユーティリティ -----------------

        private static JObject? ResolveOne(Element? inst, Element? type, JToken want, string scope, bool includeMeta)
        {
            string? wantName = null;
            BuiltInParameter? wantBip = null;

            if (want?.Type == JTokenType.Integer)
            {
                var iv = want.Value<int>();
                if (Enum.IsDefined(typeof(BuiltInParameter), iv))
                    wantBip = (BuiltInParameter)iv;
            }
            else if (want?.Type == JTokenType.String)
            {
                wantName = want.Value<string>();
            }

            Parameter? prm = null;
            string where = "";

            switch (scope)
            {
                case "instance":
                    if (inst != null)
                    {
                        prm = GetParam(inst, wantName, wantBip);
                        where = "instance";
                    }
                    break;

                case "type":
                    if (type != null)
                    {
                        prm = GetParam(type, wantName, wantBip);
                        where = "type";
                    }
                    break;

                case "auto":
                default:
                    if (inst != null)
                    {
                        prm = GetParam(inst, wantName, wantBip);
                        where = "instance";
                    }
                    if (prm == null && type != null)
                    {
                        prm = GetParam(type, wantName, wantBip);
                        where = "type";
                    }
                    break;
            }

            if (prm == null) return new JObject { ["found"] = false };

            var dto = new JObject
            {
                ["found"] = true,
                ["where"] = where,
                ["name"] = SafeParamName(prm),
                ["id"] = ParamIdGuess(prm),
                ["storage"] = prm.StorageType.ToString(),
                ["isReadOnly"] = prm.IsReadOnly
            };

            if (includeMeta)
            {
                var spec = UnitHelper.GetSpec(prm);
                dto["spec"] = SpecToStr(spec);
            }

            // 値の詰め：UnitHelper で共通整形（SI + display）
            var info = UnitHelper.ParamToSiInfo(prm);
            if (info is JObject jo)
            {
                if (jo["value"] != null) dto["value"] = jo["value"];
                if (jo["display"] != null) dto["display"] = jo["display"];
            }
            else
            {
                // フォールバック
                switch (prm.StorageType)
                {
                    case StorageType.String: dto["value"] = prm.AsString() ?? ""; break;
                    case StorageType.Integer: dto["value"] = prm.AsInteger(); break;
                    case StorageType.ElementId: dto["value"] = prm.AsElementId()?.IntValue() ?? -1; break;
                    case StorageType.Double: dto["value"] = prm.AsDouble(); break; // 内部値
                    default: dto["value"] = null; break;
                }
                dto["display"] = prm.AsValueString() ?? "";
            }

            return dto;
        }

        private static Parameter? GetParam(Element e, string? name, BuiltInParameter? bip)
        {
            Parameter? p = null;
            if (bip.HasValue)
            {
                try { p = e.get_Parameter(bip.Value); } catch { }
            }
            if (p == null && !string.IsNullOrEmpty(name))
                p = e.LookupParameter(name!);
            return p;
        }

        private static JObject BuildTargetInfo(Element? e)
        {
            if (e == null) return new JObject();
            string catName = e.Category?.Name ?? "";
            try
            {
                var bic = (BuiltInCategory)(e.Category?.Id?.IntValue() ?? 0);
                if (Enum.IsDefined(typeof(BuiltInCategory), bic))
                    catName = $"OST_{bic}";
            }
            catch { }

            return new JObject
            {
                ["id"] = e.Id.IntValue(),
                ["uniqueId"] = e.UniqueId ?? "",
                ["typeId"] = (e is ElementType) ? e.Id.IntValue() : e.GetTypeId()?.IntValue() ?? -1,
                ["category"] = catName,
                ["name"] = e.Name ?? ""
            };
        }

        private static string SpecToStr(ForgeTypeId? spec)
        {
            if (spec == null) return "";
            try
            {
                if (spec.Equals(SpecTypeId.Length)) return "Length";
                if (spec.Equals(SpecTypeId.Area)) return "Area";
                if (spec.Equals(SpecTypeId.Volume)) return "Volume";
                if (spec.Equals(SpecTypeId.Angle)) return "Angle";
            }
            catch { }
            return spec?.ToString() ?? "";
        }

        private static ElementId? ToElementId(JToken? t)
        {
            if (t == null) return null;
            if (t.Type == JTokenType.Integer) return Autodesk.Revit.DB.ElementIdCompat.From(t.Value<int>());
            if (t.Type == JTokenType.String && int.TryParse(t.Value<string>(), out var iv)) return Autodesk.Revit.DB.ElementIdCompat.From(iv);
            return null;
        }

        private static BuiltInCategory? TryParseBuiltInCategory(JToken tok)
        {
            try
            {
                if (tok.Type == JTokenType.String)
                {
                    var s = tok.Value<string>() ?? "";
                    if (s.StartsWith("OST_", OrdinalIgnoreCase))
                    {
                        if (Enum.TryParse<BuiltInCategory>(s, true, out var bicFull)) return bicFull;
                        if (Enum.TryParse<BuiltInCategory>(s.Substring(4), true, out var bicShort)) return bicShort;
                    }
                    else
                    {
                        if (Enum.TryParse<BuiltInCategory>(s, true, out var bicAny)) return bicAny;
                    }
                }
                else if (tok.Type == JTokenType.Integer)
                {
                    var iv = tok.Value<int>();
                    if (Enum.IsDefined(typeof(BuiltInCategory), iv))
                        return (BuiltInCategory)iv;
                }
            }
            catch { }
            return null;
        }

        private static FilteredElementCollector ApplySelectors(
            Document doc,
            FilteredElementCollector col,
            List<JObject>? selectors)
        {
            if (selectors == null || selectors.Count == 0) return col;

            IEnumerable<Element> q = col.ToElements();
            foreach (var sel in selectors)
            {
                var want = sel["param"];
                var equalsTok = sel["equals"];
                if (want == null || equalsTok == null) continue;

                string? wantName = null;
                BuiltInParameter? bip = null;

                if (want.Type == JTokenType.Integer)
                {
                    var iv = want.Value<int>();
                    if (Enum.IsDefined(typeof(BuiltInParameter), iv))
                        bip = (BuiltInParameter)iv;
                }
                else if (want.Type == JTokenType.String)
                {
                    wantName = want.Value<string>();
                }

                var wantStr = equalsTok.Value<string>() ?? "";

                q = q.Where(e =>
                {
                    var prm = (bip != null) ? e.get_Parameter(bip.Value)
                                            : (!string.IsNullOrEmpty(wantName) ? e.LookupParameter(wantName!) : null);
                    if (prm == null) return false;

                    var disp = prm.AsValueString() ?? prm.AsString() ?? "";
                    if (!string.IsNullOrEmpty(disp))
                        return string.Equals(disp, wantStr, OrdinalIgnoreCase);

                    switch (prm.StorageType)
                    {
                        case StorageType.Integer: return prm.AsInteger().ToString() == wantStr;
                        case StorageType.ElementId: return (prm.AsElementId()?.IntValue() ?? -1).ToString() == wantStr;
                        case StorageType.Double: return prm.AsDouble().ToString() == wantStr;
                        case StorageType.String: return (prm.AsString() ?? "") == wantStr;
                    }
                    return false;
                });
            }

            // Collectorに戻す意味は薄いので、ここではメモリ列をもとに再構築
            var list = q.Take(8).ToList();
            return new FilteredElementCollector(doc, list.Select(x => x.Id).ToList());
        }

        private static string SafeParamName(Parameter p)
        {
            try { return p.Definition?.Name ?? ""; } catch { return ""; }
        }

        private static int ParamIdGuess(Parameter p)
        {
            try { return p.Id?.IntValue() ?? 0; } catch { return 0; }
        }
    }
}


