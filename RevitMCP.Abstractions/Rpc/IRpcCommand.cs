// File: RevitMCP.Abstractions/Rpc/IRpcCommand.cs  (C#7.3対応版)
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Abstractions.Rpc
{
    public enum RpcCommandKind
    {
        Read,
        Write
    }

    public interface IRpcCommand
    {
        string Name { get; }
        RpcCommandKind Kind { get; }
        Type ParamsType { get; }
        Type ResultType { get; }
        Task<string> ExecuteAsync(JsonElement? param);
        object Execute(JObject args);
    }

    // Base class providing default behaviors for RPC commands
    public abstract class RpcCommandBase : IRpcCommand
    {
        public abstract string Name { get; }
        public virtual RpcCommandKind Kind => RpcCommandKind.Read;
        public virtual Type ParamsType => null;
        public virtual Type ResultType => null;

        protected abstract Task<object> ProcessAsync(JsonElement? param);

        public async Task<string> ExecuteAsync(JsonElement? param)
        {
            try
            {
                var resultObj = await ProcessAsync(param).ConfigureAwait(false);
                var response = new { ok = true, result = resultObj };
                return JsonSerializer.Serialize(response, DefaultJsonOptions);
            }
            catch (Exception ex)
            {
                var errorResponse = new { ok = false, error = ex.GetType().Name, msg = ex.Message };
                return JsonSerializer.Serialize(errorResponse, DefaultJsonOptions);
            }
        }

        public object Execute(JObject args)
        {
            JsonElement? elem = null;
            if (args != null)
            {
                using (var doc = JsonDocument.Parse(args.ToString(Newtonsoft.Json.Formatting.None)))
                    elem = doc.RootElement.Clone();
            }

            var json = ExecuteAsync(elem).GetAwaiter().GetResult();
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<object>(json, DefaultJsonOptions)
                       ?? new { ok = false, error = "deserialize_null", msg = "No content" };
            }
            catch (Exception ex)
            {
                return new { ok = false, error = "deserialize_error", msg = ex.Message, raw = json };
            }
        }

        protected static readonly JsonSerializerOptions DefaultJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>JsonElement から任意型へ安全にデシリアライズ（失敗時は例外を投げる）</summary>
        protected static T DeserializeParams<T>(JsonElement? param)
        {
            if (param == null)
                return default(T);

            using (var ms = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(ms))
                    param.Value.WriteTo(writer);
                var bytes = ms.ToArray();
                return System.Text.Json.JsonSerializer.Deserialize<T>(bytes, DefaultJsonOptions);
            }
        }
    }

    public abstract class RpcCommandBase<TParams, TResult> : RpcCommandBase
    {
        public override Type ParamsType => typeof(TParams);
        public override Type ResultType => typeof(TResult);

        /// <summary>型付きの処理本体を実装してください。</summary>
        protected abstract Task<TResult> ProcessAsync(TParams param);

        /// <summary>JsonElement? を TParams に変換して型付き実装へ委譲</summary>
        protected sealed override async Task<object> ProcessAsync(JsonElement? param)
        {
            var typed = DeserializeParams<TParams>(param);
            var result = await ProcessAsync(typed).ConfigureAwait(false);
            return (object)result ?? new object();
        }
    }
}
