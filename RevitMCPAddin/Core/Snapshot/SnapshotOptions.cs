// ================================================================
// File: Core/Snapshot/SnapshotOptions.cs
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;

namespace RevitMCPAddin.Core
{
    public sealed class SnapshotOptions
    {
        // 保存先: %LOCALAPPDATA%\RevitMCP\data\{snapshotId}
        public string? BaseDir { get; set; } = null;
        // 対象カテゴリ（例: "Walls","Rooms","Doors"...）
        public List<string> Categories { get; set; } = new List<string> { "Walls", "Rooms" };
        // 取得列（軽量属性）— 未設定ならデフォルト列
        public List<string>? IncludeParams { get; set; }
        // サンプル行数（manifestに入れるプレビュー用）
        public int SampleRows { get; set; } = 3;
        // 1カテゴリあたりの最大行数（重い時の安全弁。0=無制限）
        public int LimitPerCategory { get; set; } = 0;
        // 事前/事後スナップショット両方を保存するか
        public bool SavePreAndPost { get; set; } = true;
        // 幾何は含めない（必要時は別コマンド）
        public bool ExcludeHeavyGeometry { get; set; } = true;
    }
}
