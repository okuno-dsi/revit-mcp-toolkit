// Commands/Debug/SmokeTestNoopHandler.cs
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Debug
{
    public sealed class SmokeTestNoopHandler : IRevitCommandHandler
    {
        public string CommandName => "smoke_test";
        public object Execute(UIApplication uiapp, RequestCommand cmd)
            => new { ok = true, msg = "smoke_test is handled on server (Add-in no-op)." };
    }
}
