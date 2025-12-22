#nullable enable
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitMCPAddin.Core.Dto;

namespace RevitMCPAddin.Core
{
    public static class ElementPaintAndRegionUtils
    {
        private static bool IsFacePaintedSafe(Document doc, Element elem, Face face)
        {
            try
            {
                return doc.IsPainted(elem.Id, face);
            }
            catch
            {
                // Some categories / elements cannot be painted, or the face is invalid.
                // Treat as "not painted" but never fail the command.
                return false;
            }
        }

        private static int GetRegionCountSafe(Face face)
        {
            try
            {
                if (!face.HasRegions)
                    return 0;

                var regions = face.GetRegions();
                return regions?.Count ?? 0;
            }
            catch
            {
                // If anything goes wrong, just return 0 – the element is still usable.
                return 0;
            }
        }

        /// <summary>
        /// Scans a single element and returns paint/split info.
        /// Returns null if the element has neither paint nor split regions.
        /// </summary>
        public static PaintAndRegionElementInfo? ScanElement(
            Document doc,
            Element elem,
            bool includeFaceRefs,
            int maxFacesPerElement)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (elem == null) throw new ArgumentNullException(nameof(elem));

            var opt = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Medium,
                IncludeNonVisibleObjects = true
            };

            var geom = elem.get_Geometry(opt);
            if (geom == null)
                return null;

            var info = new PaintAndRegionElementInfo
            {
                ElementId = elem.Id.IntValue(),
                UniqueId = elem.UniqueId,
                Category = elem.Category?.Name,
                HasPaint = false,
                HasSplitRegions = false,
                Faces = includeFaceRefs ? new List<PaintOrRegionFaceInfo>() : null
            };

            foreach (var gObj in geom)
            {
                var solid = gObj as Solid;
                if (solid == null || solid.Faces == null || solid.Faces.Size == 0)
                    continue;

                foreach (Face face in solid.Faces)
                {
                    bool hasPaint = IsFacePaintedSafe(doc, elem, face);
                    bool hasRegions = face.HasRegions;
                    int regionCount = hasRegions ? GetRegionCountSafe(face) : 0;

                    if (!hasPaint && !hasRegions)
                        continue;

                    info.HasPaint |= hasPaint;
                    info.HasSplitRegions |= hasRegions;

                    if (!includeFaceRefs)
                        continue;

                    if (info.Faces == null)
                        info.Faces = new List<PaintOrRegionFaceInfo>();

                    if (info.Faces.Count >= maxFacesPerElement)
                        continue;

                    string? stableRef = null;
                    try
                    {
                        var refFace = face.Reference;
                        stableRef = refFace != null
                            ? refFace.ConvertToStableRepresentation(doc)
                            : null;
                    }
                    catch
                    {
                        // Ignore stable ref failures; keep element-level info.
                    }

                    info.Faces.Add(new PaintOrRegionFaceInfo
                    {
                        StableReference = stableRef,
                        HasPaint = hasPaint,
                        HasRegions = hasRegions,
                        RegionCount = regionCount
                    });
                }
            }

            if (!info.HasPaint && !info.HasSplitRegions)
                return null;

            return info;
        }
    }
}

