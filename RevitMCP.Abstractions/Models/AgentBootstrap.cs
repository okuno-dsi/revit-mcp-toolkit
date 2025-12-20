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
        public string Product { get; set; }
        public ProcessInfo Process { get; set; } = new ProcessInfo();
    }
    public sealed class ProcessInfo { public int Pid { get; set; } public string ExePath { get; set; } public int Port { get; set; } }

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
    public sealed class UnitSet { public string Length { get; set; } = "mm"; public string Angle { get; set; } = "deg"; public string Area { get; set; } = "m2"; public string Volume { get; set; } = "m3"; }

    public sealed class CommandsInfo
    {
        public int Count { get; set; }
        public List<string> Hot { get; set; } = new List<string>();
        public List<CommandBrief> Catalog { get; set; } = new List<CommandBrief>();
    }
    public sealed class CommandBrief
    {
        public string Method { get; set; }
        public string Category { get; set; }
        public string Desc { get; set; }
        public string ParamsMin { get; set; }
        public string ResultHint { get; set; }
    }

    public sealed class PoliciesInfo
    {
        public bool IdFirst { get; set; } = true;
        public string LengthUnit { get; set; } = "mm";
        public string AngleUnit { get; set; } = "deg";
        public ThrottleInfo Throttle { get; set; } = new ThrottleInfo();
    }
    public sealed class ThrottleInfo { public int MinMsBetweenCalls { get; set; } = 80; }

    public sealed class KnownError { public string Code { get; set; } public string Msg { get; set; } }
}
