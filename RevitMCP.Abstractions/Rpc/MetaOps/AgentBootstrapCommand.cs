// ================================================================
// File: RevitMCPAbstraction/Rpc/MetaOps/AgentBootstrapCommand.cs
// Purpose : JSON-RPC "agent_bootstrap" のメタ定義（Server側は説明だけ）
// Note    : 実処理は Revit Add-in 側。Server は /rpc → キュー → Add-in へ委譲。
// Depends : RevitMCP.Abstractions.Rpc (RpcCommandBase)
// ================================================================
#nullable enable
using System.Text.Json;
using System.Threading.Tasks;
using RevitMCP.Abstractions.Rpc;           // ← これが必須（基底クラスの名前空間）
using RevitMCPAbstraction.Models;          // 型情報（Params/Result）を露出したい時に

namespace RevitMCPAbstraction.Rpc.MetaOps
{
    // 型情報をOpenAPIに出したいならジェネリック基底でもOK：
    // public sealed class AgentBootstrapCommand : RpcCommandBase<AgentBootstrapRequest, AgentBootstrapResponse>
    public sealed class AgentBootstrapCommand : RevitMCP.Abstractions.Rpc.RpcCommandBase
    {
        public override string Name => "agent_bootstrap";

        // OpenAPIで型を出したくない場合はそのまま（null）。
        // 出したい場合はジェネリック基底を使うか、下2行を有効化。
        // public override System.Type? ParamsType => typeof(AgentBootstrapRequest);
        // public override System.Type? ResultType => typeof(AgentBootstrapResponse);

        // RpcCommandBase は protected abstract なので修飾子は protected override に合わせる
        protected override Task<object> ProcessAsync(JsonElement? param)
        {
            // Serverプロセス内では実行せず、ガイダンスを返す。
            // （基底の ExecuteAsync が { ok:true, result: ... } でラップします）
            var guidance = new
            {
                note = "This method is executed inside the Revit Add-in process, not here.",
                howTo = new
                {
                    request = "POST /rpc/agent_bootstrap with {jsonrpc:\"2.0\", method:\"agent_bootstrap\", params:{}, id:1}",
                    poll = "GET /get_result until 200 with JSON-RPC result (204 if not ready)"
                }
            };
            return Task.FromResult<object>(guidance);
        }
    }
}

