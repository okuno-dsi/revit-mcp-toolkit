namespace RevitMcp.Shared
{
    public sealed class RpcResult<T>
    {
        public bool ok { get; init; }
        public string code { get; init; } = "OK";
        public string msg  { get; init; } = "OK";
        public T? data     { get; init; }
        public static RpcResult<T> Ok(T? data = default, string msg = "OK") => new() { ok=true, code="OK", msg=msg, data=data };
        public static RpcResult<T> Fail(string code, string msg)            => new() { ok=false, code=code, msg=msg };
    }
}

