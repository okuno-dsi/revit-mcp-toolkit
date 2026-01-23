// Models/RpcRequest.cs – JSON-RPC 2.0 リクエストモデル
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RevitMCPServer.Models
{
    /// <summary>
    /// JSON-RPC 2.0 リクエストを表すモデル
    /// </summary>
    public class RpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string Jsonrpc { get; set; } = "2.0";

        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        [JsonPropertyName("params")]
        public JsonElement? Params { get; set; }

        [JsonPropertyName("id")]
        public JsonElement? Id { get; set; }
    }
}
