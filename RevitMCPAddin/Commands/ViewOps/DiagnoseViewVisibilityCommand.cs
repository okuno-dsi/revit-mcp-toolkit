#nullable enable
// ================================================================
// File   : Commands/ViewOps/DiagnoseViewVisibilityCommand.cs
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// Summary:
//   Step 8: Diagnostics (Visibility & UI traps)
//   - view.diagnose_visibility (alias: diagnose_visibility)
//   Helps answer: "It worked but I can't see it."
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
        "view.diagnose_visibility",
        Aliases = new[] { "diagnose_visibility" },
        Category = "Diagnostics",
        Tags = new[] { "view", "diag", "visibility", "template", "crop", "ui" },
        Risk = RiskLevel.Low,
        Kind = "read",
        Importance = "high",
        Summary = "Diagnose common view visibility/graphics traps (templates, crop, category visibility, temporary modes).",
        ExampleJsonRpc =
            "{ \"jsonrpc\":\"2.0\", \"id\":1, \"method\":\"view.diagnose_visibility\", \"params\":{ \"viewId\": 12345, \"includeAllCategories\": false } }"
    )]
    public sealed class DiagnoseViewVisibilityCommand : IRevitCommandHandler
    {
        // Legacy dispatch (kept; canonical name is provided by RpcCommandAttribute).
        public string CommandName => "diagnose_visibility";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var uidoc = uiapp?.ActiveUIDocument;
                var doc = uidoc?.Document;
                if (doc == null)
                    return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "No active document.");

                var p = cmd?.Params as JObject ?? new JObject();

                string? viewWarn = null;
                var (view, viewSpecified) = ResolveView(doc, p, out viewWarn);
                if (view == null)
                {
                    if (viewSpecified)
                        return RpcResultEnvelope.Fail("INVALID_PARAMS", "View not found. Provide viewId / viewUniqueId / viewName or params.view {id|uniqueId|name}.");
                    return RpcResultEnvelope.Fail("PRECONDITION_FAILED", "Active view is not available.");
                }

                bool includeAllCategories = p.Value<bool?>("includeAllCategories") ?? false;
                bool includeCategoryStates = p.Value<bool?>("includeCategoryStates") ?? true;
                bool includeTemplateParamIds = p.Value<bool?>("includeTemplateParamIds") ?? false;
                int maxTemplateParamIds = p.Value<int?>("maxTemplateParamIds") ?? 50;
                if (maxTemplateParamIds < 0) maxTemplateParamIds = 0;

                var warnings = new List<string>();
                if (!string.IsNullOrWhiteSpace(viewWarn)) warnings.Add(viewWarn!);

                // --- View summary ---
                var viewObj = new JObject
                {
                    ["viewId"] = view.Id.IntValue(),
                    ["uniqueId"] = view.UniqueId ?? "",
                    ["name"] = view.Name ?? "",
                    ["viewType"] = view.ViewType.ToString(),
                    ["rawViewType"] = view.GetType().Name,
                    ["isTemplate"] = view.IsTemplate,
                    ["canBePrinted"] = SafeBool(() => view.CanBePrinted),
                    ["scale"] = SafeInt(() => view.Scale),
                    ["discipline"] = SafeString(() => view.Discipline.ToString()),
                    ["detailLevel"] = SafeString(() => view.DetailLevel.ToString()),
                    ["displayStyle"] = SafeString(() => view.DisplayStyle.ToString()),
                    ["partsVisibility"] = SafeString(() => view.PartsVisibility.ToString())
                };

                // --- Template ---
                var templateObj = BuildTemplateDiagnostics(doc, view, includeTemplateParamIds, maxTemplateParamIds, warnings);

                // --- Global visibility toggles ---
                var globalVis = new JObject
                {
                    ["areModelCategoriesHidden"] = SafeBool(() => view.AreModelCategoriesHidden),
                    ["areAnnotationCategoriesHidden"] = SafeBool(() => view.AreAnnotationCategoriesHidden),
                    ["areAnalyticalModelCategoriesHidden"] = SafeBool(() => view.AreAnalyticalModelCategoriesHidden),
                    ["areImportCategoriesHidden"] = SafeBool(() => view.AreImportCategoriesHidden),
                    ["areCoordinationModelHandlesHidden"] = SafeBool(() => view.AreCoordinationModelHandlesHidden),
                    ["arePointCloudsHidden"] = SafeBool(() => view.ArePointCloudsHidden)
                };
                if (globalVis.Value<bool?>("areModelCategoriesHidden") == true)
                    warnings.Add("Model categories are hidden in this view (AreModelCategoriesHidden=true).");
                if (globalVis.Value<bool?>("areAnnotationCategoriesHidden") == true)
                    warnings.Add("Annotation categories are hidden in this view (AreAnnotationCategoriesHidden=true).");

                // --- Temporary view modes ---
                var tempModes = BuildTemporaryModesDiagnostics(view, warnings);

                // --- Crop / crop region ---
                var cropObj = BuildCropDiagnostics(view);

                // --- Overrides availability ---
                bool overridesAllowed = false;
                try { overridesAllowed = view.AreGraphicsOverridesAllowed(); } catch { overridesAllowed = false; }
                if (!overridesAllowed)
                    warnings.Add("Graphics overrides are not allowed in this view type (AreGraphicsOverridesAllowed=false).");

                // --- Common display-style trap (solid fill / paint visibility) ---
                try
                {
                    if (view.DisplayStyle == DisplayStyle.Wireframe)
                        warnings.Add("DisplayStyle is Wireframe. Solid fills/patterns may not appear. Try HiddenLine/Shaded/ConsistentColors.");
                }
                catch { /* ignore */ }

                // --- Category visibility states ---
                JArray? catStates = null;
                if (includeCategoryStates)
                {
                    var ids = ResolveCategoryIds(doc, p, includeAllCategories);
                    catStates = BuildCategoryStates(doc, view, ids, includeAllCategories);
                }

                var data = new JObject
                {
                    ["view"] = viewObj,
                    ["template"] = templateObj,
                    ["overrides"] = new JObject
                    {
                        ["graphicsOverridesAllowed"] = overridesAllowed
                    },
                    ["globalVisibility"] = globalVis,
                    ["temporaryModes"] = tempModes,
                    ["crop"] = cropObj,
                    ["categories"] = catStates != null ? (JToken)catStates : JValue.CreateNull()
                };

                return new JObject
                {
                    ["ok"] = true,
                    ["code"] = "OK",
                    ["msg"] = "View visibility diagnostics.",
                    ["warnings"] = new JArray(warnings.Distinct().Where(x => !string.IsNullOrWhiteSpace(x))),
                    ["data"] = data
                };
            }
            catch (Exception ex)
            {
                return RpcResultEnvelope.Fail("INTERNAL_ERROR", "view.diagnose_visibility failed: " + ex.Message);
            }
        }

        private static (View? view, bool specified) ResolveView(Document doc, JObject p, out string? warning)
        {
            warning = null;
            if (doc == null) return (null, false);

            bool specified = HasAnyViewRef(p);

            if (!specified)
                return (doc.ActiveView, false);

            // Prefer the shared "view ref" resolver used by sheet auto commands.
            var v = PlaceViewOnSheetAutoCommand.ResolveViewFlexible(doc, p, out warning);
            if (v != null) return (v, true);

            // Fallback: viewId at root (in case caller does not use ResolveViewFlexible-compatible keys).
            int viewId = p.Value<int?>("viewId") ?? 0;
            if (viewId > 0)
                return (doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewId)) as View, true);

            return (null, true);
        }

        private static bool HasAnyViewRef(JObject p)
        {
            if (p == null) return false;
            if (p["view"] is JObject) return true;
            if ((p.Value<int?>("viewId") ?? 0) > 0) return true;
            if (!string.IsNullOrWhiteSpace(p.Value<string>("viewUniqueId"))) return true;
            if (!string.IsNullOrWhiteSpace(p.Value<string>("viewName"))) return true;
            return false;
        }

        private static JObject BuildTemplateDiagnostics(Document doc, View view, bool includeParamIds, int maxParamIds, List<string> warnings)
        {
            var obj = new JObject();

            var templateId = view.ViewTemplateId;
            bool applied = templateId != null && templateId != ElementId.InvalidElementId;
            obj["applied"] = applied;
            if (!applied) return obj;

            obj["templateId"] = templateId.IntValue();

            View? template = null;
            try { template = doc.GetElement(templateId) as View; } catch { template = null; }
            if (template != null)
            {
                obj["templateName"] = template.Name ?? "";
                obj["templateViewType"] = template.ViewType.ToString();
                obj["templateRawViewType"] = template.GetType().Name;
                obj["templateIsTemplate"] = template.IsTemplate;
            }
            else
            {
                obj["templateName"] = "";
            }

            bool validTemplate = false;
            try { validTemplate = view.IsValidViewTemplate(templateId); } catch { validTemplate = false; }
            obj["isValidViewTemplateId"] = validTemplate;

            // Even if we cannot pinpoint which VG toggles are controlled, warn for visibility/override issues.
            warnings.Add("A view template is applied. If overrides/visibility changes do not appear, try removing the template or adjusting non-controlled parameters.");

            try
            {
                var all = view.GetTemplateParameterIds();
                var non = view.GetNonControlledTemplateParameterIds();
                obj["templateParameterIdsCount"] = all != null ? all.Count : 0;
                obj["nonControlledTemplateParameterIdsCount"] = non != null ? non.Count : 0;
                obj["controlledTemplateParameterCount"] = (all != null && non != null) ? Math.Max(0, all.Count - non.Count) : 0;

                if (includeParamIds)
                {
                    var nonIds = new JArray();
                    if (non != null)
                    {
                        foreach (var id in non.Take(maxParamIds))
                            nonIds.Add(id.IntValue());
                    }
                    obj["nonControlledTemplateParameterIdsSample"] = nonIds;

                    var allIds = new JArray();
                    if (all != null)
                    {
                        foreach (var id in all.Take(maxParamIds))
                            allIds.Add(id.IntValue());
                    }
                    obj["templateParameterIdsSample"] = allIds;
                }
            }
            catch { /* best-effort */ }

            return obj;
        }

        private static JObject BuildTemporaryModesDiagnostics(View view, List<string> warnings)
        {
            var obj = new JObject();

            bool isTmpHideIso = false;
            bool isInTmp = false;
            bool isTmpProps = false;
            try { isTmpHideIso = view.IsTemporaryHideIsolateActive(); } catch { isTmpHideIso = false; }
            try
            {
                isInTmp =
                    view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate) ||
                    view.IsInTemporaryViewMode(TemporaryViewMode.RevealHiddenElements) ||
                    view.IsInTemporaryViewMode(TemporaryViewMode.TemporaryViewProperties);
            }
            catch { isInTmp = false; }
            try { isTmpProps = view.IsTemporaryViewPropertiesModeEnabled(); } catch { isTmpProps = false; }

            obj["isTemporaryHideIsolateActive"] = isTmpHideIso;
            obj["isInTemporaryViewMode"] = isInTmp;
            obj["isTemporaryViewPropertiesModeEnabled"] = isTmpProps;
            obj["revealConstraintsMode"] = SafeString(() => view.RevealConstraintsMode.ToString());

            try
            {
                var tvm = view.TemporaryViewModes;
                if (tvm != null)
                {
                    obj["revealHiddenElements"] = tvm.RevealHiddenElements;
                    obj["revealConstraints"] = tvm.RevealConstraints;
                    obj["worksharingDisplay"] = tvm.WorksharingDisplay.ToString();
                }
            }
            catch { /* ignore */ }

            if (isTmpHideIso)
                warnings.Add("Temporary Hide/Isolate is active. Elements/categories may be temporarily hidden.");
            if (isTmpProps)
                warnings.Add("Temporary View Properties Mode is enabled. The view may not reflect saved properties.");

            return obj;
        }

        private static JObject BuildCropDiagnostics(View view)
        {
            var obj = new JObject();

            try
            {
                obj["cropBoxActive"] = view.CropBoxActive;
                obj["cropBoxVisible"] = view.CropBoxVisible;
            }
            catch
            {
                obj["cropBoxActive"] = JValue.CreateNull();
                obj["cropBoxVisible"] = JValue.CreateNull();
            }

            try
            {
                var bb = view.CropBox;
                if (bb != null)
                {
                    var size = new JObject
                    {
                        ["x"] = ConvertFromInternalUnits(Math.Abs(bb.Max.X - bb.Min.X), UnitTypeId.Millimeters),
                        ["y"] = ConvertFromInternalUnits(Math.Abs(bb.Max.Y - bb.Min.Y), UnitTypeId.Millimeters),
                        ["z"] = ConvertFromInternalUnits(Math.Abs(bb.Max.Z - bb.Min.Z), UnitTypeId.Millimeters)
                    };
                    obj["cropBoxSizeMm"] = size;
                }
            }
            catch { /* optional */ }

            try
            {
                var mgr = view.GetCropRegionShapeManager();
                if (mgr != null)
                {
                    obj["cropRegionCanHaveShape"] = mgr.CanHaveShape;
                    obj["cropRegionShapeSet"] = mgr.ShapeSet;
                    try
                    {
                        var loops = mgr.GetCropShape();
                        if (loops != null && loops.Count > 0)
                        {
                            bool allValid = true;
                            foreach (var loop in loops)
                            {
                                try
                                {
                                    if (!mgr.IsCropRegionShapeValid(loop))
                                        allValid = false;
                                }
                                catch
                                {
                                    allValid = false;
                                }
                            }

                            obj["cropRegionShapeLoopCount"] = loops.Count;
                            obj["cropRegionShapeValid"] = allValid;
                        }
                        else
                        {
                            obj["cropRegionShapeLoopCount"] = 0;
                            obj["cropRegionShapeValid"] = JValue.CreateNull();
                        }
                    }
                    catch
                    {
                        obj["cropRegionShapeValid"] = JValue.CreateNull();
                    }
                }
            }
            catch { /* optional */ }

            return obj;
        }

        private static HashSet<int> ResolveCategoryIds(Document doc, JObject p, bool includeAllCategories)
        {
            var set = new HashSet<int>();
            if (doc == null) return set;

            // Caller-provided categoryIds
            if (p?["categoryIds"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    int id = 0;
                    if (t.Type == JTokenType.Integer) id = t.Value<int>();
                    else if (t.Type == JTokenType.String) int.TryParse(t.Value<string>(), out id);
                    if (id != 0) set.Add(id);
                }
            }

            // Default key categories (only if not includeAllCategories and no explicit categoryIds)
            if (!includeAllCategories && set.Count == 0)
            {
                foreach (var id in GetDefaultKeyCategoryIds(doc))
                    set.Add(id);
            }

            return set;
        }

        private static IEnumerable<int> GetDefaultKeyCategoryIds(Document doc)
        {
            var ids = new List<int>();
            TryAddBic(doc, BuiltInCategory.OST_Walls, ids);
            TryAddBic(doc, BuiltInCategory.OST_Floors, ids);
            TryAddBic(doc, BuiltInCategory.OST_Roofs, ids);
            TryAddBic(doc, BuiltInCategory.OST_Ceilings, ids);
            TryAddBic(doc, BuiltInCategory.OST_Doors, ids);
            TryAddBic(doc, BuiltInCategory.OST_Windows, ids);
            TryAddBic(doc, BuiltInCategory.OST_Rooms, ids);
            TryAddBic(doc, BuiltInCategory.OST_RoomTags, ids);
            TryAddBic(doc, BuiltInCategory.OST_Areas, ids);
            TryAddBic(doc, BuiltInCategory.OST_AreaTags, ids);
            TryAddBic(doc, BuiltInCategory.OST_MEPSpaces, ids);
            TryAddBic(doc, BuiltInCategory.OST_MEPSpaceTags, ids);
            TryAddBic(doc, BuiltInCategory.OST_StructuralColumns, ids);
            TryAddBic(doc, BuiltInCategory.OST_Columns, ids);
            TryAddBic(doc, BuiltInCategory.OST_StructuralFraming, ids);
            TryAddBic(doc, BuiltInCategory.OST_GenericModel, ids);
            TryAddBic(doc, BuiltInCategory.OST_Furniture, ids);
            TryAddBic(doc, BuiltInCategory.OST_PlumbingFixtures, ids);
            TryAddBic(doc, BuiltInCategory.OST_LightingFixtures, ids);
            return ids.Distinct();
        }

        private static void TryAddBic(Document doc, BuiltInCategory bic, List<int> ids)
        {
            try
            {
                var cat = Category.GetCategory(doc, bic);
                if (cat != null)
                    ids.Add(cat.Id.IntValue());
            }
            catch { /* ignore */ }
        }

        private static JArray BuildCategoryStates(Document doc, View view, HashSet<int> requestedIds, bool includeAllCategories)
        {
            var arr = new JArray();
            if (doc == null || view == null) return arr;

            IEnumerable<Category> cats;
            if (includeAllCategories)
            {
                cats = doc.Settings.Categories.Cast<Category>();
            }
            else
            {
                cats = requestedIds
                    .Select(id =>
                    {
                        try { return Category.GetCategory(doc, Autodesk.Revit.DB.ElementIdCompat.From(id)); }
                        catch { return null; }
                    })
                    .Where(c => c != null)
                    .Cast<Category>();
            }

            foreach (var c in cats)
            {
                if (c == null) continue;
                int cid = c.Id.IntValue();
                if (!includeAllCategories && requestedIds.Count > 0 && !requestedIds.Contains(cid)) continue;

                bool canBeHidden = false;
                bool? visible = null;
                try { canBeHidden = view.CanCategoryBeHidden(c.Id); } catch { canBeHidden = false; }
                try { visible = !view.GetCategoryHidden(c.Id); } catch { visible = null; }

                arr.Add(new JObject
                {
                    ["categoryId"] = cid,
                    ["name"] = c.Name ?? "",
                    ["canBeHidden"] = canBeHidden,
                    ["visible"] = visible.HasValue ? (JToken)new JValue(visible.Value) : JValue.CreateNull()
                });
            }

            return arr;
        }

        private static bool SafeBool(Func<bool> getter)
        {
            try { return getter(); } catch { return false; }
        }

        private static int SafeInt(Func<int> getter)
        {
            try { return getter(); } catch { return 0; }
        }

        private static string SafeString(Func<string> getter)
        {
            try { return getter() ?? string.Empty; } catch { return string.Empty; }
        }
    }
}
