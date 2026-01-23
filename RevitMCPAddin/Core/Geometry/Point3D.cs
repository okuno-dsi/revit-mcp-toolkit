#nullable enable
using Newtonsoft.Json;

namespace RevitMCPAddin.Models
{
    /// <summary>
    /// 3次元位置ベクトル（サーバー側 Point3D とJSON互換）
    /// </summary>
    public class Point3D
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("z")]
        public double Z { get; set; }

        [JsonProperty("unit")]
        public string Unit { get; set; } = "mm";

        public Point3D() { }

        public Point3D(double x, double y, double z)
        {
            X = x; Y = y; Z = z;
        }
    }
}
