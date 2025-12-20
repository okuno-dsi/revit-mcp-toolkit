// ================================================================
// File: Core/RequestCommand.cs
// Purpose: JSON-RPC 要求の正規化（method/command 両対応）＋ meta 対応
// Target : .NET Framework 4.8 / C# 8 / Newtonsoft.Json
// Notes  : RequestMeta / SnapshotOptions は別ファイル定義を参照する
// ================================================================
#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    public class RequestCommand
    {
        [JsonProperty("jsonrpc")]
        public string Jsonrpc { get; set; } = "2.0";

        // 数値/文字列どちらも来るため JToken で保持
        [JsonProperty("id")]
        public JToken? Id { get; set; }

        // 入力: "method" / "command" のどちらが来ても Command に正規化
        [JsonProperty("method")]
        private string? MethodWire
        {
            get => Command;
            set { if (!string.IsNullOrWhiteSpace(value)) Command = value!; }
        }

        [JsonProperty("command")]
        private string? CommandWire
        {
            get => Command;
            set { if (!string.IsNullOrWhiteSpace(value)) Command = value!; }
        }

        // ★ 後方互換の公開エイリアス（外部コードから cmd.Method を参照していても動く）
        [JsonIgnore]
        public string Method
        {
            get => Command;
            set => Command = value ?? "";
        }

        // 正規化後のコマンド名（常にここを見る）
        [JsonIgnore]
        public string Command { get; set; } = "";

        // params は必ず JObject に統一（null/配列/プリミティブは {} に補正）
        [JsonProperty("params")]
        public JToken? ParamsRaw { get; set; }

        [JsonIgnore]
        public JObject Params
        {
            get
            {
                if (ParamsRaw is JObject jo) return jo;
                return new JObject(); // null/配列/プリミティブ → 空JObject
            }
            set { ParamsRaw = value; }
        }

        // meta は JObject で受けてから型に寄せる（既定値は別ファイルの RequestMeta 側）
        [JsonProperty("meta")]
        public JToken? MetaRaw { get; set; }

        [JsonIgnore]
        public RequestMeta Meta
        {
            get
            {
                if (MetaRaw is JObject jo)
                {
                    try
                    {
                        var m = jo.ToObject<RequestMeta>();
                        return m ?? new RequestMeta();
                    }
                    catch { return new RequestMeta(); }
                }
                return new RequestMeta();
            }
            set
            {
                MetaRaw = JObject.FromObject(value ?? new RequestMeta());
            }
        }
    }
}
