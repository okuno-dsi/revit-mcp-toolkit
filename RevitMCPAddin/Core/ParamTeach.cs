// ================================================================
// File: Core/ParamTeach.cs
// 目的:
//  - コマンドごとのパラメータ仕様(必須/一意/許容/エイリアス)を宣言
//  - 実行前に検証し、名前不一致を検出 → 優しく叱る＋exampleRight返却
//  - 必要に応じてエイリアスを正規化（newTypeName → newName 等）
// 使い方:
//  var err = ParamTeach.Guard(cmd.Command, cmd.Params as JObject);
//  if (err != null) return err; // 実行せず丁寧エラー
//  ※ Guard は in-place で Params を正規化します
// ================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RevitMCPAddin.Core
{
    public static class ParamTeach
    {
        // 共通で黙認するオプションキー（任意）
        private static readonly HashSet<string> CommonOptional = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "documentPath", "debug", "dryRun", "scope", "where", "target", "targets"
        };

        // メソッド別プロファイル
        private static readonly Dictionary<string, ParamProfile> Profiles =
            new Dictionary<string, ParamProfile>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "rename_wall_type",
                    new ParamProfile("rename_wall_type")
                        .Require("newName")
                        .OneOf("typeId","typeName","elementId")
                        .Alias("newName", "newTypeName","name","renameTo")
                        .Allow("typeId","typeName","elementId","newName")
                        .Example(p =>
                        {
                            // 既に与えられているID/名前があれば活かす
                            int typeId = p?.Value<int?>("typeId") ?? 12345;
                            string newName = p?.Value<string>("newName") ?? "(外壁)ECP60";
                            return new JObject {
                                ["typeId"] = typeId,
                                ["newName"] = newName
                            };
                        })
                },
                {
                    "create_text_note",
                    new ParamProfile("create_text_note")
                        .Require("viewId","positionMm","text")
                        .Alias("positionMm","pointMm","posMm")
                        .Allow(
                            "viewId","positionMm","text",
                            "typeId","typeName","textNoteTypeName",
                            "horizontalAlign","verticalAlign","wrapWidthMm","widthMm"
                        )
                        .Example(p =>
                        {
                            int viewId = p?.Value<int?>("viewId") ?? 100;
                            var pos = p?["positionMm"] as JObject
                                       ?? p?["pointMm"] as JObject
                                       ?? new JObject{["x"]=1000,["y"]=2000,["z"]=0};
                            string text = p?.Value<string>("text") ?? "注記テキスト";
                            return new JObject {
                                ["viewId"] = viewId,
                                ["positionMm"] = pos,
                                ["text"] = text
                            };
                        })
                }
                // 必要に応じてここへ他コマンドも足してください
            };

        /// <summary>
        /// 検証＆正規化。エラー時は { ok:false, code, msg, humanMessage, detail, fix{exampleRight} } を返す。
        /// 正常時は null を返し、p はエイリアス正規化済みになる。
        /// </summary>
        public static object Guard(string method, JObject p)
        {
            if (string.IsNullOrWhiteSpace(method) || p == null) return null;
            if (!Profiles.TryGetValue(method, out var prof)) return null;

            // 1) エイリアス → 正規キーへ寄せる（存在する場合のみ）
            foreach (var map in prof.AliasMap)
            {
                var canonical = map.Key;
                if (p[canonical] != null) continue;
                foreach (var alias in map.Value)
                {
                    if (p.TryGetValue(alias, StringComparison.OrdinalIgnoreCase, out var v))
                    {
                        p[canonical] = v; // 正規キーにコピー
                        break;
                    }
                }
            }

            // 2) 必須チェック
            var missing = prof.Required.Where(k => p[k] == null).ToList();

            // 3) OneOf グループ（最低1つ必要）
            var oneOfMissing = new List<string[]>();
            foreach (var group in prof.OneOfGroups)
            {
                if (!group.Any(k => p[k] != null))
                    oneOfMissing.Add(group);
            }

            // 4) 未知キー（許容＋エイリアス＋共通キー 以外）
            var known = new HashSet<string>(prof.Allowed, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in prof.AliasMap) foreach (var a in kv.Value) known.Add(a);
            foreach (var c in CommonOptional) known.Add(c);

            var unknown = p.Properties()
                .Select(pp => pp.Name)
                .Where(n => !known.Contains(n))
                .ToList();

            if (missing.Count > 0 || oneOfMissing.Count > 0 || unknown.Count > 0)
            {
                // サジェスト: 似ているキー名の提案
                var suggestions = new Dictionary<string, string>();
                foreach (var u in unknown)
                {
                    string near = FindNearest(u, known);
                    if (!string.IsNullOrEmpty(near)) suggestions[u] = near;
                }

                var exampleParams = prof.BuildExample(p);
                var exampleRight = new
                {
                    jsonrpc = "2.0",
                    method = prof.Method,
                    @params = exampleParams,
                    id = 1
                };

                // 優しく叱る
                string human = "やさしいお願い：このコマンドのハンドラ仕様をもう一度読み直して、パラメータ名を正しく使ってね。"
                             + "\n次からは下の『正しい例』をコピペして送ってください。";

                return new
                {
                    ok = false,
                    code = "ERR_PARAM_MISMATCH",
                    msg = "Parameter names mismatch or required parameter missing.",
                    humanMessage = human,
                    detail = new
                    {
                        missingRequired = missing.Count == 0 ? null : missing,
                        missingOneOf = oneOfMissing.Count == 0 ? null : oneOfMissing.Select(g => (IEnumerable<string>)g),
                        unknownKeys = unknown.Count == 0 ? null : unknown,
                        aliasAccepted = prof.AliasMap.Count == 0 ? null :
                            prof.AliasMap.ToDictionary(kv => kv.Key, kv => (IEnumerable<string>)kv.Value)
                    },
                    fix = new
                    {
                        exampleRight,
                        suggestions = suggestions.Count == 0 ? null : suggestions
                    }
                };
            }

            // 5) 正常：そのまま続行（p はすでに正規化済み）
            return null;
        }

        // ------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------
        private static string FindNearest(string key, HashSet<string> known)
        {
            string best = null;
            int bestDist = int.MaxValue;
            foreach (var k in known)
            {
                int d = Levenshtein(key, k);
                if (d < bestDist) { bestDist = d; best = k; }
            }
            return bestDist <= 3 ? best : null; // 適当に3以下のみ提案
        }

        // 低コストなレーベンシュタイン距離
        private static int Levenshtein(string a, string b)
        {
            if (a == null) a = "";
            if (b == null) b = "";
            int n = a.Length, m = b.Length;
            var dp = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) dp[i, 0] = i;
            for (int j = 0; j <= m; j++) dp[0, j] = j;
            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost
                    );
                }
            }
            return dp[n, m];
        }

        // ------------------------------------
        // プロファイル宣言用クラス
        // ------------------------------------
        private class ParamProfile
        {
            public string Method { get; }
            public HashSet<string> Allowed { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Required { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<string[]> OneOfGroups { get; } = new List<string[]>();
            public Dictionary<string, string[]> AliasMap { get; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            private Func<JObject, JObject> _exampleFactory = _ => new JObject();

            public ParamProfile(string method) { Method = method; }

            public ParamProfile Allow(params string[] keys) { foreach (var k in keys) Allowed.Add(k); return this; }
            public ParamProfile Require(params string[] keys) { foreach (var k in keys) Required.Add(k); Allowed.UnionWith(keys); return this; }
            public ParamProfile OneOf(params string[] keys) { OneOfGroups.Add(keys); Allowed.UnionWith(keys); return this; }
            public ParamProfile Alias(string canonical, params string[] aliases) { AliasMap[canonical] = aliases ?? new string[0]; return this; }
            public ParamProfile Example(Func<JObject, JObject> factory) { _exampleFactory = factory ?? (_ => new JObject()); return this; }
            public JObject BuildExample(JObject p) => _exampleFactory?.Invoke(p) ?? new JObject();
        }
    }
}
