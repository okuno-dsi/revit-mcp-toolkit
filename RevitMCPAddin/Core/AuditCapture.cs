// ================================================================
// File: Core/AuditCapture.cs
// Purpose: “元に戻す手掛かり”としての前値を採取
// ================================================================
#nullable enable
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core
{
    public static class AuditCapture
    {
        public static (double x, double y, double z)? TryGetLocationMm(Element e)
        {
            var loc = e.Location;
            if (loc is LocationPoint lp)
            {
                var p = lp.Point;
                return UnitHelper.XyzToMm(p);
            }
            return null;
        }

        public static (string storage, object? value) GetParamOldValue(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.Double: return ("Double", p.AsDouble());
                case StorageType.Integer: return ("Integer", p.AsInteger());
                case StorageType.String: return ("String", p.AsString());
                case StorageType.ElementId: return ("ElementId", p.AsElementId()?.IntegerValue ?? 0);
                default: return ("None", null);
            }
        }
    }
}
