using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.Core;
using RevitMCPAddin.UI.PythonRunner;

namespace RevitMCPAddin.Commands
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ShowPythonRunnerCommand : IExternalCommand
    {
        private static PythonRunnerWindow? _win;

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                if (_win == null || !_win.IsLoaded)
                {
                    var doc = data?.Application?.ActiveUIDocument?.Document;
                    var docTitle = doc?.Title;
                    string? docKey = null;
                    try
                    {
                        string source;
                        docKey = DocumentKeyUtil.GetDocKeyOrStable(doc, createIfMissing: true, out source);
                    }
                    catch { /* best-effort */ }
                    if (string.IsNullOrWhiteSpace(docKey))
                        docKey = RevitMCPAddin.AppServices.CurrentDocKey;
                    _win = new PythonRunnerWindow(docTitle, docKey);
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
