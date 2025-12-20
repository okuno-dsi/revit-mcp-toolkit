// ================================================================
// File: Commands/ElementOps/Wall/WallLayerCommands.cs  (UnitHelper対応版 / Revit 2023 / .NET Fx 4.8)
// 修正要点: すべての単位変換を UnitHelper に統一（Ft⇄Mm）
//          CompoundStructure.VariableLayerIndex を反射含めて安全管理
// ================================================================

using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    // UnitHelper を直接使用するため、WLUnits は不要

    internal sealed class WallTypeResolveResult
    {
        public Autodesk.Revit.DB.Wall WallInstance { get; set; }
        public WallType WallType { get; set; }
        public int? ElementId { get; set; }
        public string UniqueId { get; set; }
        public int TypeId => WallType?.Id.IntegerValue ?? 0;
        public string TypeName => WallType?.Name ?? string.Empty;
    }

    internal static class WallTypeResolver
    {
        public static WallTypeResolveResult Resolve(UIApplication uiapp, JObject p)
        {
            var doc = uiapp.ActiveUIDocument.Document;

            if (p.ContainsKey("elementId") || p.ContainsKey("wallId") || p.ContainsKey("uniqueId"))
            {
                Autodesk.Revit.DB.Wall w;
                int wallId; string uid; string err;
                if (!Commands.ElementOps.WallLookupUtil.TryGetWall(doc, new RequestCommand { Params = p }, out w, out wallId, out uid, out err))
                    throw new InvalidOperationException(err ?? "Wall not found.");

                var wt = doc.GetElement(w.GetTypeId()) as WallType;
                if (wt == null) throw new InvalidOperationException("WallType not found for the wall instance.");
                return new WallTypeResolveResult { WallInstance = w, WallType = wt, ElementId = wallId, UniqueId = uid };
            }

            WallType found = null;
            if (p.TryGetValue("typeId", out var tidTok))
                found = doc.GetElement(new ElementId(tidTok.Value<int>())) as WallType;
            else if (p.TryGetValue("typeName", out var tnTok))
            {
                var name = tnTok.Value<string>() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    found = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                        .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (found == null) throw new InvalidOperationException("WallType not found (typeId/typeName).");

            return new WallTypeResolveResult { WallInstance = null, WallType = found, ElementId = null, UniqueId = found.UniqueId };
        }

        public static WallType DuplicateIfRequested(Document doc, WallType src, JObject p, out string note)
        {
            note = null;
            bool dup = p.Value<bool?>("duplicateIfInUse") ?? false;
            if (!dup) return src;

            string baseName = src.Name + " (Copy)";
            string newName = baseName;
            int i = 2;
            while (new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                   .Any(t => t.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                newName = $"{baseName} {i++}";

            WallType dst;
            using (var tx = new Transaction(doc, "Duplicate WallType for Edit"))
            {
                tx.Start();
                dst = src.Duplicate(newName) as WallType;
                tx.Commit();
            }
            note = "Original type duplicated and edited.";
            return dst;
        }
    }

    internal sealed class LayerDto
    {
        public int index { get; set; }
        public string function { get; set; }
        public int materialId { get; set; }
        public string materialName { get; set; }
        public double thicknessMm { get; set; }
        public bool isCore { get; set; }
        public bool isVariable { get; set; }
    }

    internal static class CompoundHelpers
    {
        public static bool IsBasic(WallType wt) => wt != null && wt.Kind == WallKind.Basic;
        public static string KindString(WallType wt)
            => wt == null ? "" : (wt.Kind == WallKind.Basic ? "Basic" : wt.Kind == WallKind.Curtain ? "Curtain" : "Stacked");

        public static string SafeGetDataType(Parameter p)
        {
            try { return p.Definition?.GetDataType()?.ToString(); }
            catch { return null; }
        }

        public static (int firstCore, int lastCore) GetCoreRange(CompoundStructure cs)
        {
            if (cs == null) return (-1, -1);
            int f = -1, l = -1;
            try { f = cs.GetFirstCoreLayerIndex(); } catch { }
            try { l = cs.GetLastCoreLayerIndex(); } catch { }
            return (f, l);
        }

        public static (int extShell, int intShell) GetShellCounts(CompoundStructure cs, int layerCount)
        {
            var (f, l) = GetCoreRange(cs);
            if (f < 0 || l < 0) return (0, 0);
            return (Math.Max(0, f), Math.Max(0, (layerCount - 1) - l));
        }

        public static void RestoreShellCounts(CompoundStructure cs, int exterior, int interior, int newCount)
        {
            exterior = Math.Max(0, exterior);
            interior = Math.Max(0, interior);
            if (exterior + interior > Math.Max(0, newCount - 1))
            {
                int overflow = exterior + interior - (newCount - 1);
                if (interior >= overflow) interior -= overflow;
                else { overflow -= interior; interior = 0; exterior = Math.Max(0, exterior - overflow); }
            }
            cs.SetNumberOfShellLayers(ShellLayerType.Exterior, exterior);
            cs.SetNumberOfShellLayers(ShellLayerType.Interior, interior);
        }

        public static int GetVariableLayerIndex(CompoundStructure cs)
        {
            if (cs == null) return -1;
            try { return cs.VariableLayerIndex; } catch { }
            try
            {
                var t = cs.GetType();
                var mi = t.GetMethod("GetVariableLayerIndex", Type.EmptyTypes);
                if (mi != null)
                {
                    var val = mi.Invoke(cs, null);
                    if (val is int idx) return idx;
                }
            }
            catch { }
            return -1;
        }

        public static void SetVariableLayerIndex(CompoundStructure cs, int index)
        {
            if (cs == null) return;
            try { cs.VariableLayerIndex = index; return; } catch { }
            try
            {
                var t = cs.GetType();
                var mi = t.GetMethod("SetVariableLayerIndex", new[] { typeof(int) });
                if (mi != null) { mi.Invoke(cs, new object[] { index }); return; }
                if (index < 0)
                {
                    var miClear = t.GetMethod("ClearVariableLayerIndex", Type.EmptyTypes);
                    if (miClear != null) miClear.Invoke(cs, null);
                }
            }
            catch { }
        }
    }

    // 1) get_wall_type_info
    public class GetWallTypeInfoCommand : IRevitCommandHandler
    {
        public string CommandName => "get_wall_type_info";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (JObject)cmd.Params;
                var res = WallTypeResolver.Resolve(uiapp, p);
                var wt = res.WallType;

                double widthMm = 0.0;
                int layerCount = 0;
                if (CompoundHelpers.IsBasic(wt))
                {
                    var cs = wt.GetCompoundStructure();
                    layerCount = cs?.GetLayers()?.Count ?? 0;
                    widthMm = UnitHelper.FtToMm(wt.Width);
                }

                return new
                {
                    ok = true,
                    elementId = res.ElementId,
                    uniqueId = res.UniqueId,
                    typeId = wt.Id.IntegerValue,
                    typeName = wt.Name,
                    kind = CompoundHelpers.KindString(wt),
                    width = Math.Round(widthMm, 3),
                    layerCount,
                    units = new { input = new { Length = "mm" }, internalUnits = new { Length = "ft" } }
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // 2) get_wall_layers
    public class GetWallLayersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_wall_layers";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (JObject)cmd.Params;
                bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

                var res = WallTypeResolver.Resolve(uiapp, p);
                var wt = res.WallType;
                if (!CompoundHelpers.IsBasic(wt))
                    return new { ok = false, msg = "Target WallType is not Basic." };

                var doc = uiapp.ActiveUIDocument.Document;
                var cs = wt.GetCompoundStructure();
                var raw = cs.GetLayers();
                var (firstCore, lastCore) = CompoundHelpers.GetCoreRange(cs);
                int varIdx = CompoundHelpers.GetVariableLayerIndex(cs);

                var items = new List<LayerDto>();
                for (int i = 0; i < raw.Count; i++)
                {
                    var l = raw[i];
                    var mat = doc.GetElement(l.MaterialId) as Autodesk.Revit.DB.Material;
                    items.Add(new LayerDto
                    {
                        index = i,
                        function = l.Function.ToString(),
                        materialId = l.MaterialId.IntegerValue,
                        materialName = mat?.Name ?? string.Empty,
                        thicknessMm = Math.Round(UnitHelper.FtToMm(l.Width), 3),
                        isCore = (firstCore >= 0 && lastCore >= 0 && i >= firstCore && i <= lastCore),
                        isVariable = (i == varIdx)
                    });
                }

                object layersPayload = items;
                if (namesOnly)
                {
                    layersPayload = items
                        .Select(x => string.IsNullOrEmpty(x.materialName) ? $"({x.materialId})" : x.materialName)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(n => n.Length)
                        .ToList();
                }

                double totalThicknessMm = items.Sum(x => x.thicknessMm);

                int skip = p.Value<int?>("skip") ?? 0;
                int count = p.Value<int?>("count") ?? int.MaxValue;
                int totalCount = items.Count;

                if (skip == 0 && p.ContainsKey("count") && p.Value<int>("count") == 0)
                {
                    return new
                    {
                        ok = true,
                        elementId = res.ElementId,
                        uniqueId = res.UniqueId,
                        typeId = wt.Id.IntegerValue,
                        typeName = wt.Name,
                        totalCount,
                        inputUnits = new { Length = "mm" },
                        internalUnits = new { Length = "ft" }
                    };
                }

                var page = items.Skip(skip).Take(count).ToList();

                return new
                {
                    ok = true,
                    elementId = res.ElementId,
                    uniqueId = res.UniqueId,
                    typeId = wt.Id.IntegerValue,
                    typeName = wt.Name,
                    layers = page,
                    totalThicknessMm
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // 3) update_wall_layer
    public class UpdateWallLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "update_wall_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            try
            {
                var res = WallTypeResolver.Resolve(uiapp, p);
                var srcType = res.WallType;
                if (!CompoundHelpers.IsBasic(srcType))
                    return new { ok = false, msg = "Target WallType is not Basic." };

                string note;
                var editType = WallTypeResolver.DuplicateIfRequested(doc, srcType, p, out note);

                int layerIndex = p.Value<int>("layerIndex");
                double? newThkMm = p["thicknessMm"]?.Value<double?>();
                int? newMatId = p["materialId"]?.Value<int?>();
                string newMatName = p.Value<string>("materialName");

                using (var tx = new Transaction(doc, "Update Wall Layer"))
                {
                    tx.Start();

                    var cs = editType.GetCompoundStructure();
                    var layers = cs.GetLayers();
                    if (layerIndex < 0 || layerIndex >= layers.Count)
                        throw new InvalidOperationException("layerIndex out of range.");

                    var (extShell, intShell) = CompoundHelpers.GetShellCounts(cs, layers.Count);
                    int varIdx = CompoundHelpers.GetVariableLayerIndex(cs);

                    var l = layers[layerIndex];

                    if (newThkMm.HasValue) l.Width = UnitHelper.MmToFt(newThkMm.Value);

                    if (newMatId.HasValue)
                        l.MaterialId = new ElementId(newMatId.Value);
                    else if (!string.IsNullOrWhiteSpace(newMatName))
                    {
                        var mat = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Material))
                            .Cast<Autodesk.Revit.DB.Material>()
                            .FirstOrDefault(m => m.Name.Equals(newMatName, StringComparison.OrdinalIgnoreCase));
                        if (mat == null) throw new InvalidOperationException($"Material not found: {newMatName}");
                        l.MaterialId = mat.Id;
                    }

                    layers[layerIndex] = l;

                    cs.SetLayers(layers);
                    CompoundHelpers.RestoreShellCounts(cs, extShell, intShell, layers.Count);
                    CompoundHelpers.SetVariableLayerIndex(cs, varIdx);
                    editType.SetCompoundStructure(cs);

                    tx.Commit();

                    return new
                    {
                        ok = true,
                        typeId = editType.Id.IntegerValue,
                        note,
                        edited = new { layerIndex, thicknessMm = newThkMm, materialName = newMatName, materialId = newMatId }
                    };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // 4) add_wall_layer
    public class AddWallLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "add_wall_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            try
            {
                var res = WallTypeResolver.Resolve(uiapp, p);
                var srcType = res.WallType;
                if (!CompoundHelpers.IsBasic(srcType))
                    return new { ok = false, msg = "Target WallType is not Basic." };

                string note;
                var editType = WallTypeResolver.DuplicateIfRequested(doc, srcType, p, out note);

                int insertAt = p.Value<int?>("insertAt") ?? 0;
                string funcStr = p.Value<string>("function") ?? "Structure";
                Enum.TryParse(funcStr, true, out MaterialFunctionAssignment func);

                int matId = p.Value<int?>("materialId") ?? 0;
                string matName = p.Value<string>("materialName");
                Autodesk.Revit.DB.Material mat = null;
                if (matId > 0) mat = doc.GetElement(new ElementId(matId)) as Autodesk.Revit.DB.Material;
                if (mat == null && !string.IsNullOrWhiteSpace(matName))
                    mat = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Material))
                          .Cast<Autodesk.Revit.DB.Material>()
                          .FirstOrDefault(m => m.Name.Equals(matName, StringComparison.OrdinalIgnoreCase));
                if (mat == null) throw new InvalidOperationException("Material not found (materialId/materialName).");

                double thkMm = p.Value<double?>("thicknessMm") ?? 1.0;
                bool wantCore = p.Value<bool?>("isCore") ?? false;
                bool wantVariable = p.Value<bool?>("isVariable") ?? false;

                using (var tx = new Transaction(doc, "Add Wall Layer"))
                {
                    tx.Start();

                    var cs = editType.GetCompoundStructure();
                    var layers = cs.GetLayers();
                    var oldCount = layers.Count;

                    var (extShell, intShell) = CompoundHelpers.GetShellCounts(cs, oldCount);
                    int varIdx = CompoundHelpers.GetVariableLayerIndex(cs);
                    int interiorStartOld = oldCount - intShell;

                    insertAt = Math.Max(0, Math.Min(insertAt, oldCount));

                    var newLayer = new CompoundStructureLayer(UnitHelper.MmToFt(thkMm), func, mat.Id);
                    layers.Insert(insertAt, newLayer);

                    if (varIdx >= 0 && insertAt <= varIdx) varIdx++;

                    cs.SetLayers(layers);

                    int extNew = extShell;
                    int intNew = intShell;

                    if (!wantCore)
                    {
                        if (insertAt <= extShell) extNew = extShell + 1;
                        else if (insertAt >= interiorStartOld) intNew = intShell + 1;
                        else
                        {
                            if (insertAt - extShell <= interiorStartOld - insertAt) extNew = extShell + 1;
                            else intNew = intShell + 1;
                        }
                    }

                    CompoundHelpers.RestoreShellCounts(cs, extNew, intNew, layers.Count);

                    if (wantVariable) varIdx = insertAt;
                    CompoundHelpers.SetVariableLayerIndex(cs, varIdx);

                    editType.SetCompoundStructure(cs);

                    tx.Commit();

                    return new
                    {
                        ok = true,
                        typeId = editType.Id.IntegerValue,
                        newLayerIndex = insertAt,
                        totalThicknessMm = Math.Round(UnitHelper.FtToMm(editType.Width), 3),
                        note
                    };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // 5) remove_wall_layer
    public class RemoveWallLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "remove_wall_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            try
            {
                var res = WallTypeResolver.Resolve(uiapp, p);
                var srcType = res.WallType;
                if (!CompoundHelpers.IsBasic(srcType))
                    return new { ok = false, msg = "Target WallType is not Basic." };

                string note;
                var editType = WallTypeResolver.DuplicateIfRequested(doc, srcType, p, out note);

                int layerIndex = p.Value<int>("layerIndex");

                using (var tx = new Transaction(doc, "Remove Wall Layer"))
                {
                    tx.Start();

                    var cs = editType.GetCompoundStructure();
                    var layers = cs.GetLayers();
                    var oldCount = layers.Count;

                    if (oldCount <= 1) throw new InvalidOperationException("Cannot remove the last layer.");
                    if (layerIndex < 0 || layerIndex >= oldCount)
                        throw new InvalidOperationException("layerIndex out of range.");

                    var (extShell, intShell) = CompoundHelpers.GetShellCounts(cs, oldCount);
                    int varIdx = CompoundHelpers.GetVariableLayerIndex(cs);
                    int interiorStartOld = oldCount - intShell;

                    layers.RemoveAt(layerIndex);

                    if (varIdx == layerIndex) varIdx = -1;
                    else if (varIdx > layerIndex) varIdx--;

                    cs.SetLayers(layers);

                    int extNew = extShell;
                    int intNew = intShell;

                    if (layerIndex < extShell) extNew = Math.Max(0, extShell - 1);
                    else if (layerIndex >= interiorStartOld) intNew = Math.Max(0, intShell - 1);

                    CompoundHelpers.RestoreShellCounts(cs, extNew, intNew, layers.Count);
                    CompoundHelpers.SetVariableLayerIndex(cs, varIdx);

                    editType.SetCompoundStructure(cs);

                    tx.Commit();

                    return new
                    {
                        ok = true,
                        typeId = editType.Id.IntegerValue,
                        removedIndex = layerIndex,
                        totalThicknessMm = Math.Round(UnitHelper.FtToMm(editType.Width), 3),
                        note
                    };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // 6) set_wall_variable_layer
    public class SetWallVariableLayerCommand : IRevitCommandHandler
    {
        public string CommandName => "set_wall_variable_layer";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            try
            {
                var res = WallTypeResolver.Resolve(uiapp, p);
                var srcType = res.WallType;
                if (!CompoundHelpers.IsBasic(srcType))
                    return new { ok = false, msg = "Target WallType is not Basic." };

                string note;
                var editType = WallTypeResolver.DuplicateIfRequested(doc, srcType, p, out note);

                int layerIndex = p.Value<int>("layerIndex");
                bool flag = p.Value<bool>("isVariable");

                using (var tx = new Transaction(doc, "Set Variable Layer"))
                {
                    tx.Start();

                    var cs = editType.GetCompoundStructure();
                    var layers = cs.GetLayers();
                    if (layerIndex < 0 || layerIndex >= layers.Count)
                        throw new InvalidOperationException("layerIndex out of range.");

                    var (extShell, intShell) = CompoundHelpers.GetShellCounts(cs, layers.Count);
                    int varIdx = CompoundHelpers.GetVariableLayerIndex(cs);

                    if (flag) varIdx = layerIndex;
                    else if (varIdx == layerIndex) varIdx = -1;

                    CompoundHelpers.RestoreShellCounts(cs, extShell, intShell, layers.Count);
                    CompoundHelpers.SetVariableLayerIndex(cs, varIdx);
                    editType.SetCompoundStructure(cs);

                    tx.Commit();
                    return new { ok = true, note };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // 7) swap_wall_layer_materials
    public class SwapWallLayerMaterialsCommand : IRevitCommandHandler
    {
        public string CommandName => "swap_wall_layer_materials";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params;

            try
            {
                var res = WallTypeResolver.Resolve(uiapp, p);
                var srcType = res.WallType;
                if (!CompoundHelpers.IsBasic(srcType))
                    return new { ok = false, msg = "Target WallType is not Basic." };

                string note;
                var editType = WallTypeResolver.DuplicateIfRequested(doc, srcType, p, out note);

                string contains = p["find"]?["materialNameContains"]?.Value<string>();
                int? findId = p["find"]?["materialId"]?.Value<int?>();
                int maxCount = p.Value<int?>("maxCount") ?? int.MaxValue;

                int? repId = p["replace"]?["materialId"]?.Value<int?>();
                string repName = p["replace"]?["materialName"]?.Value<string>();

                Autodesk.Revit.DB.Material repMat = null;
                if (repId.HasValue) repMat = doc.GetElement(new ElementId(repId.Value)) as Autodesk.Revit.DB.Material;
                if (repMat == null && !string.IsNullOrWhiteSpace(repName))
                    repMat = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Material))
                             .Cast<Autodesk.Revit.DB.Material>()
                             .FirstOrDefault(m => m.Name.Equals(repName, StringComparison.OrdinalIgnoreCase));
                if (repMat == null) return new { ok = false, msg = "Replacement material not found." };

                int swapped = 0;

                using (var tx = new Transaction(doc, "Swap Wall Layer Materials"))
                {
                    tx.Start();

                    var cs = editType.GetCompoundStructure();
                    var layers = cs.GetLayers();

                    var (extShell, intShell) = CompoundHelpers.GetShellCounts(cs, layers.Count);
                    int varIdx = CompoundHelpers.GetVariableLayerIndex(cs);

                    for (int i = 0; i < layers.Count && swapped < maxCount; i++)
                    {
                        var l = layers[i];
                        var cur = doc.GetElement(l.MaterialId) as Autodesk.Revit.DB.Material;
                        bool match =
                            (findId.HasValue && l.MaterialId.IntegerValue == findId.Value) ||
                            (!string.IsNullOrEmpty(contains) && cur != null &&
                             cur.Name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0);

                        if (match)
                        {
                            l.MaterialId = repMat.Id;
                            layers[i] = l;
                            swapped++;
                        }
                    }

                    if (swapped == 0)
                    {
                        tx.RollBack();
                        return new { ok = true, swapped = 0, note = "No matching layers." };
                    }

                    cs.SetLayers(layers);
                    CompoundHelpers.RestoreShellCounts(cs, extShell, intShell, layers.Count);
                    CompoundHelpers.SetVariableLayerIndex(cs, varIdx);
                    editType.SetCompoundStructure(cs);

                    tx.Commit();
                    return new { ok = true, swapped, note };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}
