using System.Collections.Generic;

namespace RevitMCPAddin.Models
{
    /// <summary>
    /// Request for forgiving keyword-based element discovery.
    /// Intended to be populated from JSON (Newtonsoft).
    /// </summary>
    public sealed class SearchElementsRequest
    {
        public string Keyword { get; set; } = "";
        public List<string> Categories { get; set; } = new List<string>();
        public int? ViewId { get; set; }
        public int? LevelId { get; set; }
        public bool IncludeTypes { get; set; } = false;
        public bool CaseSensitive { get; set; } = false;
        public int MaxResults { get; set; } = 50;
    }

    /// <summary>
    /// Request for structured element queries.
    /// Intended to be populated from JSON (Newtonsoft).
    /// </summary>
    public sealed class QueryElementsRequest
    {
        public QueryScope Scope { get; set; } = new QueryScope();
        public QueryFilters Filters { get; set; } = new QueryFilters();
        public QueryOptions Options { get; set; } = new QueryOptions();
    }

    public sealed class QueryScope
    {
        public int? ViewId { get; set; }
        public bool IncludeHiddenInView { get; set; } = false;
    }

    public sealed class QueryFilters
    {
        public List<string> Categories { get; set; } = new List<string>();
        public List<string> ClassNames { get; set; } = new List<string>();
        public int? LevelId { get; set; }
        public NameFilter Name { get; set; }
        public BBoxFilter BBox { get; set; }
        public List<ParameterCondition> Parameters { get; set; } = new List<ParameterCondition>();
    }

    public sealed class QueryOptions
    {
        public bool IncludeElementType { get; set; } = true;
        public bool IncludeBoundingBox { get; set; } = false;
        public List<string> IncludeParameters { get; set; } = new List<string>();
        public int MaxResults { get; set; } = 200;
        public string OrderBy { get; set; } = "id"; // id|name
    }

    public sealed class NameFilter
    {
        public string Mode { get; set; } = "contains"; // equals|contains|startsWith|endsWith|regex
        public string Value { get; set; } = "";
        public bool CaseSensitive { get; set; } = false;
    }

    public sealed class BBoxFilter
    {
        public Point3D Min { get; set; } = new Point3D();
        public Point3D Max { get; set; } = new Point3D();
        public string Mode { get; set; } = "intersects"; // intersects|inside
    }

    public sealed class ParameterCondition
    {
        public ParamRef Param { get; set; } = new ParamRef();
        public string Op { get; set; } = "equals"; // equals|contains|range|gt|gte|lt|lte|startsWith|endsWith
        public string Value { get; set; } = "";
        public double? Number { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public string Unit { get; set; } = "ft"; // optional; for Double comparisons (length-only in first version)
        public bool CaseSensitive { get; set; } = false;
    }

    public sealed class ParamRef
    {
        public string Kind { get; set; } = "name"; // builtin|name|guid|paramId
        public string Value { get; set; } = "";
    }
}
