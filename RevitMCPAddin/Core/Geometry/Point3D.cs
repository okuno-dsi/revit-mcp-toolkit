#nullable enable
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RevitMCPAddin.Models
{
    /// <summary>
    /// 3次元位置ベクトル（サーバー側 Point3D とJSON互換）
    /// </summary>
    public class Point3D
    {
        [JsonPropertyName("x")]
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        [JsonProperty("z")]
        public double Z { get; set; }

        public Point3D() { }

        public Point3D(double x, double y, double z)
        {
            X = x; Y = y; Z = z;
        }
    }
}
