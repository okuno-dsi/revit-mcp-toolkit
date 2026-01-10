// ================================================================
// File: Commands/ViewOps/CreateFocus3DViewFromSelectionCommand.cs
// Purpose:
//   Create a new isometric 3D view, set SectionBox around selected elements,
//   and optionally activate the view — all in a single MCP command to reduce round-trips.
//
// Inputs (params):
//   - elementIds?: int[]   (optional; defaults to current selection)
//   - paddingMm?: double   (default: 200)
//   - name?: string        (optional; overrides generated name)
//   - viewNamePrefix?: string (default: "Clip3D_Selected_")
//   - activate?: bool      (default: true)
//   - templateViewId?: int (optional; apply template after creating view)
//
// Output:
//   - { ok, viewId, name, activated, sectionBox:{min/max(mm)}, skipped[] }
//
// Target: .NET Framework 4.8 / Revit 2023+ / C# 8
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.RevitUI;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ViewOps
{
    [RpcCommand("view.create_focus_3d_view_from_selection",
        Aliases = new[] { "create_focus_3d_view_from_selection", "create_clipping_3d_view_from_selection", "view.create_clipping_3d_view_from_selection" },
        Category = "ViewOps",
        Tags = new[] { "View", "3D", "SectionBox" },
        Kind = "write",
        Importance = "normal",
        Risk = RiskLevel.Low,
        Summary = "Create a new 3D view focused on current selection (SectionBox clipping), optionally activate it.")]
    public sealed class CreateFocus3DViewFromSelectionCommand : IRevitCommandHandler
    {
        public string CommandName => "create_focus_3d_view_from_selection";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null) return ResultUtil.Err("No active document.");

                var p = cmd.Params as JObject ?? new JObject();
                var guard = ExpectedContextGuard.Validate(uiapp, p);
                if (guard != null) return guard;

                // 1) Resolve targets (elementIds -> fallback to current selection)
                var elementIds = new List<int>();
                if (p["elementIds"] is JArray arr && arr.Count > 0)
                {
                    elementIds.AddRange(arr.Values<int>());
                }
                else
                {
                    var sel = uidoc.Selection.GetElementIds();
                    if (sel != null && sel.Count > 0)
                        elementIds.AddRange(sel.Select(x => x.IntValue()));
                }

                elementIds = elementIds.Distinct().ToList();
                if (elementIds.Count == 0)
                    return ResultUtil.Err("elementIds was not provided and current selection is empty.");

                // 2) Parameters
                double paddingMm = p.Value<double?>("paddingMm") ?? p.Value<double?>("padding") ?? 200.0;
                bool activate = p.Value<bool?>("activate") ?? true;
                int templateViewId = p.Value<int?>("templateViewId") ?? 0;

                string desiredName = (p.Value<string>("name") ?? string.Empty).Trim();
                string prefix = (p.Value<string>("viewNamePrefix") ?? "Clip3D_Selected_").Trim();
                if (string.IsNullOrWhiteSpace(prefix)) prefix = "Clip3D_Selected_";

                string baseName = !string.IsNullOrWhiteSpace(desiredName)
                    ? desiredName
                    : prefix + DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // 3) Compute world bounding box (with padding)
                if (!TryComputeWorldSectionBox(doc, elementIds, paddingMm, out var box, out var skipped, out var minFt, out var maxFt))
                {
                    return ResultUtil.Err(new
                    {
                        msg = "Failed to compute a valid bounding box for the given elements.",
                        skipped
                    });
                }

                // 4) Create view + apply section box in a single transaction
                var warnings = new List<string>();
                View3D? view3d = null;
                using (var tx = new Transaction(doc, "[MCP] Create focus 3D view"))
                {
                    tx.Start();

                    var vft = View3DUtil.Find3DViewFamilyType(doc);
                    if (vft == null)
                    {
                        tx.RollBack();
                        return ResultUtil.Err("ViewFamilyType(ThreeDimensional) not found.");
                    }

                    view3d = View3D.CreateIsometric(doc, vft.Id);
                    view3d.Name = View3DUtil.MakeUniqueViewName(doc, baseName);

                    if (!view3d.IsSectionBoxActive) view3d.IsSectionBoxActive = true;
                    view3d.SetSectionBox(box);

                    if (templateViewId > 0)
                    {
                        try { View3DUtil.TryApplyViewTemplate(view3d, templateViewId); }
                        catch (Exception ex) { warnings.Add("templateViewId: " + ex.Message); }
                    }

                    tx.Commit();
                }

                // 5) Activate the new view (UI)
                bool activated = false;
                if (activate && view3d != null)
                {
                    activated = UiHelpers.TryRequestViewChange(uidoc, view3d);
                }

                return new
                {
                    ok = true,
                    viewId = view3d?.Id.IntValue() ?? -1,
                    name = view3d?.Name ?? string.Empty,
                    activated,
                    elementIds,
                    paddingMm,
                    sectionBox = ToMmBox(minFt, maxFt),
                    skipped,
                    warnings
                };
            }
            catch (Exception ex)
            {
                return ResultUtil.Err(ex.Message);
            }
        }

        private static bool TryComputeWorldSectionBox(
            Document doc,
            IList<int> elementIds,
            double paddingMm,
            out BoundingBoxXYZ box,
            out List<object> skipped,
            out XYZ minFt,
            out XYZ maxFt)
        {
            box = null!;
            skipped = new List<object>();
            minFt = XYZ.Zero;
            maxFt = XYZ.Zero;

            if (doc == null || elementIds == null || elementIds.Count == 0) return false;

            double padFt = ConvertToInternalUnits(paddingMm, UnitTypeId.Millimeters);
            bool any = false;
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

            foreach (var id in elementIds.Distinct())
            {
                var e = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id));
                // Skip 3D Section Box pseudo-element (no-op for section box itself)
                try
                {
                    if (e?.Category?.Id?.IntValue() == -2000301)
                    {
                        skipped.Add(new { elementId = id, reason = "section_box" });
                        continue;
                    }
                }
                catch { /* ignore */ }

                if (e == null)
                {
                    skipped.Add(new { elementId = id, reason = "not found" });
                    continue;
                }

                var bb = e.get_BoundingBox(null);
                if (bb == null)
                {
                    skipped.Add(new { elementId = id, reason = "no bounding box" });
                    continue;
                }

                var tr = bb.Transform ?? Transform.Identity;
                var corners = new[]
                {
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Min.X, bb.Max.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Min.Y, bb.Max.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                    new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z),
                }.Select(c => tr.OfPoint(c));

                foreach (var w in corners)
                {
                    minX = Math.Min(minX, w.X); minY = Math.Min(minY, w.Y); minZ = Math.Min(minZ, w.Z);
                    maxX = Math.Max(maxX, w.X); maxY = Math.Max(maxY, w.Y); maxZ = Math.Max(maxZ, w.Z);
                }
                any = true;
            }

            if (!any || !(minX < maxX && minY < maxY && minZ < maxZ)) return false;

            minX -= padFt; minY -= padFt; minZ -= padFt;
            maxX += padFt; maxY += padFt; maxZ += padFt;

            minFt = new XYZ(minX, minY, minZ);
            maxFt = new XYZ(maxX, maxY, maxZ);

            box = new BoundingBoxXYZ
            {
                Min = minFt,
                Max = maxFt,
                Transform = Transform.Identity
            };
            return true;
        }

        private static object ToMmBox(XYZ minFt, XYZ maxFt)
        {
            return new
            {
                min = new
                {
                    x = Math.Round(ConvertFromInternalUnits(minFt.X, UnitTypeId.Millimeters), 3),
                    y = Math.Round(ConvertFromInternalUnits(minFt.Y, UnitTypeId.Millimeters), 3),
                    z = Math.Round(ConvertFromInternalUnits(minFt.Z, UnitTypeId.Millimeters), 3)
                },
                max = new
                {
                    x = Math.Round(ConvertFromInternalUnits(maxFt.X, UnitTypeId.Millimeters), 3),
                    y = Math.Round(ConvertFromInternalUnits(maxFt.Y, UnitTypeId.Millimeters), 3),
                    z = Math.Round(ConvertFromInternalUnits(maxFt.Z, UnitTypeId.Millimeters), 3)
                }
            };
        }
    }
}
