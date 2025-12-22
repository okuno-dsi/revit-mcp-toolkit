// ================================================================
// File: Commands/Export/ExportDxfCustomLayersCommand.cs
// Target : .NET Framework 4.8 / Revit 2023+ / C# 8
// Purpose: View→DXFを書き出し、そのDXFのレイヤを自由ルールで再割当
// Notes  : RevitのExport Setupに縛られない柔軟レイヤを後段で実現（netDxf使用）
//          - ベースDXFは Revit の Document.Export を利用（忠実な線分化）
//          - その後 DXF 内の Layer を自由に rename / delete / merge
// ================================================================
#nullable enable
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
// NuGet: netDxf
using netDxf;
using netDxf.Entities;
using Newtonsoft.Json.Linq;
using RevitMCPAddin.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RevitMCPAddin.Commands.Export
{
    public class ExportDxfCustomLayersCommand : IRevitCommandHandler
    {
        public string CommandName => "export_dxf_custom_layers";

        public object Execute(UIApplication uiapp, RequestCommand cmd)
        {
            var uidoc = uiapp?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return new { ok = false, msg = "アクティブドキュメントがありません。" };

            var p = (JObject)cmd.Params;

            try
            {
                // ---------- 入力 ----------
                int? viewIdIn = p.Value<int?>("viewId");
                string viewUidIn = p.Value<string>("viewUniqueId");
                string outputPath = p.Value<string>("outputPath");
                if (string.IsNullOrWhiteSpace(outputPath))
                    return new { ok = false, msg = "outputPath は必須です（絶対パスで指定）。" };
                outputPath = EnsureExtension(outputPath, ".dxf"); // 常に .dxf

                string baseSetup = p.Value<string>("baseSetup"); // 任意：DWG/DXF Export Setup 名
                bool merge = p.Value<bool?>("merge") ?? true;
                string defaultLayer = p.Value<string>("defaultLayer") ?? null;

                // maps: [{"from":"A-DOOR*", "to":"MY-DOOR"}, {"from":"ANNO-TEMP","action":"delete"}]
                var maps = p["maps"]?.ToObject<List<LayerMapRule>>() ?? new List<LayerMapRule>();

                // ---------- 対象ビュー ----------
                View baseView = null;
                if (viewIdIn.HasValue && viewIdIn.Value > 0)
                {
                    baseView = doc.GetElement(Autodesk.Revit.DB.ElementIdCompat.From(viewIdIn.Value)) as View;
                    if (baseView == null) return new { ok = false, msg = $"viewId={viewIdIn} のビューが見つかりません。" };
                }
                else if (!string.IsNullOrWhiteSpace(viewUidIn))
                {
                    baseView = doc.GetElement(viewUidIn) as View;
                    if (baseView == null) return new { ok = false, msg = $"viewUniqueId={viewUidIn} のビューが見つかりません。" };
                }
                else
                {
                    baseView = uidoc.ActiveView ?? throw new InvalidOperationException("アクティブビューが特定できません。");
                }

                // ---------- 複製ビュー（安全名） ----------
                View workingView = null;
                ElementId dupId = ElementId.InvalidElementId;
                using (var t = new Transaction(doc, "Duplicate View for DXF Export"))
                {
                    t.Start();
                    dupId = baseView.Duplicate(ViewDuplicateOption.WithDetailing);
                    workingView = (View)doc.GetElement(dupId);
                    workingView.Name = $"{SanitizeElementName(baseView.Name)} DXF {DateTime.Now:HHmmss}";
                    t.Commit();
                }

                // ---------- DXF書き出し（Revit Export） ----------
                var opt = new DWGExportOptions();

                // バージョンは環境に存在する最上位を選ぶ（ACAD2018未定義環境対策）
                opt.FileVersion = ResolveSupportedAcadVersion(new[]
                {
                    "ACAD2021","ACAD2018","ACAD2013","ACAD2010","ACAD2007","ACAD2004","ACAD2000"
                });

                if (!string.IsNullOrWhiteSpace(baseSetup))
                    TryApplyDwgExportSetupByName(doc, baseSetup, opt);

                var folder = Path.GetDirectoryName(outputPath) ?? "";
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var nameNoExt = Path.GetFileNameWithoutExtension(outputPath);
                nameNoExt = SanitizeFileName(nameNoExt);

                bool ok = false;
                Exception exportErr = null;
                try
                {
                    ok = doc.Export(folder, nameNoExt, new List<ElementId> { workingView.Id }, opt);
                }
                catch (Exception ex)
                {
                    exportErr = ex;
                }

                // 複製ビュー後片付け
                CleanupTempView(doc, workingView, keep: false);

                if (!ok)
                {
                    return new { ok = false, msg = "DXF export failed (Revit). " + exportErr?.Message };
                }

                // ---------- DXFレイヤ再割当（netDxf） ----------
                string dxfPath = Path.Combine(folder, nameNoExt + ".dxf");
                if (!File.Exists(dxfPath)) return new { ok = false, msg = $"DXFが見つかりません: {dxfPath}" };

                int beforeLayers, afterLayers, renamedCount, deletedCount;
                try
                {
                    RemapLayers(dxfPath, maps, merge, defaultLayer, out beforeLayers, out afterLayers, out renamedCount, out deletedCount);
                }
                catch (Exception ex)
                {
                    return new { ok = false, msg = "DXF layer remap failed: " + ex.Message };
                }

                return new
                {
                    ok = true,
                    path = dxfPath,
                    beforeLayers,
                    afterLayers,
                    remapped = renamedCount,
                    deleted = deletedCount
                };
            }
            catch (Exception ex)
            {
                return new { ok = false, msg = ex.Message };
            }
        }

        // ----------------- DXF layer remap core -----------------
        private static void RemapLayers(
            string dxfPath,
            List<LayerMapRule> maps,
            bool merge,
            string defaultLayer,
            out int beforeLayers,
            out int afterLayers,
            out int renamedCount,
            out int deletedCount
        )
        {
            var dxf = DxfDocument.Load(dxfPath);
            beforeLayers = dxf.Layers.Count;

            // ワイルドカード→正規表現
            var wc = new List<(Regex re, LayerMapRule rule)>();
            foreach (var m in maps)
            {
                if (!string.IsNullOrWhiteSpace(m.from))
                {
                    var pattern = "^" + Regex.Escape(m.from).Replace("\\*", ".*") + "$";
                    wc.Add((new Regex(pattern, RegexOptions.IgnoreCase), m));
                }
            }

            // 既存レイヤ名一覧
            var layerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ly in dxf.Layers) layerNames.Add(ly.Name);

            // 削除対象レイヤを抽出
            var deleteTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (re, rule) in wc)
            {
                if (rule.IsDelete)
                {
                    foreach (var ln in layerNames)
                        if (re.IsMatch(ln)) deleteTargets.Add(ln);
                }
            }

            // --- Entities をスナップショットで取得（バージョン差対策：反射で配列化） ---
            var entities = GetEntitiesSnapshot(dxf);

            // (1) 削除対象処理：エンティティを defaultLayer へ退避後、レイヤを削除
            deletedCount = 0;
            if (deleteTargets.Count > 0)
            {
                netDxf.Tables.Layer defaultLy = null;
                if (!string.IsNullOrWhiteSpace(defaultLayer))
                    defaultLy = EnsureLayer(dxf, defaultLayer);

                foreach (var ent in entities)
                {
                    var entLayerName = ent.Layer?.Name ?? "0";
                    if (deleteTargets.Contains(entLayerName) && defaultLy != null)
                        ent.Layer = defaultLy;
                }

                foreach (var ln in deleteTargets)
                {
                    var ly = dxf.Layers.FirstOrDefault(x => x.Name.Equals(ln, StringComparison.OrdinalIgnoreCase));
                    if (ly != null)
                    {
                        dxf.Layers.Remove(ly);
                        deletedCount++;
                    }
                }
            }

            // (2) rename / merge
            renamedCount = 0;
            foreach (var ent in entities)
            {
                var cur = ent.Layer?.Name ?? "0";
                string toName = null;

                foreach (var (re, rule) in wc)
                {
                    if (rule.IsDelete) continue;
                    if (re.IsMatch(cur)) { toName = rule.to; break; }
                }

                if (!string.IsNullOrWhiteSpace(toName) && !toName.Equals(cur, StringComparison.OrdinalIgnoreCase))
                {
                    var dest = EnsureLayer(dxf, toName);
                    ent.Layer = dest;
                    renamedCount++;
                }
            }

            afterLayers = dxf.Layers.Count;

            // 保存
            dxf.Save(dxfPath);
        }

        // -------- DrawingEntities を安全に配列化する互換スナップショット ----------
        private static List<netDxf.Entities.EntityObject> GetEntitiesSnapshot(netDxf.DxfDocument dxf)
        {
            var list = new List<netDxf.Entities.EntityObject>();

            // Entities オブジェクト（型は netDxf 内の DrawingEntities など版差あり）
            var entsObj = dxf.Entities;
            var type = entsObj.GetType();

            // 1) GetEnumerator() を反射で呼び出して列挙（IEnumerator 経由）
            var getEnum = type.GetMethod("GetEnumerator", Type.EmptyTypes);
            if (getEnum != null)
            {
                var enumerator = getEnum.Invoke(entsObj, null) as System.Collections.IEnumerator;
                if (enumerator != null)
                {
                    while (enumerator.MoveNext())
                    {
                        if (enumerator.Current is netDxf.Entities.EntityObject eo)
                            list.Add(eo);
                    }
                    if (list.Count > 0) return list;
                }
            }

            // 2) Count / this[int] インデクサで吸い出し（多くの版でこれが使える）
            var countProp = type.GetProperty("Count");
            var indexer = type.GetProperty("Item", new[] { typeof(int) }); // this[int index]
            if (countProp != null && indexer != null)
            {
                var count = (int)countProp.GetValue(entsObj);
                for (int i = 0; i < count; i++)
                {
                    var eo = indexer.GetValue(entsObj, new object[] { i }) as netDxf.Entities.EntityObject;
                    if (eo != null) list.Add(eo);
                }
                if (list.Count > 0) return list;
            }

            // 3) 最後の保険：Entities の公開プロパティに IEnumerable を返す物があれば総なめ
            //    （例：Lines, Arcs などのコレクションを持つ版向け）
            foreach (var pi in type.GetProperties())
            {
                // IEnumerable 派生のみ対象
                if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(pi.PropertyType)) continue;

                var col = pi.GetValue(entsObj) as System.Collections.IEnumerable;
                if (col == null) continue;

                foreach (var obj in col)
                {
                    if (obj is netDxf.Entities.EntityObject eo)
                        list.Add(eo);
                }
            }

            return list;
        }

        private static netDxf.Tables.Layer EnsureLayer(DxfDocument dxf, string name)
        {
            var existing = dxf.Layers.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var layer = new netDxf.Tables.Layer(name);
            dxf.Layers.Add(layer);
            return layer;
        }

        // ----------------- 共通小物 -----------------
        private static string EnsureExtension(string path, string ext)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            var e = Path.GetExtension(path);
            if (string.IsNullOrEmpty(e)) return path + ext;
            if (!e.Equals(ext, StringComparison.OrdinalIgnoreCase))
                return Path.ChangeExtension(path, ext);
            return path;
        }

        private static void CleanupTempView(Document doc, View v, bool keep)
        {
            if (v == null || keep) return;
            using (var t = new Transaction(doc, "Cleanup DXF Temp View"))
            {
                t.Start();
                try { doc.Delete(v.Id); } catch { /* ignore */ }
                t.Commit();
            }
        }

        private static string SanitizeElementName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Temp";
            var forbidden = new HashSet<char>(new[] { '\\', '/', ';', ':', '*', '?', '"', '<', '>', '|', '[', ']', '{', '}', '=' });
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (char.IsControl(ch) || forbidden.Contains(ch)) continue;
                sb.Append(ch);
            }
            var cleaned = sb.ToString().Trim();
            if (cleaned.Length == 0) cleaned = "Temp";
            if (cleaned.Length > 255) cleaned = cleaned.Substring(0, 255);
            return cleaned;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "export";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            var cleaned = sb.ToString().Trim();
            if (cleaned.EndsWith(".")) cleaned = cleaned.TrimEnd('.');
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
                "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
            };
            if (reserved.Contains(cleaned)) cleaned = "_" + cleaned;
            if (string.IsNullOrEmpty(cleaned)) cleaned = "export";
            return cleaned;
        }

        private static void TryApplyDwgExportSetupByName(Document doc, string setupName, DWGExportOptions opt)
        {
            try
            {
                var collector = new FilteredElementCollector(doc).OfClass(typeof(Element));
                var candidates = collector.Where(e =>
                {
                    var cn = e.GetType().Name;
                    return cn.Contains("ExportDWGSettings") || cn.Contains("DWGExportSettings");
                }).ToList();

                foreach (var e in candidates)
                {
                    var nameParam = e.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_NAME)
                                    ?? e.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM)
                                    ?? e.LookupParameter("Name");
                    var n = nameParam?.AsString();
                    if (!string.IsNullOrEmpty(n) && string.Equals(n, setupName, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var mi = typeof(DWGExportOptions).GetMethod("SetExportDWGSettings");
                        if (mi != null) mi.Invoke(opt, new object[] { doc, e.Id });
                        break;
                    }
                }
            }
            catch { /* ignore; default options */ }
        }

        // ACADVersion の存在を確認し、使える最上位を返す
        private static ACADVersion ResolveSupportedAcadVersion(IEnumerable<string> preferredOrder)
        {
            foreach (var name in preferredOrder)
            {
                try
                {
                    if (Enum.IsDefined(typeof(ACADVersion), name))
                        return (ACADVersion)Enum.Parse(typeof(ACADVersion), name, true);
                }
                catch { /* ignore */ }
            }
            var names = Enum.GetNames(typeof(ACADVersion));
            return (ACADVersion)Enum.Parse(typeof(ACADVersion), names.First(), true);
        }

        // ルール定義
        private class LayerMapRule
        {
            public string from { get; set; } = "";   // ワイルドカード * 対応（例："A-DOOR*"})
            public string to { get; set; } = "";     // 置換先
            public string action { get; set; } = ""; // "delete" で削除
            public bool IsDelete => string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase);
        }
    }
}

