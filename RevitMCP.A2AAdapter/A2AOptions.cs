namespace RevitMCP.A2AAdapter;

public sealed class A2AOptions
{
    public int Port { get; init; } = 5220;
    public string BindHost { get; init; } = "127.0.0.1";
    public string RevitMcpServerUrl { get; init; } = "http://127.0.0.1:5210";
    public string ProtocolVersion { get; init; } = "0.3";
    public string AgentVersion { get; init; } = "0.1.0";
    public int BlockingTimeoutSeconds { get; init; } = 60;
}
