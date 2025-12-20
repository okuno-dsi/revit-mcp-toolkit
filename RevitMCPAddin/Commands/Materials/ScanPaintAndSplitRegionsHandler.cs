#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using RevitMCPAddin.Core.Dto;

namespace RevitMCPAddin.Commands.Materials
{
    /// <summary>
    /// scan_paint_and_split_regions
    /// プロジェクト内の要素を走査し、塗装された面または Split Face 領域を持つ要素を列挙します。
    /// </summary>
    public class ScanPaintAndSplitRegionsHandler : IRevitCommandHandler
    {
        public string CommandName => "scan_paint_and_split_regions";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            try
            {
                var p = (JObject?)(cmd.Params ?? new JObject()) ?? new JObject();

                var req = new ScanPaintAndRegionsRequest
                {
                    Categories = p["categories"] is JArray cats
                        ? cats.OfType<JValue>().Select(v => v.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                        : null,
                    IncludeFaceRefs = p.Value<bool?>("includeFaceRefs"),
                    MaxFacesPerElement = p.Value<int?>("maxFacesPerElement"),
                    Limit = p.Value<int?>("limit")
                };

                bool includeFaceRefs = req.IncludeFaceRefs ?? false;
                int maxFacesPerElement = req.MaxFacesPerElement.HasValue && req.MaxFacesPerElement.Value > 0
                    ? req.MaxFacesPerElement.Value
                    : 50;
                int limit = req.Limit ?? 0;

                // Resolve categories
                var bicList = CategoryResolver.ResolveCategories(req.Categories);
                if (bicList.Count == 0)
                {
                    // Fallback to default paintable model categories
                    bicList = CategoryResolver.DefaultPaintableModelCategories();
                }

                var items = new List<PaintAndRegionElementInfo>();

                foreach (var bic in bicList)
                {
                    try
                    {
                        var cat = doc.Settings.Categories.get_Item(bic);
                        if (cat == null || cat.CategoryType != CategoryType.Model)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        // Category not available in this document; skip silently
                        continue;
                    }

                    var col = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToElements();

                    foreach (var e in col)
                    {
                        if (limit > 0 && items.Count >= limit)
                            break;

                        try
                        {
                            var info = ElementPaintAndRegionUtils.ScanElement(doc, e, includeFaceRefs, maxFacesPerElement);
                            if (info != null)
                            {
                                items.Add(info);
                                if (limit > 0 && items.Count >= limit)
                                    break;
                            }
                        }
                        catch
                        {
                            // Per-element errors are ignored to keep the scan robust.
                        }
                    }

                    if (limit > 0 && items.Count >= limit)
                        break;
                }

                if (items.Count == 0)
                {
                    return new ScanPaintAndRegionsResult
                    {
                        ok = true,
                        msg = "No elements with paint or split regions were found.",
                        items = new List<PaintAndRegionElementInfo>()
                    };
                }

                return new ScanPaintAndRegionsResult
                {
                    ok = true,
                    msg = null,
                    items = items
                };
            }
            catch (Exception ex)
            {
                return new ScanPaintAndRegionsResult
                {
                    ok = false,
                    msg = ex.Message,
                    items = new List<PaintAndRegionElementInfo>()
                };
            }
        }
    }
}

