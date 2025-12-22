// ================================================================
// File: Commands/LinkOps/LinkCommands.cs
// Revit 2023 / .NET Framework 4.8 / C# 8
// 概要: Revit リンク操作コマンド一式（1ファイル集約版 / UnitHelper対応）
//  - list_links（★ transform に加えて transformMm を併記）
//  - reload_link / unload_link / reload_link_from
//  - bind_link（API未提供を明示）/ detach_link（開く時のみ可を明示）
// 依存: Autodesk.Revit.DB, Autodesk.Revit.UI, Newtonsoft.Json.Linq,
//       RevitMCPAddin.Core (IRevitCommandHandler, RequestCommand, ResultUtil, UnitHelper)
// 方針: Revit バージョン差は反射で可能な限りフォロー。数値座標は mm も返却。
// ================================================================
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;

namespace RevitMCPAddin.Commands.LinkOps
{
    // ------------------------------------------------------------
    // 内部ヘルパ: RevitLinkType のバージョン差を吸収
    // ------------------------------------------------------------
    internal static class RevitLinkTypeExtensions
    {
        /// <summary>IsLoaded を安全に取得（メソッドが無い/例外時は false を返す）</summary>
        public static bool TryGetIsLoaded(RevitLinkType type, out bool isLoaded)
        {
            isLoaded = false;
            if (type == null) return false;
            try
            {
                // 署名候補: bool IsLoaded() / property ではない想定
                var mi = typeof(RevitLinkType).GetMethod("IsLoaded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (mi != null)
                {
                    var r = mi.Invoke(type, null);
                    if (r is bool b) { isLoaded = b; return true; }
                }
            }
            catch { /* ignore */ }
            return false;
        }

        /// <summary>Reload() があれば呼ぶ。無ければ現在パスで LoadFrom する。</summary>
        public static void ReloadOrLoadFromCurrentPath(Document doc, RevitLinkType type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            // Try Reload()
            var methodReload = typeof(RevitLinkType).GetMethod("Reload", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (methodReload != null)
            {
                methodReload.Invoke(type, null);
                return;
            }

            // Fallback → 現在の ExternalFileReference 経由で LoadFrom
            var ext = type.GetExternalFileReference();
            if (ext == null) throw new InvalidOperationException("ExternalFileReference not found.");
            var mp = ext.GetAbsolutePath();
            if (mp == null) throw new InvalidOperationException("ModelPath is null.");
            type.LoadFrom(mp, new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
        }

        /// <summary>Unload() を反射で呼ぶ（未提供なら例外）</summary>
        public static void UnloadOrThrow(RevitLinkType type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var mi = typeof(RevitLinkType).GetMethod("Unload", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (mi == null) throw new InvalidOperationException("RevitLinkType.Unload method not available.");
            mi.Invoke(type, null);
        }
    }

    // ------------------------------------------------------------
    // 共通: typeId / name で RevitLinkType を解決
    // ------------------------------------------------------------
    internal static class LinkResolve
    {
        public static RevitLinkType ResolveRevitLinkType(Document doc, JObject p, out string why)
        {
            why = null;
            if (doc == null) { why = "No active document."; return null; }
            if (p == null) { why = "params is null."; return null; }

            if (p.TryGetValue("typeId", out var tIdTok) && tIdTok.Type == JTokenType.Integer)
            {
                var id = Autodesk.Revit.DB.ElementIdCompat.From(tIdTok.Value<int>());
                var t = doc.GetElement(id) as RevitLinkType;
                if (t != null) return t;
                why = $"RevitLinkType not found: {id.IntValue()}";
                return null;
            }

            if (p.TryGetValue("name", out var nameTok))
            {
                var name = (nameTok?.ToString() ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    var t = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType))
                        .Cast<RevitLinkType>()
                        .FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (t != null) return t;
                }
                why = $"RevitLinkType not found by name: {name}";
                return null;
            }

            why = "Specify typeId or name.";
            return null;
        }

        public static WorksetConfiguration BuildWorksetConfig(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);
            mode = mode.Trim().ToLowerInvariant();
            if (mode == "visible" || mode == "last" || mode == "lastviewed") return new WorksetConfiguration(WorksetConfigurationOption.OpenLastViewed);
            if (mode == "none" || mode == "closeall" || mode == "closed") return new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);
            return new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);
        }
    }

    // ============================================================
    // 1) list_links  — ★UnitHelper対応（transformMm 追加）
    // ============================================================
    public class ListLinksCommand : IRevitCommandHandler
    {
        public string CommandName => "list_links";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null)
                return ResultUtil.Err("No active document.");

            try
            {
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkType))
                    .Cast<RevitLinkType>()
                    .ToList();

                var insts = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();

                var mapTypeToInsts = insts
                    .GroupBy(i => i.GetTypeId())
                    .ToDictionary(g => g.Key, g => g.ToList());

                var arr = new JArray();

                foreach (var t in types)
                {
                    string path = null;
                    try
                    {
                        var ext = t.GetExternalFileReference();
                        if (ext != null)
                        {
                            var mp = ext.GetAbsolutePath();
                            if (mp != null)
                                path = ModelPathUtils.ConvertModelPathToUserVisiblePath(mp);
                        }
                    }
                    catch { /* BIM360等で取れない場合あり */ }

                    var status = t.GetLinkedFileStatus(); // enum
                    bool isLoaded;
                    if (!RevitLinkTypeExtensions.TryGetIsLoaded(t, out isLoaded))
                    {
                        // フォールバック（"Loaded" を含むかで判定）
                        isLoaded = status.ToString().IndexOf("Loaded", StringComparison.OrdinalIgnoreCase) >= 0;
                    }

                    var instList = new JArray();
                    if (mapTypeToInsts.TryGetValue(t.Id, out var related))
                    {
                        foreach (var ins in related)
                        {
                            var tr = ins.GetTransform();

                            // 従来（内部 ft）を維持
                            var trRaw = new JObject
                            {
                                ["origin"] = ToJ(tr.Origin),
                                ["basisX"] = ToJ(tr.BasisX),
                                ["basisY"] = ToJ(tr.BasisY),
                                ["basisZ"] = ToJ(tr.BasisZ),
                            };

                            // ★追加：mm 換算（origin のみ単位依存 / basis は無次元）
                            var orgMm = UnitHelper.XyzToMm(tr.Origin);
                            var trMm = new JObject
                            {
                                ["origin"] = new JObject
                                {
                                    ["x"] = Math.Round(orgMm.x, 3),
                                    ["y"] = Math.Round(orgMm.y, 3),
                                    ["z"] = Math.Round(orgMm.z, 3),
                                },
                                ["basisX"] = ToJ(tr.BasisX), // 単位なし
                                ["basisY"] = ToJ(tr.BasisY),
                                ["basisZ"] = ToJ(tr.BasisZ),
                            };

                            instList.Add(new JObject
                            {
                                ["instanceId"] = ins.Id.IntValue(),
                                ["name"] = ins.Name,
                                ["transform"] = trRaw,   // 旧互換（ft）
                                ["transformMm"] = trMm   // 新推奨（mm）
                            });
                        }
                    }

                    arr.Add(new JObject
                    {
                        ["typeId"] = t.Id.IntValue(),
                        ["name"] = t.Name,
                        ["isLoaded"] = isLoaded,
                        ["status"] = status.ToString(),
                        ["statusCode"] = (int)status,
                        ["path"] = path ?? string.Empty,
                        ["instances"] = instList,
                        ["units"] = new JObject
                        {
                            ["internal"] = new JObject { ["length"] = "ft" },
                            ["recommendedOutput"] = new JObject { ["length"] = "mm" }
                        }
                    });
                }

                return ResultUtil.Ok(new JObject { ["links"] = arr });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err("list_links failed: " + ex.Message);
            }
        }

        private static JObject ToJ(XYZ p) => new JObject { ["x"] = p.X, ["y"] = p.Y, ["z"] = p.Z };
    }

    // ============================================================
    // 2) reload_link  — パスは変更せず再読込（バージョン差は反射でフォロー）
    // ============================================================
    public class ReloadLinkCommand : IRevitCommandHandler
    {
        public string CommandName => "reload_link";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");
            var p = (JObject)(cmd.Params ?? new JObject());

            var type = LinkResolve.ResolveRevitLinkType(doc, p, out var why);
            if (type == null) return ResultUtil.Err(why);

            try
            {
                using (var tx = new Transaction(doc, "[MCP] Reload Link"))
                {
                    tx.Start();
                    RevitLinkTypeExtensions.ReloadOrLoadFromCurrentPath(doc, type);
                    tx.Commit();
                }
                return ResultUtil.Ok(new JObject
                {
                    ["typeId"] = type.Id.IntValue(),
                    ["name"] = type.Name,
                    ["message"] = "Reloaded."
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err("reload_link failed: " + ex.Message);
            }
        }
    }

    // ============================================================
    // 3) unload_link  — 反射で Unload() があれば実行
    // ============================================================
    public class UnloadLinkCommand : IRevitCommandHandler
    {
        public string CommandName => "unload_link";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");
            var p = (JObject)(cmd.Params ?? new JObject());

            var type = LinkResolve.ResolveRevitLinkType(doc, p, out var why);
            if (type == null) return ResultUtil.Err(why);

            try
            {
                using (var tx = new Transaction(doc, "[MCP] Unload Link"))
                {
                    tx.Start();
                    RevitLinkTypeExtensions.UnloadOrThrow(type);
                    tx.Commit();
                }
                return ResultUtil.Ok(new JObject
                {
                    ["typeId"] = type.Id.IntValue(),
                    ["name"] = type.Name,
                    ["message"] = "Unloaded."
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err("unload_link failed: " + ex.Message);
            }
        }
    }

    // ============================================================
    // 4) reload_link_from  — 参照先パスを再指定してロード
    //     params: { typeId|name, path, worksetMode?: "all"|"visible"|"none" }
    // ============================================================
    public class ReloadLinkFromCommand : IRevitCommandHandler
    {
        public string CommandName => "reload_link_from";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null) return ResultUtil.Err("No active document.");
            var p = (JObject)(cmd.Params ?? new JObject());

            var type = LinkResolve.ResolveRevitLinkType(doc, p, out var why);
            if (type == null) return ResultUtil.Err(why);

            if (!p.TryGetValue("path", out var pathTok))
                return ResultUtil.Err("Missing 'path'.");

            var userPath = pathTok.ToString();
            ModelPath mp;
            try
            {
                mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(userPath);
            }
            catch (Exception ex)
            {
                return ResultUtil.Err("Invalid path: " + ex.Message);
            }

            var wsMode = p.Value<string>("worksetMode") ?? "all";
            var config = LinkResolve.BuildWorksetConfig(wsMode);

            try
            {
                using (var tx = new Transaction(doc, "[MCP] Reload Link From"))
                {
                    tx.Start();
                    type.LoadFrom(mp, config);
                    tx.Commit();
                }
                return ResultUtil.Ok(new JObject
                {
                    ["typeId"] = type.Id.IntValue(),
                    ["name"] = type.Name,
                    ["newPath"] = userPath,
                    ["message"] = "Reloaded from specified path."
                });
            }
            catch (Exception ex)
            {
                return ResultUtil.Err("reload_link_from failed: " + ex.Message);
            }
        }
    }

    // ============================================================
    // 5) bind_link  — API未提供を明示
    // ============================================================
    public class BindLinkCommand : IRevitCommandHandler
    {
        public string CommandName => "bind_link";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            return ResultUtil.Err("Bind Link is not exposed in the Revit API. Use alternatives such as copy/monitor or manual bind via UI.");
        }
    }

    // ============================================================
    // 6) detach_link  — 既に開いているリンクの detach は不可を明示
    // ============================================================
    public class DetachLinkCommand : IRevitCommandHandler
    {
        public string CommandName => "detach_link";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            return ResultUtil.Err("Detaching a linked model is not supported on an already-open link via API. DetachFromCentral is only available when opening a model.");
        }
    }
}


