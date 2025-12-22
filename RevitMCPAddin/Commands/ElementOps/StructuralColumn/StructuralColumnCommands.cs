// File: RevitMCPAddin/Commands/ElementOps/StructuralColumn/StructuralColumnOps.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ElementOps.StructuralColumn
{
    // ============================================================
    // 共通ユーティリティ（Double 変換 / Param DTO）
    // ============================================================
    internal static class ColumnUnits
    {
        public static object ConvertDoubleBySpec(double raw, ForgeTypeId fdt)
        {
            try
            {
                if (fdt != null)
                {
                    if (fdt.Equals(SpecTypeId.Length))
                        return Math.Round(ConvertFromInternalUnits(raw, UnitTypeId.Millimeters), 3);
                    if (fdt.Equals(SpecTypeId.Area))
                        return Math.Round(ConvertFromInternalUnits(raw, UnitTypeId.SquareMillimeters), 3);
                    if (fdt.Equals(SpecTypeId.Volume))
                        return Math.Round(ConvertFromInternalUnits(raw, UnitTypeId.CubicMillimeters), 3);
                    if (fdt.Equals(SpecTypeId.Angle))
                        return Math.Round(raw * (180.0 / Math.PI), 3);
                }
            }
            catch { /* フォールバック */ }
            return Math.Round(raw, 3);
        }

        public static object InputUnits() => new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" };
        public static object InternalUnits() => new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" };
    }

    internal static class ColumnParamDto
    {
        public static object Build(Parameter pa)
        {
            if (pa == null) return null;

            ForgeTypeId fdt = null; string dataType = null;
            try { fdt = pa.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

            object value = null;
            try
            {
                switch (pa.StorageType)
                {
                    case StorageType.Double: value = ColumnUnits.ConvertDoubleBySpec(pa.AsDouble(), fdt); break;
                    case StorageType.Integer: value = pa.AsInteger(); break;
                    case StorageType.String: value = pa.AsString() ?? string.Empty; break;
                    case StorageType.ElementId: value = pa.AsElementId()?.IntValue() ?? -1; break;
                    default: value = null; break;
                }
            }
            catch { value = null; }

            return new
            {
                name = pa.Definition?.Name ?? "",
                id = pa.Id.IntValue(),
                storageType = pa.StorageType.ToString(),
                isReadOnly = pa.IsReadOnly,
                dataType,
                value
            };
        }
    }

    // ============================================================
    // 1. Move Structural Column
    // ============================================================
    public class MoveStructuralColumnCommand : IRevitCommandHandler
    {
        public string CommandName => "move_structural_column";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            // elementId / uniqueId 両対応
            Element target = null;
            int eid = p.Value<int?>("elementId") ?? p.Value<int?>("columnId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            if (!(target is FamilyInstance fi)) return new { ok = false, msg = "FamilyInstance（構造柱）が見つかりません。" };

            var off = p.Value<JObject>("offset");
            if (off == null) return new { ok = false, msg = "offset が必要です（mm）。" };

            var offset = new XYZ(
                ConvertToInternalUnits(off.Value<double>("x"), UnitTypeId.Millimeters),
                ConvertToInternalUnits(off.Value<double>("y"), UnitTypeId.Millimeters),
                ConvertToInternalUnits(off.Value<double>("z"), UnitTypeId.Millimeters));

            using (var tx = new Transaction(doc, "Move Structural Column"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, fi.Id, offset);
                tx.Commit();
            }

            return new
            {
                ok = true,
                elementId = fi.Id.IntValue(),
                uniqueId = fi.UniqueId,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }
    }

    // ============================================================
    // 2. Delete Structural Column
    // ============================================================
    public class DeleteStructuralColumnCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_structural_column";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element target = null;
            int eid = p.Value<int?>("elementId") ?? p.Value<int?>("columnId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            if (target == null) return new { ok = false, msg = "要素が見つかりません（elementId/uniqueId）。" };

            ICollection<ElementId> deleted = null;
            using (var tx = new Transaction(doc, "Delete Structural Column"))
            {
                tx.Start();
                deleted = doc.Delete(target.Id);
                tx.Commit();
            }

            return new { ok = true, deletedCount = deleted?.Count ?? 0 };
        }
    }

    // ============================================================
    // 3. Update Geometry (set location mm)
    // ============================================================
    public class UpdateStructuralColumnGeometryCommand : IRevitCommandHandler
    {
        public string CommandName => "update_structural_column_geometry";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element target = null;
            int eid = p.Value<int?>("elementId") ?? p.Value<int?>("columnId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            var col = target as FamilyInstance;
            if (col == null) return new { ok = false, msg = "FamilyInstance（構造柱）が見つかりません。" };

            var lp = col.Location as LocationPoint;
            if (lp == null || lp.Point == null) return new { ok = false, msg = "LocationPoint を持たない要素です。" };

            var loc = p.Value<JObject>("location");
            if (loc == null) return new { ok = false, msg = "location が必要です（mm）。" };

            var newPt = new XYZ(
                ConvertToInternalUnits(loc.Value<double>("x"), UnitTypeId.Millimeters),
                ConvertToInternalUnits(loc.Value<double>("y"), UnitTypeId.Millimeters),
                ConvertToInternalUnits(loc.Value<double>("z"), UnitTypeId.Millimeters));
            var offset = newPt - lp.Point;

            using (var tx = new Transaction(doc, "Update Structural Column Geometry"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, col.Id, offset);
                tx.Commit();
            }

            return new { ok = true, elementId = col.Id.IntValue(), uniqueId = col.UniqueId };
        }
    }

    // ============================================================
    // 4. Get Single Parameter (instance)
    // ============================================================
    public class GetStructuralColumnParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_column_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element target = null;
            int eid = p.Value<int?>("elementId") ?? p.Value<int?>("columnId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            var inst = target as FamilyInstance;
            if (inst == null) return new { ok = false, msg = "FamilyInstance（構造柱）が見つかりません。" };

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName)) return new { ok = false, msg = "paramName が必要です。" };

            var pa = inst.LookupParameter(paramName);
            if (pa == null) return new { ok = false, msg = $"Parameter not found: {paramName}" };

            ForgeTypeId fdt = null; string dataType = null;
            try { fdt = pa.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

            object value = null;
            try
            {
                switch (pa.StorageType)
                {
                    case StorageType.Double: value = ColumnUnits.ConvertDoubleBySpec(pa.AsDouble(), fdt); break;
                    case StorageType.Integer: value = pa.AsInteger(); break;
                    case StorageType.String: value = pa.AsString() ?? string.Empty; break;
                    case StorageType.ElementId: value = pa.AsElementId()?.IntValue() ?? -1; break;
                }
            }
            catch { value = null; }

            return new
            {
                ok = true,
                elementId = inst.Id.IntValue(),
                uniqueId = inst.UniqueId,
                name = pa.Definition?.Name ?? "",
                id = pa.Id.IntValue(),
                storageType = pa.StorageType.ToString(),
                isReadOnly = pa.IsReadOnly,
                dataType,
                value,
                inputUnits = ColumnUnits.InputUnits(),
                internalUnits = ColumnUnits.InternalUnits()
            };
        }
    }

    // ============================================================
    // 5. Update Single Parameter (instance)
    // ============================================================
    public class UpdateStructuralColumnParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_structural_column_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element target = null;
            int eid = p.Value<int?>("elementId") ?? p.Value<int?>("columnId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            var inst = target as FamilyInstance;
            if (inst == null) return new { ok = false, msg = "FamilyInstance（構造柱）が見つかりません。" };

            var paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, msg = "paramName または builtInName/builtInId/guid のいずれかが必要です。" };

            var pa = ParamResolver.ResolveByPayload(inst, p, out var resolvedBy);
            if (pa == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)" };
            if (pa.IsReadOnly) return new { ok = false, msg = $"Parameter '{paramName}' は読み取り専用です。" };
            if (!p.TryGetValue("value", out var vtok)) return new { ok = false, msg = "value が必要です。" };

            // オプション: レベルオフセット等の変更を Z 方向の移動としても反映する
            bool applyOffsetAsMove = p.Value<bool?>("applyOffsetAsMove") ?? false;
            double? deltaOffsetMm = null;
            if (applyOffsetAsMove && pa.StorageType == StorageType.Double)
            {
                try
                {
                    double oldInternal = pa.AsDouble();
                    double oldMm = ConvertFromInternalUnits(oldInternal, UnitTypeId.Millimeters);
                    double newMm = vtok.Value<double>();
                    double diff = newMm - oldMm;
                    if (Math.Abs(diff) > 1e-6)
                        deltaOffsetMm = diff;
                }
                catch
                {
                    // 差分計算に失敗した場合は位置移動を行わず、従来通りパラメータ更新のみを行う
                    deltaOffsetMm = null;
                }
            }

            using (var tx = new Transaction(doc, $"Set {paramName}"))
            {
                tx.Start();
                switch (pa.StorageType)
                {
                    case StorageType.String:
                        pa.Set(vtok.Value<string>() ?? string.Empty);
                        break;
                    case StorageType.Integer:
                        pa.Set(vtok.Value<int>());
                        break;
                    case StorageType.Double:
                        pa.Set(ConvertToInternalUnits(vtok.Value<double>(), UnitTypeId.Millimeters));
                        break;
                    case StorageType.ElementId:
                        pa.Set(Autodesk.Revit.DB.ElementIdCompat.From(vtok.Value<int>()));
                        break;
                    default:
                        tx.RollBack();
                        return new { ok = false, msg = $"Unsupported StorageType: {pa.StorageType}" };
                }

                // 必要に応じて、オフセット差分に応じて柱全体を Z 方向に移動
                if (deltaOffsetMm.HasValue && Math.Abs(deltaOffsetMm.Value) > 1e-6)
                {
                    try
                    {
                        var offset = UnitHelper.MmToInternalXYZ(0.0, 0.0, deltaOffsetMm.Value);
                        ElementTransformUtils.MoveElement(doc, inst.Id, offset);
                    }
                    catch (Exception ex)
                    {
                        // 位置移動に失敗してもパラメータ更新自体は成功させる
                        RevitMCPAddin.Core.RevitLogger.Warn($"update_structural_column_parameter: move by offset failed for element {inst.Id.IntValue()}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return new { ok = true, elementId = inst.Id.IntValue(), uniqueId = inst.UniqueId };
        }
    }

    // ============================================================
    // 6. Get All Instance Parameters (instance, paging / namesOnly)
    // ============================================================
    public class GetStructuralColumnParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_column_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element target = null;
            int eid = p.Value<int?>("elementId") ?? p.Value<int?>("columnId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            var inst = target as FamilyInstance;
            if (inst == null) return new { ok = false, msg = "FamilyInstance（構造柱）が見つかりません。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (inst.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
            {
                return new
                {
                    ok = true,
                    elementId = inst.Id.IntValue(),
                    uniqueId = inst.UniqueId,
                    totalCount,
                    inputUnits = ColumnUnits.InputUnits(),
                    internalUnits = ColumnUnits.InternalUnits()
                };
            }

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    elementId = inst.Id.IntValue(),
                    uniqueId = inst.UniqueId,
                    totalCount,
                    names,
                    inputUnits = ColumnUnits.InputUnits(),
                    internalUnits = ColumnUnits.InternalUnits()
                };
            }

            var page = ordered.Skip(skip).Take(count);
            var list = new List<object>();
            foreach (var pa in page)
                list.Add(ColumnParamDto.Build(pa));

            return new
            {
                ok = true,
                elementId = inst.Id.IntValue(),
                uniqueId = inst.UniqueId,
                totalCount,
                parameters = list,
                inputUnits = ColumnUnits.InputUnits(),
                internalUnits = ColumnUnits.InternalUnits()
            };
        }
    }

    // ============================================================
    // 7. Get Type Parameters (type or instance→type)
    // ============================================================
    public class GetStructuralColumnTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_column_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            FamilySymbol sym = null;
            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");

            if (typeId > 0)
            {
                sym = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as FamilySymbol;
                if (sym == null) return new { ok = false, msg = $"typeId={typeId} のタイプが見つかりません。" };
                if (sym.Category?.Id.IntValue() != (int)BuiltInCategory.OST_StructuralColumns)
                    return new { ok = false, msg = "指定タイプは構造柱カテゴリではありません。" };
            }
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                var q = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .Cast<FamilySymbol>()
                    .Where(s => string.Equals(s.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(familyName))
                    q = q.Where(s => string.Equals(s.Family?.Name ?? "", familyName, StringComparison.OrdinalIgnoreCase));
                sym = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
                if (sym == null) return new { ok = false, msg = $"typeName='{typeName}' のタイプが見つかりません。" };
            }
            else
            {
                // インスタンス→タイプ解決
                Element instElm = null;
                int eid = p.Value<int?>("elementId") ?? p.Value<int?>("columnId") ?? 0;
                string uid = p.Value<string>("uniqueId");
                if (eid > 0) instElm = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                else if (!string.IsNullOrWhiteSpace(uid)) instElm = doc.GetElement(uid);
                var inst = instElm as FamilyInstance;
                if (inst == null) return new { ok = false, msg = "FamilyInstance（構造柱）が見つかりません。" };
                sym = doc.GetElement(inst.GetTypeId()) as FamilySymbol;
                if (sym == null) return new { ok = false, msg = "タイプが取得できませんでした。" };
            }

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (sym.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
            {
                return new
                {
                    ok = true,
                    scope = "type",
                    typeId = sym.Id.IntValue(),
                    uniqueId = sym.UniqueId,
                    totalCount,
                    inputUnits = ColumnUnits.InputUnits(),
                    internalUnits = ColumnUnits.InternalUnits()
                };
            }

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    scope = "type",
                    typeId = sym.Id.IntValue(),
                    uniqueId = sym.UniqueId,
                    totalCount,
                    names,
                    inputUnits = ColumnUnits.InputUnits(),
                    internalUnits = ColumnUnits.InternalUnits()
                };
            }

            var page = ordered.Skip(skip).Take(count);
            var list = new List<object>();
            foreach (var pa in page)
                list.Add(ColumnParamDto.Build(pa));

            return new
            {
                ok = true,
                scope = "type",
                typeId = sym.Id.IntValue(),
                uniqueId = sym.UniqueId,
                totalCount,
                parameters = list,
                inputUnits = ColumnUnits.InputUnits(),
                internalUnits = ColumnUnits.InternalUnits()
            };
        }
    }

    // ============================================================
    // 8. Change Type (instance)
    // ============================================================
    public class ChangeStructuralColumnTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_structural_column_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element instElm = null;
            int eid = p.Value<int?>("elementId") ?? p.Value<int?>("columnId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) instElm = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) instElm = doc.GetElement(uid);
            var inst = instElm as FamilyInstance;
            if (inst == null) return new { ok = false, msg = "FamilyInstance（構造柱）が見つかりません。" };

            // new type: typeId / typeName(+familyName)
            FamilySymbol newSym = null;
            int typeId = p.Value<int?>("typeId") ?? 0;
            if (typeId > 0)
            {
                newSym = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as FamilySymbol;
                if (newSym == null) return new { ok = false, msg = $"typeId={typeId} のタイプが見つかりません。" };
            }
            else
            {
                var tn = p.Value<string>("typeName");
                var fn = p.Value<string>("familyName");
                if (!string.IsNullOrWhiteSpace(tn))
                {
                    var q = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_StructuralColumns)
                        .Cast<FamilySymbol>()
                        .Where(s => string.Equals(s.Name ?? "", tn, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(fn))
                        q = q.Where(s => string.Equals(s.Family?.Name ?? "", fn, StringComparison.OrdinalIgnoreCase));
                    newSym = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
                }
                if (newSym == null) return new { ok = false, msg = "新しいタイプが見つかりません。" };
            }

            using (var tx = new Transaction(doc, "Change Structural Column Type"))
            {
                tx.Start();
                inst.ChangeTypeId(newSym.Id);
                tx.Commit();
            }

            return new { ok = true, elementId = inst.Id.IntValue(), uniqueId = inst.UniqueId, typeId = inst.GetTypeId().IntValue() };
        }
    }

    // ============================================================
    // 9. Get All Structural Column Types (filters/paging/namesOnly)
    // ============================================================
    public class GetStructuralColumnTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_column_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            string filterTypeName = p.Value<string>("typeName");
            string filterFamilyName = p.Value<string>("familyName");
            int filterFamilyId = p.Value<int?>("familyId") ?? 0;
            string nameContains = p.Value<string>("nameContains");

            var allSyms = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .Cast<FamilySymbol>()
                .ToList();

            IEnumerable<FamilySymbol> q = allSyms;

            if (!string.IsNullOrWhiteSpace(filterTypeName))
            {
                q = q.Where(s => string.Equals(s.Name ?? "", filterTypeName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(filterFamilyName))
                    q = q.Where(s => string.Equals(s.Family?.Name ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase));
            }

            if (filterFamilyId > 0)
                q = q.Where(s => s.Family != null && s.Family.Id.IntValue() == filterFamilyId);

            if (!string.IsNullOrWhiteSpace(filterFamilyName))
                q = q.Where(s => string.Equals(s.Family?.Name ?? "", filterFamilyName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(s => (s.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            var ordered = q.Select(s => new
            {
                s,
                famName = s.Family != null ? (s.Family.Name ?? "") : "",
                typeName = s.Name ?? "",
                typeId = s.Id.IntValue()
            })
            .OrderBy(x => x.famName)
            .ThenBy(x => x.typeName)
            .ThenBy(x => x.typeId)
            .Select(x => x.s)
            .ToList();

            int totalCount = ordered.Count;

            if (count == 0)
            {
                return new
                {
                    ok = true,
                    totalCount,
                    inputUnits = ColumnUnits.InputUnits(),
                    internalUnits = ColumnUnits.InternalUnits()
                };
            }

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(s => s.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    totalCount,
                    names,
                    inputUnits = ColumnUnits.InputUnits(),
                    internalUnits = ColumnUnits.InternalUnits()
                };
            }

            var list = ordered.Skip(skip).Take(count)
                .Select(s => new
                {
                    typeId = s.Id.IntValue(),
                    uniqueId = s.UniqueId,
                    typeName = s.Name ?? "",
                    familyId = s.Family != null ? s.Family.Id.IntValue() : (int?)null,
                    familyName = s.Family != null ? (s.Family.Name ?? "") : ""
                }).ToList();

            return new
            {
                ok = true,
                totalCount,
                types = list,
                inputUnits = ColumnUnits.InputUnits(),
                internalUnits = ColumnUnits.InternalUnits()
            };
        }
    }

    // ============================================================
    // 10. List Parameter Definitions (instance or type)
    // ============================================================
    public class ListStructuralColumnParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "list_structural_column_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element target = null; string scope = "instance";
            if (p.TryGetValue("elementId", out var cid))
            {
                target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(cid.Value<int>()));
            }
            else if (p.TryGetValue("uniqueId", out var uidTok))
            {
                target = doc.GetElement(uidTok.Value<string>());
            }
            else if (p.TryGetValue("typeId", out var tid))
            {
                target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid.Value<int>()));
                scope = "type";
            }
            else if (!string.IsNullOrWhiteSpace(p.Value<string>("typeName")))
            {
                var tn = p.Value<string>("typeName");
                var fn = p.Value<string>("familyName");
                var q = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_StructuralColumns)
                    .Cast<FamilySymbol>()
                    .Where(s => string.Equals(s.Name ?? "", tn, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(fn))
                    q = q.Where(s => string.Equals(s.Family?.Name ?? "", fn, StringComparison.OrdinalIgnoreCase));
                target = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
                scope = "type";
            }

            if (target == null) return new { ok = false, msg = "Element/Type が見つかりません。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (target.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            int? elementIdOut = scope == "instance" ? (int?)target.Id.IntValue() : null;
            int? typeIdOut = scope == "type"
                ? target.Id.IntValue()
                : (target.GetTypeId() != null && target.GetTypeId() != ElementId.InvalidElementId
                    ? (int?)target.GetTypeId().IntValue()
                    : null);

            if (count == 0)
            {
                return new
                {
                    ok = true,
                    scope,
                    elementId = elementIdOut,
                    typeId = typeIdOut,
                    uniqueId = target.UniqueId,
                    totalCount,
                    inputUnits = ColumnUnits.InputUnits(),
                    internalUnits = ColumnUnits.InternalUnits()
                };
            }

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    scope,
                    elementId = elementIdOut,
                    typeId = typeIdOut,
                    uniqueId = target.UniqueId,
                    totalCount,
                    names,
                    inputUnits = ColumnUnits.InputUnits(),
                    internalUnits = ColumnUnits.InternalUnits()
                };
            }

            var page = ordered.Skip(skip).Take(count);
            var defs = new List<object>();
            foreach (var pa in page)
            {
                string dataType = null;
                try { dataType = pa.Definition?.GetDataType()?.TypeId; } catch { dataType = null; }
                defs.Add(new
                {
                    name = pa.Definition?.Name ?? "",
                    id = pa.Id.IntValue(),
                    storageType = pa.StorageType.ToString(),
                    dataType,
                    isReadOnly = pa.IsReadOnly
                });
            }

            return new
            {
                ok = true,
                scope,
                elementId = elementIdOut,
                typeId = typeIdOut,
                uniqueId = target.UniqueId,
                totalCount,
                definitions = defs,
                inputUnits = ColumnUnits.InputUnits(),
                internalUnits = ColumnUnits.InternalUnits()
            };
        }
    }
}


