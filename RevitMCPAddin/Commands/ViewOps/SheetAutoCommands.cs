#nullable enable
// ================================================================
// File   : Commands/ViewOps/SheetAutoCommands.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Summary:
//   Step 5: High-level "auto" commands for sheet workflows.
//   - sheet.place_view_auto: Place a view on a sheet; auto-duplicate if needed.
//   - sheet.remove_titleblocks_auto: Remove all titleblocks from a sheet (optionally dryRun).
// Notes:
//   - Canonical names are provided via RpcCommandAttribute; legacy names remain callable.
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ViewOps
{
    [RpcCommand(
        "sheet.place_view_auto",
        Aliases = new[] { "place_view_on_sheet_auto" },
        Category = "ViewSheet Ops",
        Tags = new[] { "sheet", "place", "auto" },
        Risk = RiskLevel.Medium,
        Kind = "write",
        Importance = "high",
        Summary = "Place a view on a sheet; auto-duplicate if already placed elsewhere.",
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"sheet.place_view_auto\", \"params\":{ \"sheet\":{ \"number\":\"A-101\" }, \"view\":{ \"name\":\"Level 1 - Plan\" }, \"placement\":{ \"mode\":\"center\" }, \"ifAlreadyPlaced\":\"duplicate_dependent\", \"dryRun\":false } }"
    )]
    public class PlaceViewOnSheetAutoCommand : IRevitCommandHandler
    {
        // Legacy dispatch (kept for backward compatibility)
        public string CommandName => "place_view_on_sheet_auto";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "No active document.");

            var p = (JObject)(cmd.Params ?? new JObject());

            bool dryRun = p.Value<bool?>("dryRun") ?? false;
            string ifAlreadyPlaced = (p.Value<string>("ifAlreadyPlaced") ?? "duplicate_dependent").Trim().ToLowerInvariant();
            if (ifAlreadyPlaced != "error" && ifAlreadyPlaced != "duplicate" && ifAlreadyPlaced != "duplicate_dependent")
                ifAlreadyPlaced = "duplicate_dependent";

            var sheet = ResolveSheetFlexible(doc, p, out var sheetWarn);
            if (sheet == null)
                return RpcResultEnvelope.Fail("INVALID_PARAMS", "Sheet not found. Provide sheet.id / sheet.number / sheetId / sheetNumber, or make a sheet view active.");

            var view = ResolveViewFlexible(doc, p, out var viewWarn);
            if (view == null)
                return RpcResultEnvelope.Fail("INVALID_PARAMS", "View not found. Provide view.id / view.name / viewId / viewUniqueId.");

            // Placement
            bool placementOk = TryResolvePlacementOnSheet(sheet, p, out var posFt, out var placementMsg);
            if (!placementOk)
                return RpcResultEnvelope.Fail("INVALID_PARAMS", placementMsg);
            string? placementWarn = null;
            if (!string.IsNullOrWhiteSpace(placementMsg))
                placementWarn = placementMsg;

            // Schedules: separate placement path.
            if (view is ViewSchedule schedule)
            {
                if (dryRun)
                {
                    return new
                    {
                        ok = true,
                        code = "OK",
                        msg = "DryRun: schedule would be placed on sheet.",
                        warnings = BuildWarnings(sheetWarn, viewWarn, placementWarn),
                        data = new
                        {
                            kind = "schedule",
                            dryRun = true,
                            sheetId = sheet.Id.IntValue(),
                            scheduleViewId = schedule.Id.IntValue(),
                            locationMm = new { x = SheetUtil.FtToMm(posFt.X), y = SheetUtil.FtToMm(posFt.Y) }
                        }
                    };
                }

                using (var tx = new Transaction(doc, "Sheet.Place View Auto (Schedule)"))
                {
                    try
                    {
                        tx.Start();
                        var inst = ScheduleSheetInstance.Create(doc, sheet.Id, schedule.Id, posFt);
                        tx.Commit();
                        return new
                        {
                            ok = true,
                            code = "OK",
                            msg = "Placed schedule on sheet.",
                            warnings = BuildWarnings(sheetWarn, viewWarn, placementWarn),
                            data = new
                            {
                                kind = "schedule",
                                sheetId = sheet.Id.IntValue(),
                                viewId = schedule.Id.IntValue(),
                                scheduleInstanceId = inst.Id.IntValue()
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return RpcResultEnvelope.Fail("INTERNAL_ERROR", "Failed to place schedule on sheet: " + ex.Message);
                    }
                }
            }

            // Reject templates and sheets themselves.
            if (view.IsTemplate)
                return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "View templates cannot be placed on sheets.");
            if (view is ViewSheet)
                return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "A sheet view cannot be placed on another sheet.");

            bool canAdd = false;
            try { canAdd = Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id); } catch { canAdd = false; }

            // If can add: create viewport
            if (canAdd)
            {
                if (dryRun)
                {
                    return new
                    {
                        ok = true,
                        code = "OK",
                        msg = "DryRun: viewport would be created.",
                        warnings = BuildWarnings(sheetWarn, viewWarn, placementWarn),
                        data = new
                        {
                            kind = "viewport",
                            dryRun = true,
                            sheetId = sheet.Id.IntValue(),
                            viewId = view.Id.IntValue(),
                            wouldDuplicate = false,
                            locationMm = new { x = SheetUtil.FtToMm(posFt.X), y = SheetUtil.FtToMm(posFt.Y) }
                        }
                    };
                }

                using (var tx = new Transaction(doc, "Sheet.Place View Auto"))
                {
                    try
                    {
                        tx.Start();
                        var vp = Viewport.Create(doc, sheet.Id, view.Id, posFt);
                        tx.Commit();
                        return new
                        {
                            ok = true,
                            code = "OK",
                            msg = "Placed view on sheet.",
                            warnings = BuildWarnings(sheetWarn, viewWarn, placementWarn),
                            data = new
                            {
                                kind = "viewport",
                                sheetId = sheet.Id.IntValue(),
                                viewId = view.Id.IntValue(),
                                viewportId = vp.Id.IntValue(),
                                createdViewId = 0
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return RpcResultEnvelope.Fail("INTERNAL_ERROR", "Failed to place view on sheet: " + ex.Message);
                    }
                }
            }

            // Can't add: try detect "already placed" and either fail or duplicate.
            var placements = GetViewportPlacements(doc, view.Id);
            if (placements.Count == 0)
            {
                return new
                {
                    ok = false,
                    code = "PRECONDITION_FAILED",
                    msg = "This view cannot be placed on sheets (unsupported view type or other constraint).",
                    warnings = BuildWarnings(sheetWarn, viewWarn, placementWarn),
                    data = new
                    {
                        sheetId = sheet.Id.IntValue(),
                        viewId = view.Id.IntValue(),
                        viewType = view.ViewType.ToString(),
                        canAdd = false,
                        placements = new object[0]
                    },
                    nextActions = new[]
                    {
                        new { method = "help.describe_command", reason = "Confirm constraints for the view type and required placement method." }
                    }
                };
            }

            // Already placed somewhere.
            if (ifAlreadyPlaced == "error")
            {
                return new
                {
                    ok = false,
                    code = "CONSTRAINT_VIEW_ALREADY_PLACED",
                    msg = "This view is already placed on another sheet.",
                    warnings = BuildWarnings(sheetWarn, viewWarn, placementWarn),
                    data = new
                    {
                        sheetId = sheet.Id.IntValue(),
                        viewId = view.Id.IntValue(),
                        existingViewportIds = placements.Select(x => x.viewportId).ToArray(),
                        existingSheetNumbers = placements.Select(x => x.sheetNumber).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        placements = placements
                    },
                    nextActions = new[]
                    {
                        new
                        {
                            method = "sheet.place_view_auto",
                            reason = "Retry with ifAlreadyPlaced='duplicate_dependent' or 'duplicate' to auto-create a new view and place it."
                        }
                    }
                };
            }

            // Duplicate (dependent or normal), then place.
            var dupOpt = (ifAlreadyPlaced == "duplicate_dependent") ? ViewDuplicateOption.AsDependent : ViewDuplicateOption.Duplicate;
            if (dryRun)
            {
                return new
                {
                    ok = true,
                    code = "OK",
                    msg = "DryRun: view would be duplicated and placed.",
                    warnings = BuildWarnings(sheetWarn, viewWarn, placementWarn),
                    data = new
                    {
                        kind = "viewport",
                        dryRun = true,
                        sheetId = sheet.Id.IntValue(),
                        sourceViewId = view.Id.IntValue(),
                        wouldDuplicate = true,
                        duplicateOption = dupOpt.ToString(),
                        locationMm = new { x = SheetUtil.FtToMm(posFt.X), y = SheetUtil.FtToMm(posFt.Y) }
                    }
                };
            }

            using (var tx = new Transaction(doc, "Sheet.Place View Auto (Duplicate)"))
            {
                try
                {
                    tx.Start();

                    ElementId dupId;
                    bool usedFallback = false;
                    try
                    {
                        dupId = view.Duplicate(dupOpt);
                    }
                    catch
                    {
                        if (dupOpt == ViewDuplicateOption.AsDependent)
                        {
                            // Some views cannot be duplicated as dependent.
                            dupId = view.Duplicate(ViewDuplicateOption.Duplicate);
                            usedFallback = true;
                        }
                        else
                        {
                            throw;
                        }
                    }

                    var newView = doc.GetElement(dupId) as View;
                    if (newView == null)
                    {
                        tx.RollBack();
                        return RpcResultEnvelope.Fail("INTERNAL_ERROR", "Duplicated view was not found.");
                    }

                    bool canAddNew = false;
                    try { canAddNew = Viewport.CanAddViewToSheet(doc, sheet.Id, newView.Id); } catch { canAddNew = false; }
                    if (!canAddNew)
                    {
                        tx.RollBack();
                        return new
                        {
                            ok = false,
                            code = "PRECONDITION_FAILED",
                            msg = "Duplicated view still cannot be placed on the target sheet.",
                            warnings = BuildWarnings(sheetWarn, viewWarn, placementWarn),
                            data = new
                            {
                                sheetId = sheet.Id.IntValue(),
                                sourceViewId = view.Id.IntValue(),
                                createdViewId = newView.Id.IntValue(),
                                placements = placements
                            }
                        };
                    }

                    var vp = Viewport.Create(doc, sheet.Id, newView.Id, posFt);

                    tx.Commit();

                    var warnings = BuildWarnings(sheetWarn, viewWarn, placementWarn).ToList();
                    if (usedFallback)
                        warnings.Add("duplicate_dependent was not supported for this view; used duplicate instead.");

                    return new
                    {
                        ok = true,
                        code = "OK",
                        msg = "Duplicated and placed view on sheet.",
                        warnings = warnings.ToArray(),
                        data = new
                        {
                            kind = "viewport",
                            sheetId = sheet.Id.IntValue(),
                            sourceViewId = view.Id.IntValue(),
                            viewId = newView.Id.IntValue(),
                            createdViewId = newView.Id.IntValue(),
                            viewportId = vp.Id.IntValue()
                        }
                    };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return RpcResultEnvelope.Fail("INTERNAL_ERROR", "Failed to duplicate/place view: " + ex.Message);
                }
            }
        }

        internal static ViewSheet ResolveSheetFlexible(Document doc, JObject p, out string? warning)
        {
            warning = null;

            // New spec: params.sheet { id|uniqueId|number|name }
            var sheetObj = p["sheet"] as JObject;
            if (sheetObj != null)
            {
                int id = sheetObj.Value<int?>("id") ?? sheetObj.Value<int?>("sheetId") ?? 0;
                if (id > 0)
                {
                    var s = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as ViewSheet;
                    if (s != null) return s;
                }

                string uid = sheetObj.Value<string>("uniqueId");
                if (!string.IsNullOrWhiteSpace(uid))
                {
                    var s = doc.GetElement(uid) as ViewSheet;
                    if (s != null) return s;
                }

                string number = (sheetObj.Value<string>("number") ?? sheetObj.Value<string>("sheetNumber") ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(number))
                {
                    var s = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .FirstOrDefault(vs => string.Equals(vs.SheetNumber ?? "", number, StringComparison.OrdinalIgnoreCase));
                    if (s != null) return s;
                }

                string name = (sheetObj.Value<string>("name") ?? sheetObj.Value<string>("sheetName") ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var s = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewSheet))
                        .Cast<ViewSheet>()
                        .FirstOrDefault(vs => string.Equals(vs.Name ?? "", name, StringComparison.OrdinalIgnoreCase));
                    if (s != null) return s;
                }
            }

            // Legacy: sheetId/uniqueId/sheetNumber (+ viewId accidents)
            var s2 = SheetUtil.ResolveSheet(doc, p);
            if (s2 != null) return s2;

            // Fallback: active sheet
            var av = doc.ActiveView as ViewSheet;
            if (av != null)
            {
                warning = "sheet was not specified; used the active sheet view.";
                return av;
            }

            return null;
        }

        internal static View ResolveViewFlexible(Document doc, JObject p, out string? warning)
        {
            warning = null;

            // New spec: params.view { id|uniqueId|name }
            var viewObj = p["view"] as JObject;
            if (viewObj != null)
            {
                int id = viewObj.Value<int?>("id") ?? viewObj.Value<int?>("viewId") ?? 0;
                if (id > 0)
                {
                    var v = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(id)) as View;
                    if (v != null) return v;
                }

                string uid = viewObj.Value<string>("uniqueId");
                if (!string.IsNullOrWhiteSpace(uid))
                {
                    var v = doc.GetElement(uid) as View;
                    if (v != null) return v;
                }

                string name = (viewObj.Value<string>("name") ?? viewObj.Value<string>("viewName") ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var matches = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Where(v => !v.IsTemplate && string.Equals(v.Name ?? "", name, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(v => v.Id.IntValue())
                        .Take(2)
                        .ToList();

                    if (matches.Count == 1) return matches[0];
                    if (matches.Count > 1)
                    {
                        warning = $"Multiple views matched name '{name}'. Used the smallest elementId={matches[0].Id.IntValue()}.";
                        return matches[0];
                    }
                }
            }

            // Legacy: viewId/viewUniqueId
            var v2 = SheetUtil.ResolveView(doc, p);
            if (v2 != null) return v2;

            // Legacy-ish: viewName at root
            string vn = (p.Value<string>("viewName") ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(vn))
            {
                var matches = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && string.Equals(v.Name ?? "", vn, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(v => v.Id.IntValue())
                    .Take(2)
                    .ToList();
                if (matches.Count == 1) return matches[0];
                if (matches.Count > 1)
                {
                    warning = $"Multiple views matched name '{vn}'. Used the smallest elementId={matches[0].Id.IntValue()}.";
                    return matches[0];
                }
            }

            return null;
        }

        private static bool TryResolvePlacementOnSheet(ViewSheet sheet, JObject p, out XYZ posFt, out string msg)
        {
            posFt = XYZ.Zero;
            msg = string.Empty;

            // New spec: placement { mode: center|point, pointMm:{x,y} }
            var placement = p["placement"] as JObject;
            if (placement != null)
            {
                string mode = (placement.Value<string>("mode") ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(mode)) mode = "center";

                if (mode == "center")
                {
                    posFt = SheetUtil.GetSheetCenterFt(sheet);
                    return true;
                }

                if (mode == "point")
                {
                    var pt = placement["pointMm"] as JObject ?? placement["point"] as JObject ?? placement["locationMm"] as JObject;
                    if (pt == null)
                    {
                        msg = "placement.pointMm {x,y} is required when placement.mode='point'.";
                        return false;
                    }
                    double x = pt.Value<double?>("x") ?? 0;
                    double y = pt.Value<double?>("y") ?? 0;
                    posFt = new XYZ(SheetUtil.MmToFt(x), SheetUtil.MmToFt(y), 0);
                    return true;
                }

                msg = "placement.mode must be 'center' or 'point'.";
                return false;
            }

            // Legacy: centerOnSheet or location {x,y} in mm
            bool center = p.Value<bool?>("centerOnSheet") ?? false;
            if (center)
            {
                posFt = SheetUtil.GetSheetCenterFt(sheet);
                return true;
            }

            var loc = p["location"] as JObject;
            if (loc != null)
            {
                posFt = new XYZ(
                    SheetUtil.MmToFt(loc.Value<double?>("x") ?? 0),
                    SheetUtil.MmToFt(loc.Value<double?>("y") ?? 0),
                    0);
                return true;
            }

            // Default to center if nothing provided.
            posFt = SheetUtil.GetSheetCenterFt(sheet);
            msg = "No placement specified; defaulted to sheet center.";
            return true;
        }

        private static List<(int viewportId, int sheetId, string sheetNumber, string sheetName)> GetViewportPlacements(Document doc, ElementId viewId)
        {
            var list = new List<(int, int, string, string)>();
            try
            {
                var vps = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .Where(vp => vp.ViewId.IntValue() == viewId.IntValue())
                    .ToList();

                foreach (var vp in vps)
                {
                    var sheet = doc.GetElement(vp.SheetId) as ViewSheet;
                    list.Add((
                        vp.Id.IntValue(),
                        sheet?.Id.IntValue() ?? vp.SheetId.IntValue(),
                        sheet?.SheetNumber ?? "",
                        sheet?.Name ?? ""
                    ));
                }
            }
            catch { /* ignore */ }
            return list;
        }

        private static string[] BuildWarnings(string? a, string? b, string? c)
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(a)) list.Add(a!);
            if (!string.IsNullOrWhiteSpace(b)) list.Add(b!);
            if (!string.IsNullOrWhiteSpace(c)) list.Add(c!);
            return list.ToArray();
        }
    }

    [RpcCommand(
        "sheet.remove_titleblocks_auto",
        Aliases = new[] { "remove_titleblocks_auto" },
        Category = "ViewSheet Ops",
        Tags = new[] { "sheet", "titleblock", "remove", "auto" },
        Risk = RiskLevel.Medium,
        Kind = "write",
        Importance = "normal",
        Summary = "Remove all titleblock instances from a sheet (supports dryRun).",
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"sheet.remove_titleblocks_auto\", \"params\":{ \"sheet\":{ \"number\":\"A-101\" }, \"dryRun\":true } }"
    )]
    public class RemoveTitleblocksAutoCommand : IRevitCommandHandler
    {
        public string CommandName => "remove_titleblocks_auto";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "No active document.");

            var p = (JObject)(cmd.Params ?? new JObject());
            bool dryRun = p.Value<bool?>("dryRun") ?? false;

            var sheet = PlaceViewOnSheetAutoCommand.ResolveSheetFlexible(doc, p, out var warn);
            if (sheet == null)
                return RpcResultEnvelope.Fail("INVALID_PARAMS", "Sheet not found. Provide sheet.id / sheet.number / sheetId / sheetNumber, or make a sheet view active.");

            var ids = new List<ElementId>();
            try
            {
                ids = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .ToElementIds()
                    .ToList();
            }
            catch { /* ignore */ }

            if (dryRun)
            {
                return new
                {
                    ok = true,
                    code = "OK",
                    msg = "DryRun: titleblocks would be deleted.",
                    warnings = string.IsNullOrWhiteSpace(warn) ? new string[0] : new[] { warn },
                    data = new
                    {
                        dryRun = true,
                        sheetId = sheet.Id.IntValue(),
                        sheetNumber = sheet.SheetNumber ?? "",
                        count = ids.Count,
                        elementIds = ids.Select(x => x.IntValue()).ToArray()
                    }
                };
            }

            using (var tx = new Transaction(doc, "Sheet.Remove TitleBlocks Auto"))
            {
                try
                {
                    tx.Start();
                    var deleted = (ids.Count > 0) ? doc.Delete(ids) : new List<ElementId>();
                    tx.Commit();
                    return new
                    {
                        ok = true,
                        code = "OK",
                        msg = "Deleted titleblocks on sheet.",
                        warnings = string.IsNullOrWhiteSpace(warn) ? new string[0] : new[] { warn },
                        data = new
                        {
                            sheetId = sheet.Id.IntValue(),
                            sheetNumber = sheet.SheetNumber ?? "",
                            requested = ids.Count,
                            deletedCount = deleted?.Count ?? 0
                        }
                    };
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return RpcResultEnvelope.Fail("INTERNAL_ERROR", "Failed to delete titleblocks: " + ex.Message);
                }
            }
        }
    }
}
