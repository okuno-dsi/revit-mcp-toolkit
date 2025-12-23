// ================================================================
// File: Commands/ElementOps/Material/MaterialCommands.cs  (UnitHelper対応版)
// 仕様: namesOnly / paging / filters、返却一貫 (materialId/uniqueId 等)
//      Double は SpecTypeId に基づき mm/mm2/mm3/deg ⇔ ft/ft2/ft3/rad を UnitHelper 経由で変換
// Target: .NET Framework 4.8 / Revit 2023 / C# 8
// Depends: Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq, RevitMCPAddin.Core(UnitHelper)
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using ARDB = Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands.ElementOps.Material
{
    // 共通ユーティリティ（表示↔内部の変換を UnitHelper に委譲）
    internal static class MatUnits
    {
        public static object InputUnits() => new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" };
        public static object InternalUnits() => new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" };

        // 内部→表示
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
            catch { /* 変換失敗時は内部値をそのまま返す */ }
            return Math.Round(rawInternal, 3);
        }

        // 表示→内部（不明 Spec は素通し）
        public static double ToInternal(double userValue, ForgeTypeId fdt)
        {
            try
            {
                if (fdt != null)
                {
                    if (fdt.Equals(SpecTypeId.Length)) return UnitHelper.MmToFt(userValue);
                    if (fdt.Equals(SpecTypeId.Area)) return UnitHelper.Mm2ToFt2(userValue);   // 必要なら UnitHelper に実装
                    if (fdt.Equals(SpecTypeId.Volume)) return UnitHelper.Mm3ToFt3(userValue);   // 必要なら UnitHelper に実装
                    if (fdt.Equals(SpecTypeId.Angle)) return UnitHelper.DegToRad(userValue);
                }
            }
            catch { /* 失敗時は生値 */ }
            return userValue;
        }
    }

    // 1) Material 一覧
    public class GetMaterialsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_materials";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            // legacy paging (backward compatible)
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // shape/paging + lightweight
            var shape = p["_shape"] as JObject;
            bool idsOnly = shape?.Value<bool?>("idsOnly") ?? false;
            var page = shape?["page"] as JObject;
            int limit = System.Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = System.Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;
            string nameContains = p.Value<string>("nameContains");
            string filterClass = p.Value<string>("materialClass"); // Material.MaterialClass

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(ARDB.Material))
                .Cast<ARDB.Material>()
                .ToList();

            IEnumerable<ARDB.Material> q = all;

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(m => (m.Name ?? "").IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            if (!string.IsNullOrWhiteSpace(filterClass))
                q = q.Where(m => string.Equals(m.MaterialClass ?? "", filterClass, StringComparison.OrdinalIgnoreCase));

            var ordered = q
                .Select(m => new { m, name = m.Name ?? "", id = m.Id.IntValue() })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.m)
                .ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
                return new { ok = true, totalCount, inputUnits = MatUnits.InputUnits(), internalUnits = MatUnits.InternalUnits() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(m => m.Name ?? "").ToList();
                return new { ok = true, totalCount, names, inputUnits = MatUnits.InputUnits(), internalUnits = MatUnits.InternalUnits() };
            }

            // idsOnly path
            if (idsOnly)
            {
                var materialIds = ordered.Skip(skip).Take(limit).Select(m => m.Id.IntValue()).ToList();
                return new { ok = true, totalCount, materialIds, inputUnits = MatUnits.InputUnits(), internalUnits = MatUnits.InternalUnits() };
            }

            var materials = ordered.Skip(skip).Take(limit)
                .Select(m => new
                {
                    materialId = m.Id.IntValue(),
                    uniqueId = m.UniqueId,
                    materialName = m.Name ?? "",
                    materialClass = m.MaterialClass ?? ""
                }).ToList();

            return new { ok = true, totalCount, materials, inputUnits = MatUnits.InputUnits(), internalUnits = MatUnits.InternalUnits() };
        }
    }

    // 2) Material パラメータ（値）一覧
    public class GetMaterialParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_material_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null) return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };

            // paging (legacy + shape)
            int legacySkip = p.Value<int?>("skip") ?? 0;
            int legacyCount = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            var shape = p["_shape"] as JObject;
            var page = shape?["page"] as JObject;
            int limit = System.Math.Max(0, page?.Value<int?>("limit") ?? legacyCount);
            int skip = System.Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? legacySkip);
            bool summaryOnly = p.Value<bool?>("summaryOnly") ?? false;

            var ordered = (material.Parameters?.Cast<ARDB.Parameter>() ?? Enumerable.Empty<ARDB.Parameter>())
                .Select(prm => new { prm, name = prm?.Definition?.Name ?? "", id = prm?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.prm).ToList();

            int totalCount = ordered.Count;

            if (summaryOnly || limit == 0)
            {
                return new
                {
                    ok = true,
                    materialId = material.Id.IntValue(),
                    uniqueId = material.UniqueId,
                    totalCount,
                    inputUnits = MatUnits.InputUnits(),
                    internalUnits = MatUnits.InternalUnits()
                };
            }

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(limit).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    materialId = material.Id.IntValue(),
                    uniqueId = material.UniqueId,
                    totalCount,
                    names,
                    inputUnits = MatUnits.InputUnits(),
                    internalUnits = MatUnits.InternalUnits()
                };
            }

            var pageSeq = ordered.Skip(skip).Take(limit);
            var parameters = new List<object>();
            foreach (var prm in pageSeq)
            {
                if (prm == null) continue;
                ForgeTypeId fdt = null; string dataType = null;
                try { fdt = prm.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

                object value = null;
                try
                {
                    switch (prm.StorageType)
                    {
                        case ARDB.StorageType.String: value = prm.AsString() ?? string.Empty; break;
                        case ARDB.StorageType.Integer: value = prm.AsInteger(); break;
                        case ARDB.StorageType.Double: value = MatUnits.ConvertDoubleBySpec(prm.AsDouble(), fdt); break;
                        case ARDB.StorageType.ElementId: value = prm.AsElementId()?.IntValue() ?? -1; break;
                    }
                }
                catch { value = null; }

                parameters.Add(new
                {
                    name = prm.Definition?.Name ?? "",
                    id = prm.Id.IntValue(),
                    storageType = prm.StorageType.ToString(),
                    isReadOnly = prm.IsReadOnly,
                    dataType,
                    value
                });
            }

            return new
            {
                ok = true,
                materialId = material.Id.IntValue(),
                uniqueId = material.UniqueId,
                totalCount,
                parameters,
                inputUnits = MatUnits.InputUnits(),
                internalUnits = MatUnits.InternalUnits()
            };
        }
    }

    // 3) Material パラメータ（定義）一覧
    public class ListMaterialParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "list_material_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null) return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (material.Parameters?.Cast<ARDB.Parameter>() ?? Enumerable.Empty<ARDB.Parameter>())
                .Select(prm => new { prm, name = prm?.Definition?.Name ?? "", id = prm?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.prm).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
                return new { ok = true, materialId = material.Id.IntValue(), uniqueId = material.UniqueId, totalCount, inputUnits = MatUnits.InputUnits(), internalUnits = MatUnits.InternalUnits() };

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(prm => prm?.Definition?.Name ?? "").ToList();
                return new { ok = true, materialId = material.Id.IntValue(), uniqueId = material.UniqueId, totalCount, names, inputUnits = MatUnits.InputUnits(), internalUnits = MatUnits.InternalUnits() };
            }

            var page = ordered.Skip(skip).Take(count);
            var parameterDefinitions = new List<object>();
            foreach (var prm in page)
            {
                string dataType = null;
                try { dataType = prm.Definition?.GetDataType()?.TypeId; } catch { dataType = null; }

                parameterDefinitions.Add(new
                {
                    name = prm.Definition?.Name ?? "",
                    id = prm.Id.IntValue(),
                    storageType = prm.StorageType.ToString(),
                    dataType,
                    isReadOnly = prm.IsReadOnly
                });
            }

            return new { ok = true, materialId = material.Id.IntValue(), uniqueId = material.UniqueId, totalCount, parameterDefinitions, inputUnits = MatUnits.InputUnits(), internalUnits = MatUnits.InternalUnits() };
        }
    }

    // 4) Material パラメータ更新（mm/deg 入力→内部変換）
    public class UpdateMaterialParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_material_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null) return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null) return new { ok = false, msg = "paramName または builtInName/builtInId/guid が必要です。" };
            if (!p.TryGetValue("value", out var valToken)) return new { ok = false, msg = "value が必要です。" };
            var param = ParamResolver.ResolveByPayload(material as ARDB.Element, p, out var resolvedBy);
            if (param == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)" };
            if (param.IsReadOnly) return new { ok = false, msg = $"Parameter '{param.Definition?.Name}' は読み取り専用です。" };

            using (var tx = new ARDB.Transaction(doc, $"Set Material Param {param.Definition?.Name ?? "(unknown)"}"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    switch (param.StorageType)
                    {
                        case ARDB.StorageType.String:
                            param.Set(valToken.Value<string>() ?? string.Empty);
                            break;

                        case ARDB.StorageType.Integer:
                            param.Set(valToken.Value<int>());
                            break;

                        case ARDB.StorageType.Double:
                            {
                                ForgeTypeId fdt = null; try { fdt = param.Definition?.GetDataType(); } catch { fdt = null; }
                                double user = valToken.Value<double>(); // mm / mm2 / mm3 / deg
                                double internalVal = MatUnits.ToInternal(user, fdt); // ft / ft2 / ft3 / rad
                                param.Set(internalVal);
                                break;
                            }

                        case ARDB.StorageType.ElementId:
                            param.Set(Autodesk.Revit.DB.ElementIdCompat.From(valToken.Value<int>()));
                            break;

                        default:
                            tx.RollBack(); return new { ok = false, msg = $"Unsupported StorageType: {param.StorageType}" };
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }

            return new { ok = true, materialId = material.Id.IntValue(), uniqueId = material.UniqueId };
        }
    }

    // 5) Material 複製
    public class DuplicateMaterialCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_material";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            string newName = p.Value<string>("newName");

            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null) return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };
            if (string.IsNullOrWhiteSpace(newName)) return new { ok = false, msg = "newName が必要です。" };

            ARDB.Material newMat = null;
            using (var tx = new ARDB.Transaction(doc, "Duplicate Material"))
            {
                tx.Start();
                try { newMat = material.Duplicate(newName) as ARDB.Material; }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = ex.Message }; }
                tx.Commit();
            }

            return new { ok = true, newMaterialId = newMat.Id.IntValue(), newMaterialName = newMat.Name, uniqueId = newMat.UniqueId };
        }
    }

    // 6) Material 名前変更
    public class RenameMaterialCommand : IRevitCommandHandler
    {
        public string CommandName => "rename_material";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            string newName = p.Value<string>("newName");

            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null) return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };
            if (string.IsNullOrWhiteSpace(newName)) return new { ok = false, msg = "newName が必要です。" };

            using (var tx = new ARDB.Transaction(doc, "Rename Material"))
            {
                tx.Start();
                try { material.Name = newName; }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = ex.Message }; }
                tx.Commit();
            }

            return new { ok = true, materialId = material.Id.IntValue(), uniqueId = material.UniqueId, materialName = newName };
        }
    }

    // 7) Material 削除
    public class DeleteMaterialCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_material";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null) return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };

            List<ElementId> deletedIds = null;
            using (var tx = new ARDB.Transaction(doc, "Delete Material"))
            {
                tx.Start();
                try { deletedIds = doc.Delete(material.Id).ToList(); }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = ex.Message }; }
                tx.Commit();
            }

            bool success = deletedIds != null && deletedIds.Contains(material.Id);
            var idList = deletedIds?.Select(x => x.IntValue()).ToList() ?? new List<int>();
            return new { ok = success, materialId = material.Id.IntValue(), uniqueId = material.UniqueId, deletedCount = idList.Count, deletedElementIds = idList };
        }
    }

    // 8) Material 新規作成
    public class CreateMaterialCommand : IRevitCommandHandler
    {
        public string CommandName => "create_material";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            string name = p.Value<string>("name");
            if (string.IsNullOrWhiteSpace(name)) return new { ok = false, msg = "name が必要です。" };

            ElementId newMatId;
            using (var tx = new ARDB.Transaction(doc, "Create Material"))
            {
                tx.Start();
                try { newMatId = ARDB.Material.Create(doc, name); }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = ex.Message }; }
                tx.Commit();
            }

            var mat = doc.GetElement(newMatId) as ARDB.Material;
            return new { ok = true, materialId = newMatId.IntValue(), uniqueId = mat?.UniqueId, materialName = name };
        }
    }

    // 9) 要素の Material パラメータへ適用
    public class ApplyMaterialToElementCommand : IRevitCommandHandler
    {
        public string CommandName => "apply_material_to_element";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // elementId / uniqueId
            Element elem = null;
            int elementId = p.Value<int?>("elementId") ?? 0;
            string uniqueId = p.Value<string>("uniqueId");
            if (elementId > 0) elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId));
            else if (!string.IsNullOrWhiteSpace(uniqueId)) elem = doc.GetElement(uniqueId);
            if (elem == null) return new { ok = false, msg = "要素が見つかりません（elementId/uniqueId）。" };

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName)) return new { ok = false, msg = "paramName が必要です。" };

            int materialId = p.Value<int?>("materialId") ?? 0;
            if (materialId <= 0) return new { ok = false, msg = "materialId が必要です。" };

            var param = elem.LookupParameter(paramName);
            if (param == null) return new { ok = false, msg = $"Parameter not found: {paramName}" };
            if (param.IsReadOnly) return new { ok = false, msg = $"Parameter '{paramName}' は読み取り専用です。" };
            if (param.StorageType != ARDB.StorageType.ElementId) return new { ok = false, msg = $"Parameter '{paramName}' は ElementId 型ではありません。" };

            using (var tx = new ARDB.Transaction(doc, "Apply Material"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try { param.Set(Autodesk.Revit.DB.ElementIdCompat.From(materialId)); }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = ex.Message }; }
                tx.Commit();
            }

            return new { ok = true, elementId = elem.Id.IntValue(), uniqueId = elem.UniqueId, materialId };
        }
    }

    // 10) 要素の Material パラメータ取得
    public class GetElementMaterialCommand : IRevitCommandHandler
    {
        public string CommandName => "get_element_material";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            // elementId / uniqueId
            Element elem = null;
            int elementId = p.Value<int?>("elementId") ?? 0;
            string uniqueId = p.Value<string>("uniqueId");
            if (elementId > 0) elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(elementId));
            else if (!string.IsNullOrWhiteSpace(uniqueId)) elem = doc.GetElement(uniqueId);
            if (elem == null) return new { ok = false, msg = "要素が見つかりません（elementId/uniqueId）。" };

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName)) return new { ok = false, msg = "paramName が必要です。" };

            var param = elem.LookupParameter(paramName);
            if (param == null) return new { ok = false, msg = $"Parameter not found: {paramName}" };
            if (param.StorageType != ARDB.StorageType.ElementId) return new { ok = false, msg = $"Parameter '{paramName}' は ElementId 型ではありません。" };

            var matId = param.AsElementId();
            ARDB.Material mat = null;
            if (matId != null && matId != ElementId.InvalidElementId)
                mat = doc.GetElement(matId) as ARDB.Material;

            return new
            {
                ok = true,
                elementId = elem.Id.IntValue(),
                uniqueId = elem.UniqueId,
                materialId = matId?.IntValue() ?? -1,
                materialName = mat?.Name ?? ""
            };
        }
    }

    // 11) Material に紐づくアセット情報取得
    public class GetMaterialAssetsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_material_assets";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null) return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };

            var structuralId = material.StructuralAssetId;
            var thermalId = material.ThermalAssetId;

            PropertySetElement structuralElem = null;
            PropertySetElement thermalElem = null;
            if (structuralId != null && structuralId != ElementId.InvalidElementId)
                structuralElem = doc.GetElement(structuralId) as PropertySetElement;
            if (thermalId != null && thermalId != ElementId.InvalidElementId)
                thermalElem = doc.GetElement(thermalId) as PropertySetElement;

            object structural = null;
            if (structuralElem != null)
            {
                structural = new
                {
                    assetId = structuralElem.Id.IntValue(),
                    name = structuralElem.Name ?? string.Empty,
                    kind = "structural"
                };
            }

            object thermal = null;
            if (thermalElem != null)
            {
                thermal = new
                {
                    assetId = thermalElem.Id.IntValue(),
                    name = thermalElem.Name ?? string.Empty,
                    kind = "thermal"
                };
            }

            return new
            {
                ok = true,
                materialId = material.Id.IntValue(),
                uniqueId = material.UniqueId,
                structural,
                thermal
            };
        }
    }

    // 12) 物理（構造）アセット一覧
    public class ListPhysicalAssetsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_physical_assets";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            string nameContains = p.Value<string>("nameContains");

            // shape / paging（簡易）
            var shape = p["_shape"] as JObject;
            var page = shape?["page"] as JObject;
            int limit = Math.Max(0, page?.Value<int?>("limit") ?? (p.Value<int?>("count") ?? int.MaxValue));
            int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? (p.Value<int?>("skip") ?? 0));

            // PropertySetElement から StructuralAsset を持つものを抽出（反射で安全に判定）
            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(PropertySetElement))
                .Cast<PropertySetElement>()
                .ToList();

            var list = new List<PropertySetElement>();
            var mi = typeof(PropertySetElement).GetMethod("GetStructuralAsset");
            foreach (var pse in all)
            {
                try
                {
                    var asset = mi?.Invoke(pse, null);
                    if (asset != null)
                        list.Add(pse);
                }
                catch
                {
                    // 反射に失敗した場合は無視
                }
            }

            IEnumerable<PropertySetElement> q = list;
            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(x => (x.Name ?? string.Empty).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            var ordered = q
                .Select(x => new { x, name = x.Name ?? string.Empty, id = x.Id.IntValue() })
                .OrderBy(x => x.name)
                .ThenBy(x => x.id)
                .Select(x => x.x)
                .ToList();

            int totalCount = ordered.Count;
            if (limit == 0)
            {
                return new { ok = true, totalCount };
            }

            var pageSeq = ordered.Skip(skip).Take(limit);
            var assets = pageSeq.Select(x => new
            {
                assetId = x.Id.IntValue(),
                name = x.Name ?? string.Empty,
                kind = "structural"
            }).ToList();

            return new { ok = true, totalCount, assets };
        }
    }

    // 13) 熱アセット一覧
    public class ListThermalAssetsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_thermal_assets";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            string nameContains = p.Value<string>("nameContains");

            var shape = p["_shape"] as JObject;
            var page = shape?["page"] as JObject;
            int limit = Math.Max(0, page?.Value<int?>("limit") ?? (p.Value<int?>("count") ?? int.MaxValue));
            int skip = Math.Max(0, page?.Value<int?>("skip") ?? page?.Value<int?>("offset") ?? (p.Value<int?>("skip") ?? 0));

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(PropertySetElement))
                .Cast<PropertySetElement>()
                .ToList();

            var list = new List<PropertySetElement>();
            var mi = typeof(PropertySetElement).GetMethod("GetThermalAsset");
            foreach (var pse in all)
            {
                try
                {
                    var asset = mi?.Invoke(pse, null);
                    if (asset != null)
                        list.Add(pse);
                }
                catch
                {
                    // 反射に失敗した場合は無視
                }
            }

            IEnumerable<PropertySetElement> q = list;
            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(x => (x.Name ?? string.Empty).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

            var ordered = q
                .Select(x => new { x, name = x.Name ?? string.Empty, id = x.Id.IntValue() })
                .OrderBy(x => x.name)
                .ThenBy(x => x.id)
                .Select(x => x.x)
                .ToList();

            int totalCount = ordered.Count;
            if (limit == 0)
            {
                return new { ok = true, totalCount };
            }

            var pageSeq = ordered.Skip(skip).Take(limit);
            var assets = pageSeq.Select(x => new
            {
                assetId = x.Id.IntValue(),
                name = x.Name ?? string.Empty,
                kind = "thermal"
            }).ToList();

            return new { ok = true, totalCount, assets };
        }
    }

    // 14) Material にアセットを割り当てる
    public class SetMaterialAssetCommand : IRevitCommandHandler
    {
        // エイリアス: set_material_asset / set_material_structural_asset / set_material_thermal_asset
        public string CommandName => "set_material_asset|set_material_structural_asset|set_material_thermal_asset";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null) return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };

            // assetKind が省略された場合はコマンド名から推定（構造/熱 専用コマンド名を許可）
            string assetKind = p.Value<string>("assetKind");
            var method = (cmd.Command ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(assetKind))
            {
                if (method == "set_material_structural_asset") assetKind = "structural";
                else if (method == "set_material_thermal_asset") assetKind = "thermal";
            }
            if (string.IsNullOrWhiteSpace(assetKind)) return new { ok = false, msg = "assetKind が必要です（structural / thermal）。" };
            assetKind = assetKind.ToLowerInvariant();
            bool isStructural = assetKind == "structural";
            bool isThermal = assetKind == "thermal";
            if (!isStructural && !isThermal) return new { ok = false, msg = "assetKind は structural または thermal を指定してください。" };

            int assetId = p.Value<int?>("assetId") ?? 0;
            string assetName = p.Value<string>("assetName");

            PropertySetElement targetAsset = null;
            if (assetId > 0)
            {
                targetAsset = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(assetId)) as PropertySetElement;
            }
            else if (!string.IsNullOrWhiteSpace(assetName))
            {
                // 名前で検索
                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(PropertySetElement))
                    .Cast<PropertySetElement>();

                var mi = typeof(PropertySetElement).GetMethod(isStructural ? "GetStructuralAsset" : "GetThermalAsset");
                foreach (var pse in all)
                {
                    if (!string.Equals(pse.Name ?? string.Empty, assetName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        var asset = mi?.Invoke(pse, null);
                        if (asset != null)
                        {
                            targetAsset = pse;
                            break;
                        }
                    }
                    catch
                    {
                        // ignore and continue
                    }
                }
            }

            if (targetAsset == null)
                return new { ok = false, msg = "指定されたアセットが見つかりません（assetId/assetName）。" };

            using (var tx = new ARDB.Transaction(doc, "Set Material Asset"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    if (isStructural)
                    {
                        material.SetMaterialAspectByPropertySet(MaterialAspect.Structural, targetAsset.Id);
                    }
                    else if (isThermal)
                    {
                        material.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, targetAsset.Id);
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }

            return new
            {
                ok = true,
                materialId = material.Id.IntValue(),
                uniqueId = material.UniqueId,
                assetKind = isStructural ? "structural" : "thermal",
                assetId = targetAsset.Id.IntValue(),
                assetName = targetAsset.Name ?? string.Empty
            };
        }
    }

    // 15) Material の Thermal アセットに熱伝導率(λ)を設定
    public class SetMaterialThermalConductivityCommand : IRevitCommandHandler
    {
        public string CommandName => "set_material_thermal_conductivity";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)(cmd.Params ?? new JObject());

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null) return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };
            if (!p.TryGetValue("value", out var valToken)) return new { ok = false, msg = "value が必要です。" };

            double lambda;
            try { lambda = valToken.Value<double>(); }
            catch { return new { ok = false, msg = "value は数値(double)で指定してください。" }; }

            // 単位（省略時は W/(m・K) とみなす）
            string units = p.Value<string>("units") ?? "W/(m·K)";

            // value / units から W/(m·K) に正規化
            double wPerMK;
            try { wPerMK = ThermalUnitUtil.ToWPerMeterK(lambda, units); }
            catch (Exception ex) { return new { ok = false, msg = ex.Message }; }

            // W/(m·K) → Revit 内部単位
            double internalK;
            try
            {
                internalK = ARDB.UnitUtils.ConvertToInternalUnits(
                    wPerMK,
                    ARDB.UnitTypeId.WattsPerMeterKelvin);
            }
            catch
            {
                // 万が一 UnitTypeId が解決できない/例外の場合は、生値で通す
                internalK = wPerMK;
            }

            // Thermal アセット取得
            var thermalId = material.ThermalAssetId;
            if (thermalId == null || thermalId == ElementId.InvalidElementId)
                return new { ok = false, msg = "Material に Thermal アセットが設定されていません。先に set_material_asset で thermal アセットを割り当ててください。" };

            var pse = doc.GetElement(thermalId) as PropertySetElement;
            if (pse == null) return new { ok = false, msg = "PropertySetElement(Thermal) が取得できません。" };

            var debug = new List<object>();
            int oldAssetId = 0;
            string oldAssetName = string.Empty;
            double? beforeInternal = null;
            double? beforeWPerMK = null;

            try
            {
                oldAssetId = thermalId.IntValue();
                oldAssetName = pse.Name ?? string.Empty;
                var before = pse.GetThermalAsset();
                if (before != null)
                {
                    beforeInternal = before.ThermalConductivity;
                    try
                    {
                        beforeWPerMK = ARDB.UnitUtils.ConvertFromInternalUnits(
                            beforeInternal.Value,
                            ARDB.UnitTypeId.WattsPerMeterKelvin);
                    }
                    catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }

            using (var tx = new ARDB.Transaction(doc, "Set Material Thermal Conductivity"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    // 3段フォールバック:
                    //  1) 既存アセットを直接編集
                    //  2) PropertySetElement を複製して編集し、SetMaterialAspectByPropertySet で再割当
                    //  3) 新規 ThermalAsset + 新規 PropertySetElement を作成して割当

                    string lastError = null;

                    // --- 1) Direct edit ---
                    try
                    {
                        var strategy = "direct_edit";
                        debug.Add(new { strategy, msg = "try" });

                        var asset = pse.GetThermalAsset();
                        if (asset == null)
                            throw new InvalidOperationException("ThermalAsset が取得できません。");

                        asset.ThermalConductivity = internalK;
                        pse.SetThermalAsset(asset);

                        if (TryVerifyThermalConductivity(pse, internalK, out var storedInternal))
                        {
                            tx.Commit();
                            return BuildThermalConductivityResult(
                                material,
                                pse,
                                lambda,
                                units,
                                wPerMK,
                                internalK,
                                storedInternal,
                                strategy,
                                debug,
                                oldAssetId,
                                oldAssetName,
                                beforeWPerMK,
                                beforeInternal);
                        }

                        throw new InvalidOperationException("Verification failed after direct edit.");
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        debug.Add(new { strategy = "direct_edit", ok = false, error = ex.Message });
                    }

                    // --- 2) Duplicate + rebind (SetMaterialAspectByPropertySet) ---
                    try
                    {
                        var strategy = "duplicate_rebind";
                        debug.Add(new { strategy, msg = "try" });

                        var newName = MakeUniquePropertySetName(doc, (pse.Name ?? "ThermalAsset") + "_Custom");
                        var dup = pse.Duplicate(doc, newName) as PropertySetElement;
                        if (dup == null)
                            throw new InvalidOperationException("Thermal asset duplication failed.");

                        var asset2 = dup.GetThermalAsset();
                        if (asset2 == null)
                            throw new InvalidOperationException("複製した ThermalAsset が取得できません。");

                        asset2.ThermalConductivity = internalK;
                        dup.SetThermalAsset(asset2);

                        // IMPORTANT: ThermalAssetId 直代入は反映されないことがあるため、SetMaterialAspectByPropertySet を使用する
                        material.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, dup.Id);

                        if (TryVerifyThermalConductivity(dup, internalK, out var storedInternal2))
                        {
                            tx.Commit();
                            return BuildThermalConductivityResult(
                                material,
                                dup,
                                lambda,
                                units,
                                wPerMK,
                                internalK,
                                storedInternal2,
                                strategy,
                                debug,
                                oldAssetId,
                                oldAssetName,
                                beforeWPerMK,
                                beforeInternal);
                        }

                        throw new InvalidOperationException("Verification failed after duplicate/rebind.");
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        debug.Add(new { strategy = "duplicate_rebind", ok = false, error = ex.Message });
                    }

                    // --- 3) Create new ThermalAsset + PropertySetElement + rebind ---
                    try
                    {
                        var strategy = "create_new";
                        debug.Add(new { strategy, msg = "try" });

                        var srcAsset = pse.GetThermalAsset();
                        var tmt = ThermalMaterialType.Solid;
                        try
                        {
                            if (srcAsset != null) tmt = srcAsset.ThermalMaterialType;
                        }
                        catch { /* ignore */ }

                        var baseName = (material.Name ?? "Material") + "_Thermal";
                        var newPseName = MakeUniquePropertySetName(doc, baseName);
                        var newAsset = new ThermalAsset(newPseName, tmt);
                        if (srcAsset != null)
                            TryCopyThermalAssetWritableProperties(srcAsset, newAsset, debug);

                        newAsset.ThermalConductivity = internalK;

                        var created = PropertySetElement.Create(doc, newAsset);
                        try { created.Name = newPseName; } catch { /* ignore */ }
                        material.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, created.Id);

                        if (TryVerifyThermalConductivity(created, internalK, out var storedInternal3))
                        {
                            tx.Commit();
                            return BuildThermalConductivityResult(
                                material,
                                created,
                                lambda,
                                units,
                                wPerMK,
                                internalK,
                                storedInternal3,
                                strategy,
                                debug,
                                oldAssetId,
                                oldAssetName,
                                beforeWPerMK,
                                beforeInternal);
                        }

                        throw new InvalidOperationException("Verification failed after create-new.");
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        debug.Add(new { strategy = "create_new", ok = false, error = ex.Message });
                    }

                    tx.RollBack();
                    return new
                    {
                        ok = false,
                        msg = lastError ?? "Failed to set ThermalConductivity (all strategies failed).",
                        debug
                    };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message, debug };
                }
            }
        }

        private static bool TryVerifyThermalConductivity(PropertySetElement pse, double expectedInternal, out double storedInternal)
        {
            storedInternal = 0.0;
            try
            {
                var got = pse?.GetThermalAsset();
                if (got == null) return false;
                storedInternal = got.ThermalConductivity;
                return ThermalAssetUtil.NearlyEqual(storedInternal, expectedInternal);
            }
            catch
            {
                return false;
            }
        }

        private static object BuildThermalConductivityResult(
            ARDB.Material material,
            PropertySetElement pse,
            double inputValue,
            string inputUnits,
            double normalizedWPerMK,
            double expectedInternal,
            double storedInternal,
            string strategy,
            List<object> debug,
            int oldAssetId,
            string oldAssetName,
            double? beforeWPerMK,
            double? beforeInternal)
        {
            double storedWPerMK = storedInternal;
            try
            {
                storedWPerMK = ARDB.UnitUtils.ConvertFromInternalUnits(
                    storedInternal,
                    ARDB.UnitTypeId.WattsPerMeterKelvin);
            }
            catch { /* ignore */ }

            return new
            {
                ok = true,
                materialId = material.Id.IntValue(),
                uniqueId = material.UniqueId,
                strategy,
                input = new { value = inputValue, units = inputUnits },
                normalized = new { W_per_mK = normalizedWPerMK },
                stored = new { W_per_mK = Math.Round(storedWPerMK, 6), internalValue = storedInternal },
                expected = new { internalValue = expectedInternal },
                thermalAsset = new { assetId = pse.Id.IntValue(), assetName = pse.Name ?? string.Empty },
                previous = new
                {
                    assetId = oldAssetId,
                    assetName = oldAssetName,
                    ThermalConductivity_W_per_mK = beforeWPerMK.HasValue ? Math.Round(beforeWPerMK.Value, 6) : (double?)null,
                    ThermalConductivity_internal = beforeInternal
                },
                debug
            };
        }

        private static string MakeUniquePropertySetName(ARDB.Document doc, string baseName)
        {
            string Sanitize(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "ThermalAsset";
                var bad = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*', '[', ']', '{', '}', ';' };
                foreach (var c in bad) s = s.Replace(c, '_');
                return s.Trim();
            }

            var safe = Sanitize(baseName ?? "ThermalAsset");

            HashSet<string> names;
            try
            {
                names = new FilteredElementCollector(doc)
                    .OfClass(typeof(PropertySetElement))
                    .Cast<PropertySetElement>()
                    .Select(x => x.Name ?? string.Empty)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (!names.Contains(safe)) return safe;

            int i = 2;
            string name;
            do { name = $"{safe}_{i++}"; } while (names.Contains(name));
            return name;
        }

        private static void TryCopyThermalAssetWritableProperties(ARDB.ThermalAsset src, ARDB.ThermalAsset dst, List<object> debug)
        {
            if (src == null || dst == null) return;

            var t = typeof(ARDB.ThermalAsset);
            var props = t.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

            foreach (var pi in props)
            {
                try
                {
                    if (!pi.CanRead || !pi.CanWrite) continue;
                    if (string.Equals(pi.Name, "Name", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(pi.Name, "ThermalMaterialType", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(pi.Name, "IsValidObject", StringComparison.OrdinalIgnoreCase)) continue;

                    object val;
                    try { val = pi.GetValue(src, null); }
                    catch { continue; }
                    if (val == null) continue;

                    try { pi.SetValue(dst, val, null); }
                    catch (Exception ex)
                    {
                        if (debug != null)
                            debug.Add(new { copyProp = pi.Name, error = ex.Message });
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    // 16) Material のアセット物性値取得（Structural / Thermal）
    public class GetMaterialAssetPropertiesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_material_asset_properties";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null) return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };

            string kindFilter = (p.Value<string>("assetKind") ?? "").ToLowerInvariant(); // "structural" / "thermal" / ""

            object structural = null;
            if (kindFilter == "" || kindFilter == "structural")
            {
                var sid = material.StructuralAssetId;
                if (sid != null && sid != ElementId.InvalidElementId)
                {
                    var pse = doc.GetElement(sid) as PropertySetElement;
                    if (pse != null)
                    {
                        var asset = pse.GetStructuralAsset();
                        if (asset != null)
                        {
                            structural = new
                            {
                                assetId = pse.Id.IntValue(),
                                name = pse.Name ?? string.Empty,
                                kind = "structural",
                                properties = BuildAssetProperties(asset, "structural")
                            };
                        }
                    }
                }
            }

            object thermal = null;
            if (kindFilter == "" || kindFilter == "thermal")
            {
                var tid = material.ThermalAssetId;
                if (tid != null && tid != ElementId.InvalidElementId)
                {
                    var pse = doc.GetElement(tid) as PropertySetElement;
                    if (pse != null)
                    {
                        var asset = pse.GetThermalAsset();
                        if (asset != null)
                        {
                            thermal = new
                            {
                                assetId = pse.Id.IntValue(),
                                name = pse.Name ?? string.Empty,
                                kind = "thermal",
                                properties = BuildAssetProperties(asset, "thermal")
                            };
                        }
                    }
                }
            }

            return new
            {
                ok = true,
                materialId = material.Id.IntValue(),
                uniqueId = material.UniqueId,
                structural,
                thermal
            };
        }

        private static IList<object> BuildAssetProperties(object asset, string assetKind)
        {
            var list = new List<object>();
            if (asset == null) return list;

            var type = asset.GetType();
            var props = type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            foreach (var pi in props)
            {
                if (!pi.CanRead) continue;

                object valueObj;
                try { valueObj = pi.GetValue(asset, null); }
                catch { continue; }

                if (valueObj == null) continue;

                string storageType;
                object valueOut = null;
                string unit = "raw";

                if (valueObj is double d)
                {
                    storageType = "Double";

                    // Thermal / Structural アセットの代表的な物性値については、
                    // Revit 内部単位から人間向け単位に変換して返す。
                    if (assetKind == "thermal")
                    {
                        double outVal = d;
                        try
                        {
                            if (pi.Name == "ThermalConductivity")
                                outVal = ARDB.UnitUtils.ConvertFromInternalUnits(d, ARDB.UnitTypeId.WattsPerMeterKelvin);
                            else if (pi.Name == "Density")
                                outVal = ARDB.UnitUtils.ConvertFromInternalUnits(d, ARDB.UnitTypeId.KilogramsPerCubicMeter);
                            else if (pi.Name == "SpecificHeat")
                                outVal = ARDB.UnitUtils.ConvertFromInternalUnits(d, ARDB.UnitTypeId.JoulesPerKilogramDegreeCelsius);
                            else if (pi.Name == "Permeability")
                                outVal = ARDB.UnitUtils.ConvertFromInternalUnits(d, ARDB.UnitTypeId.NanogramsPerPascalSecondSquareMeter);
                        }
                        catch
                        {
                            // ignore and keep raw internal value
                        }

                        valueOut = Math.Round(outVal, 6);
                        unit = GuessAssetUnit(assetKind, pi.Name);
                    }
                    else if (assetKind == "structural" && pi.Name == "Density")
                    {
                        double outVal = d;
                        try { outVal = ARDB.UnitUtils.ConvertFromInternalUnits(d, ARDB.UnitTypeId.KilogramsPerCubicMeter); } catch { }
                        valueOut = Math.Round(outVal, 6);
                        unit = GuessAssetUnit(assetKind, pi.Name);
                    }
                    else
                    {
                        // それ以外は内部値をそのまま返し、ユニットはヒューリスティックで付与
                        valueOut = Math.Round(d, 6);
                        unit = GuessAssetUnit(assetKind, pi.Name);
                    }
                }
                else if (valueObj is int || valueObj is long)
                {
                    storageType = "Integer";
                    valueOut = valueObj;
                }
                else if (valueObj is bool b)
                {
                    storageType = "Integer";
                    valueOut = b ? 1 : 0;
                }
                else if (valueObj is string s)
                {
                    storageType = "String";
                    valueOut = s;
                }
                else
                {
                    // その他の型は一旦 ToString() だけ返す
                    storageType = valueObj.GetType().Name;
                    valueOut = valueObj.ToString();
                }

                list.Add(new
                {
                    id = pi.Name,
                    name = pi.Name,
                    storageType,
                    unit,
                    value = valueOut
                });
            }
            return list;
        }

        private static string GuessAssetUnit(string assetKind, string propName)
        {
            if (assetKind == "thermal")
            {
                switch (propName)
                {
                    case "ThermalConductivity":
                        return "W/(m·K)";
                    case "Density":
                        return "kg/m3";
                    case "SpecificHeat":
                        return "J/(kg·K)";
                    case "Permeability":
                        return "ng/(Pa·s·m²)";
                    default:
                        return "raw";
                }
            }

            if (assetKind == "structural")
            {
                switch (propName)
                {
                    case "YoungModulusX":
                    case "YoungModulusY":
                    case "YoungModulusZ":
                    case "ShearModulusXY":
                    case "ShearModulusYZ":
                    case "ShearModulusXZ":
                    case "ConcreteCompression":
                        return "Pa";
                    case "Density":
                        return "kg/m3";
                    case "ThermalExpansionCoefficientX":
                    case "ThermalExpansionCoefficientY":
                    case "ThermalExpansionCoefficientZ":
                        return "1/K";
                    default:
                        return "raw";
                }
            }

            return "raw";
        }
    }

    // 17) Material アセット名の変更（構造/熱）
    public class SetMaterialAssetNameCommand : IRevitCommandHandler
    {
        public string CommandName => "set_material_asset_name";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null) return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };

            string assetKind = (p.Value<string>("assetKind") ?? "").ToLowerInvariant(); // "structural" / "thermal"
            if (string.IsNullOrWhiteSpace(assetKind)) return new { ok = false, msg = "assetKind が必要です（structural / thermal）。" };
            bool isStructural = assetKind == "structural";
            bool isThermal = assetKind == "thermal";
            if (!isStructural && !isThermal) return new { ok = false, msg = "assetKind は structural または thermal を指定してください。" };

            string newName = p.Value<string>("newName");
            if (string.IsNullOrWhiteSpace(newName)) return new { ok = false, msg = "newName が必要です。" };

            ElementId assetId = ElementId.InvalidElementId;
            if (isStructural) assetId = material.StructuralAssetId;
            else if (isThermal) assetId = material.ThermalAssetId;

            if (assetId == null || assetId == ElementId.InvalidElementId)
                return new { ok = false, msg = "指定された種別のアセットがマテリアルに割り当てられていません。" };

            var pse = doc.GetElement(assetId) as PropertySetElement;
            if (pse == null) return new { ok = false, msg = "PropertySetElement が取得できません。" };

            using (var tx = new ARDB.Transaction(doc, "Set Material Asset Name"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    // 直接編集を試みる
                    try
                    {
                        if (isStructural)
                        {
                            var sAsset = pse.GetStructuralAsset();
                            if (sAsset == null) throw new InvalidOperationException("StructuralAsset が取得できません。");
                            sAsset.Name = newName;
                            pse.SetStructuralAsset(sAsset);
                        }
                        else
                        {
                            var tAsset = pse.GetThermalAsset();
                            if (tAsset == null) throw new InvalidOperationException("ThermalAsset が取得できません。");
                            tAsset.Name = newName;
                            pse.SetThermalAsset(tAsset);
                        }
                    }
                    catch
                    {
                        // 直接編集できない場合は複製して名前変更
                        var dup = pse.Duplicate(doc, newName) as PropertySetElement;
                        if (dup == null) throw;

                        if (isStructural)
                        {
                            var s2 = dup.GetStructuralAsset();
                            if (s2 == null) throw new InvalidOperationException("複製した StructuralAsset が取得できません。");
                            s2.Name = newName;
                            dup.SetStructuralAsset(s2);
                            // IMPORTANT: 直代入では反映されないことがあるため、SetMaterialAspectByPropertySet を使用する
                            material.SetMaterialAspectByPropertySet(MaterialAspect.Structural, dup.Id);
                        }
                        else
                        {
                            var t2 = dup.GetThermalAsset();
                            if (t2 == null) throw new InvalidOperationException("複製した ThermalAsset が取得できません。");
                            t2.Name = newName;
                            dup.SetThermalAsset(t2);
                            // IMPORTANT: 直代入では反映されないことがあるため、SetMaterialAspectByPropertySet を使用する
                            material.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, dup.Id);
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }

            return new
            {
                ok = true,
                materialId = material.Id.IntValue(),
                uniqueId = material.UniqueId,
                assetKind = isStructural ? "structural" : "thermal",
                newName
            };
        }
    }

    // 18) Material のアセットを複製して同じマテリアルにバインド（構造/熱）
    public class DuplicateMaterialAssetCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_material_asset";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int materialId = p.Value<int?>("materialId") ?? 0;
            string matUid = p.Value<string>("uniqueId");
            ARDB.Material material = null;
            if (materialId > 0) material = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(materialId)) as ARDB.Material;
            else if (!string.IsNullOrWhiteSpace(matUid)) material = doc.GetElement(matUid) as ARDB.Material;

            if (material == null)
                return new { ok = false, msg = "Material が見つかりません（materialId/uniqueId）。" };

            // 既定は thermal。必要に応じて structural も指定可能。
            string assetKind = (p.Value<string>("assetKind") ?? "thermal").ToLowerInvariant();
            bool isStructural = assetKind == "structural";
            bool isThermal = assetKind == "thermal";
            if (!isStructural && !isThermal)
                return new { ok = false, msg = "assetKind は structural または thermal を指定してください。" };

            ElementId assetId = ElementId.InvalidElementId;
            if (isStructural) assetId = material.StructuralAssetId;
            else if (isThermal) assetId = material.ThermalAssetId;

            if (assetId == null || assetId == ElementId.InvalidElementId)
                return new { ok = false, msg = "指定された種別のアセットがマテリアルに割り当てられていません。" };

            var pse = doc.GetElement(assetId) as PropertySetElement;
            if (pse == null)
                return new { ok = false, msg = "PropertySetElement が取得できません。" };

            // newName が指定されていない場合は「元の名前 + _Copy[_n]」で一意になるように生成
            string newName = p.Value<string>("newName");
            if (string.IsNullOrWhiteSpace(newName))
            {
                var baseName = (pse.Name ?? (isStructural ? "StructuralAsset" : "ThermalAsset")) + "_Copy";
                newName = baseName;
                int i = 1;

                var existingNames = new FilteredElementCollector(doc)
                    .OfClass(typeof(PropertySetElement))
                    .Cast<PropertySetElement>()
                    .Select(x => x.Name ?? string.Empty)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                while (existingNames.Contains(newName))
                {
                    newName = $"{baseName}_{i++}";
                }
            }

            int newAssetIdInt;
            using (var tx = new ARDB.Transaction(doc, "Duplicate Material Asset"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    var dup = pse.Duplicate(doc, newName) as PropertySetElement;
                    if (dup == null)
                        throw new InvalidOperationException("アセットの複製に失敗しました。");

                    // アセット自体の値はそのままに、複製側をマテリアルにバインド
                    if (isStructural)
                        material.SetMaterialAspectByPropertySet(MaterialAspect.Structural, dup.Id);
                    else
                        material.SetMaterialAspectByPropertySet(MaterialAspect.Thermal, dup.Id);

                    newAssetIdInt = dup.Id.IntValue();
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }

            return new
            {
                ok = true,
                materialId = material.Id.IntValue(),
                uniqueId = material.UniqueId,
                assetKind = isStructural ? "structural" : "thermal",
                oldAssetId = assetId.IntValue(),
                newAssetId = newAssetIdInt,
                newName
            };
        }
    }
}



