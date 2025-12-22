// ================================================================
// File: Commands/FireProtection/FireProtectionCommands.cs
// Target : Revit 2023 / .NET Framework 4.8 / C# 8
// Policy : すべての単位変換・値整形は RevitMCPAddin.Core.UnitHelper に統一
// Notes  : 入力は mm（角度は deg）、内部は ft/rad。unitsMode=SI|Project|Raw|Both をサポート
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.FireProtection
{
    internal static class FpCommon
    {
        // 防火系と見なすファミリ名（必要に応じて拡張）
        public static readonly string[] FamilyNames = { "FireDoor", "FireWall", "FireDamper", "FireExtinguisher" };

        public static UnitsMode ResolveMode(Document doc, JObject? p) => UnitHelper.ResolveUnitsMode(doc, p);

        public static XYZ MmPoint(JToken? pt)
        {
            if (pt == null) throw new ArgumentException("location は {x,y,z} が必要です。");
            return UnitHelper.MmToXyz(
                pt.Value<double>("x"),
                pt.Value<double>("y"),
                pt.Value<double>("z")
            );
        }

        public static (double x, double y, double z) MmTuple(XYZ p) => UnitHelper.XyzToMm(p);
    }

    // 1. モデル内の防火対策要素 インスタンス一覧取得（位置は mm）
    public class GetFireProtectionInstancesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_fire_protection_instances";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());
            var mode = FpCommon.ResolveMode(doc, p);

            var instances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(fi => fi.Symbol != null && FpCommon.FamilyNames.Contains(fi.Symbol.FamilyName))
                .Select(fi =>
                {
                    var loc = fi.Location as LocationPoint;
                    object? locationMm = null;
                    if (loc != null)
                    {
                        var (x, y, z) = FpCommon.MmTuple(loc.Point);
                        locationMm = new { x = Math.Round(x, 3), y = Math.Round(y, 3), z = Math.Round(z, 3), unit = "mm" };
                    }

                    return new
                    {
                        elementId = fi.Id.IntValue(),
                        uniqueId = fi.UniqueId,
                        familyName = fi.Symbol.FamilyName,
                        typeName = fi.Symbol.Name,
                        levelId = fi.LevelId?.IntValue(),
                        location = locationMm
                    };
                })
                .ToList();

            return new
            {
                ok = true,
                totalCount = instances.Count,
                instances,
                units = UnitHelper.DefaultUnitsMeta(),
                unitsMode = mode.ToString()
            };
        }
    }

    // 2. 防火ファミリタイプを指定位置に配置（入力位置 mm）
    public class CreateFireProtectionInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "create_fire_protection_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var mode = FpCommon.ResolveMode(doc, p);

            int typeId = p.Value<int>("typeId");
            var symbol = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as FamilySymbol
                ?? throw new InvalidOperationException($"Type not found: {typeId}");

            var xyz = FpCommon.MmPoint(p["location"]);

            int levelId = p.Value<int>("levelId");
            var level = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(levelId)) as Level
                ?? throw new InvalidOperationException($"Level not found: {levelId}");

            if (!symbol.IsActive)
            {
                using (var tx0 = new Transaction(doc, "Activate Symbol"))
                { tx0.Start(); symbol.Activate(); tx0.Commit(); }
            }

            FamilyInstance inst;
            using (var tx = new Transaction(doc, "Create Fire Protection"))
            {
                tx.Start();
                inst = doc.Create.NewFamilyInstance(xyz, symbol, level, StructuralType.NonStructural);
                tx.Commit();
            }

            return new
            {
                ok = true,
                elementId = inst.Id.IntValue(),
                units = UnitHelper.DefaultUnitsMeta(),
                unitsMode = mode.ToString()
            };
        }
    }

    // 3. 防火要素インスタンスをオフセット移動（dx/dy/dz は mm）
    public class MoveFireProtectionInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "move_fire_protection_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var mode = FpCommon.ResolveMode(doc, p);

            int eid = p.Value<int>("elementId");
            double dx = UnitHelper.MmToFt(p.Value<double?>("dx") ?? 0);
            double dy = UnitHelper.MmToFt(p.Value<double?>("dy") ?? 0);
            double dz = UnitHelper.MmToFt(p.Value<double?>("dz") ?? 0);

            using (var tx = new Transaction(doc, "Move Fire Protection"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, Autodesk.Revit.DB.ElementIdCompat.From(eid), new XYZ(dx, dy, dz));
                tx.Commit();
            }

            return new { ok = true, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };
        }
    }

    // 4. 防火要素インスタンスを削除
    public class DeleteFireProtectionInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_fire_protection_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            int eid = ((JObject)cmd.Params).Value<int>("elementId");

            using (var tx = new Transaction(doc, "Delete Fire Protection"))
            {
                tx.Start();
                doc.Delete(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                tx.Commit();
            }
            return new { ok = true };
        }
    }

    // 5. インスタンスをコピーして別位置に配置（入力位置 mm）
    public class DuplicateFireProtectionInstanceCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_fire_protection_instance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var mode = FpCommon.ResolveMode(doc, p);

            int eid = p.Value<int>("elementId");
            var orig = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid)) as FamilyInstance
                ?? throw new InvalidOperationException($"Instance not found: {eid}");

            var level = doc.GetElement(orig.LevelId) as Level
                ?? throw new InvalidOperationException($"Level not found: {orig.LevelId.IntValue()}");

            var xyz = FpCommon.MmPoint(p["location"]);

            var symbol = orig.Symbol;
            if (!symbol.IsActive)
            {
                using (var tx0 = new Transaction(doc, "Activate Symbol"))
                { tx0.Start(); symbol.Activate(); tx0.Commit(); }
            }

            FamilyInstance dup;
            using (var tx = new Transaction(doc, "Duplicate Fire Protection"))
            {
                tx.Start();
                dup = doc.Create.NewFamilyInstance(xyz, symbol, level, StructuralType.NonStructural);
                tx.Commit();
            }

            return new
            {
                ok = true,
                newElementId = dup.Id.IntValue(),
                units = UnitHelper.DefaultUnitsMeta(),
                unitsMode = mode.ToString()
            };
        }
    }

    // 6. インスタンスのパラメータ一覧（MapParameterでSI/Project/Raw/Both）
    public class GetFireProtectionParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_fire_protection_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());
            var mode = FpCommon.ResolveMode(doc, p);

            int eid = p.Value<int>("elementId");
            var inst = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid)) as Element
                ?? throw new InvalidOperationException($"Element not found: {eid}");

            bool includeDisplay = p.Value<bool?>("includeDisplay") ?? true;
            bool includeRaw = p.Value<bool?>("includeRaw") ?? true;
            bool includeUnit = p.Value<bool?>("includeUnit") ?? true;
            int siDigits = p.Value<int?>("siDigits") ?? 3;

            var list = inst.Parameters
                .Cast<Parameter>()
                .Where(x => x.StorageType != StorageType.None)
                .Select(pa => UnitHelper.MapParameter(pa, doc, mode, includeDisplay, includeRaw, siDigits, includeUnit))
                .ToList();

            return new
            {
                ok = true,
                elementId = eid,
                totalCount = list.Count,
                parameters = list,
                units = UnitHelper.DefaultUnitsMeta(),
                unitsMode = mode.ToString()
            };
        }
    }

    // 7. インスタンスのパラメータ更新（TrySetParameterByExternalValue + ParamResolver）
    public class SetFireProtectionParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_fire_protection_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var mode = FpCommon.ResolveMode(doc, p);

            int eid = p.Value<int>("elementId");
            var inst = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid)) as Element
                ?? throw new InvalidOperationException($"Element not found: {eid}");

            // Accept builtInId / builtInName / guid / paramName
            if (p["paramName"] == null && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, msg = "paramName または builtInName/builtInId/guid が必要です。" };

            var prm = ParamResolver.ResolveByPayload(inst, p, out var resolvedByInst);
            if (prm == null) return new { ok = false, msg = "Parameter not found (name/builtIn/guid)" };
            if (prm.IsReadOnly) return new { ok = false, msg = $"Parameter '{prm.Definition?.Name}' is read-only." };

            var valueObj = p["value"]?.ToObject<object>();

            using (var tx = new Transaction(doc, "Set FP Param"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                if (!UnitHelper.TrySetParameterByExternalValue(prm, valueObj, out var err))
                {
                    tx.RollBack();
                    return new { ok = false, msg = err ?? "Failed to set parameter." };
                }
                tx.Commit();
            }

            return new { ok = true, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };
        }
    }

    // 8. 利用可能な防火ファミリタイプ一覧
    public class GetFireProtectionTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_fire_protection_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var types = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => FpCommon.FamilyNames.Contains(fs.FamilyName))
                .Select(fs => new
                {
                    typeId = fs.Id.IntValue(),
                    uniqueId = fs.UniqueId,
                    familyName = fs.FamilyName,
                    typeName = fs.Name
                })
                .ToList();

            return new { ok = true, totalCount = types.Count, types, units = UnitHelper.DefaultUnitsMeta() };
        }
    }

    // 9. 防火タイプの複製
    public class DuplicateFireProtectionTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_fire_protection_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int srcType = p.Value<int>("sourceTypeId");
            string newName = p.Value<string>("newTypeName");

            var original = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(srcType)) as FamilySymbol
                ?? throw new InvalidOperationException($"Type not found: {srcType}");

            FamilySymbol dup;
            using (var tx = new Transaction(doc, "Duplicate FP Type"))
            {
                tx.Start();
                dup = original.Duplicate(newName) as FamilySymbol;
                tx.Commit();
            }

            return new { ok = true, newTypeId = dup.Id.IntValue() };
        }
    }

    // 10. 防火タイプを削除
    public class DeleteFireProtectionTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_fire_protection_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            int typeId = ((JObject)cmd.Params).Value<int>("typeId");

            using (var tx = new Transaction(doc, "Delete FP Type"))
            {
                tx.Start();
                doc.Delete(Autodesk.Revit.DB.ElementIdCompat.From(typeId));
                tx.Commit();
            }

            return new { ok = true };
        }
    }

    // 11. インスタンスタイプ変更
    public class ChangeFireProtectionTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_fire_protection_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            int eid = p.Value<int>("elementId");
            int newType = p.Value<int>("newTypeId");

            var inst = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid)) as FamilyInstance
                ?? throw new InvalidOperationException($"Instance not found: {eid}");

            using (var tx = new Transaction(doc, "Change FP Type"))
            {
                tx.Start();
                inst.ChangeTypeId(Autodesk.Revit.DB.ElementIdCompat.From(newType));
                tx.Commit();
            }

            return new { ok = true };
        }
    }

    // 12. 防火タイプのパラメータ一覧（MapParameter）
    public class GetFireProtectionTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_fire_protection_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)(cmd.Params ?? new JObject());
            var mode = FpCommon.ResolveMode(doc, p);

            int tid = p.Value<int>("typeId");
            var symbol = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid)) as FamilySymbol
                ?? throw new InvalidOperationException($"Type not found: {tid}");

            bool includeDisplay = p.Value<bool?>("includeDisplay") ?? true;
            bool includeRaw = p.Value<bool?>("includeRaw") ?? true;
            bool includeUnit = p.Value<bool?>("includeUnit") ?? true;
            int siDigits = p.Value<int?>("siDigits") ?? 3;

            var list = symbol.Parameters
                .Cast<Parameter>()
                .Where(x => x.StorageType != StorageType.None)
                .Select(pa => UnitHelper.MapParameter(pa, doc, mode, includeDisplay, includeRaw, siDigits, includeUnit))
                .ToList();

            return new
            {
                ok = true,
                typeId = tid,
                totalCount = list.Count,
                parameters = list,
                units = UnitHelper.DefaultUnitsMeta(),
                unitsMode = mode.ToString()
            };
        }
    }

    // 13. 防火タイプのパラメータ更新（TrySetParameterByExternalValue + ParamResolver）
    public class SetFireProtectionTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_fire_protection_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            var mode = FpCommon.ResolveMode(doc, p);

            int tid = p.Value<int>("typeId");
            var symbol = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid)) as FamilySymbol
                ?? throw new InvalidOperationException($"Type not found: {tid}");

            // Accept builtInId / builtInName / guid / paramName
            if (p["paramName"] == null && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, msg = "paramName または builtInName/builtInId/guid が必要です。" };

            var param = ParamResolver.ResolveByPayload(symbol, p, out var resolvedByType);
            if (param == null) return new { ok = false, msg = "Parameter not found (name/builtIn/guid)" };
            if (param.IsReadOnly) return new { ok = false, msg = $"Parameter '{param.Definition?.Name}' is read-only" };

            var valueObj = p["value"]?.ToObject<object>();

            using (var tx = new Transaction(doc, "Set FP Type Param"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                if (!UnitHelper.TrySetParameterByExternalValue(param, valueObj, out var err))
                {
                    tx.RollBack();
                    return new { ok = false, msg = err ?? "Failed to set parameter." };
                }
                tx.Commit();
            }

            return new { ok = true, units = UnitHelper.DefaultUnitsMeta(), unitsMode = mode.ToString() };
        }
    }

    // 14. 耐火性能基準チェック（数値はそのまま、結果のみ）
    public class CheckFireRatingComplianceCommand : IRevitCommandHandler
    {
        public string CommandName => "check_fire_rating_compliance";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            int minRating = ((JObject)cmd.Params).Value<int>("minimumRatingMinutes");
            var issues = new List<object>();

            var instances = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .ToList();

            foreach (var fi in instances)
            {
                var param = fi.LookupParameter("FireRating");
                if (param == null || param.StorageType != StorageType.Integer) continue;
                int rating = param.AsInteger();
                if (rating < minRating)
                {
                    issues.Add(new
                    {
                        elementId = fi.Id.IntValue(),
                        rating,
                        required = minRating
                    });
                }
            }

            return new { ok = true, issues };
        }
    }

    // 15. 防火要素のスケジュールビュー生成（そのまま）
    public class GenerateFireProtectionScheduleCommand : IRevitCommandHandler
    {
        public string CommandName => "generate_fire_protection_schedule";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;
            string title = p.Value<string>("title");
            var fields = p["fields"].ToObject<List<string>>();

            ViewSchedule sched;
            using (var tx = new Transaction(doc, "Generate FP Schedule"))
            {
                tx.Start();

                // マルチカテゴリ・スケジュール
                sched = ViewSchedule.CreateSchedule(doc, ElementId.InvalidElementId);
                sched.Name = title;

                // フィールド追加（名前一致）
                var available = sched.Definition.GetSchedulableFields();
                foreach (string f in fields)
                {
                    var sf = available.FirstOrDefault(x => x.GetName(doc).Equals(f, StringComparison.OrdinalIgnoreCase));
                    if (sf != null) sched.Definition.AddField(sf);
                }

                tx.Commit();
            }

            return new { ok = true, scheduleViewId = sched.Id.IntValue() };
        }
    }
}


