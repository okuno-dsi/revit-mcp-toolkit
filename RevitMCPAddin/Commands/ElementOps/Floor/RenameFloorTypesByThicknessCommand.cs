// File: Commands/ElementOps/Floor/RenameFloorTypesByThicknessCommand.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    /// <summary>
    /// Rename FloorType names by prefixing the overall structure thickness in millimeters.
    /// Default format: "({mm}mm) <BaseName>" with integer mm.
    /// Removes existing leading thickness like "(200mm) ".
    /// </summary>
    public class RenameFloorTypesByThicknessCommand : IRevitCommandHandler
    {
        public string CommandName => "rename_floor_types_by_thickness";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "No active document." };

            var p = (JObject)(cmd.Params ?? new JObject());
            bool dryRun = p.Value<bool?>("dryRun") ?? false;
            string template = p.Value<string>("template") ?? "({mm}mm) ";

            // Collect floor types
            var floorTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .WhereElementIsElementType()
                .Cast<FloorType>()
                .ToList();

            // Build name set for conflicts
            var nameSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in floorTypes)
            {
                try { nameSet.Add(t.Name ?? string.Empty); } catch { }
            }

            var items = new List<object>();
            int processed = 0, renamed = 0, skipped = 0;

            using (var tx = new Transaction(doc, "Rename Floor Types By Thickness"))
            {
                if (!dryRun) tx.Start();

                foreach (var ft in floorTypes)
                {
                    processed++;
                    try
                    {
                        double mm = GetThicknessMillimeters(ft);
                        if (mm <= 0)
                        {
                            skipped++; items.Add(new { typeId = ft.Id.IntegerValue, oldName = ft.Name, reason = "no_thickness" });
                            continue;
                        }
                        string mmInt = Math.Round(mm).ToString(CultureInfo.InvariantCulture);
                        string prefix = template.Replace("{mm}", mmInt);
                        string baseName = StripThicknessPrefix(ft.Name ?? string.Empty);
                        string newName = prefix + baseName;
                        if (string.Equals(newName, ft.Name, StringComparison.Ordinal))
                        {
                            skipped++; items.Add(new { typeId = ft.Id.IntegerValue, oldName = ft.Name, reason = "already_up_to_date" });
                            continue;
                        }
                        if (nameSet.Contains(newName))
                        {
                            skipped++; items.Add(new { typeId = ft.Id.IntegerValue, oldName = ft.Name, newName, reason = "name_conflict" });
                            continue;
                        }

                        if (!dryRun)
                        {
                            ft.Name = newName;
                            nameSet.Add(newName);
                        }
                        renamed++;
                        items.Add(new { typeId = ft.Id.IntegerValue, oldName = ft.Name, newName });
                    }
                    catch (Exception ex)
                    {
                        skipped++; items.Add(new { typeId = ft.Id.IntegerValue, oldName = ft.Name, reason = ex.Message });
                    }
                }

                if (!dryRun) tx.Commit();
            }

            return new { ok = true, processed, renamed, skipped, items };
        }

        private static double GetThicknessMillimeters(FloorType ft)
        {
            try
            {
                var cs = ft.GetCompoundStructure();
                if (cs == null) return 0.0;
                double total = 0.0;
                foreach (var layer in cs.GetLayers())
                {
                    try { total += layer.Width; } catch { }
                }
#if REVIT2023_OR_NEWER
                return UnitUtils.ConvertFromInternalUnits(total, UnitTypeId.Millimeters);
#else
                return UnitUtils.ConvertFromInternalUnits(total, DisplayUnitType.DUT_MILLIMETERS);
#endif
            }
            catch { return 0.0; }
        }

        private static string StripThicknessPrefix(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var n = name.TrimStart();
            try
            {
                if (n.Length > 2 && n[0] == '(')
                {
                    int end = n.IndexOf(')');
                    if (end > 0)
                    {
                        var inner = n.Substring(1, end - 1).Replace(" ", string.Empty);
                        // e.g., 200mm, 180MM
                        if (inner.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
                            return n.Substring(end + 1).TrimStart();
                    }
                }
            }
            catch { }
            return name;
        }
    }
}

