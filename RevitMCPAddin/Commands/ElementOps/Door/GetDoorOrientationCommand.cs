// RevitMCPAddin/Commands/ElementOps/Door/GetDoorOrientationHandler.cs
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Door
{
    public class GetDoorOrientationHandler : IRevitCommandHandler
    {
        public string CommandName => "get_door_orientation";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var el = DoorUtil.ResolveElement(doc, cmd.Params);
            var inst = el as FamilyInstance;
            if (inst == null) return new { ok = false, msg = "FamilyInstance が見つかりません（elementId/uniqueId）。" };
            if (inst.Category?.Id.IntValue() != (int)BuiltInCategory.OST_Doors)
                return new { ok = false, msg = "対象はドアではありません。" };

            XYZ hand = inst.HandOrientation ?? XYZ.BasisX;
            XYZ face = inst.FacingOrientation ?? XYZ.BasisY;
            bool handFlipped = inst.HandFlipped;
            bool facingFlipped = inst.FacingFlipped;
            bool mirrored = inst.Mirrored;

            int? hostWallId = null;
            XYZ wallNormal = null;
            if (inst.Host is Autodesk.Revit.DB.Wall w) { hostWallId = w.Id.IntValue(); wallNormal = w.Orientation; }

            Func<XYZ, double?> yawDeg = v => v == null ? (double?)null : Math.Atan2(v.Y, v.X) * 180.0 / Math.PI;

            double? openingDeg = null;
            try
            {
                Parameter sp = inst.LookupParameter("開き角度")
                              ?? inst.LookupParameter("Swing Angle")
                              ?? inst.LookupParameter("Angle");
                if (sp != null && sp.StorageType == StorageType.Double)
                {
                    ForgeTypeId fdt = null; try { fdt = sp.Definition?.GetDataType(); } catch { }
                    var outv = DoorUtil.ConvertDoubleBySpec(sp.AsDouble(), fdt);
                    if (outv is double dd) openingDeg = dd;
                }
            }
            catch { openingDeg = null; }

            object location = null;
            var lp = inst.Location as LocationPoint;
            if (lp != null && lp.Point != null)
            {
                location = new
                {
                    x = Math.Round(UnitHelper.FtToMm(lp.Point.X), 3),
                    y = Math.Round(UnitHelper.FtToMm(lp.Point.Y), 3),
                    z = Math.Round(UnitHelper.FtToMm(lp.Point.Z), 3)
                };
            }

            return new
            {
                ok = true,
                elementId = inst.Id.IntValue(),
                uniqueId = inst.UniqueId,
                typeId = inst.Symbol?.Id.IntValue(),
                typeName = inst.Symbol?.Name ?? "",
                familyName = inst.Symbol?.Family?.Name ?? "",
                hostWallId,
                location,
                handOrientation = new { x = hand.X, y = hand.Y, z = hand.Z },
                facingOrientation = new { x = face.X, y = face.Y, z = face.Z },
                wallNormal = wallNormal == null ? null : new { x = wallNormal.X, y = wallNormal.Y, z = wallNormal.Z },
                yaw = new { handDeg = yawDeg(hand), facingDeg = yawDeg(face), wallNormalDeg = yawDeg(wallNormal) },
                flips = new { handFlipped, facingFlipped, mirrored },
                openingAngleDeg = openingDeg,
                inputUnits = new { Angle = "deg" },
                internalUnits = new { Length = "ft", Angle = "rad" }
            };
        }
    }
}

