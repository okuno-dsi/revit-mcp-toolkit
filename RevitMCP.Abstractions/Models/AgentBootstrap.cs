// RevitMCPAbstraction/Models/AgentBootstrap.cs
#nullable enable
using System.Collections.Generic;

namespace RevitMCPAbstraction.Models
{
    public sealed class AgentBootstrapRequest
    {
        public bool LiteCatalog { get; set; } = true;
        public bool PreferSiUnits { get; set; } = true;
        public bool NamesOnly { get; set; } = false;
    }

    public sealed class AgentBootstrapResponse
    {
        public bool Ok { get; set; }
        public ServerInfo Server { get; set; } = new ServerInfo();
        public ProjectInfo Project { get; set; } = new ProjectInfo();
        public EnvironmentInfo Environment { get; set; } = new EnvironmentInfo();
        public CommandsInfo Commands { get; set; } = new CommandsInfo();
        public PoliciesInfo Policies { get; set; } = new PoliciesInfo();
        public List<KnownError> KnownErrors { get; set; } = new List<KnownError>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ServerInfo
    {
        public string Product { get; set; } = string.Empty;
        public ProcessInfo Process { get; set; } = new ProcessInfo();
    }
    public sealed class ProcessInfo { public int Pid { get; set; } public string ExePath { get; set; } = string.Empty; public int Port { get; set; } }

    public sealed class ProjectInfo
    {
        public bool Ok { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string RevitVersion { get; set; } = string.Empty;
        public string DocumentGuid { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class EnvironmentInfo
    {
        public UnitsInfo Units { get; set; } = new UnitsInfo();
        public int ActiveViewId { get; set; }
        public string ActiveViewName { get; set; } = string.Empty;
    }
    public sealed class UnitsInfo
    {
        public UnitSet Input { get; set; } = new UnitSet();
        public UnitSet Internal { get; set; } = new UnitSet();
    }
    public sealed class UnitSet { public string Length { get; set; } = "mm"; public string Angle { get; set; } = "deg"; public string Area { get; set; } = "m2"; public string Volume { get; set; } = "m3"; }

    public sealed class CommandsInfo
    {
        public int Count { get; set; }
        public List<string> Hot { get; set; } = new List<string>();
        public List<CommandBrief> Catalog { get; set; } = new List<CommandBrief>();
    }
    public sealed class CommandBrief
    {
        public string Method { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Desc { get; set; } = string.Empty;
        public string ParamsMin { get; set; } = string.Empty;
        public string ResultHint { get; set; } = string.Empty;
    }

    public sealed class PoliciesInfo
    {
        public bool IdFirst { get; set; } = true;
        public string LengthUnit { get; set; } = "mm";
        public string AngleUnit { get; set; } = "deg";
        public ThrottleInfo Throttle { get; set; } = new ThrottleInfo();
    }
    public sealed class ThrottleInfo { public int MinMsBetweenCalls { get; set; } = 80; }

    public sealed class KnownError { public string Code { get; set; } = string.Empty; public string Msg { get; set; } = string.Empty; }
}
