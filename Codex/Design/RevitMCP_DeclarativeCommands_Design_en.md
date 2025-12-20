# RevitMCP Addin – Declarative Command Architecture Design (EN)

## 1. Goal

Refactor the current *centralized* command registry in **RevitMCPAddin** into a more **declarative, discoverable, and testable** architecture.

Key objectives:

- Avoid constant edits to a single `CommandRegistry.cs` file
- Allow different teams to add commands **without merge conflicts**
- Make command metadata (name, group, description, etc.) **machine-readable** for:
  - JSON-RPC over MCP
  - CLI tools
  - AI agents (ChatGPT, Gemini, local LLM, etc.)
- Improve **unit testability** via DI and isolated command classes
- Support **incremental migration** from the existing implementation

---

## 2. High-Level Architecture

### 2.1 Core Ideas

1. **Attribute-based declaration**  
   Each command class declares its own metadata via a custom attribute, e.g.:

   ```csharp
   [McpCommand("export_dwg_with_workset_bucketing",
       Group = "Export",
       Description = "DWG export with workset bucket rules")]
   public sealed class ExportDwgWithWorksetBucketingCommand : IRevitCommandHandler
   {
       // ...
   }
   ```

2. **Assembly scanning at startup**  
   On Revit add-in startup (`IExternalApplication.OnStartup`), the add-in scans one or more assemblies for types that:

   - implement `IRevitCommandHandler`
   - are decorated with `McpCommandAttribute`

   Those types are then registered in:

   - DI container (`IServiceCollection` / `IServiceProvider`)
   - `IMcpRouter` (our internal “command router”)

3. **Dependency Injection (DI)**  
   Each command receives its dependencies via constructor injection, which enables:

   - Easy mocking in unit tests
   - Clear separation between domain logic and Revit API integration
   - Pluggable infrastructure components (exporters, analyzers, etc.)

4. **Router instead of central registry**  
   `IMcpRouter` acts as the **lookup table** that maps command names (e.g. `"export_dwg_with_workset_bucketing"`) to factories that create `IRevitCommandHandler` instances using DI.

---

## 3. Key Components

### 3.1 `IRevitCommandHandler`

A simple interface that all RevitMCP commands implement. It deliberately stays close to existing patterns in the project.

```csharp
public interface IRevitCommandHandler
{
    /// <summary>
    /// Logical JSON-RPC command name. Optional if you rely solely on McpCommandAttribute.
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// Execute the command against the current Revit session.
     /// </summary>
    /// <param name="uiapp">Revit UIApplication.</param>
    /// <param name="cmd">MCP/JSON-RPC request payload.</param>
    /// <returns>Any serializable object to be sent back over MCP.</returns>
    object Execute(UIApplication uiapp, RequestCommand cmd);
}
```

> Note: `RequestCommand` is assumed to be the existing DTO used between MCP server and addin.

### 3.2 `McpCommandAttribute`

`McpCommandAttribute` provides declarative, machine-readable metadata.

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class McpCommandAttribute : Attribute
{
    public string Name { get; }
    public string? Group { get; init; }
    public string? Description { get; init; }
    public string? SinceVersion { get; init; }
    public string? DeprecatedSinceVersion { get; init; }

    public McpCommandAttribute(string name)
    {
        Name = name;
    }
}
```

Usage example:

```csharp
[McpCommand(
    "export_dwg_with_workset_bucketing",
    Group = "Export",
    Description = "DWG export with workset bucket rules",
    SinceVersion = "1.5.0")]
public sealed class ExportDwgWithWorksetBucketingCommand : IRevitCommandHandler
{
    // ...
}
```

This metadata can be queried at runtime and exposed as:

- JSON manifest for AI/CLI
- Help/list commands (`list_commands`, `describe_command`, etc.)

### 3.3 `IMcpRouter` and `McpRouter`

Router responsibilities:

- Register commands by logical name
- Provide a way to resolve and execute handlers by name
- Expose command catalog (for introspection / documentation)

```csharp
public interface IMcpRouter
{
    void Register(
        string name,
        Func<IServiceProvider, IRevitCommandHandler> factory,
        string? group = null,
        string? description = null);

    bool TryGet(
        string name,
        out Func<IServiceProvider, IRevitCommandHandler> factory);

    IReadOnlyList<McpCommandInfo> GetAllCommands();
}

public sealed record McpCommandInfo(
    string Name,
    string? Group,
    string? Description);
```

Basic implementation:

```csharp
public sealed class McpRouter : IMcpRouter
{
    private readonly Dictionary<string, (Func<IServiceProvider, IRevitCommandHandler> Factory, McpCommandInfo Info)> _map
        = new(StringComparer.OrdinalIgnoreCase);

    public void Register(
        string name,
        Func<IServiceProvider, IRevitCommandHandler> factory,
        string? group = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Command name must not be null or empty.", nameof(name));
        if (factory is null)
            throw new ArgumentNullException(nameof(factory));

        var info = new McpCommandInfo(name, group, description);
        _map[name] = (factory, info);
    }

    public bool TryGet(
        string name,
        out Func<IServiceProvider, IRevitCommandHandler> factory)
    {
        if (_map.TryGetValue(name, out var entry))
        {
            factory = entry.Factory;
            return true;
        }

        factory = null!;
        return false;
    }

    public IReadOnlyList<McpCommandInfo> GetAllCommands()
        => _map.Values.Select(v => v.Info).OrderBy(i => i.Name).ToList();
}
```

### 3.4 Command Discovery (`McpCommandDiscovery`)

This static helper scans an assembly and registers all eligible commands.

```csharp
public static class McpCommandDiscovery
{
    public static void AddMcpCommandsFrom(
        Assembly assembly,
        IServiceCollection services,
        IMcpRouter router)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (router == null) throw new ArgumentNullException(nameof(router));

        var handlerType = typeof(IRevitCommandHandler);

        var candidates =
            assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && handlerType.IsAssignableFrom(t))
                .Select(t => new
                {
                    Type = t,
                    Attr = t.GetCustomAttribute<McpCommandAttribute>(inherit: false)
                })
                .Where(x => x.Attr != null);

        foreach (var c in candidates)
        {
            // Register type into DI
            services.AddTransient(c.Type);

            // Register into router
            var attr = c.Attr!;
            router.Register(
                name: attr.Name,
                factory: sp => (IRevitCommandHandler)sp.GetRequiredService(c.Type),
                group: attr.Group,
                description: attr.Description);
        }
    }
}
```

This is typically invoked for each “commands” assembly, for example:

- `RevitMCPAddin.Commands.Core`
- `RevitMCPAddin.Commands.Export`
- `RevitMCPAddin.Commands.ParamOps`
- etc.

---

## 4. Startup & Wiring

### 4.1 Revit External Application

In `IExternalApplication.OnStartup`, we wire up:

- DI `ServiceCollection`
- `McpRouter`
- Command discovery across assemblies

Example (simplified):

```csharp
public class RevitMcpApp : IExternalApplication
{
    private static IServiceProvider? _serviceProvider;
    private static IMcpRouter? _router;

    public Result OnStartup(UIControlledApplication application)
    {
        var services = new ServiceCollection();

        // Infrastructure / shared services
        services.AddSingleton<IDwgExporter, DwgExporter>();
        services.AddSingleton<IWorksetBucketer, WorksetBucketer>();

        // Router
        var router = new McpRouter();
        services.AddSingleton<IMcpRouter>(router);

        // Build intermediate provider for discovery-time services if needed
        var tempProvider = services.BuildServiceProvider();

        // Command discovery (you can add multiple assemblies here)
        var commandAssemblies = new[]
        {
            typeof(RevitMcpApp).Assembly, // if commands are in the same assembly
            // typeof(SomeCommandInOtherAssembly).Assembly,
        };

        foreach (var asm in commandAssemblies)
        {
            McpCommandDiscovery.AddMcpCommandsFrom(asm, services, router);
        }

        // Final provider (DI graph is now complete)
        _serviceProvider = services.BuildServiceProvider();
        _router = _serviceProvider.GetRequiredService<IMcpRouter>();

        // Optionally: log/register command catalog somewhere
        LogDiscoveredCommands(_router);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        if (_serviceProvider is IDisposable d)
        {
            d.Dispose();
        }

        _serviceProvider = null;
        _router = null;
        return Result.Succeeded;
    }

    private static void LogDiscoveredCommands(IMcpRouter router)
    {
        var infos = router.GetAllCommands();
        // Write to your logging abstraction
        foreach (var info in infos)
        {
            // Example: Debug.WriteLine(...)
        }
    }

    // Helper for MCP endpoint to resolve and execute commands
    public static object ExecuteByName(
        UIApplication uiapp,
        string commandName,
        RequestCommand cmd)
    {
        if (_serviceProvider == null || _router == null)
        {
            throw new InvalidOperationException("RevitMcpApp is not initialized.");
        }

        if (!_router.TryGet(commandName, out var factory))
        {
            throw new KeyNotFoundException($"Command '{commandName}' not found.");
        }

        var handler = factory(_serviceProvider);
        return handler.Execute(uiapp, cmd);
    }
}
```

### 4.2 MCP / JSON-RPC Integration

On the MCP server side (ASP.NET Core, etc.), the JSON-RPC handler already:

- Receives a `command` name and `params`
- Sends them to RevitMCP Addin via named pipe / TCP / other IPC

The new architecture only changes the **lookup** on the add-in side:

- Old: `CommandRegistry.GetHandler(commandName)` returns an instance
- New: `RevitMcpApp.ExecuteByName(uiapp, commandName, cmd)`

Minimal migration step: the existing IPC layer only needs to call the new entry point.

---

## 5. Example Command

```csharp
[McpCommand(
    "export_dwg_with_workset_bucketing",
    Group = "Export",
    Description = "DWG export with workset bucket rules")]
public sealed class ExportDwgWithWorksetBucketingCommand : IRevitCommandHandler
{
    private readonly IWorksetBucketer _bucketer;
    private readonly IDwgExporter _exporter;

    public string CommandName => "export_dwg_with_workset_bucketing";

    public ExportDwgWithWorksetBucketingCommand(
        IWorksetBucketer bucketer,
        IDwgExporter exporter)
    {
        _bucketer = bucketer ?? throw new ArgumentNullException(nameof(bucketer));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
    }

    public object Execute(UIApplication uiapp, RequestCommand cmd)
    {
        if (uiapp?.ActiveUIDocument?.Document is not Document doc)
        {
            throw new InvalidOperationException("No active Revit document.");
        }

        // Example: extract arguments from JSON payload
        var args = cmd.Args; // assuming JObject or similar

        var rules = _bucketer.BuildRules(doc);
        var result = _exporter.Export(doc, rules, args);

        return new
        {
            success = true,
            details = result
        };
    }
}
```

---

## 6. Testing Strategy

### 6.1 Unit Testing Individual Commands

Use a simple DI container in tests (or `ServiceCollection`) and mock dependencies.

Example (xUnit-like pseudocode):

```csharp
[Fact]
public void ExportCommand_Runs_With_Mocks()
{
    var services = new ServiceCollection();
    services.AddSingleton<IDwgExporter, FakeExporter>();
    services.AddSingleton<IWorksetBucketer, FakeBucketer>();
    services.AddTransient<ExportDwgWithWorksetBucketingCommand>();

    var sp = services.BuildServiceProvider();

    var cmd = sp.GetRequiredService<ExportDwgWithWorksetBucketingCommand>();

    var fakeUiApp = TestHelpers.CreateUiAppWithFakeDocument();
    var req = new RequestCommand { Args = new JObject() };

    var result = cmd.Execute(fakeUiApp, req);

    Assert.NotNull(result);
}
```

### 6.2 Testing Discovery & Router

You can write tests to verify that:

- `McpCommandDiscovery.AddMcpCommandsFrom` registers the expected number of commands
- `McpRouter.GetAllCommands` returns consistent metadata
- `ExecuteByName` successfully executes known commands and fails for unknown names

---

## 7. Migration Plan

1. **Introduce new infrastructure**  
   Add the following to the existing solution without modifying current behavior:
   - `McpCommandAttribute`
   - `IMcpRouter` & `McpRouter`
   - `McpCommandDiscovery`
   - `RevitMcpApp.ExecuteByName` helper

2. **Wire up DI & discovery**  
   In `OnStartup`, build DI, create router, and scan at least one commands assembly.

3. **Port one real command**  
   Choose a command with low risk (e.g. a read-only “info” command), and:
   - Move its logic into an `IRevitCommandHandler` implementation
   - Add `[McpCommand("...")]`
   - Ensure it can be called through `ExecuteByName`

4. **Add MCP list/describe commands** (optional but recommended)  
   Implement commands like:
   - `list_commands` → returns router catalog
   - `describe_command` → returns metadata for a given name

5. **Gradually migrate existing commands**  
   Whenever a command is modified, take the chance to move it into the new pattern.

6. **Deprecate old `CommandRegistry`**  
   Once all commands are migrated and fully stable:
   - Remove or freeze the old registry
   - Keep a small compatibility layer if needed for legacy tools

---

## 8. Benefits Summary

- **Reduced merge conflicts**: no more massive `CommandRegistry.cs` edits.
- **Plug-in style extensibility**: adding a new command = add a class + attribute.
- **Introspection-friendly**: command list easily exposed to AI/CLI/MCP clients.
- **Better testability**: DI + small classes = easier and safer unit tests.
- **Gradual rollout**: old and new styles can coexist during migration.

This design intentionally keeps things **simple** (no complex framework dependency) while unlocking a much more flexible and AI-friendly RevitMCP command ecosystem.
