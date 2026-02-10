using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;
using RevitMCPAddin.UI.InfoPick;

namespace RevitMCPAddin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ShowInfoPickCommand : IExternalCommand
    {
        private static InfoPickWindow? _win;

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = data?.Application?.ActiveUIDocument;
                if (uidoc == null || uidoc.Document == null)
                {
                    message = "Active document is not available.";
                    return Result.Failed;
                }

                string? docKey = null;
                try
                {
                    string source;
                    docKey = DocumentKeyUtil.GetDocKeyOrStable(uidoc.Document, createIfMissing: true, out source);
                }
                catch { /* best-effort */ }
                if (string.IsNullOrWhiteSpace(docKey))
                    docKey = RevitMCPAddin.AppServices.CurrentDocKey;

                if (_win == null || !_win.IsLoaded)
                {
                    _win = new InfoPickWindow(uidoc, docKey);
                    var helper = new WindowInteropHelper(_win)
                    {
                        Owner = data.Application.MainWindowHandle
                    };
                    _win.Closed += (_, __) => _win = null;
                    _win.Show();
                }
                else
                {
                    _win.Show();
                    _win.Activate();
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
