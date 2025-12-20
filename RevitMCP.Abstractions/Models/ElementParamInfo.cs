using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RevitMCP.Abstractions.Models
{
    /// <summary>
    /// 要素パラメータ情報
    /// </summary>
    public class ElementParamInfo
    {
        [JsonPropertyName("paramName")]
        [JsonProperty("paramName")]
        public string ParamName { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        [JsonProperty("value")]
        public object? Value { get; set; }

        [JsonPropertyName("storageType")]
        [JsonProperty("storageType")]
        public string? StorageType { get; set; }

        [JsonPropertyName("isReadOnly")]
        [JsonProperty("isReadOnly")]
        public bool IsReadOnly { get; set; }
    }
}
