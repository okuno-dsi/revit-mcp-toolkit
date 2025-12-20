// ================================================================
// File: Core/CommandLogModels.cs
// Target : .NET Framework 4.8 / C# 8
// Purpose: コマンドログ（人間可読サマリ＋再実行スニペット＋前値）
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core
{
    /// <summary>再実行用の最小スニペット（微修正して使える）</summary>
    public class ReplaySnippet
    {
        [JsonProperty("jsonrpc")] public string JsonRpc => "2.0";
        [JsonProperty("method")] public string Method { get; set; } = "";
        [JsonProperty("params")] public object Params { get; set; } = new { };
    }

    /// <summary>“元に戻す手掛かり”としての前値（完全復元はUndoを使用）</summary>
    public class BeforeState
    {
        // パラメータ更新の前値
        public string? ParamNameOrId { get; set; }
        public string? OldParamStorageType { get; set; } // Double|Integer|String|ElementId
        public object? OldParamValue { get; set; }

        // 位置・角度・タイプ等の前値
        public int? ElementId { get; set; }
        public (double x, double y, double z)? OldLocationMm { get; set; }
        public double? OldAngleDeg { get; set; }
        public int? OldTypeId { get; set; }
    }

    /// <summary>1操作のログ（JSONL 1行）</summary>
    public class CommandLogEntry
    {
        public DateTimeOffset Ts { get; set; }                   // 完了時刻（ローカルTZ）
        public string Session { get; set; } = "";                // 起動PIDなど
        public string User { get; set; } = "";                   // MACHINE\USER
        public string Command { get; set; } = "";                // 実行コマンド名（jsonrpc method）
        public object Params { get; set; } = new { };            // 実行時パラメータ（サニタイズ済み）
        public string Summary { get; set; } = "";                // 人間向けサマリ
        public List<int> AffectedElementIds { get; set; } = new List<int>();
        public object Result { get; set; } = new { ok = true };  // 返却（ok/msg など）
        public ReplaySnippet Replay { get; set; } = new ReplaySnippet(); // 微修正→再実行用
        public BeforeState? Before { get; set; }                 // 元に戻す手掛かり（前値）
    }

    /// <summary>ログ用メタをハンドラから渡す最小インタフェース</summary>
    public interface ICommandLogMeta
    {
        string Summary { get; }               // 人間向け要約（短文）
        IEnumerable<int> AffectedIds { get; } // 影響した ElementId の列
        BeforeState? Before { get; }          // 前値（任意）
        object ReplayParams { get; }          // 同一コマンドで再実行するための params
    }
}
