// ============================================================================
// File   : Core/Common/ViewScopeUtil.cs
// Purpose: ビューの実スコープ（Crop/Section 等）取得と AABB 交差判定
// ============================================================================
#nullable disable
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core.Common
{
    public static class ViewScopeUtil
    {
        public static BoundingBoxXYZ TryGetViewScopeBox(View view)
        {
            try
            {
                if (view != null && view.CropBoxActive)
                {
                    var bb = view.CropBox;
                    if (bb != null) return bb;
                }
            }
            catch (System.Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"TryGetViewScopeBox failed: {ex.Message}");
            }
            return null; // 無制限扱い
        }

        public static bool IntersectsViewScope(Element e, View view, BoundingBoxXYZ viewBox)
        {
            if (e == null) return false;
            if (viewBox == null) return true; // 制限なし

            BoundingBoxXYZ ebb = null;
            try { ebb = e.get_BoundingBox(view) ?? e.get_BoundingBox(null); }
            catch (System.Exception ex)
            {
                RevitMCPAddin.Core.RevitLogger.Warn($"IntersectsViewScope: get_BoundingBox failed: {ex.Message}");
            }
            if (ebb == null) return false;

            var a = ToWorldAabb(ebb);
            var b = ToWorldAabb(viewBox);
            return AabbIntersects(a.min, a.max, b.min, b.max);
        }

        private struct Aabb { public XYZ min; public XYZ max; }

        private static Aabb ToWorldAabb(BoundingBoxXYZ bb)
        {
            var tf = bb.Transform ?? Transform.Identity;
            XYZ[] pts = new XYZ[8];
            pts[0] = tf.OfPoint(new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z));
            pts[1] = tf.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z));
            pts[2] = tf.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z));
            pts[3] = tf.OfPoint(new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z));
            pts[4] = tf.OfPoint(new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z));
            pts[5] = tf.OfPoint(new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z));
            pts[6] = tf.OfPoint(new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z));
            pts[7] = tf.OfPoint(new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z));

            double minX = pts[0].X, minY = pts[0].Y, minZ = pts[0].Z;
            double maxX = pts[0].X, maxY = pts[0].Y, maxZ = pts[0].Z;
            for (int i = 1; i < 8; i++)
            {
                var p = pts[i];
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }

            return new Aabb
            {
                min = new XYZ(minX, minY, minZ),
                max = new XYZ(maxX, maxY, maxZ)
            };
        }

        private static bool AabbIntersects(XYZ amin, XYZ amax, XYZ bmin, XYZ bmax)
        {
            bool sepX = (amax.X < bmin.X) || (bmax.X < amin.X);
            bool sepY = (amax.Y < bmin.Y) || (bmax.Y < amin.Y);
            bool sepZ = (amax.Z < bmin.Z) || (bmax.Z < amin.Z);
            return !(sepX || sepY || sepZ);
        }
    }
}
