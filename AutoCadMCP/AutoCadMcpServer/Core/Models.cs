using System.Text.Json.Nodes;

namespace AutoCadMcpServer.Core
{
    public class LayerStrategy
    {
        public string Mode { get; set; } = ""; // map | prefix | unify
        public Dictionary<string, string>? Map { get; set; }
        public string? Prefix { get; set; }
        public bool DeleteEmptyLayers { get; set; }

        public static LayerStrategy From(JsonObject? jo)
        {
            var ls = new LayerStrategy();
            if (jo == null) return ls;
            ls.Mode = jo["mode"]?.GetValue<string>() ?? "";
            ls.DeleteEmptyLayers = jo["deleteEmptyLayers"]?.GetValue<bool?>() ?? false;
            if (jo["map"] is JsonObject map)
                ls.Map = map.ToDictionary(kv => kv.Key, kv => kv.Value?.GetValue<string>() ?? "");
            ls.Prefix = jo["prefix"]?.GetValue<string>();
            return ls;
        }
    }

    public class OverkillOptions
    {
        public bool Enabled { get; set; }
        public double? Tolerance { get; set; }      // 幾何トレランス
        public bool OptimizeSegments { get; set; }  // セグメント最適化
        public bool CombineCollinear { get; set; }  // 直線結合
        public bool RunTwice { get; set; }          // 2回実行
        public static OverkillOptions? From(JsonObject? jo)
        {
            if (jo == null) return null;
            return new OverkillOptions
            {
                Enabled = jo["enabled"]?.GetValue<bool?>() ?? false,
                Tolerance = jo["tolerance"]?.GetValue<double?>(),
                OptimizeSegments = jo["optimizeSegments"]?.GetValue<bool?>() ?? true,
                CombineCollinear = jo["combineCollinear"]?.GetValue<bool?>() ?? true,
                RunTwice = jo["runTwice"]?.GetValue<bool?>() ?? false
            };
        }
    }

    public class TextDedupOptions
    {
        public bool Enabled { get; set; }
        public double PosTolerance { get; set; } = 1e-3;
        public int RoundDigits { get; set; } = 6;
        public static TextDedupOptions? From(JsonObject? jo)
        {
            if (jo == null) return null;
            return new TextDedupOptions
            {
                Enabled = jo["enabled"]?.GetValue<bool?>() ?? false,
                PosTolerance = jo["posTolerance"]?.GetValue<double?>() ?? 1e-3,
                RoundDigits = jo["roundDigits"]?.GetValue<int?>() ?? 6
            };
        }
    }

    public class Job
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTimeOffset EnqueuedAt { get; set; } = DateTimeOffset.Now;
        public string Kind { get; set; } = "merge";

        public List<string> Inputs { get; set; } = new();
        public string Output { get; set; } = string.Empty;
        public string AccorePath { get; set; } = string.Empty;
        public string SeedPath { get; set; } = string.Empty;
        public string Locale { get; set; } = "ja-JP";
        public int TimeoutMs { get; set; } = 600000;
        public LayerStrategy LayerStrategy { get; set; } = new();
        public string? LayTransDws { get; set; }
        public bool DoPurge { get; set; } = true;
        public bool DoAudit { get; set; } = true;
        public OverkillOptions? Overkill { get; set; }      // ★追加
        public TextDedupOptions? TextDedup { get; set; }    // ★追加
        public string StagingRoot { get; set; } = string.Empty;
        public bool KeepTempOnError { get; set; }
        public bool AtomicWrite { get; set; } = true;

        public string StagingDir => Path.Combine(StagingRoot, Id);
        public string InDir => Path.Combine(StagingDir, "in");
        public string OutDir => Path.Combine(StagingDir, "out");
        public string LogsDir => Path.Combine(StagingDir, "logs");

        public static Job CreateMergeJob(
            List<string> inputs,
            string output,
            string accorePath,
            string seed,
            string locale,
            int timeoutMs,
            LayerStrategy ls,
            string? layTransDws,
            bool doPurge,
            bool doAudit,
            OverkillOptions? overkill,
            TextDedupOptions? textDedup,
            string stagingRoot,
            bool keepTempOnError,
            bool atomicWrite)
        {
            return new Job
            {
                Kind = "merge",
                Inputs = inputs,
                Output = output,
                AccorePath = accorePath,
                SeedPath = seed,
                Locale = locale,
                TimeoutMs = timeoutMs,
                LayerStrategy = ls,
                LayTransDws = layTransDws,
                DoPurge = doPurge,
                DoAudit = doAudit,
                Overkill = overkill,
                TextDedup = textDedup,
                StagingRoot = stagingRoot,
                KeepTempOnError = keepTempOnError,
                AtomicWrite = atomicWrite
            };
        }
    }
}
