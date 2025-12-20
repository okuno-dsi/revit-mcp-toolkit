// ================================================================
// File: Commands/ViewOps/ResizeSectionBoxCommand.cs
// 機能: 3Dビューの切断ボックス(Section Box)をXYZ方向に拡大/縮小
// 入力: { viewId?:int, deltaMm?:{x?:double,y?:double,z?:double},
//         offsetsMm?:{minX?:double,maxX?:double,minY?:double,maxY?:double,minZ?:double,maxZ?:double} }
// 仕様:
//  - 単位は mm。正数=拡大、負数=縮小。
//  - deltaMm は各軸の“合計伸縮量”を中央から対称に適用（例: x=200 → MinXを-100, MaxXを+100）。
//  - offsetsMm は各面を個別に移動（minX>0 で MinX 面を外側へ、minX<0 で内側へ）。
//  - 両方指定可。適用順: offsets → delta の順。いずれも未指定/0なら変更なし。
// 出力: { ok, viewId, sectionBox:{min{mm},max{mm}}, applied:{deltaMm,offsetsMm} }
// 失敗: { ok:false, msg }
// ================================================================
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ViewOps
{
    public class ResizeSectionBoxCommand : IRevitCommandHandler
    {
        public string CommandName => "resize_section_box";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            // 1) 対象 3D ビューの解決（未指定ならアクティブビュー）
            View3D view3d = null;
            if (p.TryGetValue("viewId", out var vidTok))
                view3d = doc.GetElement(new ElementId(vidTok.Value<int>())) as View3D;
            else
                view3d = uidoc?.ActiveView as View3D;

            if (view3d == null || view3d.IsTemplate)
                return new { ok = false, msg = "対象は3Dビューではありません（viewId を確認、または3Dビューをアクティブにしてください）。" };

            // 2) パラメータ取得
            var deltaObj = p["deltaMm"] as JObject;
            double dxMm = deltaObj?.Value<double?>("x") ?? 0.0;
            double dyMm = deltaObj?.Value<double?>("y") ?? 0.0;
            double dzMm = deltaObj?.Value<double?>("z") ?? 0.0;

            var ofsObj = p["offsetsMm"] as JObject;
            double? ofsMinX = ofsObj?.Value<double?>("minX");
            double? ofsMaxX = ofsObj?.Value<double?>("maxX");
            double? ofsMinY = ofsObj?.Value<double?>("minY");
            double? ofsMaxY = ofsObj?.Value<double?>("maxY");
            double? ofsMinZ = ofsObj?.Value<double?>("minZ");
            double? ofsMaxZ = ofsObj?.Value<double?>("maxZ");

            bool anyChange =
                dxMm != 0 || dyMm != 0 || dzMm != 0 ||
                ofsMinX.HasValue || ofsMaxX.HasValue ||
                ofsMinY.HasValue || ofsMaxY.HasValue ||
                ofsMinZ.HasValue || ofsMaxZ.HasValue;

            if (!anyChange)
                return new { ok = false, msg = "変更寸法が指定されていません（deltaMm または offsetsMm を設定してください）。" };

            using (var tx = new Transaction(doc, "Resize Section Box"))
            {
                try
                {
                    tx.Start();

                    if (!view3d.IsSectionBoxActive)
                        view3d.IsSectionBoxActive = true;

                    var box = view3d.GetSectionBox();
                    if (box == null)
                    {
                        tx.RollBack();
                        return new { ok = false, msg = "切断ボックスを取得できませんでした。" };
                    }

                    // 現在のボックス（ローカル座標系を維持）
                    var t = box.Transform ?? Transform.Identity;
                    var min = box.Min;
                    var max = box.Max;

                    // 3) offsetsMm を適用（各面を個別に移動）
                    //    ルール: min面は +で外側(値はftに変換後 減算)、max面は +で外側(加算)
                    if (ofsMinX.HasValue) min = new XYZ(min.X - MmToFt(ofsMinX.Value), min.Y, min.Z);
                    if (ofsMaxX.HasValue) max = new XYZ(max.X + MmToFt(ofsMaxX.Value), max.Y, max.Z);
                    if (ofsMinY.HasValue) min = new XYZ(min.X, min.Y - MmToFt(ofsMinY.Value), min.Z);
                    if (ofsMaxY.HasValue) max = new XYZ(max.X, max.Y + MmToFt(ofsMaxY.Value), max.Z);
                    if (ofsMinZ.HasValue) min = new XYZ(min.X, min.Y, min.Z - MmToFt(ofsMinZ.Value));
                    if (ofsMaxZ.HasValue) max = new XYZ(max.X, max.Y, max.Z + MmToFt(ofsMaxZ.Value));

                    // 4) deltaMm を適用（各軸の合計量を対称に）
                    if (dxMm != 0)
                    {
                        double d = MmToFt(dxMm) / 2.0;
                        min = new XYZ(min.X - d, min.Y, min.Z);
                        max = new XYZ(max.X + d, max.Y, max.Z);
                    }
                    if (dyMm != 0)
                    {
                        double d = MmToFt(dyMm) / 2.0;
                        min = new XYZ(min.X, min.Y - d, min.Z);
                        max = new XYZ(max.X, max.Y + d, max.Z);
                    }
                    if (dzMm != 0)
                    {
                        double d = MmToFt(dzMm) / 2.0;
                        min = new XYZ(min.X, min.Y, min.Z - d);
                        max = new XYZ(max.X, max.Y, max.Z + d);
                    }

                    // 5) 妥当性チェック（縮小し過ぎ防止）
                    const double EPS = 1e-9;
                    if (!(min.X + EPS < max.X && min.Y + EPS < max.Y && min.Z + EPS < max.Z))
                    {
                        tx.RollBack();
                        return new { ok = false, msg = "縮小しすぎで切断ボックスが無効寸法になりました。値を見直してください。" };
                    }

                    var newBox = new BoundingBoxXYZ { Transform = t, Min = min, Max = max };
                    view3d.SetSectionBox(newBox);

                    tx.Commit();

                    // 返却は mm
                    return new
                    {
                        ok = true,
                        viewId = view3d.Id.IntegerValue,
                        sectionBox = new
                        {
                            min = new
                            {
                                x = RoundMm(min.X),
                                y = RoundMm(min.Y),
                                z = RoundMm(min.Z)
                            },
                            max = new
                            {
                                x = RoundMm(max.X),
                                y = RoundMm(max.Y),
                                z = RoundMm(max.Z)
                            }
                        },
                        applied = new
                        {
                            deltaMm = new { x = dxMm, y = dyMm, z = dzMm },
                            offsetsMm = new
                            {
                                minX = ofsMinX,
                                maxX = ofsMaxX,
                                minY = ofsMinY,
                                maxY = ofsMaxY,
                                minZ = ofsMinZ,
                                maxZ = ofsMaxZ
                            }
                        }
                    };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = ex.Message };
                }
            }
        }

        private static double MmToFt(double mm) => ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        private static double FtToMm(double ft) => ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
        private static double RoundMm(double ft) => Math.Round(FtToMm(ft), 3);
    }
}
