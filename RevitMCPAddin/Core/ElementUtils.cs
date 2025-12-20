#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitMCPAddin.Core; // ← UnitHelper の名前空間

public static class ElementUtils
{
    public static Level GetLevelByName(Document doc, string name)
        => new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
           .FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    // ★ C#8 互換：T? を返したいので where T: class 制約を付ける
    public static T GetElement<T>(Document doc, int id) where T : class
        => doc.GetElement(new ElementId(id)) as T;

    public static bool EnsureClosed2D(IList<XYZ> poly)
        => poly != null && poly.Count >= 3 && poly.First().IsAlmostEqualTo(poly.Last());

    public static IList<XYZ> To2DPolyMm(IList<(double x, double y)> ptsMm, bool close)
    {
        var list = ptsMm.Select(p => UnitHelper.MmToXyz(p.x, p.y, 0)).ToList();
        if (close && (list.Count >= 3) && !list.First().IsAlmostEqualTo(list.Last()))
            list.Add(list.First());
        return list;
    }
}
