using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    /// <summary>
    /// JSON-RPC: debug_section_view_sectionbox
    /// 手動で作成した ViewSection の SectionBox/Transform をダンプして、
    /// create_element_sectionbox_debug などの結果と比較するためのデバッグ用コマンド。
    /// </summary>
    public class DebugSectionViewSectionBoxCommand : IRevitCommandHandler
    {
        public string CommandName => "debug_section_view_sectionbox";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null)
            {
                return new { ok = false, msg = "アクティブドキュメントがありません。" };
            }

            ViewSection sectionView = null;

            try
            {
                // 1. 選択要素から ViewSection を探す
                var selIds = uidoc != null
                    ? uidoc.Selection.GetElementIds()
                    : new System.Collections.Generic.List<ElementId>();

                if (selIds != null && selIds.Count > 0)
                {
                    var first = selIds.First();
                    sectionView = doc.GetElement(first) as ViewSection;
                }
            }
            catch
            {
                // ignore selection errors
            }

            // 2. 選択から取得できなければ、アクティブビューを ViewSection とみなす
            if (sectionView == null)
            {
                sectionView = doc.ActiveView as ViewSection;
            }

            if (sectionView == null)
            {
                return new { ok = false, msg = "選択要素にもアクティブビューにも ViewSection が見つかりません。" };
            }

            try
            {
                var box = sectionView.CropBox;
                if (box == null)
                {
                    return new { ok = false, msg = "選択された ViewSection に CropBox がありません。" };
                }

                var t = box.Transform;

                return new
                {
                    ok = true,
                    viewId = sectionView.Id.IntValue(),
                    viewName = sectionView.Name,
                    scale = sectionView.Scale,
                    box = new
                    {
                        min = ToPointMm(box.Min),
                        max = ToPointMm(box.Max)
                    },
                    transform = new
                    {
                        origin = ToPointMm(t.Origin),
                        basisX = ToVector(t.BasisX),
                        basisY = ToVector(t.BasisY),
                        basisZ = ToVector(t.BasisZ)
                    }
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    ok = false,
                    msg = "SectionBox 情報の取得中に例外が発生しました: " + ex.GetType().Name +
                          (string.IsNullOrEmpty(ex.Message) ? "" : " - " + ex.Message)
                };
            }
        }

        private static object ToPointMm(XYZ p)
        {
            return new
            {
                x_ft = p.X,
                y_ft = p.Y,
                z_ft = p.Z,
                x_mm = UnitUtils.ConvertFromInternalUnits(p.X, UnitTypeId.Millimeters),
                y_mm = UnitUtils.ConvertFromInternalUnits(p.Y, UnitTypeId.Millimeters),
                z_mm = UnitUtils.ConvertFromInternalUnits(p.Z, UnitTypeId.Millimeters)
            };
        }

        private static object ToVector(XYZ v)
        {
            return new { x = v.X, y = v.Y, z = v.Z };
        }
    }
}


