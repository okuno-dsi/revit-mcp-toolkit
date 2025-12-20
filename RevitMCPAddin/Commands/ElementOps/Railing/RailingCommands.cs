// ================================================================
// File: Commands/ElementOps/Railing/RailingCommands.cs  (UnitHelper対応・Architecture.Railing版)
// Target : .NET Framework 4.8 / Revit 2023 / C# 8
// Policy : 入力=mm/deg、内部=ft/rad（UnitHelperで統一）
// Notes  : elementId / uniqueId 両対応、namesOnly/paging/filters
// Depends: Autodesk.Revit.DB, Autodesk.Revit.DB.Architecture, Autodesk.Revit.UI,
//          Newtonsoft.Json.Linq, RevitMCPAddin.Core (UnitHelper)
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture; // Railing / RailingType
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

// 型あいまいさ回避のためエイリアスを用意（明示的に Architecture を使う）
using ArchRailing = Autodesk.Revit.DB.Architecture.Railing;
using ArchRailingType = Autodesk.Revit.DB.Architecture.RailingType;

namespace RevitMCPAddin.Commands.ElementOps.Railing
{
    // ------------------------------------------------------------
    // 共通ユーティリティ（UnitHelper経由で mm/deg ⇔ ft/rad を統一）
    // ------------------------------------------------------------
    internal static class RailingUnits
    {
        public static object InputUnits() => new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" };
        public static object InternalUnits() => new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" };

        // 内部(ft/rad) → 表示(mm/deg) への変換（SpecTypeIdに基づく）
        public static object ConvertDoubleBySpec(double rawInternal, ForgeTypeId fdt)
        {
            try
            {
                if (fdt != null)
                {
                    if (fdt.Equals(SpecTypeId.Length)) return Math.Round(UnitHelper.FtToMm(rawInternal), 3);
                    if (fdt.Equals(SpecTypeId.Area)) return Math.Round(UnitHelper.Ft2ToMm2(rawInternal), 3);
                    if (fdt.Equals(SpecTypeId.Volume)) return Math.Round(UnitHelper.Ft3ToMm3(rawInternal), 3);
                    if (fdt.Equals(SpecTypeId.Angle)) return Math.Round(UnitHelper.RadToDeg(rawInternal), 3);
                }
            }
            catch { /* 失敗時はそのまま返す */ }
            return Math.Round(rawInternal, 3);
        }

        // 表示(mm/deg) → 内部(ft/rad) への変換（SpecTypeIdに基づく）
        public static double ToInternal(double userValue, ForgeTypeId fdt)
        {
            try
            {
                if (fdt != null)
                {
                    if (fdt.Equals(SpecTypeId.Length)) return UnitHelper.MmToFt(userValue);
                    if (fdt.Equals(SpecTypeId.Area)) return UnitHelper.Mm2ToFt2(userValue);
                    if (fdt.Equals(SpecTypeId.Volume)) return UnitHelper.Mm3ToFt3(userValue);
                    if (fdt.Equals(SpecTypeId.Angle)) return UnitHelper.DegToRad(userValue);
                }
            }
            catch { }
            return userValue; // 未知Specは素通し
        }

        // mm座標 -> XYZ(ft)
        public static XYZ Mm(double x, double y, double z) => new XYZ(
            UnitHelper.MmToFt(x),
            UnitHelper.MmToFt(y),
            UnitHelper.MmToFt(z));

        public static XYZ Mm(JToken pt) => new XYZ(
            UnitHelper.MmToFt(pt.Value<double>("x")),
            UnitHelper.MmToFt(pt.Value<double>("y")),
            UnitHelper.MmToFt(pt.Value<double>("z")));

        public static Element ResolveElement(Document doc, JObject p)
        {
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) return doc.GetElement(new ElementId(eid));
            if (!string.IsNullOrWhiteSpace(uid)) return doc.GetElement(uid);
            return null;
        }
    }

    // ------------------------------------------------------------
    // 1) create_railing（baseline: 2点以上の折れ線、mm入力）
    // ------------------------------------------------------------
    public class CreateRailingCommand : IRevitCommandHandler
    {
        public string CommandName => "create_railing";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var ptsTok = p["baseline"] as JArray;
            if (ptsTok == null || ptsTok.Count < 2)
                return new { ok = false, msg = "baseline must contain at least 2 points" };

            var typeId = new ElementId(p.Value<int>("railingTypeId"));
            var levelId = new ElementId(p.Value<int>("levelId"));

            var loop = new CurveLoop();
            for (int i = 0; i < ptsTok.Count - 1; i++)
            {
                var a = RailingUnits.Mm(ptsTok[i]);
                var b = RailingUnits.Mm(ptsTok[i + 1]);
                loop.Append(Line.CreateBound(a, b));
            }

            ArchRailing railing = null;
            using (var tx = new Transaction(doc, "Create Railing"))
            {
                tx.Start();
                try
                {
                    railing = ArchRailing.Create(doc, loop, typeId, levelId);
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"Failed to create railing: {ex.Message}" };
                }
            }

            return new
            {
                ok = true,
                elementId = railing.Id.IntegerValue,
                uniqueId = railing.UniqueId,
                typeId = railing.GetTypeId().IntegerValue,
                levelId = railing.LevelId.IntegerValue,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }
    }

    // ------------------------------------------------------------
    // 2) get_railings（filters / paging / namesOnly）
    // ------------------------------------------------------------
    public class GetRailingsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_railings";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            // 既存ページング（互換）
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // shape/paging + 軽量出力
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = System.Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = System.Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            bool includeBaseline = p.Value<bool?>("includeBaseline") ?? true;

            int filterTypeId = p.Value<int?>("typeId") ?? p.Value<int?>("railingTypeId") ?? 0;
            string filterTypeName = p.Value<string>("typeName");
            int filterLevelId = p.Value<int?>("levelId") ?? 0;
            string filterLevelName = p.Value<string>("levelName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(ArchRailing))
                .Cast<ArchRailing>()
                .ToList();

            // caches
            var typeIds = all.Select(r => r.GetTypeId().IntegerValue).Distinct().ToList();
            var typeMap = new Dictionary<int, ElementType>(typeIds.Count);
            foreach (var id in typeIds) typeMap[id] = doc.GetElement(new ElementId(id)) as ElementType;

            var levelIds = all.Select(r => r.LevelId.IntegerValue).Distinct().ToList();
            var levelMap = new Dictionary<int, Level>(levelIds.Count);
            foreach (var id in levelIds) levelMap[id] = doc.GetElement(new ElementId(id)) as Level;

            IEnumerable<ArchRailing> q = all;

            if (filterTypeId > 0)
                q = q.Where(r => r.GetTypeId().IntegerValue == filterTypeId);

            if (!string.IsNullOrWhiteSpace(filterTypeName))
                q = q.Where(r =>
                {
                    typeMap.TryGetValue(r.GetTypeId().IntegerValue, out var et);
                    return et != null && string.Equals(et.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase);
                });

            if (filterLevelId > 0)
                q = q.Where(r => r.LevelId.IntegerValue == filterLevelId);

            if (!string.IsNullOrWhiteSpace(filterLevelName))
                q = q.Where(r =>
                {
                    levelMap.TryGetValue(r.LevelId.IntegerValue, out var lv);
                    return lv != null && string.Equals(lv.Name ?? "", filterLevelName, StringComparison.OrdinalIgnoreCase);
                });

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(r =>
                {
                    var n = r.Name ?? "";
                    if (n.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    typeMap.TryGetValue(r.GetTypeId().IntegerValue, out var et);
                    return (et?.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                });

            // 安定ソート: typeName -> elementId
            var ordered = q
                .Select(r =>
                {
                    typeMap.TryGetValue(r.GetTypeId().IntegerValue, out var et);
                    return new { r, typeName = et?.Name ?? "", id = r.Id.IntegerValue };
                })
                .OrderBy(x => x.typeName).ThenBy(x => x.id)
                .Select(x => x.r)
                .ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount, inputUnits = RailingUnits.InputUnits(), internalUnits = RailingUnits.InternalUnits() };

            if (namesOnly)
            {
                // すべて string を返すように統一（delegate戻り値エラー回避）
                var names = ordered
                    .Skip(skip).Take(limit)
                    .Select(r => !string.IsNullOrEmpty(r.Name)
                        ? r.Name
                        : (typeMap.TryGetValue(r.GetTypeId().IntegerValue, out var et) ? (et?.Name ?? "") : ""))
                    .ToList();

                return new { ok = true, totalCount, names, inputUnits = RailingUnits.InputUnits(), internalUnits = RailingUnits.InternalUnits() };
            }

            // ページング + 軽量 idsOnly
            IEnumerable<ArchRailing> paged = ordered;
            if (skip > 0) paged = paged.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit);

            if (idsOnly)
            {
                var ids = paged.Select(r => r.Id.IntegerValue).ToList();
                return new { ok = true, totalCount, elementIds = ids, inputUnits = RailingUnits.InputUnits(), internalUnits = RailingUnits.InternalUnits() };
            }

            var items = paged.Select(r =>
            {
                // baseline (直線の場合は mm で返す)
                object baseline = null;
                if (includeBaseline)
                {
                    var lc = r.Location as LocationCurve;
                    if (lc?.Curve != null)
                    {
                        var p0 = lc.Curve.GetEndPoint(0);
                        var p1 = lc.Curve.GetEndPoint(1);
                        baseline = new[]
                        {
                            new { x = UnitHelper.FtToMm(p0.X), y = UnitHelper.FtToMm(p0.Y), z = UnitHelper.FtToMm(p0.Z) },
                            new { x = UnitHelper.FtToMm(p1.X), y = UnitHelper.FtToMm(p1.Y), z = UnitHelper.FtToMm(p1.Z) }
                        };
                    }
                }

                typeMap.TryGetValue(r.GetTypeId().IntegerValue, out var et);
                levelMap.TryGetValue(r.LevelId.IntegerValue, out var lv);

                return new
                {
                    elementId = r.Id.IntegerValue,
                    uniqueId = r.UniqueId,
                    typeId = r.GetTypeId().IntegerValue,
                    typeName = et?.Name ?? "",
                    levelId = r.LevelId.IntegerValue,
                    levelName = lv?.Name ?? "",
                    baseline
                };
            }).ToList();

            return new
            {
                ok = true,
                totalCount,
                railings = items,
                inputUnits = RailingUnits.InputUnits(),
                internalUnits = RailingUnits.InternalUnits()
            };
        }
    }

    // ------------------------------------------------------------
    // 3) delete_railing（elementId/uniqueId）
    // ------------------------------------------------------------
    public class DeleteRailingCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_railing";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var el = RailingUnits.ResolveElement(doc, p);
            if (el == null) return new { ok = false, msg = "Railing が見つかりません（elementId/uniqueId）。" };

            ICollection<ElementId> deleted = null;
            using (var tx = new Transaction(doc, "Delete Railing"))
            {
                tx.Start();
                deleted = doc.Delete(el.Id);
                tx.Commit();
            }

            var ids = deleted != null ? deleted.Select(x => x.IntegerValue).ToList() : new List<int>();
            return new { ok = true, elementId = el.Id.IntegerValue, uniqueId = el.UniqueId, deletedCount = ids.Count, deletedElementIds = ids };
        }
    }

    // ------------------------------------------------------------
    // 4) get_railing_parameters（instance/type 兼用・Spec換算）
    // ------------------------------------------------------------
    public class GetRailingParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_railing_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            Element target = null;
            string scope;

            // 1) インスタンス優先で解決
            target = RailingUnits.ResolveElement(doc, p);
            if (target is ArchRailing) scope = "instance";
            else
            {
                // 2) タイプ解決
                target = null;
                int typeId = p.Value<int?>("typeId") ?? p.Value<int?>("railingTypeId") ?? 0;
                string typeName = p.Value<string>("typeName");

                if (typeId > 0) target = doc.GetElement(new ElementId(typeId));
                else if (!string.IsNullOrWhiteSpace(typeName))
                    target = new FilteredElementCollector(doc)
                        .OfClass(typeof(ArchRailingType)).Cast<ArchRailingType>()
                        .FirstOrDefault(t => string.Equals(t.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));

                if (target is ArchRailingType) scope = "type";
                else return new { ok = false, msg = "Railing（instance/type）が特定できません。" };
            }

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (target.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
            {
                return new
                {
                    ok = true,
                    scope,
                    elementId = scope == "instance" ? (int?)target.Id.IntegerValue : null,
                    typeId = scope == "type" ? target.Id.IntegerValue : (target.GetTypeId() != null && target.GetTypeId() != ElementId.InvalidElementId ? (int?)target.GetTypeId().IntegerValue : null),
                    uniqueId = target.UniqueId,
                    totalCount,
                    inputUnits = RailingUnits.InputUnits(),
                    internalUnits = RailingUnits.InternalUnits()
                };
            }

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    scope,
                    elementId = scope == "instance" ? (int?)target.Id.IntegerValue : null,
                    typeId = scope == "type" ? target.Id.IntegerValue : (target.GetTypeId() != null && target.GetTypeId() != ElementId.InvalidElementId ? (int?)target.GetTypeId().IntegerValue : null),
                    uniqueId = target.UniqueId,
                    totalCount,
                    names,
                    inputUnits = RailingUnits.InputUnits(),
                    internalUnits = RailingUnits.InternalUnits()
                };
            }

            var list = new List<object>();
            foreach (var pa in ordered.Skip(skip).Take(count))
            {
                ForgeTypeId fdt = null; string dataType = null;
                try { fdt = pa.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

                object val = null;
                try
                {
                    switch (pa.StorageType)
                    {
                        case StorageType.Double: val = RailingUnits.ConvertDoubleBySpec(pa.AsDouble(), fdt); break;
                        case StorageType.Integer: val = pa.AsInteger(); break;
                        case StorageType.String: val = pa.AsString() ?? string.Empty; break;
                        case StorageType.ElementId: val = pa.AsElementId()?.IntegerValue ?? -1; break;
                    }
                }
                catch { val = null; }

                list.Add(new
                {
                    name = pa.Definition?.Name ?? "",
                    id = pa.Id.IntegerValue,
                    storageType = pa.StorageType.ToString(),
                    isReadOnly = pa.IsReadOnly,
                    dataType,
                    value = val
                });
            }

            return new
            {
                ok = true,
                scope,
                elementId = scope == "instance" ? (int?)target.Id.IntegerValue : null,
                typeId = scope == "type" ? target.Id.IntegerValue : (target.GetTypeId() != null && target.GetTypeId() != ElementId.InvalidElementId ? (int?)target.GetTypeId().IntegerValue : null),
                uniqueId = target.UniqueId,
                totalCount,
                parameters = list,
                inputUnits = RailingUnits.InputUnits(),
                internalUnits = RailingUnits.InternalUnits()
            };
        }
    }

    // ------------------------------------------------------------
    // 5) set_railing_parameter（mm/deg 入力 → 内部変換でSet）
    // ------------------------------------------------------------
    public class SetRailingParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_railing_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var el = RailingUnits.ResolveElement(doc, p);
            if (el == null) return new { ok = false, msg = "Railing が見つかりません（elementId/uniqueId）。" };

            string name = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(name) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, msg = "paramName または builtInName/builtInId/guid のいずれかが必要です。" };
            if (!p.TryGetValue("value", out var vtok)) return new { ok = false, msg = "value が必要です。" };
            var param = ParamResolver.ResolveByPayload(el, p, out var resolvedBy);
            if (param == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)." };
            if (param.IsReadOnly) return new { ok = false, msg = $"Parameter '{name}' は読み取り専用です。" };

            using (var tx = new Transaction(doc, "Set Railing Param"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    switch (param.StorageType)
                    {
                        case StorageType.Double:
                            ForgeTypeId fdt = null; try { fdt = param.Definition?.GetDataType(); } catch { fdt = null; }
                            param.Set(RailingUnits.ToInternal(vtok.Value<double>(), fdt));
                            break;
                        case StorageType.Integer:
                            param.Set(vtok.Value<int>());
                            break;
                        case StorageType.String:
                            param.Set(vtok.Value<string>() ?? string.Empty);
                            break;
                        case StorageType.ElementId:
                            param.Set(new ElementId(vtok.Value<int>()));
                            break;
                        default:
                            tx.RollBack(); return new { ok = false, msg = $"Unsupported StorageType: {param.StorageType}" };
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"Set failed: {ex.Message}" };
                }
            }
            return new { ok = true, elementId = el.Id.IntegerValue, uniqueId = el.UniqueId };
        }
    }

    // ------------------------------------------------------------
    // 6) get_railing_types（paging / namesOnly / nameContains）
    // ------------------------------------------------------------
    public class GetRailingTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_railing_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            // 既存ページング（互換）
            int skipLegacy = p.Value<int?>("skip") ?? 0;
            int countLegacy = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // shape/paging + 軽量
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = System.Math.Max(0, page?.Value<int?>("limit") ?? countLegacy);
            int skip = System.Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? skipLegacy);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            string typeName = p.Value<string>("typeName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(ArchRailingType))
                .Cast<ArchRailingType>()
                .ToList();

            IEnumerable<ArchRailingType> q = all;

            if (!string.IsNullOrWhiteSpace(typeName))
                q = q.Where(t => string.Equals(t.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(t => (t.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            var filtered = q.ToList();
            int totalCount = filtered.Count;

            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount, inputUnits = RailingUnits.InputUnits(), internalUnits = RailingUnits.InternalUnits() };

            var ordered = filtered.Select(t => new { t, name = t.Name ?? "", id = t.Id.IntegerValue })
                                  .OrderBy(x => x.name).ThenBy(x => x.id)
                                  .Select(x => x.t).ToList();

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(t => t.Name ?? "").ToList();
                return new { ok = true, totalCount, names, inputUnits = RailingUnits.InputUnits(), internalUnits = RailingUnits.InternalUnits() };
            }

            if (idsOnly)
            {
                var typeIds = ordered.Skip(skip).Take(limit).Select(t => t.Id.IntegerValue).ToList();
                return new { ok = true, totalCount, typeIds, inputUnits = RailingUnits.InputUnits(), internalUnits = RailingUnits.InternalUnits() };
            }

            var types = ordered.Skip(skip).Take(limit)
                .Select(t => new { typeId = t.Id.IntegerValue, uniqueId = t.UniqueId, typeName = t.Name ?? "" })
                .ToList();

            return new { ok = true, totalCount, types, inputUnits = RailingUnits.InputUnits(), internalUnits = RailingUnits.InternalUnits() };
        }
    }

    // ------------------------------------------------------------
    // 7) change_railing_type（elementId/uniqueId × typeId/typeName）
    // ------------------------------------------------------------
    public class ChangeRailingTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_railing_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            var el = RailingUnits.ResolveElement(doc, p) as ArchRailing;
            if (el == null) return new { ok = false, msg = "Railing が見つかりません。" };

            ArchRailingType newType = null;
            int typeId = p.Value<int?>("typeId") ?? p.Value<int?>("railingTypeId") ?? 0;
            if (typeId > 0) newType = doc.GetElement(new ElementId(typeId)) as ArchRailingType;
            else
            {
                var tn = p.Value<string>("typeName");
                if (!string.IsNullOrWhiteSpace(tn))
                    newType = new FilteredElementCollector(doc)
                        .OfClass(typeof(ArchRailingType)).Cast<ArchRailingType>()
                        .FirstOrDefault(t => string.Equals(t.Name ?? "", tn, StringComparison.OrdinalIgnoreCase));
            }
            if (newType == null) return new { ok = false, msg = "新しい RailingType が見つかりません。" };

            int oldTypeId = el.GetTypeId().IntegerValue;

            using (var tx = new Transaction(doc, "Change Railing Type"))
            {
                tx.Start();
                el.ChangeTypeId(newType.Id);
                tx.Commit();
            }

            return new { ok = true, elementId = el.Id.IntegerValue, uniqueId = el.UniqueId, oldTypeId = oldTypeId, typeId = el.GetTypeId().IntegerValue };
        }
    }

    // ------------------------------------------------------------
    // 8) duplicate_railing_type
    // ------------------------------------------------------------
    public class DuplicateRailingTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_railing_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            int typeId = p.Value<int>("railingTypeId");
            string newName = p.Value<string>("newName");
            if (string.IsNullOrWhiteSpace(newName)) return new { ok = false, msg = "newName が必要です。" };

            var original = doc.GetElement(new ElementId(typeId)) as ArchRailingType;
            if (original == null) return new { ok = false, msg = "Railing type not found" };

            ArchRailingType dup = null;
            using (var tx = new Transaction(doc, "Duplicate Railing Type"))
            {
                tx.Start();
                dup = original.Duplicate(newName) as ArchRailingType;
                tx.Commit();
            }
            if (dup == null) return new { ok = false, msg = "タイプの複製に失敗しました。" };

            return new { ok = true, newTypeId = dup.Id.IntegerValue, newTypeName = dup.Name, uniqueId = dup.UniqueId };
        }
    }

    // ------------------------------------------------------------
    // 9) delete_railing_type
    // ------------------------------------------------------------
    public class DeleteRailingTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_railing_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            int typeId = ((JObject)cmd.Params).Value<int>("railingTypeId");
            ArchRailingType rt = doc.GetElement(new ElementId(typeId)) as ArchRailingType;
            if (rt == null) return new { ok = false, msg = "Railing type not found" };

            ICollection<ElementId> deleted = null;
            using (var tx = new Transaction(doc, "Delete Railing Type"))
            {
                tx.Start();
                deleted = doc.Delete(rt.Id);
                tx.Commit();
            }
            var ids = deleted != null ? deleted.Select(x => x.IntegerValue).ToList() : new List<int>();
            return new { ok = true, deletedCount = ids.Count, deletedElementIds = ids };
        }
    }

    // ------------------------------------------------------------
    // 10) get_railing_type_parameters（paging / namesOnly、mm/deg 出力）
    // ------------------------------------------------------------
    public class GetRailingTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_railing_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            ArchRailingType rt = null;
            int typeId = p.Value<int?>("typeId") ?? p.Value<int?>("railingTypeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            if (typeId > 0) rt = doc.GetElement(new ElementId(typeId)) as ArchRailingType;
            else if (!string.IsNullOrWhiteSpace(typeName))
                rt = new FilteredElementCollector(doc).OfClass(typeof(ArchRailingType)).Cast<ArchRailingType>()
                    .FirstOrDefault(t => string.Equals(t.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
            if (rt == null) return new { ok = false, msg = "RailingType が見つかりません。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (rt.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, totalCount, inputUnits = RailingUnits.InputUnits(), internalUnits = RailingUnits.InternalUnits() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new { ok = true, totalCount, names, inputUnits = RailingUnits.InputUnits(), internalUnits = RailingUnits.InternalUnits() };
            }

            var parameters = new List<object>();
            foreach (var pa in ordered.Skip(skip).Take(count))
            {
                ForgeTypeId fdt = null; string dataType = null;
                try { fdt = pa.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

                object value = null;
                try
                {
                    switch (pa.StorageType)
                    {
                        case StorageType.Double: value = RailingUnits.ConvertDoubleBySpec(pa.AsDouble(), fdt); break;
                        case StorageType.Integer: value = pa.AsInteger(); break;
                        case StorageType.String: value = pa.AsString() ?? string.Empty; break;
                        case StorageType.ElementId: value = pa.AsElementId()?.IntegerValue ?? -1; break;
                    }
                }
                catch { value = null; }

                parameters.Add(new
                {
                    name = pa.Definition?.Name ?? "",
                    id = pa.Id.IntegerValue,
                    storageType = pa.StorageType.ToString(),
                    isReadOnly = pa.IsReadOnly,
                    dataType,
                    value
                });
            }

            return new { ok = true, totalCount, parameters, inputUnits = RailingUnits.InputUnits(), internalUnits = RailingUnits.InternalUnits() };
        }
    }

    // ------------------------------------------------------------
    // 11) set_railing_type_parameter（mm/deg 入力 → 内部変換）
    // ------------------------------------------------------------
    public class SetRailingTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_railing_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            ArchRailingType rt = null;
            int typeId = p.Value<int?>("typeId") ?? p.Value<int?>("railingTypeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            if (typeId > 0) rt = doc.GetElement(new ElementId(typeId)) as ArchRailingType;
            else if (!string.IsNullOrWhiteSpace(typeName))
                rt = new FilteredElementCollector(doc).OfClass(typeof(ArchRailingType)).Cast<ArchRailingType>()
                    .FirstOrDefault(t => string.Equals(t.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
            if (rt == null) return new { ok = false, msg = "RailingType が見つかりません。" };

            string name = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(name) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null) return new { ok = false, msg = "paramName または builtInName/builtInId/guid が必要です。" };
            if (!p.TryGetValue("value", out var vtok)) return new { ok = false, msg = "value が必要です。" };
            var param = ParamResolver.ResolveByPayload(rt, p, out var resolvedBy);
            if (param == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)." };
            if (param.IsReadOnly) return new { ok = false, msg = $"Parameter '{name}' は読み取り専用です。" };

            using (var tx = new Transaction(doc, "Set Railing Type Param"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    switch (param.StorageType)
                    {
                        case StorageType.Double:
                            ForgeTypeId fdt = null; try { fdt = param.Definition?.GetDataType(); } catch { fdt = null; }
                            param.Set(RailingUnits.ToInternal(vtok.Value<double>(), fdt));
                            break;
                        case StorageType.Integer:
                            param.Set(vtok.Value<int>());
                            break;
                        case StorageType.String:
                            param.Set(vtok.Value<string>() ?? string.Empty);
                            break;
                        case StorageType.ElementId:
                            param.Set(new ElementId(vtok.Value<int>()));
                            break;
                        default:
                            tx.RollBack(); return new { ok = false, msg = $"Unsupported StorageType: {param.StorageType}" };
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"Set failed: {ex.Message}" };
                }
            }
            return new { ok = true, typeId = rt.Id.IntegerValue, uniqueId = rt.UniqueId };
        }
    }
}
