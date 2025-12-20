# RevitMCP – Declarative Commands Minimal Solution Skeleton (EN)

This document describes a **minimal Visual Studio solution skeleton** that implements the declarative command architecture described in the design document.

> Note: Namespaces and assembly names are examples. Adjust to match your existing `RevitMCPAddin` conventions.

---

## 1. Solution / Project Structure

```text
RevitMCPDeclarativeSample.sln
  src/
    RevitMCPAddin/
      RevitMCPAddin.csproj
      App/
        RevitMcpApp.cs
      Core/
        IRevitCommandHandler.cs
        RequestCommand.cs           (placeholder if not already defined)
        McpCommandAttribute.cs
        IMcpRouter.cs
        McpRouter.cs
        McpCommandDiscovery.cs
      Commands/
        Export/
          ExportDwgWithWorksetBucketingCommand.cs
        // other command groups...
```

You can embed this skeleton into your existing solution instead of creating a new `.sln`, but the following code is written so that it can also compile standalone if you provide basic stubs for existing types.

---

## 2. `RevitMCPAddin.csproj` (simplified example)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AssemblyName>RevitMCPAddin</AssemblyName>
    <RootNamespace>RevitMCPAddin</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- Revit API references (paths are examples) -->
    <Reference Include="RevitAPI">
      <HintPath>$(REVIT_API_PATH)\RevitAPI.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="RevitAPIUI">
      <HintPath>$(REVIT_API_PATH)\RevitAPIUI.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <!-- DI container -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
    <!-- JSON, if needed for RequestCommand.Args -->
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>

</Project>
```

Adjust `TargetFramework` and package versions to your environment (e.g. .NET Framework 4.8).

---

## 3. Core Types

### 3.1 `IRevitCommandHandler.cs`

```csharp
using Autodesk.Revit.UI;

namespace RevitMCPAddin.Core
{
    public interface IRevitCommandHandler
    {
        string CommandName { get; }

        object Execute(UIApplication uiapp, RequestCommand cmd);
    }
}
```

### 3.2 `RequestCommand.cs` (placeholder)

If you already have a `RequestCommand` type in another project, **reuse it**.  
Otherwise, a minimal placeholder could look like this:

```csharp
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    public sealed class RequestCommand
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Arbitrary JSON arguments passed from MCP/JSON-RPC layer.
        /// </summary>
        public JObject Args { get; set; } = new JObject();
    }
}
```

### 3.3 `McpCommandAttribute.cs`

```csharp
using System;

namespace RevitMCPAddin.Core
{
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
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
}
```

### 3.4 `IMcpRouter.cs` & `McpRouter.cs`

```csharp
using System;
using System.Collections.Generic;

namespace RevitMCPAddin.Core
{
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
            => new List<McpCommandInfo>(_map.Values.Select(v => v.Info));
    }
}
```

> Note: If you are on C# 9+, `record` and target-typed `new` will work as above. Otherwise, convert `record` to a class and expand `new(...)` syntax.

### 3.5 `McpCommandDiscovery.cs`

```csharp
using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace RevitMCPAddin.Core
{
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
                services.AddTransient(c.Type);

                var attr = c.Attr!;
                router.Register(
                    name: attr.Name,
                    factory: sp => (IRevitCommandHandler)sp.GetRequiredService(c.Type),
                    group: attr.Group,
                    description: attr.Description);
            }
        }
    }
}
```

---

## 4. Minimal Example Command

`Commands/Export/ExportDwgWithWorksetBucketingCommand.cs`

```csharp
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Export
{
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

            JObject args = cmd.Args;

            var rules = _bucketer.BuildRules(doc);
            var exportResult = _exporter.Export(doc, rules, args);

            return new
            {
                success = true,
                details = exportResult
            };
        }
    }

    // Interfaces below are placeholders – wire them to your real implementation.
    public interface IWorksetBucketer
    {
        object BuildRules(Document doc);
    }

    public interface IDwgExporter
    {
        object Export(Document doc, object rules, JObject args);
    }
}
```

You can replace the placeholder interfaces with your real implementations in your existing project.

---

## 5. Revit External Application Entry Point

`App/RevitMcpApp.cs`

```csharp
using System;
using Autodesk.Revit.UI;
using Microsoft.Extensions.DependencyInjection;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.App
{
    public class RevitMcpApp : IExternalApplication
    {
        private static IServiceProvider? _serviceProvider;
        private static IMcpRouter? _router;

        public Result OnStartup(UIControlledApplication application)
        {
            var services = new ServiceCollection();

            // TODO: register your real infrastructure services
            // services.AddSingleton<IDwgExporter, DwgExporter>();
            // services.AddSingleton<IWorksetBucketer, WorksetBucketer>();

            var router = new McpRouter();
            services.AddSingleton<IMcpRouter>(router);

            // First build can be optional; used only if some services are required
            var interimProvider = services.BuildServiceProvider();

            // Discover commands in this assembly
            var currentAssembly = typeof(RevitMcpApp).Assembly;
            McpCommandDiscovery.AddMcpCommandsFrom(currentAssembly, services, router);

            // Final DI container
            _serviceProvider = services.BuildServiceProvider();
            _router = _serviceProvider.GetRequiredService<IMcpRouter>();

            // Optional: log discovered commands
            LogDiscoveredCommands(_router);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _serviceProvider = null;
            _router = null;
            return Result.Succeeded;
        }

        private static void LogDiscoveredCommands(IMcpRouter router)
        {
            foreach (var info in router.GetAllCommands())
            {
                // Replace with your logging abstraction
                System.Diagnostics.Debug.WriteLine($"[MCP] Command registered: {info.Name} ({info.Group})");
            }
        }

        /// <summary>
        /// Helper entry point to be called from MCP/IPC side.
        /// </summary>
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
                throw new InvalidOperationException($"Command '{commandName}' not found.");
            }

            var handler = factory(_serviceProvider);
            return handler.Execute(uiapp, cmd);
        }
    }
}
```

Hook `ExecuteByName` into your existing MCP/IPC layer where you previously resolved commands via a central registry.

---

## 6. How to Integrate into Existing RevitMCPAddin

1. **Add Core Files**  
   Copy the files from `Core/` into your existing add-in project / library.

2. **Add Router & Discovery**  
   Wire `McpRouter` and `McpCommandDiscovery` in your existing `IExternalApplication.OnStartup` implementation.

3. **MCP/IPC Entry Point**  
   Expose a function like `ExecuteByName` (or adapt the existing one) that:
   - resolves command handler via `IMcpRouter`
   - invokes `Execute`

4. **Port First Command**  
   Move one existing JSON-RPC command into the new pattern and decorate it with `[McpCommand("...")]`.

5. **Gradually Migrate**  
   Convert other commands over time. Old and new styles can coexist during the transition.

This skeleton is intentionally minimal but **complete enough to compile** once Revit and your own infrastructure references are added. You can further split into multiple projects (Core vs Commands vs Infrastructure) if that matches your architecture.
