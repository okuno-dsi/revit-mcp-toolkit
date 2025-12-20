// ================================================================
// File: Core/Snapshot/SnapshotWriter.cs
// Note : JSONLの1行=1要素。mm/m2などSIで正規化（UnitHelper利用前提）。
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core
{
    internal static class SnapshotWriter
    {
        public static CategoryFile WriteCategoryJsonl(Document doc, string rootDir, string categoryName, SnapshotOptions opt, string unitsMode)
        {
            var filePath = Path.Combine(rootDir, $"{categoryName}.jsonl");
            Directory.CreateDirectory(rootDir);

            int rowCount = 0;
            var sha256 = SHA256.Create();

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                foreach (var row in EnumerateElements(doc, categoryName, opt, unitsMode))
                {
                    var json = JsonConvert.SerializeObject(row);
                    sw.WriteLine(json);
                    rowCount++;
                    // スキーマハッシュは列名順＋型の集合から計算したいが
                    // ここでは簡易に行データのKey集合から導出
                    sha256.TransformBlock(Encoding.UTF8.GetBytes(string.Join(",", row.Keys.OrderBy(x => x))), 0,
                        Encoding.UTF8.GetByteCount(string.Join(",", row.Keys.OrderBy(x => x))), null, 0);

                    if (opt.LimitPerCategory > 0 && rowCount >= opt.LimitPerCategory)
                        break;
                }
            }
            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var schemaHash = "sha256:" + BitConverter.ToString(sha256.Hash!).Replace("-", "").ToLowerInvariant();

            return new CategoryFile
            {
                Category = categoryName,
                Path = filePath,
                RowCount = rowCount,
                SchemaHash = schemaHash
            };
        }

        private static IEnumerable<Dictionary<string, object?>> EnumerateElements(Document doc, string categoryName, SnapshotOptions opt, string unitsMode)
        {
            BuiltInCategory? bic = TryMapCategoryName(categoryName);
            if (bic == null) yield break;

            var cat = new ElementCategoryFilter(bic.Value);
            var col = new FilteredElementCollector(doc).WherePasses(cat).WhereElementIsNotElementType();

            int count = 0;
            foreach (var e in col)
            {
                var row = MakeRow(doc, e, categoryName, unitsMode);
                yield return row;

                count++;
                if (opt.LimitPerCategory > 0 && count >= opt.LimitPerCategory) yield break;
            }
        }

        private static Dictionary<string, object?> MakeRow(Document doc, Element e, string categoryName, string unitsMode)
        {
            var dict = new Dictionary<string, object?>();
            dict["elementId"] = e.Id.IntegerValue;
            dict["uniqueId"] = e.UniqueId;
            dict["category"] = categoryName;

            // 代表的な列（必要に応じて拡張）
            dict["typeName"] = SafeGetTypeName(doc, e);
            dict["level"] = SafeGetLevelName(doc, e);
            dict["comments"] = TryGetParamString(e, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);

            // 壁などの代表的な数値（UnitHelper前提。ここではft→mmの簡易変換）
            try
            {
                var bbox = e.get_BoundingBox(null);
                if (bbox != null)
                {
                    dict["bbox_min"] = new[] { FtToMm(bbox.Min.X), FtToMm(bbox.Min.Y), FtToMm(bbox.Min.Z) };
                    dict["bbox_max"] = new[] { FtToMm(bbox.Max.X), FtToMm(bbox.Max.Y), FtToMm(bbox.Max.Z) };
                }
            }
            catch { /* ignore */ }

            dict["lastModified"] = DateTime.UtcNow.ToString("o"); // 実際は履歴から取れれば理想

            return dict;
        }

        private static double FtToMm(double ft) => ft * 304.8;

        private static string SafeGetTypeName(Document doc, Element e)
        {
            try
            {
                var typeId = e.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    var t = doc.GetElement(typeId);
                    return t?.Name ?? "";
                }
            }
            catch { }
            return "";
        }

        private static string SafeGetLevelName(Document doc, Element e)
        {
            try
            {
                var p = e.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var lv = doc.GetElement(p.AsElementId()) as Level;
                    return lv?.Name ?? "";
                }
            }
            catch { }
            return "";
        }

        private static string TryGetParamString(Element e, BuiltInParameter bip)
        {
            try
            {
                var p = e.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.String)
                    return p.AsString() ?? "";
            }
            catch { }
            return "";
        }

        private static BuiltInCategory? TryMapCategoryName(string categoryName)
        {
            // 必要に応じて拡張
            switch (categoryName)
            {
                case "Walls": return BuiltInCategory.OST_Walls;
                case "Rooms": return BuiltInCategory.OST_Rooms;
                case "Doors": return BuiltInCategory.OST_Doors;
                case "Windows": return BuiltInCategory.OST_Windows;
                default: return null;
            }
        }
    }
}
