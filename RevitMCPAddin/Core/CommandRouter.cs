// ================================================================
// File: Core/CommandRouter.cs (Safe Router - Full Text)
// Purpose:
//  - params.documentPath が来たら該当ドキュメントをアクティブ化
//  - uniqueId / uniqueIds（および target / targets 内）を elementId / elementIds に自動変換
//  - roomId / areaId / spaceId / gridId / wallId / doorId / windowId / floorId 等の
//    "エイリアスID" を elementId に自動昇格（top, target, targets[] の各コンテナで）
//  - （後方互換）Area/Room/Space系メソッドでは elementId → areaId/roomId/spaceId を自動補完
//  - 想定外や未解決時は { ok:false, errorCode, msg } を返す
//  - NEW-A: ルータ横断のスコープ相互排他（doc/view/elem/agent/global）+ タイムアウト
//  - NEW-B: UI実行の一元化（UiEventPump.InvokeSmart）
//  - NEW-C: JSON-RPC 包装（method/agentId を必ずエコー）※既にJSON-RPC形は素通し
//  - NEW-D: すべての例外を JSON-RPC error で返却（HTTP 200）→ 致命化防止
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;  // NEW
using System.Threading;               // NEW

namespace RevitMCPAddin.Core
{
    public class CommandRouter
    {
        private readonly Dictionary<string, IRevitCommandHandler> _handlers;

        public CommandRouter(IEnumerable<IRevitCommandHandler> handlers)
        {
            var map = new Dictionary<string, IRevitCommandHandler>(StringComparer.OrdinalIgnoreCase);
            var dupList = new List<string>();

            foreach (var h in handlers ?? Enumerable.Empty<IRevitCommandHandler>())
            {
                var name = h?.CommandName ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;

                var methods = name.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(x => x.Trim())
                                  .Where(x => !string.IsNullOrEmpty(x));
                foreach (var m in methods)
                {
                    if (map.ContainsKey(m))
                    {
                        dupList.Add($"{m} -> [{map[m].GetType().Name}] & [{h.GetType().Name}]");
                        // 先勝ち（既存の割当を尊重）
                        continue;
                    }
                    map[m] = h;
                }
            }

            if (dupList.Count > 0)
            {
                var msg = string.Join("\r\n", dupList);
                System.Diagnostics.Debug.WriteLine("[CommandRouter] Duplicate method bindings:\r\n" + msg);
            }

            _handlers = map;
        }

        // ----------------------------------------------------------------
        // ルータ横断の相互排他・包装ヘルパ
        // ----------------------------------------------------------------

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _scopeGates
            = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        private static SemaphoreSlim GetGate(string key)
            => _scopeGates.GetOrAdd(string.IsNullOrWhiteSpace(key) ? "global" : key, _ => new SemaphoreSlim(1, 1));

        // params/meta から最も具体的なスコープ鍵を選ぶ: docGuid → viewId → elementId → agentId → global
        private static string ResolveScopeKey(RequestCommand cmd)
        {
            var p = cmd?.Params as JObject;

            var docGuid = p?.Value<string>("docGuid");
            if (!string.IsNullOrWhiteSpace(docGuid)) return "doc:" + docGuid;

            var viewId = p?.Value<int?>("viewId");
            if (viewId.HasValue && viewId.Value > 0) return "view:" + viewId.Value.ToString();

            var elemId = p?.Value<int?>("elementId");
            if (elemId.HasValue && elemId.Value > 0) return "elem:" + elemId.Value.ToString();

            string? agentId = null;
            try { agentId = p?.Value<string>("agentId"); } catch { /* ignore */ }
            if (agentId.IsNullOrEmpty() && cmd?.MetaRaw is JObject metaJo)
            {
                try { agentId = metaJo.Value<string>("agentId"); } catch { /* ignore */ }
            }
            if (!string.IsNullOrWhiteSpace(agentId)) return "agent:" + agentId;

            return "global";
        }

        // エラー応答用の agentId 抽出
        private static string GetAgentIdEcho(RequestCommand cmd)
        {
            var p = cmd?.Params as JObject;
            var a = p?.Value<string>("agentId")
                 ?? (cmd?.MetaRaw as JObject)?.Value<string>("agentId")
                 ?? "";
            return a ?? "";
        }

        // 既に JSON-RPC 形なら素通し。そうでない場合にだけ method/agentId を付けて包む
        // ledger は optional（McpLedgerEngine が返す）
        private static object WrapJsonRpcIfNeeded(RequestCommand cmd, object raw, object ledger)
        {
            try
            {
                if (raw is JObject jo && (jo["jsonrpc"] != null || jo["result"] != null || jo["error"] != null))
                    return raw; // 既に JSON-RPC 形を返すハンドラはそのまま
            }
            catch { /* best effort */ }

            string method = cmd?.Command ?? "";
            string agentId = GetAgentIdEcho(cmd);

            // ledger が null のときは payload に入れない（後方互換）
            if (ledger == null)
            {
                return new
                {
                    jsonrpc = "2.0",
                    id = cmd?.Id,
                    method = method,   // エコー: 取り違え検知
                    agentId = agentId, // エコー: マルチクライアント混線対策
                    result = raw       // 既存 { ok, ... } 等はそのまま内包（意味は不変）
                };
            }

            return new
            {
                jsonrpc = "2.0",
                id = cmd?.Id,
                method = method,   // エコー: 取り違え検知
                agentId = agentId, // エコー: マルチクライアント混線対策
                result = raw,      // 既存 { ok, ... } 等はそのまま内包（意味は不変）
                ledger = ledger
            };
        }

        // ----------------------------------------------------------------

        public object Route(UIApplication uiapp, RequestCommand cmd)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));

            if (string.IsNullOrWhiteSpace(cmd.Command))
            {
                return new
                {
                    ok = false,
                    errorCode = "ERR_NO_METHOD",
                    msg = "Command(method) 名が空です。JSON-RPC の 'method' で送ってください。",
                    fix = new
                    {
                        exampleRight = new { jsonrpc = "2.0", method = "get_project_info", @params = new { }, id = 1 }
                    }
                };
            }

            // NEW: params 教示ガード（空 {} は許容。存在しない/オブジェクト以外はNG）
            if (cmd.Params == null || cmd.Params.Type != JTokenType.Object)
            {
                var example = new
                {
                    jsonrpc = "2.0",
                    method = cmd.Command ?? "(method)",
                    @params = new { },
                    id = 1
                };
                return new
                {
                    ok = false,
                    errorCode = "ERR_MISSING_PARAMS",
                    msg = "このコマンドは JSON-RPC の params:{.} に引数を包んで送ってください。",
                    error = new
                    {
                        humanMessage = "やさしいお願い：引数はすべて params に入れてね。トップレベル直置きは拾えません。"
                    },
                    fix = new { exampleRight = example }
                };
            }

            // ① documentPath が来ていたらアクティブドキュメント切替
            TryActivateDocument(uiapp, cmd);

            // ② uniqueId / uniqueIds & 各種エイリアスID → elementId / elementIds の前処理（＋後方互換バックフィル）
            var early = PreprocessIdsAndAliases(uiapp, cmd);
            if (early != null) return early;

            // ③ よくあるキーのエイリアス正規化（軽微）＋配列正規化（elementIds/uniqueIds/categoryIds）
            var pObj = cmd.Params as JObject;
            NormalizeCommandSpecificAliases(cmd.Command, pObj);
            // Generic array normalization: tolerate scalar -> array for common keys
            try
            {
                if (pObj != null)
                {
                    NormalizeArray(pObj, "elementIds");
                    NormalizeArray(pObj, "uniqueIds");
                    NormalizeArray(pObj, "categoryIds");
                    // Also normalize nested 'params' when wrapped (e.g., smoke_test)
                    if (pObj["params"] is JObject inner)
                    {
                        NormalizeArray(inner, "elementIds");
                        NormalizeArray(inner, "uniqueIds");
                        NormalizeArray(inner, "categoryIds");
                    }
                }
            }
            catch { /* best-effort; never fail router normalization */ }

            // ④ コマンド別パラメータ検証（必須/OneOf/未知キー/エイリアス）
            var teachErr = CommandParamTeach.Guard(cmd.Command, cmd.Params as JObject);
            if (teachErr != null) return teachErr;

            // ⑤ ディスパッチ（ここだけ“包む”。ハンドラは無改造）
            if (_handlers.TryGetValue(cmd.Command, out var handler))
            {
                var scopeKey = ResolveScopeKey(cmd);
                var gate = GetGate(scopeKey);

                // --- 相互排他 with タイムアウト（デッドロック予防）---
                const int LOCK_TIMEOUT_MS = 8000; // 必要なら設定化
                if (!gate.Wait(millisecondsTimeout: LOCK_TIMEOUT_MS))
                {
                    return new
                    {
                        jsonrpc = "2.0",
                        id = cmd?.Id,
                        method = cmd?.Command ?? "",
                        agentId = GetAgentIdEcho(cmd),
                        error = new
                        {
                            code = -32002,
                            message = $"{cmd?.Command}: concurrency lock timeout on scope '{scopeKey}' (>{LOCK_TIMEOUT_MS}ms)"
                        }
                    };
                }

                try
                {
                    // NOTE:
                    // CommandRouter.Route is invoked from RevitCommandExecutor (ExternalEvent) and therefore already runs
                    // in a valid Revit API context (UI thread). Using UiEventPump.InvokeSmart here can mask real exceptions
                    // and cause long timeouts due to nested ExternalEvent waits. Execute directly and surface errors.
                    var exec = McpLedgerEngine.ExecuteWithLedger(uiapp, cmd, handler);

                    var raw = exec != null ? exec.RawResult : null;
                    var ledger = exec != null ? (object)exec.LedgerInfo : null;

                    // JSON-RPC ラップ（既に JSON-RPC で返すハンドラは素通し）
                    return WrapJsonRpcIfNeeded(cmd, raw, ledger);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    return new
                    {
                        jsonrpc = "2.0",
                        id = cmd?.Id,
                        method = cmd?.Command ?? "",
                        agentId = GetAgentIdEcho(cmd),
                        error = new { code = -32602, message = $"{cmd?.Command}: {ex.Message}" }
                    };
                }
                catch (InvalidOperationException ex)
                {
                    return new
                    {
                        jsonrpc = "2.0",
                        id = cmd?.Id,
                        method = cmd?.Command ?? "",
                        agentId = GetAgentIdEcho(cmd),
                        error = new { code = -32003, message = $"{cmd?.Command}: {ex.Message}" }
                    };
                }
                catch (Exception ex)
                {
                    return new
                    {
                        jsonrpc = "2.0",
                        id = cmd?.Id,
                        method = cmd?.Command ?? "",
                        agentId = GetAgentIdEcho(cmd),
                        error = new { code = -32099, message = $"{cmd?.Command}: {ex.Message}" }
                    };
                }
                finally
                {
                    gate.Release();
                }
            }

            // 既存互換: 未知は例外のまま（必要なら -32601 に置換可）
            throw new InvalidOperationException($"Unknown command: {cmd.Command}");
        }

        // ----------------------------------------------------------------
        // 以降は既存の内部ヘルパー（内容は変更なし）
        // ----------------------------------------------------------------

        private static int ResolveUiTimeoutMs(string? command)
        {
            // 既定は 4s（UiEventPump 既定）だが、以下は重くなり得る
            const int DEFAULT_MS = 4000;
            if (string.IsNullOrWhiteSpace(command)) return DEFAULT_MS;
            var m = command.Trim().ToLowerInvariant();

            // 重い/ブロッキングが起きやすい UI コマンド群
            switch (m)
            {
                // Heaviest read
                case "get_floors":
                    return 240_000; // 240s for large models
                // Heavy reads that can iterate many elements
                case "get_roofs":
                case "delete_view":
                case "open_views":
                case "close_inactive_views":
                case "duplicate_view":
                case "export_dwg":
                case "isolate_by_filter_in_view":
                case "set_visual_override":
                case "clear_visual_override":
                // Parameter updates can trigger regeneration and be slow
                case "update_wall_parameter":
                case "update_wall_type_parameter":
                case "update_curtain_wall_parameter":
                case "update_door_parameter":
                case "update_window_parameter":
                case "set_ceiling_parameter":
                case "set_ceiling_type_parameter":
                case "set_floor_parameter":
                case "set_floor_type_parameter":
                case "set_roof_parameter":
                case "set_roof_type_parameter":
                case "update_material_parameter":
                case "update_text_note_parameter":
                case "update_tag_parameter":
                case "create_text_note":
                    return 240_000; // allow longer time for UI creation
                case "set_text":
                case "move_text_note":
                case "delete_text_note":
                case "create_tag":
                case "move_tag":
                case "delete_tag":
                case "set_railing_parameter":
                case "set_railing_type_parameter":
                case "update_parameters_batch":
                case "set_stair_parameter":
                case "set_stair_type_parameter":
                case "set_stair_flight_parameters":
                case "set_family_type_parameter":
                case "update_family_instance_parameter":
                case "update_level_parameter":
                case "set_revision_cloud_parameter":
                case "set_revision_cloud_type_parameter":
                case "set_view_parameter":
                    return 120_000; // 120s まで待つ
                default:
                    return DEFAULT_MS;
            }
        }

        private static void TryActivateDocument(UIApplication uiapp, RequestCommand cmd)
        {
            if (uiapp?.Application == null) return;
            var p = cmd?.Params as JObject;
            if (p == null) return;

            if (p.TryGetValue("documentPath", out JToken token) && token.Type == JTokenType.String)
            {
                var path = (string)token;
                if (string.IsNullOrWhiteSpace(path)) return;

                var active = uiapp.ActiveUIDocument?.Document;
                var target = uiapp.Application.Documents
                    .Cast<Document>()
                    .FirstOrDefault(d => string.Equals(d.PathName, path, StringComparison.OrdinalIgnoreCase));

                if (active != null && target != null && active.PathName == target.PathName)
                    return; // 既にアクティブ

                uiapp.OpenAndActivateDocument(path);
            }
        }

        private static object PreprocessIdsAndAliases(UIApplication uiapp, RequestCommand cmd)
        {
            var doc = uiapp?.ActiveUIDocument?.Document;
            var p = cmd?.Params as JObject;
            if (doc == null || p == null) return null;

            // 上位直下の補完
            var err = MapContainer(doc, p);
            if (err != null) return err;
            BackfillDomainSpecificIds(cmd.Command, p);

            // target: { . }
            if (p.TryGetValue("target", out var tgtTok) && tgtTok is JObject tgtObj)
            {
                err = MapContainer(doc, tgtObj);
                if (err != null) return err;
                BackfillDomainSpecificIds(cmd.Command, tgtObj);
            }

            // targets: [ { . }, . ]
            if (p.TryGetValue("targets", out var tgtsTok) && tgtsTok is JArray tgtsArr)
            {
                foreach (var item in tgtsArr.OfType<JObject>())
                {
                    err = MapContainer(doc, item);
                    if (err != null) return err;
                    BackfillDomainSpecificIds(cmd.Command, item);
                }
            }

            return null;
        }

        private static object MapContainer(Document doc, JObject container)
        {
            // 1) uniqueId -> elementId
            if (container.TryGetValue("uniqueId", out var uidTok)
                && uidTok.Type == JTokenType.String
                && !container.ContainsKey("elementId"))
            {
                var uid = uidTok.Value<string>();
                var e = string.IsNullOrWhiteSpace(uid) ? null : doc.GetElement(uid);
                if (e == null)
                {
                    return new
                    {
                        ok = false,
                        errorCode = "NOT_FOUND",
                        msg = $"Element not found for uniqueId: {uid}"
                    };
                }
                container["elementId"] = e.Id.IntegerValue;
            }

            // 2) uniqueIds -> elementIds
            if (container.TryGetValue("uniqueIds", out var uidsTok)
                && uidsTok is JArray uidsArr
                && !container.ContainsKey("elementIds"))
            {
                var outIds = new JArray();
                foreach (var s in uidsArr.Values<string>())
                {
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    var e = doc.GetElement(s);
                    if (e != null) outIds.Add(e.Id.IntegerValue);
                }

                if (outIds.Count == 0)
                {
                    return new
                    {
                        ok = false,
                        errorCode = "NOT_FOUND",
                        msg = "No elements found for given uniqueIds."
                    };
                }

                container["elementIds"] = outIds;
            }

            // 3) 各種 "XXId" エイリアスを elementId に昇格（elementId未指定時のみ）
            PromoteAliasIdToElementId(container, "roomId");
            PromoteAliasIdToElementId(container, "areaId");
            PromoteAliasIdToElementId(container, "spaceId");
            PromoteAliasIdToElementId(container, "gridId");
            PromoteAliasIdToElementId(container, "wallId");
            PromoteAliasIdToElementId(container, "doorId");
            PromoteAliasIdToElementId(container, "windowId");
            PromoteAliasIdToElementId(container, "floorId");
            // Site系は elementId を直接必要としないため昇格しない

            // 注意: viewId はビューIDとして利用。elementId へは昇格しない
            return null;
        }

        private static void PromoteAliasIdToElementId(JObject container, string elemKey)
        {
            if (!container.ContainsKey("elementId") &&
                container.TryGetValue(elemKey, out var tok) &&
                (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float))
            {
                container["elementId"] = tok.Value<int>();
            }
        }

        private static void BackfillDomainSpecificIds(string method, JObject container)
        {
            if (container == null || string.IsNullOrEmpty(method)) return;

            var elem = container["elementId"];
            if (elem == null) return;

            var m = method.ToLowerInvariant();

            // Area 系
            if (m.StartsWith("get_area") || m.StartsWith("update_area") ||
                m.StartsWith("move_area") || m.StartsWith("delete_area") ||
                m.Contains("_area"))
            {
                if (container["areaId"] == null) container["areaId"] = elem;
            }

            // Room 系
            if (m.StartsWith("get_room") || m.StartsWith("set_room") ||
                m.StartsWith("delete_room") || m.Contains("_room"))
            {
                if (container["roomId"] == null) container["roomId"] = elem;
            }

            // Space 系
            if (m.StartsWith("get_space") || m.StartsWith("update_space") ||
                m.StartsWith("move_space") || m.StartsWith("delete_space") ||
                m.Contains("_space"))
            {
                if (container["spaceId"] == null) container["spaceId"] = elem;
            }
        }

        // 追加の軽微エイリアス正規化
        private static void NormalizeCommandSpecificAliases(string method, JObject p)
        {
            if (p == null || string.IsNullOrEmpty(method)) return;
            var m = method.Trim().ToLowerInvariant();

            // Generic: if no location object is provided, but top-level x/y/z exist,
            // synthesize location = { x, y, z } for backward compatibility.
            try
            {
                var hasLocationObj = p["location"] is JObject;
                var hasX = p["x"] != null && (p["x"].Type == JTokenType.Integer || p["x"].Type == JTokenType.Float);
                var hasY = p["y"] != null && (p["y"].Type == JTokenType.Integer || p["y"].Type == JTokenType.Float);
                var hasZ = p["z"] != null && (p["z"].Type == JTokenType.Integer || p["z"].Type == JTokenType.Float);
                if (!hasLocationObj && hasX && hasY && hasZ)
                {
                    p["location"] = new JObject
                    {
                        ["x"] = p["x"],
                        ["y"] = p["y"],
                        ["z"] = p["z"]
                    };
                }
            }
            catch { /* best effort fallback; never fail normalization */ }

            // rename_wall_type: newTypeName / name / renameTo → newName
            if (m == "rename_wall_type")
            {
                if (p["newName"] == null)
                {
                    if (p["newTypeName"] != null) p["newName"] = p["newTypeName"];
                    else if (p["name"] != null) p["newName"] = p["name"];
                    else if (p["renameTo"] != null) p["newName"] = p["renameTo"];
                }
            }

            // create_text_note: pointMm/posMm → positionMm、textNoteTypeName → typeName
            if (m == "create_text_note")
            {
                if (p["positionMm"] == null && p["pointMm"] is JObject pt) p["positionMm"] = pt;
                if (p["positionMm"] == null && p["posMm"] is JObject pt2) p["positionMm"] = pt2;
                if (p["typeName"] == null && p["textNoteTypeName"] != null) p["typeName"] = p["textNoteTypeName"];
            }

            // get_wall_type_parameters: typeId -> typeIds[], elementId -> elementIds[]
            if (m == "get_wall_type_parameters")
            {
                if (p["typeIds"] == null && (p["typeId"]?.Type == JTokenType.Integer || p["typeId"]?.Type == JTokenType.Float))
                    p["typeIds"] = new JArray(p.Value<int>("typeId"));
                if (p["elementIds"] == null && (p["elementId"]?.Type == JTokenType.Integer || p["elementId"]?.Type == JTokenType.Float))
                    p["elementIds"] = new JArray(p.Value<int>("elementId"));
            }

            // Site 系軽微エイリアス
            if (m == "create_toposurface_from_points" || m == "append_toposurface_points" || m == "replace_toposurface_points")
            {
                if (p["pointsMm"] == null && p["points"] is JArray arr) p["pointsMm"] = arr;
            }
            if (m == "create_site_subregion_from_boundary")
            {
                if (p["boundaryMm"] == null && p["boundary"] is JArray arr2) p["boundaryMm"] = arr2;
            }
            if (m == "place_site_component" || m == "place_parking_spot")
            {
                if (p["angleDeg"] == null && (p["angleDegrees"]?.Type == JTokenType.Integer || p["angleDegrees"]?.Type == JTokenType.Float))
                    p["angleDeg"] = p["angleDegrees"];
                if (p["locationMm"] == null && p["positionMm"] is JObject pp) p["locationMm"] = pp;
            }
        }

        private static void NormalizeArray(JObject obj, string key)
        {
            if (obj[key] == null) return;
            var t = obj[key];
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float || t.Type == JTokenType.String)
            {
                // Wrap scalar into array
                obj[key] = new JArray(t);
            }
            // leave if already array/object/null
        }

        // ============================================================
        // コマンド別の“優しく叱る”検証器（このファイル内完結版）
        // （Profiles 内容は簡略ダミー。あなたの既存定義がある場合は置換不要）
        // ============================================================
        private static class CommandParamTeach
        {
            private static readonly HashSet<string> CommonOptional = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "documentPath", "debug", "dryRun", "scope", "where", "target", "targets"
            };

            private static readonly Dictionary<string, ParamProfile> Profiles =
                new Dictionary<string, ParamProfile>(StringComparer.OrdinalIgnoreCase)
                {
                    { "get_open_documents", new ParamProfile("get_open_documents").Allow().Example(_ => new JObject()) },
                    { "get_project_info",   new ParamProfile("get_project_info").Allow().Example(_ => new JObject()) },
                    { "ping_server",        new ParamProfile("ping_server").Allow().Example(_ => new JObject()) },
                    // TODO: ここに既存の詳細プロファイルを移植（必要に応じて）
                };

            public static object? Guard(string method, JObject? p)
            {
                if (string.IsNullOrWhiteSpace(method)) return null;
                p ??= new JObject();

                if (!Profiles.TryGetValue(method, out var prof))
                {
                    // 未登録メソッドは寛容に素通し（互換優先）
                    return null;
                }

                return prof.Validate(p, CommonOptional);
            }

            // ---- ParamProfile ----
            private sealed class ParamProfile
            {
                private readonly string _name;
                private readonly HashSet<string> _require = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                private readonly List<HashSet<string>> _oneOf = new List<HashSet<string>>();
                private readonly Dictionary<string, HashSet<string>> _alias = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                private readonly HashSet<string> _allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                private Func<JObject?, JObject>? _example;

                public ParamProfile(string name) { _name = name; }

                public ParamProfile Require(params string[] keys) { foreach (var k in keys) _require.Add(k); return this; }
                public ParamProfile OneOf(params string[] keys) { _oneOf.Add(new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase)); return this; }
                public ParamProfile Alias(string canonical, params string[] alts)
                {
                    if (!_alias.TryGetValue(canonical, out var set))
                        _alias[canonical] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var a in alts) set.Add(a);
                    return this;
                }
                public ParamProfile Allow(params string[] keys) { foreach (var k in keys) _allow.Add(k); return this; }
                public ParamProfile Example(Func<JObject?, JObject> maker) { _example = maker; return this; }

                public object? Validate(JObject p, HashSet<string> commonOptional)
                {
                    // 1) エイリアス正規化
                    foreach (var kv in _alias)
                    {
                        var canon = kv.Key;
                        if (p[canon] != null) continue;
                        foreach (var alt in kv.Value)
                        {
                            if (p[alt] != null) { p[canon] = p[alt]; break; }
                        }
                    }

                    // 2) 必須
                    foreach (var req in _require)
                    {
                        if (p[req] == null)
                        {
                            return new
                            {
                                ok = false,
                                errorCode = "ERR_PARAM_REQUIRED",
                                msg = $"Parameter '{req}' is required for '{_name}'.",
                                exampleRight = _example?.Invoke(p) ?? new JObject()
                            };
                        }
                    }

                    // 3) OneOf
                    foreach (var group in _oneOf)
                    {
                        if (!group.Any(k => p[k] != null))
                        {
                            return new
                            {
                                ok = false,
                                errorCode = "ERR_PARAM_ONEOF",
                                msg = $"One of [{string.Join(", ", group)}] is required for '{_name}'.",
                                exampleRight = _example?.Invoke(p) ?? new JObject()
                            };
                        }
                    }

                    // 4) 未知キー（共通黙認 + Allow 以外は黙認：互換優先）
                    var allowed = new HashSet<string>(_allow, StringComparer.OrdinalIgnoreCase);
                    foreach (var c in commonOptional) allowed.Add(c);
                    foreach (var k in p.Properties().Select(x => x.Name))
                    {
                        if (!allowed.Contains(k))
                        {
                            // 厳格に拒否せず黙認（ヒント返却に切替も可）
                        }
                    }

                    return null;
                }
            }
        }
    }
}

// 文字列拡張（.NET 4.8 互換向け）
internal static class _StrEx
{
    public static bool IsNullOrEmpty(this string? s) => string.IsNullOrEmpty(s);
}
