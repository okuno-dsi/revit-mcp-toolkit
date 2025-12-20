namespace RevitMCPAddin.Commands.ElementOps.Mass
{
    /// <summary>
    /// DTO for returning Mass (FamilyInstance or DirectShape) element information.
    /// </summary>
    public class MassElementInfo
    {
        public int ElementId { get; set; }
        public string ElementType { get; set; } = string.Empty;
        public int? TypeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? LevelId { get; set; }
        public LocationInfo? Location { get; set; }
    }

    /// <summary>
    /// DTO for a 3D point location.
    /// </summary>
    public class LocationInfo
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}
