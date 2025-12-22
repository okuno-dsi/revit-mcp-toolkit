// File: RevitMCPAddin/Commands/ElementOps/CurtainWall/GetCurtainWallScheduleCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Commands.ElementOps.CurtainWall;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    /// <summary>
    /// カーテンウォールのガラス面積・マリオン長さなど集計（方式選択：param/geom/bbox）
    /// 追加オプション:
    /// - areaMethod: "param"|"geom"|"bbox"（既定 "param"）
    /// - filterPanelMaterialContains: "Glass" 等（任意）
    /// - includeBreakdown: true で各パネル/マリオンの内訳も返す
    /// </summary>
    public class GetCurtainWallScheduleCommand : IRevitCommandHandler
    {
        public string CommandName => "get_curtain_wall_schedule";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            // 対象ウォール（elementId/uniqueId）
            var wall = CurtainUtil.ResolveCurtainWall(doc, p);
            if (wall == null) return new { ok = false, msg = "Curtain wall が見つからないか、CurtainGrid がありません。" };

            var grid = wall.CurtainGrid;
            if (grid == null) return new { ok = false, msg = "Curtain grid が見つかりません。" };

            // オプション
            string areaMethod = (p.Value<string>("areaMethod") ?? "param").ToLowerInvariant(); // param|geom|bbox
            string matFilter = p.Value<string>("filterPanelMaterialContains"); // 材質名の部分一致
            bool includeBreakdown = p.Value<bool?>("includeBreakdown") ?? false;

            Func<Element, bool> panelMaterialPredicate = (panelEl) =>
            {
                if (string.IsNullOrWhiteSpace(matFilter)) return true;
                try
                {
                    var mids = panelEl.GetMaterialIds(false);
                    foreach (var mid in mids)
                    {
                        var m = doc.GetElement(mid) as Autodesk.Revit.DB.Material;
                        if (m != null && (m.Name ?? "").IndexOf(matFilter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                }
                catch { }
                return false;
            };

            // パネル面積（m2）
            double totalPanelAreaM2 = 0.0;
            var panelBreakdown = includeBreakdown ? new List<object>() : null;

            foreach (var pid in grid.GetPanelIds())
            {
                var panel = doc.GetElement(pid);
                if (panel == null) continue;
                if (!panelMaterialPredicate(panel)) continue;

                double areaFt2 = 0.0;

                if (areaMethod == "param")
                {
                    // HOST_AREA_COMPUTED を優先
                    var pArea = panel.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    if (pArea != null && pArea.StorageType == StorageType.Double)
                        areaFt2 = pArea.AsDouble();
                    else
                        areaMethod = "geom"; // 次善へ自動フォールバック
                }

                if (areaMethod == "geom")
                {
                    try
                    {
                        var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };
                        var geo = panel.get_Geometry(opt);
                        if (geo != null)
                        {
                            foreach (var obj in geo)
                            {
                                var sol = obj as Solid;
                                if (sol == null || sol.Faces == null) continue;
                                foreach (Autodesk.Revit.DB.Face f in sol.Faces)
                                {
                                    // 垂直面（法線のZが小さい）を合算（外装面の投影面積に近い）
                                    var pf = f as PlanarFace;
                                    if (pf != null)
                                    {
                                        var n = pf.FaceNormal;
                                        if (Math.Abs(n.Z) < 0.5) areaFt2 += pf.Area; // 強い水平面は除外
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (areaMethod == "bbox" || areaFt2 <= 1e-9)
                {
                    // バウンディングボックス近似 (X,Y方向)
                    try
                    {
                        var bbox = panel.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            double w = bbox.Max.X - bbox.Min.X;
                            double h = bbox.Max.Y - bbox.Min.Y;
                            if (w > 0 && h > 0) areaFt2 = w * h;
                        }
                    }
                    catch { }
                }

                double areaM2 = CurtainUtil.Ft2ToM2(areaFt2);
                totalPanelAreaM2 += areaM2;

                if (includeBreakdown)
                {
                    string typeName = (doc.GetElement(panel.GetTypeId()) as ElementType)?.Name ?? "";
                    panelBreakdown.Add(new { panelId = pid.IntValue(), typeName, areaM2 = Math.Round(areaM2, 3) });
                }
            }

            // マリオン長さ合計（m）
            double totalMullionLengthM = 0.0;
            var mullionBreakdown = includeBreakdown ? new List<object>() : null;

            foreach (var mid in grid.GetMullionIds())
            {
                var mull = doc.GetElement(mid) as Mullion;
                if (mull == null) continue;
                double lenFt = 0.0;
                try
                {
                    var c = (mull.Location as LocationCurve)?.Curve;
                    if (c != null) lenFt = c.Length;
                }
                catch { }
                double lenM = CurtainUtil.FtToM(lenFt);
                totalMullionLengthM += lenM;

                if (includeBreakdown)
                {
                    string mullType = (doc.GetElement(mull.GetTypeId()) as ElementType)?.Name ?? "";
                    mullionBreakdown.Add(new { mullionId = mid.IntValue(), typeName = mullType, lengthM = Math.Round(lenM, 3) });
                }
            }

            // 返却
            return new
            {
                ok = true,
                elementId = wall.Id.IntValue(),
                uniqueId = wall.UniqueId,
                typeId = wall.GetTypeId().IntValue(),
                typeName = (doc.GetElement(wall.GetTypeId()) as ElementType)?.Name ?? "",
                schedule = new
                {
                    panelAreaM2 = Math.Round(totalPanelAreaM2, 3),
                    mullionLengthM = Math.Round(totalMullionLengthM, 3),
                    areaMethod = areaMethod
                },
                breakdown = includeBreakdown ? new { panels = panelBreakdown, mullions = mullionBreakdown } : null,
                units = CurtainUtil.ScheduleUnits()
            };
        }
    }
}

