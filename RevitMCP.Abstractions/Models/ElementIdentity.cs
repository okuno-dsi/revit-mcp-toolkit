using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RevitMCP.Abstractions.Models
{
    public class ElementIdentity
    {
        [JsonPropertyName("uniqueId")]
        [JsonProperty("uniqueId")]
        public string? UniqueId { get; set; }

        [JsonPropertyName("elementId")]
        [JsonProperty("elementId")]
        public int ElementId { get; set; }

        [JsonPropertyName("category")]
        [JsonProperty("category")]
        public string? Category { get; set; }

        [JsonPropertyName("familyName")]
        [JsonProperty("familyName")]
        public string? FamilyName { get; set; }

        [JsonPropertyName("typeName")]
        [JsonProperty("typeName")]
        public string? TypeName { get; set; }

        [JsonPropertyName("levelName")]
        [JsonProperty("levelName")]
        public string? LevelName { get; set; }

        [JsonPropertyName("isPinned")]
        [JsonProperty("isPinned")]
        public bool? IsPinned { get; set; }

        [JsonPropertyName("documentPath")]
        [JsonProperty("documentPath")]
        public string? DocumentPath { get; set; }

        [JsonPropertyName("documentKind")]
        [JsonProperty("documentKind")]
        public string? DocumentKind { get; set; }
    }
}
