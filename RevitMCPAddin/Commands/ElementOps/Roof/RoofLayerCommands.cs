// ================================================================
// File: Commands/RoofOps/RoofLayerCommands.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Purpose: RoofType の CompoundStructure レイヤ取得/編集
//          - get_roof_layers
//          - update_roof_layer
//          - add_roof_layer
//          - remove_roof_layer
//          - set_roof_variable_layer
// Impl   : Revit 2023 API 互換
//          * 関数 enum は MaterialFunctionAssignment
//          * 追加/削除は SetLayers で再構成
//          * 可変層は VariableLayerIndex (int; -1で解除)
// ================================================================

#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // IRevitCommandHandler, RequestCommand, UnitHelper など

namespace RevitMCPAddin.Commands.RoofOps
{
    internal static class RoofCsUtil
    {
        public static RoofType? ResolveRoofType(Document doc, JObject p, out ElementId? fromInstanceId, out string? message)
        {
            fromInstanceId = null; message = null;

            // typeId
            if (p.TryGetValue("typeId", out var jTypeId) && jTypeId.Type == JTokenType.Integer)
            {
                var t = doc.GetElement(new ElementId((int)jTypeId)) as RoofType;
                if (t != null) return t;
                message = $"RoofType not found by typeId={(int)jTypeId}";
            }

            // typeName (+familyName)
            if (p.TryGetValue("typeName", out var jTypeName) && jTypeName.Type == JTokenType.String)
            {
                string tn = (string)jTypeName;
                string? fn = p.TryGetValue("familyName", out var jFam) && jFam.Type == JTokenType.String ? (string)jFam : null;

                var match = new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<RoofType>()
                    .FirstOrDefault(x => x.Name.Equals(tn, StringComparison.OrdinalIgnoreCase)
                                      && (fn == null || x.FamilyName.Equals(fn, StringComparison.OrdinalIgnoreCase)));
                if (match != null) return match;
                message = $"RoofType not found by typeName='{tn}'" + (fn != null ? $" familyName='{fn}'" : "");
            }

            // elementId -> type
            if (p.TryGetValue("elementId", out var jEid) && jEid.Type == JTokenType.Integer)
            {
                var el = doc.GetElement(new ElementId((int)jEid));
                if (el != null)
                {
                    var rt = doc.GetElement(el.GetTypeId()) as RoofType;
                    if (rt != null) { fromInstanceId = el.Id; return rt; }
                    message = $"RoofType not found: instance typeId={el.GetTypeId().IntegerValue}";
                }
                else message = $"Element not found by elementId={(int)jEid}";
            }

            // uniqueId -> type
            if (p.TryGetValue("uniqueId", out var jUid) && jUid.Type == JTokenType.String)
            {
                var el = doc.GetElement((string)jUid);
                if (el != null)
                {
                    var rt = doc.GetElement(el.GetTypeId()) as RoofType;
                    if (rt != null) { fromInstanceId = el.Id; return rt; }
                    message = $"RoofType not found: instance typeId={el.GetTypeId().IntegerValue}";
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
    }

    // ----------------------------
    // 0) get_roof_layers
    // ----------------------------
    public sealed class GetRoofLayersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_roof_layers";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var rt = RoofCsUtil.ResolveRoofType(doc, p, out var fromInstId, out var msg);
                if (rt == null) return new { ok = false, msg = msg ?? "RoofType not found." };

                var cs = rt.GetCompoundStructure(); // ここで copy が返る（SetCompoundStructure で反映） :contentReference[oaicite:2]{index=2}
                if (cs == null) return new { ok = false, msg = $"CompoundStructure is null on RoofType: {rt.Name}" };

                int coreStart = cs.GetFirstCoreLayerIndex();
                int coreEnd = cs.GetLastCoreLayerIndex();
                int varIdx = cs.VariableLayerIndex; // -1 のことあり（未設定） :contentReference[oaicite:3]{index=3}

                var arr = new JArray();
                double totalFt = 0.0;

                var layers = cs.GetLayers(); // Outside->Inside / Roof は「上→下」 :contentReference[oaicite:4]{index=4}
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
                result["typeId"] = rt.Id.IntegerValue;
                result["typeName"] = rt.Name;
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
    // 1) update_roof_layer
    // ----------------------------
    public sealed class UpdateRoofLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "update_roof_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var rtSrc = RoofCsUtil.ResolveRoofType(doc, p, out _, out var msg);
                if (rtSrc == null) return new { ok = false, msg = msg ?? "RoofType not found." };

                if (!p.TryGetValue("layerIndex", out var jIdx) || jIdx.Type != JTokenType.Integer)
                    return new { ok = false, msg = "layerIndex (int) is required." };
                int layerIndex = (int)jIdx;

                double? thkMm = p.TryGetValue("thicknessMm", out var jThk) && jThk.Type != JTokenType.Null ? (double?)jThk : null;
                string? fnStr = p.TryGetValue("function", out var jFn) && jFn.Type == JTokenType.String ? (string)jFn : null;
                bool duplicateIfInUse = p.TryGetValue("duplicateIfInUse", out var jDup) && jDup.Type == JTokenType.Boolean && (bool)jDup;

                using (var t = new Transaction(doc, "Update Roof Layer"))
                {
                    t.Start();

                    // 使用中タイプなら複製
                    RoofType rt = rtSrc;
                    if (duplicateIfInUse)
                    {
                        var dup = rtSrc.Duplicate(rtSrc.Name + " (Edited " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ")") as ElementType;
                        if (dup is RoofType rtt) rt = rtt;
                    }

                    var cs = rt.GetCompoundStructure();
                    if (cs == null) { t.RollBack(); return new { ok = false, msg = $"CompoundStructure is null on RoofType: {rt.Name}" }; }

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
                    var mat = RoofCsUtil.ResolveMaterial(doc, p, out matErr);
                    if (matErr != null && (p.ContainsKey("materialId") || p.ContainsKey("materialName")))
                    {
                        t.RollBack(); return new { ok = false, msg = matErr };
                    }
                    if (mat != null) cs.SetMaterialId(layerIndex, mat.Id);

                    // 機能（任意）
                    if (!string.IsNullOrWhiteSpace(fnStr))
                    {
                        if (!RoofCsUtil.TryParseFunction(fnStr, out var fn))
                        {
                            t.RollBack();
                            return new { ok = false, msg = $"Unknown function='{fnStr}'. Allowed: {string.Join("|", Enum.GetNames(typeof(MaterialFunctionAssignment)))}" };
                        }
                        cs.SetLayerFunction(layerIndex, fn);
                    }

                    rt.SetCompoundStructure(cs);
                    t.Commit();

                    var edited = new JObject();
                    edited["layerIndex"] = layerIndex;
                    if (thkMm.HasValue) edited["thicknessMm"] = Math.Round(thkMm.Value, 3);
                    if (mat != null) { edited["materialId"] = mat.Id.IntegerValue; edited["materialName"] = mat.Name; }
                    if (!string.IsNullOrWhiteSpace(fnStr)) edited["function"] = fnStr;

                    var res = new JObject();
                    res["ok"] = true;
                    res["typeId"] = rt.Id.IntegerValue;
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
    // 2) add_roof_layer (SetLayersで再構成)
    // ----------------------------
    public sealed class AddRoofLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "add_roof_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var rtSrc = RoofCsUtil.ResolveRoofType(doc, p, out _, out var msg);
                if (rtSrc == null) return new { ok = false, msg = msg ?? "RoofType not found." };

                if (!p.TryGetValue("insertAt", out var jIns) || jIns.Type != JTokenType.Integer)
                    return new { ok = false, msg = "insertAt (int) is required." };

                if (!p.TryGetValue("function", out var jFn) || jFn.Type != JTokenType.String)
                    return new { ok = false, msg = "function (string) is required." };

                if (!RoofCsUtil.TryParseFunction((string)jFn, out var fn))
                    return new { ok = false, msg = $"Unknown function='{(string)jFn}'." };

                double thkMm = p.TryGetValue("thicknessMm", out var jThk) && jThk.Type != JTokenType.Null ? (double)jThk : 0.0;
                bool isVariable = p.TryGetValue("isVariable", out var jVar) && jVar.Type == JTokenType.Boolean && (bool)jVar;
                bool duplicateIfInUse = p.TryGetValue("duplicateIfInUse", out var jDup) && jDup.Type == JTokenType.Boolean && (bool)jDup;

                using (var t = new Transaction(doc, "Add Roof Layer"))
                {
                    t.Start();

                    RoofType rt = rtSrc;
                    if (duplicateIfInUse)
                    {
                        var dup = rtSrc.Duplicate(rtSrc.Name + " (Edited " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ")") as ElementType;
                        if (dup is RoofType rtt) rt = rtt;
                    }

                    var cs = rt.GetCompoundStructure();
                    if (cs == null) { t.RollBack(); return new { ok = false, msg = $"CompoundStructure is null on RoofType: {rt.Name}" }; }

                    var old = cs.GetLayers();
                    int insertAt = Math.Max(0, Math.Min((int)jIns, old.Count));

                    // 新規レイヤ作成（幅/機能/材料）
                    string? matErr;
                    var mat = RoofCsUtil.ResolveMaterial(doc, p, out matErr);
                    if (matErr != null && (p.ContainsKey("materialId") || p.ContainsKey("materialName")))
                    {
                        t.RollBack(); return new { ok = false, msg = matErr };
                    }
                    var newLayer = new CompoundStructureLayer(UnitHelper.MmToFt(Math.Max(0.0, thkMm)), fn, mat?.Id ?? ElementId.InvalidElementId); // ctor: (width, function, materialId) :contentReference[oaicite:5]{index=5}

                    // リストに挿入して SetLayers
                    var list = new List<CompoundStructureLayer>(old);
                    list.Insert(insertAt, newLayer);
                    cs.SetLayers(list); // 可変/構造材インデックスは unset される点に注意 :contentReference[oaicite:6]{index=6}

                    // 可変層指定（必要なら再設定）
                    if (isVariable) cs.VariableLayerIndex = insertAt;  // -1で解除、indexで設定（プロパティ） :contentReference[oaicite:7]{index=7}

                    rt.SetCompoundStructure(cs);

                    // 合計厚
                    double totalFt = 0; foreach (var L in cs.GetLayers()) totalFt += L.Width;

                    t.Commit();

                    var res = new JObject();
                    res["ok"] = true;
                    res["typeId"] = rt.Id.IntegerValue;
                    res["newLayerIndex"] = insertAt;
                    res["totalThicknessMm"] = Math.Round(UnitHelper.FtToMm(totalFt), 3);
                    res["note"] = duplicateIfInUse ? "Original type duplicated and edited." : "Type edited.";
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
    // 3) remove_roof_layer (SetLayersで再構成)
    // ----------------------------
    public sealed class RemoveRoofLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "remove_roof_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var rtSrc = RoofCsUtil.ResolveRoofType(doc, p, out _, out var msg);
                if (rtSrc == null) return new { ok = false, msg = msg ?? "RoofType not found." };

                if (!p.TryGetValue("layerIndex", out var jIdx) || jIdx.Type != JTokenType.Integer)
                    return new { ok = false, msg = "layerIndex (int) is required." };
                int layerIndex = (int)jIdx;

                bool duplicateIfInUse = p.TryGetValue("duplicateIfInUse", out var jDup) && jDup.Type == JTokenType.Boolean && (bool)jDup;

                using (var t = new Transaction(doc, "Remove Roof Layer"))
                {
                    t.Start();

                    RoofType rt = rtSrc;
                    if (duplicateIfInUse)
                    {
                        var dup = rtSrc.Duplicate(rtSrc.Name + " (Edited " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ")") as ElementType;
                        if (dup is RoofType rtt) rt = rtt;
                    }

                    var cs = rt.GetCompoundStructure();
                    if (cs == null) { t.RollBack(); return new { ok = false, msg = $"CompoundStructure is null on RoofType: {rt.Name}" }; }

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
                    cs.SetLayers(list);           // ここで VariableLayerIndex はリセットされる
                    cs.VariableLayerIndex = -1;   // 念のため明示的に解除

                    rt.SetCompoundStructure(cs);

                    double totalFt = 0; foreach (var L in cs.GetLayers()) totalFt += L.Width;
                    t.Commit();

                    var res = new JObject();
                    res["ok"] = true;
                    res["typeId"] = rt.Id.IntegerValue;
                    res["removedIndex"] = layerIndex;
                    res["totalThicknessMm"] = Math.Round(UnitHelper.FtToMm(totalFt), 3);
                    res["note"] = duplicateIfInUse ? "Original type duplicated and edited." : "Type edited.";
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
    // 4) set_roof_variable_layer (VariableLayerIndex を使う)
    // ----------------------------
    public sealed class SetRoofVariableLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "set_roof_variable_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var rtSrc = RoofCsUtil.ResolveRoofType(doc, p, out _, out var msg);
                if (rtSrc == null) return new { ok = false, msg = msg ?? "RoofType not found." };

                if (!p.TryGetValue("layerIndex", out var jIdx) || jIdx.Type != JTokenType.Integer)
                    return new { ok = false, msg = "layerIndex (int) is required." };
                int layerIndex = (int)jIdx;

                bool isVariable = !(p.TryGetValue("isVariable", out var jVar) && jVar.Type == JTokenType.Boolean) || (bool)jVar;
                bool duplicateIfInUse = p.TryGetValue("duplicateIfInUse", out var jDup) && jDup.Type == JTokenType.Boolean && (bool)jDup;

                using (var t = new Transaction(doc, "Set Roof Variable Layer"))
                {
                    t.Start();

                    RoofType rt = rtSrc;
                    if (duplicateIfInUse)
                    {
                        var dup = rtSrc.Duplicate(rtSrc.Name + " (Edited " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ")") as ElementType;
                        if (dup is RoofType rtt) rt = rtt;
                    }

                    var cs = rt.GetCompoundStructure();
                    if (cs == null) { t.RollBack(); return new { ok = false, msg = $"CompoundStructure is null on RoofType: {rt.Name}" }; }

                    var layers = cs.GetLayers();
                    if (layerIndex < 0 || layerIndex >= layers.Count)
                    {
                        t.RollBack(); return new { ok = false, msg = $"Invalid layerIndex: {layerIndex} (count={layers.Count})." };
                    }

                    if (isVariable)
                    {
                        // 設定可能か一応チェック
                        if (!cs.CanLayerBeVariable(layerIndex))
                        {
                            t.RollBack(); return new { ok = false, msg = "This layer cannot be designated as variable (CanLayerBeVariable=false)." };
                        }
                        cs.VariableLayerIndex = layerIndex;
                    }
                    else
                    {
                        // 対象が可変なら解除
                        if (cs.VariableLayerIndex == layerIndex) cs.VariableLayerIndex = -1;
                    }

                    rt.SetCompoundStructure(cs);
                    t.Commit();

                    return new
                    {
                        ok = true,
                        typeId = rt.Id.IntegerValue,
                        layerIndex = layerIndex,
                        isVariable = isVariable,
                        note = duplicateIfInUse ? "Original type duplicated and edited." : "Type edited."
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
}
