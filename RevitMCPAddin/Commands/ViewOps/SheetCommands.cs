// ================================================================
// File: Commands/ViewOps/SheetCommands.cs
// Target : Revit 2023 / .NET Framework 4.8 / C# 8
// Summary: ViewSheet 操作（create/get/delete/place/remove）
// I/O    : 位置は mm、内部単位(ft)に変換。エラー時 { ok:false, msg }。
// Note   : place_view_on_sheet は一般ビュー(Viewport)とスケジュール
//          (ScheduleSheetInstance)の両方に対応。
// ================================================================
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
    internal static class SheetUtil
    {
        public static double MmToFt(double mm) =>
            ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        public static double FtToMm(double ft) =>
            ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        public static ViewSheet ResolveSheet(Document doc, JObject p)
        {
            // sheetId > uniqueId > sheetNumber（SHEET_NUMBER）
            int sheetId = p.Value<int?>("sheetId") ?? p.Value<int?>("viewId") ?? 0; // エージェントが viewId と書く事故に備える
            if (sheetId > 0)
            {
                var s = doc.GetElement(new ElementId(sheetId)) as ViewSheet;
                if (s != null) return s;
            }

            string uid = p.Value<string>("uniqueId");
            if (!string.IsNullOrWhiteSpace(uid))
            {
                var s = doc.GetElement(uid) as ViewSheet;
                if (s != null) return s;
            }

            string sheetNumber = p.Value<string>("sheetNumber");
            if (!string.IsNullOrWhiteSpace(sheetNumber))
            {
                var s = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .FirstOrDefault(vs => string.Equals(
                            (vs.get_Parameter(BuiltInParameter.SHEET_NUMBER)?.AsString() ?? ""),
                            sheetNumber, StringComparison.OrdinalIgnoreCase));
                if (s != null) return s;
            }
            return null;
        }

        public static View ResolveView(Document doc, JObject p)
        {
            int id = p.Value<int?>("viewId") ?? 0;
            if (id > 0)
            {
                var v = doc.GetElement(new ElementId(id)) as View;
                if (v != null) return v;
            }
            string uid = p.Value<string>("viewUniqueId");
            if (!string.IsNullOrWhiteSpace(uid))
            {
                var v = doc.GetElement(uid) as View;
                if (v != null) return v;
            }
            return null;
        }

        public static FamilySymbol ResolveTitleBlockType(Document doc, JObject p)
        {
            int typeId = p.Value<int?>("titleBlockTypeId") ?? 0;
            if (typeId > 0)
            {
                var fs = doc.GetElement(new ElementId(typeId)) as FamilySymbol;
                if (fs != null && fs.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_TitleBlocks)
                    return fs;
            }

            string familyName = p.Value<string>("titleBlockFamilyName");
            string typeName = p.Value<string>("titleBlockTypeName");

            var q = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>();

            if (!string.IsNullOrWhiteSpace(familyName))
                q = q.Where(x => string.Equals(x.FamilyName ?? "", familyName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(typeName))
                q = q.Where(x => string.Equals(x.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));

            var fs2 = q.OrderBy(x => x.FamilyName ?? "")
                       .ThenBy(x => x.Name ?? "")
                       .FirstOrDefault();

            return fs2; // null もあり得る（無題ブロックで作る）
        }

        public static (double widthMm, double heightMm) GetSheetSizeMm(ViewSheet sheet)
        {
            double w = 0, h = 0;
            try
            {
                var pw = sheet.get_Parameter(BuiltInParameter.SHEET_WIDTH);
                var ph = sheet.get_Parameter(BuiltInParameter.SHEET_HEIGHT);
                if (pw != null && pw.StorageType == StorageType.Double) w = FtToMm(pw.AsDouble());
                if (ph != null && ph.StorageType == StorageType.Double) h = FtToMm(ph.AsDouble());
            }
            catch { /* ignore */ }
            return (Math.Round(w, 3), Math.Round(h, 3));
        }
    }

    // ------------------------------------------------------------
    // 1) create_sheet
    // ------------------------------------------------------------
    public class CreateSheetCommand : IRevitCommandHandler
    {
        public string CommandName => "create_sheet";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)(cmd.Params ?? new JObject());

            string sheetNumber = (p.Value<string>("sheetNumber") ?? "").Trim();
            string sheetName = (p.Value<string>("sheetName") ?? p.Value<string>("name") ?? "").Trim();
            bool noTitleBlock = p.Value<bool?>("noTitleBlock") ?? false;

            var titleType = SheetUtil.ResolveTitleBlockType(doc, p);
            ElementId tId = (noTitleBlock || titleType == null) ? ElementId.InvalidElementId : titleType.Id;

            ViewSheet sheet = null;
            using (var tx = new Transaction(doc, "Create Sheet"))
            {
                try
                {
                    tx.Start();
                    sheet = ViewSheet.Create(doc, tId);
                    if (!string.IsNullOrEmpty(sheetNumber))
                        sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER)?.Set(sheetNumber);
                    if (!string.IsNullOrEmpty(sheetName))
                        sheet.get_Parameter(BuiltInParameter.SHEET_NAME)?.Set(sheetName);
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"シート作成に失敗: {ex.Message}" };
                }
            }

            var (w, h) = SheetUtil.GetSheetSizeMm(sheet);
            return new
            {
                ok = true,
                sheetId = sheet.Id.IntegerValue,
                uniqueId = sheet.UniqueId,
                sheetNumber = sheet.get_Parameter(BuiltInParameter.SHEET_NUMBER)?.AsString(),
                sheetName = sheet.get_Parameter(BuiltInParameter.SHEET_NAME)?.AsString(),
                sizeMm = new { width = w, height = h },
                titleBlockTypeId = titleType?.Id.IntegerValue
            };
        }
    }

    // ------------------------------------------------------------
    // 2) get_sheets
    // ------------------------------------------------------------
    public class GetSheetsCommand : IRevitCommandHandler
    {
        public string CommandName => "get_sheets";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)(cmd.Params ?? new JObject());

            int skip = p.Value<int?>("skip") ?? 0;
            int count = p.Value<int?>("count") ?? int.MaxValue;
            bool namesOnly = p.Value<bool?>("namesOnly") ?? false;
            bool includePlacedViews = p.Value<bool?>("includePlacedViews") ?? false;
            string numberContains = (p.Value<string>("sheetNumberContains") ?? "").Trim();
            string nameContains = (p.Value<string>("nameContains") ?? "").Trim();

            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .ToList();

            if (!string.IsNullOrEmpty(numberContains))
                all = all.Where(vs =>
                    ((vs.get_Parameter(BuiltInParameter.SHEET_NUMBER)?.AsString() ?? "")
                        .IndexOf(numberContains, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            if (!string.IsNullOrEmpty(nameContains))
                all = all.Where(vs =>
                    ((vs.get_Parameter(BuiltInParameter.SHEET_NAME)?.AsString() ?? "")
                        .IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            var ordered = all
                .Select(vs => new {
                    vs,
                    number = vs.get_Parameter(BuiltInParameter.SHEET_NUMBER)?.AsString() ?? "",
                    name = vs.get_Parameter(BuiltInParameter.SHEET_NAME)?.AsString() ?? ""
                })
                .OrderBy(x => x.number)
                .ThenBy(x => x.name)
                .ThenBy(x => x.vs.Id.IntegerValue)
                .Select(x => x.vs)
                .ToList();

            int total = ordered.Count;

            // meta-only
            if (skip == 0 && p.ContainsKey("count") && count == 0)
                return new { ok = true, totalCount = total };

            // namesOnly
            if (namesOnly)
            {
                var pageNames = ordered.Skip(skip).Take(count)
                    .Select(vs => vs.get_Parameter(BuiltInParameter.SHEET_NUMBER)?.AsString() ?? "")
                    .ToList();
                return new { ok = true, totalCount = total, names = pageNames };
            }

            // full items
            var list = new List<object>();
            foreach (var vs in ordered.Skip(skip).Take(count))
            {
                var num = vs.get_Parameter(BuiltInParameter.SHEET_NUMBER)?.AsString() ?? "";
                var nm = vs.get_Parameter(BuiltInParameter.SHEET_NAME)?.AsString() ?? "";
                var (w, h) = SheetUtil.GetSheetSizeMm(vs);

                object placed = null;
                if (includePlacedViews)
                {
                    var items = new List<object>();

                    // Viewports
                    foreach (var vpId in vs.GetAllViewports() ?? new List<ElementId>())
                    {
                        var vp = doc.GetElement(vpId) as Viewport;
                        if (vp == null) continue;
                        var v = doc.GetElement(vp.ViewId) as View;
                        string vname = v?.Name ?? "";
                        string vtype = v?.ViewType.ToString() ?? "";

                        XYZ center;
                        try { center = vp.GetBoxCenter(); }
                        catch
                        {
                            // fallback: BoxOutline の中心
                            var bo = vp.GetBoxOutline();
                            center = (bo.MaximumPoint + bo.MinimumPoint) * 0.5;
                        }

                        items.Add(new
                        {
                            kind = "viewport",
                            viewportId = vp.Id.IntegerValue,
                            viewId = vp.ViewId.IntegerValue,
                            viewName = vname,
                            viewType = vtype,
                            centerMm = new
                            {
                                x = Math.Round(SheetUtil.FtToMm(center.X), 3),
                                y = Math.Round(SheetUtil.FtToMm(center.Y), 3)
                            }
                        });
                    }

                    // Schedule instances
                    var schs = new FilteredElementCollector(doc, vs.Id)
                        .OfClass(typeof(ScheduleSheetInstance))
                        .Cast<ScheduleSheetInstance>()
                        .ToList();

                    foreach (var si in schs)
                    {
                        var loc = si.Point; // ft
                        items.Add(new
                        {
                            kind = "schedule",
                            scheduleInstanceId = si.Id.IntegerValue,
                            scheduleViewId = si.ScheduleId.IntegerValue,
                            locationMm = new
                            {
                                x = Math.Round(SheetUtil.FtToMm(loc.X), 3),
                                y = Math.Round(SheetUtil.FtToMm(loc.Y), 3)
                            }
                        });
                    }

                    placed = items;
                }

                list.Add(new
                {
                    sheetId = vs.Id.IntegerValue,
                    uniqueId = vs.UniqueId,
                    sheetNumber = num,
                    sheetName = nm,
                    sizeMm = new { width = w, height = h },
                    placedItems = placed
                });
            }

            return new { ok = true, totalCount = total, sheets = list };
        }
    }

    // ------------------------------------------------------------
    // 3) delete_sheet
    // ------------------------------------------------------------
    public class DeleteSheetCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_sheet";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)(cmd.Params ?? new JObject());

            var sheet = SheetUtil.ResolveSheet(doc, p);
            if (sheet == null) return new { ok = false, msg = "シートが見つかりません（sheetId/uniqueId/sheetNumber）。" };

            using (var tx = new Transaction(doc, "Delete Sheet"))
            {
                try
                {
                    tx.Start();
                    var deleted = doc.Delete(sheet.Id);
                    tx.Commit();
                    return new { ok = true, deletedCount = deleted?.Count ?? 0 };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"削除に失敗: {ex.Message}" };
                }
            }
        }
    }

    // ------------------------------------------------------------
    // 4) place_view_on_sheet
    // ------------------------------------------------------------
    public class PlaceViewOnSheetCommand : IRevitCommandHandler
    {
        public string CommandName => "place_view_on_sheet";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)(cmd.Params ?? new JObject());

            var sheet = SheetUtil.ResolveSheet(doc, p);
            if (sheet == null) return new { ok = false, msg = "シートが見つかりません（sheetId/uniqueId/sheetNumber）。" };

            var view = SheetUtil.ResolveView(doc, p);
            if (view == null) return new { ok = false, msg = "ビューが見つかりません（viewId/viewUniqueId）。" };

            // 配置位置
            XYZ pos;
            bool center = p.Value<bool?>("centerOnSheet") ?? false;
            if (center)
            {
                var (w, h) = SheetUtil.GetSheetSizeMm(sheet);
                pos = new XYZ(SheetUtil.MmToFt(w * 0.5), SheetUtil.MmToFt(h * 0.5), 0);
            }
            else
            {
                var loc = p["location"] as JObject;
                if (loc == null) return new { ok = false, msg = "location {x,y} (mm) を指定してください。centerOnSheet:true でも可。" };
                pos = new XYZ(SheetUtil.MmToFt(loc.Value<double>("x")),
                              SheetUtil.MmToFt(loc.Value<double>("y")), 0);
            }

            using (var tx = new Transaction(doc, "Place View On Sheet"))
            {
                try
                {
                    tx.Start();

                    // スケジュールかどうか
                    if (view is ViewSchedule vs)
                    {
                        var inst = ScheduleSheetInstance.Create(doc, sheet.Id, vs.Id, pos);
                        tx.Commit();
                        return new
                        {
                            ok = true,
                            kind = "schedule",
                            scheduleInstanceId = inst.Id.IntegerValue,
                            sheetId = sheet.Id.IntegerValue,
                            viewId = vs.Id.IntegerValue
                        };
                    }

                    // 一般ビュー（テンプレートは不可）
                    if (view.IsTemplate)
                    {
                        tx.RollBack();
                        return new { ok = false, msg = "ビュー テンプレートはシートに配置できません。" };
                    }

                    if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                    {
                        tx.RollBack();
                        return new { ok = false, msg = "このビューはシートに配置できません（既に配置済み/非対応ビュー等）。" };
                    }

                    var vp = Viewport.Create(doc, sheet.Id, view.Id, pos);
                    tx.Commit();

                    return new
                    {
                        ok = true,
                        kind = "viewport",
                        viewportId = vp.Id.IntegerValue,
                        sheetId = sheet.Id.IntegerValue,
                        viewId = view.Id.IntegerValue
                    };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"配置に失敗: {ex.Message}" };
                }
            }
        }
    }

    // ------------------------------------------------------------
    // 5) remove_view_from_sheet
    // ------------------------------------------------------------
    public class RemoveViewFromSheetCommand : IRevitCommandHandler
    {
        public string CommandName => "remove_view_from_sheet";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)(cmd.Params ?? new JObject());

            var sheet = SheetUtil.ResolveSheet(doc, p);
            if (sheet == null) return new { ok = false, msg = "シートが見つかりません（sheetId/uniqueId/sheetNumber）。" };

            int viewportId = p.Value<int?>("viewportId") ?? 0;
            int viewId = p.Value<int?>("viewId") ?? 0;
            int scheduleInstanceId = p.Value<int?>("scheduleInstanceId") ?? 0;

            var toDelete = new List<ElementId>();

            // 直接指定: Viewport
            if (viewportId > 0)
            {
                var vp = doc.GetElement(new ElementId(viewportId)) as Viewport;
                if (vp != null && vp.SheetId == sheet.Id) toDelete.Add(vp.Id);
            }

            // 直接指定: ScheduleSheetInstance
            if (scheduleInstanceId > 0)
            {
                var si = doc.GetElement(new ElementId(scheduleInstanceId)) as ScheduleSheetInstance;
                if (si != null && si.OwnerViewId == sheet.Id) toDelete.Add(si.Id);
            }

            // viewId 指定 → 同シート上の Viewport / Schedule を探して削除
            if (viewId > 0)
            {
                foreach (var vpId in sheet.GetAllViewports() ?? new List<ElementId>())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp != null && vp.ViewId.IntegerValue == viewId) toDelete.Add(vp.Id);
                }

                var schs = new FilteredElementCollector(doc, sheet.Id)
                    .OfClass(typeof(ScheduleSheetInstance))
                    .Cast<ScheduleSheetInstance>()
                    .ToList();

                foreach (var si in schs)
                {
                    if (si.ScheduleId.IntegerValue == viewId) toDelete.Add(si.Id);
                }
            }

            if (toDelete.Count == 0)
                return new { ok = false, msg = "削除対象が見つかりません（viewportId / scheduleInstanceId / viewId を確認）。" };

            using (var tx = new Transaction(doc, "Remove View From Sheet"))
            {
                try
                {
                    tx.Start();
                    var deleted = doc.Delete(toDelete);
                    tx.Commit();
                    return new { ok = true, requested = toDelete.Count, deletedCount = deleted?.Count ?? 0 };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"シートからの削除に失敗: {ex.Message}" };
                }
            }
        }
    }

    // ------------------------------------------------------------
    // 6) replace_view_on_sheet
    // ------------------------------------------------------------
    /// <summary>
    /// シート上の既存ビュー（またはスケジュール）を別のビューに入れ替える。
    /// JSON-RPC:
    ///   method: "replace_view_on_sheet"
    ///   params: {
    ///     // 既存配置の特定
    ///     viewportId?: int,              // これがあれば最優先
    ///     sheetId|uniqueId|sheetNumber?, // viewportId が無い場合に使用
    ///     oldViewId?: int,
    ///     oldViewUniqueId?: string,
    ///
    ///     // 新しいビュー
    ///     newViewId?: int,
    ///     newViewUniqueId?: string,
    ///
    ///     // 位置・回転・スケール
    ///     keepLocation?: bool,           // 既定: true（旧ビューポート中心を再利用）
    ///     centerOnSheet?: bool,          // keepLocation:false のときに有効
    ///     location?: { x:double, y:double }, // mm, keepLocation:false かつ centerOnSheet:false のとき必須
    ///     copyRotation?: bool,           // 既定: true（旧ビューポートの Rotation を新しいものへ）
    ///     copyScale?: bool               // 既定: false（true なら View.Scale を旧ビューに合わせる）
    ///   }
    /// </summary>
    public class ReplaceViewOnSheetCommand : IRevitCommandHandler
    {
        public string CommandName => "replace_view_on_sheet";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());

            int viewportId = p.Value<int?>("viewportId") ?? 0;
            int oldViewId = p.Value<int?>("oldViewId") ?? 0;
            string oldViewUid = p.Value<string>("oldViewUniqueId");

            int newViewId = p.Value<int?>("newViewId") ?? 0;
            string newViewUid = p.Value<string>("newViewUniqueId");

            if (viewportId <= 0 && oldViewId <= 0 && string.IsNullOrWhiteSpace(oldViewUid))
                return new { ok = false, msg = "viewportId か oldViewId/oldViewUniqueId のいずれかを指定してください。" };

            if (newViewId <= 0 && string.IsNullOrWhiteSpace(newViewUid))
                return new { ok = false, msg = "newViewId または newViewUniqueId を指定してください。" };

            ViewSheet sheet = null;
            Viewport oldViewport = null;
            ScheduleSheetInstance oldSchedule = null;
            View oldView = null;

            // 既存配置を特定（case1: viewportId 指定）
            if (viewportId > 0)
            {
                var elem = doc.GetElement(new ElementId(viewportId));
                oldViewport = elem as Viewport;
                if (oldViewport != null)
                {
                    sheet = doc.GetElement(oldViewport.SheetId) as ViewSheet;
                    oldView = doc.GetElement(oldViewport.ViewId) as View;
                }
                else
                {
                    oldSchedule = elem as ScheduleSheetInstance;
                    if (oldSchedule != null)
                    {
                        sheet = doc.GetElement(oldSchedule.OwnerViewId) as ViewSheet;
                        oldView = doc.GetElement(oldSchedule.ScheduleId) as View;
                    }
                }

                if (sheet == null)
                    return new { ok = false, msg = "viewportId からシートを特定できませんでした。" };
            }
            else
            {
                // case2: シート＋旧ビュー指定
                sheet = SheetUtil.ResolveSheet(doc, p);
                if (sheet == null)
                    return new { ok = false, msg = "シートが見つかりません（sheetId/uniqueId/sheetNumber）。" };

                if (oldViewId > 0)
                    oldView = doc.GetElement(new ElementId(oldViewId)) as View;
                if (oldView == null && !string.IsNullOrWhiteSpace(oldViewUid))
                    oldView = doc.GetElement(oldViewUid) as View;
                if (oldView == null)
                    return new { ok = false, msg = "旧ビューが見つかりません（oldViewId/oldViewUniqueId）。" };

                // 同シート上の Viewport / Schedule を探す
                foreach (var vpId in sheet.GetAllViewports() ?? new List<ElementId>())
                {
                    var vp = doc.GetElement(vpId) as Viewport;
                    if (vp != null && vp.ViewId.IntegerValue == oldView.Id.IntegerValue)
                    {
                        oldViewport = vp;
                        break;
                    }
                }

                if (oldViewport == null)
                {
                    var schs = new FilteredElementCollector(doc, sheet.Id)
                        .OfClass(typeof(ScheduleSheetInstance))
                        .Cast<ScheduleSheetInstance>();

                    foreach (var si in schs)
                    {
                        if (si.ScheduleId.IntegerValue == oldView.Id.IntegerValue)
                        {
                            oldSchedule = si;
                            break;
                        }
                    }
                }

                if (oldViewport == null && oldSchedule == null)
                    return new { ok = false, msg = "指定した旧ビューは、このシート上に配置されていません。" };
            }

            // 新しいビューを解決
            View newView = null;
            if (newViewId > 0)
                newView = doc.GetElement(new ElementId(newViewId)) as View;
            if (newView == null && !string.IsNullOrWhiteSpace(newViewUid))
                newView = doc.GetElement(newViewUid) as View;
            if (newView == null)
                return new { ok = false, msg = "新しいビューが見つかりません（newViewId/newViewUniqueId）。" };

            if (newView.IsTemplate)
                return new { ok = false, msg = "ビュー テンプレートはシートに配置できません。" };

            // 旧配置位置（ft）を取得
            XYZ targetPosFt;
            if (oldViewport != null)
            {
                try
                {
                    targetPosFt = oldViewport.GetBoxCenter();
                }
                catch
                {
                    var bo = oldViewport.GetBoxOutline();
                    targetPosFt = (bo.MaximumPoint + bo.MinimumPoint) * 0.5;
                }
            }
            else if (oldSchedule != null)
            {
                targetPosFt = oldSchedule.Point;
            }
            else
            {
                return new { ok = false, msg = "旧配置位置を取得できませんでした。" };
            }

            // 位置指定オプションの反映
            bool keepLocation = p.Value<bool?>("keepLocation") ?? true;
            bool centerOnSheet = p.Value<bool?>("centerOnSheet") ?? false;
            if (!keepLocation)
            {
                if (centerOnSheet)
                {
                    var (w, h) = SheetUtil.GetSheetSizeMm(sheet);
                    targetPosFt = new XYZ(SheetUtil.MmToFt(w * 0.5), SheetUtil.MmToFt(h * 0.5), 0);
                }
                else
                {
                    var loc = p["location"] as JObject;
                    if (loc == null)
                        return new { ok = false, msg = "keepLocation:false の場合、location {x,y} (mm) か centerOnSheet:true を指定してください。" };
                    targetPosFt = new XYZ(
                        SheetUtil.MmToFt(loc.Value<double>("x")),
                        SheetUtil.MmToFt(loc.Value<double>("y")),
                        0);
                }
            }

            bool copyRotation = p.Value<bool?>("copyRotation") ?? true;
            bool copyScale = p.Value<bool?>("copyScale") ?? false;

            ViewportRotation? oldRotation = null;
            if (oldViewport != null)
            {
                try { oldRotation = oldViewport.Rotation; } catch { /* ignore */ }
            }

            using (var tx = new Transaction(doc, "Replace View On Sheet"))
            {
                try
                {
                    tx.Start();

                    // 旧配置を削除
                    if (oldViewport != null)
                        doc.Delete(oldViewport.Id);
                    if (oldSchedule != null)
                        doc.Delete(oldSchedule.Id);

                    object createdInfo;

                    // 新しいビューがスケジュールか一般ビューかで分岐
                    if (newView is ViewSchedule newVs)
                    {
                        var inst = ScheduleSheetInstance.Create(doc, sheet.Id, newVs.Id, targetPosFt);
                        createdInfo = new
                        {
                            kind = "schedule",
                            scheduleInstanceId = inst.Id.IntegerValue,
                            sheetId = sheet.Id.IntegerValue,
                            viewId = newVs.Id.IntegerValue
                        };
                    }
                    else
                    {
                        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, newView.Id))
                        {
                            tx.RollBack();
                            return new { ok = false, msg = "このビューはシートに配置できません（既に配置済み/非対応ビュー等）。" };
                        }

                        var vpNew = Viewport.Create(doc, sheet.Id, newView.Id, targetPosFt);

                        if (copyRotation && oldRotation.HasValue)
                        {
                            try { vpNew.Rotation = oldRotation.Value; } catch { /* ignore */ }
                        }

                        createdInfo = new
                        {
                            kind = "viewport",
                            viewportId = vpNew.Id.IntegerValue,
                            sheetId = sheet.Id.IntegerValue,
                            viewId = newView.Id.IntegerValue
                        };
                    }

                    string scaleWarning = null;
                    if (copyScale && oldView != null && newView != null && !(newView is ViewSchedule))
                    {
                        try
                        {
                            int oldScale = oldView.Scale;
                            if (oldScale > 0 && newView.Scale != oldScale)
                            {
                                newView.Scale = oldScale;
                            }
                        }
                        catch (Exception ex)
                        {
                            scaleWarning = ex.Message;
                        }
                    }

                    tx.Commit();

                    return new
                    {
                        ok = true,
                        sheetId = sheet.Id.IntegerValue,
                        oldViewId = oldView?.Id.IntegerValue,
                        newViewId = newView.Id.IntegerValue,
                        usedLocationFromOld = keepLocation,
                        copyRotation,
                        copyScale,
                        scaleWarning,
                        result = createdInfo
                    };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = $"ビューの入れ替えに失敗: {ex.Message}" };
                }
            }
        }
    }
}
