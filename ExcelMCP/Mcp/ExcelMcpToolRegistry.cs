using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class ExcelMcpToolRegistry
{
    private readonly List<ExcelMcpToolDefinition> _tools;
    private readonly Dictionary<string, ExcelMcpToolDefinition> _byName;

    private ExcelMcpToolRegistry(IEnumerable<ExcelMcpToolDefinition> tools)
    {
        _tools = tools
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _byName = _tools.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static ExcelMcpToolRegistry Load(string contentRootPath)
    {
        var tools = new List<ExcelMcpToolDefinition>();
        tools.AddRange(LoadRestTools(contentRootPath));
        tools.AddRange(CreateManualTools());
        return new ExcelMcpToolRegistry(tools);
    }

    public bool TryGet(string name, out ExcelMcpToolDefinition tool) =>
        _byName.TryGetValue(name, out tool!);

    public IReadOnlyList<object> ListTools(string? cursor, out string? nextCursor)
    {
        const int pageSize = 100;
        var offset = DecodeCursor(cursor);
        var page = _tools.Skip(offset).Take(pageSize).ToList();
        nextCursor = offset + page.Count < _tools.Count ? EncodeCursor(offset + page.Count) : null;

        return page.Select(ToToolDescriptor).Cast<object>().ToList();
    }

    public string? ValidateArguments(ExcelMcpToolDefinition tool, JsonObject args)
    {
        var schema = tool.InputSchema;
        if (schema["required"] is JsonArray required)
        {
            foreach (var nameNode in required)
            {
                var name = nameNode?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                if (!args.TryGetPropertyValue(name, out var value) || value is null)
                    return $"Missing required argument: {name}";
            }
        }

        var additionalPropertiesAllowed = schema["additionalProperties"]?.GetValue<bool?>() ?? true;
        var properties = schema["properties"] as JsonObject;
        if (!additionalPropertiesAllowed && properties is not null)
        {
            foreach (var key in args.Select(x => x.Key))
            {
                if (!properties.ContainsKey(key))
                    return $"Unknown argument: {key}";
            }
        }

        if (properties is not null)
        {
            foreach (var property in properties)
            {
                if (!args.TryGetPropertyValue(property.Key, out var argValue) || argValue is null)
                    continue;
                if (property.Value is not JsonObject propSchema)
                    continue;

                var typeError = ValidateNodeType(property.Key, argValue, propSchema);
                if (!string.IsNullOrWhiteSpace(typeError))
                    return typeError;

                if (propSchema["enum"] is JsonArray enumValues)
                {
                    var actual = argValue.GetValue<string?>();
                    if (actual is not null)
                    {
                        var allowed = enumValues
                            .Select(x => x?.GetValue<string>())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (allowed.Count > 0 && !allowed.Contains(actual))
                            return $"Invalid value for {property.Key}: '{actual}'. Allowed: {string.Join(", ", allowed.OrderBy(x => x))}";
                    }
                }
            }
        }

        return null;
    }

    private static IEnumerable<ExcelMcpToolDefinition> LoadRestTools(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "mcp_commands.jsonl");
        if (!File.Exists(path))
            yield break;

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var node = JsonNode.Parse(line) as JsonObject;
            if (node is null)
                continue;

            var name = node["name"]?.GetValue<string>();
            var method = node["method"]?.GetValue<string>()?.ToUpperInvariant();
            var endpointPath = node["path"]?.GetValue<string>();
            var description = node["description"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(endpointPath))
                continue;

            yield return new ExcelMcpToolDefinition
            {
                Name = name.StartsWith("excel.", StringComparison.OrdinalIgnoreCase) ? name : $"excel.{name}",
                Description = BuildToolDescription(description, method, endpointPath),
                HttpMethod = method,
                Path = endpointPath,
                InputSchema = NormalizeSchema(node["input_schema"] as JsonObject),
                OutputExample = node["output_example"] as JsonObject,
                Annotations = BuildAnnotations(method, endpointPath),
            };
        }
    }

    private static IEnumerable<ExcelMcpToolDefinition> CreateManualTools()
    {
        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.list_open_workbooks",
            Description = "List currently open Excel workbooks via COM. Use this before COM write/sort operations.",
            HttpMethod = "GET",
            Path = "/list_open_workbooks",
            InputSchema = EmptySchema(),
            Annotations = BuildAnnotations("GET", "/list_open_workbooks"),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.com.activate_workbook",
            Description = "Activate an already open Excel workbook by full path or workbook name.",
            HttpMethod = "POST",
            Path = "/com/activate_workbook",
            InputSchema = Schema(new[] { ("workbookFullName", StringSchema("Target workbook full path.")), ("workbookName", StringSchema("Target workbook file name.")) }, new[] { "workbookFullName" }, allowAdditionalProperties: false),
            Annotations = BuildAnnotations("POST", "/com/activate_workbook"),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.com.activate_sheet",
            Description = "Activate a sheet in an open workbook by sheet name or index.",
            HttpMethod = "POST",
            Path = "/com/activate_sheet",
            InputSchema = Schema(new[]
            {
                ("workbookFullName", StringSchema()),
                ("workbookName", StringSchema()),
                ("sheetName", StringSchema()),
                ("sheetIndex", IntegerSchema()),
            }, Array.Empty<string>(), allowAdditionalProperties: false),
            Annotations = BuildAnnotations("POST", "/com/activate_sheet"),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.com.read_cells",
            Description = "Read cells from an open workbook via COM automation. Prefer this when you must inspect the currently open workbook state.",
            HttpMethod = "POST",
            Path = "/com/read_cells",
            InputSchema = Schema(new[]
            {
                ("workbookFullName", StringSchema()),
                ("workbookName", StringSchema()),
                ("sheetName", StringSchema()),
                ("rangeA1", StringSchema("A1 range to read.")),
                ("useValue2", BooleanSchema()),
            }, new[] { "rangeA1" }, allowAdditionalProperties: false),
            Annotations = BuildAnnotations("POST", "/com/read_cells"),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.com.write_cells",
            Description = "Write a 2D value matrix into an already open workbook via COM automation.",
            HttpMethod = "POST",
            Path = "/com/write_cells",
            InputSchema = Schema(new[]
            {
                ("workbookFullName", StringSchema()),
                ("workbookName", StringSchema()),
                ("sheetName", StringSchema()),
                ("startCell", StringSchema()),
                ("values", ArraySchema("2D array of values.")),
            }, new[] { "startCell", "values" }, allowAdditionalProperties: false),
            Annotations = BuildAnnotations("POST", "/com/write_cells"),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.com.append_rows",
            Description = "Append rows into an already open workbook via COM automation.",
            HttpMethod = "POST",
            Path = "/com/append_rows",
            InputSchema = Schema(new[]
            {
                ("workbookFullName", StringSchema()),
                ("workbookName", StringSchema()),
                ("sheetName", StringSchema()),
                ("startColumn", StringSchema()),
                ("rows", ArraySchema("2D array of rows to append.")),
            }, new[] { "rows" }, allowAdditionalProperties: false),
            Annotations = BuildAnnotations("POST", "/com/append_rows"),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.com.save_workbook",
            Description = "Save the currently open workbook. Optionally provide SaveAsFullName to write a copy.",
            HttpMethod = "POST",
            Path = "/com/save_workbook",
            InputSchema = Schema(new[]
            {
                ("workbookFullName", StringSchema()),
                ("workbookName", StringSchema()),
                ("saveAsFullName", StringSchema()),
            }, Array.Empty<string>(), allowAdditionalProperties: false),
            Annotations = BuildAnnotations("POST", "/com/save_workbook"),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.com.format_range",
            Description = "Apply formatting to a range in an open workbook via COM automation.",
            HttpMethod = "POST",
            Path = "/com/format_range",
            InputSchema = Schema(new[]
            {
                ("workbookFullName", StringSchema()),
                ("workbookName", StringSchema()),
                ("sheetName", StringSchema()),
                ("target", StringSchema()),
                ("numberFormat", StringSchema()),
                ("horizontalAlign", StringSchema()),
                ("verticalAlign", StringSchema()),
                ("wrapText", BooleanSchema()),
                ("fontName", StringSchema()),
                ("fontSize", NumberSchema()),
                ("bold", BooleanSchema()),
                ("italic", BooleanSchema()),
                ("fontColor", StringSchema()),
                ("fillColor", StringSchema()),
                ("columnWidth", NumberSchema()),
                ("rowHeight", NumberSchema()),
                ("autoFitColumns", BooleanSchema()),
                ("autoFitRows", BooleanSchema()),
            }, new[] { "target" }, allowAdditionalProperties: false),
            Annotations = BuildAnnotations("POST", "/com/format_range"),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.com.add_sheet",
            Description = "Add a new sheet to an open workbook.",
            HttpMethod = "POST",
            Path = "/com/add_sheet",
            InputSchema = Schema(new[]
            {
                ("workbookFullName", StringSchema()),
                ("workbookName", StringSchema()),
                ("newSheetName", StringSchema()),
                ("beforeSheetName", StringSchema()),
                ("afterSheetName", StringSchema()),
            }, Array.Empty<string>(), allowAdditionalProperties: false),
            Annotations = BuildAnnotations("POST", "/com/add_sheet"),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.com.delete_sheet",
            Description = "Delete a sheet from an open workbook.",
            HttpMethod = "POST",
            Path = "/com/delete_sheet",
            InputSchema = Schema(new[]
            {
                ("workbookFullName", StringSchema()),
                ("workbookName", StringSchema()),
                ("sheetName", StringSchema()),
            }, new[] { "sheetName" }, allowAdditionalProperties: false),
            Annotations = BuildAnnotations("POST", "/com/delete_sheet"),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.com.sort_range",
            Description = "Sort a range in an open workbook using one or more keys.",
            HttpMethod = "POST",
            Path = "/com/sort_range",
            InputSchema = Schema(new[]
            {
                ("workbookFullName", StringSchema()),
                ("workbookName", StringSchema()),
                ("sheetName", StringSchema()),
                ("rangeA1", StringSchema()),
                ("hasHeader", BooleanSchema()),
                ("keys", ArraySchema("Sort keys list.")),
            }, new[] { "rangeA1", "keys" }, allowAdditionalProperties: false),
            Annotations = BuildAnnotations("POST", "/com/sort_range"),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.preview_write_cells",
            Description = "Preview a write_cells operation without changing the workbook. Returns range, row count, column count, and a sample payload.",
            InputSchema = Schema(new[]
            {
                ("excelPath", StringSchema()),
                ("sheetName", StringSchema()),
                ("startCell", StringSchema()),
                ("values", ArraySchema("2D array of values.")),
                ("treatNullAsClear", BooleanSchema()),
            }, new[] { "excelPath", "startCell", "values" }, allowAdditionalProperties: false),
            Annotations = new JsonObject
            {
                ["readOnlyHint"] = true,
                ["idempotentHint"] = true,
            },
            Handler = static (context, _) => Task.FromResult(PreviewWriteCells(context.Arguments)),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.preview_append_rows",
            Description = "Preview an append_rows operation without changing the workbook. Returns row count, column span, and a sample payload.",
            InputSchema = Schema(new[]
            {
                ("excelPath", StringSchema()),
                ("sheetName", StringSchema()),
                ("startColumn", StringSchema()),
                ("rows", ArraySchema("2D array of rows.")),
            }, new[] { "excelPath", "rows" }, allowAdditionalProperties: false),
            Annotations = new JsonObject
            {
                ["readOnlyHint"] = true,
                ["idempotentHint"] = true,
            },
            Handler = static (context, _) => Task.FromResult(PreviewAppendRows(context.Arguments)),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.preview_set_formula",
            Description = "Preview a set_formula operation without changing the workbook.",
            InputSchema = Schema(new[]
            {
                ("excelPath", StringSchema()),
                ("sheetName", StringSchema()),
                ("target", StringSchema()),
                ("formulaA1", StringSchema()),
            }, new[] { "excelPath", "target", "formulaA1" }, allowAdditionalProperties: false),
            Annotations = new JsonObject
            {
                ["readOnlyHint"] = true,
                ["idempotentHint"] = true,
            },
            Handler = static (context, _) => Task.FromResult(PreviewSetFormula(context.Arguments)),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "excel.api_call",
            Description = "Fallback tool for calling an existing ExcelMCP HTTP endpoint directly. Prefer the first-class excel.* tools when available.",
            InputSchema = Schema(new[]
            {
                ("path", StringSchema("Endpoint path such as /sheet_info or /com/read_cells.")),
                ("method", StringSchema("HTTP method. GET is default; POST for body-based operations.")),
                ("body", new JsonObject { ["type"] = "object" }),
            }, new[] { "path" }, allowAdditionalProperties: false),
            Annotations = new JsonObject
            {
                ["readOnlyHint"] = false,
                ["openWorldHint"] = false,
            },
            Handler = static (_, _) => throw new NotSupportedException("excel.api_call is handled explicitly by the MCP endpoint."),
        };

        yield return new ExcelMcpToolDefinition
        {
            Name = "mcp.status",
            Description = "Return MCP session status and negotiated protocol information.",
            InputSchema = EmptySchema(),
            Annotations = new JsonObject
            {
                ["readOnlyHint"] = true,
                ["idempotentHint"] = true,
            },
            Handler = static (_, _) => throw new NotSupportedException("mcp.status is handled explicitly by the MCP endpoint."),
        };
    }

    private static JsonObject ToToolDescriptor(ExcelMcpToolDefinition tool)
    {
        var obj = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["inputSchema"] = tool.InputSchema.DeepClone(),
        };

        if (tool.Annotations is not null)
            obj["annotations"] = tool.Annotations.DeepClone();

        return obj;
    }

    private static JsonObject NormalizeSchema(JsonObject? schema)
    {
        if (schema is null)
            return EmptySchema();

        var clone = schema.DeepClone() as JsonObject ?? new JsonObject();
        clone["type"] ??= "object";
        clone["properties"] ??= new JsonObject();
        clone["additionalProperties"] ??= false;
        return clone;
    }

    private static JsonObject EmptySchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["additionalProperties"] = false,
    };

    private static JsonObject Schema(IEnumerable<(string Name, JsonObject Schema)> properties, IEnumerable<string> required, bool allowAdditionalProperties)
    {
        var props = new JsonObject();
        foreach (var property in properties)
            props[property.Name] = property.Schema;

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(required.Select(x => JsonValue.Create(x)).ToArray()),
            ["additionalProperties"] = allowAdditionalProperties,
        };
    }

    private static JsonObject StringSchema(string? description = null)
    {
        var obj = new JsonObject { ["type"] = "string" };
        if (!string.IsNullOrWhiteSpace(description))
            obj["description"] = description;
        return obj;
    }

    private static JsonObject IntegerSchema(string? description = null)
    {
        var obj = new JsonObject { ["type"] = "integer" };
        if (!string.IsNullOrWhiteSpace(description))
            obj["description"] = description;
        return obj;
    }

    private static JsonObject NumberSchema(string? description = null)
    {
        var obj = new JsonObject { ["type"] = "number" };
        if (!string.IsNullOrWhiteSpace(description))
            obj["description"] = description;
        return obj;
    }

    private static JsonObject BooleanSchema(string? description = null)
    {
        var obj = new JsonObject { ["type"] = "boolean" };
        if (!string.IsNullOrWhiteSpace(description))
            obj["description"] = description;
        return obj;
    }

    private static JsonObject ArraySchema(string? description = null)
    {
        var obj = new JsonObject { ["type"] = "array" };
        if (!string.IsNullOrWhiteSpace(description))
            obj["description"] = description;
        return obj;
    }

    private static JsonObject BuildAnnotations(string method, string path)
    {
        var writeLike = IsWriteLike(method, path);
        return new JsonObject
        {
            ["readOnlyHint"] = !writeLike,
            ["idempotentHint"] = !writeLike,
            ["destructiveHint"] = path.Contains("delete", StringComparison.OrdinalIgnoreCase),
            ["openWorldHint"] = false,
        };
    }

    private static bool IsWriteLike(string method, string path)
    {
        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
            return false;

        var readOnlyPrefixes = new[] { "/health", "/sheet_info", "/read_cells", "/to_csv", "/to_json", "/list_charts", "/parse_plan", "/list_open_workbooks", "/com/read_cells" };
        return !readOnlyPrefixes.Contains(path, StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildToolDescription(string description, string method, string path)
    {
        var mode = IsWriteLike(method, path) ? "This can modify workbook state." : "This is read-only.";
        return $"{description} [{method} {path}] {mode}".Trim();
    }

    private static string? ValidateNodeType(string propertyName, JsonNode argValue, JsonObject schema)
    {
        var expectedType = schema["type"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(expectedType))
            return null;

        bool ok = expectedType switch
        {
            "string" => argValue is JsonValue && (argValue.GetValue<object?>() is string),
            "boolean" => argValue is JsonValue && (argValue.GetValue<object?>() is bool),
            "integer" => argValue is JsonValue && TryGetInteger(argValue),
            "number" => argValue is JsonValue && TryGetNumber(argValue),
            "object" => argValue is JsonObject,
            "array" => argValue is JsonArray,
            _ => true,
        };

        return ok ? null : $"Invalid type for {propertyName}: expected {expectedType}.";
    }

    private static bool TryGetInteger(JsonNode node)
    {
        try
        {
            _ = node.GetValue<int>();
            return true;
        }
        catch
        {
            try
            {
                _ = node.GetValue<long>();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static bool TryGetNumber(JsonNode node)
    {
        try
        {
            _ = node.GetValue<double>();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? EncodeCursor(int offset) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(offset.ToString()));

    private static int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return int.TryParse(raw, out var offset) ? Math.Max(0, offset) : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static ExcelMcpToolExecutionResult PreviewWriteCells(JsonObject args)
    {
        var values = args["values"] as JsonArray ?? new JsonArray();
        var rowCount = values.Count;
        var colCount = values
            .Select(x => x as JsonArray)
            .Where(x => x is not null)
            .Select(x => x!.Count)
            .DefaultIfEmpty(0)
            .Max();

        return new ExcelMcpToolExecutionResult
        {
            Payload = JsonSerializer.SerializeToNode(new
            {
                ok = true,
                preview = true,
                operation = "write_cells",
                excelPath = args["excelPath"]?.GetValue<string>(),
                sheetName = args["sheetName"]?.GetValue<string>(),
                startCell = args["startCell"]?.GetValue<string>(),
                rowCount,
                colCount,
                treatNullAsClear = args["treatNullAsClear"]?.GetValue<bool?>() ?? false,
                sample = values.Take(3).ToArray(),
            })!,
            IsError = false,
        };
    }

    private static ExcelMcpToolExecutionResult PreviewAppendRows(JsonObject args)
    {
        var rows = args["rows"] as JsonArray ?? new JsonArray();
        var rowCount = rows.Count;
        var colCount = rows
            .Select(x => x as JsonArray)
            .Where(x => x is not null)
            .Select(x => x!.Count)
            .DefaultIfEmpty(0)
            .Max();

        return new ExcelMcpToolExecutionResult
        {
            Payload = JsonSerializer.SerializeToNode(new
            {
                ok = true,
                preview = true,
                operation = "append_rows",
                excelPath = args["excelPath"]?.GetValue<string>(),
                sheetName = args["sheetName"]?.GetValue<string>(),
                startColumn = args["startColumn"]?.GetValue<string>() ?? "A",
                rowCount,
                colCount,
                sample = rows.Take(3).ToArray(),
            })!,
            IsError = false,
        };
    }

    private static ExcelMcpToolExecutionResult PreviewSetFormula(JsonObject args)
    {
        return new ExcelMcpToolExecutionResult
        {
            Payload = JsonSerializer.SerializeToNode(new
            {
                ok = true,
                preview = true,
                operation = "set_formula",
                excelPath = args["excelPath"]?.GetValue<string>(),
                sheetName = args["sheetName"]?.GetValue<string>(),
                target = args["target"]?.GetValue<string>(),
                formulaA1 = args["formulaA1"]?.GetValue<string>(),
            })!,
            IsError = false,
        };
    }
}
