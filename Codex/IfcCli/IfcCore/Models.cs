using System;
using System.Collections.Generic;

namespace IfcCore;

// Profile definition -------------------------------------------------

public class ProfileDefinition
{
    public string ProfileName { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = "1.0.0";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<string> SourceFiles { get; set; } = new();
    public Dictionary<string, EntityRule> EntityRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class EntityRule
{
    public List<RequiredPropertyRule> RequiredProperties { get; set; } = new();
}

public class RequiredPropertyRule
{
    public string Pset { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double MinFillRate { get; set; }
}

// Check result -------------------------------------------------------

public class CheckResult
{
    public bool Ok { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public string TargetFile { get; set; } = string.Empty;
    public CheckSummary Summary { get; set; } = new();
    public List<CheckItem> Items { get; set; } = new();
}

public class CheckSummary
{
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
}

public class CheckItem
{
    public string Severity { get; set; } = "error";
    public string EntityName { get; set; } = string.Empty;
    public string IfcGuid { get; set; } = string.Empty;
    public string Pset { get; set; } = string.Empty;
    public string Property { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

// IFC model ----------------------------------------------------------

public class IfcModel
{
    public string SourcePath { get; set; } = string.Empty;
    public Dictionary<int, IfcEntity> EntitiesById { get; } = new();
    public Dictionary<string, List<IfcEntity>> EntitiesByType { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddEntity(IfcEntity e)
    {
        EntitiesById[e.Id] = e;
        if (!EntitiesByType.TryGetValue(e.IfcType, out var list))
        {
            list = new List<IfcEntity>();
            EntitiesByType[e.IfcType] = list;
        }
        list.Add(e);
    }
}

public class IfcEntity
{
    public int Id { get; set; }
    public string IfcType { get; set; } = string.Empty;
    public string GlobalId { get; set; } = string.Empty;

    /// <summary>
    /// Optional display name (Name attribute on most IfcRoot-derived entities).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional: the building storey this element is contained in (via IfcRelContainedInSpatialStructure).
    /// </summary>
    public int? StoreyId { get; set; }

    /// <summary>
    /// Optional: name of the building storey (e.g. "1FL", "2FL") this element belongs to.
    /// </summary>
    public string StoreyName { get; set; } = string.Empty;

    public Dictionary<PropertyKey, bool> Properties { get; } = new();
}

public readonly struct PropertyKey : IEquatable<PropertyKey>
{
    public string Pset { get; }
    public string Prop { get; }

    public PropertyKey(string pset, string prop)
    {
        Pset = pset ?? string.Empty;
        Prop = prop ?? string.Empty;
    }

    public bool Equals(PropertyKey other) =>
        string.Equals(Pset, other.Pset, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Prop, other.Prop, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is PropertyKey other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Pset) * 397 ^
        StringComparer.OrdinalIgnoreCase.GetHashCode(Prop);
}

// Analysis stats -----------------------------------------------------

public class AnalysisResult
{
    public List<string> SourceFiles { get; set; } = new();
    public Dictionary<string, EntityStats> Entities { get; } = new(StringComparer.OrdinalIgnoreCase);

    public EntityStats GetOrAdd(string ifcType)
    {
        if (!Entities.TryGetValue(ifcType, out var s))
        {
            s = new EntityStats();
            Entities[ifcType] = s;
        }
        return s;
    }
}

public class EntityStats
{
    public int InstanceCount { get; set; }
    public Dictionary<PropertyKey, PropertyStats> Properties { get; } = new();

    public PropertyStats GetOrAdd(PropertyKey key)
    {
        if (!Properties.TryGetValue(key, out var s))
        {
            s = new PropertyStats();
            Properties[key] = s;
        }
        return s;
    }
}

public class PropertyStats
{
    private readonly HashSet<int> _seen = new();
    private readonly HashSet<int> _withValue = new();

    public int EntityCount => _seen.Count;
    public int ValueCount => _withValue.Count;
    public double FillRate => EntityCount == 0 ? 0.0 : (double)ValueCount / EntityCount;

    public void Register(int entityId, bool hasValue)
    {
        if (!_seen.Add(entityId))
        {
            if (hasValue) _withValue.Add(entityId);
            return;
        }
        if (hasValue) _withValue.Add(entityId);
    }
}
