using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RevitMCP.Abstractions.Models
{
    /// <summary>単位宣言（入力／内部）</summary>
    public sealed class UnitsSpec
    {
        /// <summary>長さ単位（例: "mm" / "m" / "ft"）</summary>
        [JsonPropertyName("length")]
        [JsonProperty("length")]
        public string Length { get; set; } = "mm";

        /// <summary>角度単位（例: "deg" / "rad"）</summary>
        [JsonPropertyName("angle")]
        [JsonProperty("angle")]
        public string Angle { get; set; } = "deg";
    }
}
