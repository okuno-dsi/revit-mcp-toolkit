#nullable enable
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPAddin.UI.Chat;
using System;
using System.Reflection;

namespace RevitMCPAddin.Commands.Chat
{
    [Transaction(TransactionMode.ReadOnly)]
    public class ToggleChatPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var uiapp = data?.Application;
                if (uiapp == null)
                {
                    message = "UIApplication is null.";
                    return Result.Failed;
                }

                var pane = uiapp.GetDockablePane(ChatDockablePaneIds.PaneId);
                if (pane == null)
                {
                    message = "Chat DockablePane not found.";
                    return Result.Failed;
                }

                bool visible = false;
                try
                {
                    var mi = typeof(DockablePane).GetMethod("IsVisible", BindingFlags.Public | BindingFlags.Instance);
                    if (mi != null && mi.ReturnType == typeof(bool))
                        visible = (bool)mi.Invoke(pane, null);
                    else
                    {
                        var mi2 = typeof(DockablePane).GetMethod("IsShown", BindingFlags.Public | BindingFlags.Instance);
                        if (mi2 != null && mi2.ReturnType == typeof(bool))
                            visible = (bool)mi2.Invoke(pane, null);
                    }
                }
                catch { /* ignore */ }

                if (visible) pane.Hide();
                else pane.Show();

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
