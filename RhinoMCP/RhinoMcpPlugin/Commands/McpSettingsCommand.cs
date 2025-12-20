using Rhino;
using Rhino.Commands;

namespace RhinoMcpPlugin.Commands
{
    public class McpSettingsCommand : Command
    {
        public override string EnglishName => "McpSettings";
        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            RhinoApp.WriteLine("Current settings:");
            RhinoApp.WriteLine($"  Rhino MCP: {RhinoMcpPlugin.Instance.RhinoMcpBaseUrl}");
            RhinoApp.WriteLine($"  Revit MCP: {RhinoMcpPlugin.Instance.RevitMcpBaseUrl}");
            return Result.Success;
        }
    }
}
