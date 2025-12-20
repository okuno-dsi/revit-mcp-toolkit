// ================================================================
// File: Commands/Revision/ExportSnapshotBundleCommand.cs
// Purpose : 複合スナップショット（elements + levels + grids + layers + materials ...）
// Target  : .NET Framework 4.8 / Revit 2023+
// Notes   :
//  - 既存 "export_snapshot" を内部呼び出し
//  - include[] で論理名を指定 → 対応 get_* を実行して results にまとめる
//  - 将来の get_* 追加に備えて、論理名→ハンドラ名のマップを一元管理
//  - 部分失敗は errors[] に集約（stopOnFirstError で厳格化可能）
// ================================================================
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core; // CommandRouter, RequestCommand など

namespace RevitMCPAddin.Commands.Revision
{
    public class ExportSnapshotBundleCommand : IRevitCommandHandler
    {
        public string CommandName => "export_snapshot_bundle";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)(cmd.Params ?? new JObject());
            var include = p["include"] as JArray ?? new JArray("elements", "levels", "grids", "layers", "materials", "categories", "project");
            var elemSnapParams = p["elementSnapshot"] as JObject ?? new JObject();
            var options = p["options"] as JObject ?? new JObject();

            string outPath = options.Value<string>("path");
            bool flatten = options.Value<bool?>("flatten") ?? false;
            bool stopOnFirstError = options.Value<bool?>("stopOnFirstError") ?? false;

            // --- 論理名→内部メソッド名のマップ ---
            // 追加したい項目があればここに 1 行足すだけで収集対象にできる
            var map = new Dictionary<string, Func<List<object>>>()
            {
                // 要素スナップショット（既存 export_snapshot を再利用）
                ["elements"] = () => {
                    var subReq = new RequestCommand
                    {
                        Method = "export_snapshot",
                        Params = elemSnapParams,
                        Id = null // ← サブ呼び出しにID不要なら null で十分
                    };
                    var result = Route(uiapp, subReq);
                    return new List<object> { result };
                },

                // レベル/通り芯
                ["levels"] = () => RunList(uiapp, "get_levels"),
                ["grids"] = () => RunList(uiapp, "get_grids"),

                // レイヤ（壁/床/屋根）
                ["layers"] = () => {
                    var r = new List<object>();
                    r.AddRange(RunList(uiapp, "get_wall_layers"));
                    r.AddRange(RunList(uiapp, "get_floor_layers"));
                    r.AddRange(RunList(uiapp, "get_roof_layers"));
                    return r;
                },

                // マテリアル
                ["materials"] = () => RunList(uiapp, "get_materials"),

                // カテゴリ/プロジェクト
                ["categories"] = () => RunList(uiapp, "get_project_categories"),
                ["project"] = () => RunList(uiapp, "get_project_info"),
            };

            // --- 収集本体 ---
            var results = new JObject();
            var summary = new JObject();
            var errors = new JArray();

            foreach (var token in include)
            {
                string key = token?.ToString()?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(key)) continue;

                if (!map.ContainsKey(key))
                {
                    errors.Add(new JObject { ["target"] = key, ["msg"] = "unsupported include key" });
                    if (stopOnFirstError) return new { ok = false, msg = $"unsupported include: {key}" };
                    continue;
                }

                try
                {
                    var list = map[key]();
                    // 正常/エラーを仕分けて格納
                    if (key == "elements")
                    {
                        // export_snapshot の戻りをそのまま格納
                        var e = (JObject)JToken.FromObject(list.FirstOrDefault() ?? new { ok = false, msg = "no result" });
                        results["elements"] = e;
                        // summary 反映
                        var sum = e["summary"] as JObject;
                        if (sum != null) summary["elements"] = sum.DeepClone();
                    }
                    else if (key == "layers")
                    {
                        var pack = new JObject();
                        // 壁/床/屋根それぞれを探して件数集計
                        MergePack(pack, list, "get_wall_layers", "walls");
                        MergePack(pack, list, "get_floor_layers", "floors");
                        MergePack(pack, list, "get_roof_layers", "roofs");
                        results["layers"] = pack;
                        summary["layers"] = new JObject
                        {
                            ["walls"] = (pack["get_wall_layers"] as JArray)?.Count ?? 0,
                            ["floors"] = (pack["get_floor_layers"] as JArray)?.Count ?? 0,
                            ["roofs"] = (pack["get_roof_layers"] as JArray)?.Count ?? 0
                        };
                    }
                    else
                    {
                        // 通常の get_* は配列にまとめて key 名で格納
                        var arr = new JArray(list.Select(JToken.FromObject));
                        results[key] = arr;
                        // 雑に件数サマリ（必要なら各コマンド仕様に合わせて拡張）
                        summary[key] = new JObject { ["count"] = arr.Count };
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new JObject { ["target"] = key, ["msg"] = ex.Message });
                    if (stopOnFirstError) return new { ok = false, msg = $"{key} failed: {ex.Message}" };
                }
            }

            // --- フラット化（任意） ---
            JToken payload = results;
            if (flatten)
            {
                // elements.summary などは維持しつつ、各キー直下をなるべく一次配列/一次オブジェクトに寄せる簡易版
                payload = Flatten(results);
            }

            // --- ファイル保存（任意） ---
            if (!string.IsNullOrWhiteSpace(outPath))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    var fileObj = new JObject
                    {
                        ["ok"] = true,
                        ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["summary"] = summary,
                        ["results"] = payload,
                        ["errors"] = errors
                    };
                    File.WriteAllText(outPath, JsonConvert.SerializeObject(fileObj, Formatting.Indented), Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    return new { ok = false, msg = $"ファイル保存に失敗しました: {ex.Message}" };
                }
            }

            return new
            {
                ok = true,
                summary,
                results = payload,
                errors,
                path = string.IsNullOrWhiteSpace(outPath) ? null : outPath
            };
        }

        // --- ヘルパ ---
        internal static class RouterHub
        {
            public static CommandRouter? Instance; // App.csの起動時にセット: RouterHub.Instance = new CommandRouter(...);
        }

        private static object Route(UIApplication uiapp, RequestCommand sub)
        {
            if (RouterHub.Instance == null)
                throw new InvalidOperationException("CommandRouter instance is not initialized.");
            return RouterHub.Instance.Route(uiapp, sub);
        }

        private static List<object> RunList(UIApplication uiapp, string method, JObject? @params = null)
        {
            var sub = new RequestCommand { Method = method, Params = @params ?? new JObject(), Id = null };
            var r = Route(uiapp, sub);
            // 返り値が { ok:true, items:[...] } 型のものと配列返しのもの両方に緩く対応
            var jo = r as JObject ?? JObject.FromObject(r ?? new { ok = false, msg = "no result" });
            if (jo["items"] is JArray arr) return arr.Select(x => (object)x).ToList();
            if (jo["rows"] is JArray rows) return rows.Select(x => (object)x).ToList();
            if (jo["data"] is JArray data) return data.Select(x => (object)x).ToList();
            // 単一オブジェクト系は 1 件として扱う
            return new List<object> { jo };
        }

        private static void MergePack(JObject pack, List<object> list, string methodName, string keyAlias)
        {
            // list の中から method 名っぽいキーを持つもの/または items 配列を探して寄せる緩めの実装
            // 実際は get_* の戻り形式に合わせて最適化してください
            var arr = new JArray();
            foreach (var o in list)
            {
                var jo = o as JObject ?? JObject.FromObject(o);
                if (jo["items"] is JArray items) foreach (var it in items) arr.Add(it);
                else arr.Add(jo);
            }
            pack[$"{methodName}"] = arr;
            pack[$"{keyAlias}Count"] = arr.Count;
        }

        private static JToken Flatten(JObject src)
        {
            // 簡易フラットナー：第二階層の items/data/rows を引き上げる
            var dst = new JObject();
            foreach (var kv in src)
            {
                var v = kv.Value;
                if (v is JObject o)
                {
                    if (o["items"] is JArray items) dst[kv.Key] = items;
                    else if (o["data"] is JArray data) dst[kv.Key] = data;
                    else if (o["rows"] is JArray rows) dst[kv.Key] = rows;
                    else dst[kv.Key] = o;
                }
                else
                {
                    dst[kv.Key] = v;
                }
            }
            return dst;
        }
    }
}
