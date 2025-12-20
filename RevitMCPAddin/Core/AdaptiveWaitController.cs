#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Newtonsoft.Json;

namespace RevitMCPAddin.Core
{
    public enum WaitMode { Auto, Manual }

    public sealed class AdaptiveWaitController
    {
        public static AdaptiveWaitController Instance { get; } = new AdaptiveWaitController();

        private readonly object _lock = new object();

        // チューニングしやすい既定値
        private const int DefaultMs = 200;
        private const int MinMs = 100;
        private const int MaxMs = 600;

        // AIMD / EWMA パラメータ
        private const double EwmaAlpha = 0.25;       // 0.0(重い平滑)〜1.0(生の値)
        private const int AdditiveStep = 30;        // 空振り等で増加させるときの加算値
        private const double MultDecrease = 0.70;   // 仕事あり等で減らす乗数 (<1.0)

        // クールダウン（ms）
        private const int CooldownMs = 250;

        // 観測値（EWMA）
        private double _ewmaProcessedPerSec = 0.0;
        private double _ewmaEmptyRatio = 1.0;
        private double _ewmaExecMs = DefaultMs;

        // 状態
        private int _currentMs = DefaultMs;
        private DateTime _lastAdjust = DateTime.MinValue;

        // UI/設定
        public WaitMode Mode { get; private set; } = WaitMode.Auto;
        public int ManualMs { get; private set; } = DefaultMs;

        // 統計（UI表示/ログ用）
        public long TotalPolls { get; private set; }
        public long EmptyPolls { get; private set; }
        public long TotalProcessed { get; private set; }
        public long Timeouts { get; private set; }
        public long TxnConflicts { get; private set; }

        private AdaptiveWaitController() { Load(); }

        public int GetCurrentDelayMs()
        {
            lock (_lock)
            {
                return Mode == WaitMode.Manual ? ManualMs : _currentMs;
            }
        }

        public void ReportPoll(bool gotWork)
        {
            lock (_lock)
            {
                TotalPolls++;
                if (!gotWork) EmptyPolls++;
            }
        }

        public void ReportProcessed(int count, int execMs)
        {
            lock (_lock)
            {
                TotalProcessed += count;
                _ewmaProcessedPerSec = Ewma(_ewmaProcessedPerSec, count * 1000.0 / Math.Max(execMs, 1), EwmaAlpha);
                _ewmaExecMs = Ewma(_ewmaExecMs, execMs, EwmaAlpha);
            }
        }

        public void ReportTimeout() { lock (_lock) Timeouts++; }
        public void ReportTxnConflict() { lock (_lock) TxnConflicts++; }

        public void AdjustIfNeeded()
        {
            lock (_lock)
            {
                if (Mode == WaitMode.Manual) return;
                if ((DateTime.UtcNow - _lastAdjust).TotalMilliseconds < CooldownMs) return;

                var emptyRatio = TotalPolls == 0 ? 1.0 : (double)EmptyPolls / TotalPolls;
                _ewmaEmptyRatio = Ewma(_ewmaEmptyRatio, emptyRatio, EwmaAlpha);

                int next = _currentMs;

                // 仕事が取れている or 実行時間が長い → レイテンシ優先 = 少し攻める
                bool hadWork = TotalProcessed > 0 && (TotalPolls - EmptyPolls) > 0;
                if (hadWork || _ewmaExecMs > 300)
                {
                    next = (int)Math.Round(_currentMs * MultDecrease);
                }

                // 空振り率が高い or タイムアウト/衝突 → 少し待ちを増やす
                if (_ewmaEmptyRatio > 0.8 || Timeouts > 0 || TxnConflicts > 0)
                {
                    next = _currentMs + AdditiveStep;
                }

                // クランプ
                next = Math.Max(MinMs, Math.Min(MaxMs, next));

                if (next != _currentMs)
                {
                    _currentMs = next;
                    _lastAdjust = DateTime.UtcNow;
                }

                // 窓つきリセット（飽和防止）
                if (TotalPolls >= 200)
                {
                    TotalPolls = EmptyPolls = 0;
                    TotalProcessed = 0;
                    Timeouts = TxnConflicts = 0;
                }
            }
        }

        public void SetManual(int ms)
        {
            lock (_lock)
            {
                Mode = WaitMode.Manual;
                ManualMs = Clamp(ms);
                Save();
            }
        }

        public void SetAuto()
        {
            lock (_lock)
            {
                Mode = WaitMode.Auto;
                _currentMs = Clamp(ManualMs);
                Save();
            }
        }

        public void NudgePercent(double pct) // -0.1 = -10%, +0.1 = +10%
        {
            lock (_lock)
            {
                if (Mode == WaitMode.Manual)
                {
                    ManualMs = Clamp((int)Math.Round(ManualMs * (1.0 + pct)));
                }
                else
                {
                    _currentMs = Clamp((int)Math.Round(_currentMs * (1.0 + pct)));
                }
                Save();
            }
        }

        public (int currentMs, WaitMode mode, double emptyRatio, double pps, double execMs) Snapshot()
        {
            lock (_lock)
            {
                var empty = TotalPolls == 0 ? _ewmaEmptyRatio : (double)EmptyPolls / Math.Max(1, TotalPolls);
                return (GetCurrentDelayMs(), Mode, empty, _ewmaProcessedPerSec, _ewmaExecMs);
            }
        }

        private static int Clamp(int ms) => Math.Max(MinMs, Math.Min(MaxMs, ms));
        private static double Ewma(double prev, double x, double alpha) => prev == 0 ? x : (alpha * x + (1 - alpha) * prev);

        // 設定 I/O
        private string ConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "RevitMCP", "config.json");

        private sealed class Config { public string waitMode = "Auto"; public int manualMs = DefaultMs; }

        private void Load()
        {
            try
            {
                var p = ConfigPath;
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                if (File.Exists(p))
                {
                    var cfg = JsonConvert.DeserializeObject<Config>(File.ReadAllText(p)) ?? new Config();
                    Mode = (string.Equals(cfg.waitMode, "Manual", StringComparison.OrdinalIgnoreCase)) ? WaitMode.Manual : WaitMode.Auto;
                    ManualMs = Clamp(cfg.manualMs);
                }
                else Save();
            }
            catch { /* ignore */ }
        }

        private void Save()
        {
            try
            {
                var p = ConfigPath;
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                var json = JsonConvert.SerializeObject(new Config
                {
                    waitMode = Mode.ToString(),
                    manualMs = ManualMs
                }, Formatting.Indented);
                File.WriteAllText(p, json);
            }
            catch { /* ignore */ }
        }
    }
}
