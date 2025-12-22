// ================================================================
// Internal: FoundationUnits (UnitHelper連携ユーティリティ)
// 既定入出力: Length=mm / Area=mm2 / Volume=mm3 / Angle=deg
// ================================================================
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.Foundation
{
    internal static class FoundationUnits
    {
        public static object InputUnits() => new { Length = "mm", Area = "mm2", Volume = "mm3", Angle = "deg" };
        public static object InternalUnits() => new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" };

        // mm → 内部(ft) への統一変換
        public static double Mm(double v) => UnitHelper.ToInternalBySpec(v, SpecTypeId.Length);

        public static XYZ MmXYZ(double xMm, double yMm, double zMm)
            => new XYZ(Mm(xMm), Mm(yMm), Mm(zMm));

        // 内部値を表示用（mm/deg 他）へ
        public static object ToUser(double internalValue, ForgeTypeId spec)
            => UnitHelper.ConvertDoubleBySpec(internalValue, spec);

        // 表示値を内部へ（spec 未取得時は Length 前提）
        public static double ToInternal(double displayValue, ForgeTypeId spec)
            => UnitHelper.ToInternalBySpec(displayValue, spec ?? SpecTypeId.Length);

        // elementId / uniqueId 解決（インスタンス）
        public static Element ResolveInstance(Document doc, JObject p)
        {
            var eid = p.Value<int?>("elementId") ?? 0;
            var uid = p.Value<string>("uniqueId");
            if (eid > 0) return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(eid));
            if (!string.IsNullOrWhiteSpace(uid)) return doc.GetElement(uid);
            return null;
        }

        // typeId / typeName(+familyName) 解決（タイプ）
        public static ElementType ResolveType(Document doc, JObject p, BuiltInCategory cat)
        {
            var typeId = p.Value<int?>("typeId") ?? 0;
            var typeName = p.Value<string>("typeName");
            var familyName = p.Value<string>("familyName");

            if (typeId > 0) return doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(typeId)) as ElementType;

            if (!string.IsNullOrWhiteSpace(typeName))
            {
                // FamilySymbol 優先（ロード可能族）
                var sym = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .OfCategory(cat)
                    .Cast<FamilySymbol>()
                    .Where(s => string.Equals(s.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase)
                             && (string.IsNullOrWhiteSpace(familyName) || string.Equals(s.Family?.Name ?? "", familyName, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(s => s.Family?.Name ?? "")
                    .ThenBy(s => s.Name ?? "")
                    .ThenBy(s => s.Id.IntValue())
                    .FirstOrDefault();
                if (sym != null) return sym;

                // 見つからない場合は ElementType 全体から名称で緩和
                var any = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElementType))
                    .Cast<ElementType>()
                    .FirstOrDefault(t =>
                        string.Equals(t.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(familyName) || string.Equals(t.FamilyName ?? "", familyName, StringComparison.OrdinalIgnoreCase)));
                return any;
            }
            return null;
        }
    }
}

