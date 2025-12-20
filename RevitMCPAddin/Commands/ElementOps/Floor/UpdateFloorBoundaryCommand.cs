// RevitMCPAddin/Commands/ElementOps/FloorOps/UpdateFloorBoundaryCommand.cs
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.ElementOps.FloorOps
{
    public class UpdateFloorBoundaryCommand : IRevitCommandHandler
    {
        public string CommandName => "update_floor_boundary";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            return new
            {
                ok = false,
                msg = "Floor はシステムファミリのため、境界プロファイルの編集はサポートされていません。",
                // 返却メタ（I/O想定は SI）
                inputUnits = UnitHelper.DefaultUnitsMeta(),
                internalUnits = new { Length = "ft", Area = "ft2", Volume = "ft3", Angle = "rad" }
            };
        }
    }
}
