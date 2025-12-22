// ================================================================
// File: Commands/RoofOps/RoofTypeExtras.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Purpose: RoofType 用 追加コマンド
//          - get_roof_type_info
//          - swap_roof_layer_materials
// Notes  : CompoundStructure 読み/編集（SetLayers, SetLayerWidth ほか）
// ================================================================

#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // IRevitCommandHandler, RequestCommand, UnitHelper

namespace RevitMCPAddin.Commands.RoofOps
{
    // ---------------------------------------------
    // 1) get_roof_type_info
    // ---------------------------------------------
    public sealed class GetRoofTypeInfoCommand : IRevitCommandHandler
    {
        public string CommandName => "get_roof_type_info";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var rt = RoofCsUtil.ResolveRoofType(doc, p, out _, out var msg);
                if (rt == null) return new { ok = false, msg = msg ?? "RoofType not found." };

                var cs = rt.GetCompoundStructure();
                if (cs == null) return new { ok = false, msg = $"CompoundStructure is null on RoofType: {rt.Name}" };

                var layers = cs.GetLayers();
                double totalFt = 0; foreach (var L in layers) totalFt += L.Width;

                int coreStart = cs.GetFirstCoreLayerIndex();
                int coreEnd = cs.GetLastCoreLayerIndex();
                int varIdx = cs.VariableLayerIndex;

                var core = new JObject();
                if (coreStart >= 0 && coreEnd >= coreStart)
                {
                    core["startIndex"] = coreStart;
                    core["endIndex"] = coreEnd;
                    core["count"] = coreEnd - coreStart + 1;
                }
                else
                {
                    core["startIndex"] = null;
                    core["endIndex"] = null;
                    core["count"] = 0;
                }

                return new JObject
                {
                    ["ok"] = true,
                    ["typeId"] = rt.Id.IntValue(),
                    ["typeName"] = rt.Name,
                    ["kind"] = "CompoundStructure",
                    ["thicknessMm"] = Math.Round(UnitHelper.FtToMm(totalFt), 3),
                    ["layerCount"] = layers.Count,
                    ["variableLayerIndex"] = varIdx >= 0 ? (JToken)varIdx : JValue.CreateNull(),
                    ["core"] = core
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return new { ok = false, msg = "Exception: " + ex.Message };
            }
        }
    }

    // ---------------------------------------------
    // 2) swap_roof_layer_materials
    // ---------------------------------------------
    public sealed class SwapRoofLayerMaterialsCommand : IRevitCommandHandler
    {
        public string CommandName => "swap_roof_layer_materials";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            try
            {
                var p = cmd.Params ?? new JObject();
                var rtSrc = RoofCsUtil.ResolveRoofType(doc, p, out _, out var msg);
                if (rtSrc == null) return new { ok = false, msg = msg ?? "RoofType not found." };

                // find: { materialNameContains?: string }
                string? contains = null;
                if (p.TryGetValue("find", out var jFind) && jFind is JObject jf)
                {
                    if (jf.TryGetValue("materialNameContains", out var jmc) && jmc.Type == JTokenType.String)
                        contains = (string)jmc;
                }
                if (string.IsNullOrWhiteSpace(contains))
                    return new { ok = false, msg = "find.materialNameContains (string) is required." };

                // replace: { materialName?: string, materialId?: int }
                int? replaceMatIdInt = null;
                string? replaceMatName = null;
                if (p.TryGetValue("replace", out var jRep) && jRep is JObject jr)
                {
                    if (jr.TryGetValue("materialId", out var jmid) && jmid.Type == JTokenType.Integer)
                        replaceMatIdInt = (int)jmid;
                    if (jr.TryGetValue("materialName", out var jmn) && jmn.Type == JTokenType.String)
                        replaceMatName = (string)jmn;
                }
                if (replaceMatIdInt == null && string.IsNullOrWhiteSpace(replaceMatName))
                    return new { ok = false, msg = "replace.materialId or replace.materialName is required." };

                // resolve replace material
                Material? replaceMat = null;
                if (replaceMatIdInt != null)
                {
                    var eid = Autodesk.Revit.DB.ElementIdCompat.From(replaceMatIdInt.Value);
                    replaceMat = doc.GetElement(eid) as Material;
                    if (replaceMat == null)
                        return new { ok = false, msg = $"Material not found by materialId={replaceMatIdInt.Value}" };
                }
                else
                {
                    replaceMat = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
                                  .FirstOrDefault(x => x.Name.Equals(replaceMatName, StringComparison.OrdinalIgnoreCase));
                    if (replaceMat == null)
                        return new { ok = false, msg = $"Material not found by materialName='{replaceMatName}'" };
                }

                // options
                int maxCount = p.TryGetValue("maxCount", out var jMax) && jMax.Type == JTokenType.Integer ? (int)jMax : int.MaxValue;
                bool duplicateIfInUse = p.TryGetValue("duplicateIfInUse", out var jDup) && jDup.Type == JTokenType.Boolean && (bool)jDup;

                using (var t = new Transaction(doc, "Swap Roof Layer Materials"))
                {
                    t.Start();

                    // duplicate (安全編集)
                    RoofType rt = rtSrc;
                    if (duplicateIfInUse)
                    {
                        var dup = rtSrc.Duplicate(rtSrc.Name + " (Edited " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ")") as ElementType;
                        if (dup is RoofType rtt) rt = rtt;
                    }

                    var cs = rt.GetCompoundStructure();
                    if (cs == null) { t.RollBack(); return new { ok = false, msg = $"CompoundStructure is null on RoofType: {rt.Name}" }; }

                    var layers = cs.GetLayers();
                    int swapped = 0;
                    string needle = contains!.Trim();

                    for (int i = 0; i < layers.Count; i++)
                    {
                        if (swapped >= maxCount) break;

                        var mid = layers[i].MaterialId;
                        if (mid == null || mid == ElementId.InvalidElementId) continue;

                        var m = doc.GetElement(mid) as Material;
                        var mname = m?.Name ?? "";
                        if (mname.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            cs.SetMaterialId(i, replaceMat.Id);
                            swapped++;
                        }
                    }

                    if (swapped == 0)
                    {
                        t.Commit();
                        return new
                        {
                            ok = true,
                            typeId = rt.Id.IntValue(),
                            swapped = 0,
                            note = duplicateIfInUse ? "Duplicated but no layers matched." : "No layers matched."
                        };
                    }

                    rt.SetCompoundStructure(cs);
                    t.Commit();

                    return new
                    {
                        ok = true,
                        typeId = rt.Id.IntValue(),
                        swapped = swapped,
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


