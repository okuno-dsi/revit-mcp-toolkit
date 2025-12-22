// File: RevitMCPAddin/Commands/ElementOps/StructuralFrame/StructuralFrameCommands.cs
// Revit 2023 / .NET Framework 4.8 / C# 8
// Notes:
//  - 単位変換は UnitHelper に統一（入力は mm / 角度は deg で扱い、内部は ft/rad）
//  - Double パラメータの取得は Spec に基づいて mm/m2/m3/deg に正規化
//  - Double パラメータの設定は UnitHelper.TrySetParameterFromSi(...) を使用（Spec で内部値へ）
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.StructuralFrame
{
    // 1. Create Structural Frame
    public class CreateStructuralFrameCommand : IRevitCommandHandler
    {
        public string CommandName => "create_structural_frame";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            // Level: id または name
            Level level = null;
            if (p.TryGetValue("levelId", out var lid))
                level = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(lid.Value<int>())) as Level;
            if (level == null && p.TryGetValue("levelName", out var lname))
                level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(l => string.Equals(l.Name, lname.Value<string>(), StringComparison.OrdinalIgnoreCase));
            if (level == null) return new { ok = false, msg = "Level が見つかりません。" };

            // Symbol: typeId / typeName(+familyName)
            FamilySymbol symbol = null;
            if (p.TryGetValue("typeId", out var tid))
            {
                symbol = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid.Value<int>())) as FamilySymbol;
            }
            else
            {
                var tn = p.Value<string>("typeName");
                var fn = p.Value<string>("familyName"); // 任意
                if (!string.IsNullOrWhiteSpace(tn))
                {
                    var q = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .Cast<FamilySymbol>()
                        .Where(s => string.Equals(s.Name, tn, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(fn))
                        q = q.Where(s => string.Equals(s.Family?.Name, fn, StringComparison.OrdinalIgnoreCase));
                    symbol = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
                }
            }
            if (symbol == null) return new { ok = false, msg = "Structural frame type が見つかりません。" };

            // Start/End (mm → internal ft)
            var s = p.Value<JObject>("start"); var e = p.Value<JObject>("end");
            if (s == null || e == null) return new { ok = false, msg = "start/end が必要です（mm）。" };

            var startPt = UnitHelper.MmToInternalXYZ(s.Value<double>("x"), s.Value<double>("y"), s.Value<double>("z"));
            var endPt = UnitHelper.MmToInternalXYZ(e.Value<double>("x"), e.Value<double>("y"), e.Value<double>("z"));

            using (var tx = new Transaction(doc, "Create Structural Frame"))
            {
                tx.Start();
                if (!symbol.IsActive) symbol.Activate();
                var line = Line.CreateBound(startPt, endPt);
                var frame = doc.Create.NewFamilyInstance(line, symbol, level, StructuralType.Beam);
                tx.Commit();

                return new
                {
                    ok = true,
                    elementId = frame.Id.IntValue(),
                    uniqueId = frame.UniqueId,
                    typeId = frame.GetTypeId().IntValue(),
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }
        }
    }

    // 2. Duplicate Structural Frame Type
    public class DuplicateStructuralFrameTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "duplicate_structural_frame_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            // typeId / typeName(+familyName)
            FamilySymbol original = null;
            if (p.TryGetValue("typeId", out var tid))
            {
                original = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid.Value<int>())) as FamilySymbol;
            }
            else
            {
                var tn = p.Value<string>("typeName");
                var fn = p.Value<string>("familyName");
                if (!string.IsNullOrWhiteSpace(tn))
                {
                    var q = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .Cast<FamilySymbol>()
                        .Where(s => string.Equals(s.Name, tn, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(fn))
                        q = q.Where(s => string.Equals(s.Family?.Name, fn, StringComparison.OrdinalIgnoreCase));
                    original = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
                }
            }
            if (original == null) return new { ok = false, msg = "元タイプが見つかりません。" };

            var newName = p.Value<string>("newTypeName");
            if (string.IsNullOrWhiteSpace(newName))
                return new { ok = false, msg = "newTypeName が必要です。" };

            using (var tx = new Transaction(doc, "Duplicate Structural Frame Type"))
            {
                tx.Start();
                var dup = original.Duplicate(newName) as FamilySymbol;
                if (dup == null) { tx.RollBack(); return new { ok = false, msg = "タイプの複製に失敗しました。" }; }
                tx.Commit();
                return new
                {
                    ok = true,
                    originalId = original.Id.IntValue(),
                    newTypeId = dup.Id.IntValue(),
                    newTypeName = dup.Name
                };
            }
        }
    }

    // 3. Get Structural Frames (filters + paging + namesOnly)
    public class GetStructuralFramesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_frames";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            // 単一指定
            int targetEid = p.Value<int?>("elementId") ?? 0;
            string targetUid = p.Value<string>("uniqueId");

            // フィルタ
            int filterTypeId = p.Value<int?>("typeId") ?? 0;
            string filterTypeName = p.Value<string>("typeName");
            string filterFamilyName = p.Value<string>("familyName");
            int filterLevelId = p.Value<int?>("levelId") ?? 0;
            string filterLevelName = p.Value<string>("levelName");
            string nameContains = p.Value<string>("nameContains");

            var all = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            // type/level 辞書
            var typeIds = all.Select(x => x.GetTypeId().IntValue()).Distinct().ToList();
            var typeMap = new Dictionary<int, FamilySymbol>(typeIds.Count);
            foreach (var id in typeIds)
                typeMap[id] = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as FamilySymbol;

            var levelIds = all.Select(x => x.LevelId.IntValue()).Distinct().ToList();
            var levelMap = new Dictionary<int, Level>(levelIds.Count);
            foreach (var id in levelIds)
                levelMap[id] = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as Level;

            IEnumerable<FamilyInstance> q = all;

            // 単一ターゲット優先
            if (targetEid > 0 || !string.IsNullOrWhiteSpace(targetUid))
            {
                FamilyInstance target = null;
                if (targetEid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(targetEid)) as FamilyInstance;
                else target = doc.GetElement(targetUid) as FamilyInstance;

                q = target == null ? Enumerable.Empty<FamilyInstance>() : new[] { target };
            }

            // typeId / typeName(+familyName)
            if (filterTypeId > 0)
                q = q.Where(x => x.GetTypeId().IntValue() == filterTypeId);

            if (!string.IsNullOrWhiteSpace(filterTypeName))
            {
                q = q.Where(x =>
                {
                    FamilySymbol sym = null;
                    typeMap.TryGetValue(x.GetTypeId().IntValue(), out sym);
                    if (sym == null) return false;
                    var ok = string.Equals(sym.Name, filterTypeName, StringComparison.OrdinalIgnoreCase);
                    if (!ok) return false;
                    if (!string.IsNullOrWhiteSpace(filterFamilyName))
                        return string.Equals(sym.Family?.Name, filterFamilyName, StringComparison.OrdinalIgnoreCase);
                    return true;
                });
            }

            // levelId / levelName
            if (filterLevelId > 0)
                q = q.Where(x => x.LevelId.IntValue() == filterLevelId);

            if (!string.IsNullOrWhiteSpace(filterLevelName))
            {
                q = q.Where(x =>
                {
                    Level lv; levelMap.TryGetValue(x.LevelId.IntValue(), out lv);
                    return lv != null && string.Equals(lv.Name, filterLevelName, StringComparison.OrdinalIgnoreCase);
                });
            }

            // nameContains（インスタンス名 or タイプ名）
            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                q = q.Where(x =>
                {
                    var instName = x.Name ?? string.Empty;
                    if (instName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    FamilySymbol sym; typeMap.TryGetValue(x.GetTypeId().IntValue(), out sym);
                    var tName = sym?.Name ?? string.Empty;
                    return tName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            }

            // 並び：typeName -> elementId
            var ordered = q
                .Select(x =>
                {
                    FamilySymbol sym; typeMap.TryGetValue(x.GetTypeId().IntValue(), out sym);
                    string tName = sym?.Name ?? "";
                    return new { x, tName };
                })
                .OrderBy(a => a.tName)
                .ThenBy(a => a.x.Id.IntValue())
                .Select(a => a.x)
                .ToList();

            int totalCount = ordered.Count;

            // メタのみ
            if (count == 0)
            {
                return new
                {
                    ok = true,
                    totalCount,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            // namesOnly
            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(x =>
                {
                    if (!string.IsNullOrEmpty(x.Name)) return x.Name;
                    FamilySymbol sym; typeMap.TryGetValue(x.GetTypeId().IntValue(), out sym);
                    return sym?.Name ?? string.Empty;
                }).ToList();

                return new
                {
                    ok = true,
                    totalCount,
                    names,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            // フル明細（XYZ → mm へ）
            var page = ordered.Skip(skip).Take(count).ToList();
            var frames = page.Select(e =>
            {
                FamilySymbol sym; typeMap.TryGetValue(e.GetTypeId().IntValue(), out sym);
                string typeName = sym?.Name ?? string.Empty;
                string familyName = sym?.Family?.Name ?? string.Empty;

                Level lv; levelMap.TryGetValue(e.LevelId.IntValue(), out lv);
                string levelName = lv?.Name ?? string.Empty;

                (double x, double y, double z)? sMm = null, eMm = null;
                if (e.Location is LocationCurve lc && lc.Curve != null)
                {
                    var sPt = lc.Curve.GetEndPoint(0);
                    var ePt = lc.Curve.GetEndPoint(1);
                    var s3 = UnitHelper.XyzToMm(sPt);
                    var e3 = UnitHelper.XyzToMm(ePt);
                    sMm = (Math.Round(s3.x, 3), Math.Round(s3.y, 3), Math.Round(s3.z, 3));
                    eMm = (Math.Round(e3.x, 3), Math.Round(e3.y, 3), Math.Round(e3.z, 3));
                }
                else if (e.Location is LocationPoint lp && lp.Point != null)
                {
                    var c = UnitHelper.XyzToMm(lp.Point);
                    sMm = (Math.Round(c.x, 3), Math.Round(c.y, 3), Math.Round(c.z, 3));
                    eMm = sMm;
                }

                return new
                {
                    elementId = e.Id.IntValue(),
                    uniqueId = e.UniqueId,
                    typeId = e.GetTypeId().IntValue(),
                    typeName,
                    familyName,
                    levelId = e.LevelId.IntValue(),
                    levelName,
                    start = sMm != null ? new { x = sMm.Value.x, y = sMm.Value.y, z = sMm.Value.z } : null,
                    end = eMm != null ? new { x = eMm.Value.x, y = eMm.Value.y, z = eMm.Value.z } : null
                };
            }).ToList();

            return new
            {
                ok = true,
                totalCount,
                structuralFrames = frames,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }

    // 4. Move Structural Frame
    public class MoveStructuralFrameCommand : IRevitCommandHandler
    {
        public string CommandName => "move_structural_frame";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element target = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            if (target == null) return new { ok = false, msg = "要素が見つかりません（elementId/uniqueId）。" };

            var off = p.Value<JObject>("offset");
            if (off == null) return new { ok = false, msg = "offset が必要です（mm）。" };
            var offset = UnitHelper.MmToInternalXYZ(off.Value<double>("x"), off.Value<double>("y"), off.Value<double>("z"));

            using (var tx = new Transaction(doc, "Move Structural Frame"))
            {
                tx.Start();
                ElementTransformUtils.MoveElement(doc, target.Id, offset);
                tx.Commit();
            }
            return new
            {
                ok = true,
                elementId = target.Id.IntValue(),
                uniqueId = target.UniqueId,
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }
    }

    // 5. Delete Structural Frame
    public class DeleteStructuralFrameCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_structural_frame";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element target = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            if (target == null) return new { ok = false, msg = "要素が見つかりません（elementId/uniqueId）。" };

            ICollection<ElementId> deleted = null;
            using (var tx = new Transaction(doc, "Delete Structural Frame"))
            {
                tx.Start();
                deleted = doc.Delete(target.Id);
                tx.Commit();
            }
            return new { ok = true, deletedCount = deleted?.Count ?? 0 };
        }
    }

    // 6. Update Structural Frame Geometry
    public class UpdateStructuralFrameGeometryCommand : IRevitCommandHandler
    {
        public string CommandName => "update_structural_frame_geometry";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element target = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            var fr = target as FamilyInstance;
            if (fr == null) return new { ok = false, msg = "FamilyInstance が必要です。" };

            var lc = fr.Location as LocationCurve;
            if (lc == null) return new { ok = false, msg = "LocationCurve を持たない要素です。" };

            var s = p.Value<JObject>("start"); var e = p.Value<JObject>("end");
            if (s == null || e == null) return new { ok = false, msg = "start/end が必要です（mm）。" };

            var newLine = Line.CreateBound(
                UnitHelper.MmToInternalXYZ(s.Value<double>("x"), s.Value<double>("y"), s.Value<double>("z")),
                UnitHelper.MmToInternalXYZ(e.Value<double>("x"), e.Value<double>("y"), e.Value<double>("z")));

            using (var tx = new Transaction(doc, "Update Structural Frame Geometry"))
            {
                tx.Start();
                lc.Curve = newLine;
                tx.Commit();
            }
            return new { ok = true, elementId = fr.Id.IntValue(), uniqueId = fr.UniqueId };
        }
    }

    // 7. Get Single Parameter (instance)
    public class GetStructuralFrameParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_frame_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element target = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            var fr = target as FamilyInstance;
            if (fr == null) return new { ok = false, msg = "FamilyInstance が見つかりません。" };

            var paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName)) return new { ok = false, msg = "paramName が必要です。" };

            var param = fr.LookupParameter(paramName);
            if (param == null) return new { ok = false, msg = $"Parameter not found: {paramName}" };

            ForgeTypeId fdt = null; string dataType = null;
            try { fdt = param.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

            object value = null;
            try
            {
                switch (param.StorageType)
                {
                    case StorageType.Double:
                        if (fdt != null) value = UnitHelper.ConvertDoubleBySpec(param.AsDouble(), fdt, 3);
                        else value = Math.Round(param.AsDouble(), 3);
                        break;
                    case StorageType.Integer: value = param.AsInteger(); break;
                    case StorageType.String: value = param.AsString() ?? string.Empty; break;
                    case StorageType.ElementId: value = param.AsElementId()?.IntValue() ?? -1; break;
                }
            }
            catch { value = null; }

            return new
            {
                ok = true,
                elementId = fr.Id.IntValue(),
                uniqueId = fr.UniqueId,
                name = param.Definition?.Name ?? paramName,
                id = param.Id.IntValue(),
                storageType = param.StorageType.ToString(),
                isReadOnly = param.IsReadOnly,
                dataType,
                value,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }

    // 8. Update Single Parameter (instance)
    public class UpdateStructuralFrameParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_structural_frame_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element target = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            var fr = target as FamilyInstance;
            if (fr == null) return new { ok = false, msg = "FamilyInstance が見つかりません。" };

            var paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, msg = "paramName または builtInName/builtInId/guid のいずれかが必要です。" };
            var param = ParamResolver.ResolveByPayload(fr, p, out var resolvedBy);
            if (param == null) return new { ok = false, msg = $"Parameter not found (name/builtIn/guid)" };
            if (param.IsReadOnly) return new { ok = false, msg = $"Parameter '{paramName}' は読み取り専用です。" };
            if (!p.TryGetValue("value", out var valTok)) return new { ok = false, msg = "value が必要です。" };

            // オプション: レベルオフセット変更をジオメトリの移動としても反映する
            // applyOffsetAsMove=true かつ 対象パラメータが始端/終端レベル オフセットの場合のみ有効
            bool applyOffsetAsMove = p.Value<bool?>("applyOffsetAsMove") ?? false;
            double? deltaOffsetMm = null;
            if (applyOffsetAsMove && param.StorageType == StorageType.Double)
            {
                try
                {
                    var defName = param.Definition?.Name ?? paramName ?? string.Empty;
                    if (defName == "始端レベル オフセット" || defName == "終端レベル オフセット")
                    {
                        // 現在値（内部 ft → mm）と新しい SI 値との差分を算出
                        double oldInternal = param.AsDouble();
                        double oldMm = UnitUtils.ConvertFromInternalUnits(oldInternal, UnitTypeId.Millimeters);
                        double newMm = valTok.Value<double>();
                        var diff = newMm - oldMm;
                        if (Math.Abs(diff) > 1e-6)
                            deltaOffsetMm = diff;
                    }
                }
                catch
                {
                    // ここでの例外はジオメトリ移動をスキップし、従来どおりパラメータ更新のみを行う
                    deltaOffsetMm = null;
                }
            }

            using (var tx = new Transaction(doc, $"Set {paramName}"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                if (!UnitHelper.TrySetParameterFromSi(param, valTok, out var reason))
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"Failed to set parameter: {reason}" };
                }

                // 必要に応じて、レベルオフセット変更量に応じて梁全体を Z 方向に移動
                if (deltaOffsetMm.HasValue && Math.Abs(deltaOffsetMm.Value) > 1e-6)
                {
                    try
                    {
                        var offset = UnitHelper.MmToInternalXYZ(0.0, 0.0, deltaOffsetMm.Value);
                        ElementTransformUtils.MoveElement(doc, fr.Id, offset);
                    }
                    catch (Exception ex)
                    {
                        // 位置移動に失敗してもパラメータ更新自体は成功させる
                        RevitMCPAddin.Core.RevitLogger.Warn($"update_structural_frame_parameter: move by offset failed for element {fr.Id.IntValue()}: {ex.Message}");
                    }
                }

                tx.Commit();
            }

            return new { ok = true, elementId = fr.Id.IntValue(), uniqueId = fr.UniqueId };
        }
    }

    // 9. Get All Instance Parameters (instance)
    public class GetStructuralFrameParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_frame_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;
            Element target = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) target = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) target = doc.GetElement(uid);
            var fr = target as FamilyInstance;
            if (fr == null) return new { ok = false, msg = "FamilyInstance が見つかりません。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ordered = (fr.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa).ToList();

            int totalCount = ordered.Count;

            if (count == 0)
            {
                return new
                {
                    ok = true,
                    elementId = fr.Id.IntValue(),
                    uniqueId = fr.UniqueId,
                    totalCount,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(pa => pa?.Definition?.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    elementId = fr.Id.IntValue(),
                    uniqueId = fr.UniqueId,
                    totalCount,
                    names,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            var page = ordered.Skip(skip).Take(count);
            var list = new List<object>();
            foreach (var pa in page)
            {
                if (pa == null) continue;
                ForgeTypeId fdt = null; string dataType = null;
                try { fdt = pa.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

                object val = null;
                try
                {
                    switch (pa.StorageType)
                    {
                        case StorageType.Double:
                            if (fdt != null) val = UnitHelper.ConvertDoubleBySpec(pa.AsDouble(), fdt, 3);
                            else val = Math.Round(pa.AsDouble(), 3);
                            break;
                        case StorageType.Integer: val = pa.AsInteger(); break;
                        case StorageType.String: val = pa.AsString() ?? string.Empty; break;
                        case StorageType.ElementId: val = pa.AsElementId()?.IntValue() ?? -1; break;
                    }
                }
                catch { val = null; }

                list.Add(new
                {
                    name = pa.Definition?.Name ?? "",
                    id = pa.Id.IntValue(),
                    storageType = pa.StorageType.ToString(),
                    isReadOnly = pa.IsReadOnly,
                    dataType,
                    value = val
                });
            }

            return new
            {
                ok = true,
                elementId = fr.Id.IntValue(),
                uniqueId = fr.UniqueId,
                totalCount,
                parameters = list,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }

    // 10. Get Type Parameters (type or instance→type)
    public class GetStructuralFrameTypeParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_frame_type_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            // typeId / typeName(+familyName) / elementId / uniqueId
            FamilySymbol sym = null;
            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");

            if (typeId > 0)
            {
                sym = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as FamilySymbol;
            }
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                var q = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .Cast<FamilySymbol>()
                    .Where(s => string.Equals(s.Name, typeName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(familyName))
                    q = q.Where(s => string.Equals(s.Family?.Name, familyName, StringComparison.OrdinalIgnoreCase));
                sym = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
            }
            else
            {
                Element inst = null;
                int eid = p.Value<int?>("elementId") ?? 0;
                string uid = p.Value<string>("uniqueId");
                if (eid > 0) inst = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
                else if (!string.IsNullOrWhiteSpace(uid)) inst = doc.GetElement(uid);
                var fi = inst as FamilyInstance;
                if (fi != null) sym = doc.GetElement(fi.GetTypeId()) as FamilySymbol;
            }

            if (sym == null) return new { ok = false, msg = "Structural frame type が見つかりません。" };

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
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
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
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            var page = ordered.Skip(skip).Take(count);
            var list = new List<object>();
            foreach (var pa in page)
            {
                if (pa == null) continue;
                ForgeTypeId fdt = null; string dataType = null;
                try { fdt = pa.Definition?.GetDataType(); dataType = fdt?.TypeId; } catch { dataType = null; }

                object val = null;
                try
                {
                    switch (pa.StorageType)
                    {
                        case StorageType.Double:
                            if (fdt != null) val = UnitHelper.ConvertDoubleBySpec(pa.AsDouble(), fdt, 3);
                            else val = Math.Round(pa.AsDouble(), 3);
                            break;
                        case StorageType.Integer: val = pa.AsInteger(); break;
                        case StorageType.String: val = pa.AsString() ?? string.Empty; break;
                        case StorageType.ElementId: val = pa.AsElementId()?.IntValue() ?? -1; break;
                    }
                }
                catch { val = null; }

                list.Add(new
                {
                    name = pa.Definition?.Name ?? "",
                    id = pa.Id.IntValue(),
                    storageType = pa.StorageType.ToString(),
                    isReadOnly = pa.IsReadOnly,
                    dataType,
                    value = val
                });
            }

            return new
            {
                ok = true,
                scope = "type",
                typeId = sym.Id.IntValue(),
                uniqueId = sym.UniqueId,
                totalCount,
                parameters = list,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }

    // 11. Change Type
    public class ChangeStructuralFrameTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "change_structural_frame_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            Element instElm = null;
            int eid = p.Value<int?>("elementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) instElm = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            else if (!string.IsNullOrWhiteSpace(uid)) instElm = doc.GetElement(uid);

            var fr = instElm as FamilyInstance;
            if (fr == null) return new { ok = false, msg = "FamilyInstance が見つかりません。" };

            // new type: typeId / typeName(+familyName)
            FamilySymbol newSym = null;
            int typeId = p.Value<int?>("typeId") ?? 0;
            if (typeId > 0)
            {
                newSym = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as FamilySymbol;
            }
            else
            {
                var tn = p.Value<string>("typeName");
                var fn = p.Value<string>("familyName");
                if (!string.IsNullOrWhiteSpace(tn))
                {
                    var q = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .Cast<FamilySymbol>()
                        .Where(s => string.Equals(s.Name, tn, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(fn))
                        q = q.Where(s => string.Equals(s.Family?.Name, fn, StringComparison.OrdinalIgnoreCase));
                    newSym = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
                }
            }
            if (newSym == null) return new { ok = false, msg = "新しいタイプが見つかりません。" };

            using (var tx = new Transaction(doc, "Change Structural Frame Type"))
            {
                tx.Start();
                fr.ChangeTypeId(newSym.Id);
                tx.Commit();
            }
            return new { ok = true, elementId = fr.Id.IntValue(), uniqueId = fr.UniqueId, typeId = fr.GetTypeId().IntValue() };
        }
    }

    // 12. Get All Structural Frame Types
    public class GetStructuralFrameTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_structural_frame_types";

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
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .Cast<FamilySymbol>()
                .ToList();

            IEnumerable<FamilySymbol> q = allSyms;

            if (!string.IsNullOrWhiteSpace(filterTypeName))
            {
                q = q.Where(s => string.Equals(s.Name, filterTypeName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(filterFamilyName))
                    q = q.Where(s => string.Equals(s.Family?.Name, filterFamilyName, StringComparison.OrdinalIgnoreCase));
            }

            if (filterFamilyId > 0)
                q = q.Where(s => s.Family != null && s.Family.Id.IntValue() == filterFamilyId);

            if (!string.IsNullOrWhiteSpace(filterFamilyName))
                q = q.Where(s => string.Equals(s.Family?.Name, filterFamilyName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(nameContains))
                q = q.Where(s => (s.Name ?? string.Empty).IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0);

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

            int total = ordered.Count;

            if (count == 0)
            {
                return new
                {
                    ok = true,
                    totalCount = total,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
                };
            }

            if (namesOnly)
            {
                var names = ordered.Skip(skip).Take(count).Select(s => s.Name ?? "").ToList();
                return new
                {
                    ok = true,
                    totalCount = total,
                    names,
                    inputUnits = UnitHelper.DefaultUnitsMeta(),
                    internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
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
                totalCount = total,
                types = list,
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }

    // 13. List Parameter Definitions (instance or type)
    public class ListStructuralFrameParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "list_structural_frame_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            // elementId/uniqueId or typeId/typeName(+familyName)
            Element elem = null;
            if (p.TryGetValue("elementId", out var fid))
            {
                elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(fid.Value<int>()));
            }
            else if (p.TryGetValue("uniqueId", out var uidTok))
            {
                elem = doc.GetElement(uidTok.Value<string>());
            }
            else if (p.TryGetValue("typeId", out var tid))
            {
                elem = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tid.Value<int>()));
            }
            else if (!string.IsNullOrWhiteSpace(p.Value<string>("typeName")))
            {
                var tn = p.Value<string>("typeName");
                var fn = p.Value<string>("familyName");
                var q = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .Cast<FamilySymbol>()
                    .Where(s => string.Equals(s.Name, tn, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(fn))
                    q = q.Where(s => string.Equals(s.Family?.Name, fn, StringComparison.OrdinalIgnoreCase));
                elem = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
            }

            if (elem == null) return new { ok = false, msg = "Element/Type が見つかりません。" };

            // 並び（名前 → id）
            var defs = (elem.Parameters?.Cast<Parameter>() ?? Enumerable.Empty<Parameter>())
                .Select(pa => new { pa, name = pa?.Definition?.Name ?? "", id = pa?.Id.IntValue() ?? -1 })
                .OrderBy(x => x.name).ThenBy(x => x.id)
                .Select(x => x.pa)
                .Select(pa =>
                {
                    string dataType = null;
                    try { dataType = pa.Definition?.GetDataType()?.TypeId; } catch { dataType = null; }
                    return new
                    {
                        name = pa.Definition?.Name ?? "",
                        id = pa.Id.IntValue(),
                        storageType = pa.StorageType.ToString(),
                        dataType,
                        isReadOnly = pa.IsReadOnly
                    };
                })
                .ToList();

            return new
            {
                ok = true,
                elementId = (elem as FamilyInstance)?.Id.IntValue(),
                typeId = elem is FamilySymbol ? elem.Id.IntValue() : (elem as FamilyInstance)?.GetTypeId().IntValue(),
                uniqueId = elem.UniqueId,
                totalCount = defs.Count,
                definitions = defs
            };
        }
    }

    // 14. Update Type‐Level Parameter
    public class UpdateStructuralFrameTypeParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "update_structural_frame_type_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            // typeId / typeName(+familyName)
            FamilySymbol sym = null;
            if (p.TryGetValue("typeId", out var tidToken))
            {
                sym = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tidToken.Value<int>())) as FamilySymbol;
            }
            else
            {
                var tn = p.Value<string>("typeName");
                var fn = p.Value<string>("familyName");
                if (!string.IsNullOrWhiteSpace(tn))
                {
                    var q = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .Cast<FamilySymbol>()
                        .Where(s => string.Equals(s.Name, tn, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(fn))
                        q = q.Where(s => string.Equals(s.Family?.Name, fn, StringComparison.OrdinalIgnoreCase));
                    sym = q.OrderBy(s => s.Family?.Name ?? "").ThenBy(s => s.Name ?? "").FirstOrDefault();
                }
            }
            if (sym == null) return new { ok = false, msg = "Structural frame type が見つかりません。" };

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, msg = "paramName または builtInName/builtInId/guid のいずれかが必要です。" };
            var param = ParamResolver.ResolveByPayload(sym, p, out var resolvedBy2);
            if (param == null) return new { ok = false, msg = $"Parameter not found on type (name/builtIn/guid)" };
            if (param.IsReadOnly) return new { ok = false, msg = $"Parameter '{paramName}' は読み取り専用です。" };
            if (!p.TryGetValue("value", out var valToken)) return new { ok = false, msg = "value が必要です。" };

            using (var tx = new Transaction(doc, $"Set Type Param {paramName}"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                if (!UnitHelper.TrySetParameterFromSi(param, valToken, out var reason))
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"Failed to set type parameter: {reason}" };
                }
                tx.Commit();
            }
            return new { ok = true, typeId = sym.Id.IntValue(), uniqueId = sym.UniqueId };
        }
    }
}


