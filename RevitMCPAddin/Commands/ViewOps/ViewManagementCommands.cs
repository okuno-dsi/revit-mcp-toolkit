// ================================================================
// File: Commands/ViewOps/ViewManagementCommands.cs
// Purpose : View Delete / View Template Assign-Clear / Save as Template / Rename Template
//           View Type Change / View Parameter Set (Spec-aware) / Get View Parameters (Spec-aware)
// Target  : .NET Framework 4.8 / Revit 2023+
// Depends : Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq
//           RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand)
// Notes   :
//  - save_view_as_template は Revit 2023 で "AsTemplate" が無いため
//    Duplicate(WithDetailing) → ConvertToTemplate()/IsTemplate(set) の反射で対応
//  - set_view_parameter は SpecType による mm/deg 変換を簡易実装
//  - get_view_parameters は Spec/StorageType を明示し、mm/deg 等の人間向け値も併記
//  - すべてのコマンドは { ok, ... } を返し、失敗時は理由(msg/ reason)を含める
// ================================================================

#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.RevitUI;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ViewOps
{
    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------
    internal static class ViewUtil
    {
        public static View ResolveView(Document doc, JObject p)
        {
            int vid = p.Value<int?>("viewId") ?? 0;
            string vuid = p.Value<string>("uniqueId");
            if (vid > 0) return doc.GetElement(new ElementId(vid)) as View;
            if (!string.IsNullOrWhiteSpace(vuid)) return doc.GetElement(vuid) as View;
            return null;
        }

        public static View ResolveTemplate(Document doc, JObject p)
        {
            int tid = p.Value<int?>("templateViewId") ?? 0;
            string tname = p.Value<string>("templateName");
            if (tid > 0)
            {
                var v = doc.GetElement(new ElementId(tid)) as View;
                if (v != null && v.IsTemplate) return v;
            }
            if (!string.IsNullOrWhiteSpace(tname))
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(View)).Cast<View>()
                    .FirstOrDefault(v => v.IsTemplate && string.Equals(v.Name ?? "", tname, StringComparison.OrdinalIgnoreCase));
            }
            return null;
        }

        public static ViewFamilyType ResolveViewFamilyType(Document doc, JObject p)
        {
            int id = p.Value<int?>("newViewTypeId") ?? p.Value<int?>("viewTypeId") ?? 0;
            string name = p.Value<string>("newViewTypeName") ?? p.Value<string>("viewTypeName");
            if (id > 0)
            {
                var t = doc.GetElement(new ElementId(id)) as ViewFamilyType;
                if (t != null) return t;
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => string.Equals(vft.Name ?? "", name, StringComparison.OrdinalIgnoreCase));
            }
            return null;
        }

        // ---- Spec-aware conversions (Double only) ----
        public static double ToInternalBySpec(double v, ForgeTypeId spec)
        {
            try
            {
                if (spec != null)
                {
                    if (spec.Equals(SpecTypeId.Length)) return ConvertToInternalUnits(v, UnitTypeId.Millimeters);
                    if (spec.Equals(SpecTypeId.Area)) return ConvertToInternalUnits(v, UnitTypeId.SquareMillimeters);
                    if (spec.Equals(SpecTypeId.Volume)) return ConvertToInternalUnits(v, UnitTypeId.CubicMillimeters);
                    if (spec.Equals(SpecTypeId.Angle)) return v * (Math.PI / 180.0);
                }
            }
            catch { /* fallback raw */ }
            return v; // 未知Specは生値
        }

        public static double FromInternalBySpec(double internalVal, ForgeTypeId spec)
        {
            try
            {
                if (spec != null)
                {
                    if (spec.Equals(SpecTypeId.Length)) return ConvertFromInternalUnits(internalVal, UnitTypeId.Millimeters);
                    if (spec.Equals(SpecTypeId.Area)) return ConvertFromInternalUnits(internalVal, UnitTypeId.SquareMillimeters);
                    if (spec.Equals(SpecTypeId.Volume)) return ConvertFromInternalUnits(internalVal, UnitTypeId.CubicMillimeters);
                    if (spec.Equals(SpecTypeId.Angle)) return internalVal * (180.0 / Math.PI);
                }
            }
            catch { /* fallback raw */ }
            return internalVal; // 未知Specはそのまま
        }

        public static bool TryConvertToTemplate(View v)
        {
            if (v == null) return false;
            try
            {
                // ConvertToTemplate() があれば呼ぶ
                var mi = typeof(View).GetMethod("ConvertToTemplate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    mi.Invoke(v, null);
                    return v.IsTemplate;
                }

                // IsTemplate セッターがあれば true を代入
                var prop = typeof(View).GetProperty("IsTemplate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(v, true, null);
                    return v.IsTemplate;
                }
            }
            catch { }
            return v.IsTemplate;
        }

        internal static JObject BuildParamInfo(Document doc, Element owner, Parameter pr, bool includeEmpty)
        {
            var o = new JObject();
            string name = pr.Definition != null ? pr.Definition.Name ?? "" : pr.AsValueString(); // fallback

            o["name"] = name;
            o["storageType"] = pr.StorageType.ToString();
            o["isReadOnly"] = pr.IsReadOnly;

            // spec/unit hint
            ForgeTypeId spec = null;
            try { spec = pr.Definition != null ? pr.Definition.GetDataType() : null; } catch { spec = null; }
            o["spec"] = spec != null ? spec.TypeId : null; // e.g., "autodesk.spec.aec:length-1.0.0"

            // shared param GUID if any
            try
            {
                Guid guid;
                if (pr.IsShared && pr.GUID != Guid.Empty)
                {
                    guid = pr.GUID;
                    o["isShared"] = true;
                    o["guid"] = guid.ToString("D");
                }
                else
                {
                    o["isShared"] = false;
                }
            }
            catch { o["isShared"] = false; }

            // values
            try
            {
                switch (pr.StorageType)
                {
                    case StorageType.Double:
                        {
                            double ival = pr.AsDouble(); // internal (ft, ft^2, ft^3, rad)
                            bool hasVal = !double.IsNaN(ival) && !double.IsInfinity(ival);
                            o["hasValue"] = hasVal;
                            if (hasVal || includeEmpty)
                            {
                                o["internal"] = ival;
                                double disp = FromInternalBySpec(ival, spec);
                                o["value"] = disp;
                                // unit hint
                                if (spec != null)
                                {
                                    if (spec.Equals(SpecTypeId.Length)) o["unit"] = "mm";
                                    else if (spec.Equals(SpecTypeId.Area)) o["unit"] = "mm2";
                                    else if (spec.Equals(SpecTypeId.Volume)) o["unit"] = "mm3";
                                    else if (spec.Equals(SpecTypeId.Angle)) o["unit"] = "deg";
                                    else o["unit"] = "raw";
                                }
                                else o["unit"] = "raw";
                                // formatted
                                try { o["display"] = pr.AsValueString(); } catch { /* ignore */ }
                            }
                            break;
                        }
                    case StorageType.Integer:
                        {
                            // bool or int?
                            int ival = pr.AsInteger();
                            o["hasValue"] = true; // Revit int はだいたい 0/1 を含め必ず取れる
                            // Attempt to detect YesNo
                            bool? asBool = null;
                            try
                            {
                                // 仕様的に完全には判定できないが、ParameterType.YesNo 相当は 0/1
                                if (ival == 0 || ival == 1) asBool = (ival != 0);
                            }
                            catch { }
                            if (asBool.HasValue) o["value"] = asBool.Value;
                            else o["value"] = ival;
                            o["unit"] = null;
                            try { o["display"] = pr.AsValueString(); } catch { }
                            break;
                        }
                    case StorageType.String:
                        {
                            string sval = pr.AsString();
                            bool hasVal = !string.IsNullOrEmpty(sval);
                            o["hasValue"] = hasVal;
                            if (hasVal || includeEmpty)
                            {
                                o["value"] = sval ?? "";
                                o["display"] = sval ?? "";
                            }
                            o["unit"] = null;
                            break;
                        }
                    case StorageType.ElementId:
                        {
                            var id = pr.AsElementId();
                            bool hasVal = id != null && id != ElementId.InvalidElementId;
                            o["hasValue"] = hasVal;
                            if (hasVal || includeEmpty)
                            {
                                o["value"] = id.IntegerValue;
                                string refName = null;
                                try
                                {
                                    var refElem = (id != null && id != ElementId.InvalidElementId) ? doc.GetElement(id) : null;
                                    if (refElem != null)
                                    {
                                        try { refName = refElem.Name; } catch { refName = null; }
                                    }
                                }
                                catch { /* ignore */ }
                                if (!string.IsNullOrEmpty(refName)) o["refName"] = refName;
                            }
                            o["unit"] = null;
                            try { o["display"] = pr.AsValueString(); } catch { }
                            break;
                        }
                    default:
                        o["hasValue"] = false;
                        o["value"] = null;
                        o["unit"] = null;
                        break;
                }
            }
            catch
            {
                o["hasValue"] = false;
                o["value"] = null;
            }

            return o;
        }
    }

    // ------------------------------------------------------------
    // 1) delete_view : ビュー削除
    // ------------------------------------------------------------
    public class DeleteViewCommand : IRevitCommandHandler
    {
        public string CommandName => "delete_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var uidoc = uiapp!.ActiveUIDocument;
            var p = (JObject)(cmd.Params ?? new JObject());

            // Optional execution guard (PID/ProjectGuid/View)
            var guard = RevitMCPAddin.Core.ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            // 追加オプション
            bool fast = p.Value<bool?>("fast") ?? true;                 // 既定: 高速モード
            bool refreshView = p.Value<bool?>("refreshView") ?? false;  // 既定: 手動でのみリフレッシュ
            bool closeOpenUiViews = p.Value<bool?>("closeOpenUiViews") ?? true;
            bool useTempDraftingView = p.Value<bool?>("useTempDraftingView") ?? true;

            var view = ViewUtil.ResolveView(doc, p);
            if (view == null) return new { ok = false, msg = "View not found (viewId/uniqueId)." };
            if (view.IsTemplate) return new { ok = false, msg = "テンプレートビューは delete_view では削除できません。" };

            var targetId = view.Id;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // 1) アクティブ/オープン対応を先に処理
            try
            {
                // 開いている UIView は閉じる（描画コスト削減）
                if (closeOpenUiViews && uidoc != null)
                {
                    foreach (var uiv in uidoc.GetOpenUIViews().ToList())
                    {
                        if (uiv?.ViewId == targetId) { try { uiv.Close(); } catch { /* ignore */ } }
                    }
                }

                // アクティブなら一時ドラフティングビューに退避（最軽量）
                if (uidoc != null && doc.ActiveView?.Id == targetId && useTempDraftingView)
                {
                    View temp = null;
                    using (var tx = new Transaction(doc, "Create Temp Drafting View (for delete)"))
                    {
                        tx.Start();
                        try
                        {
                            var vft = new FilteredElementCollector(doc)
                                .OfClass(typeof(ViewFamilyType))
                                .Cast<ViewFamilyType>()
                                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);

                            if (vft != null)
                            {
                                // ViewDrafting.Create は ElementId ではなく ViewDrafting を返す
                                ViewDrafting vd = ViewDrafting.Create(doc, vft.Id);
                                temp = vd as View;                 // もしくは: temp = (View)vd;

                                if (temp != null)
                                {
                                    try { temp.Name = "_tmp_delete_buffer"; } catch { /* ignore */ }
                                }
                            }

                            tx.Commit();
                        }
                        catch
                        {
                            tx.RollBack();
                        }
                    }

                    if (temp != null)
                    {
                        // 可能なら UI 切替
                        try { RevitMCPAddin.RevitUI.UiHelpers.TryRequestViewChange(uidoc, temp); } catch { /* ignore */ }

                        // 最長 1 秒だけ同期待ち（軽め）
                        var w = System.Diagnostics.Stopwatch.StartNew();
                        while (w.ElapsedMilliseconds < 1000)
                        {
                            if (doc.ActiveView?.Id == temp.Id) break;
                            System.Threading.Thread.Sleep(50);
                        }
                    }
                }
            }
            catch { /* ignore */ }

            // 2) 削除（依存は Revit に一括で任せる）
            int deletedCount = 0;
            List<int> deletedIds = new List<int>();
            using (var tx = new Transaction(doc, "Delete View"))
            {
                tx.Start();

                // 失敗ダイアログ抑止
                try
                {
                    var fho = tx.GetFailureHandlingOptions();
                    try { fho.SetFailuresPreprocessor(new SuppressWarningsPreprocessor()); } catch { }
                    try { tx.SetFailureHandlingOptions(fho); } catch { }
                }
                catch { }

                ICollection<ElementId> deleted = null;
                try { deleted = doc.Delete(targetId); }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "削除に失敗: " + ex.Message };
                }

                tx.Commit();

                if (deleted != null)
                {
                    deletedCount = deleted.Count;
                    foreach (var id in deleted) deletedIds.Add(id.IntegerValue);
                }
            }

            // 3) 後始末：一時ドラフティングビューは削除（高速モードでは UI リフレッシュしない）
            if (useTempDraftingView)
            {
                try
                {
                    var tmp = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, "_tmp_delete_buffer", StringComparison.OrdinalIgnoreCase));
                    if (tmp != null && tmp.Id != targetId)
                    {
                        using (var tx = new Transaction(doc, "Cleanup Temp Drafting View"))
                        {
                            tx.Start();
                            try { doc.Delete(tmp.Id); } catch { }
                            tx.Commit();
                        }
                    }
                }
                catch { /* ignore */ }
            }

            // 4) 仕上げ（必要時のみ）
            if (!fast)
            {
                try { doc.Regenerate(); } catch { }
                if (refreshView)
                {
                    try { uidoc?.RefreshActiveView(); } catch { }
                }
            }

            sw.Stop();
            return new
            {
                ok = true,
                viewId = targetId.IntegerValue,
                deletedCount,
                deletedElementIds = deletedIds,
                elapsedMs = sw.ElapsedMilliseconds,
                fast,
                uiClosed = closeOpenUiViews,
                usedTempDraftingView = useTempDraftingView
            };
        }
    }

    // ------------------------------------------------------------
    // 2) set_view_template : テンプレ割当/解除
    // ------------------------------------------------------------
    public class SetViewTemplateCommand : IRevitCommandHandler
    {
        public string CommandName => "set_view_template";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            var view = ViewUtil.ResolveView(doc, p);
            if (view == null) return new { ok = false, msg = "View not found (viewId/uniqueId)." };

            bool clear = p.Value<bool?>("clear") ?? false;
            View tmpl = null;
            if (!clear)
            {
                tmpl = ViewUtil.ResolveTemplate(doc, p);
                if (tmpl == null) return new { ok = false, msg = "Template not found (templateViewId/templateName)." };
            }

            using (var tx = new Transaction(doc, "Set View Template"))
            {
                tx.Start();
                if (clear)
                {
                    view.ViewTemplateId = ElementId.InvalidElementId;
                }
                else
                {
                    // UI と同様に「テンプレート適用時にビュー設定を上書き」するため、
                    // ViewTemplateId を設定したうえで ApplyViewTemplateParameters を呼び出す。
                    view.ViewTemplateId = tmpl.Id;
                    try
                    {
                        view.ApplyViewTemplateParameters(tmpl);
                    }
                    catch (Exception ex)
                    {
                        // テンプレート種別不一致などで失敗した場合は、ID の割当だけは残しつつ理由を返す
                        tx.RollBack();
                        return new
                        {
                            ok = false,
                            msg = "ビュー テンプレートの適用に失敗しました: " + ex.Message,
                            viewId = view.Id.IntegerValue,
                            templateViewId = tmpl.Id.IntegerValue
                        };
                    }
                }
                tx.Commit();
            }

            // Regenerate + ActiveView refresh (UI を確実に更新)
            try { doc.Regenerate(); } catch { /* ignore */ }
            bool refreshed = false;
            try
            {
                if (uidoc != null)
                {
                    var active = (uidoc.ActiveGraphicalView as View) ?? uidoc.ActiveView;
                    if (active != null && active.Id == view.Id)
                    {
                        uidoc.RefreshActiveView();
                        refreshed = true;
                    }
                }
            }
            catch
            {
                // RefreshActiveView が失敗してもコマンド自体は成功扱いにする
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntegerValue,
                templateApplied = view.ViewTemplateId != ElementId.InvalidElementId,
                templateViewId = view.ViewTemplateId.IntegerValue,
                refreshedActiveView = refreshed
            };
        }
    }

    // ------------------------------------------------------------
    // 3) save_view_as_template : ビューの状態からテンプレートを作成
    //    Duplicate(WithDetailing) → ConvertToTemplate()/IsTemplate=true
    // ------------------------------------------------------------
    public class SaveViewAsTemplateCommand : IRevitCommandHandler
    {
        public string CommandName => "save_view_as_template";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            var src = ViewUtil.ResolveView(doc, p);
            if (src == null) return new { ok = false, msg = "View not found (viewId/uniqueId)." };

            string newName = p.Value<string>("newTemplateName") ?? (src.Name + " - Template");
            using (var tx = new Transaction(doc, "Save View As Template"))
            {
                tx.Start();

                var dupId = src.Duplicate(ViewDuplicateOption.WithDetailing);
                var dup = doc.GetElement(dupId) as View;
                if (dup == null)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "ビューの複製に失敗しました。" };
                }

                if (!ViewUtil.TryConvertToTemplate(dup))
                {
                    tx.RollBack();
                    return new { ok = false, msg = "このRevitバージョンではプログラムからのテンプレート化に対応していません。" };
                }

                try { dup.Name = newName; } catch { /* 重複時はRevitが自動サフィックス */ }
                tx.Commit();

                return new { ok = true, templateViewId = dup.Id.IntegerValue, templateName = dup.Name };
            }
        }
    }

    // ------------------------------------------------------------
    // 4) rename_view_template : テンプレート名変更
    // ------------------------------------------------------------
    public class RenameViewTemplateCommand : IRevitCommandHandler
    {
        public string CommandName => "rename_view_template";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            var tmpl = ViewUtil.ResolveTemplate(doc, p);
            if (tmpl == null) return new { ok = false, msg = "Template not found (templateViewId/templateName)." };

            string newName = p.Value<string>("newName");
            if (string.IsNullOrWhiteSpace(newName)) return new { ok = false, msg = "newName が必要です。" };

            using (var tx = new Transaction(doc, "Rename View Template"))
            {
                tx.Start();
                try { tmpl.Name = newName; } catch { /* 重複時は自動サフィックス */ }
                tx.Commit();
            }
            return new { ok = true, templateViewId = tmpl.Id.IntegerValue, templateName = tmpl.Name };
        }
    }

    // ------------------------------------------------------------
    // 5) set_view_type : ViewFamilyType 変更（可能なビューのみ）
    // ------------------------------------------------------------
    public class SetViewTypeCommand : IRevitCommandHandler
    {
        public string CommandName => "set_view_type";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            var view = ViewUtil.ResolveView(doc, p);
            if (view == null) return new { ok = false, msg = "View not found (viewId/uniqueId)." };

            var targetType = ViewUtil.ResolveViewFamilyType(doc, p);
            if (targetType == null) return new { ok = false, msg = "ViewFamilyType not found (newViewTypeId/newViewTypeName)." };

            var oldType = view.GetTypeId();
            using (var tx = new Transaction(doc, "Change View Type"))
            {
                tx.Start();
                try
                {
                    view.ChangeTypeId(targetType.Id);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "ビュータイプ変更に失敗: " + ex.Message };
                }
                tx.Commit();
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntegerValue,
                oldViewTypeId = oldType.IntegerValue,
                newViewTypeId = targetType.Id.IntegerValue,
                newViewTypeName = targetType.Name
            };
        }
    }

    // ------------------------------------------------------------
    // 6) set_view_parameter : ビューパラメータ更新（Spec-aware）
    // ------------------------------------------------------------
    public class SetViewParameterCommand : IRevitCommandHandler
    {
        public string CommandName => "set_view_parameter";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            var view = ViewUtil.ResolveView(doc, p);
            if (view == null) return new { ok = false, msg = "View not found (viewId/uniqueId)." };

            string paramName = p.Value<string>("paramName");
            if (string.IsNullOrWhiteSpace(paramName) && p["builtInName"] == null && p["builtInId"] == null && p["guid"] == null)
                return new { ok = false, msg = "paramName または builtInName/builtInId/guid のいずれかが必要です。" };
            if (!p.TryGetValue("value", out var vtok)) return new { ok = false, msg = "value が必要です。" };
            // Optional: detach template to avoid template-locked params
            bool detachTemplate = p.Value<bool?>("detachViewTemplate") ?? false;
            if (detachTemplate && view.ViewTemplateId != ElementId.InvalidElementId)
            {
                using (var tx0 = new Transaction(doc, "Detach View Template (set_view_parameter)"))
                {
                    try { tx0.Start(); view.ViewTemplateId = ElementId.InvalidElementId; TxnUtil.ConfigureProceedWithWarnings(tx0); tx0.Commit(); } catch { try { tx0.RollBack(); } catch { } }
                }
            }

            var pr = ParamResolver.ResolveByPayload(view, p, out var resolvedBy);
            if (pr == null) return new { ok = false, msg = "Parameter not found (name/builtIn/guid)" };
            if (pr.IsReadOnly) return new { ok = false, msg = "Parameter '" + (pr.Definition?.Name ?? paramName) + "' は読み取り専用です。" };

            using (var tx = new Transaction(doc, "Set View Parameter"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);
                try
                {
                    switch (pr.StorageType)
                    {
                        case StorageType.Double:
                            {
                                ForgeTypeId spec = null;
                                try { spec = pr.Definition != null ? pr.Definition.GetDataType() : null; } catch { spec = null; }
                                double inVal = vtok.Value<double>();
                                double internalVal = ViewUtil.ToInternalBySpec(inVal, spec);
                                pr.Set(internalVal);
                                break;
                            }
                        case StorageType.Integer:
                            pr.Set(vtok.Type == JTokenType.Boolean ? (vtok.Value<bool>() ? 1 : 0) : vtok.Value<int>());
                            break;
                        case StorageType.String:
                            pr.Set(vtok.Type == JTokenType.Null ? string.Empty : (vtok.Value<string>() ?? string.Empty));
                            break;
                        case StorageType.ElementId:
                            pr.Set(new ElementId(vtok.Value<int>()));
                            break;
                        default:
                            tx.RollBack();
                            return new { ok = false, msg = "Unsupported StorageType: " + pr.StorageType };
                    }
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return new { ok = false, msg = "パラメータ設定に失敗: " + ex.Message };
                }
                tx.Commit();
            }

            // 返却は入力値をそのまま返す（確認用）
            return new { ok = true, viewId = view.Id.IntegerValue, paramName = paramName, value = vtok };
        }
    }

    // ------------------------------------------------------------
    // 7) get_view_parameters : ビュー（＋任意でビュータイプ）のパラメータ取得（Spec-aware）
    // ------------------------------------------------------------
    public class GetViewParametersCommand : IRevitCommandHandler
    {
        public string CommandName => "get_view_parameters";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };
            var p = (JObject)cmd.Params ?? new JObject();

            var view = ViewUtil.ResolveView(doc, p);
            if (view == null) return new { ok = false, msg = "View not found (viewId/uniqueId)." };

            // フィルタ: names (大文字小文字無視)
            HashSet<string> nameFilter = null;
            if (p.TryGetValue("names", out var namesTok) && namesTok is JArray)
            {
                nameFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in (JArray)namesTok)
                {
                    var s = (t.Type == JTokenType.String) ? (string)t : null;
                    if (!string.IsNullOrWhiteSpace(s)) nameFilter.Add(s.Trim());
                }
            }

            bool includeTypeParams = p.Value<bool?>("includeTypeParams") ?? false;
            bool includeEmpty = p.Value<bool?>("includeEmpty") ?? true;

            // インスタンス（ビュー自身）
            var instList = new List<JObject>();
            try
            {
                var it = view.Parameters; // ParameterSet
                foreach (Parameter pr in it)
                {
                    // パラメータ名でフィルタ
                    string nm = pr.Definition != null ? pr.Definition.Name ?? "" : null;
                    if (nameFilter != null && !string.IsNullOrEmpty(nm) && !nameFilter.Contains(nm)) continue;

                    var info = ViewUtil.BuildParamInfo(doc, view, pr, includeEmpty);
                    // includeEmpty=false の場合、hasValue=true のみ返す
                    if (includeEmpty || (info["hasValue"] != null && info.Value<bool>("hasValue")))
                        instList.Add(info);
                }
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = "インスタンスパラメータ取得に失敗: " + ex.Message };
            }

            // タイプ（ViewFamilyType）— 任意
            JArray typeParams = null;
            int? viewTypeId = null;
            string viewTypeName = null;
            if (includeTypeParams)
            {
                try
                {
                    var typeElem = doc.GetElement(view.GetTypeId()) as ViewFamilyType;
                    if (typeElem != null)
                    {
                        viewTypeId = typeElem.Id.IntegerValue;
                        viewTypeName = typeElem.Name;
                        var list = new List<JObject>();
                        foreach (Parameter pr in typeElem.Parameters)
                        {
                            string nm = pr.Definition != null ? pr.Definition.Name ?? "" : null;
                            if (nameFilter != null && !string.IsNullOrEmpty(nm) && !nameFilter.Contains(nm)) continue;

                            var info = ViewUtil.BuildParamInfo(doc, typeElem, pr, includeEmpty);
                            if (includeEmpty || (info["hasValue"] != null && info.Value<bool>("hasValue")))
                                list.Add(info);
                        }
                        typeParams = new JArray(list);
                    }
                }
                catch (Exception ex)
                {
                    return new { ok = false, msg = "タイプパラメータ取得に失敗: " + ex.Message };
                }
            }

            var result = new JObject
            {
                ["ok"] = true,
                ["viewId"] = view.Id.IntegerValue,
                ["viewName"] = view.Name ?? "",
                ["parameters"] = new JArray(instList)
            };
            if (includeTypeParams)
            {
                result["typeParameters"] = typeParams ?? new JArray();
                if (viewTypeId.HasValue) result["viewTypeId"] = viewTypeId.Value;
                if (!string.IsNullOrEmpty(viewTypeName)) result["viewTypeName"] = viewTypeName;
            }
            return result;
        }
    }

    // ------------------------------------------------------------
    // 8) clear_view_template : ビューテンプレートを解除（テンプレートなしにする）
    // ------------------------------------------------------------
    public class ClearViewTemplateCommand : IRevitCommandHandler
    {
        public string CommandName => "clear_view_template";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null)
                return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            var view = ViewUtil.ResolveView(doc, p);
            if (view == null)
                return new { ok = false, msg = "View not found (viewId/uniqueId)." };

            if (view.ViewTemplateId == ElementId.InvalidElementId)
                return new { ok = true, msg = "すでにテンプレートは設定されていません。", viewId = view.Id.IntegerValue };

            using (var tx = new Transaction(doc, "Clear View Template"))
            {
                tx.Start();
                view.ViewTemplateId = ElementId.InvalidElementId;
                tx.Commit();
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntegerValue,
                templateCleared = true
            };
        }
    }

}


