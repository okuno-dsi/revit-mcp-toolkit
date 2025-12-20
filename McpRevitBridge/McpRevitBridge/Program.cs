// ================================================================
// File: Program.cs  (C# 8 / .NET Framework 4.8 互換)
// Purpose : Minimal JSON-RPC bridge (STDIN ⇄ HTTP)
// Summary :
//   - 1行=1メッセージ（JSON-RPC Object または Array(batch)）
//   - STDIN が閉じられたら静かに正常終了
//   - STDOUT 側が閉じられていれば（IOException）静かに終了
//   - 例外は JSON-RPC error で返す（出力不能ならそのまま終了）
//   - --url http://127.0.0.1:5210/rpc
//   - --timeout-ms 60000
//   - --header X-Api-Key:abcdef  （複数指定可）
//
// 依存: Newtonsoft.Json (JToken/JObject/JArray)
// ================================================================

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

internal static class Program
{
    private sealed class Options
    {
        public Uri RpcUri;
        public int TimeoutMs;
        public List<KeyValuePair<string, string>> Headers;

        public Options()
        {
            RpcUri = new Uri("http://127.0.0.1:5210/rpc");
            TimeoutMs = 60000;
            Headers = new List<KeyValuePair<string, string>>();
        }
    }

    public static async Task<int> Main(string[] args)
    {
        // ---- グローバル例外ガード ------------------------------------------
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                var msg = (e.ExceptionObject != null) ? e.ExceptionObject.ToString() : "UnhandledException";
                var env = ErrorEnvelope(null, -32001, msg);
                TryWriteJson(env);
            }
            catch { /* 出力不能なら諦める */ }
            Environment.Exit(1);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            try
            {
                var env = ErrorEnvelope(null, -32001, e.Exception != null ? e.Exception.ToString() : "UnobservedTaskException");
                TryWriteJson(env);
            }
            catch { }
            e.SetObserved();
            Environment.Exit(1);
        };
        // --------------------------------------------------------------------

        Options opt;
        try
        {
            opt = ParseArgs(args);
        }
        catch (Exception ex)
        {
            TryWriteJson(ErrorEnvelope(null, -32602, "Invalid params: " + ex.Message));
            return 2;
        }

        var handler = new HttpClientHandler();
        handler.UseCookies = false;

        using (var http = new HttpClient(handler))
        {
            http.Timeout = TimeSpan.FromMilliseconds(opt.TimeoutMs);
            foreach (var kv in opt.Headers)
            {
                if (http.DefaultRequestHeaders.Contains(kv.Key))
                    http.DefaultRequestHeaders.Remove(kv.Key);
                http.DefaultRequestHeaders.Add(kv.Key, kv.Value);
            }

            // STDIN ループ
            while (true)
            {
                string line;
                try
                {
                    line = Console.ReadLine();
                }
                catch (IOException)
                {
                    // 入力パイプ破断 → 終了
                    break;
                }

                if (line == null) // EOF
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    // 先頭非空白で batch 判定
                    char first = FirstNonWhitespace(line);
                    if (first == '[')
                    {
                        // ---- batch ----
                        JArray batch = null;
                        try
                        {
                            batch = JArray.Parse(line);
                        }
                        catch
                        {
                            TryWriteJson(ErrorEnvelope(null, -32700, "Parse error (batch)."));
                            continue;
                        }
                        if (batch == null)
                        {
                            TryWriteJson(ErrorEnvelope(null, -32700, "Parse error (batch=null)."));
                            continue;
                        }

                        var results = new JArray();
                        foreach (var node in batch)
                        {
                            var tuple = await ForwardOneAsync(http, opt.RpcUri, node);
                            bool ok = tuple.ok;
                            JToken id = tuple.id;
                            JToken payload = tuple.payload;

                            if (ok)
                                results.Add(payload);
                            else
                                results.Add(ErrorEnvelope(id, -32000, payload != null ? payload.ToString(Newtonsoft.Json.Formatting.None) : "Unknown error"));
                        }

                        TryWriteJson(results);
                    }
                    else
                    {
                        // ---- 単発 ----
                        JToken req = null;
                        try
                        {
                            req = JToken.Parse(line);
                        }
                        catch
                        {
                            TryWriteJson(ErrorEnvelope(null, -32700, "Parse error."));
                            continue;
                        }

                        var tuple = await ForwardOneAsync(http, opt.RpcUri, req);
                        if (tuple.ok)
                            TryWriteJson(tuple.payload);
                        else
                            TryWriteJson(ErrorEnvelope(tuple.id, -32000, tuple.payload != null ? tuple.payload.ToString(Newtonsoft.Json.Formatting.None) : "Unknown error"));
                    }
                }
                catch (IOException)
                {
                    // STDOUT 側が閉じられていた → 静かに終了
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    TryWriteJson(ErrorEnvelope(null, -32001, ex.Message));
                }
            }
        }

        return 0;
    }

    // ------------------------------------------------------------
    // HTTP 転送：req(JToken) をそのまま JSON-RPC サーバーへ POST
    // 戻り値: ok=true  → payload はサーバーの JSON（JToken）そのまま
    //        ok=false → payload は err 情報（JObject）など
    // ------------------------------------------------------------
    private static async Task<(bool ok, JToken id, JToken payload)> ForwardOneAsync(HttpClient http, Uri rpcUri, JToken req)
    {
        JToken id = null;
        try
        {
            if (req != null && req.Type == JTokenType.Object)
            {
                var obj = (JObject)req;
                if (obj.TryGetValue("id", out var idNode))
                    id = idNode;
            }

            using (var content = new StringContent(req != null ? req.ToString(Newtonsoft.Json.Formatting.None) : "{}", Encoding.UTF8, "application/json"))
            using (var res = await http.PostAsync(rpcUri, content).ConfigureAwait(false))
            {
                var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

                JToken parsed = null;
                try { parsed = JToken.Parse(body); } catch { /* 非JSONはそのまま文字列で扱う */ }

                if (!res.IsSuccessStatusCode)
                {
                    var msg = "HTTP " + ((int)res.StatusCode).ToString() + " " + res.ReasonPhrase;
                    if (parsed != null)
                    {
                        var obj = new JObject();
                        obj["http"] = msg;
                        obj["body"] = parsed;
                        return (false, id, obj);
                    }
                    else
                    {
                        var obj = new JObject();
                        obj["http"] = msg;
                        obj["bodyText"] = body;
                        return (false, id, obj);
                    }
                }

                return (true, id, parsed ?? (JToken)new JObject(new JProperty("ok", true), new JProperty("raw", body)));
            }
        }
        catch (TaskCanceledException tex)
        {
            var obj = new JObject();
            obj["timeout"] = true;
            obj["message"] = tex.Message;
            return (false, id, obj);
        }
        catch (Exception ex)
        {
            var obj = new JObject();
            obj["exception"] = ex.GetType().Name;
            obj["message"] = ex.Message;
            return (false, id, obj);
        }
    }

    // JSON-RPC エラーオブジェクト
    private static JObject ErrorEnvelope(JToken id, int code, string message)
    {
        var env = new JObject();
        env["jsonrpc"] = "2.0";
        env["id"] = (id != null) ? id : JValue.CreateNull();

        var err = new JObject();
        err["code"] = code;
        err["message"] = message ?? "error";

        env["error"] = err;
        return env;
    }

    // 安全出力（STDOUT クローズ検知で静かに終了）
    private static bool TryWriteJson(JToken token)
    {
        try
        {
            Console.WriteLine(token.ToString(Newtonsoft.Json.Formatting.None));
            Console.Out.Flush();
            return true;
        }
        catch (IOException)
        {
            Environment.Exit(0);
            return false; // 到達しない
        }
    }

    private static char FirstNonWhitespace(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (!char.IsWhiteSpace(c)) return c;
        }
        return '\0';
    }

    private static Options ParseArgs(string[] args)
    {
        var url = (Uri)null;
        int? timeoutMs = null;
        var headers = new List<KeyValuePair<string, string>>();

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--url" && i + 1 < args.Length)
            {
                url = new Uri(args[++i], UriKind.Absolute);
            }
            else if (a == "--timeout-ms" && i + 1 < args.Length)
            {
                int t;
                if (!int.TryParse(args[++i], out t) || t <= 0) throw new ArgumentException("timeout-ms must be positive integer.");
                timeoutMs = t;
            }
            else if (a == "--header" && i + 1 < args.Length)
            {
                var kv = args[++i];
                var idx = kv.IndexOf(':');
                if (idx <= 0) throw new ArgumentException("header must be 'Key:Value' format.");
                var key = kv.Substring(0, idx).Trim();
                var val = kv.Substring(idx + 1).Trim();
                if (string.IsNullOrEmpty(key)) throw new ArgumentException("header key is empty.");
                headers.Add(new KeyValuePair<string, string>(key, val));
            }
            else if (a.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Unknown option: " + a);
            }
        }

        var opt = new Options();
        opt.RpcUri = (url != null) ? url : new Uri("http://127.0.0.1:5210/rpc");
        opt.TimeoutMs = timeoutMs.HasValue ? timeoutMs.Value : 60000;
        foreach (var h in headers) opt.Headers.Add(h);
        return opt;
    }
}
