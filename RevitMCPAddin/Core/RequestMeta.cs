// ================================================================
// File: Core/RequestMeta.cs
// Purpose: すべてのコマンドで使える共通メタ情報
// Notes  : SnapshotOptions は Core/Snapshot/SnapshotOptions.cs を参照
// ================================================================
#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    public sealed class RequestMeta
    {
        // 相関ID（ログ・トレース用）
        [JsonProperty("requestId")]
        public string? RequestId { get; set; }

        // スナップショット出力フラグ（共通前後処理で使用）
        [JsonProperty("exportSnapshot")]
        public bool ExportSnapshot { get; set; } = false;

        // スナップショットを出さない場合でも、マニフェストだけ同梱するか
        [JsonProperty("includeManifest")]
        public bool IncludeManifest { get; set; } = false;

        // スナップショットの細かい指定（保存先/カテゴリ/サンプル行など）
        [JsonProperty("snapshotOptions")]
        public SnapshotOptions? SnapshotOptions { get; set; } = new SnapshotOptions();

        // 単位モード（既定：SI）
        [JsonProperty("unitsMode")]
        public string? UnitsMode { get; set; } = "SI";

        // 要約粒度（none|min|default|max）
        [JsonProperty("summaryLevel")]
        public string? SummaryLevel { get; set; } = "default";

        // 将来拡張用：未知キーをここに吸収（破壊的変更を避ける）
        [JsonExtensionData]
        public IDictionary<string, JToken>? Extensions { get; set; }
    }
}
