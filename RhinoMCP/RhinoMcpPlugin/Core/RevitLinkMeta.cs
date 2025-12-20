namespace RhinoMcpPlugin.Core
{
    public class RevitLinkMeta
    {
        public string UniqueId { get; set; } = "";
        public string SnapshotStamp { get; set; } = "";
        public string GeomHash { get; set; } = "";
        public string Units { get; set; } = "feet";
        public double ScaleToRhino { get; set; } = 304.8; // ft -> mm
    }
}
