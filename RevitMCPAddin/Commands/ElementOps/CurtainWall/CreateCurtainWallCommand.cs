// File: RevitMCPAddin/Commands/ElementOps/CurtainWall/CreateCurtainWallCommand.cs
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    public class CreateCurtainWallCommand : IRevitCommandHandler
    {
        public string CommandName => "create_curtain_wall";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = (JObject)cmd.Params;

                // --- 1) Base Level 解決（Id→Name→最下レベル） ---
                ElementId baseLevelId = ElementId.InvalidElementId;
                if (p.TryGetValue("baseLevelId", out var blid)) baseLevelId = Autodesk.Revit.DB.ElementIdCompat.From(blid.Value<int>());
                else if (p.TryGetValue("levelId", out var lid)) baseLevelId = Autodesk.Revit.DB.ElementIdCompat.From(lid.Value<int>());

                Level baseLevel = null;
                if (baseLevelId != ElementId.InvalidElementId)
                    baseLevel = doc.GetElement(baseLevelId) as Level;

                if (baseLevel == null)
                {
                    string baseLevelName = null;
                    if (p.TryGetValue("baseLevelName", out var blname)) baseLevelName = blname.Value<string>();
                    else if (p.TryGetValue("levelName", out var lname)) baseLevelName = lname.Value<string>();

                    if (!string.IsNullOrWhiteSpace(baseLevelName))
                    {
                        baseLevel = new FilteredElementCollector(doc)
                            .OfClass(typeof(Level)).Cast<Level>()
                            .FirstOrDefault(l => l.Name.Equals(baseLevelName, StringComparison.OrdinalIgnoreCase));
                    }
                }
                if (baseLevel == null)
                {
                    baseLevel = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level)).Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .FirstOrDefault();
                    if (baseLevel == null) return new { ok = false, msg = "基準レベル(Level)が見つかりません。" };
                }

                // --- 2) baseline（mm→ft）: 2点直線前提 ---
                var baselineTok = p["baseline"];
                if (baselineTok == null || !baselineTok.Any())
                    return new { ok = false, msg = "baseline（2点以上）が必要です。" };

                var pts = baselineTok.Select(pt => UnitHelper.MmToXyz(
                    (double)pt["x"], (double)pt["y"], (double)pt["z"])
                ).ToList();

                if (pts.Count < 2)
                    return new { ok = false, msg = "baseline は少なくとも2点（start/end）が必要です。" };

                var curve = Line.CreateBound(pts[0], pts[1]);

                // --- 3) Curtain Wall Type 解決 ---
                WallType cwType = null;
                if (p.TryGetValue("typeName", out var tnameTok))
                {
                    var tname = tnameTok.Value<string>();
                    if (!string.IsNullOrWhiteSpace(tname))
                    {
                        cwType = new FilteredElementCollector(doc)
                            .OfClass(typeof(WallType)).Cast<WallType>()
                            .FirstOrDefault(t => t.Name.Equals(tname, StringComparison.OrdinalIgnoreCase)
                                              && t.Kind == WallKind.Curtain);
                    }
                }
                if (cwType == null && p.TryGetValue("typeId", out var tidTok))
                {
                    var cand = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tidTok.Value<int>())) as WallType;
                    if (cand != null && cand.Kind == WallKind.Curtain) cwType = cand;
                }
                if (cwType == null)
                {
                    cwType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType)).Cast<WallType>()
                        .FirstOrDefault(t => t.Kind == WallKind.Curtain);
                    if (cwType == null) return new { ok = false, msg = "Curtain Wall の WallType が見つかりません。" };
                }

                // --- 4) 高さモード（topLevel 指定 or level-to-level → 拘束 / それ以外は非拘束） ---
                var htToken = p["heightMm"];
                bool hasTopLevelParam = p.ContainsKey("topLevelId") || p.ContainsKey("topLevelName");
                bool isLevelToLevel = (htToken == null) ||
                                      (htToken.Type == JTokenType.String &&
                                       string.Equals(htToken.Value<string>(), "level-to-level", StringComparison.OrdinalIgnoreCase));

                // オフセット（mm→ft）
                double baseOffsetFt = UnitHelper.MmToFt(p.Value<double?>("baseOffsetMm") ?? 0.0);
                double topOffsetFt = UnitHelper.MmToFt(p.Value<double?>("topOffsetMm") ?? 0.0);

                // 非拘束時の Unconnected Height（mm→ft）
                double unconnHeightFt = UnitHelper.MmToFt(
                    (htToken != null && (htToken.Type == JTokenType.Integer || htToken.Type == JTokenType.Float))
                        ? htToken.Value<double>()
                        : 3000.0 // fallback 3m
                );

                // 上部拘束レベル解決（指定優先→直上階）
                Level topLevel = null;
                if (hasTopLevelParam || isLevelToLevel)
                {
                    if (p.TryGetValue("topLevelId", out var tlid))
                        topLevel = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(tlid.Value<int>())) as Level;

                    if (topLevel == null && p.TryGetValue("topLevelName", out var tlname))
                    {
                        var name = tlname.Value<string>();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            topLevel = new FilteredElementCollector(doc)
                                .OfClass(typeof(Level)).Cast<Level>()
                                .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        }
                    }

                    if (topLevel == null)
                    {
                        topLevel = new FilteredElementCollector(doc)
                            .OfClass(typeof(Level)).Cast<Level>()
                            .Where(l => l.Elevation > baseLevel.Elevation)
                            .OrderBy(l => l.Elevation)
                            .FirstOrDefault();
                    }
                }

                bool useTopConstraint = (topLevel != null);

                // --- 5) 作成＆拘束設定 ---
                using (var tx = new Transaction(doc, "Create Curtain Wall"))
                {
                    tx.Start();

                    var cw = Autodesk.Revit.DB.Wall.Create(
                        doc,
                        curve,
                        cwType.Id,
                        baseLevel.Id,
                        unconnHeightFt, // 非拘束時に使う高さ
                        baseOffsetFt,   // ベースオフセット
                        false,          // flip
                        false           // structural
                    );

                    // Base
                    var pBase = cw.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    var pBaseOff = cw.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                    if (pBase != null && !pBase.IsReadOnly) pBase.Set(baseLevel.Id);
                    if (pBaseOff != null && !pBaseOff.IsReadOnly) pBaseOff.Set(baseOffsetFt);

                    // Top / Unconnected
                    var pTop = cw.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                    var pTopOff = cw.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
                    var pUnconn = cw.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);

                    if (useTopConstraint)
                    {
                        if (pTop != null && !pTop.IsReadOnly) pTop.Set(topLevel.Id);
                        if (pTopOff != null && !pTopOff.IsReadOnly) pTopOff.Set(topOffsetFt);
                        if (pUnconn != null && !pUnconn.IsReadOnly) pUnconn.Set(0.0);
                    }
                    else
                    {
                        if (pTop != null && !pTop.IsReadOnly) pTop.Set(ElementId.InvalidElementId); // Unconnected
                        if (pTopOff != null && !pTopOff.IsReadOnly) pTopOff.Set(0.0);
                        if (pUnconn != null && !pUnconn.IsReadOnly) pUnconn.Set(unconnHeightFt);
                    }

                    tx.Commit();

                    return new
                    {
                        ok = true,
                        elementId = cw.Id.IntValue(),
                        typeId = cw.GetTypeId().IntValue(),
                        baseLevelId = baseLevel.Id.IntValue(),
                        topLevelId = useTopConstraint ? topLevel.Id.IntValue() : (int?)null,
                        mode = useTopConstraint ? "top-constrained" : "unconnected"
                    };
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}


