// ================================================================
// File: Commands/ViewOps/CreateInteriorElevationCommand.cs
// Purpose : Interior Elevation（展開図）を作成・情報取得
// Target  : .NET Framework 4.8 / C# 8 / Revit 2023 API
// Depends : Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq
//           RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand)
// Notes   : 入力は mm、内部 ft。ViewFamilyType(Elevation) の列挙・解決あり。
//           マーカーの slotIndex は 0..3（E/N/W/S の一般的割当をフォールバック）
// ================================================================
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ViewOps
{
    internal static class ElevUtil
    {
        public static object UnitsIn() => new { Length = "mm" };
        public static object UnitsInt() => new { Length = "ft" };

        public static double MmToFt(double mm) => ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double FtToMm(double ft) => ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        /// <summary>Elevation用 ViewFamilyType を1件解決。viewTypeId または最初の Elevation を返す。</summary>
        public static ViewFamilyType ResolveElevationViewFamilyType(Document doc, JObject p)
        {
            int vftId = p.Value<int?>("viewTypeId") ?? 0;
            if (vftId > 0)
            {
                var t = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(vftId)) as ViewFamilyType;
                if (t != null && t.ViewFamily == ViewFamily.Elevation) return t;
            }
            // 最初に見つかった Elevation 用を返す
            var elevType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Elevation);
            return elevType;
        }

        /// <summary>レベル解決（levelId / levelName）。見つからない場合 null。</summary>
        public static Level ResolveLevel(Document doc, JObject p)
        {
            int lid = p.Value<int?>("levelId") ?? 0;
            if (lid > 0)
            {
                var l = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(lid)) as Level;
                if (l != null) return l;
            }
            string lname = p.Value<string>("levelName");
            if (!string.IsNullOrWhiteSpace(lname))
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(x => string.Equals(x.Name ?? "", lname, StringComparison.OrdinalIgnoreCase));
            }
            return null;
        }

        /// <summary>指定レベルの平面ビュー（AreaPlan/StructuralPlanを除く通常のPlan）をひとつ返す。</summary>
        public static ViewPlan ResolveHostPlanForLevel(Document doc, Level level)
        {
            if (level == null) return null;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(vp => vp.ViewType == ViewType.FloorPlan && vp.GenLevel?.Id == level.Id);
        }

        /// <summary>方位→slotIndex（一般的割当）。E=0, N=1, W=2, S=3。未知は 0。</summary>
        public static int FacingToSlotIndex(string facing)
        {
            if (string.IsNullOrWhiteSpace(facing)) return 0;
            var f = facing.Trim().ToLowerInvariant();
            if (f == "east" || f == "e" || f == "東") return 0;
            if (f == "north" || f == "n" || f == "北") return 1;
            if (f == "west" || f == "w" || f == "西") return 2;
            if (f == "south" || f == "s" || f == "南") return 3;
            return 0;
        }

        /// <summary>安全な mm→XYZ 変換。</summary>
        public static XYZ ReadPointMm(JObject tok, double defaultZ = 0.0)
        {
            if (tok == null) return new XYZ(0, 0, MmToFt(defaultZ));
            double x = MmToFt(tok.Value<double?>("x") ?? 0);
            double y = MmToFt(tok.Value<double?>("y") ?? 0);
            double z = MmToFt(tok.Value<double?>("z") ?? defaultZ);
            return new XYZ(x, y, z);
        }
    }

    // ------------------------------------------------------------
    // 1) Elevation 用 ViewFamilyType を列挙
    //    Params : skip?, count?, namesOnly?
    //    Return : { ok, totalCount, types:[{viewTypeId, name, viewFamily}] or names }
    // ------------------------------------------------------------
    public class GetElevationViewTypesCommand : IRevitCommandHandler
    {
        public string CommandName => "get_elevation_view_types";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(vft => vft.ViewFamily == ViewFamily.Elevation)
                .OrderBy(v => v.Name ?? "")
                .ThenBy(v => v.Id.IntValue())
                .ToList();

            int totalCount = all.Count;
            if (count == 0) return new { ok = true, totalCount };

            if (namesOnly)
            {
                var names = all.Skip(skip).Take(count).Select(v => v.Name ?? "").ToList();
                return new { ok = true, totalCount, names };
            }

            var list = all.Skip(skip).Take(count)
                .Select(v => new { viewTypeId = v.Id.IntValue(), name = v.Name ?? "", viewFamily = v.ViewFamily.ToString() })
                .ToList();

            return new { ok = true, totalCount, types = list };
        }
    }

    // ------------------------------------------------------------
    // 2) 展開図ビューの情報取得
    //    Params : viewId
    //    Return : { ok, ... name, scale, viewDirection, cropBox(min/max mm), detailLevel etc. }
    // ------------------------------------------------------------
    public class GetElevationViewInfoCommand : IRevitCommandHandler
    {
        public string CommandName => "get_elevation_view_info";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params;

            int vid = p.Value<int>("viewId");
            var view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(vid)) as ViewSection;
            if (view == null || view.ViewType != ViewType.Elevation)
                return new { ok = false, msg = "Elevation view が見つかりません。" };

            // 基本情報
            var name = view.Name ?? "";
            int scale = view.Scale;
            var dir = view.ViewDirection; // XYZ
            var dl = view.DetailLevel;
            var disp = view.DisplayStyle;

            // クロップ
            BoundingBoxXYZ crop = null;
            try { if (view.CropBoxActive) crop = view.CropBox; } catch { /* ignore */ }

            object cropBox = null;
            if (crop != null)
            {
                cropBox = new
                {
                    min = new { x = Math.Round(ElevUtil.FtToMm(crop.Min.X), 3), y = Math.Round(ElevUtil.FtToMm(crop.Min.Y), 3), z = Math.Round(ElevUtil.FtToMm(crop.Min.Z), 3) },
                    max = new { x = Math.Round(ElevUtil.FtToMm(crop.Max.X), 3), y = Math.Round(ElevUtil.FtToMm(crop.Max.Y), 3), z = Math.Round(ElevUtil.FtToMm(crop.Max.Z), 3) }
                };
            }

            return new
            {
                ok = true,
                viewId = vid,
                name,
                scale,
                viewDirection = new { x = dir?.X ?? 0, y = dir?.Y ?? 0, z = dir?.Z ?? 0 },
                detailLevel = dl.ToString(),
                displayStyle = disp.ToString(),
                cropActive = view.CropBoxActive,
                cropVisible = view.CropBoxVisible,
                cropBox,
                inputUnits = ElevUtil.UnitsIn(),
                internalUnits = ElevUtil.UnitsInt()
            };
        }
    }

    // ------------------------------------------------------------
    // 3) 単体のインテリア展開図を作成
    //    Params : 
    //      name?           : string
    //      levelId/levelName? : 基点レベル（無指定時は origin.Z の近傍レベルを試す or 先頭）
    //      hostViewId?     : 平面ビューID（無指定時は level に紐づく FloorPlan を使用）
    //      origin          : {x,y,z} (mm) 必須
    //      viewTypeId?     : Elevation 用 ViewFamilyType（無指定=最初）
    //      slotIndex?      : 0..3（E/N/W/S 推奨順）。facing 指定があれば上書き。
    //      facing?         : "East|North|West|South"（slotIndex 未指定時のフォールバック）
    //      scale?          : ビュー尺度（既定 100）
    //    Return : { ok, viewId, name }
    // ------------------------------------------------------------
    public class CreateInteriorElevationCommand : IRevitCommandHandler
    {
        public string CommandName => "create_interior_elevation";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

                var p = (JObject)cmd.Params ?? new JObject();

                // 原点（mm）
                var originTok = p["origin"] as JObject;
                if (originTok == null) return new { ok = false, msg = "origin {x,y,z} (mm) が必要です。" };
                var origin = ElevUtil.ReadPointMm(originTok);

                // Elevation ViewFamilyType
                var vft = ElevUtil.ResolveElevationViewFamilyType(doc, p);
                if (vft == null) return new { ok = false, msg = "Elevation用の ViewFamilyType が見つかりません。" };

                // 平面ビューの解決（hostViewId > level > fallback）
                ViewPlan hostPlan = null;
                int hostViewId = p.Value<int?>("hostViewId") ?? 0;
                if (hostViewId > 0)
                {
                    hostPlan = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(hostViewId)) as ViewPlan;
                }
                if (hostPlan == null)
                {
                    // levelId/levelName 指定があればそのレベルに紐づく平面図
                    var level = ElevUtil.ResolveLevel(doc, p);
                    hostPlan = ElevUtil.ResolveHostPlanForLevel(doc, level);
                }
                if (hostPlan == null)
                {
                    // 最後のフォールバック：最初の FloorPlan
                    hostPlan = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewPlan))
                        .Cast<ViewPlan>()
                        .FirstOrDefault(vp => vp.ViewType == ViewType.FloorPlan);
                }
                if (hostPlan == null) return new { ok = false, msg = "ホストとなる平面ビュー(ViewPlan)が解決できませんでした。" };

                // slotIndex/facing
                int slotIndex = p.Value<int?>("slotIndex") ?? -1;
                if (slotIndex < 0)
                {
                    string facing = p.Value<string>("facing");
                    slotIndex = ElevUtil.FacingToSlotIndex(facing);
                }
                // 安全な範囲
                if (slotIndex < 0 || slotIndex > 3) slotIndex = 0;

                int scale = p.Value<int?>("scale") ?? 100;
                string name = p.Value<string>("name") ?? $"Interior Elev {DateTime.Now:HHmmss}";

                ViewSection elevView = null;
                using (var tx = new Transaction(doc, "Create Interior Elevation"))
                {
                    tx.Start();

                    // マーカーを作成（スケールは marker の last 引数。Revit的にはビュー側にも適用する）
                    // ※ Revit 2023 API: ElevationMarker.CreateElevationMarker(doc, vft.Id, origin, scale)
                    var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, origin, scale);

                    // slotIndex で展開図を作成（0..3）
                    elevView = marker.CreateElevation(doc, hostPlan.Id, slotIndex);
                    elevView.Scale = scale;

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        try { elevView.Name = name; } catch { /* 名前競合時はRevitが自動で (2) 等を付与 */ }
                    }

                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    viewId = elevView?.Id.IntValue() ?? 0,
                    name = elevView?.Name ?? name,
                    inputUnits = ElevUtil.UnitsIn(),
                    internalUnits = ElevUtil.UnitsInt()
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------
    // 4) Room から展開図を一括作成（便利コマンド）
    //    Params :
    //      roomId            : 対象部屋
    //      levelId/levelName?: ホスト平面の解決に利用（無ければ部屋のレベル）
    //      viewTypeId?       : Elevation 用 ViewFamilyType
    //      makeFaces?        : ["E","N","W","S"] デフォルト全方向
    //      scale?            : 既定 100
    //      offsetMm?         : 部屋の重心からのオフセット量（各方向へ）
    //      namePrefix?       : ビュー名の接頭辞
    //    Return : { ok, created:[{slotIndex, viewId, name}], skipped:[..] }
    // ------------------------------------------------------------
    public class CreateRoomInteriorElevationsCommand : IRevitCommandHandler
    {
        public string CommandName => "create_room_interior_elevations";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
                var p = (JObject)cmd.Params;

                int roomId = p.Value<int>("roomId");
                var room = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(roomId)) as Autodesk.Revit.DB.Architecture.Room;
                if (room == null) return new { ok = false, msg = $"Room not found: {roomId}" };

                // Elevation ViewFamilyType
                var vft = ElevUtil.ResolveElevationViewFamilyType(doc, p);
                if (vft == null) return new { ok = false, msg = "Elevation用の ViewFamilyType が見つかりません。" };

                // ホスト平面の解決
                ViewPlan hostPlan = null;
                var level = ElevUtil.ResolveLevel(doc, p) ?? (doc.GetElement(room.LevelId) as Level);
                hostPlan = ElevUtil.ResolveHostPlanForLevel(doc, level);
                if (hostPlan == null)
                {
                    hostPlan = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewPlan))
                        .Cast<ViewPlan>()
                        .FirstOrDefault(vp => vp.ViewType == ViewType.FloorPlan);
                }
                if (hostPlan == null) return new { ok = false, msg = "ホストとなる平面ビュー(ViewPlan)が解決できませんでした。" };

                // 重心（部屋の位置）を基点に
                XYZ centerFt;
                try
                {
                    var calc = new SpatialElementGeometryCalculator(doc);
                    var res = calc.CalculateSpatialElementGeometry(room);
                    var solid = res.GetGeometry();
                    centerFt = solid?.ComputeCentroid() ?? XYZ.Zero;
                }
                catch { centerFt = XYZ.Zero; }

                // 対象方向
                var faces = (p["makeFaces"] as JArray)?.Values<string>().ToList();
                if (faces == null || faces.Count == 0) faces = new List<string> { "E", "N", "W", "S" };

                int scale = p.Value<int?>("scale") ?? 100;
                double offsetMm = p.Value<double?>("offsetMm") ?? 1000.0; // 1m オフセット
                double offFt = ElevUtil.MmToFt(offsetMm);
                string prefix = p.Value<string>("namePrefix") ?? $"Room {room.Number}-";

                var created = new List<object>();
                var skipped = new List<object>();

                using (var tx = new Transaction(doc, "Create Room Interior Elevations"))
                {
                    tx.Start();

                    // 方向→slotIndex とオフセット
                    foreach (var f in faces)
                    {
                        int slot = ElevUtil.FacingToSlotIndex(f);

                        // 方向ベクトル（平面XY）とオフセット
                        XYZ dir = XYZ.BasisX; // default East
                        switch (slot)
                        {
                            case 0: dir = XYZ.BasisX; break;           // East
                            case 1: dir = XYZ.BasisY; break;           // North
                            case 2: dir = -XYZ.BasisX; break;          // West
                            case 3: dir = -XYZ.BasisY; break;          // South
                        }
                        var origin = centerFt + (dir * offFt);

                        try
                        {
                            var marker = ElevationMarker.CreateElevationMarker(doc, vft.Id, origin, scale);
                            var elev = marker.CreateElevation(doc, hostPlan.Id, slot);
                            elev.Scale = scale;

                            string name = $"{prefix}{f}";
                            try { elev.Name = name; } catch { /* 競合時は自動で (2) */ }

                            created.Add(new { slotIndex = slot, viewId = elev.Id.IntValue(), name = elev.Name });
                        }
                        catch (Exception ex)
                        {
                            skipped.Add(new { face = f, reason = ex.Message });
                        }
                    }

                    tx.Commit();
                }

                return new
                {
                    ok = true,
                    roomId = roomId,
                    created,
                    skipped,
                    inputUnits = ElevUtil.UnitsIn(),
                    internalUnits = ElevUtil.UnitsInt()
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }
    }
}


