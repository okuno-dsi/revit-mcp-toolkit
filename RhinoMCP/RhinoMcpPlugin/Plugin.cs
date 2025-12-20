using System;
using Rhino;
using Rhino.PlugIns;
using System.Diagnostics;
using System.IO;

namespace RhinoMcpPlugin
{
    public class RhinoMcpPlugin : PlugIn
    {
        public static RhinoMcpPlugin Instance { get; private set; }

        public string RhinoMcpBaseUrl { get; set; } = "http://127.0.0.1:5200";
        public string RevitMcpBaseUrl { get; set; } = "http://127.0.0.1:5210";
        public string UnitsRhino { get; set; } = "mm"; // assumed
        public string UnitsRevit { get; set; } = "feet";

        private Process _serverProcess;

        public RhinoMcpPlugin()
        {
            Instance = this;
        }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            Core.Logger.Init();
            RhinoApp.WriteLine("[RhinoMcpPlugin] Loaded.");
            Core.PluginIpcServer.Start();
            TryStartRhinoMcpServer();
            return LoadReturnCode.Success;
        }

        protected override void OnShutdown()
        {
            Core.PluginIpcServer.Stop();
            TryStopRhinoMcpServer();
            base.OnShutdown();
        }

        private void TryStartRhinoMcpServer()
        {
            try
            {
                var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
                string[] candidates = new[]
                {
                    Path.GetFullPath(Path.Combine(pluginDir, "..", "..", "..", "RhinoMcpServer", "bin", "Debug", "net6.0", "RhinoMcpServer.exe")),
                    Path.GetFullPath(Path.Combine(pluginDir, "..", "..", "..", "RhinoMcpServer", "bin", "Release", "net6.0", "RhinoMcpServer.exe"))
                };

                foreach (var exe in candidates)
                {
                    if (File.Exists(exe))
                    {
                        _serverProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = exe,
                                WorkingDirectory = Path.GetDirectoryName(exe),
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        _serverProcess.Start();
                        Core.Logger.Info($"Started RhinoMcpServer: {exe}");
                        return;
                    }
                }

                // Fallback: dotnet run
                var solRoot = Path.GetFullPath(Path.Combine(pluginDir, "..", "..", ".."));
                var csproj = Path.Combine(solRoot, "RhinoMcpServer", "RhinoMcpServer.csproj");
                if (File.Exists(csproj))
                {
                    _serverProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = $"run --project \"{csproj}\"",
                            WorkingDirectory = solRoot,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    _serverProcess.Start();
                    Core.Logger.Info($"Started RhinoMcpServer via dotnet run: {csproj}");
                }
            }
            catch (System.Exception ex)
            {
                Core.Logger.Error("Failed to start RhinoMcpServer: " + ex.Message);
            }
        }

        private void TryStopRhinoMcpServer()
        {
            try
            {
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    _serverProcess.Kill();
                    _serverProcess.Dispose();
                    _serverProcess = null;
                    Core.Logger.Info("Stopped RhinoMcpServer.");
                }
            }
            catch (System.Exception ex)
            {
                Core.Logger.Error("Failed to stop RhinoMcpServer: " + ex.Message);
            }
        }
    }
}
