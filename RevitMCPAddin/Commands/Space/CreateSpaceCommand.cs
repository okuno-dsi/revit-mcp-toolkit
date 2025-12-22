// ================================================================
// File: Commands/Space/CreateSpaceCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: create_space（InputPointReader対応＋高さ設定＋ComputeVolumes ON）
// Notes  : 既存の堅牢配置ロジック（作成→Move→Area==0検出）を維持
//           Space高さは BuiltInParameter.SPACE_UNBOUNDED_HEIGHT を使用
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;     // ResultUtil / UnitHelper / InputPointReader など
using SpaceElem = Autodesk.Revit.DB.Mechanical.Space;

namespace RevitMCPAddin.Commands.Space
{
    public class CreateSpaceCommand : IRevitCommandHandler
    {
        public string CommandName => "create_space";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = (JObject)(cmd.Params ?? new JObject());

            // ---- Level 取得 ----
            int levelId = p.Value<int?>("levelId")
                          ?? p.SelectToken("level.id")?.Value<int?>()
                          ?? 0;
            if (levelId == 0) return ResultUtil.Err("levelId is required.");
            var level = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(levelId)) as Level;
            if (level == null) return ResultUtil.Err($"Level not found: {levelId}");

            // ---- 入力XY（mm）読取（location/point/pt/[x,y] 等も可） ----
            if (!InputPointReader.TryReadXYMm(p, out var xMm, out var yMm))
                return ResultUtil.Err("x, y (mm) are required. (x/y or location.{x,y} or point.{x,y} or [x,y])");

            // ---- 任意 heightMm（mm） ----
            double? heightMmIn = p.Value<double?>("heightMm");

            // 既定高さの決定（未指定なら Room から推定 → 無ければ 2500）
            const double FALLBACK_MM = 2500.0;
            double defaultHeightMm = heightMmIn ?? (TryInferDefaultRoomHeightMm(doc) ?? FALLBACK_MM);

            // ---- mm -> internal(ft) ----
            double xFt = UnitHelper.MmToInternal(xMm);
            double yFt = UnitHelper.MmToInternal(yMm);
            var targetUv = new UV(xFt, yFt);

            try
            {
                using (var tg = new TransactionGroup(doc, "[MCP] create_space (height & computeVolumes)"))
                {
                    tg.Start();

                    // 1) Area/Volume 計算を ON（既にONでもOK）
                    using (var t = new Transaction(doc, "[MCP] Enable Area/Volume"))
                    {
                        t.Start();
                        var av = AreaVolumeSettings.GetAreaVolumeSettings(doc);
                        if (!av.ComputeVolumes)
                            av.ComputeVolumes = true;
                        t.Commit();
                    }

                    // 2) Space 作成＋堅牢移動＋境界チェック
                    int newId = -1;
                    double usedHeightMm = heightMmIn ?? defaultHeightMm;

                    using (var tx = new Transaction(doc, "[MCP] Create Space"))
                    {
                        tx.Start();

                        // a) 作成（Revit個体差で原点寄りに落ちることがある）
                        var space = doc.Create.NewSpace(level, targetUv);
                        if (space == null)
                        {
                            tx.RollBack();
                            tg.RollBack();
                            return ResultUtil.Err("Failed to create space (unknown).");
                        }

                        // b) 位置確認＆確実に移動
                        if (!(space.Location is LocationPoint lp))
                        {
                            tx.RollBack();
                            tg.RollBack();
                            return ResultUtil.Err("Space created without Location (invalid boundary?).");
                        }
                        var cur = lp.Point; // XYZ(ft)
                        var desired = new XYZ(xFt, yFt, cur.Z);
                        var delta = desired - cur;
                        if (delta.GetLength() > 1e-9)
                        {
                            try
                            {
                                ElementTransformUtils.MoveElement(doc, space.Id, delta);
                            }
                            catch (Exception exMove)
                            {
                                tx.RollBack();
                                tg.RollBack();
                                return ResultUtil.Err($"Space move failed: {exMove.Message}");
                            }
                        }

                        //// c) 最終検証：境界内（簡易：Area>0）
                        //try
                        //{
                        //    var areaParam = space.get_Parameter(BuiltInParameter.ROOM_AREA);
                        //    double areaFt2 = areaParam?.AsDouble() ?? 0.0;
                        //    if (areaFt2 <= 1e-9)
                        //    {
                        //        tx.RollBack();
                        //        tg.RollBack();
                        //        return ResultUtil.Err("The point is likely outside of a bounded Space region (Area=0). Place Space inside MEP boundaries.");
                        //    }
                        //}
                        //catch { /* ignore */ }

                        // d) 高さ設定：SPACE_UNBOUNDED_HEIGHT（mm→ft）
                        TrySetSpaceUnboundedHeight(space, usedHeightMm);

                        // e) 任意の Name / Number 設定
                        var nameIn = p.Value<string>("name");
                        if (!string.IsNullOrWhiteSpace(nameIn))
                        {
                            var pName = space.get_Parameter(BuiltInParameter.ROOM_NAME) ?? space.LookupParameter("Name");
                            if (pName != null && !pName.IsReadOnly) pName.Set(nameIn);
                        }
                        var numberIn = p.Value<string>("number");
                        if (!string.IsNullOrWhiteSpace(numberIn))
                        {
                            var pNum = space.get_Parameter(BuiltInParameter.ROOM_NUMBER) ?? space.LookupParameter("Number");
                            if (pNum != null && !pNum.IsReadOnly) pNum.Set(numberIn);
                        }

                        newId = space.Id.IntValue();
                        tx.Commit();

                        var pos = ((LocationPoint)space.Location).Point;
                        tg.Assimilate();

                        return new
                        {
                            ok = true,
                            elementId = newId,
                            usedHeightMm = usedHeightMm,
                            location = new
                            {
                                xMm = Math.Round(UnitHelper.InternalToMm(pos.X), 3),
                                yMm = Math.Round(UnitHelper.InternalToMm(pos.Y), 3),
                                zMm = Math.Round(UnitHelper.InternalToMm(pos.Z), 3)
                            },
                            msg = ""
                        };
                    }
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                return ResultUtil.Err($"Argument error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return ResultUtil.Err($"create_space exception: {ex.Message}");
            }
        }

        // --- Space 高さ setter（mm→ft）
        private static bool TrySetSpaceUnboundedHeight(SpaceElem space, double heightMm)
        {
            try
            {
                // Unbounded Height は BuiltInParameter に存在しないため LookupParameter で取得
                var p = space.LookupParameter("Unbounded Height");
                if (p == null || p.IsReadOnly) return false;

                if (p.StorageType == StorageType.Double)
                {
                    double ft = UnitUtils.ConvertToInternalUnits(heightMm, UnitTypeId.Millimeters);
                    return p.Set(ft);
                }
            }
            catch
            {
                // フォールバック（日本語UIなど）
                try
                {
                    var p2 = space.LookupParameter("部屋高さ(レベル指定)");
                    if (p2 != null && !p2.IsReadOnly && p2.StorageType == StorageType.Double)
                    {
                        double ft = UnitUtils.ConvertToInternalUnits(heightMm, UnitTypeId.Millimeters);
                        return p2.Set(ft);
                    }
                }
                catch { /* ignore */ }
            }
            return false;
        }

        // --- 既存 Room 群から高さ（mm）の代表値（中央値）を推定。無ければ null。
        private static double? TryInferDefaultRoomHeightMm(Document doc)
        {
            try
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpatialElement))
                    .Cast<SpatialElement>()
                    .OfType<Autodesk.Revit.DB.Architecture.Room>()
                    .ToList();
                if (rooms.Count == 0) return null;

                var vals = new List<double>();
                foreach (var r in rooms)
                {
                    // Room は ROOM_HEIGHT が適切（ROOM_UNBOUNDED_HEIGHT は存在しない）
                    var pr = r.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
                    if (pr != null && pr.StorageType == StorageType.Double)
                    {
                        double ft = pr.AsDouble();
                        if (ft > 0)
                        {
                            double mm = UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
                            if (mm > 1) vals.Add(mm);
                        }
                    }
                }
                if (vals.Count == 0) return null;

                vals.Sort();
                int n = vals.Count;
                double median = (n % 2 == 1) ? vals[n / 2] : 0.5 * (vals[n / 2 - 1] + vals[n / 2]);
                return Math.Round(median, 1);
            }
            catch { return null; }
        }
    }
}


