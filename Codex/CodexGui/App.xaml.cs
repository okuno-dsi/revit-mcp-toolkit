using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CodexGui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CodexGuiLog.Init();

        DispatcherUnhandledException += (_, args) =>
        {
            CodexGuiLog.Exception("DispatcherUnhandledException", args.Exception);
            // Keep the app alive when possible; errors should surface in the UI/log.
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            CodexGuiLog.Exception("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CodexGuiLog.Exception("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }
}
