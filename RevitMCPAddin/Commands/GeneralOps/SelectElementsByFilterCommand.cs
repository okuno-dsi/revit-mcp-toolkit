// ================================================================
// File: Commands/GeneralOps/SelectElementsByFilterByIdCommand.cs
// 機能 : すべて ID 指定のみで要素を検索し、UI の選択に反映
// 依存 : Revit 2023+ / .NET Framework 4.8
// 仕様 : 文字列名は一切受け付けない（日本語ローカライズ差異を完全回避）
// 入力 : viewId?                → ビュー内に限定（省略可）
//        builtInCategoryIds?[]  → BuiltInCategory の int 値（省略可）
//        levelId?               → レベルID一致で絞り込み（省略可）
//        paramFilters?[]        → { scope, parameterId|builtinId, op, value, units? }
//           scope: "instance"|"type"|"auto"(既定)
//           op:    "eq"|"neq"|"gt"|"gte"|"lt"|"lte"|"in"
//           units: "length"|"area"|"volume"|"angle"|"raw"（Double 比較時）
//        logic: "all"(AND) | "any"(OR)（既定: "all"）
//        selectionMode: "replace"(既定) | "add" | "remove"
//        dryRun: true で選択を変更せず結果のみ返却
//        maxCount: 既定 5000
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.GeneralOps
{
    public class SelectElementsByFilterByIdCommand : IRevitCommandHandler
    {
        public string CommandName => "select_elements_by_filter_id";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            // ---------------- ベースの Collector ----------------
            FilteredElementCollector col;
            if (p.TryGetValue("viewId", out var vtok))
            {
                var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(vtok.Value<int>())) as View;
                if (view == null) return new { ok = false, msg = $"View not found: {vtok}" };
                col = new FilteredElementCollector(doc, view.Id);
            }
            else
            {
                col = new FilteredElementCollector(doc);
            }
            col = col.WhereElementIsNotElementType();

            // ---------------- カテゴリ絞り込み（BuiltInCategory の int 群のみ） ----------------
            if (p["builtInCategoryIds"] is JArray bicArr && bicArr.Count > 0)
            {
                var bics = new List<BuiltInCategory>();
                foreach (var t in bicArr) bics.Add((BuiltInCategory)t.Value<int>());
                var catFilter = new ElementMulticategoryFilter(bics);
                col = col.WherePasses(catFilter);
            }

            // ---------------- Level 絞り込み（ID のみ） ----------------
            int levelId = p.Value<int?>("levelId") ?? 0;

            // LevelId を持たない型もあるため、ここは IEnumerable で安全に判定
            IEnumerable<Element> elems = col.ToElements().Where(e =>
            {
                if (levelId <= 0) return true;

                // 1) public Property LevelId があれば見る（FamilyInstance 等）
                try
                {
                    var prop = e.GetType().GetProperty("LevelId");
                    if (prop != null)
                    {
                        var eid = prop.GetValue(e) as ElementId;
                        if (eid != null && eid.IntValue() == levelId) return true;
                    }
                }
                catch { /* ignore */ }

                // 2) "Level" BIP を見る（持っていれば ElementId）
                try
                {
                    var pLevel = e.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                    if (pLevel != null && pLevel.StorageType == StorageType.ElementId)
                        return (pLevel.AsElementId()?.IntValue() ?? 0) == levelId;
                }
                catch { /* ignore */ }

                return false;
            });

            // ---------------- パラメータフィルタ（IDのみ） ----------------
            var pf = (p["paramFilters"] as JArray)?.OfType<JObject>()?.ToList() ?? new List<JObject>();
            bool useAny = string.Equals(p.Value<string>("logic") ?? "all", "any", StringComparison.OrdinalIgnoreCase);

            if (pf.Count > 0)
            {
                elems = elems.Where(e =>
                {
                    bool passAll = true;
                    bool passAny = false;

                    foreach (var f in pf)
                    {
                        bool pass = TestParamFilterById(doc, e, f);
                        passAll &= pass;
                        passAny |= pass;
                        if (!useAny && !passAll) break;
                    }

                    return useAny ? passAny : passAll;
                });
            }

            // ---------------- 収集 + 上限 ----------------
            int maxCount = Math.Max(1, p.Value<int?>("maxCount") ?? 5000);
            var matches = new List<ElementId>(maxCount);
            foreach (var e in elems)
            {
                matches.Add(e.Id);
                if (matches.Count >= maxCount) break;
            }

            // ---------------- 選択適用 ----------------
            bool dryRun = p.Value<bool?>("dryRun") ?? false;
            string selectionMode = (p.Value<string>("selectionMode") ?? "replace").ToLowerInvariant();

            int applied = 0;
            if (!dryRun)
            {
                var current = uidoc!.Selection.GetElementIds() ?? new List<ElementId>();
                var cur = new HashSet<int>(current.Select(x => x.IntValue()));

                if (selectionMode == "add")
                {
                    foreach (var id in matches) cur.Add(id.IntValue());
                    var next = cur.Select(i => Autodesk.Revit.DB.ElementIdCompat.From(i)).ToList();
                    uidoc.Selection.SetElementIds(next);
                    applied = next.Count;
                }
                else if (selectionMode == "remove")
                {
                    foreach (var id in matches) cur.Remove(id.IntValue());
                    var next = cur.Select(i => Autodesk.Revit.DB.ElementIdCompat.From(i)).ToList();
                    uidoc.Selection.SetElementIds(next);
                    applied = next.Count;
                }
                else // replace
                {
                    uidoc.Selection.SetElementIds(matches);
                    applied = matches.Count;
                }
            }

            return new
            {
                ok = true,
                totalMatched = matches.Count,
                appliedSelectionCount = dryRun ? (int?)null : applied,
                selectionMode = dryRun ? null : selectionMode,
                limited = (matches.Count >= maxCount),
                elementIds = matches.Select(x => x.IntValue()).ToList()
            };
        }

        // ---------------- helper: parameter filter (ID ベースのみ) ----------------
        private static bool TestParamFilterById(Document doc, Element e, JObject f)
        {
            string scope = (f.Value<string>("scope") ?? "auto").ToLowerInvariant();

            Element inst = e;
            Element type = null;
            try { type = doc.GetElement(e.GetTypeId()); } catch { }

            IEnumerable<Element> targets = scope switch
            {
                "instance" => new[] { inst },
                "type" => type != null ? new[] { type } : Array.Empty<Element>(),
                _ => type != null ? new[] { inst, type } : new[] { inst }
            };

            // 必須: parameterId または builtinId
            int pId = f.Value<int?>("parameterId") ?? 0;
            int builtinId = f.Value<int?>("builtinId") ?? 0;
            if (pId == 0 && builtinId == 0) return false;

            string op = (f.Value<string>("op") ?? "eq").ToLowerInvariant();
            JToken valTok = f["value"];

            // Double の単位ヒント
            string units = (f.Value<string>("units") ?? "raw").ToLowerInvariant();

            foreach (var tgt in targets)
            {
                Parameter p = null;
                if (pId != 0)
                {
                    try { p = tgt.Parameters?.Cast<Parameter>()?.FirstOrDefault(x => x.Id.IntValue() == pId); } catch { }
                }
                else
                {
                    try { p = tgt.get_Parameter((BuiltInParameter)builtinId); } catch { }
                }
                if (p == null) continue;

                try
                {
                    switch (p.StorageType)
                    {
                        case StorageType.String:
                            if (CompareString(p.AsString() ?? p.AsValueString() ?? string.Empty, op, valTok)) return true;
                            break;

                        case StorageType.Integer:
                            if (CompareNumber(p.AsInteger(), op, valTok)) return true;
                            break;

                        case StorageType.ElementId:
                            if (CompareNumber(p.AsElementId()?.IntValue() ?? 0, op, valTok)) return true;
                            break;

                        case StorageType.Double:
                            {
                                double lhs = p.AsDouble(); // 内部単位
                                double rhs = ToInternalFromUser(valTok, units);
                                if (CompareDouble(lhs, op, rhs)) return true;
                                break;
                            }
                    }
                }
                catch { /* 次の target */ }
            }

            return false;
        }

        private static bool CompareString(string s, string op, JToken val)
        {
            string rhs = val?.ToString() ?? string.Empty;
            return op switch
            {
                "eq" => string.Equals(s, rhs, StringComparison.Ordinal),
                "neq" => !string.Equals(s, rhs, StringComparison.Ordinal),
                "in" => (val is JArray arr) && arr.Any(t => string.Equals(t.ToString(), s, StringComparison.Ordinal)),
                // 文字列は部分一致などを禁止（ローカライズで不安定になるため）
                _ => false
            };
        }

        private static bool CompareNumber(long v, string op, JToken val)
        {
            if (val == null) return false;
            long rhs = (val.Type == JTokenType.Integer || val.Type == JTokenType.Float)
                        ? (long)val.Value<double>()
                        : (long.TryParse(val.ToString(), out var parsed) ? parsed : long.MinValue);

            return op switch
            {
                "eq" => v == rhs,
                "neq" => v != rhs,
                "gt" => v > rhs,
                "gte" => v >= rhs,
                "lt" => v < rhs,
                "lte" => v <= rhs,
                "in" => (val is JArray arr) && arr.Any(t => (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) && (long)t.Value<double>() == v),
                _ => false
            };
        }

        private static bool CompareDouble(double lhs, string op, double rhs)
        {
            const double eps = 1e-9;
            return op switch
            {
                "eq" => Math.Abs(lhs - rhs) <= eps,
                "neq" => Math.Abs(lhs - rhs) > eps,
                "gt" => lhs > rhs + eps,
                "gte" => lhs > rhs - eps,
                "lt" => lhs < rhs - eps,
                "lte" => lhs < rhs + eps,
                _ => false
            };
        }

        // ユーザ値（mm/m2/m3/deg/raw）→ 内部（ft/ft2/ft3/rad）
        private static double ToInternalFromUser(JToken val, string units)
        {
            if (val == null) return double.NaN;
            double v;
            if (val.Type == JTokenType.Integer || val.Type == JTokenType.Float) v = val.Value<double>();
            else if (!double.TryParse(val.ToString(), out v)) return double.NaN;

            try
            {
                switch (units)
                {
                    case "length": return ConvertToInternalUnits(v, UnitTypeId.Millimeters);
                    case "area": return ConvertToInternalUnits(v, UnitTypeId.SquareMillimeters);
                    case "volume": return ConvertToInternalUnits(v, UnitTypeId.CubicMillimeters);
                    case "angle": return v * (Math.PI / 180.0);
                    default: return v; // raw
                }
            }
            catch { return v; }
        }
    }
}


