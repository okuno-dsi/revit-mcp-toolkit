// ================================================================
// Revision Cloud Look&Feel (type-side) for Revit 2023-safe
//  - List types / Read type params / Set type param / Change cloud's type
//  - Use ElementType filtered by BuiltInCategory.OST_RevisionClouds
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

namespace RevitMCPAddin.Commands.RevisionCloud
{
    internal static class RcUnits
    {
        public static double MmToFt(double mm) => ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double ToMm(double ft) => ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
    }

    internal static class RcFind
    {
        // Revision Cloud の ElementType 一覧を取得（2023互換）
        public static IList<ElementType> GetCloudTypes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType))
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .Where(et => et.Category != null
                          && et.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RevisionClouds)
                .OrderBy(et => et.Name)
                .ToList();
        }

        public static ElementType GetCloudTypeById(Document doc, int typeId)
        {
            var et = doc.GetElement(new ElementId(typeId)) as ElementType;
            if (et != null && et.Category != null
                && et.Category.Id.IntegerValue == (int)BuiltInCategory.OST_RevisionClouds)
                return et;
            return null;
        }
    }

    public class GetRevisionCloudTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_revision_cloud_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            string nameContains = p.Value<string>("nameContains");

            var all = RcFind.GetCloudTypes(doc);
            if (!string.IsNullOrWhiteSpace(nameContains))
                all = all.Where(t => t.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            int totalCount = all.Count;
            if (skip == 0 && p.ContainsKey("count") && count == 0)
                return new { ok = true, totalCount };

            var types = all.Skip(skip).Take(count)
                           .Select(t => new { typeId = t.Id.IntegerValue, typeName = t.Name })
                           .ToList();
            return new { ok = true, totalCount, types };
        }
    }

    public class GetRevisionCloudTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_revision_cloud_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int typeId = p.Value<int>("typeId");
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;

            var et = RcFind.GetCloudTypeById(doc, typeId);
            if (et == null) return new { ok = false, msg = $"Revision Cloud Type not found: {typeId}" };

            var allParams = et.Parameters
                .Cast<Parameter>()
                .Where(pa => pa.StorageType != StorageType.None)
                .Select(pa => new
                {
                    name = pa.Definition.Name,
                    id = pa.Id.IntegerValue,
                    storageType = pa.StorageType.ToString(),
                    isReadOnly = pa.IsReadOnly,
                    value = pa.AsValueString() ?? pa.AsString()
                          ?? (pa.StorageType == StorageType.Integer ? pa.AsInteger().ToString()
                           : pa.StorageType == StorageType.ElementId ? pa.AsElementId().IntegerValue.ToString()
                           : string.Empty)
                })
                .ToList();

            int totalCount = allParams.Count;
            if (skip == 0 && p.ContainsKey("count") && count == 0)
                return new { ok = true, typeId, totalCount };

            var parameters = allParams.Skip(skip).Take(count).ToList();
            return new { ok = true, typeId, totalCount, parameters };
        }
    }

    public class SetRevisionCloudTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_revision_cloud_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int typeId = p.Value<int>("typeId");
            string paramName = p.Value<string>("paramName");
            var valTok = p["value"];

            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null) return new { ok = false, msg = "paramName または builtInName/builtInId/guid は必須です。" };
            if (valTok == null) return new { ok = false, msg = "value は必須です。" };

            var et = RcFind.GetCloudTypeById(doc, typeId);
            if (et == null) return new { ok = false, msg = $"Revision Cloud Type not found: {typeId}" };

            var param = ParamResolver.ResolveByPayload(et, p, out var resolvedBy);
            if (param == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)" };
            if (param.IsReadOnly) return new { ok = false, msg = $"Parameter '{param.Definition?.Name}' is read-only." };

            using (var tx = new Transaction(doc, $"Set RC Type Param '{paramName}'"))
            {
                tx.Start();
                try
                {
                    switch (param.StorageType)
                    {
                        case StorageType.String:
                            param.Set(valTok.Type == JTokenType.Null ? string.Empty : valTok.ToString());
                            break;
                        case StorageType.Integer:
                            param.Set(valTok.Value<int>());
                            break;
                        case StorageType.Double:
                            // 長さ想定：mm→ft
                            param.Set(RcUnits.MmToFt(valTok.Value<double>()));
                            break;
                        case StorageType.ElementId:
                            param.Set(new ElementId(valTok.Value<int>()));
                            break;
                        default:
                            tx.RollBack();
                            return new { ok = false, msg = $"Unsupported StorageType: {param.StorageType}" };
                    }
                    tx.Commit();
                    return new { ok = true };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }
        }
    }

    public class ChangeRevisionCloudTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_revision_cloud_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            int elemId = p.Value<int>("elementId");
            int typeId = p.Value<int>("typeId");

            var cloud = doc.GetElement(new ElementId(elemId)) as Autodesk.Revit.DB.RevisionCloud;
            if (cloud == null) return new { ok = false, msg = $"Revision Cloud not found: {elemId}" };

            var et = RcFind.GetCloudTypeById(doc, typeId);
            if (et == null) return new { ok = false, msg = $"Revision Cloud Type not found: {typeId}" };

            using (var tx = new Transaction(doc, "Change Revision Cloud Type"))
            {
                tx.Start();
                try
                {
                    cloud.ChangeTypeId(et.Id);
                    tx.Commit();
                    return new { ok = true, elementId = elemId, newTypeId = typeId };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }
        }
    }
}
