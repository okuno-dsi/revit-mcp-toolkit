// ================================================================
// File: RevitMCPAddin/Commands/ElementOps/StairOps/StairCommands.cs
// Target : Revit 2023 / .NET Framework 4.8 / C# 8
// Policy : 単位・表示・パラメータ変換は 100% UnitHelper に委譲
//          - 入出力の既定は SI（Length=mm, Area=m2, Volume=m3, Angle=deg）
//          - unitsMode（SI/Project/Raw/Both）は UnitHelper.ResolveUnitsMode で解決
// Error  : 例外時は { ok:false, msg } を返す（AIが理由を理解できる文言）
// ================================================================

using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.StairOps
{
    // --------------------------------------------
    // 小物: 位置(XYZ)を mm の {x,y,z} 匿名オブジェクトに整形
    // --------------------------------------------
    internal static class StairLocFmt
    {
        public static object ToMmXYZ(XYZ p, int digits = 3)
        {
            if (p == null) return null;
            var (x, y, z) = UnitHelper.XyzToMm(p); // (double x, double y, double z)
            return new
            {
                x = Math.Round(x, digits, MidpointRounding.AwayFromZero),
                y = Math.Round(y, digits, MidpointRounding.AwayFromZero),
                z = Math.Round(z, digits, MidpointRounding.AwayFromZero)
            };
        }
    }

    // ============================================================
    // 1. get_stairs  … 一覧（フィルタ/ページング/namesOnly）
    // ============================================================
    public class GetStairsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_stairs";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            // legacy paging (backward compatible)
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // shape/paging + lightweight
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            bool includeLocation = p.Value<bool?>("includeLocation") ?? true;

            // 単一ターゲット（優先）
            int targetEid = p.Value<int?>("elementId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");

            // フィルタ
            int filterTypeId = p.Value<int?>("typeId") ?? 0;
            string filterTypeName = p.Value<string>("typeName");
            int filterLevelId = p.Value<int?>("levelId") ?? 0;
            string filterLevelName = p.Value<string>("levelName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Stairs)
                .WhereElementIsNotElementType()
                .Cast<Stairs>()
                .ToList();

            // タイプ名キャッシュ
            var typeIds = all.Select(s => s.GetTypeId().IntegerValue).Distinct().ToList();
            var typeMap = new Dictionary<int, ElementType>(typeIds.Count);
            foreach (var id in typeIds) typeMap[id] = doc.GetElement(new ElementId(id)) as ElementType;

            // レベル名キャッシュ
            var levelIds = all.Select(s => s.LevelId.IntegerValue).Distinct().ToList();
            var levelMap = new Dictionary<int, Level>(levelIds.Count);
            foreach (var id in levelIds) levelMap[id] = doc.GetElement(new ElementId(id)) as Level;

            IEnumerable<Stairs> q = all;

            // 単一ターゲット
            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                Stairs target = null;
                if (targetEid > 0) target = doc.GetElement(new ElementId(targetEid)) as Stairs;
                else target = doc.GetElement(targetUid) as Stairs;
                q = target == null ? Enumerable.Empty<Stairs>() : new[] { target };
            }

            // フィルタ適用
            if (filterTypeId > 0)
                q = q.Where(s => s.GetTypeId().IntegerValue == filterTypeId);

            if (!string.IsNullOrWhiteSpace(filterTypeName))
                q = q.Where(s =>
                {
                    typeMap.TryGetValue(s.GetTypeId().IntegerValue, out var et);
                    return et != null && string.Equals(et.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase);
                });

            if (filterLevelId > 0)
                q = q.Where(s => s.LevelId.IntegerValue == filterLevelId);

            if (!string.IsNullOrWhiteSpace(filterLevelName))
                q = q.Where(s =>
                {
                    levelMap.TryGetValue(s.LevelId.IntegerValue, out var lv);
                    return lv != null && string.Equals(lv.Name ?? "", filterLevelName, StringComparison.OrdinalIgnoreCase);
                });

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(s =>
                {
                    var n = s.Name ?? "";
                    if (n.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    typeMap.TryGetValue(s.GetTypeId().IntegerValue, out var et);
                    return (et?.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                });

            // 並び: typeName → elementId
            var ordered = q.Select(s =>
            {
                typeMap.TryGetValue(s.GetTypeId().IntegerValue, out var et);
                string tName = et?.Name ?? "";
                return new { s, tName };
            })
            .OrderBy(x => x.tName)
            .ThenBy(x => x.s.Id.IntegerValue)
            .Select(x => x.s)
            .ToList();

            int totalCount = ordered.Count;

            // メタのみ
            if (summaryOnly || limit == 0)
                return new
                {
                    ok = true,
                    totalCount,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };

            // namesOnly
            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(s =>
                {
                    var n = s.Name ?? "";
                    if (!string.IsNullOrEmpty(n)) return n;
                    typeMap.TryGetValue(s.GetTypeId().IntegerValue, out var et);
                    return et?.Name ?? "";
                }).ToList();

                return new
                {
                    ok = true,
                    totalCount,
                    names,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };
            }

            // paging + idsOnly + optional location
            IEnumerable<Stairs> paged = ordered;
            if (skip > 0) paged = paged.Skip(skip);
            if (limit > 0 && limit != int.MaxValue) paged = paged.Take(limit);

            if (idsOnly)
            {
                var ids = paged.Select(st => st.Id.IntegerValue).ToList();
                return new
                {
                    ok = true,
                    totalCount,
                    elementIds = ids,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };
            }

            var list = paged
                .Select(st =>
                {
                    XYZ origin = null;
                    if (includeLocation)
                    {
                        if (st.Location is LocationPoint lp) origin = lp.Point;
                        else if (st.Location is LocationCurve lc) origin = lc.Curve?.GetEndPoint(0);
                    }

                    typeMap.TryGetValue(st.GetTypeId().IntegerValue, out var et);
                    levelMap.TryGetValue(st.LevelId.IntegerValue, out var lv);

                    return new
                    {
                        elementId = st.Id.IntegerValue,
                        uniqueId = st.UniqueId,
                        typeId = st.GetTypeId().IntegerValue,
                        typeName = et?.Name ?? "",
                        levelId = st.LevelId.IntegerValue,
                        levelName = lv?.Name ?? "",
                        location = StairLocFmt.ToMmXYZ(origin)
                    };
                })
                .ToList();

            return new
            {
                ok = true,
                totalCount,
                stairs = list,
                inputUnits = UnitHelper.InputUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }
    }

    // ============================================================
    // 2. duplicate_stair_instance  … CopyElement(offset: mm)
    // ============================================================
    public class DuplicateStairInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_stair_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            Element src = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) src = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) src = doc.GetElement(uid);
            var st = src as Stairs;
            if (st == null) return new { ok = false, msg = "Stair が見つかりません（elementId/uniqueId）。" };

            // offset or location キー（相対移動ベクトルmm）
            var off = p.Value<JObject>("offset") ?? p.Value<JObject>("location");
            if (off == null) return new { ok = false, msg = "offset が必要です（mm）。" };

            var offset = UnitHelper.MmToXyz(off.Value<double?>("x") ?? 0, off.Value<double?>("y") ?? 0, off.Value<double?>("z") ?? 0);

            ICollection<ElementId> newIds = null;
            using (var tx = new Transaction(doc, "Duplicate Stair Instance"))
            {
                tx.Start();
                newIds = ElementTransformUtils.CopyElement(doc, st.Id, offset);
                tx.Commit();
            }

            if (newIds != null && newIds.Count > 0)
            {
                // 最初に返るのが Stair 本体とは限らないため、本体を探す
                ElementId pick = newIds.First();
                foreach (var nid in newIds)
                {
                    var e = doc.GetElement(nid);
                    if (e is Stairs) { pick = nid; break; }
                }
                var newSt = doc.GetElement(pick);
                return new
                {
                    ok = true,
                    newElementId = pick.IntegerValue,
                    newUniqueId = newSt?.UniqueId,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };
            }

            return new { ok = false, msg = "階段の複製に失敗しました。" };
        }
    }

    // ============================================================
    // 3. move_stair_instance  … MoveElement(offset: mm)
    // ============================================================
    public class MoveStairInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "move_stair_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            Element src = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) src = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) src = doc.GetElement(uid);
            if (!(src is Stairs st)) return new { ok = false, msg = "Stair が見つかりません。" };

            // dx/dy/dz or offset
            XYZ offset;
            var off = p.Value<JObject>("offset");
            if (off != null)
            {
                offset = UnitHelper.MmToXyz(off.Value<double?>("x") ?? 0, off.Value<double?>("y") ?? 0, off.Value<double?>("z") ?? 0);
            }
            else
            {
                double dx = p.Value<double?>("dx") ?? 0;
                double dy = p.Value<double?>("dy") ?? 0;
                double dz = p.Value<double?>("dz") ?? 0;
                offset = UnitHelper.MmToXyz(dx, dy, dz);
            }

            using (var tx = new Transaction(doc, "Move Stair Instance"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, st.Id, offset);
                tx.Commit();
            }

            return new
            {
                ok = true,
                elementId = st.Id.IntegerValue,
                uniqueId = st.UniqueId,
                inputUnits = UnitHelper.InputUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }
    }

    // ============================================================
    // 4. delete_stair_instance
    // ============================================================
    public class DeleteStairInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_stair_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            Element src = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) src = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) src = doc.GetElement(uid);
            if (!(src is Stairs st)) return new { ok = false, msg = "Stair が見つかりません。" };

            ICollection<ElementId> deleted = null;
            using (var tx = new Transaction(doc, "Delete Stair Instance"))
            {
                tx.Start();
                deleted = doc.Delete(st.Id);
                tx.Commit();
            }

            return new { ok = true, deletedCount = deleted?.Count ?? 0 };
        }
    }

    // ============================================================
    // 5. get_stair_parameters  … インスタンスのパラメータ一覧（paging/namesOnly）
    // ============================================================
    public class GetStairParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_stair_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            Element src = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) src = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) src = doc.GetElement(uid);
            if (src == null) return new { ok = false, msg = "Stair が見つかりません（elementId/uniqueId）。" };

            // unitsMode を解決（SI/Project/Raw/Both）
            var mode = UnitHelper.ResolveUnitsMode(doc, p);

            // paging (legacy + shape)
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            var ordered = (src.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
                return new
                {
                    ok = true,
                    elementId = src.Id.IntegerValue,
                    uniqueId = src.UniqueId,
                    totalCount,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    elementId = src.Id.IntegerValue,
                    uniqueId = src.UniqueId,
                    totalCount,
                    names,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };
            }

            var pageSeq = ordered.Skip(skip).Take(limit);
            var list = new List<object>();
            foreach (var pa in pageSeq)
            {
                // UnitHelper で外部表現を構築
                list.Add(UnitHelper.MapParameter(pa, doc, mode, includeDisplay: true, includeRaw: true, siDigits: 3));
            }

            return new
            {
                ok = true,
                elementId = src.Id.IntegerValue,
                uniqueId = src.UniqueId,
                totalCount,
                parameters = list,
                inputUnits = UnitHelper.InputUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }
    }

    // ============================================================
    // 6. set_stair_parameter  … 単一設定（Doubleは SI 入力→内部に変換）
    // ============================================================
    public class SetStairParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_stair_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            Element src = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) src = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) src = doc.GetElement(uid);
            if (src == null) return new { ok = false, msg = "Stair が見つかりません（elementId/uniqueId）。" };

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null) return new { ok = false, msg = "paramName または builtInName/builtInId/guid が必要です。" };

            var pa = ParamResolver.ResolveByPayload(src, p, out var resolvedBy);
            if (pa == null) return new { ok = false, msg = $"Parameter '{paramName}' not found." };
            if (pa.IsReadOnly) return new { ok = false, msg = $"Parameter '{paramName}' is read-only." };
            if (!p.TryGetValue("value", out var vtok)) return new { ok = false, msg = "value が必要です。" };

            using (var tx = new Transaction(doc, $"Set Stair Parameter {paramName}"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                if (!UnitHelper.TrySetParameterFromSi(pa, vtok, out var reason))
                {
                    tx.RollBack();
                    return new { ok = false, msg = reason ?? "値の設定に失敗しました。" };
                }
                tx.Commit();
            }
            return new { ok = true, elementId = src.Id.IntegerValue, uniqueId = src.UniqueId };
        }
    }

    // ============================================================
    // 7. get_stair_types  … 一覧（paging/namesOnly/filters）
    // ============================================================
    public class GetStairTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_stair_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            // paging (legacy + shape)
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            string filterTypeName = p.Value<string>("typeName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(StairsType))
                .WhereElementIsElementType()
                .Cast<StairsType>()
                .ToList();

            IEnumerable<StairsType> q = all;

            if (!string.IsNullOrWhiteSpace(filterTypeName))
                q = q.Where(t => string.Equals(t.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(t => (t.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            var ordered = q.Select(t => new { t, name = t.Name ?? "", id = t.Id.IntegerValue })
                           .OrderBy(x => x.name).ThenBy(x => x.id).Select(x => x.t).ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount, inputUnits = UnitHelper.InputUnitsMeta(), internalUnits = UnitHelper.InternalUnitsMeta() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(t => t.Name ?? "").ToList();
                return new { ok = true, totalCount, names, inputUnits = UnitHelper.InputUnitsMeta(), internalUnits = UnitHelper.InternalUnitsMeta() };
            }

            if (idsOnly)
            {
                var typeIds = ordered.Skip(skip).Take(limit).Select(t => t.Id.IntegerValue).ToList();
                return new { ok = true, totalCount, typeIds, inputUnits = UnitHelper.InputUnitsMeta(), internalUnits = UnitHelper.InternalUnitsMeta() };
            }

            var list = ordered.Skip(skip).Take(limit)
                .Select(t => new { typeId = t.Id.IntegerValue, uniqueId = t.UniqueId, typeName = t.Name ?? "" })
                .ToList();

            return new { ok = true, totalCount, types = list, inputUnits = UnitHelper.InputUnitsMeta(), internalUnits = UnitHelper.InternalUnitsMeta() };
        }
    }

    // ============================================================
    // 8. duplicate_stair_type
    // ============================================================
    public class DuplicateStairTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_stair_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            int srcId = p.Value<int>("sourceTypeId");
            string newName = p.Value<string>("newTypeName");
            if (string.IsNullOrWhiteSpace(newName)) return new { ok = false, msg = "newTypeName が必要です。" };

            var src = doc.GetElement(new ElementId(srcId)) as StairsType;
            if (src == null) return new { ok = false, msg = $"StairsType not found: {srcId}" };

            StairsType dup = null;
            using (var tx = new Transaction(doc, "Duplicate Stair Type"))
            {
                tx.Start();
                dup = src.Duplicate(newName) as StairsType;
                tx.Commit();
            }
            if (dup == null) return new { ok = false, msg = "タイプの複製に失敗しました。" };

            return new { ok = true, newTypeId = dup.Id.IntegerValue, newTypeName = dup.Name, uniqueId = dup.UniqueId };
        }
    }

    // ============================================================
    // 9. delete_stair_type
    // ============================================================
    public class DeleteStairTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_stair_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            int typeId = p.Value<int>("typeId");
            var id = new ElementId(typeId);
            var t = doc.GetElement(id) as StairsType;
            if (t == null) return new { ok = false, msg = $"StairsType not found: {typeId}" };

            using (var tx = new Transaction(doc, "Delete Stair Type"))
            {
                tx.Start();
                doc.Delete(id);
                tx.Commit();
            }
            return new { ok = true, deletedTypeId = typeId, deletedTypeName = t.Name };
        }
    }

    // ============================================================
    // 10. change_stair_type
    // ============================================================
    public class ChangeStairTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_stair_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            Element src = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) src = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) src = doc.GetElement(uid);
            var st = src as Stairs;
            if (st == null) return new { ok = false, msg = "Stair が見つかりません。" };

            ElementType newType = null;
            int newTypeId = p.Value<int?>("newTypeId") ?? 0;
            if (newTypeId > 0)
            {
                newType = doc.GetElement(new ElementId(newTypeId)) as StairsType;
            }
            else
            {
                string typeName = p.Value<string>("typeName");
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    newType = new FilteredElementCollector(doc)
                        .OfClass(typeof(StairsType))
                        .WhereElementIsElementType()
                        .Cast<StairsType>()
                        .FirstOrDefault(t => string.Equals(t.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (newType == null) return new { ok = false, msg = "新しい StairsType が見つかりません。" };

            using (var tx = new Transaction(doc, "Change Stair Type"))
            {
                tx.Start();
                st.ChangeTypeId(newType.Id);
                tx.Commit();
            }
            return new { ok = true, elementId = st.Id.IntegerValue, uniqueId = st.UniqueId, typeId = st.GetTypeId().IntegerValue };
        }
    }

    // ============================================================
    // 11. get_stair_type_parameters  … タイプのパラメータ一覧（paging/namesOnly）
    // ============================================================
    public class GetStairTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_stair_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            // typeId / typeName / elementId(uniqueId)→type
            ElementType t = null;
            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            if (typeId > 0)
            {
                t = doc.GetElement(new ElementId(typeId)) as ElementType;
            }
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                t = new FilteredElementCollector(doc)
                    .OfClass(typeof(StairsType))
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .FirstOrDefault(x => string.Equals(x.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                Element inst = null;
                int eid = p.Value<int?>("elementId") ?? 0;
                string uid = p.Value<string>("uniqueId");
                if (eid > 0) inst = doc.GetElement(new ElementId(eid));
                else if (!string.IsNullOrWhiteSpace(uid)) inst = doc.GetElement(uid);
                if (inst != null)
                {
                    var tid = inst.GetTypeId();
                    if (tid != null && tid != ElementId.InvalidElementId)
                        t = doc.GetElement(tid) as ElementType;
                }
            }
            if (t == null) return new { ok = false, msg = "StairsType が見つかりません。" };

            // unitsMode
            var mode = UnitHelper.ResolveUnitsMode(doc, p);

            // paging (legacy + shape)
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            var shape = p["_shape"] as JObject;
            var page = shape?["page"] as JObject;
            int limit = Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            var ordered = (t.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntegerValue ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
                return new
                {
                    ok = true,
                    scope = "type",
                    typeId = t.Id.IntegerValue,
                    uniqueId = t.UniqueId,
                    totalCount,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    scope = "type",
                    typeId = t.Id.IntegerValue,
                    uniqueId = t.UniqueId,
                    totalCount,
                    names,
                    inputUnits = UnitHelper.InputUnitsMeta(),
                    internalUnits = UnitHelper.InternalUnitsMeta()
                };
            }

            var pageSeq = ordered.Skip(skip).Take(limit);
            var list = new List<object>();
            foreach (var pa in pageSeq)
            {
                list.Add(UnitHelper.MapParameter(pa, doc, mode, includeDisplay: true, includeRaw: true, siDigits: 3));
            }

            return new
            {
                ok = true,
                scope = "type",
                typeId = t.Id.IntegerValue,
                uniqueId = t.UniqueId,
                totalCount,
                parameters = list,
                inputUnits = UnitHelper.InputUnitsMeta(),
                internalUnits = UnitHelper.InternalUnitsMeta()
            };
        }
    }

    // ============================================================
    // 12. set_stair_type_parameter
    // ============================================================
    public class SetStairTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_stair_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            ElementType t = null;
            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            if (typeId > 0)
                t = doc.GetElement(new ElementId(typeId)) as ElementType;
            else if (!string.IsNullOrWhiteSpace(typeName))
                t = new FilteredElementCollector(doc).OfClass(typeof(StairsType)).WhereElementIsElementType().Cast<ElementType>()
                     .FirstOrDefault(x => string.Equals(x.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
            if (t == null) return new { ok = false, msg = "StairsType が見つかりません。" };

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null) return new { ok = false, msg = "paramName または builtInName/builtInId/guid が必要です。" };
            if (!p.TryGetValue("value", out var vtok)) return new { ok = false, msg = "value が必要です。" };

            var pa = ParamResolver.ResolveByPayload(t, p, out var resolvedBy2);
            if (pa == null) return new { ok = false, msg = $"Parameter '{paramName}' not found." };
            if (pa.IsReadOnly) return new { ok = false, msg = $"Parameter '{paramName}' is read-only." };

            using (var tx = new Transaction(doc, $"Set StairsType Parameter {paramName}"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                if (!UnitHelper.TrySetParameterFromSi(pa, vtok, out var reason))
                {
                    tx.RollBack();
                    return new { ok = false, msg = reason ?? "値の設定に失敗しました。" };
                }
                tx.Commit();
            }
            return new { ok = true, typeId = t.Id.IntegerValue, uniqueId = t.UniqueId };
        }
    }

    // ============================================================
    // 13. get_stair_flights  … ラン情報取得（単位なし）
    // ============================================================
    public class GetStairFlightsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_stair_flights";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params ?? new JObject();
            Element src = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) src = doc.GetElement(new ElementId(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) src = doc.GetElement(uid);
            var st = src as Stairs;
            if (st == null) return new { ok = false, msg = "Stair が見つかりません。" };

            var runIds = st.GetStairsRuns().Cast<ElementId>().ToList();
            var flights = new List<object>(runIds.Count);

            for (int i = 0; i < runIds.Count; i++)
            {
                var runEl = doc.GetElement(runIds[i]) as StairsRun;
                int risers = runEl?.ActualRisersNumber ?? 0;
                int treads = runEl?.ActualTreadsNumber ?? 0;
                flights.Add(new
                {
                    flightIndex = i,
                    runId = runIds[i].IntegerValue,
                    runUniqueId = runEl?.UniqueId,
                    riserCount = risers,
                    treadCount = treads
                });
            }

            return new { ok = true, elementId = st.Id.IntegerValue, uniqueId = st.UniqueId, flights };
        }
    }

    // ============================================================
    // 14. set_stair_flight_parameters  … ラン単位のパラメータ設定（SI入力）
    // ============================================================
    public class SetStairFlightParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "set_stair_flight_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params ?? new JObject();
            int elementId = p.Value<int>("elementId");
            int flightIndex = p.Value<int>("flightIndex");
            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName)) return new { ok = false, msg = "paramName が必要です。" };
            if (!p.TryGetValue("value", out var vtok)) return new { ok = false, msg = "value が必要です。" };

            var st = doc.GetElement(new ElementId(elementId)) as Stairs;
            if (st == null) return new { ok = false, msg = $"Stair not found: {elementId}" };

            var runIds = st.GetStairsRuns().Cast<ElementId>().ToList();
            if (flightIndex < 0 || flightIndex >= runIds.Count) return new { ok = false, msg = $"flightIndex {flightIndex} は範囲外です。" };

            var runEl = doc.GetElement(runIds[flightIndex]);
            if (runEl == null) return new { ok = false, msg = $"StairsRun not found: {runIds[flightIndex].IntegerValue}" };

            var pa = ParamResolver.ResolveByPayload(runEl, p, out var resolvedBy3);
            if (pa == null) return new { ok = false, msg = $"Parameter '{paramName}' not found on run." };
            if (pa.IsReadOnly) return new { ok = false, msg = $"Parameter '{paramName}' is read-only." };

            using (var tx = new Transaction(doc, $"Set Stair Run Parameter {paramName}"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                if (!UnitHelper.TrySetParameterFromSi(pa, vtok, out var reason))
                {
                    tx.RollBack();
                    return new { ok = false, msg = reason ?? "値の設定に失敗しました。" };
                }
                tx.Commit();
            }
            return new { ok = true, elementId = st.Id.IntegerValue, uniqueId = st.UniqueId, flightIndex };
        }
    }
}
