// ================================================================
// File: RevitMCPAddin/Commands/MetaOps/SearchCommandsHandler.cs
// Desc: JSON-RPC "search_commands" (Add-in local) —
//       commands_lex.jsonl を読み込み、英日混在キーワードでコマンド検索
// Target: .NET Framework 4.8 / C# 8.0
// Notes: 依存を最小化（Microsoft.Extensions.* 不要 / ILogger<> 不要）
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // RequestCommand / IRevitCommandHandler / RevitLogger がある前提

namespace RevitMCPAddin.Commands.MetaOps
{
    internal sealed class LexEntry
    {
        public string name { get; set; } = "";
        public string category { get; set; } = "Other";
        public string kind { get; set; } = "read";         // read|write
        public string importance { get; set; } = "normal"; // high|normal|low
        public List<string> tokens { get; set; } = new List<string>();
        public string hint { get; set; } = "";
    }

    internal sealed class SearchParams
    {
        public string q { get; set; } = "";
        public int? top { get; set; }
        public string? category { get; set; }
        public string? kind { get; set; }           // read|write
        public string? importance { get; set; }     // high|normal|low
        public bool? prefixOnly { get; set; }
    }

    public sealed class SearchCommandsHandler : IRevitCommandHandler
    {
        public string CommandName => "search_commands";

        // キャッシュ（C#8/NET4.8向けに volatile 不使用。ロックで守る）
        private static readonly object _gate = new object();
        private static List<LexEntry>? _lex;               // スナップショット
        private static DateTime _loadedAtUtc = DateTime.MinValue;
        private static string _lexPath = ResolveLexPath();

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            try
            {
                var p = (cmd.Params as JObject) != null
                    ? ((JObject)cmd.Params!).ToObject<SearchParams>() ?? new SearchParams()
                    : new SearchParams();

                if (p == null || string.IsNullOrWhiteSpace(p.q))
                    return new { ok = false, msg = "Missing 'q'." };

                var lex = EnsureLexLoaded();
                if (lex == null || lex.Count == 0)
                    return new { ok = false, msg = "Lexicon not found or empty.", lexPath = _lexPath };

                string nq = Normalize(p.q);
                string[] qtokens = nq.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                // オプション絞り込み
                IEnumerable<LexEntry> pool = lex;
                if (!string.IsNullOrWhiteSpace(p.category))
                    pool = pool.Where(r => ContainsIgnoreCase(r.category, p.category!));
                if (!string.IsNullOrWhiteSpace(p.kind))
                    pool = pool.Where(r => string.Equals(r.kind, p.kind, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(p.importance))
                    pool = pool.Where(r => string.Equals(r.importance, p.importance, StringComparison.OrdinalIgnoreCase));

                // スコアリング
                List<Tuple<double, LexEntry>> scored = new List<Tuple<double, LexEntry>>(256);
                foreach (var r in pool)
                {
                    double s = 0.0;
                    string nameLower = r.name.ToLowerInvariant();
                    // tokens はすでに小文字想定ではないため都度 lower
                    List<string> toks = (r.tokens ?? new List<string>()).Select(t => t.ToLowerInvariant()).ToList();

                    for (int i = 0; i < qtokens.Length; i++)
                    {
                        string qt = qtokens[i];
                        bool exact = toks.Contains(qt);
                        bool partial = toks.Any(t => TokenMatch(t, qt, p.prefixOnly == true));
                        if (exact) s += 3.0;
                        else if (partial) s += 1.0;
                        if (nameLower.IndexOf(qt, StringComparison.Ordinal) >= 0) s += 2.0;
                    }
                    if (string.Equals(r.importance, "high", StringComparison.OrdinalIgnoreCase)) s *= 1.1;
                    if (string.Equals(r.kind, "read", StringComparison.OrdinalIgnoreCase)) s *= 1.02;

                    if (s > 0.0) scored.Add(Tuple.Create(s, r));
                }

                int top = Math.Max(1, Math.Min(200, p.top.HasValue ? p.top.Value : 15));
                var items = scored
                    .OrderByDescending(x => x.Item1)
                    .ThenBy(x => x.Item2.name, StringComparer.OrdinalIgnoreCase)
                    .Take(top)
                    .Select(x => new JObject
                    {
                        ["name"] = x.Item2.name,
                        ["category"] = x.Item2.category,
                        ["kind"] = x.Item2.kind,
                        ["importance"] = x.Item2.importance,
                        ["hint"] = x.Item2.hint,
                        ["tokens"] = new JArray((x.Item2.tokens ?? new List<string>()).ToArray()),
                        ["score"] = Math.Round(x.Item1, 3)
                    })
                    .ToList();

                return new JObject
                {
                    ["ok"] = true,
                    ["total"] = scored.Count,
                    ["items"] = new JArray(items.ToArray()),
                    ["loadedAtUtc"] = _loadedAtUtc.ToString("o"),
                    ["lexPath"] = _lexPath
                };
            }
            catch (Exception ex)
            {
                RevitLogger.Error("search_commands failed: " + ex);
                return new { ok = false, msg = ex.Message };
            }
        }

        // ------------------------------------------------------------
        // 読み込み & キャッシュ
        // ------------------------------------------------------------
        private static List<LexEntry>? EnsureLexLoaded()
        {
            lock (_gate)
            {
                try
                {
                    if (!File.Exists(_lexPath))
                        return null;

                    // 単純な「毎回ロード」でも十分速いが、必要なら更新時刻で軽キャッシュ化
                    DateTime now = DateTime.UtcNow;
                    if (_lex != null && (now - _loadedAtUtc).TotalSeconds < 2.0)
                        return _lex;

                    List<LexEntry> list = new List<LexEntry>(2048);
                    foreach (var line in File.ReadLines(_lexPath, Encoding.UTF8))
                    {
                        string s = (line ?? "").Trim();
                        if (s.Length == 0) continue;
                        try
                        {
                            LexEntry obj = JsonConvert.DeserializeObject<LexEntry>(s);
                            if (obj != null && !string.IsNullOrWhiteSpace(obj.name))
                                list.Add(obj);
                        }
                        catch
                        {
                            // 壊れ行はスキップ（ログは控えめ）
                        }
                    }
                    _lex = list;
                    _loadedAtUtc = now;
                    return _lex;
                }
                catch (Exception ex)
                {
                    RevitLogger.Warn("EnsureLexLoaded failed: " + ex.Message);
                    return null;
                }
            }
        }

        // ------------------------------------------------------------
        // Path 解決（Add-inフォルダ / wwwroot / 同階層 / %LOCALAPPDATA% フォールバック）
        // ------------------------------------------------------------
        private static string ResolveLexPath()
        {
            try
            {
                string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;
                string p1 = Path.Combine(baseDir, "wwwroot", "commands_lex.jsonl");
                if (File.Exists(p1)) return p1;

                string p2 = Path.Combine(baseDir, "commands_lex.jsonl");
                if (File.Exists(p2)) return p2;

                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string p3 = Path.Combine(local, "RevitMCP", "server", "wwwroot", "commands_lex.jsonl");
                if (File.Exists(p3)) return p3;

                // 最後の手段：baseDirの親も探す
                string p4 = Path.Combine(Directory.GetParent(baseDir)?.FullName ?? baseDir, "wwwroot", "commands_lex.jsonl");
                if (File.Exists(p4)) return p4;

                return p1; // 見つからなくても既定は wwwroot 配下
            }
            catch
            {
                return "commands_lex.jsonl";
            }
        }

        // ------------------------------------------------------------
        // Utils
        // ------------------------------------------------------------
        private static string Normalize(string s)
        {
            string lower = (s ?? "").Trim().ToLowerInvariant();
            // 半角/全角などの本格正規化は不要なら省略。必要なら NFKC を導入する
            // .NET Framework 4.8 でも簡単な空白圧縮で十分
            string one = System.Text.RegularExpressions.Regex.Replace(lower, @"\s+", " ");
            return one.Trim();
        }

        private static bool ContainsIgnoreCase(string hay, string needle)
        {
            if (hay == null) return false;
            if (needle == null) return true;
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TokenMatch(string token, string q, bool prefixOnly)
        {
            if (prefixOnly)
                return token.StartsWith(q, StringComparison.OrdinalIgnoreCase);
            return token.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
