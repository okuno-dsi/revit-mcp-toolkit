// ================================================================
// File: Core/GraphicsOverrideHelper.cs
// Revit 2023/2024 で安全に使える OverrideGraphicSettings のラッパー
// - 線色設定
// - Surface/Cut の前景・背景のソリッド塗り（Solid Fill）設定
// - 透過度設定（Surface）
// いずれも try/catch で囲み、環境差で例外になっても落とさない方針。
// ================================================================
#nullable enable
using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core
{
    public static class GraphicsOverrideHelper
    {
        /// <summary>線色（Projection/Cut）を設定</summary>
        public static void TrySetLineColors(OverrideGraphicSettings ogs, Color color)
        {
            try { ogs.SetProjectionLineColor(color); } catch { /* ignore */ }
            try { ogs.SetCutLineColor(color); } catch { /* ignore */ }
        }

        /// <summary>Surface/Cut の前景・背景に Solid Fill を適用し、色と可視を設定</summary>
        public static void TrySetAllSurfaceAndCutPatterns(Document doc, OverrideGraphicSettings ogs, Color color, bool visible)
        {
            try
            {
                var solid = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement))
                    .Cast<FillPatternElement>()
                    .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill)?.Id;

                if (solid == null || solid == ElementId.InvalidElementId)
                    return;

                // Surface
                try { ogs.SetSurfaceForegroundPatternId(solid); } catch { }
                try { ogs.SetSurfaceForegroundPatternColor(color); } catch { }
                TrySetSurfaceForegroundVisible(ogs, visible);

                try { ogs.SetSurfaceBackgroundPatternId(solid); } catch { }
                try { ogs.SetSurfaceBackgroundPatternColor(color); } catch { }
                TrySetSurfaceBackgroundVisible(ogs, visible);

                // Cut
                try { ogs.SetCutForegroundPatternId(solid); } catch { }
                try { ogs.SetCutForegroundPatternColor(color); } catch { }
                TrySetCutForegroundVisible(ogs, visible);

                try { ogs.SetCutBackgroundPatternId(solid); } catch { }
                try { ogs.SetCutBackgroundPatternColor(color); } catch { }
                TrySetCutBackgroundVisible(ogs, visible);
            }
            catch
            {
                // 収集だけ失敗しても他の設定に影響を与えない
            }
        }

        /// <summary>Surface の透過度（0=不透明, 100=完全透明）を設定</summary>
        public static void TrySetSurfaceTransparency(OverrideGraphicSettings ogs, int transparency)
        {
            try { ogs.SetSurfaceTransparency(transparency); } catch { /* ignore */ }
        }

        // Revit 2023/2024 では Visible フラグのセッターが無いバージョンもあるため try で吸収
        private static void TrySetSurfaceForegroundVisible(OverrideGraphicSettings ogs, bool visible)
        {
            try { ogs.SetSurfaceForegroundPatternVisible(visible); } catch { /* ignore */ }
        }

        private static void TrySetSurfaceBackgroundVisible(OverrideGraphicSettings ogs, bool visible)
        {
            try { ogs.SetSurfaceBackgroundPatternVisible(visible); } catch { /* ignore */ }
        }

        private static void TrySetCutForegroundVisible(OverrideGraphicSettings ogs, bool visible)
        {
            try { ogs.SetCutForegroundPatternVisible(visible); } catch { /* ignore */ }
        }

        private static void TrySetCutBackgroundVisible(OverrideGraphicSettings ogs, bool visible)
        {
            try { ogs.SetCutBackgroundPatternVisible(visible); } catch { /* ignore */ }
        }
    }
}
