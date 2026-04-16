using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitMCP.A2AAdapter;

public static class JsonRpcUtil
{
    public static JsonObject Success(JsonNode? id, JsonNode? result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result?.DeepClone() ?? new JsonObject()
        };
    }

    public static JsonObject Error(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var err = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data != null)
            err["data"] = data.DeepClone();

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = err
        };
    }

    public static JsonNode? CloneId(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var id))
            return null;
        return JsonNode.Parse(id.GetRawText());
    }

    public static string? GetMethod(JsonElement root)
    {
        if (!root.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
            return null;
        return method.GetString();
    }

    public static JsonNode? ParamsAsNode(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters))
            return null;
        return JsonNode.Parse(parameters.GetRawText());
    }

    public static JsonNode? ToNode(object value, JsonSerializerOptions options)
    {
        return JsonSerializer.SerializeToNode(value, value.GetType(), options);
    }
}
