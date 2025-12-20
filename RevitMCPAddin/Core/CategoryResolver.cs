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
                { "窓", BuiltInCategory.OST_Windows }
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
                    var name = raw.Trim();

                    // 1) Direct map (friendly or localized)
                    if (_nameMap.TryGetValue(name, out var bic))
                    {
                        result.Add(bic);
                        continue;
                    }

                    // 2) BuiltInCategory enum name (e.g. "OST_Walls")
                    if (Enum.TryParse<BuiltInCategory>(name, ignoreCase: true, out var bic2)
                        && bic2 != BuiltInCategory.INVALID)
                    {
                        result.Add(bic2);
                        continue;
                    }
                }
            }

            return result.ToList();
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

