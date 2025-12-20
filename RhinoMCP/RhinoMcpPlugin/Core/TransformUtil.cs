using System;
using Rhino.Geometry;

namespace RhinoMcpPlugin.Core
{
    public static class TransformUtil
    {
        public static bool TryExtractTROnly(Transform delta, double tol, out Vector3d t, out double yawDeg, out string error)
        {
            t = Vector3d.Zero; yawDeg = 0; error = null;

            // Translation from matrix
            t = new Vector3d(delta.M03, delta.M13, delta.M23);

            // Check linear 3x3 part is orthonormal (no scale/shear)
            var c0 = new Vector3d(delta.M00, delta.M10, delta.M20);
            var c1 = new Vector3d(delta.M01, delta.M11, delta.M21);
            var c2 = new Vector3d(delta.M02, delta.M12, delta.M22);

            double n0 = c0.Length; double n1 = c1.Length; double n2 = c2.Length;
            bool unitCols = Math.Abs(n0 - 1.0) <= tol && Math.Abs(n1 - 1.0) <= tol && Math.Abs(n2 - 1.0) <= tol;
            bool orth = Math.Abs(Vector3d.Multiply(c0, c1)) <= tol && Math.Abs(Vector3d.Multiply(c0, c2)) <= tol && Math.Abs(Vector3d.Multiply(c1, c2)) <= tol;
            double det = c0.X * (c1.Y * c2.Z - c1.Z * c2.Y) - c1.X * (c0.Y * c2.Z - c0.Z * c2.Y) + c2.X * (c0.Y * c1.Z - c0.Z * c1.Y);
            bool proper = Math.Abs(Math.Abs(det) - 1.0) <= tol; // allow reflection? here we reject if not ~1

            if (!(unitCols && orth && proper))
            {
                error = "Scale/Shear is not allowed (shape must remain unchanged).";
                return false;
            }

            // Yaw from rotated X axis projection
            var rx = c0; // X axis after rotation
            yawDeg = Math.Atan2(rx.Y, rx.X) * 180.0 / Math.PI;
            return true;
        }
    }
}
