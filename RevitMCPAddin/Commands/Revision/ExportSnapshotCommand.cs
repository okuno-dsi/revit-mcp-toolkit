// ================================================================
// File: Commands/Revision/ExportSnapshotCommand.cs  (snapshot-only, robust)
// Target : .NET Framework 4.8 / Revit 2023+
// Purpose:
//   - "export_snapshot" : スナップショット出力専用コマンド
//   - baseline/compare 機能は一切ありません（DiffReportCommand は廃止）
//   - includeAllTypeParams=true の場合はタイプ集約モードで types[] を返却
//   - 通常は要素スナップショットを items[] に返却
//
// Params (JSON):
// {
//   "scope": { "viewId": 0|int, "onlyElementsInView": false|true },
//   "filters": {
//     "includeCategoryIds": [int,...],      // BuiltInCategory(int) 推奨
//     "excludeCategoryIds": [int,...],
//     "includeCategories": ["Walls","Doors"]// 名前(部分一致, 大文字小文字無視)
//   },
//   "rules": {
//     "paramIds":    [int,...],             // 軽量ダンプ: id 指定のみ
//     "builtinIds":  [int,...],             // BuiltInParameter(int)
//     "includeAllInstanceParams": false|true,
//     "includeAllTypeParams":     false|true
//   },
//   "output": {
//     "includeProjectInfo": true,
//     "preferTypeSectionWhenTypeParams": true,   // 既定 true: types[] を優先
//     "path": "C:\\tmp\\snapshot.json"           // 保存先（省略可）
//   }
// }
//
// Result (JSON):
// {
//   "ok": true,
//   "project": { ... } | null,
//   "policy":  { includeAllInstanceParams, includeAllTypeParams, paramIds[], builtinIds[] },
//   "summary": { "items": n } | { "types": n, "elementsReferenced": m },
//   "items": [ ... ] | null,
//   "types": [ ... ] | null,
//   "path": "..." | null
// }
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.Revision
{
    public class ExportSnapshotCommand : IRevitCommandHandler
    {
        public string CommandName => "export_snapshot";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            // ---------- scope / filters / rules / output ----------
            var scope = p["scope"] as JObject ?? new JObject();
            var filters = p["filters"] as JObject ?? new JObject();
            var rules = p["rules"] as JObject ?? new JObject();
            var output = p["output"] as JObject ?? new JObject();

            int viewId = scope.Value<int?>("viewId") ?? 0;
            bool onlyInView = scope.Value<bool?>("onlyElementsInView") ?? false;

            var includeCatIds = (filters["includeCategoryIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();
            var excludeCatIds = (filters["excludeCategoryIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();
            var includeCatNames = (filters["includeCategories"] as JArray)?.Values<string>()?
                                  .Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();

            var ruleParamIds = (rules["paramIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();
            var ruleBuiltinIds = (rules["builtinIds"] as JArray)?.Values<int>()?.ToHashSet() ?? new HashSet<int>();
            bool capAllInst = rules.Value<bool?>("includeAllInstanceParams") ?? false;
            bool capAllType = rules.Value<bool?>("includeAllTypeParams") ?? false;

            bool includeProjectInfo = output.Value<bool?>("includeProjectInfo") ?? true;
            bool preferTypeSection = output.Value<bool?>("preferTypeSectionWhenTypeParams") ?? true;
            string path = output.Value<string>("path");

            // ---------- category resolution ----------
            var incBic = new HashSet<BuiltInCategory>();
            var excBic = new HashSet<BuiltInCategory>();
            ResolveCategories(doc, includeCatIds, includeCatNames, incBic);
            ResolveCategories(doc, excludeCatIds, new List<string>(), excBic);

            // ---------- collect & snapshot ----------
            var elems = CollectElements(doc, viewId, onlyInView, incBic, excBic);
            var snap = MakeSnapshot(doc, elems, ruleParamIds, ruleBuiltinIds, viewId, onlyInView, capAllInst, capAllType);

            // ---------- type-aggregation mode ----------
            JArray items = null;
            JArray types = null;
            if (capAllType && preferTypeSection)
            {
                types = BuildTypeSection(doc, snap.items);
            }
            else
            {
                items = new JArray(snap.items.Select(i => JObject.FromObject(i)));
            }

            // ---------- summary ----------
            var summary = new JObject();
            if (types != null)
            {
                summary["types"] = types.Count();
                summary["elementsReferenced"] = snap.items.Count; // 集約前の要素件数
            }
            else
            {
                summary["items"] = snap.items.Count;
            }

            // ---------- project info ----------
            JObject project = null;
            if (includeProjectInfo)
            {
                project = new JObject
                {
                    ["projectName"] = SafeGetProjectString(doc, BuiltInParameter.PROJECT_NAME),
                    ["projectNumber"] = SafeGetProjectString(doc, BuiltInParameter.PROJECT_NUMBER),
                    ["issueDate"] = SafeGetProjectString(doc, BuiltInParameter.PROJECT_ISSUE_DATE),
                    ["address"] = SafeGetProjectString(doc, BuiltInParameter.PROJECT_ADDRESS)
                };
            }

            // ---------- meta (createdAt + document) ----------
            var createdAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK");
            var documentMeta = new JObject
            {
                ["title"] = doc.Title ?? string.Empty,
                ["path"] = doc.PathName ?? string.Empty,
                ["revitVersion"] = uiapp?.Application?.VersionName ?? string.Empty,
                ["revitBuild"] = uiapp?.Application?.VersionBuild ?? string.Empty
            };
                        // ---------- policy record ----------
            var policy = new JObject
            {
                ["includeAllInstanceParams"] = capAllInst,
                ["includeAllTypeParams"] = capAllType,
                ["paramIds"] = new JArray(ruleParamIds),
                ["builtinIds"] = new JArray(ruleBuiltinIds)
            };

            // ---------- save to file (optional) ----------
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var fileObj = new JObject
                    {
                        ["ok"] = true,
                        ["project"] = project,
                        ["policy"] = policy,
                        ["summary"] = summary,
                        ["items"] = items,
                        ["types"] = types
                    };
                    fileObj["createdAt"] = createdAt;
                    fileObj["document"] = documentMeta;
                }
                catch (Exception ex)
                {
                    return new { ok = false, msg = $"ファイル保存に失敗しました: {ex.Message}" };
                }
            }

            return new
            {
                ok = true,
                createdAt,
                document = documentMeta,
                project,
                policy,
                summary,
                items,
                types,
                path = string.IsNullOrWhiteSpace(path) ? null : path
            };
        }

        // ======== Snapshot DTO ========
        private class ParamDto
        {
            public int id { get; set; }
            public string name { get; set; } = "";
            public string storageType { get; set; } = "";
            public bool isReadOnly { get; set; }
            public string dataType { get; set; } = null; // SpecTypeId.TypeId
            public string unit { get; set; } = null;     // "mm","deg","mm2","mm3" etc.
            public object value { get; set; }            // normalized value for compare
            public string display { get; set; } = null;  // AsValueString()
        }

        private class SnapItem
        {
            public int elementId;
            public string uniqueId = "";
            public int categoryId;
            public int? levelId; public string levelName = "";
            public int? typeId; public string typeName = ""; public string familyName = "";
            public double cxMm, cyMm, czMm;
            public double? yawDeg;
            public double? lengthMm;

            // 軽量: id 指定分
            public Dictionary<int, object> paramById = new Dictionary<int, object>();
            public Dictionary<int, object> builtinById = new Dictionary<int, object>();

            // フルダンプ
            public List<ParamDto> instParams { get; set; } = null;
            public List<ParamDto> typeParams { get; set; } = null;

            public string bucket = "";
        }

        private class Snapshot
        {
            public string title = "";
            public int? viewId;
            public bool onlyInView;
            public List<SnapItem> items = new List<SnapItem>();
        }

        // ======== Core ========
        private Snapshot MakeSnapshot(
            Document doc, IEnumerable<Element> elems,
            HashSet<int> paramIds, HashSet<int> builtinIds,
            int viewId, bool onlyInView,
            bool captureAllInst, bool captureAllType)
        {
            var snap = new Snapshot { title = doc.Title, viewId = (viewId > 0 ? viewId : (int?)null), onlyInView = onlyInView };

            foreach (var e in elems)
            {
                var it = new SnapItem
                {
                    elementId = e.Id.IntegerValue,
                    uniqueId = e.UniqueId ?? "",
                    categoryId = e.Category?.Id.IntegerValue ?? 0
                };

                // Level
                try
                {
                    ElementId lvlId = ElementId.InvalidElementId;
                    try { lvlId = (e as dynamic).LevelId; } catch { }
                    if (lvlId != ElementId.InvalidElementId)
                    {
                        var lvl = doc.GetElement(lvlId) as Level;
                        if (lvl != null) { it.levelId = lvl.Id.IntegerValue; it.levelName = lvl.Name ?? ""; }
                    }
                }
                catch { }

                // Type / Family
                ElementType et = null;
                try
                {
                    et = doc.GetElement(e.GetTypeId()) as ElementType;
                    it.typeId = et?.Id.IntegerValue; it.typeName = et?.Name ?? ""; it.familyName = et?.FamilyName ?? "";
                }
                catch { }

                // 幾何サマリ
                var c = GetCentroid(e);
                it.cxMm = FtToMm(c.X); it.cyMm = FtToMm(c.Y); it.czMm = FtToMm(c.Z);
                it.yawDeg = GetYawDeg(e);
                it.lengthMm = GetLengthMm(e);
                it.bucket = BuildBucketForSnap(it);

                // 軽量: id 指定分のみ
                foreach (var pid in paramIds)
                {
                    var pr = FindAnyParameterById(e, pid);
                    if (pr != null) it.paramById[pid] = AsComparable(pr);
                }
                foreach (var bid in builtinIds)
                {
                    var pr = e.get_Parameter((BuiltInParameter)bid);
                    if (pr != null) it.builtinById[bid] = AsComparable(pr);
                }
                if (et != null)
                {
                    foreach (var pid in paramIds)
                    {
                        var pr = FindAnyParameterById(et, pid);
                        if (pr != null) it.paramById[pid] = AsComparable(pr);
                    }
                    foreach (var bid in builtinIds)
                    {
                        var pr = et.get_Parameter((BuiltInParameter)bid);
                        if (pr != null) it.builtinById[bid] = AsComparable(pr);
                    }
                }

                // フルダンプ
                if (captureAllInst)
                {
                    var list = new List<ParamDto>();
                    try
                    {
                        foreach (Parameter pa in e.Parameters)
                        {
                            var dto = MapParam(pa);
                            if (dto != null) list.Add(dto);
                        }
                    }
                    catch { }
                    if (list.Count > 0) it.instParams = list;
                }
                if (captureAllType && et != null)
                {
                    var list = new List<ParamDto>();
                    try
                    {
                        foreach (Parameter pa in et.Parameters)
                        {
                            var dto = MapParam(pa);
                            if (dto != null) list.Add(dto);
                        }
                    }
                    catch { }
                    if (list.Count > 0) it.typeParams = list;
                }

                snap.items.Add(it);
            }

            return snap;
        }

        private static string BuildBucketForSnap(SnapItem it)
        {
            const double QYaw = 5.0;   // deg
            const double QLen = 50.0;  // mm
            string pos = $"{Qmm(it.cxMm, QLen)},{Qmm(it.cyMm, QLen)},{Qmm(it.czMm, QLen)}";
            if (it.yawDeg.HasValue || it.lengthMm.HasValue)
            {
                double yawQ = it.yawDeg.HasValue ? Math.Round(it.yawDeg.Value / QYaw) * QYaw : 0;
                double lenQ = it.lengthMm.HasValue ? Math.Round(it.lengthMm.Value / QLen) * QLen : 0;
                return $"{pos}|yaw:{yawQ}|len:{lenQ}";
            }
            return $"{pos}|pt";

            static string Qmm(double v, double q) => (Math.Round(v / q) * q).ToString(CultureInfo.InvariantCulture);
        }

        // ======== Type aggregation ========
        private JArray BuildTypeSection(Document doc, List<SnapItem> items)
        {
            // typeId ごとにタイプ情報を集約（typeParams は先頭採用）
            var byType = new Dictionary<int, (int catId, string fam, string typ, List<int> eids, List<ParamDto> tparams)>();

            foreach (var it in items)
            {
                if (!it.typeId.HasValue) continue;
                int tid = it.typeId.Value;
                if (!byType.ContainsKey(tid))
                {
                    byType[tid] = (it.categoryId, it.familyName ?? "", it.typeName ?? "",
                                   new List<int> { it.elementId }, it.typeParams ?? new List<ParamDto>());
                }
                else
                {
                    var entry = byType[tid];
                    entry.eids.Add(it.elementId);
                    byType[tid] = entry; // 先頭の typeParams を維持
                }
            }

            var arr = new JArray();
            foreach (var kv in byType)
            {
                var tid = kv.Key;
                var v = kv.Value;
                var jo = new JObject
                {
                    ["typeId"] = tid,
                    ["typeName"] = v.typ,
                    ["familyName"] = v.fam,
                    ["categoryId"] = v.catId,
                    ["usedCount"] = v.eids.Count,
                    ["sampleElementIds"] = new JArray(v.eids.Take(10)),
                    ["typeParams"] = v.tparams != null ? JArray.FromObject(v.tparams) : null
                };
                arr.Add(jo);
            }
            return new JArray(arr.OrderBy(x => x.Value<int?>("typeId") ?? 0));
        }

        // ======== Collect / Resolve ========
        private IEnumerable<Element> CollectElements(Document doc, int viewId, bool onlyInView,
                                                     HashSet<BuiltInCategory> inc, HashSet<BuiltInCategory> exc)
        {
            FilteredElementCollector fc = (onlyInView && viewId > 0)
                ? new FilteredElementCollector(doc, new ElementId(viewId))
                : new FilteredElementCollector(doc);
            fc = fc.WhereElementIsNotElementType();

            if (inc != null && inc.Count > 0)
            {
                var bids = inc.Select(b => new ElementId((int)b)).ToList();
                fc = fc.WherePasses(new ElementMulticategoryFilter(bids));
            }

            return fc.ToElements()
                     .Where(e => e?.Category != null)
                     .Where(e => exc == null || !exc.Contains((BuiltInCategory)e.Category.Id.IntegerValue))
                     .ToList();
        }

        private static void ResolveCategories(Document doc, HashSet<int> idSet, List<string> names,
                                              HashSet<BuiltInCategory> outSet)
        {
            if (idSet.Count > 0)
            {
                foreach (var id in idSet)
                {
                    if (Enum.IsDefined(typeof(BuiltInCategory), id))
                        outSet.Add((BuiltInCategory)id);
                }
                return;
            }
            if (names.Count == 0) return;

            foreach (Category c in doc.Settings.Categories)
            {
                if (c == null) continue;
                var n = c.Name ?? "";
                foreach (var name in names)
                {
                    if (n.Equals(name, StringComparison.OrdinalIgnoreCase) || n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var bic = (BuiltInCategory)c.Id.IntegerValue;
                        outSet.Add(bic);
                        break;
                    }
                }
            }
        }

        // ======== Parameter mapping / helpers ========
        private static object AsComparable(Parameter p)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        {
                            ForgeTypeId fdt = null; try { fdt = p.Definition?.GetDataType(); } catch { }
                            if (fdt != null)
                            {
                                if (fdt.Equals(SpecTypeId.Length)) return Round3(ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters));
                                if (fdt.Equals(SpecTypeId.Area)) return Round3(ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.SquareMillimeters));
                                if (fdt.Equals(SpecTypeId.Volume)) return Round3(ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.CubicMillimeters));
                                if (fdt.Equals(SpecTypeId.Angle)) return Round3(p.AsDouble() * 180.0 / Math.PI);
                            }
                            return Round3(p.AsDouble());
                        }
                    case StorageType.Integer: return p.AsInteger();
                    case StorageType.String: return p.AsString() ?? string.Empty;
                    case StorageType.ElementId: return p.AsElementId()?.IntegerValue ?? 0;
                    default: return null;
                }
            }
            catch { return null; }
        }

        private static ParamDto MapParam(Parameter p)
        {
            if (p == null) return null;
            var dto = new ParamDto();
            try
            {
                dto.id = p.Id.IntegerValue;
                dto.name = p.Definition?.Name ?? "";
                dto.storageType = p.StorageType.ToString();
                dto.isReadOnly = p.IsReadOnly;
                try { dto.dataType = p.Definition?.GetDataType()?.TypeId; } catch { dto.dataType = null; }

                object val = null; string unit = null;
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        {
                            ForgeTypeId fdt = null; try { fdt = p.Definition?.GetDataType(); } catch { }
                            if (fdt != null)
                            {
                                if (fdt.Equals(SpecTypeId.Length)) { unit = "mm"; val = Round3(ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters)); }
                                else if (fdt.Equals(SpecTypeId.Area)) { unit = "mm2"; val = Round3(ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.SquareMillimeters)); }
                                else if (fdt.Equals(SpecTypeId.Volume)) { unit = "mm3"; val = Round3(ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.CubicMillimeters)); }
                                else if (fdt.Equals(SpecTypeId.Angle)) { unit = "deg"; val = Round3(p.AsDouble() * 180.0 / Math.PI); }
                                else { val = Round3(p.AsDouble()); }
                            }
                            else { val = Round3(p.AsDouble()); }
                            break;
                        }
                    case StorageType.Integer: val = p.AsInteger(); break;
                    case StorageType.String: val = p.AsString() ?? ""; break;
                    case StorageType.ElementId: val = p.AsElementId()?.IntegerValue ?? 0; break;
                }
                dto.unit = unit;
                dto.value = val;
                try { dto.display = p.AsValueString(); } catch { dto.display = null; }
            }
            catch { /* best-effort */ }
            return dto;
        }

        private static double FtToMm(double ft) => ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
        private static double Round3(double v) => Math.Round(v, 3);

        private static XYZ GetCentroid(Element e)
        {
            if (e?.Location is LocationCurve lc && lc.Curve != null) return lc.Curve.Evaluate(0.5, true);
            if (e?.Location is LocationPoint lp && lp.Point != null) return lp.Point;
            try { var bb = e?.get_BoundingBox(null); if (bb != null) return (bb.Min + bb.Max) * 0.5; } catch { }
            return XYZ.Zero;
        }
        private static double? GetYawDeg(Element e)
        {
            try
            {
                if (e?.Location is LocationCurve lc && lc.Curve != null)
                {
                    var a = lc.Curve.GetEndPoint(0); var b = lc.Curve.GetEndPoint(1);
                    return Math.Atan2(b.Y - a.Y, b.X - a.X) * 180.0 / Math.PI;
                }
            }
            catch { }
            return null;
        }
        private static double? GetLengthMm(Element e)
        {
            try
            {
                if (e?.Location is LocationCurve lc && lc.Curve != null)
                    return ConvertFromInternalUnits(lc.Curve.Length, UnitTypeId.Millimeters);
            }
            catch { }
            return null;
        }

        private static Parameter? FindAnyParameterById(Element e, int paramId)
        {
            if (e == null) return null;
            try { return e.Parameters.Cast<Parameter>().FirstOrDefault(pr => pr.Id.IntegerValue == paramId); }
            catch { return null; }
        }

        private static string SafeGetProjectString(Document doc, BuiltInParameter bip)
        {
            try
            {
                var pi = doc.ProjectInformation;
                var p = pi?.get_Parameter(bip);
                return p?.AsString() ?? p?.AsValueString() ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}
