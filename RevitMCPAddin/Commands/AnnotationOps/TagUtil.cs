// File: RevitMCPAddin/Commands/AnnotationOps/TagUtil.cs
// Target: .NET Framework 4.8 / Revit 2023+ / C# 8
// Changes:
//  - 旧 自前変換(MmToFt/FtToMm/ConvertDoubleBySpec/ToInternalBySpec) を UnitHelper に統一
//  - ConvertDoubleBySpec: SI 正規化 (Length=mm / Area=m2 / Volume=m3 / Angle=deg)
//  - ToInternalBySpec   : SI 入力 → 内部値(ft/rad等) に安全変換
//  - Resolve系/ガードは従来踏襲
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.AnnotationOps
{
    internal static class TagUtil
    {
        // I/Oメタ（既存互換: TagCommands から参照される）
        public static object UnitsIn() => new { Length = "mm", Angle = "deg" };
        public static object UnitsInt() => new { Length = "ft", Angle = "rad" };

        // ---- Length 変換（UnitHelperへ委譲）----
        public static double MmToFt(double mm) => UnitHelper.MmToFt(mm);
        public static double FtToMm(double ft) => UnitHelper.FtToMm(ft);

        // ---- Double値のSpec変換（外部化: SI 正規化）----
        // spec: Length→mm, Area→m2, Volume→m3, Angle→deg, 不明→raw
        public static object ConvertDoubleBySpec(double rawInternal, ForgeTypeId fdt)
        {
            try
            {
                if (fdt == null) return Math.Round(rawInternal, 3);
                var si = UnitHelper.ToExternal(rawInternal, fdt, siDigits: 6); // 角度もdegに
                if (si.HasValue)
                {
                    // 表示桁: 長さ3/面積体積6/角度6 の統一丸め（Specで概ね妥当）
                    if (fdt.Equals(SpecTypeId.Length)) return Math.Round(si.Value, 3);
                    if (fdt.Equals(SpecTypeId.Area)) return Math.Round(si.Value, 6);
                    if (fdt.Equals(SpecTypeId.Volume)) return Math.Round(si.Value, 6);
                    if (fdt.Equals(SpecTypeId.Angle)) return Math.Round(si.Value, 6);
                    return Math.Round(si.Value, 6);
                }
            }
            catch { /* fallthrough */ }
            return Math.Round(rawInternal, 3);
        }

        // ---- SI → 内部値（Spec基準）----
        public static double ToInternalBySpec(double vSi, ForgeTypeId fdt)
        {
            try
            {
                if (fdt == null) return vSi; // 未判定はそのまま
                return UnitHelper.ToInternal(vSi, fdt);
            }
            catch { return vSi; }
        }

        // ---- View/Element/TagType 解決（従来踏襲）----
        public static View ResolveView(Document doc, JObject p)
        {
            int vid = p.Value<int?>("viewId") ?? 0;
            string vuid = p.Value<string>("viewUniqueId");
            View v = null;
            if (vid > 0) v = doc.GetElement(new ElementId(vid)) as View;
            else if (!string.IsNullOrWhiteSpace(vuid)) v = doc.GetElement(vuid) as View;
            return v;
        }

        public static Element ResolveElement(Document doc, JObject p)
        {
            int eid = p.Value<int?>("elementId") ?? p.Value<int?>("hostElementId") ?? 0;
            string uid = p.Value<string>("uniqueId");
            if (eid > 0) return doc.GetElement(new ElementId(eid));
            if (!string.IsNullOrWhiteSpace(uid)) return doc.GetElement(uid);
            return null;
        }

        /// <summary>タグタイプを解決（FamilySymbol）。typeId または typeName(+familyName[+categoryName])。</summary>
        public static FamilySymbol ResolveTagType(Document doc, JObject p)
        {
            int typeId = p.Value<int?>("typeId") ?? 0;
            string typeName = p.Value<string>("typeName");
            string familyName = p.Value<string>("familyName");
            string categoryName = p.Value<string>("categoryName");

            FamilySymbol fs = null;
            if (typeId > 0)
            {
                fs = doc.GetElement(new ElementId(typeId)) as FamilySymbol;
            }
            else if (!string.IsNullOrWhiteSpace(typeName))
            {
                var q = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .Where(s => string.Equals(s.Name ?? "", typeName, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(familyName))
                    q = q.Where(s => string.Equals(s.Family?.Name ?? "", familyName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(categoryName))
                    q = q.Where(s => string.Equals(s.Category?.Name ?? "", categoryName, StringComparison.OrdinalIgnoreCase));

                fs = q.OrderBy(s => s.Family?.Name ?? "")
                      .ThenBy(s => s.Name ?? "")
                      .FirstOrDefault();
            }
            return fs;
        }

        public static bool ViewAllowsAnnotation(View v)
        {
            if (v == null) return false;
            // 旧実装と同等の緩めガード（テンプレ/一部3Dで制限）
            return !(v is View3D && ((View3D)v).IsTemplate);
        }
    }
}
