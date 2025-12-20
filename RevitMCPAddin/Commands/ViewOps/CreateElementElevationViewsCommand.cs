using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// JSON-RPC: create_element_elevation_views
    /// 任意の要素 ID 群に対して、要素正面を向いた立面/断面ビューを自動生成する。
    /// </summary>
    public class CreateElementElevationViewsCommand : IRevitCommandHandler
    {
        public string CommandName => "create_element_elevation_views";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp != null && uiapp.ActiveUIDocument != null
                ? uiapp.ActiveUIDocument.Document
                : null;
            if (doc == null)
            {
                return new
                {
                    ok = false,
                    msg = "アクティブドキュメントがありません。"
                };
            }

            var p = cmd.Params as JObject ?? new JObject();

            // elementIds
            var idsToken = p["elementIds"] as JArray;
            if (idsToken == null || idsToken.Count == 0)
            {
                return new { ok = false, msg = "elementIds 配列を指定してください。" };
            }

            var elementIds = new List<ElementId>();
            foreach (var t in idsToken)
            {
                try
                {
                    int id = t.Type == JTokenType.Object
                        ? ((JObject)t).Value<int>("elementId")
                        : t.Value<int>();
                    if (id > 0)
                        elementIds.Add(new ElementId(id));
                }
                catch
                {
                    // ignore invalid entry
                }
            }
            if (elementIds.Count == 0)
            {
                return new { ok = false, msg = "有効な elementIds がありません。" };
            }

            // mode
            // 一旦 SectionBox ベースの断面ビューを既定とし、ElevationMarker は明示指定時のみ使用
            string modeStr = (p.Value<string>("mode") ?? "SectionBox").Trim();
            string mode = string.Equals(modeStr, "ElevationMarker", StringComparison.OrdinalIgnoreCase)
                ? "ElevationMarker"
                : "SectionBox";

            // orientation
            var orientationObj = p["orientation"] as JObject ?? new JObject();
            string orientationMode = (orientationObj.Value<string>("mode") ?? "Front").Trim();
            orientationMode = string.IsNullOrWhiteSpace(orientationMode) ? "Front" : orientationMode;

            // ドア用: 室内/室外どちら側から見るか（"Exterior" | "Interior"）。既定は室外から。
            string doorViewSide = (orientationObj.Value<string>("doorViewSide") ?? "Exterior").Trim();
            if (string.IsNullOrWhiteSpace(doorViewSide))
            {
                doorViewSide = "Exterior";
            }

            // 奥行きモード:
            //  - "Auto"              : デフォルト。ドア/窓/壁/カーテンウォールでは DoorWidthPlusMargin を標準適用。
            //  - "DoorWidthPlusMargin": ドア幅（壁厚）+マージンまでの奥行きに制限。
            //  - その他/空文字       : "Auto" と同義。
            string depthMode = (p.Value<string>("depthMode") ?? "Auto").Trim();
            if (string.IsNullOrWhiteSpace(depthMode))
            {
                depthMode = "Auto";
            }

            XYZ customDir = null;
            var customDirObj = orientationObj["customDirection"] as JObject;
            if (customDirObj != null)
            {
                try
                {
                    double dx = customDirObj.Value<double?>("x") ?? 0.0;
                    double dy = customDirObj.Value<double?>("y") ?? 0.0;
                    double dz = customDirObj.Value<double?>("z") ?? 0.0;
                    var v = new XYZ(dx, dy, dz);
                    if (!v.IsZeroLength())
                    {
                        customDir = v.Normalize();
                    }
                }
                catch { }
            }

            bool isolateTargets = p.Value<bool?>("isolateTargets") ?? true;

            string templateName = (p.Value<string>("viewTemplateName") ?? string.Empty).Trim();
            string viewFamilyTypeName = (p.Value<string>("viewFamilyTypeName") ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(viewFamilyTypeName))
            {
                // Elevation 用の推奨既定名（SectionBox では名前一致を必須とはしない）
                viewFamilyTypeName = "MCP_Elevation";
            }

            int viewScale = p.Value<int?>("viewScale") ?? 50;
            if (viewScale <= 0) viewScale = 50;

            double cropMarginMm = p.Value<double?>("cropMargin_mm") ?? 200.0;
            if (cropMarginMm < 0) cropMarginMm = 0;

            double offsetDistanceMm = p.Value<double?>("offsetDistance_mm") ?? 1500.0;
            if (offsetDistanceMm < 0) offsetDistanceMm = 0;

            // ViewFamilyType 解決
            var vftCandidates = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .ToList();

            ViewFamilyType vft = null;

            // 名前指定がある場合はまず名前一致を試すが、SectionBox のときは ViewFamily.Section のみ許可
            if (!string.IsNullOrEmpty(viewFamilyTypeName))
            {
                var byName = vftCandidates
                    .FirstOrDefault(v => string.Equals(v.Name ?? string.Empty, viewFamilyTypeName, StringComparison.OrdinalIgnoreCase));

                if (byName != null)
                {
                    if (mode == "SectionBox")
                    {
                        if (byName.ViewFamily == ViewFamily.Section)
                        {
                            vft = byName;
                        }
                    }
                    else
                    {
                        vft = byName;
                    }
                }
            }

            // 見つからなければ、モードに応じて ViewFamily から既定タイプを解決
            if (vft == null)
            {
                ViewFamily family = mode == "SectionBox" ? ViewFamily.Section : ViewFamily.Elevation;
                vft = vftCandidates.FirstOrDefault(v => v.ViewFamily == family);
            }

            if (vft == null)
            {
                return new { ok = false, msg = "指定された ViewFamilyType が見つからず、既定の Elevation/Section タイプも解決できませんでした。" };
            }

            // ViewTemplate 解決（任意）
            ElementId templateId = ElementId.InvalidElementId;
            if (!string.IsNullOrEmpty(templateName))
            {
                var tmpl = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v => v.IsTemplate && string.Equals(v.Name ?? string.Empty, templateName, StringComparison.OrdinalIgnoreCase));
                if (tmpl != null)
                {
                    templateId = tmpl.Id;
                }
            }

            var created = new List<object>();
            var skipped = new List<object>();

            // ドアの室内/室外判定用に、Room のスナップショットを事前取得（必要な場合のみ使用）
            List<Autodesk.Revit.DB.Architecture.Room> allRooms = null;

            using (var tx = new Transaction(doc, "Create Element Elevation Views"))
            {
                tx.Start();

                foreach (var eid in elementIds)
                {
                    var elem = doc.GetElement(eid);
                    if (elem == null)
                    {
                        skipped.Add(new { elementId = eid.IntegerValue, reason = "要素が見つかりませんでした。" });
                        continue;
                    }

                    // 代表点（Location または BoundingBox）
                    string refMsg;
                    var refPt = SpatialUtils.GetReferencePoint(doc, elem, out refMsg);
                    if (refPt == null)
                    {
                        skipped.Add(new { elementId = eid.IntegerValue, reason = "代表点を取得できませんでした。" });
                        continue;
                    }

                    // 向きベクトル
                    string orientationError;
                    var dir = ResolveDirection(elem, orientationMode, customDir, out orientationError);
                    if (dir == null || dir.IsZeroLength())
                    {
                        skipped.Add(new { elementId = eid.IntegerValue, reason = orientationError ?? "有効な向きベクトルを決定できませんでした。" });
                        continue;
                    }

                    // ドアの場合は、室内/室外の情報を使って向きを補正する
                    var fiDoor = elem as FamilyInstance;
                    if (fiDoor != null &&
                        fiDoor.Category != null &&
                        fiDoor.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors)
                    {
                        try
                        {
                            if (allRooms == null)
                            {
                                allRooms = new FilteredElementCollector(doc)
                                    .OfCategory(BuiltInCategory.OST_Rooms)
                                    .WhereElementIsNotElementType()
                                    .Cast<Autodesk.Revit.DB.Architecture.Room>()
                                    .ToList();
                            }

                            XYZ interiorDir, exteriorDir;
                            if (TryComputeDoorSideDirections(doc, fiDoor, allRooms, out interiorDir, out exteriorDir))
                            {
                                // Exterior: 建物の外側からドアを見る（カメラは外、ビュー方向は室内側へ）
                                // Interior: 室内からドアを見る（カメラは内、ビュー方向は外側へ）
                                if (doorViewSide.Equals("Exterior", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (interiorDir != null && !interiorDir.IsZeroLength())
                                    {
                                        dir = interiorDir.Normalize();
                                    }
                                }
                                else if (doorViewSide.Equals("Interior", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (exteriorDir != null && !exteriorDir.IsZeroLength())
                                    {
                                        dir = exteriorDir.Normalize();
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // ドア用の補正に失敗しても、元の dir をそのまま利用する
                        }
                    }

                    try
                    {
                        View view = null;
                        string localReason = null;
                        string modeUsed = mode;

                        if (mode == "SectionBox")
                        {
                            // 1) SectionBox で試行
                            try
                            {
                                view = CreateSectionViewForElement(doc, vft, elem, refPt, dir, cropMarginMm, offsetDistanceMm, viewScale);
                                if (view == null)
                                {
                                    localReason = "SectionBox: CreateSectionViewForElement が null を返しました。";
                                }
                            }
                            catch (Exception exSection)
                            {
                                localReason = "SectionBox 例外: " + exSection.GetType().Name +
                                              (string.IsNullOrEmpty(exSection.Message) ? "" : " - " + exSection.Message);
                            }

                            // 2) SectionBox が失敗した場合は ElevationMarker へフォールバック
                            if (view == null)
                            {
                                // Elevation 用 ViewFamilyType を解決（名前指定があれば尊重しつつ、ViewFamily.Elevation を優先）
                                ViewFamilyType elevVft = null;
                                var elevVftCandidates = new FilteredElementCollector(doc)
                                    .OfClass(typeof(ViewFamilyType))
                                    .Cast<ViewFamilyType>()
                                    .ToList();

                                if (!string.IsNullOrEmpty(viewFamilyTypeName))
                                {
                                    var byName = elevVftCandidates.FirstOrDefault(v =>
                                        string.Equals(v.Name ?? string.Empty, viewFamilyTypeName, StringComparison.OrdinalIgnoreCase) &&
                                        v.ViewFamily == ViewFamily.Elevation);
                                    if (byName != null)
                                    {
                                        elevVft = byName;
                                    }
                                }

                                if (elevVft == null)
                                {
                                    elevVft = elevVftCandidates.FirstOrDefault(v => v.ViewFamily == ViewFamily.Elevation);
                                }

                                if (elevVft == null)
                                {
                                    var reason = (localReason ?? "") + " / ElevationMarker 用 ViewFamilyType (Elevation) が解決できませんでした。";
                                    skipped.Add(new { elementId = eid.IntegerValue, reason = reason });
                                    continue;
                                }

                                try
                                {
                                    view = CreateElevationViewForElement(doc, elevVft, elem, refPt, dir, cropMarginMm, offsetDistanceMm, viewScale, depthMode);
                                    modeUsed = "ElevationMarker";
                                }
                                catch (Exception exElev)
                                {
                                    var reason = (localReason ?? "SectionBox 失敗") +
                                                 " / ElevationMarker 例外: " + exElev.GetType().Name +
                                                 (string.IsNullOrEmpty(exElev.Message) ? "" : " - " + exElev.Message);
                                    skipped.Add(new { elementId = eid.IntegerValue, reason = reason });
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            view = CreateElevationViewForElement(doc, vft, elem, refPt, dir, cropMarginMm, offsetDistanceMm, viewScale, depthMode);
                            modeUsed = "ElevationMarker";
                        }

                        if (view == null)
                        {
                            skipped.Add(new
                            {
                                elementId = eid.IntegerValue,
                                reason = localReason ?? "ビュー作成に失敗しました。"
                            });
                            continue;
                        }

                        // テンプレート適用（任意）
                        if (templateId != ElementId.InvalidElementId)
                        {
                            try { view.ViewTemplateId = templateId; } catch { }
                        }

                        // 名前（断面は _Sec、立面は _Elev）
                        try
                        {
                            string suffix = string.Equals(modeUsed, "SectionBox", StringComparison.OrdinalIgnoreCase) ? "_Sec" : "_Elev";
                            string baseName = "Element_" + eid.IntegerValue + suffix;
                            view.Name = MakeUniqueViewName(doc, baseName);
                        }
                        catch { }

                        // ターゲット隔離
                        if (isolateTargets)
                        {
                            try
                            {
                                IsolateElementsInView(doc, view, elementIds);
                            }
                            catch
                            {
                                // 隔離失敗は致命的ではない
                            }
                        }

                        created.Add(new
                        {
                            elementId = eid.IntegerValue,
                            viewId = view.Id.IntegerValue,
                            viewName = view.Name,
                            mode = modeUsed
                        });
                    }
                    catch (Exception exElem)
                    {
                        skipped.Add(new { elementId = eid.IntegerValue, reason = exElem.Message });
                    }
                }

                tx.Commit();
            }

            bool ok = created.Count > 0;
            string msg;
            if (created.Count == 0)
            {
                msg = "ビューを作成できませんでした。すべての要素がスキップされました。";
            }
            else
            {
                msg = string.Format("Created elevation views for {0} element(s).", created.Count);
            }

            return new
            {
                ok = ok,
                msg = msg,
                views = created,
                skippedElements = skipped
            };
        }

        private static XYZ ResolveDirection(Element elem, string orientationMode, XYZ customDir, out string error)
        {
            error = null;
            string mode = (orientationMode ?? "Front").Trim();

            if (string.Equals(mode, "CustomVector", StringComparison.OrdinalIgnoreCase))
            {
                if (customDir == null || customDir.IsZeroLength())
                {
                    error = "orientation.mode=CustomVector ですが customDirection が無効です。";
                    return null;
                }
                return customDir.Normalize();
            }

            // FamilyInstance
            var fi = elem as FamilyInstance;
            if (fi != null)
            {
                try
                {
                    if (string.Equals(mode, "Front", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = fi.FacingOrientation;
                        if (v != null && !v.IsZeroLength()) return v.Normalize();
                    }
                    else if (string.Equals(mode, "Back", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = fi.FacingOrientation;
                        if (v != null && !v.IsZeroLength()) return (-v).Normalize();
                    }
                    else if (string.Equals(mode, "Left", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = fi.HandOrientation;
                        if (v != null && !v.IsZeroLength()) return (-v).Normalize();
                    }
                    else if (string.Equals(mode, "Right", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = fi.HandOrientation;
                        if (v != null && !v.IsZeroLength()) return v.Normalize();
                    }
                }
                catch
                {
                    // fall through to other heuristics
                }
            }

            // Wall
            var wall = elem as Wall;
            if (wall != null)
            {
                try
                {
                    var locCurve = wall.Location as LocationCurve;
                    if (locCurve != null && locCurve.Curve != null)
                    {
                        var line = locCurve.Curve as Line;
                        if (line != null)
                        {
                            var dir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
                            // 壁の法線（上方向 Z を使って直交ベクトル）
                            var z = XYZ.BasisZ;
                            var normal = dir.CrossProduct(z);
                            if (!normal.IsZeroLength())
                            {
                                if (string.Equals(mode, "Back", StringComparison.OrdinalIgnoreCase))
                                    normal = -normal;
                                return normal.Normalize();
                            }
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // その他: グローバル Y+ を既定 Front とする
            var defaultDir = new XYZ(0, 1, 0);
            error = "要素の向き情報が取得できなかったため、グローバル Y+ を Front として使用しました。";
            return defaultDir;
        }

        /// <summary>
        /// depthMode とカテゴリから、DoorWidthPlusMargin ロジックを適用すべきかを判定する。
        /// - ドア/窓/壁/カーテンウォール: depthMode が "Auto" または "DoorWidthPlusMargin" のとき適用。
        /// - それ以外: depthMode が "DoorWidthPlusMargin" のときのみ適用。
        /// </summary>
        private static bool ShouldUseDoorWidthPlusMargin(string depthMode, Element elem)
        {
            string mode = (depthMode ?? "Auto").Trim();
            if (string.IsNullOrWhiteSpace(mode))
            {
                mode = "Auto";
            }

            bool explicitDoorWidth = string.Equals(mode, "DoorWidthPlusMargin", StringComparison.OrdinalIgnoreCase);
            bool auto = string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase);

            var cat = elem?.Category;
            if (cat == null)
            {
                return explicitDoorWidth;
            }

            int cid = cat.Id.IntegerValue;
            bool doorLike =
                cid == (int)BuiltInCategory.OST_Doors ||
                cid == (int)BuiltInCategory.OST_Windows ||
                cid == (int)BuiltInCategory.OST_Walls ||
                cid == (int)BuiltInCategory.OST_CurtainWallPanels;

            if (doorLike)
            {
                return explicitDoorWidth || auto;
            }

            return explicitDoorWidth;
        }

        /// <summary>
        /// ドアの基準位置周辺で、片側が Room 内、反対側が Room 外となる法線方向を探索し、
        /// 室内側 (interiorDir) / 室外側 (exteriorDir) の単位ベクトルを返す。
        /// 戻り値: true=判定成功, false=曖昧または判定不能。
        /// </summary>
        private static bool TryComputeDoorSideDirections(
            Document doc,
            FamilyInstance door,
            IList<Autodesk.Revit.DB.Architecture.Room> allRooms,
            out XYZ interiorDir,
            out XYZ exteriorDir)
        {
            interiorDir = null;
            exteriorDir = null;

            if (door == null || allRooms == null || allRooms.Count == 0)
            {
                return false;
            }

            // ホスト壁
            var hostWall = door.Host as Wall;
            if (hostWall == null)
            {
                return false;
            }

            var locCurve = hostWall.Location as LocationCurve;
            var curve = locCurve != null ? locCurve.Curve : null;
            if (curve == null)
            {
                return false;
            }

            // 壁の接線ベクトル（XY）
            XYZ tangent;
            try
            {
                if (curve is Line ln)
                {
                    tangent = (ln.GetEndPoint(1) - ln.GetEndPoint(0));
                }
                else
                {
                    var p0 = curve.Evaluate(0.0, true);
                    var p1 = curve.Evaluate(1.0, true);
                    tangent = (p1 - p0);
                }
            }
            catch
            {
                return false;
            }

            tangent = new XYZ(tangent.X, tangent.Y, 0.0);
            if (tangent.IsZeroLength())
            {
                return false;
            }
            tangent = tangent.Normalize();

            // 壁法線候補（PerpLeft と同様に [-tY, tX, 0]）
            var nCandidate = new XYZ(-tangent.Y, tangent.X, 0.0);
            if (nCandidate.IsZeroLength())
            {
                return false;
            }
            nCandidate = nCandidate.Normalize();

            // ドアの代表点（XY は LocationPoint 優先、Z はドアの高さ中心）
            BoundingBoxXYZ bb = door.get_BoundingBox(null);
            XYZ basePoint = null;
            try
            {
                var lp = door.Location as LocationPoint;
                if (lp != null)
                {
                    basePoint = lp.Point;
                }
            }
            catch
            {
                basePoint = null;
            }

            if (basePoint == null)
            {
                if (bb != null)
                {
                    basePoint = (bb.Min + bb.Max) * 0.5;
                }
                else
                {
                    return false;
                }
            }

            double zMid = bb != null ? (bb.Min.Z + bb.Max.Z) * 0.5 : basePoint.Z;
            basePoint = new XYZ(basePoint.X, basePoint.Y, zMid);

            double testDistFt = UnitHelper.MmToFt(500.0);
            var pA = basePoint + nCandidate * testDistFt;
            var pB = basePoint - nCandidate * testDistFt;

            bool inRoomA = IsPointInAnyRoom(allRooms, pA);
            bool inRoomB = IsPointInAnyRoom(allRooms, pB);

            if (inRoomA == inRoomB)
            {
                // 両方とも室内 or 室外 → 判定不能
                return false;
            }

            if (inRoomA && !inRoomB)
            {
                interiorDir = (pA - basePoint).Normalize();
                exteriorDir = -interiorDir;
                return true;
            }

            if (inRoomB && !inRoomA)
            {
                interiorDir = (pB - basePoint).Normalize();
                exteriorDir = -interiorDir;
                return true;
            }

            return false;
        }

        private static bool IsPointInAnyRoom(IList<Autodesk.Revit.DB.Architecture.Room> rooms, XYZ p)
        {
            foreach (var room in rooms)
            {
                try
                {
                    if (room != null && room.IsPointInRoom(p))
                    {
                        return true;
                    }
                }
                catch
                {
                    // ignore problematic room
                }
            }
            return false;
        }

        private static View CreateElevationViewForElement(
            Document doc,
            ViewFamilyType vft,
            Element elem,
            XYZ refPt,
            XYZ dir,
            double cropMarginMm,
            double offsetMm,
            int viewScale,
            string depthMode)
        {
            // 対象要素に最も適した平面ビュー（レベル）を解決
            var hostPlan = ResolveHostPlanView(doc, elem);
            if (hostPlan == null)
            {
                return null;
            }

            BoundingBoxXYZ bb = elem.get_BoundingBox(null);
            if (bb == null)
            {
                return null;
            }

            // ElevationMarker は水平面上に配置する必要があるため、Z はビューのレベルに揃える
            double z = hostPlan.GenLevel != null ? hostPlan.GenLevel.Elevation : bb.Min.Z;

            // ビュー方向
            XYZ viewDir = dir.Normalize();
            if (viewDir.IsZeroLength())
            {
                return null;
            }

            // depthMode に応じてビュー原点と奥行きを決定
            bool useDoorWidthPlus = ShouldUseDoorWidthPlusMargin(depthMode, elem);
            double offsetFt = UnitHelper.MmToFt(offsetMm);
            double depthFt = 0.0;

            XYZ origin;
            if (useDoorWidthPlus)
            {
                // ドア/壁などの厚みをビュー方向に投影し、その少し向こう側までを奥行きとする
                XYZ[] corners =
                {
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z)
                };

                double minProj = double.PositiveInfinity;
                double maxProj = double.NegativeInfinity;
                foreach (var c in corners)
                {
                    double p = viewDir.DotProduct(c);
                    if (p < minProj) minProj = p;
                    if (p > maxProj) maxProj = p;
                }

                // refPt から見た前面/背面
                double refProj = viewDir.DotProduct(refPt);
                double thicknessFt = maxProj - minProj;
                if (thicknessFt < UnitHelper.MmToFt(10.0))
                {
                    thicknessFt = UnitHelper.MmToFt(200.0); // 安全側の厚み
                }

                double frontMarginFt = UnitHelper.MmToFt(300.0);                 // ビュー位置からドアまでの余白
                double backMarginFt = UnitHelper.MmToFt(Math.Max(cropMarginMm, 0.0)); // ドア裏側の余白

                // カメラ位置（ビュー原点）は前面より手前側（viewDir 逆方向）に frontMarginFt だけ離す
                double cameraProj = minProj - frontMarginFt;
                double shift = cameraProj - refProj;
                origin = refPt + viewDir * shift;

                // 奥行き = 厚み + 前後マージン
                depthFt = thicknessFt + frontMarginFt + backMarginFt;
            }
            else
            {
                // 従来どおり offsetDistance_mm をそのまま使用
                origin = refPt - viewDir * offsetFt;
            }

            origin = new XYZ(origin.X, origin.Y, z);

            // ElevationMarker を作成し、4 方向すべての Elevation を生成してから、dir(viewDir) に最も近いビューを選択する
            var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, origin, viewScale);

            ViewSection bestView = null;
            double bestDot = -1.0;
            var candidates = new List<ViewSection>();

            for (int i = 0; i < 4; i++)
            {
                ViewSection v = null;
                try
                {
                    v = marker.CreateElevation(doc, hostPlan.Id, i);
                }
                catch
                {
                    continue;
                }

                if (v == null) continue;

                candidates.Add(v);

                var vd = v.ViewDirection;
                if (vd == null || vd.IsZeroLength()) continue;

                // Revit の ViewDirection は内部的に「断面の法線方向」として element->view とは逆を向くことが多いため、
                // 要求した viewDir と反転させて類似度を評価する。
                double dot = vd.Normalize().DotProduct(-viewDir);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestView = v;
                }
            }

            if (bestView == null)
            {
                // 何も作れなかった場合は候補を片付けて終了
                foreach (var v in candidates)
                {
                    try { doc.Delete(v.Id); } catch { }
                }
                return null;
            }

            // 不要な Elevation を削除
            foreach (var v in candidates)
            {
                if (v.Id != bestView.Id)
                {
                    try { doc.Delete(v.Id); } catch { }
                }
            }

            bestView.Scale = viewScale;

            // ビューの関連レベルをホスト平面ビューのレベルに揃える（表示上のレベル不一致対策）
            AlignViewAssociatedLevel(bestView, hostPlan);

            if (useDoorWidthPlus && depthFt > 0.0)
            {
                // ドア幅＋マージンで奥行きをクリップ
                try
                {
                    var farClip = bestView.get_Parameter(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING);
                    var farOff = bestView.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
                    if (farClip != null && !farClip.IsReadOnly)
                    {
                        // 1: Clip with offset
                        farClip.Set(1);
                    }
                    if (farOff != null && !farOff.IsReadOnly)
                    {
                        farOff.Set(depthFt);
                    }
                }
                catch
                {
                    // 失敗しても致命的ではない
                }
            }

            // XY 方向のトリミングは常に要素 BB + cropMargin で調整
            AdjustCropBox(doc, bestView, elem, cropMarginMm);

            return bestView;
        }

        private static View CreateSectionViewForElement(
            Document doc,
            ViewFamilyType vft,
            Element elem,
            XYZ refPt,
            XYZ dir,
            double cropMarginMm,
            double offsetMm,
            int viewScale)
        {
            BoundingBoxXYZ bb = elem.get_BoundingBox(null);
            if (bb == null) return null;

            XYZ viewDir = dir.Normalize();
            if (viewDir.IsZeroLength()) return null;

            XYZ up = XYZ.BasisZ;
            if (Math.Abs(viewDir.DotProduct(up)) > 0.99)
                up = XYZ.BasisX;

            // IMPORTANT: Keep a right-handed coordinate system for the section box transform.
            // BasisX × BasisY must point to BasisZ (= viewDir). Otherwise ViewSection.CreateSection may throw.
            XYZ right = up.CrossProduct(viewDir).Normalize();
            up = viewDir.CrossProduct(right).Normalize();

            XYZ center = (bb.Min + bb.Max) * 0.5;
            double offsetFt = UnitHelper.MmToFt(offsetMm);

            XYZ origin = center - viewDir * offsetFt;

            var transform = Transform.Identity;
            transform.Origin = origin;
            transform.BasisX = right;
            transform.BasisY = up;
            transform.BasisZ = viewDir;

            XYZ[] corners = {
                new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z)
            };

            var inv = transform.Inverse;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;

            foreach (var c in corners)
            {
                var lc = inv.OfPoint(c);
                minX = Math.Min(minX, lc.X);
                maxX = Math.Max(maxX, lc.X);
                minY = Math.Min(minY, lc.Y);
                maxY = Math.Max(maxY, lc.Y);
                minZ = Math.Min(minZ, lc.Z);
                maxZ = Math.Max(maxZ, lc.Z);
            }

            double marginFt = UnitHelper.MmToFt(cropMarginMm);

            minX -= marginFt; maxX += marginFt;
            minY -= marginFt; maxY += marginFt;
            minZ -= marginFt; maxZ += marginFt;

            if (maxZ - minZ < UnitHelper.MmToFt(10))
                maxZ = minZ + UnitHelper.MmToFt(10);

            var box = new BoundingBoxXYZ();
            box.Transform = transform;
            box.Min = new XYZ(minX, minY, minZ);
            box.Max = new XYZ(maxX, maxY, maxZ);

            var view = ViewSection.CreateSection(doc, vft.Id, box);
            if (view == null) return null;

            view.Scale = viewScale;
            return view;
        }

        private static void AdjustCropBox(Document doc, View view, Element elem, double cropMarginMm)
        {
            try
            {
                if (view == null || elem == null) return;

                view.CropBoxActive = true;
                view.CropBoxVisible = true;

                // ビューの CropBox が持つ座標系（ビュー平面: X=水平, Y=高さ, Z=ビュー奥行き）
                var crop = view.CropBox;
                if (crop == null) return;

                // 1) ドア／窓 については、タイプの幅・高さとジオメトリ中心を用いて
                //    「中心が揃った矩形＋マージン」でトリミングする。
                if (TryAdjustCropBoxForDoorOrWindow(doc, view, elem, crop, cropMarginMm))
                {
                    // ドア／窓用のクロップ調整が成功した場合は、そのままビューへ反映して終了
                    view.CropBox = crop;
                    return;
                }

                // 2) 上記で処理できなかった場合は、汎用の「BB 投影＋マージン」ロジックを使用

                // 要素のワールド座標系でのバウンディングボックスを取得
                // （ビューを渡すと既存の CropBox に依存してしまうため、null を優先）
                BoundingBoxXYZ bbWorld = elem.get_BoundingBox(null) ?? elem.get_BoundingBox(view);
                if (bbWorld == null) return;

                var viewTransform = crop.Transform;
                var inv = viewTransform.Inverse;

                // 要素 BB の 8 つの頂点をビュー座標系に変換して、
                // ビュー平面上の X,Y 方向の最小・最大を求める。
                XYZ[] corners =
                {
                    new XYZ(bbWorld.Min.X, bbWorld.Min.Y, bbWorld.Min.Z),
                    new XYZ(bbWorld.Min.X, bbWorld.Min.Y, bbWorld.Max.Z),
                    new XYZ(bbWorld.Min.X, bbWorld.Max.Y, bbWorld.Min.Z),
                    new XYZ(bbWorld.Min.X, bbWorld.Max.Y, bbWorld.Max.Z),
                    new XYZ(bbWorld.Max.X, bbWorld.Min.Y, bbWorld.Min.Z),
                    new XYZ(bbWorld.Max.X, bbWorld.Min.Y, bbWorld.Max.Z),
                    new XYZ(bbWorld.Max.X, bbWorld.Max.Y, bbWorld.Min.Z),
                    new XYZ(bbWorld.Max.X, bbWorld.Max.Y, bbWorld.Max.Z)
                };

                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;

                foreach (var c in corners)
                {
                    var lc = inv.OfPoint(c); // ビュー座標系
                    if (lc.X < minX) minX = lc.X;
                    if (lc.X > maxX) maxX = lc.X;
                    if (lc.Y < minY) minY = lc.Y;
                    if (lc.Y > maxY) maxY = lc.Y;
                }

                double marginFt = UnitHelper.MmToFt(cropMarginMm);

                var min = crop.Min;
                var max = crop.Max;

                // X,Y は要素の範囲＋マージンでトリミング（ビュー平面上の幅＋高さ）
                min = new XYZ(minX - marginFt, minY - marginFt, min.Z); // Z は既存値を維持（奥行きは別途 Far Clip で管理）
                max = new XYZ(maxX + marginFt, maxY + marginFt, max.Z);

                crop.Min = min;
                crop.Max = max;
                view.CropBox = crop;
            }
            catch
            {
                // クロップ調整失敗は致命的ではないので無視
            }
        }

        /// <summary>
        /// ドア／窓（および類似カテゴリ）について、タイプ・インスタンスの幅・高さと代表点を用いて
        /// 中心位置が揃うように CropBox を調整する。処理できた場合は true を返す。
        /// </summary>
        private static bool TryAdjustCropBoxForDoorOrWindow(
            Document doc,
            View view,
            Element elem,
            BoundingBoxXYZ crop,
            double cropMarginMm)
        {
            try
            {
                if (doc == null || view == null || elem == null || crop == null)
                    return false;

                var fi = elem as FamilyInstance;
                if (fi == null || fi.Category == null)
                    return false;

                int catId = fi.Category.Id.IntegerValue;
                bool isDoor = catId == (int)BuiltInCategory.OST_Doors;
                bool isWindow = catId == (int)BuiltInCategory.OST_Windows;
                bool isCurtainPanel = catId == (int)BuiltInCategory.OST_CurtainWallPanels;

                if (!isDoor && !isWindow && !isCurtainPanel)
                {
                    // その他のカテゴリは汎用 BB ロジックに任せる
                    return false;
                }

                // 幅・高さパラメータ（ドア／窓の場合のみ、まずタイプ、なければインスタンス）
                Element typeElem = doc.GetElement(fi.GetTypeId());
                var type = typeElem as ElementType;

                double widthFt = 0.0;
                double heightFt = 0.0;

                if (isDoor || isWindow)
                {
                    var bipWidth = isDoor
                        ? BuiltInParameter.DOOR_WIDTH
                        : BuiltInParameter.WINDOW_WIDTH;
                    var bipHeight = isDoor
                        ? BuiltInParameter.DOOR_HEIGHT
                        : BuiltInParameter.WINDOW_HEIGHT;

                    try
                    {
                        if (type != null)
                        {
                            var pw = type.get_Parameter(bipWidth);
                            if (pw != null && pw.StorageType == StorageType.Double)
                            {
                                widthFt = pw.AsDouble();
                            }

                            var ph = type.get_Parameter(bipHeight);
                            if (ph != null && ph.StorageType == StorageType.Double)
                            {
                                heightFt = ph.AsDouble();
                            }
                        }

                        if (widthFt <= 1e-9)
                        {
                            var pwInst = fi.get_Parameter(bipWidth);
                            if (pwInst != null && pwInst.StorageType == StorageType.Double)
                            {
                                widthFt = pwInst.AsDouble();
                            }
                        }

                        if (heightFt <= 1e-9)
                        {
                            var phInst = fi.get_Parameter(bipHeight);
                            if (phInst != null && phInst.StorageType == StorageType.Double)
                            {
                                heightFt = phInst.AsDouble();
                            }
                        }
                    }
                    catch
                    {
                        // パラメータ取得失敗時は後段でジオメトリ幅・高さにフォールバックする
                        widthFt = 0.0;
                        heightFt = 0.0;
                    }
                }

                // 要素ジオメトリから、ビュー座標系での BB と中心を求める
                var viewTransform = crop.Transform;
                var inv = viewTransform.Inverse;

                BoundingBoxXYZ bbWorld = fi.get_BoundingBox(null);
                if (bbWorld == null)
                {
                    return false;
                }

                XYZ[] corners =
                {
                    new XYZ(bbWorld.Min.X, bbWorld.Min.Y, bbWorld.Min.Z),
                    new XYZ(bbWorld.Min.X, bbWorld.Min.Y, bbWorld.Max.Z),
                    new XYZ(bbWorld.Min.X, bbWorld.Max.Y, bbWorld.Min.Z),
                    new XYZ(bbWorld.Min.X, bbWorld.Max.Y, bbWorld.Max.Z),
                    new XYZ(bbWorld.Max.X, bbWorld.Min.Y, bbWorld.Min.Z),
                    new XYZ(bbWorld.Max.X, bbWorld.Min.Y, bbWorld.Max.Z),
                    new XYZ(bbWorld.Max.X, bbWorld.Max.Y, bbWorld.Min.Z),
                    new XYZ(bbWorld.Max.X, bbWorld.Max.Y, bbWorld.Max.Z)
                };

                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;

                foreach (var c in corners)
                {
                    var lc = inv.OfPoint(c);
                    if (lc.X < minX) minX = lc.X;
                    if (lc.X > maxX) maxX = lc.X;
                    if (lc.Y < minY) minY = lc.Y;
                    if (lc.Y > maxY) maxY = lc.Y;
                }

                var centerLocal = new XYZ((minX + maxX) * 0.5, (minY + maxY) * 0.5, 0.0);

                double marginFt = UnitHelper.MmToFt(cropMarginMm);

                double widthLocal = maxX - minX;
                double heightLocal = maxY - minY;

                // 型寸法がある場合はそれを優先し、なければジオメトリの幅・高さを利用
                double halfW = (widthFt > 1e-6 ? widthFt : widthLocal) * 0.5 + marginFt;
                double halfH = (heightFt > 1e-6 ? heightFt : heightLocal) * 0.5 + marginFt;

                var min = crop.Min;
                var max = crop.Max;

                // X: 水平（幅方向）、Y: 垂直（高さ方向）
                min = new XYZ(centerLocal.X - halfW, centerLocal.Y - halfH, min.Z);
                max = new XYZ(centerLocal.X + halfW, centerLocal.Y + halfH, max.Z);

                crop.Min = min;
                crop.Max = max;

                // 呼び出し元で view.CropBox に再代入される
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void IsolateElementsInView(Document doc, View view, IList<ElementId> targetIds)
        {
            var visible = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToElementIds()
                .ToList();

            var targetSet = new HashSet<ElementId>(targetIds);
            var hide = new List<ElementId>();
            foreach (var id in visible)
            {
                if (!targetSet.Contains(id))
                {
                    hide.Add(id);
                }
            }

            if (hide.Count > 0)
            {
                view.HideElements(hide);
            }
        }

        /// <summary>
        /// 要素のレベル情報から、対応する FloorPlan/CeilingPlan ビューを解決する。
        /// 見つからない場合は、アクティブな平面ビュー → いずれかの FloorPlan の順でフォールバック。
        /// </summary>
        private static ViewPlan ResolveHostPlanView(Document doc, Element elem)
        {
            // 1) アクティブビューが FloorPlan/CeilingPlan ならそれを最優先で使用
            var av = doc.ActiveView as ViewPlan;
            if (av != null && !av.IsTemplate &&
                (av.ViewType == ViewType.FloorPlan || av.ViewType == ViewType.CeilingPlan))
            {
                return av;
            }

            ElementId levelId = ElementId.InvalidElementId;
            try
            {
                if (elem is FamilyInstance fi && fi.LevelId != ElementId.InvalidElementId)
                {
                    levelId = fi.LevelId;
                }
                else if (elem.LevelId != ElementId.InvalidElementId)
                {
                    levelId = elem.LevelId;
                }
                else
                {
                    var pLevel = elem.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                    if (pLevel != null && pLevel.StorageType == StorageType.ElementId)
                    {
                        levelId = pLevel.AsElementId();
                    }
                }
            }
            catch
            {
                // ignore
            }

            if (levelId != ElementId.InvalidElementId)
            {
                var planForLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewPlan))
                    .Cast<ViewPlan>()
                    .FirstOrDefault(vp =>
                        !vp.IsTemplate &&
                        (vp.ViewType == ViewType.FloorPlan || vp.ViewType == ViewType.CeilingPlan) &&
                        vp.GenLevel != null &&
                        vp.GenLevel.Id == levelId);
                if (planForLevel != null)
                {
                    return planForLevel;
                }
            }

            // さらにフォールバック: 任意の FloorPlan
            var anyFloor = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(vp => !vp.IsTemplate && vp.ViewType == ViewType.FloorPlan);

            return anyFloor;
        }

        /// <summary>
        /// Elevation/Section ビューの「関連レベル」パラメータを、ホスト平面ビューのレベルに合わせる。
        /// （BuiltInParameter に依存せず、名前ベースで ElementId パラメータを検索）
        /// </summary>
        private static void AlignViewAssociatedLevel(View view, ViewPlan hostPlan)
        {
            if (view == null || hostPlan == null || hostPlan.GenLevel == null) return;
            var levelId = hostPlan.GenLevel.Id;

            try
            {
                foreach (Parameter p in view.Parameters)
                {
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.ElementId)
                        continue;

                    string name = p.Definition?.Name ?? string.Empty;
                    if (string.IsNullOrEmpty(name)) continue;

                    // 日本語/英語をゆるくサポート
                    if (name.IndexOf("関連レベル", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Associated Level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("参照レベル", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { p.Set(levelId); } catch { }
                        break;
                    }
                }
            }
            catch
            {
                // 失敗しても致命的ではないので黙殺
            }
        }

        private static string MakeUniqueViewName(Document doc, string baseName)
        {
            string name = baseName;
            int i = 2;
            while (new FilteredElementCollector(doc)
                   .OfClass(typeof(View))
                   .Cast<View>()
                   .Any(v => !v.IsTemplate && string.Equals(v.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = baseName + " (" + i.ToString() + ")";
                i++;
            }
            return name;
        }
    }

    internal static class XyzExtensions
    {
        public static bool IsZeroLength(this XYZ v)
        {
            return v == null || v.GetLength() < 1e-9;
        }
    }
}
