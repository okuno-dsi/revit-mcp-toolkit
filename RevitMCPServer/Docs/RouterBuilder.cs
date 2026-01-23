// File: RevitMcpServer/Docs/RouterBuilder.cs
#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using RevitMCP.Abstractions.Rpc;

namespace RevitMcpServer.Docs
{
    /// <summary>AppDomain 内から IRpcCommand/ローカルコマンドを自動登録</summary>
    public static class RouterBuilder
    {
        public static RpcRouter Build()
        {
            var router = new RpcRouter();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            // 1) IRpcCommand 実装はそのまま登録（Server/Abstractions両方）
            var iType = typeof(IRpcCommand);
            foreach (var asm in assemblies)
            {
                foreach (var t in SafeGetTypes(asm))
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!iType.IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;

                    var obj = Activator.CreateInstance(t) as IRpcCommand;
                    if (obj != null) router.Register(obj);
                }
            }

            // 2) サーバー内のローカル RpcCommandBase 派生をアダプタで登録（型名一致で探索）
            var localBase = FindLocalRpcBase(assemblies);
            if (localBase != null)
            {
                foreach (var asm in assemblies)
                {
                    foreach (var t in SafeGetTypes(asm))
                    {
                        if (t.IsAbstract || t.IsInterface) continue;
                        if (!IsSubclassOf(t, localBase)) continue;
                        if (t.GetConstructor(Type.EmptyTypes) == null) continue;

                        var inner = Activator.CreateInstance(t);
                        if (inner == null) continue;
                        var adapter = new LocalAdapter(inner, localBase);
                        router.Register(adapter);
                    }
                }
            }
            return router;
        }

        private static Type[] SafeGetTypes(Assembly a)
        {
            try { return a.GetTypes(); } catch { return new Type[0]; }
        }

        private static bool IsSubclassOf(Type t, Type baseType)
        {
            var cur = t.BaseType;
            while (cur != null && cur != typeof(object))
            {
                if (cur == baseType) return true;
                cur = cur.BaseType;
            }
            return false;
        }

        private static Type? FindLocalRpcBase(Assembly[] assemblies)
        {
            foreach (var asm in assemblies)
            {
                foreach (var t in SafeGetTypes(asm))
                {
                    if (!t.IsAbstract) continue;
                    if (t.Name != "RpcCommandBase") continue;
                    // Abstractions 側の RpcCommandBase は除外
                    if (t.Namespace == typeof(RevitMCP.Abstractions.Rpc.IRpcCommand).Namespace) continue;
                    if (t.GetMethod("ProcessAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null) continue;
                    if (t.GetProperty("Name", BindingFlags.Instance | BindingFlags.Public) == null) continue;
                    return t;
                }
            }
            return null;
        }

        /// <summary>サーバー内ローカルコマンド → IRpcCommand アダプタ</summary>
        private sealed class LocalAdapter : IRpcCommand
        {
            private readonly object _inner;
            private readonly MethodInfo _processAsync;
            private readonly PropertyInfo _nameProp;

            public LocalAdapter(object inner, Type baseType)
            {
                _inner = inner;
                var tp = inner.GetType();
                _processAsync = tp.GetMethod("ProcessAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(System.Text.Json.JsonElement?) }, null)
                    ?? throw new MissingMethodException(tp.FullName, "ProcessAsync(JsonElement?)");
                _nameProp = tp.GetProperty("Name", BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new MissingMemberException(tp.FullName, "Name");
            }

            public string Name => (string)(_nameProp.GetValue(_inner) ?? "(unnamed)");
            public RpcCommandKind Kind => RpcCommandKind.Read;
            public Type ParamsType => typeof(object);
            public Type ResultType => typeof(object);

            public async Task<string> ExecuteAsync(JsonElement? param)
            {
                var r = _processAsync.Invoke(_inner, new object?[] { param });
                var t = r as Task<object>;
                if (t != null)
                {
                    var res = await t.ConfigureAwait(false);
                    return JsonSerializer.Serialize(new { ok = true, result = res });
                }
                return JsonSerializer.Serialize(new { ok = false, error = "Unexpected ProcessAsync result type." });
            }

            public object Execute(Newtonsoft.Json.Linq.JObject args)
            {
                System.Text.Json.JsonElement? elem = null;
                if (args != null)
                {
                    using (var doc = System.Text.Json.JsonDocument.Parse(args.ToString(Newtonsoft.Json.Formatting.None)))
                        elem = doc.RootElement.Clone();
                }

                var json = ExecuteAsync(elem).GetAwaiter().GetResult();
                try
                {
                    var opts = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                        WriteIndented = false
                    };
                    return System.Text.Json.JsonSerializer.Deserialize<object>(json, opts)
                           ?? new { ok = false, error = "deserialize_null", msg = "No content" };
                }
                catch (Exception ex)
                {
                    return new { ok = false, error = "deserialize_error", msg = ex.Message, raw = json };
                }
            }
        }
    }
}
