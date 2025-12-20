// RevitMCPAddin/Core/Dto/ObbDtos.cs
namespace RevitMCPAddin.Core.Dto
{
    public sealed class Point3D
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public Point3D() { }
        public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    public sealed class OrientedBoundingBoxDto
    {
        public Point3D Center { get; set; }
        public Point3D AxisX { get; set; }
        public Point3D AxisY { get; set; }
        public Point3D AxisZ { get; set; }
        public double ExtentX { get; set; }
        public double ExtentY { get; set; }
        public double ExtentZ { get; set; }
        public System.Collections.Generic.List<Point3D> Corners { get; set; } = new System.Collections.Generic.List<Point3D>(8);
        public double Volume { get; set; }
        public string Notes { get; set; }
    }

    public sealed class GetObbRequest
    {
        public long ElementId { get; set; }
        public string Strategy { get; set; } = "auto";
        public string DetailLevel { get; set; } = "fine";
        public bool IncludeCorners { get; set; } = true;
    }

    public sealed class ObbResponse
    {
        public bool Ok { get; set; }
        public string Msg { get; set; }
        public OrientedBoundingBoxDto Obb { get; set; }
    }
}
