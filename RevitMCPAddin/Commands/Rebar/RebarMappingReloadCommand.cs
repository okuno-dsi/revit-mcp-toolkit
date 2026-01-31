// ================================================================
// Command: rebar_mapping_reload
// Purpose: Reload RebarMapping.json and return the current status.
// Target : Revit 2023+ / .NET Framework 4.8 / C# 8
// ================================================================
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Rebar
{
    public sealed class RebarMappingReloadCommand : IRevitCommandHandler
    {
        public string CommandName => "rebar_mapping_reload";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var st = RebarMappingService.Reload();
            return ResultUtil.Ok(new
            {
                ok = st.ok,
                code = st.code,
                msg = st.msg,
                path = st.path,
                version = st.version,
                sha8 = st.sha8,
                units_length = st.units_length,
                profile_default = st.profile_default,
                profiles = st.profiles
            });
        }
    }
}
