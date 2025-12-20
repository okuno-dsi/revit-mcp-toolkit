// File: RevitMCP.Abstractions/Rpc/RpcRouter.cs
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Abstractions.Rpc
{
    /// <summary>
    /// メソッド名→コマンドの登録/解決/実行を行うルーター。
    /// - Abstractions 層：登録とディスパッチのみを担当
    /// - Write コマンドは __smoke_ok が無ければブロック（安全ゲート）
    /// </summary>
    public sealed class RpcRouter
    {
        private readonly Dictionary<string, IRpcCommand> _commands =
            new Dictionary<string, IRpcCommand>(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new object();

        /// <summary>コマンドを登録</summary>
        public void Register(IRpcCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            lock (_gate)
            {
                _commands[command.Name] = command;
            }
        }

        /// <summary>登録済みコマンドの解決を試みる</summary>
        public bool TryGet(string method, out IRpcCommand command)
        {
            lock (_gate)
            {
                return _commands.TryGetValue(method, out command);
            }
        }

        /// <summary>登録コマンドを列挙（自己記述用）</summary>
        public IReadOnlyDictionary<string, IRpcCommand> GetAllCommands()
        {
            // スナップショットを返す（外部から変更できないようにコピー）
            lock (_gate)
            {
                return new Dictionary<string, IRpcCommand>(_commands);
            }
        }

        /// <summary>
        /// コマンドを実行（サーバー側で利用）
        /// ※ smoke_test は特例としてそのまま呼ぶ
        /// </summary>
        public object Execute(string method, JObject args)
        {
            if (string.IsNullOrWhiteSpace(method))
                return new { ok = false, error = "invalid_method", msg = "method is required" };

            // args が null の可能性に備える（C#7.3 なので nullable 使わず明示処理）
            if (args == null) args = new JObject();

            // smoke_test は特例: 登録されていれば直接呼ぶ
            if (string.Equals(method, "smoke_test", StringComparison.OrdinalIgnoreCase))
            {
                IRpcCommand smokeCmd;
                if (TryGet("smoke_test", out smokeCmd))
                    return smokeCmd.Execute(args);
                return new { ok = false, error = "unregistered", msg = "smoke_test not registered" };
            }

            // コマンド存在確認
            IRpcCommand cmd;
            if (!TryGet(method, out cmd))
                return new { ok = false, error = "unknown_command", msg = $"Unknown command: {method}" };

            // Write 系は __smoke_ok 必須
            if (cmd.Kind == RpcCommandKind.Write)
            {
                bool smoked = false;
                var token = args["__smoke_ok"];
                if (token != null)
                {
                    bool b;
                    if (bool.TryParse(token.ToString(), out b)) smoked = b;
                }

                if (!smoked)
                {
                    return new
                    {
                        ok = false,
                        error = "smoke_required",
                        msg = $"Smoke test required before executing '{method}'.",
                        severity = "error"
                    };
                }
            }

            // 実際のコマンド実行
            try
            {
                return cmd.Execute(args);
            }
            catch (Exception ex)
            {
                return new { ok = false, error = "execution_error", msg = ex.Message };
            }
        }
    }
}
