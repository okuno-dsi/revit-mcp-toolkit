namespace RhinoMcpServer.Models
{
    public class MeshPacket
    {
        public string uniqueId { get; set; } = "";
        public double[][] transform { get; set; } = new double[4][];
        public string units { get; set; } = "feet";
        public double[][] vertices { get; set; } = new double[0][];
        public Submesh[] submeshes { get; set; } = new Submesh[0];
        public MaterialSlot[] materials { get; set; } = new MaterialSlot[0];
        public string snapshotStamp { get; set; } = "";
        public string geomHash { get; set; } = "";
    }

    public class Submesh { public int materialKey { get; set; } public int[] indices { get; set; } = new int[0]; }
    public class MaterialSlot { public int materialKey { get; set; } public string name { get; set; } = ""; public int[] color { get; set; } = new int[3]; public double transparency { get; set; } }
}
