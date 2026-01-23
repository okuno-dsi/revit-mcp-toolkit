// ================================================================
// File: Commands/ElementOps/ElementTransformCommands.cs
// Purpose:
//   Basic element transforms: copy, mirror, linear/radial array, pin/unpin.
//   Units: lengthは既定mm（units="mm"|"m"|"ft"）、角度はrad既定（angleUnits="rad"|"deg"）。
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps
{
    internal static class TransformUnitParser
    {
        public static double ToInternalLength(double value, string? units)
        {
            var u = (units ?? "mm").Trim().ToLowerInvariant();
            switch (u)
            {
                case "mm":
                    return UnitHelper.MmToFt(value);
                case "m":
                    return UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Meters);
                case "ft":
                case "feet":
                    return value;
                default:
                    return UnitHelper.MmToFt(value); // fallback mm
            }
        }

        public static XYZ ToInternalVector(JObject vec, string? units)
        {
            double x = vec.Value<double?>("x") ?? 0.0;
            double y = vec.Value<double?>("y") ?? 0.0;
            double z = vec.Value<double?>("z") ?? 0.0;
            double xf = ToInternalLength(x, units);
            double yf = ToInternalLength(y, units);
            double zf = ToInternalLength(z, units);
            return new XYZ(xf, yf, zf);
        }

        public static double ToInternalAngle(double value, string? units)
        {
            var u = (units ?? "rad").Trim().ToLowerInvariant();
            switch (u)
            {
                case "deg":
                case "degree":
                case "degrees":
                    return UnitHelper.DegToInternal(value);
                default:
                    return value; // rad
            }
        }
    }

    // ------------------------------------------------------------
    // Copy
    // ------------------------------------------------------------
    [RpcCommand("element.copy_elements",
        Aliases = new[] { "element.copy", "copy_elements" },
        Category = "ElementOps",
        Kind = "write",
        Risk = RiskLevel.Low,
        Summary = "Copy elements by translation vector within the same document.")]
    public sealed class CopyElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "element.copy_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = cmd.Params as JObject ?? new JObject();
            var ids = (p["elementIds"] as JArray)?.Values<int>().Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0) return ResultUtil.Err("elementIds is required.");

            var transObj = p["translation"] as JObject;
            if (transObj == null) return ResultUtil.Err("translation is required.");
            string lenUnits = p.Value<string>("units") ?? p.Value<string>("lengthUnits") ?? "mm";
            bool failIfPinned = p.SelectToken("options.failIfPinned")?.Value<bool?>() ?? true;

            var translation = TransformUnitParser.ToInternalVector(transObj, lenUnits);

            // pre-check pinned
            if (failIfPinned)
            {
                var pinned = ids
                    .Select(id => doc.GetElement(new ElementId(id)))
                    .Where(e => e?.Pinned == true)
                    .Select(e => e!.Id.IntegerValue)
                    .ToList();
                if (pinned.Count > 0)
                    return ResultUtil.Err(new { code = "ELEMENT_PINNED", msg = "Pinned elements cannot be copied.", pinned });
            }

            var eidList = ids.Select(x => new ElementId(x)).ToList();

            using (var tx = new Transaction(doc, "[MCP] Copy Elements"))
            {
                try
                {
                    tx.Start();
                    var copied = ElementTransformUtils.CopyElements(doc, eidList, translation);
                    tx.Commit();
                    var copiedIds = copied.Select(x => x.IntegerValue).ToList();
                    var existingIds = copied
                        .Where(id => doc.GetElement(id) != null)
                        .Select(id => id.IntegerValue)
                        .ToList();
                    var missingIds = copiedIds
                        .Where(id => !existingIds.Contains(id))
                        .ToList();

                    if (missingIds.Count > 0)
                    {
                        var msg = (existingIds.Count == 0)
                            ? "Copy returned ids but none exist. Likely canceled by a dialog (e.g., overlap warning)."
                            : "Some copies were not created. Likely canceled by a dialog (e.g., overlap warning).";
                        return new
                        {
                            ok = existingIds.Count > 0,
                            code = existingIds.Count == 0 ? "COPY_RESULT_MISSING" : "COPY_RESULT_PARTIAL",
                            message = msg,
                            copiedElementIds = existingIds,
                            missingElementIds = missingIds,
                            sourceElementIds = ids,
                            units = new { length = lenUnits }
                        };
                    }

                    return new
                    {
                        ok = true,
                        copiedElementIds = copiedIds,
                        sourceElementIds = ids,
                        units = new { length = lenUnits }
                    };
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return ResultUtil.Err(new { code = "REVIT_API_EXCEPTION", msg = ex.Message });
                }
            }
        }
    }

    // ------------------------------------------------------------
    // Mirror
    // ------------------------------------------------------------
    [RpcCommand("element.mirror_elements",
        Aliases = new[] { "element.mirror", "mirror_elements" },
        Category = "ElementOps",
        Kind = "write",
        Risk = RiskLevel.Low,
        Summary = "Mirror elements by plane. Optionally keep originals or mirror in-place.")]
    public sealed class MirrorElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "element.mirror_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = cmd.Params as JObject ?? new JObject();
            var ids = (p["elementIds"] as JArray)?.Values<int>().Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0) return ResultUtil.Err("elementIds is required.");

            var planeObj = p["plane"] as JObject;
            if (planeObj == null) return ResultUtil.Err("plane is required.");

            bool mirrorCopies = p.Value<bool?>("mirrorCopies") ?? true;
            bool failIfPinned = p.SelectToken("options.failIfPinned")?.Value<bool?>() ?? true;
            bool precheck = p.SelectToken("options.precheckCanMirror")?.Value<bool?>() ?? true;
            string lenUnits = p.Value<string>("units") ?? p.Value<string>("lengthUnits") ?? "mm";

            var origin = TransformUnitParser.ToInternalVector(planeObj["origin"] as JObject ?? new JObject(), lenUnits);
            var normal = TransformUnitParser.ToInternalVector(planeObj["normal"] as JObject ?? new JObject(new JObject { ["x"] = 1.0 }), lenUnits);
            var plane = Plane.CreateByNormalAndOrigin(normal, origin);

            var eidList = ids.Select(x => new ElementId(x)).ToList();

            if (precheck && !ElementTransformUtils.CanMirrorElements(doc, eidList))
                return ResultUtil.Err(new { code = "NOT_SUPPORTED", msg = "One or more elements cannot be mirrored." });

            if (failIfPinned)
            {
                var pinned = ids.Select(id => doc.GetElement(new ElementId(id))).Where(e => e?.Pinned == true).Select(e => e!.Id.IntegerValue).ToList();
                if (pinned.Count > 0)
                    return ResultUtil.Err(new { code = "ELEMENT_PINNED", msg = "Pinned elements cannot be mirrored.", pinned });
            }

            using (var tx = new Transaction(doc, "[MCP] Mirror Elements"))
            {
                try
                {
                    tx.Start();
                    IList<ElementId> newIds = ElementTransformUtils.MirrorElements(doc, eidList, plane, mirrorCopies);
                    tx.Commit();
                    return new
                    {
                        ok = true,
                        mirrorCopies,
                        mirroredCopyIds = newIds.Select(x => x.IntegerValue).ToList(),
                        skipped = new List<object>(),
                        units = new { length = lenUnits }
                    };
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return ResultUtil.Err(new { code = "REVIT_API_EXCEPTION", msg = ex.Message });
                }
            }
        }
    }

    // ------------------------------------------------------------
    // Linear Array
    // ------------------------------------------------------------
    [RpcCommand("element.array_linear",
        Aliases = new[] { "array_linear" },
        Category = "ElementOps",
        Kind = "write",
        Risk = RiskLevel.Low,
        Summary = "Create linear array (associative or not).")]
    public sealed class ArrayLinearCommand : IRevitCommandHandler
    {
        public string CommandName => "element.array_linear";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = cmd.Params as JObject ?? new JObject();
            var ids = (p["elementIds"] as JArray)?.Values<int>().Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0) return ResultUtil.Err("elementIds is required.");

            int count = p.Value<int?>("count") ?? 0;
            if (count < 2) return ResultUtil.Err("count must be >= 2.");

            var dirObj = p["direction"] as JObject;
            if (dirObj == null) return ResultUtil.Err("direction is required.");

            double spacing = p.Value<double?>("spacing") ?? 0.0;
            if (Math.Abs(spacing) < 1e-9) return ResultUtil.Err("spacing must not be zero.");

            string lenUnits = p.Value<string>("units") ?? p.Value<string>("lengthUnits") ?? "mm";
            string anchor = p.Value<string>("anchorMember") ?? "Second";
            bool associate = p.Value<bool?>("associate") ?? true;
            bool failIfPinned = p.SelectToken("options.failIfPinned")?.Value<bool?>() ?? true;
            int? viewId = p.Value<int?>("viewId");

            var dir = TransformUnitParser.ToInternalVector(dirObj, lenUnits);
            var dirNorm = dir.Normalize();
            var translationToAnchor = dirNorm.Multiply(TransformUnitParser.ToInternalLength(spacing, lenUnits) * (count - 1));
            var anchorMember = anchor.Equals("Last", StringComparison.OrdinalIgnoreCase)
                ? ArrayAnchorMember.Last
                : ArrayAnchorMember.Second;

            var eidList = ids.Select(x => new ElementId(x)).ToList();

            if (failIfPinned)
            {
                var pinned = eidList.Select(doc.GetElement).Where(e => e?.Pinned == true).Select(e => e!.Id.IntegerValue).ToList();
                if (pinned.Count > 0)
                    return ResultUtil.Err(new { code = "ELEMENT_PINNED", msg = "Pinned elements cannot be arrayed.", pinned });
            }

            View view = null;
            if (viewId.HasValue) view = doc.GetElement(new ElementId(viewId.Value)) as View;
            view ??= uidoc.ActiveView;
            if (view == null) return ResultUtil.Err("viewId is required (active view missing).");

            using (var tx = new Transaction(doc, "[MCP] Linear Array"))
            {
                try
                {
                    tx.Start();
                    if (associate)
                    {
                        var arr = LinearArray.Create(doc, view, eidList, count, translationToAnchor, anchorMember);
                        var original = arr.GetOriginalMemberIds().Select(x => x.IntegerValue).ToList();
                        var copied = arr.GetCopiedMemberIds().Select(x => x.IntegerValue).ToList();
                        tx.Commit();
                        return new
                        {
                            ok = true,
                            associate = true,
                            arrayElementId = arr.Id.IntegerValue,
                            originalMemberIds = original,
                            copiedMemberIds = copied,
                            units = new { length = lenUnits }
                        };
                    }
                    else
                    {
                        var created = LinearArray.ArrayElementsWithoutAssociation(doc, view, eidList, count, translationToAnchor, anchorMember);
                        tx.Commit();
                        return new
                        {
                            ok = true,
                            associate = false,
                            arrayElementId = (int?)null,
                            createdElementIds = created.Select(x => x.IntegerValue).ToList(),
                            units = new { length = lenUnits }
                        };
                    }
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return ResultUtil.Err(new { code = "REVIT_API_EXCEPTION", msg = ex.Message });
                }
            }
        }
    }

    // ------------------------------------------------------------
    // Radial Array
    // ------------------------------------------------------------
    [RpcCommand("element.array_radial",
        Aliases = new[] { "array_radial" },
        Category = "ElementOps",
        Kind = "write",
        Risk = RiskLevel.Low,
        Summary = "Create radial array (associative or not).")]
    public sealed class ArrayRadialCommand : IRevitCommandHandler
    {
        public string CommandName => "element.array_radial";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = cmd.Params as JObject ?? new JObject();
            var ids = (p["elementIds"] as JArray)?.Values<int>().Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0) return ResultUtil.Err("elementIds is required.");

            int count = p.Value<int?>("count") ?? 0;
            if (count < 2) return ResultUtil.Err("count must be >= 2.");

            var axisObj = p["axis"] as JObject;
            if (axisObj == null) return ResultUtil.Err("axis is required.");

            double angle = p.Value<double?>("angle") ?? 0.0;
            if (Math.Abs(angle) < 1e-9) return ResultUtil.Err("angle must not be zero.");

            string lenUnits = p.Value<string>("units") ?? p.Value<string>("lengthUnits") ?? "mm";
            string angleUnits = p.Value<string>("angleUnits") ?? "rad";
            string anchor = p.Value<string>("anchorMember") ?? "Last";
            bool associate = p.Value<bool?>("associate") ?? true;
            bool failIfPinned = p.SelectToken("options.failIfPinned")?.Value<bool?>() ?? true;
            int? viewId = p.Value<int?>("viewId");

            var p0 = TransformUnitParser.ToInternalVector(axisObj["p0"] as JObject ?? new JObject(), lenUnits);
            var p1 = TransformUnitParser.ToInternalVector(axisObj["p1"] as JObject ?? new JObject(new JObject { ["z"] = 1.0 }), lenUnits);
            var axisLine = Line.CreateBound(p0, p1);
            double angleInternal = TransformUnitParser.ToInternalAngle(angle, angleUnits);
            var anchorMember = anchor.Equals("Second", StringComparison.OrdinalIgnoreCase)
                ? ArrayAnchorMember.Second
                : ArrayAnchorMember.Last;

            var eidList = ids.Select(x => new ElementId(x)).ToList();

            if (failIfPinned)
            {
                var pinned = eidList.Select(doc.GetElement).Where(e => e?.Pinned == true).Select(e => e!.Id.IntegerValue).ToList();
                if (pinned.Count > 0)
                    return ResultUtil.Err(new { code = "ELEMENT_PINNED", msg = "Pinned elements cannot be arrayed.", pinned });
            }

            View view = null;
            if (viewId.HasValue) view = doc.GetElement(new ElementId(viewId.Value)) as View;
            view ??= uidoc.ActiveView;
            if (view == null) return ResultUtil.Err("viewId is required (active view missing).");

            using (var tx = new Transaction(doc, "[MCP] Radial Array"))
            {
                try
                {
                    tx.Start();
                    if (associate)
                    {
                        var arr = RadialArray.Create(doc, view, eidList, count, axisLine, angleInternal, anchorMember);
                        var original = arr.GetOriginalMemberIds().Select(x => x.IntegerValue).ToList();
                        var copied = arr.GetCopiedMemberIds().Select(x => x.IntegerValue).ToList();
                        tx.Commit();
                        return new
                        {
                            ok = true,
                            associate = true,
                            arrayElementId = arr.Id.IntegerValue,
                            originalMemberIds = original,
                            copiedMemberIds = copied,
                            units = new { length = lenUnits, angle = angleUnits }
                        };
                    }
                    else
                    {
                        var created = RadialArray.ArrayElementsWithoutAssociation(doc, view, eidList, count, axisLine, angleInternal, anchorMember);
                        tx.Commit();
                        return new
                        {
                            ok = true,
                            associate = false,
                            arrayElementId = (int?)null,
                            createdElementIds = created.Select(x => x.IntegerValue).ToList(),
                            units = new { length = lenUnits, angle = angleUnits }
                        };
                    }
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return ResultUtil.Err(new { code = "REVIT_API_EXCEPTION", msg = ex.Message });
                }
            }
        }
    }

    // ------------------------------------------------------------
    // Pin (single)
    // ------------------------------------------------------------
    [RpcCommand("element.pin_element",
        Aliases = new[] { "pin_element" },
        Category = "ElementOps",
        Kind = "write",
        Risk = RiskLevel.Low,
        Summary = "Pin or unpin a single element.")]
    public sealed class PinElementCommand : IRevitCommandHandler
    {
        public string CommandName => "element.pin_element";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = cmd.Params as JObject ?? new JObject();
            int? id = p.Value<int?>("elementId");
            if (!id.HasValue) return ResultUtil.Err("elementId is required.");

            bool pinned = p.Value<bool?>("pinned") ?? true;

            using (var tx = new Transaction(doc, "[MCP] Pin Element"))
            {
                try
                {
                    tx.Start();
                    var el = doc.GetElement(new ElementId(id.Value));
                    if (el == null)
                    {
                        tx.RollBack();
                        return ResultUtil.Err(new { code = "ELEMENT_NOT_FOUND", msg = "Element not found." });
                    }
                    el.Pinned = pinned;
                    tx.Commit();
                    return new { ok = true, elementId = id.Value, pinned };
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return ResultUtil.Err(new { code = "REVIT_API_EXCEPTION", msg = ex.Message });
                }
            }
        }
    }

    // ------------------------------------------------------------
    // Pin (multiple)
    // ------------------------------------------------------------
    [RpcCommand("element.pin_elements",
        Aliases = new[] { "pin_elements" },
        Category = "ElementOps",
        Kind = "write",
        Risk = RiskLevel.Low,
        Summary = "Pin or unpin multiple elements (continueOnError optional).")]
    public sealed class PinElementsCommand : IRevitCommandHandler
    {
        public string CommandName => "element.pin_elements";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");

            var p = cmd.Params as JObject ?? new JObject();
            var ids = (p["elementIds"] as JArray)?.Values<int>().Distinct().ToList() ?? new List<int>();
            if (ids.Count == 0) return ResultUtil.Err("elementIds is required.");

            bool pinned = p.Value<bool?>("pinned") ?? true;
            bool continueOnError = p.SelectToken("options.continueOnError")?.Value<bool?>() ?? true;

            var results = new List<object>();
            bool anyOk = false;

            using (var tx = new Transaction(doc, "[MCP] Pin Elements"))
            {
                try
                {
                    tx.Start();
                    foreach (var id in ids)
                    {
                        try
                        {
                            var el = doc.GetElement(new ElementId(id));
                            if (el == null)
                            {
                                results.Add(new { elementId = id, ok = false, msg = "Element not found." });
                                if (!continueOnError) throw new InvalidOperationException("Element not found: " + id);
                                continue;
                            }
                            el.Pinned = pinned;
                            results.Add(new { elementId = id, ok = true });
                            anyOk = true;
                        }
                        catch (Exception ex)
                        {
                            results.Add(new { elementId = id, ok = false, msg = ex.Message });
                            if (!continueOnError) throw;
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted()) tx.RollBack();
                    return ResultUtil.Err(new { code = "REVIT_API_EXCEPTION", msg = ex.Message, results });
                }
            }

            return new
            {
                ok = anyOk,
                requestedPinned = pinned,
                results
            };
        }
    }
}
