// ================================================================
// File: Commands/FloorOps/FloorLayerCommands.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Purpose: FloorType の CompoundStructure レイヤ取得/編集
//   - get_floor_type_info
//   - get_floor_layers
//   - update_floor_layer
//   - add_floor_layer
//   - remove_floor_layer
//   - set_floor_variable_layer
//   - swap_floor_layer_materials
// Impl   : Revit 2023 API 互換
//   * 関数 enum は MaterialFunctionAssignment
//   * 追加/削除は SetLayers で再構成
//   * 可変層は VariableLayerIndex (int; -1で解除)
// ================================================================

#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // IRevitCommandHandler, RequestCommand, UnitHelper など

namespace RevitMCPAddin.Commands.FloorOps
{
    internal static class FloorCsUtil
    {
        public static FloorType? ResolveFloorType(Document doc, JObject p, out ElementId? fromInstanceId, out string? message)
        {
            fromInstanceId = null; message = null;

            // typeId
            if (p.TryGetValue("typeId", out var jTypeId) && jTypeId.Type == JTokenType.Integer)
            {
                var t = doc.GetElement(new ElementId((int)jTypeId)) as FloorType;
                if (t != null) return t;
                message = $"FloorType not found by typeId={(int)jTypeId}";
            }

            // typeName (+familyName)
            if (p.TryGetValue("typeName", out var jTypeName) && jTypeName.Type == JTokenType.String)
            {
                string tn = (string)jTypeName;
                string? fn = p.TryGetValue("familyName", out var jFam) && jFam.Type == JTokenType.String ? (string)jFam : null;

                var match = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                    .FirstOrDefault(x => x.Name.Equals(tn, StringComparison.OrdinalIgnoreCase)
                                      && (fn == null || x.FamilyName.Equals(fn, StringComparison.OrdinalIgnoreCase)));
                if (match != null) return match;
                message = $"FloorType not found by typeName='{tn}'" + (fn != null ? $" familyName='{fn}'" : "");
            }

            // elementId -> type
            if (p.TryGetValue("elementId", out var jEid) && jEid.Type == JTokenType.Integer)
            {
                var el = doc.GetElement(new ElementId((int)jEid));
                if (el != null)
                {
                    var ft = doc.GetElement(el.GetTypeId()) as FloorType;
                    if (ft != null) { fromInstanceId = el.Id; return ft; }
                    message = $"FloorType not found: instance typeId={el.GetTypeId().IntegerValue}";
                }
                else message = $"Element not found by elementId={(int)jEid}";
            }

            // uniqueId -> type
            if (p.TryGetValue("uniqueId", out var jUid) && jUid.Type == JTokenType.String)
            {
                var el = doc.GetElement((string)jUid);
                if (el != null)
                {
                    var ft = doc.GetElement(el.GetTypeId()) as FloorType;
                    if (ft != null) { fromInstanceId = el.Id; return ft; }
                    message = $"FloorType not found: instance typeId={el.GetTypeId().IntegerValue}";
                }
                else message = $"Element not found by uniqueId={(string)jUid}";
            }

            return null;
        }

        public static Material? ResolveMaterial(Document doc, JObject p, out string? err)
        {
            err = null;
            if (p.TryGetValue("materialId", out var jMid) && jMid.Type == JTokenType.Integer)
            {
                var m = doc.GetElement(new ElementId((int)jMid)) as Material;
                if (m != null) return m;
                err = $"Material not found by materialId={(int)jMid}";
                return null;
            }
            if (p.TryGetValue("materialName", out var jMname) && jMname.Type == JTokenType.String)
            {
                string name = (string)jMname;
                var m = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                            .FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (m != null) return m;
                err = $"Material not found by materialName='{name}'";
                return null;
            }
            return null; // 指定なし
        }

        public static bool TryParseFunction(string? s, out MaterialFunctionAssignment fn)
        {
            fn = MaterialFunctionAssignment.None;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return Enum.TryParse(s.Trim(), true, out fn);
        }

        public static bool IsTypeInUse(Document doc, FloorType t)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Floor))
                .Cast<Floor>()
                .Any(e => e.GetTypeId() == t.Id);
        }
    }

    // ----------------------------
    // 0) get_floor_type_info
    // ----------------------------
    public sealed class GetFloorTypeInfoCommand : IRevitCommandHandler
    {
        public string CommandName => "get_floor_type_info";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };
            try
            {
                var p = cmd.Params ?? new JObject();
                var ft = FloorCsUtil.ResolveFloorType(doc, p, out _, out var msg);
                if (ft == null) return new { ok = false, msg = msg ?? "FloorType not found." };

                var cs = ft.GetCompoundStructure();
                if (cs == null) return new { ok = false, msg = $"CompoundStructure is null on FloorType: {ft.Name}" };

                var layers = cs.GetLayers();
                double totalFt = 0.0; foreach (var L in layers) totalFt += L.Width;

                return new
                {
                    ok = true,
                    typeId = ft.Id.IntegerValue,
                    typeName = ft.Name,
                    kind = "CompoundStructure",
                    thicknessMm = Math.Round(UnitHelper.FtToMm(totalFt), 3),
                    layerCount = layers.Count
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return new { ok = false, msg = "Exception: " + ex.Message };
            }
        }
    }

    // ----------------------------
    // 1) get_floor_layers
    // ----------------------------
    public sealed class GetFloorLayersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_floor_layers";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var ft = FloorCsUtil.ResolveFloorType(doc, p, out var fromInstId, out var msg);
                if (ft == null) return new { ok = false, msg = msg ?? "FloorType not found." };

                var cs = ft.GetCompoundStructure(); // SetCompoundStructure で反映されるコピー
                if (cs == null) return new { ok = false, msg = $"CompoundStructure is null on FloorType: {ft.Name}" };

                int coreStart = cs.GetFirstCoreLayerIndex();
                int coreEnd = cs.GetLastCoreLayerIndex();
                int varIdx = cs.VariableLayerIndex; // -1 のことあり（未設定）

                var arr = new JArray();
                double totalFt = 0.0;

                var layers = cs.GetLayers(); // Outside->Inside
                for (int i = 0; i < layers.Count; i++)
                {
                    var L = layers[i];
                    totalFt += L.Width;

                    var jo = new JObject();
                    jo["index"] = i;
                    jo["function"] = cs.GetLayerFunction(i).ToString();
                    var mid = L.MaterialId;
                    if (mid != null && mid != ElementId.InvalidElementId)
                    {
                        jo["materialId"] = mid.IntegerValue;
                        var m = doc.GetElement(mid) as Material;
                        if (m != null) jo["materialName"] = m.Name;
                    }
                    jo["thicknessMm"] = Math.Round(UnitHelper.FtToMm(L.Width), 3);
                    bool isCore = (coreStart >= 0 && coreEnd >= coreStart) && (i >= coreStart && i <= coreEnd);
                    jo["isCore"] = isCore;
                    jo["isVariable"] = (varIdx == i);

                    arr.Add(jo);
                }

                var result = new JObject();
                result["ok"] = true;
                result["typeId"] = ft.Id.IntegerValue;
                result["typeName"] = ft.Name;
                result["layers"] = arr;
                result["totalThicknessMm"] = Math.Round(UnitHelper.FtToMm(totalFt), 3);

                var core = new JObject();
                if (coreStart >= 0 && coreEnd >= coreStart)
                {
                    core["startIndex"] = coreStart;
                    core["endIndex"] = coreEnd;
                    core["count"] = (coreEnd - coreStart + 1);
                }
                else
                {
                    core["startIndex"] = null;
                    core["endIndex"] = null;
                    core["count"] = 0;
                }
                result["core"] = core;

                if (fromInstId != null) result["resolvedFromInstanceId"] = fromInstId.IntegerValue;

                var units = new JObject();
                units["Length"] = "mm";
                result["units"] = units;

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return new { ok = false, msg = "Exception: " + ex.Message };
            }
        }
    }

    // ----------------------------
    // 2) update_floor_layer
    // ----------------------------
    public sealed class UpdateFloorLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "update_floor_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var ftSrc = FloorCsUtil.ResolveFloorType(doc, p, out _, out var msg);
                if (ftSrc == null) return new { ok = false, msg = msg ?? "FloorType not found." };

                if (!p.TryGetValue("layerIndex", out var jIdx) || jIdx.Type != JTokenType.Integer)
                    return new { ok = false, msg = "layerIndex (int) is required." };
                int layerIndex = (int)jIdx;

                double? thkMm = p.TryGetValue("thicknessMm", out var jThk) && jThk.Type != JTokenType.Null ? (double?)jThk : null;
                string? fnStr = p.TryGetValue("function", out var jFn) && jFn.Type == JTokenType.String ? (string)jFn : null;
                bool duplicateIfInUse = p.TryGetValue("duplicateIfInUse", out var jDup) && jDup.Type == JTokenType.Boolean && (bool)jDup;

                using (var t = new Transaction(doc, "Update Floor Layer"))
                {
                    t.Start();

                    // 使用中タイプなら複製
                    FloorType ft = ftSrc;
                    if (duplicateIfInUse && FloorCsUtil.IsTypeInUse(doc, ftSrc))
                    {
                        var dup = ftSrc.Duplicate(ftSrc.Name + " (Edited " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ")") as ElementType;
                        if (dup is FloorType ftt) ft = ftt;
                    }

                    var cs = ft.GetCompoundStructure();
                    if (cs == null) { t.RollBack(); return new { ok = false, msg = $"CompoundStructure is null on FloorType: {ft.Name}" }; }

                    var layers = cs.GetLayers();
                    if (layerIndex < 0 || layerIndex >= layers.Count)
                    {
                        t.RollBack(); return new { ok = false, msg = $"Invalid layerIndex: {layerIndex} (count={layers.Count})." };
                    }

                    // 厚み
                    if (thkMm.HasValue)
                        cs.SetLayerWidth(layerIndex, UnitHelper.MmToFt(Math.Max(0.0, thkMm.Value)));

                    // 材料（任意）
                    string? matErr;
                    var mat = FloorCsUtil.ResolveMaterial(doc, p, out matErr);
                    if (matErr != null && (p.ContainsKey("materialId") || p.ContainsKey("materialName")))
                    {
                        t.RollBack(); return new { ok = false, msg = matErr };
                    }
                    if (mat != null) cs.SetMaterialId(layerIndex, mat.Id);

                    // 機能（任意）
                    if (!string.IsNullOrWhiteSpace(fnStr))
                    {
                        if (!FloorCsUtil.TryParseFunction(fnStr, out var fn))
                        {
                            t.RollBack();
                            return new { ok = false, msg = $"Unknown function='{fnStr}'. Allowed: {string.Join("|", Enum.GetNames(typeof(MaterialFunctionAssignment)))}" };
                        }
                        cs.SetLayerFunction(layerIndex, fn);
                    }

                    ft.SetCompoundStructure(cs);
                    t.Commit();

                    var edited = new JObject();
                    edited["layerIndex"] = layerIndex;
                    if (thkMm.HasValue) edited["thicknessMm"] = Math.Round(thkMm.Value, 3);
                    if (mat != null) { edited["materialId"] = mat.Id.IntegerValue; edited["materialName"] = mat.Name; }
                    if (!string.IsNullOrWhiteSpace(fnStr)) edited["function"] = fnStr;

                    var res = new JObject();
                    res["ok"] = true;
                    res["typeId"] = ft.Id.IntegerValue;
                    res["note"] = (duplicateIfInUse ? "Original type duplicated and edited." : "Type edited.");
                    res["edited"] = edited;
                    return res;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return new { ok = false, msg = "Exception: " + ex.Message };
            }
        }
    }

    // ----------------------------
    // 3) add_floor_layer (SetLayersで再構成)
    // ----------------------------
    public sealed class AddFloorLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "add_floor_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var ftSrc = FloorCsUtil.ResolveFloorType(doc, p, out _, out var msg);
                if (ftSrc == null) return new { ok = false, msg = msg ?? "FloorType not found." };

                if (!p.TryGetValue("insertAt", out var jIns) || jIns.Type != JTokenType.Integer)
                    return new { ok = false, msg = "insertAt (int) is required." };

                if (!p.TryGetValue("function", out var jFn) || jFn.Type != JTokenType.String)
                    return new { ok = false, msg = "function (string) is required." };

                if (!FloorCsUtil.TryParseFunction((string)jFn, out var fn))
                    return new { ok = false, msg = $"Unknown function='{(string)jFn}'." };

                double thkMm = p.TryGetValue("thicknessMm", out var jThk) && jThk.Type != JTokenType.Null ? (double)jThk : 0.0;
                bool isVariable = p.TryGetValue("isVariable", out var jVar) && jVar.Type == JTokenType.Boolean && (bool)jVar;
                bool isCore = p.TryGetValue("isCore", out var jCore) && jCore.Type == JTokenType.Boolean && (bool)jCore;
                bool duplicateIfInUse = p.TryGetValue("duplicateIfInUse", out var jDup) && jDup.Type == JTokenType.Boolean && (bool)jDup;

                using (var t = new Transaction(doc, "Add Floor Layer"))
                {
                    t.Start();

                    FloorType ft = ftSrc;
                    if (duplicateIfInUse && FloorCsUtil.IsTypeInUse(doc, ftSrc))
                    {
                        var dup = ftSrc.Duplicate(ftSrc.Name + " (Edited " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ")") as ElementType;
                        if (dup is FloorType ftt) ft = ftt;
                    }

                    var cs = ft.GetCompoundStructure();
                    if (cs == null) { t.RollBack(); return new { ok = false, msg = $"CompoundStructure is null on FloorType: {ft.Name}" }; }

                    var old = cs.GetLayers();
                    int insertAt = Math.Max(0, Math.Min((int)jIns, old.Count));

                    // 新規レイヤ作成（幅/機能/材料）
                    string? matErr;
                    var mat = FloorCsUtil.ResolveMaterial(doc, p, out matErr);
                    if (matErr != null && (p.ContainsKey("materialId") || p.ContainsKey("materialName")))
                    {
                        t.RollBack(); return new { ok = false, msg = matErr };
                    }
                    var newLayer = new CompoundStructureLayer(UnitHelper.MmToFt(Math.Max(0.0, thkMm)), fn, mat?.Id ?? ElementId.InvalidElementId);

                    // リストに挿入して SetLayers
                    var list = new List<CompoundStructureLayer>(old);
                    list.Insert(insertAt, newLayer);
                    cs.SetLayers(list); // 可変/構造/コア境界は必要なら再設定

                    // 可変層（任意）
                    if (isVariable)
                    {
                        if (!cs.CanLayerBeVariable(insertAt))
                        {
                            t.RollBack(); return new { ok = false, msg = "This layer cannot be designated as variable (CanLayerBeVariable=false)." };
                        }
                        cs.VariableLayerIndex = insertAt;  // -1で解除、indexで設定
                    }

                    // コア境界（任意）
                    if (isCore)
                    {
                        // 1層だけをコアにしたい場合、start=end=insertAt に設定
                        try
                        {
                            FloorCoreBoundaryHelpers.TrySetCoreLayerIndex(cs, insertAt, insertAt);
                        }
                        catch
                        {
                            // 一部API差分で例外の可能性 → 失敗しても機能必須ではないので握りつぶし
                        }
                    }

                    ft.SetCompoundStructure(cs);

                    // 合計厚
                    double totalFt = 0; foreach (var L in cs.GetLayers()) totalFt += L.Width;

                    t.Commit();

                    var res = new JObject();
                    res["ok"] = true;
                    res["typeId"] = ft.Id.IntegerValue;
                    res["newLayerIndex"] = insertAt;
                    res["totalThicknessMm"] = Math.Round(UnitHelper.FtToMm(totalFt), 3);
                    res["note"] = (duplicateIfInUse ? "Original type duplicated and edited." : "Type edited.");
                    return res;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return new { ok = false, msg = "Exception: " + ex.Message };
            }
        }
    }

    // ----------------------------
    // 4) remove_floor_layer (SetLayersで再構成)
    // ----------------------------
    public sealed class RemoveFloorLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "remove_floor_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var ftSrc = FloorCsUtil.ResolveFloorType(doc, p, out _, out var msg);
                if (ftSrc == null) return new { ok = false, msg = msg ?? "FloorType not found." };

                if (!p.TryGetValue("layerIndex", out var jIdx) || jIdx.Type != JTokenType.Integer)
                    return new { ok = false, msg = "layerIndex (int) is required." };
                int layerIndex = (int)jIdx;

                bool duplicateIfInUse = p.TryGetValue("duplicateIfInUse", out var jDup) && jDup.Type == JTokenType.Boolean && (bool)jDup;

                using (var t = new Transaction(doc, "Remove Floor Layer"))
                {
                    t.Start();

                    FloorType ft = ftSrc;
                    if (duplicateIfInUse && FloorCsUtil.IsTypeInUse(doc, ftSrc))
                    {
                        var dup = ftSrc.Duplicate(ftSrc.Name + " (Edited " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ")") as ElementType;
                        if (dup is FloorType ftt) ft = ftt;
                    }

                    var cs = ft.GetCompoundStructure();
                    if (cs == null) { t.RollBack(); return new { ok = false, msg = $"CompoundStructure is null on FloorType: {ft.Name}" }; }

                    var old = cs.GetLayers();
                    if (layerIndex < 0 || layerIndex >= old.Count)
                    {
                        t.RollBack(); return new { ok = false, msg = $"Invalid layerIndex: {layerIndex} (count={old.Count})." };
                    }
                    if (old.Count <= 1)
                    {
                        t.RollBack(); return new { ok = false, msg = "Cannot remove the last remaining layer." };
                    }

                    var list = new List<CompoundStructureLayer>(old);
                    list.RemoveAt(layerIndex);
                    cs.SetLayers(list);
                    cs.VariableLayerIndex = -1;   // 念のため解除（SetLayersで消えるが明示）

                    try
                    {
                        // コア境界が不正になっていたら安全側にリセット（start=end=0 か解除）
                        var core = FloorCoreBoundaryHelpers.TryGetCoreLayerIndices(cs);
                        if ((core.start ?? int.MaxValue) >= list.Count
                         || (core.end ?? int.MaxValue) >= list.Count
                         || (core.start.HasValue && core.end.HasValue && core.start > core.end))
                        {
                            // セッターが無ければ no-op（握りつぶし）
                            FloorCoreBoundaryHelpers.TrySetCoreLayerIndex(cs, 0, 0);
                        }
                    }
                    catch { /* API差分で例外なら握りつぶし */ }

                    ft.SetCompoundStructure(cs);

                    double totalFt = 0; foreach (var L in cs.GetLayers()) totalFt += L.Width;
                    t.Commit();

                    var res = new JObject();
                    res["ok"] = true;
                    res["typeId"] = ft.Id.IntegerValue;
                    res["removedIndex"] = layerIndex;
                    res["totalThicknessMm"] = Math.Round(UnitHelper.FtToMm(totalFt), 3);
                    res["note"] = (duplicateIfInUse ? "Original type duplicated and edited." : "Type edited.");
                    return res;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return new { ok = false, msg = "Exception: " + ex.Message };
            }
        }


    }

    // ----------------------------
    // 5) set_floor_variable_layer (VariableLayerIndex を使う)
    // ----------------------------
    public sealed class SetFloorVariableLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "set_floor_variable_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var ftSrc = FloorCsUtil.ResolveFloorType(doc, p, out _, out var msg);
                if (ftSrc == null) return new { ok = false, msg = msg ?? "FloorType not found." };

                if (!p.TryGetValue("layerIndex", out var jIdx) || jIdx.Type != JTokenType.Integer)
                    return new { ok = false, msg = "layerIndex (int) is required." };
                int layerIndex = (int)jIdx;

                bool isVariable = !(p.TryGetValue("isVariable", out var jVar) && jVar.Type == JTokenType.Boolean) || (bool)jVar;
                bool duplicateIfInUse = p.TryGetValue("duplicateIfInUse", out var jDup) && jDup.Type == JTokenType.Boolean && (bool)jDup;

                using (var t = new Transaction(doc, "Set Floor Variable Layer"))
                {
                    t.Start();

                    FloorType ft = ftSrc;
                    if (duplicateIfInUse && FloorCsUtil.IsTypeInUse(doc, ftSrc))
                    {
                        var dup = ftSrc.Duplicate(ftSrc.Name + " (Edited " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ")") as ElementType;
                        if (dup is FloorType ftt) ft = ftt;
                    }

                    var cs = ft.GetCompoundStructure();
                    if (cs == null) { t.RollBack(); return new { ok = false, msg = $"CompoundStructure is null on FloorType: {ft.Name}" }; }

                    var layers = cs.GetLayers();
                    if (layerIndex < 0 || layerIndex >= layers.Count)
                    {
                        t.RollBack(); return new { ok = false, msg = $"Invalid layerIndex: {layerIndex} (count={layers.Count})." };
                    }

                    if (isVariable)
                    {
                        if (!cs.CanLayerBeVariable(layerIndex))
                        {
                            t.RollBack(); return new { ok = false, msg = "This layer cannot be designated as variable (CanLayerBeVariable=false)." };
                        }
                        cs.VariableLayerIndex = layerIndex;
                    }
                    else
                    {
                        if (cs.VariableLayerIndex == layerIndex) cs.VariableLayerIndex = -1;
                    }

                    ft.SetCompoundStructure(cs);
                    t.Commit();

                    return new
                    {
                        ok = true,
                        typeId = ft.Id.IntegerValue,
                        layerIndex = layerIndex,
                        isVariable = isVariable,
                        note = (duplicateIfInUse ? "Original type duplicated and edited." : "Type edited.")
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return new { ok = false, msg = "Exception: " + ex.Message };
            }
        }
    }

    // ----------------------------
    // 6) swap_floor_layer_materials
    // ----------------------------
    public sealed class SwapFloorLayerMaterialsCommand : IRevitCommandHandler
    {
        public string CommandName => "swap_floor_layer_materials";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var ftSrc = FloorCsUtil.ResolveFloorType(doc, p, out _, out var msg);
                if (ftSrc == null) return new { ok = false, msg = msg ?? "FloorType not found." };

                string contains = p.SelectToken("find.materialNameContains")?.Value<string>() ?? "";
                string? replaceName = p.SelectToken("replace.materialName")?.Value<string>();
                int maxCount = p.Value<int?>("maxCount") ?? int.MaxValue;

                if (string.IsNullOrWhiteSpace(contains) || string.IsNullOrWhiteSpace(replaceName))
                    return new { ok = false, msg = "find.materialNameContains and replace.materialName are required." };

                // 置換先マテリアル
                var targetMat = new FilteredElementCollector(doc).OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name.Equals(replaceName, StringComparison.OrdinalIgnoreCase));
                if (targetMat == null)
                    return new { ok = false, msg = $"Material not found: {replaceName}" };

                int swapped = 0;

                using (var t = new Transaction(doc, "Swap Floor Layer Materials"))
                {
                    t.Start();

                    // 使用中タイプでも、そのまま編集可（必要なら duplicateIfInUse を設けてもOK）
                    FloorType ft = ftSrc;

                    var cs = ft.GetCompoundStructure();
                    if (cs == null) { t.RollBack(); return new { ok = false, msg = $"CompoundStructure is null on FloorType: {ft.Name}" }; }

                    var layers = cs.GetLayers();
                    for (int i = 0; i < layers.Count; i++)
                    {
                        var ly = layers[i];
                        var mat = (ly.MaterialId != ElementId.InvalidElementId) ? doc.GetElement(ly.MaterialId) as Material : null;
                        if (mat != null && mat.Name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            cs.SetMaterialId(i, targetMat.Id);
                            swapped++;
                            if (swapped >= maxCount) break;
                        }
                    }

                    ft.SetCompoundStructure(cs);
                    t.Commit();
                }

                return new { ok = true, swapped };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return new { ok = false, msg = "Exception: " + ex.Message };
            }
        }
    }

    // === Core boundary helpers (shared) ===
    internal static class FloorCoreBoundaryHelpers
    {
        internal static bool TrySetCoreLayerIndex(CompoundStructure cs, int first, int last)
        {
            var mi = cs.GetType().GetMethod(
                "SetCoreLayerIndex",
                new[] { typeof(int), typeof(int) }
            );
            if (mi == null) return false; // APIが無い環境
            try
            {
                mi.Invoke(cs, new object[] { first, last });
                return true;
            }
            catch { return false; }
        }

        internal static (int? start, int? end) TryGetCoreLayerIndices(CompoundStructure cs)
        {
            int? start = null, end = null;

            var getFirst = cs.GetType().GetMethod("GetFirstCoreLayerIndex", Type.EmptyTypes);
            if (getFirst != null)
            {
                try { start = (int)getFirst.Invoke(cs, null); } catch { /* ignore */ }
            }

            var getLast = cs.GetType().GetMethod("GetLastCoreLayerIndex", Type.EmptyTypes);
            if (getLast != null)
            {
                try { end = (int)getLast.Invoke(cs, null); } catch { /* ignore */ }
            }

            return (start, end);
        }
    }

}
