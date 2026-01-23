#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Core
{
    /// <summary>
    /// Helper to resolve user-friendly category names into Revit BuiltInCategory values.
    /// </summary>
    internal static class CategoryResolver
    {
        private static readonly Dictionary<string, BuiltInCategory> _nameMap =
            new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
            {
                // Common model categories (English)
                { "Walls", BuiltInCategory.OST_Walls },
                { "Wall", BuiltInCategory.OST_Walls },
                { "Floors", BuiltInCategory.OST_Floors },
                { "Floor", BuiltInCategory.OST_Floors },
                { "Roofs", BuiltInCategory.OST_Roofs },
                { "Roof", BuiltInCategory.OST_Roofs },
                { "Ceilings", BuiltInCategory.OST_Ceilings },
                { "Ceiling", BuiltInCategory.OST_Ceilings },
                { "Generic Models", BuiltInCategory.OST_GenericModel },
                { "Generic Model", BuiltInCategory.OST_GenericModel },
                { "Columns", BuiltInCategory.OST_Columns },
                { "Structural Columns", BuiltInCategory.OST_StructuralColumns },
                { "Structural Column", BuiltInCategory.OST_StructuralColumns },
                { "Structural Framing", BuiltInCategory.OST_StructuralFraming },
                { "Structural Frame", BuiltInCategory.OST_StructuralFraming },
                { "Casework", BuiltInCategory.OST_Casework },
                { "Doors", BuiltInCategory.OST_Doors },
                { "Windows", BuiltInCategory.OST_Windows },
                { "Curtain Panels", BuiltInCategory.OST_CurtainWallPanels },
                { "Curtain Panel", BuiltInCategory.OST_CurtainWallPanels },
                { "Rooms", BuiltInCategory.OST_Rooms },
                { "Room", BuiltInCategory.OST_Rooms },
                { "Spaces", BuiltInCategory.OST_MEPSpaces },
                { "Space", BuiltInCategory.OST_MEPSpaces },
                { "Areas", BuiltInCategory.OST_Areas },
                { "Area", BuiltInCategory.OST_Areas },

                // Japanese localized examples (typical names; may vary by environment)
                { "壁", BuiltInCategory.OST_Walls },
                { "床", BuiltInCategory.OST_Floors },
                { "屋根", BuiltInCategory.OST_Roofs },
                { "天井", BuiltInCategory.OST_Ceilings },
                { "一般モデル", BuiltInCategory.OST_GenericModel },
                { "柱", BuiltInCategory.OST_Columns },
                { "構造柱", BuiltInCategory.OST_StructuralColumns },
                { "構造フレーム", BuiltInCategory.OST_StructuralFraming },
                { "建具", BuiltInCategory.OST_Doors },
                { "窓", BuiltInCategory.OST_Windows },
                { "部屋", BuiltInCategory.OST_Rooms },
                { "スペース", BuiltInCategory.OST_MEPSpaces },
                { "エリア", BuiltInCategory.OST_Areas }
            };

        /// <summary>
        /// Resolve a list of category name strings into a set of BuiltInCategory values.
        /// Unresolvable names are silently ignored.
        /// </summary>
        public static IReadOnlyList<BuiltInCategory> ResolveCategories(IEnumerable<string>? names)
        {
            var result = new HashSet<BuiltInCategory>();

            if (names != null)
            {
                foreach (var raw in names)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    if (TryResolveCategory(raw, out var bic))
                        result.Add(bic);
                }
            }

            return result.ToList();
        }

        /// <summary>
        /// Resolve a single category string into BuiltInCategory.
        /// Supports friendly/localized names and enum names (e.g. "OST_Walls").
        /// </summary>
        public static bool TryResolveCategory(string? name, out BuiltInCategory bic)
        {
            bic = BuiltInCategory.INVALID;
            if (string.IsNullOrWhiteSpace(name)) return false;

            var n = name.Trim();

            // 1) Direct map (friendly or localized)
            if (_nameMap.TryGetValue(n, out var b1) && b1 != BuiltInCategory.INVALID)
            {
                bic = b1;
                return true;
            }

            // 2) BuiltInCategory enum name (e.g. "OST_Walls")
            if (Enum.TryParse<BuiltInCategory>(n, ignoreCase: true, out var b2) && b2 != BuiltInCategory.INVALID)
            {
                bic = b2;
                return true;
            }

            // 3) Alias dictionary (best-effort, ambiguous -> false)
            if (CategoryAliasService.TryResolveBuiltInCategory(n, out var b3) && b3 != BuiltInCategory.INVALID)
            {
                bic = b3;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Default set of paint-relevant model categories, used when no categories are specified.
        /// </summary>
        public static IReadOnlyList<BuiltInCategory> DefaultPaintableModelCategories()
        {
            return new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows
            };
        }
    }
}
