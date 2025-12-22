// RevitMCPAddin/Commands/ElementOps/GetWindowOrientationHandler.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps
{
    public class GetWindowOrientationHandler : IRevitCommandHandler
    {
        public string CommandName => "get_window_orientation";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            FamilyInstance inst = null;

            int id = cmd.Params.Value<int?>("elementId")
                     ?? cmd.Params.Value<int?>("windowId") ?? 0;
            if (id > 0) inst = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as FamilyInstance;

            if (inst == null)
            {
                var uid = cmd.Params.Value<string>("uniqueId");
                if (!string.IsNullOrWhiteSpace(uid))
                    inst = doc.GetElement(uid) as FamilyInstance;
            }

            if (inst == null)
                return new { ok = false, msg = "Window FamilyInstance が見つかりません（elementId / uniqueId を確認）。" };

            var face = inst.FacingOrientation ?? XYZ.BasisY;
            var hand = inst.HandOrientation ?? XYZ.BasisX;
            bool facingFlipped = inst.FacingFlipped;
            bool handFlipped = inst.HandFlipped;
            bool mirrored = inst.Mirrored;

            XYZ direction = null;
            if (inst.Location is LocationCurve lc && lc.Curve != null)
            {
                var p0 = lc.Curve.GetEndPoint(0);
                var p1 = lc.Curve.GetEndPoint(1);
                var v = (p1 - p0);
                if (!v.IsZeroLength()) direction = v.Normalize();
            }
            else if (inst.Host is Autodesk.Revit.DB.Wall w && w.Location is LocationCurve wlc && wlc.Curve != null)
            {
                var p0 = wlc.Curve.GetEndPoint(0);
                var p1 = wlc.Curve.GetEndPoint(1);
                var v = (p1 - p0);
                if (!v.IsZeroLength()) direction = v.Normalize();
            }

            XYZ wallNormal = null;
            if (inst.Host is Autodesk.Revit.DB.Wall hostW) wallNormal = hostW.Orientation;

            double? YawDeg(XYZ v) => v == null ? (double?)null : Math.Atan2(v.Y, v.X) * 180.0 / Math.PI;

            return new
            {
                ok = true,
                elementId = inst.Id.IntValue(),
                uniqueId = inst.UniqueId,
                typeId = inst.GetTypeId().IntValue(),
                hostWallId = (inst.Host as Element)?.Id?.IntValue(),

                direction = direction == null ? null : new { x = direction.X, y = direction.Y, z = direction.Z },
                facingOrientation = new { x = face.X, y = face.Y, z = face.Z },
                handOrientation = new { x = hand.X, y = hand.Y, z = hand.Z },
                wallNormal = wallNormal == null ? null : new { x = wallNormal.X, y = wallNormal.Y, z = wallNormal.Z },

                yaw = new
                {
                    facingDeg = YawDeg(face),
                    handDeg = YawDeg(hand),
                    directionDeg = YawDeg(direction),
                    wallNormalDeg = YawDeg(wallNormal)
                },

                flips = new { facingFlipped, handFlipped, mirrored },

                // Unit meta
                inputUnits = new { Angle = "deg" },
                internalUnits = new { Length = "ft" }
            };
        }
    }

    internal static class XyzExt
    {
        public static bool IsZeroLength(this XYZ v)
            => v == null || (Math.Abs(v.X) < 1e-9 && Math.Abs(v.Y) < 1e-9 && Math.Abs(v.Z) < 1e-9);
    }
}


