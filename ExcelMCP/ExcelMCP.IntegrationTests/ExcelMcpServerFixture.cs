using System.Diagnostics;
using System.Net.Sockets;
using Xunit;

namespace ExcelMCP.IntegrationTests;

public sealed class ExcelMcpServerFixture : IAsyncLifetime
{
    private Process? _process;
    private readonly List<string> _stdout = new();
    private readonly List<string> _stderr = new();

    public string BaseUrl { get; private set; } = string.Empty;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        var projectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ExcelMCP.csproj"));

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --no-build --project \"{projectPath}\" --configuration Release --urls {BaseUrl}",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) lock (_stdout) _stdout.Add(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) lock (_stderr) _stderr.Add(e.Data); };
        if (!_process.Start())
            throw new InvalidOperationException("Failed to start ExcelMCP process.");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        Client = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(15) };
        await WaitForServerAsync();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_process is null)
            return;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            // ignore teardown failures
        }
        finally
        {
            _process.Dispose();
        }
    }

    public string DumpLogs()
    {
        lock (_stdout)
        lock (_stderr)
        {
            return string.Join(Environment.NewLine, _stdout.Concat(new[] { "--- STDERR ---" }).Concat(_stderr));
        }
    }

    private async Task WaitForServerAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await Client.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (Exception ex)
            {
                last = ex;
            }

            if (_process?.HasExited == true)
                throw new InvalidOperationException($"ExcelMCP process exited early.{Environment.NewLine}{DumpLogs()}", last);

            await Task.Delay(500);
        }

        throw new TimeoutException($"Timed out waiting for ExcelMCP at {BaseUrl}.{Environment.NewLine}{DumpLogs()}");
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}

[CollectionDefinition(nameof(ExcelMcpServerCollection))]
public sealed class ExcelMcpServerCollection : ICollectionFixture<ExcelMcpServerFixture>
{
}
