using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RevitMCP.Abstractions.Models
{
    /// <summary>
    /// JSON-RPC 2.0 リクエストモデル
    /// </summary>
    public class RpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        [JsonProperty("jsonrpc")]
        public string Jsonrpc { get; set; } = "2.0";

        [JsonPropertyName("method")]
        [JsonProperty("method")]
        public string Method { get; set; } = string.Empty;

        [JsonPropertyName("params")]
        [JsonProperty("params")]
        public JsonElement? Params { get; set; }

        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public JsonElement? Id { get; set; }
    }
}
