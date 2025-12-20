using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RevitMCP.Abstractions.Models
{
    /// <summary>機械可読＆人間可読のエラー情報</summary>
    public sealed class ErrorInfo
    {
        /// <summary>短い英語コード（例: NOT_FOUND / NO_LOCATION / MOVE_PINNED）</summary>
        [JsonPropertyName("code")]
        [JsonProperty("code")]
        public string? Code { get; set; }

        /// <summary>機械が扱いやすい補足（どう直すと良いか、等）</summary>
        [JsonPropertyName("hint")]
        [JsonProperty("hint")]
        public string? Hint { get; set; }

        /// <summary>人間向けの短い説明（日本語OK）</summary>
        [JsonPropertyName("humanMessage")]
        [JsonProperty("humanMessage")]
        public string? HumanMessage { get; set; }

        /// <summary>例外詳細など任意の付加情報</summary>
        [JsonPropertyName("details")]
        [JsonProperty("details")]
        public object? Details { get; set; }
    }
}
