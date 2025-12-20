#nullable enable
using System.Collections.Generic;

namespace RevitMCPAddin.Core.Dto
{
    public class ScanPaintAndRegionsRequest
    {
        public List<string>? Categories { get; set; }
        public bool? IncludeFaceRefs { get; set; }
        public int? MaxFacesPerElement { get; set; }
        public int? Limit { get; set; }
    }

    public class PaintOrRegionFaceInfo
    {
        public string? StableReference { get; set; }
        public bool HasPaint { get; set; }
        public bool HasRegions { get; set; }
        public int RegionCount { get; set; }
    }

    public class PaintAndRegionElementInfo
    {
        public int ElementId { get; set; }
        public string? UniqueId { get; set; }
        public string? Category { get; set; }

        public bool HasPaint { get; set; }
        public bool HasSplitRegions { get; set; }

        public List<PaintOrRegionFaceInfo>? Faces { get; set; }
    }

    public class ScanPaintAndRegionsResult
    {
        public bool ok { get; set; }
        public string? msg { get; set; }
        public List<PaintAndRegionElementInfo> items { get; set; } = new List<PaintAndRegionElementInfo>();
    }
}
