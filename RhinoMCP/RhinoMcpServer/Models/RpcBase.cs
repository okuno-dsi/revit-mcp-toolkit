namespace RhinoMcpServer.Models
{
    public class RpcRequest { public string jsonrpc { get; set; } = "2.0"; public object id { get; set; } = 1; public string method { get; set; } = ""; public object @params { get; set; } = new { }; }
}
