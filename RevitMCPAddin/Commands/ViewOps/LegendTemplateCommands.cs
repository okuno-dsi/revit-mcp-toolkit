// ================================================================
// File   : Commands/ViewOps/LegendTemplateCommands.cs
// Target : .NET Framework 4.8 / Revit 2023+
// Purpose: Template-based Legend view helpers
//          - create_legend_view_from_template
// Notes  :
//  - Legend ビューは API から新規作成できないため、
//    既存 Legend ビューを Duplicate して作成する。
// ================================================================
#nullable enable
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// create_legend_view_from_template
    /// 既存の Legend ビューを複製して新しい Legend ビューを作成する。
    /// </summary>
    public class CreateLegendViewFromTemplateCommand : IRevitCommandHandler
    {
        public string CommandName => "create_legend_view_from_template";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
            var doc = uidoc != null ? uidoc.Document : null;
            if (doc == null)
            {
                return new { ok = false, msg = "アクティブドキュメントがありません。" };
            }

            var p = cmd.Params as JObject ?? new JObject();

            // Optional execution guard
            var guard = ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            string baseName = p.Value<string>("baseLegendViewName") ?? "DoorWindow_Legend_Template";
            string newNameRequested = p.Value<string>("newLegendViewName") ?? "DoorWindow_Legend_Elevations";
            bool clearContents = p.Value<bool?>("clearContents") ?? false;
            string templateName = p.Value<string>("applyViewTemplateName");

            // Base Legend View を検索
            var baseView = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v =>
                    v.ViewType == ViewType.Legend &&
                    string.Equals(v.Name ?? string.Empty, baseName, StringComparison.OrdinalIgnoreCase));

            if (baseView == null)
            {
                return new
                {
                    ok = false,
                    msg = string.Format("Base legend view '{0}' was not found.", baseName),
                    userActionHints = new[]
                    {
                        new
                        {
                            code = "create_base_legend_view",
                            description = "Create a Legend view named 'DoorWindow_Legend_Template'.",
                            steps = new[]
                            {
                                "In Revit, go to View > Legend and create a new Legend view.",
                                "Name the view 'DoorWindow_Legend_Template'.",
                                "Place at least one door legend component and one window legend component.",
                                "Save the project and run this command again."
                            }
                        }
                    }
                };
            }

            View newView = null;
            ElementId appliedTemplateId = ElementId.InvalidElementId;

            using (var tx = new Transaction(doc, "Create Legend View From Template"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                try
                {
                    // Duplicate base legend view
                    var dupId = baseView.Duplicate(ViewDuplicateOption.Duplicate);
                    newView = doc.GetElement(dupId) as View;
                    if (newView == null || newView.ViewType != ViewType.Legend)
                    {
                        throw new InvalidOperationException("Duplicated view is not a Legend view.");
                    }

                    // 名前をユニークに設定
                    string finalName = EnsureUniqueLegendName(doc, newNameRequested);
                    try
                    {
                        newView.Name = finalName;
                    }
                    catch
                    {
                        // 名前設定に失敗した場合はそのまま（Revit が自動でユニーク名を付与する）
                    }

                    // 必要なら内容をクリア
                    if (clearContents)
                    {
                        var elemIds = new FilteredElementCollector(doc, newView.Id)
                            .WhereElementIsNotElementType()
                            .ToElementIds();
                        if (elemIds.Count > 0)
                        {
                            doc.Delete(elemIds);
                        }
                    }

                    // ビューテンプレートの適用（任意）
                    if (!string.IsNullOrWhiteSpace(templateName))
                    {
                        var tmpl = new FilteredElementCollector(doc)
                            .OfClass(typeof(View))
                            .Cast<View>()
                            .FirstOrDefault(v =>
                                v.IsTemplate &&
                                string.Equals(v.Name ?? string.Empty, templateName, StringComparison.OrdinalIgnoreCase));

                        if (tmpl != null)
                        {
                            try
                            {
                                newView.ViewTemplateId = tmpl.Id;
                                try
                                {
                                    newView.ApplyViewTemplateParameters(tmpl);
                                }
                                catch
                                {
                                    // パラメータ適用に失敗しても ID 設定は維持
                                }
                                appliedTemplateId = tmpl.Id;
                            }
                            catch
                            {
                                appliedTemplateId = ElementId.InvalidElementId;
                            }
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }

                    return new
                    {
                        ok = false,
                        msg = "Legend view creation failed: " + ex.Message,
                        userActionHints = new[]
                        {
                            new
                            {
                                code = "check_model_health",
                                description = "Check the Revit model and retry.",
                                steps = new[]
                                {
                                    "Close and reopen the Revit model.",
                                    "Ensure no group or worksharing restrictions prevent view duplication.",
                                    "Run the command again."
                                }
                            }
                        }
                    };
                }
            }

            if (newView == null)
            {
                return new { ok = false, msg = "Legend view creation failed for an unknown reason." };
            }

            return new
            {
                ok = true,
                msg = string.Format("Legend view '{0}' created from '{1}'.", newView.Name, baseView.Name),
                legendViewId = newView.Id.IntValue(),
                baseLegendViewId = baseView.Id.IntValue(),
                wasCleared = clearContents,
                appliedTemplateId = appliedTemplateId != ElementId.InvalidElementId
                    ? (int?)appliedTemplateId.IntValue()
                    : null
            };
        }

        private static string EnsureUniqueLegendName(Document doc, string requestedName)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName)
                ? "Legend"
                : requestedName.Trim();

            Func<string, bool> nameExists = n =>
                new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Any(v =>
                        v.ViewType == ViewType.Legend &&
                        string.Equals(v.Name ?? string.Empty, n, StringComparison.OrdinalIgnoreCase));

            if (!nameExists(baseName)) return baseName;

            int nSuffix = 2;
            while (nSuffix < 1000)
            {
                string candidate = baseName + " (" + nSuffix.ToString() + ")";
                if (!nameExists(candidate)) return candidate;
                nSuffix++;
            }

            return baseName;
        }
    }

    /// <summary>
    /// copy_legend_components_between_views
    /// ベース凡例ビューにある凡例コンポーネントを、別の凡例ビューに一括コピーするだけの軽量コマンド。
    /// タイプ変更やグリッド配置は行わない。
    /// </summary>
    public class CopyLegendComponentsBetweenViewsCommand : IRevitCommandHandler
    {
        public string CommandName => "copy_legend_components_between_views";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
            var doc = uidoc != null ? uidoc.Document : null;
            if (doc == null)
            {
                return new { ok = false, msg = "アクティブドキュメントがありません。" };
            }

            var p = cmd.Params as JObject ?? new JObject();

            // Optional execution guard
            var guard = ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            string sourceName =
                p.Value<string>("sourceLegendViewName") ??
                p.Value<string>("baseLegendViewName") ??
                "DoorWindow_Legend_Template";

            string targetName =
                p.Value<string>("targetLegendViewName") ??
                "DoorWindow_Legend_Elevations";

            bool clearTarget = p.Value<bool?>("clearTargetContents") ?? false;

            var sourceView = FindLegendViewByName(doc, sourceName);
            if (sourceView == null)
            {
                return new
                {
                    ok = false,
                    msg = string.Format("Source legend view '{0}' was not found.", sourceName)
                };
            }

            var targetView = FindLegendViewByName(doc, targetName);
            if (targetView == null)
            {
                return new
                {
                    ok = false,
                    msg = string.Format("Target legend view '{0}' was not found.", targetName)
                };
            }

            int copiedCount = 0;

            using (var tx = new Transaction(doc, "Copy Legend Components Between Views"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                try
                {
                    // ターゲット内容のクリア（任意）
                    if (clearTarget)
                    {
                        var tgtIds = new FilteredElementCollector(doc, targetView.Id)
                            .WhereElementIsNotElementType()
                            .OfCategory(BuiltInCategory.OST_LegendComponents)
                            .ToElementIds();
                        if (tgtIds.Count > 0)
                        {
                            doc.Delete(tgtIds);
                        }
                    }

                    // ソース凡例の LegendComponents をすべてコピー
                    var srcIds = new FilteredElementCollector(doc, sourceView.Id)
                        .WhereElementIsNotElementType()
                        .OfCategory(BuiltInCategory.OST_LegendComponents)
                        .ToElementIds();

                    if (srcIds.Count == 0)
                    {
                        tx.Commit();
                        return new
                        {
                            ok = false,
                            msg = string.Format("No legend components found in source legend view '{0}'.", sourceName),
                            sourceLegendViewId = sourceView.Id.IntValue(),
                            targetLegendViewId = targetView.Id.IntValue(),
                            copiedCount = 0
                        };
                    }

                    var opts = new CopyPasteOptions();
                    var copiedIds = ElementTransformUtils.CopyElements(
                        sourceView,
                        srcIds,
                        targetView,
                        Transform.Identity,
                        opts);

                    copiedCount = copiedIds != null ? copiedIds.Count : 0;

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }

                    return new
                    {
                        ok = false,
                        msg = "copy_legend_components_between_views failed: " + ex.Message
                    };
                }
            }

            return new
            {
                ok = true,
                msg = string.Format("Copied {0} legend component(s) from '{1}' to '{2}'.",
                    copiedCount, sourceName, targetName),
                sourceLegendViewId = sourceView.Id.IntValue(),
                targetLegendViewId = targetView.Id.IntValue(),
                copiedCount = copiedCount
            };
        }

        private static View FindLegendViewByName(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(v =>
                    v.ViewType == ViewType.Legend &&
                    string.Equals(v.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// layout_legend_components_in_view
    /// 指定ビュー内の凡例コンポーネントをグリッド状に並べ替える。
    /// コピーやタイプ変更は行わず、既存要素の位置調整のみ行う。
    /// </summary>
    public class LayoutLegendComponentsInViewCommand : IRevitCommandHandler
    {
        public string CommandName => "layout_legend_components_in_view";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp != null ? uiapp.ActiveUIDocument : null;
            var doc = uidoc != null ? uidoc.Document : null;
            if (doc == null)
            {
                return new { ok = false, msg = "アクティブドキュメントがありません。" };
            }

            var p = cmd.Params as JObject ?? new JObject();

            // Optional execution guard
            var guard = ExpectedContextGuard.Validate(uiapp, p);
            if (guard != null) return guard;

            // viewId / viewName / legendViewName を許容
            int viewIdInt = p.Value<int?>("viewId") ?? 0;
            string viewName =
                p.Value<string>("viewName") ??
                p.Value<string>("legendViewName") ??
                string.Empty;

            View view = null;
            if (viewIdInt > 0)
            {
                view = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewIdInt)) as View;
            }
            if (view == null && !string.IsNullOrWhiteSpace(viewName))
            {
                view = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .FirstOrDefault(v =>
                        v.ViewType == ViewType.Legend &&
                        string.Equals(v.Name ?? string.Empty, viewName, StringComparison.OrdinalIgnoreCase));
            }
            if (view == null)
            {
                return new
                {
                    ok = false,
                    msg = "Legend ビューが見つかりません (viewId / viewName)。"
                };
            }

            // レイアウトパラメータ
            var layout = p["layout"] as JObject ?? new JObject();
            int maxColumns = layout.Value<int?>("maxColumns") ?? 4;
            double cellWidthMm = layout.Value<double?>("cellWidth_mm") ?? 2500.0;
            double cellHeightMm = layout.Value<double?>("cellHeight_mm") ?? 2500.0;

            var startPoint = layout["startPoint"] as JObject ?? new JObject();
            double startXmm = startPoint.Value<double?>("x_mm") ?? 0.0;
            double startYmm = startPoint.Value<double?>("y_mm") ?? 0.0;

            if (maxColumns <= 0) maxColumns = 1;

            // 単位変換
            double cellWidthFt = UnitUtils.ConvertToInternalUnits(cellWidthMm, UnitTypeId.Millimeters);
            double cellHeightFt = UnitUtils.ConvertToInternalUnits(cellHeightMm, UnitTypeId.Millimeters);
            double startXft = UnitUtils.ConvertToInternalUnits(startXmm, UnitTypeId.Millimeters);
            double startYft = UnitUtils.ConvertToInternalUnits(startYmm, UnitTypeId.Millimeters);

            // 対象凡例コンポーネント
            var elems = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .OfCategory(BuiltInCategory.OST_LegendComponents)
                .ToElements()
                .OrderBy(e => e.Id.IntValue())
                .ToList();

            if (elems.Count == 0)
            {
                return new
                {
                    ok = false,
                    msg = "指定ビュー内に凡例コンポーネントがありません。",
                    viewId = view.Id.IntValue()
                };
            }

            int moved = 0;

            using (var tx = new Transaction(doc, "Layout Legend Components In View"))
            {
                tx.Start();
                TxnUtil.ConfigureProceedWithWarnings(tx);

                try
                {
                    for (int i = 0; i < elems.Count; i++)
                    {
                        var e = elems[i];

                        int row = i / maxColumns;
                        int col = i % maxColumns;

                        double x = startXft + col * cellWidthFt;
                        double y = startYft - row * cellHeightFt;
                        var targetPt = new XYZ(x, y, 0);

                        // 現在位置の中心
                        var center = GetElementCenter(e, view);
                        var delta = targetPt - center;

                        try
                        {
                            ElementTransformUtils.MoveElement(doc, e.Id, delta);
                            moved++;
                        }
                        catch
                        {
                            // 単一要素の失敗は無視して続行
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }

                    return new
                    {
                        ok = false,
                        msg = "layout_legend_components_in_view failed: " + ex.Message
                    };
                }
            }

            return new
            {
                ok = true,
                viewId = view.Id.IntValue(),
                movedCount = moved,
                msg = string.Format("Repositioned {0} legend component(s) in view '{1}'.", moved, view.Name)
            };
        }

        private static XYZ GetElementCenter(Element e, View view)
        {
            try
            {
                var loc = e.Location as LocationPoint;
                if (loc != null) return loc.Point;
            }
            catch
            {
                // ignore
            }

            try
            {
                var bb = e.get_BoundingBox(view);
                if (bb != null)
                {
                    return (bb.Min + bb.Max) * 0.5;
                }
            }
            catch
            {
                // ignore
            }

            return new XYZ(0, 0, 0);
        }
    }
}


