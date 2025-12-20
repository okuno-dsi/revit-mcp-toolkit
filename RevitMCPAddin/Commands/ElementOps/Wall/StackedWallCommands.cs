// ================================================================
// File: Commands/ElementOps/Wall/StackedWallCommands.cs (UnitHelper対応版)
// Target : Revit 2023 / .NET Framework 4.8
// Notes  : Stacked Wall Type（子壁タイプ/高さ）の取得・更新と、Basic化の近似生成
//          ・elementId / wallId / uniqueId でインスタンス → Type を解決
//          ・typeId / typeName で Type を直接解決
//          ・高さは mm 入出力（内部 ft）
//          ・APIの揺れに備え、Reflection で複数候補メソッド名を安全呼び出し
//          ・UnitUtils 直呼び出しを排し、UnitHelper に統一
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Wall
{
    // ------------------------------
    // 共通ユーティリティ（UnitHelper に統一）
    // ------------------------------
    internal static class StackedWallUtil
    {
        public static double FtToMm(double ft) => UnitHelper.FtToMm(ft);
        public static double MmToFt(double mm) => UnitHelper.MmToFt(mm);

        public static (WallType type, string msg) ResolveWallType(Document doc, JObject p)
        {
            // 1) インスタンス指定（elementId / wallId / uniqueId）
            int instId = p.Value<int?>("elementId") ?? p.Value<int?>("wallId") ?? 0;
            if (instId > 0)
            {
                var w = doc.GetElement(new ElementId(instId)) as Autodesk.Revit.DB.Wall;
                if (w == null) return (null, $"Wall not found: {instId}");
                var wt = doc.GetElement(w.GetTypeId()) as WallType;
                if (wt == null) return (null, "WallType not found from instance.");
                return (wt, null);
            }
            var uid = p.Value<string>("uniqueId");
            if (!string.IsNullOrWhiteSpace(uid))
            {
                var w = doc.GetElement(uid) as Autodesk.Revit.DB.Wall;
                if (w == null) return (null, $"Wall not found (uniqueId): {uid}");
                var wt = doc.GetElement(w.GetTypeId()) as WallType;
                if (wt == null) return (null, "WallType not found from instance (uniqueId).");
                return (wt, null);
            }

            // 2) タイプ指定（typeId / typeName）
            int tid = p.Value<int?>("typeId") ?? 0;
            if (tid > 0)
            {
                var wt = doc.GetElement(new ElementId(tid)) as WallType;
                if (wt == null) return (null, $"WallType not found: {tid}");
                return (wt, null);
            }
            var tname = p.Value<string>("typeName");
            if (!string.IsNullOrWhiteSpace(tname))
            {
                var wt = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(x => string.Equals(x.Name, tname, StringComparison.OrdinalIgnoreCase));
                if (wt == null) return (null, $"WallType not found by name: {tname}");
                return (wt, null);
            }

            return (null, "Specify elementId/wallId/uniqueId OR typeId/typeName.");
        }

        public static bool EnsureStacked(WallType wt, out string msg)
        {
            msg = null;
            if (wt == null) { msg = "WallType is null."; return false; }
            if (wt.Kind != WallKind.Stacked)
            {
                msg = $"WallType '{wt.Name}' is not Stacked (kind={wt.Kind}).";
                return false;
            }
            return true;
        }
    }

    // ------------------------------
    // Reflection ラッパ（スタック壁APIの揺れに対応）
    // ------------------------------
    internal sealed class StackedWallApi
    {
        private readonly object _swt;
        private readonly Type _type;

        private readonly MethodInfo _mGetCount;
        private readonly MethodInfo _mGetTypeId;
        private readonly MethodInfo _mSetTypeId;
        private readonly MethodInfo _mGetHeight;
        private readonly MethodInfo _mSetHeight;

        public static bool TryCreate(WallType wt, out StackedWallApi api, out string reason)
        {
            api = null; reason = null;
            if (wt == null) { reason = "WallType null."; return false; }

            var t = wt.GetType();
            if (!string.Equals(t.Name, "StackedWallType", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Type '{t.FullName}' is not StackedWallType.";
                return false;
            }

            var countCandidates = new[] { "GetMemberCount", "GetStackedMemberCount", "GetNumberOfBaseWallTypes" };
            var getTypeIdCand = new[] { "GetMemberTypeId", "GetBaseWallTypeId", "GetStackedMemberTypeId" };
            var setTypeIdCand = new[] { "SetMemberTypeId", "SetBaseWallTypeId", "SetStackedMemberTypeId" };
            var getHeightCand = new[] { "GetMemberHeight", "GetStackedMemberHeight", "GetPartHeight", "GetMemberOffset" };
            var setHeightCand = new[] { "SetMemberHeight", "SetStackedMemberHeight", "SetPartHeight", "SetMemberOffset" };

            MethodInfo Find(string[] names, Type ret, params Type[] args)
            {
                foreach (var n in names)
                {
                    var mi = t.GetMethod(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
                    if (mi != null && (ret == null || mi.ReturnType == ret)) return mi;
                }
                return null;
            }

            var mCount = Find(countCandidates, typeof(int), Type.EmptyTypes);
            var mGetTid = Find(getTypeIdCand, typeof(ElementId), new[] { typeof(int) });
            var mSetTid = Find(setTypeIdCand, typeof(void), new[] { typeof(int), typeof(ElementId) });
            var mGetHt = Find(getHeightCand, typeof(double), new[] { typeof(int) });
            var mSetHt = Find(setHeightCand, typeof(void), new[] { typeof(int), typeof(double) });

            if (mCount == null || mGetTid == null)
            {
                reason = "Stacked Wall API not available (count/typeId methods not found).";
                return false;
            }

            api = new StackedWallApi(wt, t, mCount, mGetTid, mSetTid, mGetHt, mSetHt);
            return true;
        }

        private StackedWallApi(object swt, Type type, MethodInfo mc, MethodInfo gtid, MethodInfo stid, MethodInfo ght, MethodInfo sht)
        {
            _swt = swt; _type = type;
            _mGetCount = mc; _mGetTypeId = gtid; _mSetTypeId = stid; _mGetHeight = ght; _mSetHeight = sht;
        }

        public int GetCount() => (int)_mGetCount.Invoke(_swt, null);
        public ElementId GetMemberTypeId(int index) => (ElementId)_mGetTypeId.Invoke(_swt, new object[] { index });

        public bool TrySetMemberTypeId(int index, ElementId id, out string msg)
        {
            msg = null;
            if (_mSetTypeId == null) { msg = "SetMemberTypeId API not found on this Revit version."; return false; }
            _mSetTypeId.Invoke(_swt, new object[] { index, id });
            return true;
        }

        public bool TryGetMemberHeightFt(int index, out double ft, out string msg)
        {
            msg = null; ft = 0;
            if (_mGetHeight == null) { msg = "GetMemberHeight API not found on this Revit version."; return false; }
            ft = (double)_mGetHeight.Invoke(_swt, new object[] { index });
            return true;
        }

        public bool TrySetMemberHeightFt(int index, double ft, out string msg)
        {
            msg = null;
            if (_mSetHeight == null) { msg = "SetMemberHeight API not found on this Revit version."; return false; }
            _mSetHeight.Invoke(_swt, new object[] { index, ft });
            return true;
        }
    }

    // 1) get_stacked_wall_parts
    public class GetStackedWallPartsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_stacked_wall_parts";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();

            var (wt, err) = StackedWallUtil.ResolveWallType(doc, p);
            if (wt == null) return new { ok = false, msg = err };
            if (!StackedWallUtil.EnsureStacked(wt, out var msg)) return new { ok = false, msg };

            if (!StackedWallApi.TryCreate(wt, out var api, out var reason))
                return new { ok = false, msg = reason };

            var parts = new List<object>();
            double totalFt = 0.0;

            int n = api.GetCount();
            for (int i = 0; i < n; i++)
            {
                var ctId = api.GetMemberTypeId(i);
                string ctName = doc.GetElement(ctId)?.Name ?? "";
                double hFt = 0.0;
                string hMsg;
                if (api.TryGetMemberHeightFt(i, out hFt, out hMsg))
                {
                    totalFt += hFt;
                    parts.Add(new
                    {
                        index = i,
                        childTypeId = ctId.IntegerValue,
                        childTypeName = ctName,
                        heightMm = Math.Round(StackedWallUtil.FtToMm(hFt), 3)
                    });
                }
                else
                {
                    parts.Add(new
                    {
                        index = i,
                        childTypeId = ctId.IntegerValue,
                        childTypeName = ctName,
                        heightMm = (double?)null,
                        note = hMsg
                    });
                }
            }

            return new
            {
                ok = true,
                typeId = wt.Id.IntegerValue,
                typeName = wt.Name,
                parts,
                totalHeightMm = Math.Round(StackedWallUtil.FtToMm(totalFt), 3),
                inputUnits = new { Length = "mm" },
                internalUnits = new { Length = "ft" }
            };
        }
    }

    // 2) update_stacked_wall_part
    public class UpdateStackedWallPartCommand : IRevitCommandHandler
    {
        public string CommandName => "update_stacked_wall_part";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();

            var (wt, err) = StackedWallUtil.ResolveWallType(doc, p);
            if (wt == null) return new { ok = false, msg = err };
            if (!StackedWallUtil.EnsureStacked(wt, out var msg)) return new { ok = false, msg };

            if (!StackedWallApi.TryCreate(wt, out var api, out var reason))
                return new { ok = false, msg = reason };

            int index = p.Value<int?>("partIndex") ?? p.Value<int?>("layerIndex") ?? -1;
            if (index < 0) return new { ok = false, msg = "partIndex is required (0-based)." };

            bool didAny = false;
            string note = null;

            using (var tx = new Transaction(doc, "Update Stacked Wall Part"))
            {
                tx.Start();

                // 高さ（mm → ft）
                if (p.TryGetValue("heightMm", out var hTok))
                {
                    double mm = hTok.Value<double>();
                    double ft = StackedWallUtil.MmToFt(mm);
                    if (!api.TrySetMemberHeightFt(index, ft, out var hMsg))
                        note = AppendNote(note, hMsg);
                    else
                        didAny = true;
                }

                // 子タイプ差し替え
                ElementId newChildId = ElementId.InvalidElementId;
                if (p.TryGetValue("childTypeId", out var ctIdTok))
                {
                    newChildId = new ElementId(ctIdTok.Value<int>());
                }
                else if (p.TryGetValue("childTypeName", out var ctNameTok))
                {
                    string name = ctNameTok.Value<string>();
                    var cand = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                             x.Kind == WallKind.Basic);
                    if (cand != null) newChildId = cand.Id;
                    else note = AppendNote(note, $"Child WallType not found by name: {name}");
                }

                if (newChildId != ElementId.InvalidElementId)
                {
                    if (!api.TrySetMemberTypeId(index, newChildId, out var tMsg))
                        note = AppendNote(note, tMsg);
                    else
                        didAny = true;
                }

                tx.Commit();
            }

            return new { ok = didAny, typeId = wt.Id.IntegerValue, partIndex = index, note };
        }

        private static string AppendNote(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return a + "; " + b;
        }
    }

    // 3) replace_stacked_wall_part_type
    public class ReplaceStackedWallPartTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "replace_stacked_wall_part_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();

            var (wt, err) = StackedWallUtil.ResolveWallType(doc, p);
            if (wt == null) return new { ok = false, msg = err };
            if (!StackedWallUtil.EnsureStacked(wt, out var msg)) return new { ok = false, msg };

            if (!StackedWallApi.TryCreate(wt, out var api, out var reason))
                return new { ok = false, msg = reason };

            int index = p.Value<int?>("partIndex") ?? -1;
            if (index < 0) return new { ok = false, msg = "partIndex is required (0-based)." };

            ElementId newChildId = ElementId.InvalidElementId;
            if (p.TryGetValue("childTypeId", out var ctIdTok))
            {
                newChildId = new ElementId(ctIdTok.Value<int>());
            }
            else if (p.TryGetValue("childTypeName", out var ctNameTok))
            {
                string name = ctNameTok.Value<string>();
                var cand = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                         x.Kind == WallKind.Basic);
                if (cand != null) newChildId = cand.Id;
                else return new { ok = false, msg = $"Child WallType not found by name: {name}" };
            }
            if (newChildId == ElementId.InvalidElementId)
                return new { ok = false, msg = "childTypeId/childTypeName is required." };

            using (var tx = new Transaction(doc, "Replace Stacked Wall Part Type"))
            {
                tx.Start();
                if (!api.TrySetMemberTypeId(index, newChildId, out var tMsg))
                {
                    tx.RollBack();
                    return new { ok = false, msg = tMsg };
                }
                tx.Commit();
            }

            var newName = doc.GetElement(newChildId)?.Name ?? "";
            return new { ok = true, typeId = wt.Id.IntegerValue, partIndex = index, childTypeId = newChildId.IntegerValue, childTypeName = newName };
        }
    }

    // 4) flatten_stacked_wall_to_basic
    public class FlattenStackedWallToBasicCommand : IRevitCommandHandler
    {
        public string CommandName => "flatten_stacked_wall_to_basic";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument.Document;
            var p = (JObject)cmd.Params ?? new JObject();

            var (wt, err) = StackedWallUtil.ResolveWallType(doc, p);
            if (wt == null) return new { ok = false, msg = err };
            if (!StackedWallUtil.EnsureStacked(wt, out var msg)) return new { ok = false, msg };

            if (!StackedWallApi.TryCreate(wt, out var api, out var reason))
                return new { ok = false, msg = reason };

            string newName = p.Value<string>("newTypeName");
            if (string.IsNullOrWhiteSpace(newName))
                newName = $"Flattened from {wt.Name} ({wt.Id.IntegerValue})";

            int n = api.GetCount();
            var childBasicTypes = new List<WallType>();
            double maxThicknessFt = 0.0;

            for (int i = 0; i < n; i++)
            {
                var ctId = api.GetMemberTypeId(i);
                var cwt = doc.GetElement(ctId) as WallType;
                if (cwt == null || cwt.Kind != WallKind.Basic) continue;
                childBasicTypes.Add(cwt);
                maxThicknessFt = Math.Max(maxThicknessFt, cwt.Width);
            }

            if (childBasicTypes.Count == 0)
                return new { ok = false, msg = "No Basic child types found to flatten." };

            var template = childBasicTypes[0];

            WallType newBasic;
            using (var tx = new Transaction(doc, "Flatten Stacked Wall → Basic"))
            {
                tx.Start();

                newBasic = template.Duplicate(newName) as WallType;
                if (newBasic == null)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "Failed to duplicate base Basic WallType." };
                }

                try
                {
                    var cs = newBasic.GetCompoundStructure();
                    if (cs != null)
                    {
                        var layers = cs.GetLayers().ToList();
                        if (layers.Count == 0)
                        {
                            var newLayers = new List<CompoundStructureLayer>
                            {
                                new CompoundStructureLayer(maxThicknessFt, MaterialFunctionAssignment.Structure, ElementId.InvalidElementId)
                            };
                            cs.SetLayers(newLayers);
                        }
                        else
                        {
                            double sum = layers.Sum(x => x.Width);
                            if (sum <= 1e-9) sum = 1.0; // ゼロ割回避
                            for (int i = 0; i < layers.Count; i++)
                            {
                                if (i < layers.Count - 1)
                                    layers[i].Width = maxThicknessFt * (layers[i].Width / sum);
                                else
                                    layers[i].Width = maxThicknessFt - layers.Take(i).Sum(l => l.Width);
                            }
                            cs.SetLayers(layers);
                        }
                        newBasic.SetCompoundStructure(cs);
                    }
                }
                catch { /* 厚み近似失敗は致命ではない */ }

                tx.Commit();
            }

            return new { ok = true, newBasicTypeId = newBasic.Id.IntegerValue, note = "Approximation of materials/width only." };
        }
    }
}
