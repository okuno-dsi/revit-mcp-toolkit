// ================================================================
// File: Commands/Misc/UnitModeCommands.cs
// 概要: UnitsMode 切替コマンド（SI / Project / Raw / Both）
// 実装: IExternalCommand（設定ファイルへ保存し、TaskDialogで通知）
// ================================================================
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.Misc
{
    [Transaction(TransactionMode.Manual)]
    public class SwitchUnitsSiCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UnitSettingsManager.UpdateDefaultMode(UnitsMode.SI);
            TaskDialog.Show("Units", "Units mode changed to: SI (mm, m², m³, deg)");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SwitchUnitsProjectCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UnitSettingsManager.UpdateDefaultMode(UnitsMode.Project);
            TaskDialog.Show("Units", "Units mode changed to: Project (プロジェクト表示単位)");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SwitchUnitsRawCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UnitSettingsManager.UpdateDefaultMode(UnitsMode.Raw);
            TaskDialog.Show("Units", "Units mode changed to: Raw (内部値 ft / rad)");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SwitchUnitsBothCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UnitSettingsManager.UpdateDefaultMode(UnitsMode.Both);
            TaskDialog.Show("Units", "Units mode changed to: Both (SI + Project)");
            return Result.Succeeded;
        }
    }
}
