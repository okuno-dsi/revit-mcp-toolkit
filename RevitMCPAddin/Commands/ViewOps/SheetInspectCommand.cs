#nullable enable
// ================================================================
// File   : Commands/ViewOps/SheetInspectCommand.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Summary:
//   Step 8: Diagnostics (Visibility & UI traps)
//   - sheet.inspect (alias: sheet_inspect)
//   Provides sheet diagnostics: titleblocks, outline, viewports.
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using static Autodesk.Revit.DB.UnitUtils;

namespace RevitMCPAddin.Commands.ViewOps
{
    [RpcCommand(
        "sheet.inspect",
        Aliases = new[] { "sheet_inspect" },
        Category = "Diagnostics",
        Tags = new[] { "sheet", "diag", "titleblock", "viewport" },
        Risk = RiskLevel.Low,
        Kind = "read",
        Importance = "high",
        Summary = "Inspect a sheet (titleblocks, outline, viewports).",
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"sheet.inspect\", \"params\":{ \"sheet\":{ \"number\":\"A-101\" } } }"
    )]
    public sealed class SheetInspectCommand : IRevitCommandHandler
    {
        // Legacy dispatch (kept; canonical name is provided by RpcCommandAttribute).
        public string CommandName => "sheet_inspect";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var doc = uiapp?.ActiveUIDocument?.Document;
                if (doc == null)
                    return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "No active document.");

                var p = cmd?.Params as JObject ?? new JObject();

                var sheetRef = SheetRef.TryParse(p, out var sheetSpecified);
                var sheet = ResolveSheet(doc, p, sheetRef, sheetSpecified);
                if (sheet == null)
                {
                    if (sheetSpecified)
                        return RpcResultEnvelope.Fail("INVALID_PARAMS", "Sheet not found. Provide sheet.id / sheet.uniqueId / sheet.number / sheet.name, or make a sheet view active.");
                    return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "No active sheet view and no sheet specified.");
                }

                if (sheetSpecified && sheetRef != null && !sheetRef.Matches(sheet))
                    return RpcResultEnvelope.Fail("INVALID_PARAMS", "Sheet not found (or ambiguous). Confirm sheet id/uniqueId/number/name and retry.");

                bool includeTitleblocks = p.Value<bool?>("includeTitleblocks") ?? true;
                bool includeViewports = p.Value<bool?>("includeViewports") ?? true;

                var warnings = new List<string>();

                // --- Outline ---
                var outlineObj = new JObject();
                bool needsFallbackSize = false;
                try
                {
                    var ol = sheet.Outline;
                    if (ol != null)
                    {
                        // ViewSheet.Outline is a 2D box (BoundingBoxUV).
                        var min = ol.Min;
                        var max = ol.Max;
                        var wMm = ConvertFromInternalUnits(Math.Abs(max.U - min.U), UnitTypeId.Millimeters);
                        var hMm = ConvertFromInternalUnits(Math.Abs(max.V - min.V), UnitTypeId.Millimeters);

                        outlineObj["minX"] = ConvertFromInternalUnits(min.U, UnitTypeId.Millimeters);
                        outlineObj["minY"] = ConvertFromInternalUnits(min.V, UnitTypeId.Millimeters);
                        outlineObj["maxX"] = ConvertFromInternalUnits(max.U, UnitTypeId.Millimeters);
                        outlineObj["maxY"] = ConvertFromInternalUnits(max.V, UnitTypeId.Millimeters);
                        outlineObj["width"] = wMm;
                        outlineObj["height"] = hMm;
                        if (wMm <= 0.01 || hMm <= 0.01) needsFallbackSize = true;
                    }
                    else
                    {
                        needsFallbackSize = true;
                    }
                }
                catch
                {
                    needsFallbackSize = true;
                }
                if (needsFallbackSize)
                    warnings.Add("Sheet outline size could not be inferred reliably. A titleblock may be missing or outline is invalid.");

                // --- Titleblocks ---
                JObject? titleblocksObj = null;
                if (includeTitleblocks)
                {
                    titleblocksObj = BuildTitleblockInfo(doc, sheet, warnings);
                }

                // --- Viewports ---
                JArray? viewportsArr = null;
                if (includeViewports)
                {
                    viewportsArr = BuildViewportInfo(doc, sheet);
                }

                var data = new JObject
                {
                    ["sheet"] = new JObject
                    {
                        ["sheetId"] = sheet.Id.IntValue(),
                        ["uniqueId"] = sheet.UniqueId ?? "",
                        ["sheetNumber"] = sheet.SheetNumber ?? "",
                        ["sheetName"] = sheet.Name ?? "",
                        ["isPlaceholder"] = SafeBool(() => sheet.IsPlaceholder),
                        ["title"] = SafeString(() => sheet.Title)
                    },
                    ["outlineMm"] = outlineObj,
                    ["needsFallbackSheetSize"] = needsFallbackSize,
                    ["titleblocks"] = titleblocksObj != null ? (JToken)titleblocksObj : JValue.CreateNull(),
                    ["viewports"] = viewportsArr != null ? (JToken)viewportsArr : JValue.CreateNull()
                };

                return new JObject
                {
                    ["ok"] = true,
                    ["code"] = "OK",
                    ["msg"] = "Sheet diagnostics.",
                    ["warnings"] = new JArray(warnings.Distinct().Where(x => !string.IsNullOrWhiteSpace(x))),
                    ["data"] = data
                };
            }
            catch (Exception ex)
            {
                return RpcResultEnvelope.Fail("INTERNAL_ERROR", "sheet.inspect failed: " + ex.Message);
            }
        }

        private static ViewSheet? ResolveSheet(Document doc, JObject p, SheetRef? sheetRef, bool sheetSpecified)
        {
            if (doc == null) return null;

            if (!sheetSpecified)
                return doc.ActiveView as ViewSheet;

            // Use existing resolver for consistency, but do not accept fallback silently (we validate Matches()).
            string? warn;
            var s = PlaceViewOnSheetAutoCommand.ResolveSheetFlexible(doc, p, out warn);
            return s;
        }

        private static JObject BuildTitleblockInfo(Document doc, ViewSheet sheet, List<string> warnings)
        {
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

            if (ids.Count == 0)
                warnings.Add("No titleblock instance found on this sheet.");
            else if (ids.Count > 1)
                warnings.Add("Multiple titleblock instances found on this sheet.");

            var items = new JArray();
            foreach (var id in ids)
            {
                var e = doc.GetElement(id);
                if (e == null) continue;

                var typeId = e.GetTypeId();
                var et = typeId != null ? doc.GetElement(typeId) as ElementType : null;

                items.Add(new JObject
                {
                    ["titleblockId"] = id.IntValue(),
                    ["typeId"] = typeId != null ? (JToken)new JValue(typeId.IntValue()) : JValue.CreateNull(),
                    ["typeName"] = et != null ? (et.Name ?? "") : "",
                    ["familyName"] = et != null ? (et.FamilyName ?? "") : ""
                });
            }

            return new JObject
            {
                ["count"] = ids.Count,
                ["items"] = items
            };
        }

        private static JArray BuildViewportInfo(Document doc, ViewSheet sheet)
        {
            var arr = new JArray();
            ICollection<ElementId> vpIds;
            try { vpIds = sheet.GetAllViewports(); }
            catch { vpIds = new List<ElementId>(); }

            foreach (var id in vpIds)
            {
                var vp = doc.GetElement(id) as Viewport;
                if (vp == null) continue;

                var view = doc.GetElement(vp.ViewId) as View;

                var item = new JObject
                {
                    ["viewportId"] = vp.Id.IntValue(),
                    ["viewId"] = vp.ViewId.IntValue(),
                    ["viewName"] = view != null ? (view.Name ?? "") : "",
                    ["rotation"] = SafeString(() => vp.Rotation.ToString())
                };

                try
                {
                    var c = vp.GetBoxCenter();
                    item["boxCenterMm"] = new JObject
                    {
                        ["x"] = ConvertFromInternalUnits(c.X, UnitTypeId.Millimeters),
                        ["y"] = ConvertFromInternalUnits(c.Y, UnitTypeId.Millimeters)
                    };
                }
                catch { /* ignore */ }

                try
                {
                    var ol = vp.GetBoxOutline();
                    if (ol != null)
                    {
                        var min = ol.MinimumPoint;
                        var max = ol.MaximumPoint;
                        item["boxOutlineMm"] = new JObject
                        {
                            ["minX"] = ConvertFromInternalUnits(min.X, UnitTypeId.Millimeters),
                            ["minY"] = ConvertFromInternalUnits(min.Y, UnitTypeId.Millimeters),
                            ["maxX"] = ConvertFromInternalUnits(max.X, UnitTypeId.Millimeters),
                            ["maxY"] = ConvertFromInternalUnits(max.Y, UnitTypeId.Millimeters)
                        };
                    }
                }
                catch { /* ignore */ }

                arr.Add(item);
            }

            return arr;
        }

        private static bool SafeBool(Func<bool> getter)
        {
            try { return getter(); } catch { return false; }
        }

        private static string SafeString(Func<string> getter)
        {
            try { return getter() ?? string.Empty; } catch { return string.Empty; }
        }

        private sealed class SheetRef
        {
            public int id { get; private set; }
            public string uniqueId { get; private set; } = string.Empty;
            public string number { get; private set; } = string.Empty;
            public string name { get; private set; } = string.Empty;

            public static SheetRef? TryParse(JObject p, out bool specified)
            {
                specified = false;
                if (p == null) return null;

                var r = new SheetRef();

                var sheetObj = p["sheet"] as JObject;
                if (sheetObj != null)
                {
                    r.id = sheetObj.Value<int?>("id") ?? sheetObj.Value<int?>("sheetId") ?? 0;
                    r.uniqueId = (sheetObj.Value<string>("uniqueId") ?? "").Trim();
                    r.number = (sheetObj.Value<string>("number") ?? sheetObj.Value<string>("sheetNumber") ?? "").Trim();
                    r.name = (sheetObj.Value<string>("name") ?? sheetObj.Value<string>("sheetName") ?? "").Trim();
                }

                if (r.id <= 0) r.id = p.Value<int?>("sheetId") ?? 0;
                if (string.IsNullOrWhiteSpace(r.uniqueId)) r.uniqueId = (p.Value<string>("uniqueId") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(r.number)) r.number = (p.Value<string>("sheetNumber") ?? "").Trim();
                if (string.IsNullOrWhiteSpace(r.name)) r.name = (p.Value<string>("sheetName") ?? "").Trim();

                specified =
                    r.id > 0 ||
                    !string.IsNullOrWhiteSpace(r.uniqueId) ||
                    !string.IsNullOrWhiteSpace(r.number) ||
                    !string.IsNullOrWhiteSpace(r.name) ||
                    (sheetObj != null);

                return specified ? r : null;
            }

            public bool Matches(ViewSheet sheet)
            {
                if (sheet == null) return false;

                if (id > 0) return sheet.Id.IntValue() == id;
                if (!string.IsNullOrWhiteSpace(uniqueId)) return string.Equals(sheet.UniqueId ?? "", uniqueId, StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(number)) return string.Equals(sheet.SheetNumber ?? "", number, StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(name)) return string.Equals(sheet.Name ?? "", name, StringComparison.OrdinalIgnoreCase);
                return true;
            }
        }
    }
}
