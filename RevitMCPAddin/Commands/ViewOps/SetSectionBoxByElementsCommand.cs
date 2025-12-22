// ================================================================
// File: Commands/ViewOps/SetSectionBoxByElementsCommand.cs
// 機能: 3Dビューの切断ボックス(Section Box)を、指定elementIdsの
//       統合BoundingBox(＋任意padding)に合わせて有効化＆更新
// 入力: { viewId?:int, elementIds?:int[], paddingMm?:double }
//       - elementIds が無ければ現在選択を使用
// 出力: { ok, viewId, sectionBox:{min:{x,y,z}, max:{x,y,z}}, skipped[] }
// 単位: 入出力とも mm（内部 ft に変換）
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class SetSectionBoxByElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "set_section_box_by_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp.ActiveUIDocument;
            var doc = uidoc.Document;
            var p = (JObject)(cmd.Params ?? new JObject());

            // 1) 対象Viewの解決（指定がなければアクティブビュー）
            View3D view3d = null;
            if (p.TryGetValue("viewId", out var vidTok))
            {
                view3d = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(vidTok.Value<int>())) as View3D;
            }
            else
            {
                view3d = uidoc.ActiveView as View3D;
            }
            if (view3d == null || view3d.IsTemplate)
            {
                return new { ok = false, msg = "対象は3Dビューではありません（viewId を確認、または3Dビューをアクティブにしてください）。" };
            }

            // 2) 対象要素IDsの決定（未指定なら選択）
            var elementIds = new List<int>();
            if (p.TryGetValue("elementIds", out var arrTok) && arrTok is JArray arr && arr.Count > 0)
            {
                elementIds.AddRange(arr.Values<int>());
            }
            else
            {
                var selIds = uidoc.Selection.GetElementIds();
                if (selIds != null && selIds.Count > 0)
                    elementIds.AddRange(selIds.Select(e => e.IntValue()));
            }
            if (elementIds.Count == 0)
            {
                return new { ok = false, msg = "elementIds が指定されておらず選択も空でした。" };
            }

            // 3) パディング（mm → ft）
            double paddingMm = p.Value<double?>("paddingMm") ?? 0.0;
            double padFt = ConvertToInternalUnits(paddingMm, UnitTypeId.Millimeters);

            // 4) 統合BoundingBox（ワールド座標）を作成
            bool any = false;
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

            var skipped = new List<object>();

            foreach (int id in elementIds.Distinct())
            {
                var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id));
                // Skip 3D Section Box pseudo-element (no-op for section box itself)
                try
                {
                    if (e?.Category?.Id?.IntValue() == -2000301)
                    {
                        skipped.Add(new { elementId = id, reason = "section_box" });
                        continue;
                    }
                } catch { }
                if (e == null) { skipped.Add(new { elementId = id, reason = "not found" }); continue; }

                // ビュー依存で切れるケースを避け、モデル座標のBBを採用
                var bb = e.get_BoundingBox(null);
                if (bb == null) { skipped.Add(new { elementId = id, reason = "no bounding box" }); continue; }

                // Transformが回転を含む可能性があるため、8頂点すべてを世界座標へ射影してからmin/max
                var tr = bb.Transform ?? Transform.Identity;
                var corners = new[]
                {
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z),
                }.Select(c => tr.OfPoint(c));

                foreach (var w in corners)
                {
                    minX = Math.Min(minX, w.X); minY = Math.Min(minY, w.Y); minZ = Math.Min(minZ, w.Z);
                    maxX = Math.Max(maxX, w.X); maxY = Math.Max(maxY, w.Y); maxZ = Math.Max(maxZ, w.Z);
                }
                any = true;
            }

            if (!any || !(minX < maxX && minY < maxY && minZ < maxZ))
            {
                return new { ok = false, msg = "有効なBoundingBoxが得られませんでした。", skipped };
            }

            // 5) パディング適用
            minX -= padFt; minY -= padFt; minZ -= padFt;
            maxX += padFt; maxY += padFt; maxZ += padFt;

            // 6) 3Dビューへ反映（SectionBoxを有効化して設定）
            using (var tx = new Transaction(doc, "Set Section Box by Elements"))
            {
                try
                {
                    tx.Start();

                    // 有効化（無効なら）
                    if (!view3d.IsSectionBoxActive) view3d.IsSectionBoxActive = true;

                    var newBox = new BoundingBoxXYZ
                    {
                        Min = new XYZ(minX, minY, minZ),
                        Max = new XYZ(maxX, maxY, maxZ),
                        // Transform はワールド直交座標に合わせる（通常は Identity でOK）
                        Transform = Transform.Identity
                    };

                    view3d.SetSectionBox(newBox);

                    tx.Commit();

                    // 返却はmm
                    return new
                    {
                        ok = true,
                        viewId = view3d.Id.IntValue(),
                        sectionBox = new
                        {
                            min = new
                            {
                                x = Math.Round(ConvertFromInternalUnits(minX, UnitTypeId.Millimeters), 3),
                                y = Math.Round(ConvertFromInternalUnits(minY, UnitTypeId.Millimeters), 3),
                                z = Math.Round(ConvertFromInternalUnits(minZ, UnitTypeId.Millimeters), 3)
                            },
                            max = new
                            {
                                x = Math.Round(ConvertFromInternalUnits(maxX, UnitTypeId.Millimeters), 3),
                                y = Math.Round(ConvertFromInternalUnits(maxY, UnitTypeId.Millimeters), 3),
                                z = Math.Round(ConvertFromInternalUnits(maxZ, UnitTypeId.Millimeters), 3)
                            }
                        },
                        skipped
                    };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message, skipped };
                }
            }
        }
    }
}


