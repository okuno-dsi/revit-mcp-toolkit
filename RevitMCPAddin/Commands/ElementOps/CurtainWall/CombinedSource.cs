using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.CurtainWall
{
    // --------------------------------------------------
    // list_curtain_wall_panels
    // --------------------------------------------------
    public class ListCurtainWallPanelsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_curtain_wall_panels";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var wall = CurtainUtil.ResolveCurtainWall(doc, p);
            if (wall == null) return new { ok = false, msg = "Curtain wall が見つからないか、CurtainGrid がありません。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;

            var ids = wall.CurtainGrid.GetPanelIds().ToList();
            int total = ids.Count;

            if (count == 0) return new { ok = true, elementId = wall.Id.IntValue(), uniqueId = wall.UniqueId, totalCount = total, units = CurtainWallUtil.UnitsSched() };

            var pageIds = ids.Skip(skip).Take(count).ToList();
            var list = new List<object>(pageIds.Count);

            foreach (var id in pageIds)
            {
                var panel = doc.GetElement(id);
                string typeName = (doc.GetElement(panel.GetTypeId()) as ElementType)?.Name ?? "";
                if (namesOnly) { list.Add(typeName); continue; }

                // BBoxサイズ（mm）
                double dx = 0, dy = 0, dz = 0;
                var bb = panel.get_BoundingBox(null);
                if (bb != null)
                {
                    dx = Math.Round(CurtainUtil.FtToMm(Math.Abs(bb.Max.X - bb.Min.X)), 3);
                    dy = Math.Round(CurtainUtil.FtToMm(Math.Abs(bb.Max.Y - bb.Min.Y)), 3);
                    dz = Math.Round(CurtainUtil.FtToMm(Math.Abs(bb.Max.Z - bb.Min.Z)), 3);
                }

                // 面積（m2）: HOST_AREA_COMPUTED 優先
                double areaM2 = 0;
                var pArea = panel.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (pArea != null && pArea.StorageType == StorageType.Double)
                    areaM2 = Math.Round(CurtainUtil.Ft2ToM2(pArea.AsDouble()), 3);

                list.Add(new
                {
                    panelIndex = ids.IndexOf(id),
                    elementId = id.IntValue(),
                    uniqueId = panel.UniqueId,
                    typeId = panel.GetTypeId()?.IntValue(),
                    typeName,
                    sizeMm = new { dx, dy, dz },
                    areaM2
                });
            }

            return new { ok = true, elementId = wall.Id.IntValue(), uniqueId = wall.UniqueId, totalCount = total, panels = list, units = CurtainWallUtil.UnitsSched() };
        }
    }
    // --------------------------------------------------
    // get_curtain_wall_panel_geometry
    // --------------------------------------------------
    public class GetCurtainWallPanelGeometryCommand : IRevitCommandHandler
    {
        public string CommandName => "get_curtain_wall_panel_geometry";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var wall = CurtainUtil.ResolveCurtainWall(doc, p);
            if (wall == null) return new { ok = false, msg = "Curtain wall が見つからないか、CurtainGrid がありません。" };

            int panelIndex = p.Value<int?>("panelIndex") ?? -1;
            var ids = wall.CurtainGrid.GetPanelIds().ToList();
            if (panelIndex < 0 || panelIndex >= ids.Count) return new { ok = false, msg = "panelIndex が範囲外です。" };

            var panel = doc.GetElement(ids[panelIndex]);
            var bb = panel.get_BoundingBox(null);
            if (bb == null) return new { ok = false, msg = "BoundingBox を取得できません。" };

            var min = new { x = Math.Round(CurtainUtil.FtToMm(bb.Min.X), 3), y = Math.Round(CurtainUtil.FtToMm(bb.Min.Y), 3), z = Math.Round(CurtainUtil.FtToMm(bb.Min.Z), 3) };
            var max = new { x = Math.Round(CurtainUtil.FtToMm(bb.Max.X), 3), y = Math.Round(CurtainUtil.FtToMm(bb.Max.Y), 3), z = Math.Round(CurtainUtil.FtToMm(bb.Max.Z), 3) };
            var size = new
            {
                dx = Math.Round(CurtainUtil.FtToMm(bb.Max.X - bb.Min.X), 3),
                dy = Math.Round(CurtainUtil.FtToMm(bb.Max.Y - bb.Min.Y), 3),
                dz = Math.Round(CurtainUtil.FtToMm(bb.Max.Z - bb.Min.Z), 3)
            };
            var center = new
            {
                x = Math.Round(CurtainUtil.FtToMm((bb.Max.X + bb.Min.X) / 2), 3),
                y = Math.Round(CurtainUtil.FtToMm((bb.Max.Y + bb.Min.Y) / 2), 3),
                z = Math.Round(CurtainUtil.FtToMm((bb.Max.Z + bb.Min.Z) / 2), 3)
            };

            return new { ok = true, elementId = wall.Id.IntValue(), uniqueId = wall.UniqueId, panelIndex, panelId = panel.Id.IntValue(), min, max, center, size, units = CurtainWallUtil.UnitsGeom() };
        }
    }

    // --------------------------------------------------
    // set_curtain_wall_panel_type
    // --------------------------------------------------
    public class SetCurtainWallPanelTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "set_curtain_wall_panel_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var wall = CurtainUtil.ResolveCurtainWall(doc, p);
            if (wall == null) return new { ok = false, msg = "Curtain wall が見つからないか、CurtainGrid がありません。" };

            int panelIndex = p.Value<int?>("panelIndex") ?? -1;
            var ids = wall.CurtainGrid.GetPanelIds().ToList();
            if (panelIndex < 0 || panelIndex >= ids.Count) return new { ok = false, msg = "panelIndex が範囲外です。" };

            var panel = doc.GetElement(ids[panelIndex]) as Element;
            if (panel == null) return new { ok = false, msg = "Panel element が見つかりません。" };

            var newType = CurtainWallUtil.ResolvePanelType(doc, p);
            if (newType == null) return new { ok = false, msg = "パネルタイプが解決できません（typeId or typeName(+familyName)）。" };

            using (var tx = new Transaction(doc, "Set Curtain Panel Type"))
            {
                tx.Start();
                try
                {
                    // FamilyInstance の場合は ChangeTypeId、Element でも ChangeTypeId 利用可（失敗なら例外）
                    panel.ChangeTypeId(newType.Id);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"タイプ変更に失敗: {ex.Message}" };
                }
                tx.Commit();
            }

            return new { ok = true, panelIndex, panelId = panel.Id.IntValue(), typeId = newType.Id.IntValue(), typeName = newType.Name };
        }
    }

    // --------------------------------------------------
    // add_curtain_wall_panel (要独自実装)
    // --------------------------------------------------
    public class AddCurtainWallPanelCommand : IRevitCommandHandler
    {
        public string CommandName => "add_curtain_wall_panel";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var wall = CurtainUtil.ResolveCurtainWall(doc, p);
            if (wall == null) return new { ok = false, msg = "Curtain wall が見つからないか、CurtainGrid がありません。" };

            int panelIndex = p.Value<int?>("panelIndex") ?? -1;
            var ids = wall.CurtainGrid.GetPanelIds().ToList();
            if (panelIndex < 0 || panelIndex >= ids.Count) return new { ok = false, msg = "panelIndex が範囲外です。" };

            var panel = doc.GetElement(ids[panelIndex]);
            if (panel == null) return new { ok = false, msg = "Panel が見つかりません。" };

            // 追加 = Empty → 指定タイプへ変更
            var targetType = CurtainWallUtil.ResolvePanelType(doc, p);
            if (targetType == null) return new { ok = false, msg = "挿入先タイプを解決できません（typeId or typeName(+familyName)）。" };

            using (var tx = new Transaction(doc, "Add Curtain Panel"))
            {
                tx.Start();
                try
                {
                    panel.ChangeTypeId(targetType.Id);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"パネル挿入（タイプ変更）に失敗: {ex.Message}" };
                }
                tx.Commit();
            }

            return new { ok = true, panelIndex, panelId = panel.Id.IntValue(), typeId = targetType.Id.IntValue(), typeName = targetType.Name };
        }
    }


    // --------------------------------------------------
    // remove_curtain_wall_panel (要独自実装)
    // --------------------------------------------------
    public class RemoveCurtainWallPanelCommand : IRevitCommandHandler
    {
        public string CommandName => "remove_curtain_wall_panel";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var wall = CurtainUtil.ResolveCurtainWall(doc, p);
            if (wall == null) return new { ok = false, msg = "Curtain wall が見つからないか、CurtainGrid がありません。" };

            int panelIndex = p.Value<int?>("panelIndex") ?? -1;
            var ids = wall.CurtainGrid.GetPanelIds().ToList();
            if (panelIndex < 0 || panelIndex >= ids.Count) return new { ok = false, msg = "panelIndex が範囲外です。" };

            var panel = doc.GetElement(ids[panelIndex]);
            if (panel == null) return new { ok = false, msg = "Panel が見つかりません。" };

            // 削除 = System Panel: Empty へ変更
            var empty = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_CurtainWallPanels).Cast<FamilySymbol>()
                .FirstOrDefault(s => string.Equals(s.Family?.Name ?? "", "System Panel", StringComparison.OrdinalIgnoreCase)
                                  && string.Equals(s.Name ?? "", "Empty", StringComparison.OrdinalIgnoreCase));
            if (empty == null) return new { ok = false, msg = "System Panel: Empty が見つかりません（テンプレートをご確認ください）。" };

            using (var tx = new Transaction(doc, "Remove Curtain Panel"))
            {
                tx.Start();
                try { panel.ChangeTypeId(empty.Id); }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = $"パネル削除（Empty化）に失敗: {ex.Message}" }; }
                tx.Commit();
            }

            return new { ok = true, panelIndex, panelId = panel.Id.IntValue(), typeId = empty.Id.IntValue(), typeName = empty.Name };
        }
    }


    // --------------------------------------------------
    // list_mullions
    // --------------------------------------------------
    public class ListMullionsCommand : IRevitCommandHandler
    {
        public string CommandName => "list_mullions";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var wall = CurtainUtil.ResolveCurtainWall(doc, p);
            if (wall == null) return new { ok = false, msg = "Curtain wall が見つからないか、CurtainGrid がありません。" };

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;

            var mids = wall.CurtainGrid.GetMullionIds().ToList();
            int total = mids.Count;
            if (count == 0) return new { ok = true, elementId = wall.Id.IntValue(), uniqueId = wall.UniqueId, totalCount = total, units = CurtainWallUtil.UnitsSched() };

            var page = mids.Skip(skip).Take(count).Select((eid, idx) =>
            {
                var m = doc.GetElement(eid) as Mullion;
                var c = (m?.Location as LocationCurve)?.Curve;
                double lenM = c != null ? Math.Round(CurtainUtil.FtToM(c.Length), 3) : 0.0;
                string mType = (doc.GetElement(m?.GetTypeId()) as ElementType)?.Name ?? "";
                return new
                {
                    mullionIndex = idx,
                    elementId = eid.IntValue(),
                    uniqueId = m?.UniqueId,
                    typeId = m?.GetTypeId()?.IntValue(),
                    typeName = mType,
                    lengthM = lenM
                };
            }).ToList();

            return new { ok = true, elementId = wall.Id.IntValue(), uniqueId = wall.UniqueId, totalCount = total, mullions = page, units = CurtainWallUtil.UnitsSched() };
        }
    }


    // --------------------------------------------------
    // create_mullion
    // --------------------------------------------------
    public class CreateMullionCommand : IRevitCommandHandler
    {
        public string CommandName => "create_mullion";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            // ★ フル修飾 Wall
            var wall = CurtainWallUtil.ResolveCurtainWall(doc, p);
            if (wall == null) return new { ok = false, msg = "Curtain wall が見つからないか、CurtainGrid がありません。" };

            var gl = CurtainWallUtil.ResolveGridLine(wall, doc, p);
            if (gl == null) return new { ok = false, msg = "グリッドラインを解決できません（gridLineId or orientation+gridLineIndex）。" };

            var mType = CurtainWallUtil.ResolveMullionType(doc, p);
            if (mType == null) return new { ok = false, msg = "MullionType が解決できません（mullionTypeId or typeName）。" };

            // 追加前のIDスナップショット（ICollection→List に揃える）
            var beforeIds = wall.CurtainGrid.GetMullionIds().ToList();

            // ★ Revit 2023: AddMullions は ElementSet を返す
            ElementSet createdSet = null;

            using (var tx = new Transaction(doc, "Create Mullion"))
            {
                tx.Start();
                try
                {
                    var seg = gl.AllSegmentCurves?.Cast<Curve>()?.FirstOrDefault();
                    if (seg == null) { tx.RollBack(); return new { ok = false, msg = "グリッドラインにセグメントがありません。" }; }

                    // 2023: ElementSet / 2024+: IList<ElementId>
                    // ここは 2023 を前提に ElementSet で受ける
                    createdSet = gl.AddMullions(seg, mType, false);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"マリオン作成に失敗: {ex.Message}" };
                }
                tx.Commit();
            }

            // ElementSet → List<ElementId> へ変換
            var createdIds = new List<ElementId>();
            if (createdSet != null)
            {
                foreach (Element e in createdSet) createdIds.Add(e.Id);
            }

            // 追加後のID一覧
            var afterIds = wall.CurtainGrid.GetMullionIds().ToList();

            // 新規に増えたIDを特定（最初の1本）
            var newId = createdIds.FirstOrDefault(id => !beforeIds.Contains(id));
            // もし上のやり方で取れない場合は afterIds の差分でも可
            if (newId == null || newId == ElementId.InvalidElementId)
            {
                newId = afterIds.FirstOrDefault(id => !beforeIds.Contains(id));
            }

            int newIndex = -1;
            if (newId != null && newId != ElementId.InvalidElementId)
                newIndex = afterIds.IndexOf(newId);

            var mull = (newId != null && newId != ElementId.InvalidElementId) ? doc.GetElement(newId) as Mullion : null;

            return new
            {
                ok = true,
                newMullionIndex = newIndex,
                elementId = newId?.IntValue(),
                uniqueId = mull?.UniqueId,
                typeId = (mull?.GetTypeId())?.IntValue()
            };
        }
    }


    // --------------------------------------------------
    // update_mullion_type
    // --------------------------------------------------
    public class UpdateMullionTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "update_mullion_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var wall = CurtainUtil.ResolveCurtainWall(doc, p);
            if (wall == null) return new { ok = false, msg = "Curtain wall が見つからないか、CurtainGrid がありません。" };

            int mullionIndex = p.Value<int?>("mullionIndex") ?? -1;
            var ids = wall.CurtainGrid.GetMullionIds().ToList();
            if (mullionIndex < 0 || mullionIndex >= ids.Count) return new { ok = false, msg = "mullionIndex が範囲外です。" };

            var mullion = doc.GetElement(ids[mullionIndex]) as Mullion;
            if (mullion == null) return new { ok = false, msg = "Mullion が見つかりません。" };

            var mType = CurtainWallUtil.ResolveMullionType(doc, p);
            if (mType == null) return new { ok = false, msg = "MullionType が解決できません（mullionTypeId or typeName）。" };

            using (var tx = new Transaction(doc, "Update Mullion Type"))
            {
                tx.Start();
                try { mullion.ChangeTypeId(mType.Id); }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = $"タイプ変更に失敗: {ex.Message}" }; }
                tx.Commit();
            }

            return new { ok = true, mullionIndex, elementId = mullion.Id.IntValue(), typeId = mType.Id.IntValue(), typeName = mType.Name };
        }
    }

    // --------------------------------------------------
    // delete_mullion
    // --------------------------------------------------
    public class DeleteMullionCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_mullion";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var p = (JObject)cmd.Params;
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var wall = CurtainWallUtil.ResolveCurtainWall(doc, p);
            if (wall == null) return new { ok = false, msg = "Curtain wall が見つからないか、CurtainGrid がありません。" };

            int mullionIndex = p.Value<int?>("mullionIndex") ?? -1;
            var ids = wall.CurtainGrid.GetMullionIds().ToList();
            if (mullionIndex < 0 || mullionIndex >= ids.Count) return new { ok = false, msg = "mullionIndex が範囲外です。" };

            using (var tx = new Transaction(doc, "Delete Mullion"))
            {
                tx.Start();
                try { doc.Delete(ids[mullionIndex]); }
                catch (Exception ex) { tx.RollBack(); return new { ok = false, msg = $"削除に失敗: {ex.Message}" }; }
                tx.Commit();
            }

            return new { ok = true, mullionIndex };
        }
    }
}

