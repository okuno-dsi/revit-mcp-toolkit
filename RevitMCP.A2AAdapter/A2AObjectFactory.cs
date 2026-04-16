using System.Text.Json.Nodes;

namespace RevitMCP.A2AAdapter;

public static class A2AObjectFactory
{
    public static JsonObject TextMessage(string role, string text, string? taskId = null, string? contextId = null)
    {
        var message = new JsonObject
        {
            ["role"] = role,
            ["parts"] = new JsonArray
            {
                new JsonObject
                {
                    ["kind"] = "text",
                    ["text"] = text
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(taskId))
            message["taskId"] = taskId;
        if (!string.IsNullOrWhiteSpace(contextId))
            message["contextId"] = contextId;

        return message;
    }

    public static JsonObject DataArtifact(string name, JsonNode? data)
    {
        return new JsonObject
        {
            ["artifactId"] = Guid.NewGuid().ToString("N"),
            ["name"] = name,
            ["parts"] = new JsonArray
            {
                new JsonObject
                {
                    ["kind"] = "data",
                    ["data"] = data?.DeepClone() ?? new JsonObject()
                }
            }
        };
    }

    public static JsonObject ToTask(A2ATaskRecord task, bool includeArtifacts = true, bool includeHistory = true)
    {
        var statusMessage = task.State switch
        {
            A2AStates.Completed => TextMessage("agent", "Revit MCP request completed.", task.Id, task.ContextId),
            A2AStates.Failed => TextMessage("agent", task.ErrorMessage ?? "Revit MCP request failed.", task.Id, task.ContextId),
            A2AStates.Canceled => TextMessage("agent", "Task was canceled locally in the A2A adapter. The Revit MCP queued job may already have run.", task.Id, task.ContextId),
            A2AStates.Rejected => TextMessage("agent", task.ErrorMessage ?? "Task rejected.", task.Id, task.ContextId),
            _ => TextMessage("agent", "Revit MCP request is being processed.", task.Id, task.ContextId)
        };

        var obj = new JsonObject
        {
            ["id"] = task.Id,
            ["contextId"] = task.ContextId,
            ["status"] = new JsonObject
            {
                ["state"] = task.State,
                ["message"] = statusMessage,
                ["timestamp"] = task.UpdatedUtc
            },
            ["metadata"] = new JsonObject
            {
                ["revitMethod"] = task.RevitMethod,
                ["revitJobId"] = task.RevitJobId,
                ["createdUtc"] = task.CreatedUtc,
                ["updatedUtc"] = task.UpdatedUtc
            }
        };

        if (includeArtifacts && task.ResultPayload != null)
        {
            obj["artifacts"] = new JsonArray
            {
                DataArtifact("revit-mcp-result", task.ResultPayload)
            };
        }

        if (includeHistory && task.History.Count > 0)
        {
            var history = new JsonArray();
            foreach (var item in task.History)
                history.Add(item.DeepClone());
            obj["history"] = history;
        }

        return obj;
    }
}
