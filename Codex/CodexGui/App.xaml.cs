using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CodexGui;

public partial class App : Application
{
    private A2AHubService? _a2aHub;

    protected override void OnStartup(StartupEventArgs e)
    {
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

        _a2aHub = new A2AHubService();
        _a2aHub.Start();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _a2aHub?.Stop(); } catch { }
        base.OnExit(e);
    }
}
