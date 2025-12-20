// ================================================================
// File: Models/AgentBootstrap.cs
// Target : .NET Framework 4.8 / C# 8
// Purpose: agent_bootstrap の入出力モデル
// ================================================================
#nullable enable
using System.Collections.Generic;

namespace RevitMcpServer.Models
{
    // 入力（今はオプショナル。将来 flags 等を追加可能）
    public sealed class AgentBootstrapRequest
    {
        // true: commands.catalog を軽量化（推奨）
        public bool LiteCatalog { get; set; } = true;
        // 例: "mm","deg" を強制するか（未実装でも将来互換）
        public bool PreferSiUnits { get; set; } = true;
        // namesOnly で list_commands を呼ぶ
        public bool NamesOnly { get; set; } = false;
    }

    public sealed class AgentBootstrapResponse
    {
        public bool Ok { get; set; }
        public ServerInfo Server { get; set; }
        public ProjectInfo Project { get; set; }
        public EnvironmentInfo Environment { get; set; }
        public CommandsInfo Commands { get; set; }
        public PoliciesInfo Policies { get; set; }
        public List<KnownError> KnownErrors { get; set; }
        public List<string> Warnings { get; set; }

        public AgentBootstrapResponse()
        {
            Server = new ServerInfo();
            Project = new ProjectInfo();
            Environment = new EnvironmentInfo();
            Commands = new CommandsInfo();
            Policies = new PoliciesInfo();
            KnownErrors = new List<KnownError>();
            Warnings = new List<string>();
        }
    }

    public sealed class ServerInfo
    {
        public string Product { get; set; }  // e.g., "Autodesk Revit 2024"
        public ProcessInfo Process { get; set; } = new ProcessInfo();
    }
    public sealed class ProcessInfo
    {
        public int Pid { get; set; }
        public string ExePath { get; set; }
        public int Port { get; set; }
    }

    public sealed class ProjectInfo
    {
        public bool Ok { get; set; }
        public string Name { get; set; }
        public string Number { get; set; }
        public string FilePath { get; set; }
        public string RevitVersion { get; set; }
        public string DocumentGuid { get; set; }
        public string Message { get; set; }
    }

    public sealed class EnvironmentInfo
    {
        public UnitsInfo Units { get; set; } = new UnitsInfo();
        public int ActiveViewId { get; set; }
        public string ActiveViewName { get; set; }
    }
    public sealed class UnitsInfo
    {
        public UnitSet Input { get; set; } = new UnitSet();
        public UnitSet Internal { get; set; } = new UnitSet();
    }
    public sealed class UnitSet
    {
        public string Length { get; set; } = "mm";
        public string Angle { get; set; } = "deg";
        public string Area { get; set; } = "m2";
        public string Volume { get; set; } = "m3";
    }

    public sealed class CommandsInfo
    {
        public int Count { get; set; }
        public List<string> Hot { get; set; } = new List<string>();
        // LiteCatalog: method/desc/params_min/result_hint 程度の薄型
        public List<CommandBrief> Catalog { get; set; } = new List<CommandBrief>();
    }

    public sealed class CommandBrief
    {
        public string Method { get; set; }
        public string Category { get; set; }
        public string Desc { get; set; }
        public string ParamsMin { get; set; } // 例: "viewId, elementIds?"
        public string ResultHint { get; set; } // 例: "ok, count, items[]"
    }

    public sealed class PoliciesInfo
    {
        public bool IdFirst { get; set; } = true;
        public string LengthUnit { get; set; } = "mm";
        public string AngleUnit { get; set; } = "deg";
        public ThrottleInfo Throttle { get; set; } = new ThrottleInfo();
    }
    public sealed class ThrottleInfo
    {
        public int MinMsBetweenCalls { get; set; } = 80;
    }

    public sealed class KnownError
    {
        public string Code { get; set; }
        public string Msg { get; set; }
    }
}
