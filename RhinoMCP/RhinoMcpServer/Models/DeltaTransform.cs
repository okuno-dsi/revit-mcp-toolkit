namespace RhinoMcpServer.Models
{
    public class DeltaTransform
    {
        public double[] translate { get; set; } = new double[3]; // feet
        public double rotateZDeg { get; set; } = 0.0;
    }
}
