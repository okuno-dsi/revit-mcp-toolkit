// ============================================================================
// File: MergeDwgsGuiCommand.cs  (GUI専用: acmgd + acdbmgd)
// Purpose: GUI上で複数DWGを統合し、ファイル名をレイヤ名に付加して取り込む
// Target : .NET 8, AutoCAD GUI
// Notes  : accoremgd/Core は一切参照しない
//          DuplicateRecordCloning は Replace を使用
//          ValidateSymbolName は例外で判定（戻り値は void）
//          新旧 DoMergePublic の両シグネチャを提供
// ============================================================================

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application; // ← GUI Application (acmgd)
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;   // ← GUI Document (acmgd)
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace MergeDwgsPlugin
{
    public static class MergeDwgsGuiCommand
    {
        // ------------------------------
        // Public entry (new signature)
        // ------------------------------
        public static bool DoMergePublic(JsonObject p, Action<string>? progress, out string? message)
        {
            message = null;

            // Params
            string seed = p?["seed"]?.GetValue<string>() ?? string.Empty;
            string output = p?["output"]?.GetValue<string>() ?? string.Empty;

            var inputsArr = p?["inputs"] as JsonArray ?? new JsonArray();
            var inputPaths = inputsArr
                .Select(n => n?["path"]?.GetValue<string>() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            // rename.{format, include[], exclude[]}
            string fmt = "{old}_{stem}";
            var rename = p?["rename"] as JsonObject;
            if (rename != null)
                fmt = rename?["format"]?.GetValue<string>() ?? fmt;

            var includes = GetStringList(rename, "include", defaultIfEmptyStar: true);
            var excludes = new HashSet<string>(
                GetStringList(rename, "exclude", defaultIfEmptyStar: false)
                    .DefaultIfEmpty().Where(s => !string.IsNullOrWhiteSpace(s)),
                StringComparer.OrdinalIgnoreCase);

            // 常時除外
            excludes.Add("0");
            excludes.Add("DEFPOINTS");

            try
            {
                progress?.Invoke("Preparing target drawing...");
                using var targetDocScope = EnsureTargetDrawing(seed, progress, out AcDoc targetDoc);
                var ed = targetDoc.Editor;
                var tdb = targetDoc.Database;

                // Merge each input
                int count = 0;
                foreach (var inPath in inputPaths)
                {
                    count++;
                    progress?.Invoke($"[{count}/{inputPaths.Length}] Reading: {inPath}");
                    if (!File.Exists(inPath))
                        throw new FileNotFoundException("Input DWG not found.", inPath);

                    var stem = Path.GetFileNameWithoutExtension(inPath);

                    using (var srcDb = new Database(false, true))
                    {
                        srcDb.ReadDwgFile(inPath, FileShare.Read, true, "");
                        srcDb.CloseInput(true);

                        // ソースDB側でレイヤ改名 → クローン時には目的のレイヤ名で入ってくる
                        PreRenameLayersInSource(srcDb, fmt, stem, includes, excludes, progress);

                        // モデル空間全体をターゲットへクローン
                        CloneModelSpaceToTarget(srcDb, tdb, progress);
                    }
                }

                progress?.Invoke("Purging unused items...");
                PurgeUnused(tdb);

                // Save
                if (!string.IsNullOrWhiteSpace(output))
                {
                    var outDir = Path.GetDirectoryName(output);
                    if (!string.IsNullOrWhiteSpace(outDir) && !Directory.Exists(outDir))
                        Directory.CreateDirectory(outDir);

                    progress?.Invoke($"Saving to: {output}");
                    tdb.SaveAs(output, DwgVersion.Current);
                }

                progress?.Invoke("Done.");
                return true;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        // ------------------------------
        // Public entry (legacy signature)
        // ------------------------------
        public static bool DoMergePublic(
            object rawParams,
            string[] inputs,
            string seed,
            string output,
            IEnumerable<string> include,
            HashSet<string> exclude)
        {
            // 旧引数 → 新Json へ詰め替え
            var p = new JsonObject
            {
                ["seed"] = seed ?? string.Empty,
                ["output"] = output ?? string.Empty,
                ["inputs"] = new JsonArray()
            };

            var incArr = new JsonArray();
            foreach (var s in include ?? Array.Empty<string>()) incArr.Add(s);
            var excArr = new JsonArray();
            foreach (var s in (exclude ?? new HashSet<string>())) excArr.Add(s);

            p["rename"] = new JsonObject
            {
                ["format"] = "{old}_{stem}",
                ["include"] = incArr,
                ["exclude"] = excArr
            };

            var inputsJa = (JsonArray)p["inputs"]!;
            foreach (var s in inputs ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(s))
                    inputsJa.Add(new JsonObject { ["path"] = s });
            }

            return DoMergePublic(p, progress: null, out _);
        }

        // =====================================================================
        // Internals
        // =====================================================================

        /// <summary>ターゲット図面を用意（seed があれば開く、無ければ現用/新規）。</summary>
        private static TargetDocScope EnsureTargetDrawing(string seed, Action<string>? progress, out AcDoc targetDoc)
        {
            var dm = AcAp.DocumentManager;

            if (!string.IsNullOrWhiteSpace(seed) && File.Exists(seed))
            {
                progress?.Invoke($"Opening seed drawing: {seed}");

                // 既に開いていればそれを使う
                var opened = dm.Cast<AcDoc>().FirstOrDefault(d =>
                    string.Equals(d.Name, seed, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.Database.Filename, seed, StringComparison.OrdinalIgnoreCase));

                if (opened is null)
                {
                    // 新規に開く
                    var doc = dm.Open(seed, forReadOnly: false);
                    dm.MdiActiveDocument = doc;
                    targetDoc = doc;
                    return new TargetDocScope(docActivated: true);
                }
                else
                {
                    dm.MdiActiveDocument = opened;
                    targetDoc = opened;
                    return new TargetDocScope(docActivated: false);
                }
            }
            else
            {
                // 既存MDIがあればそれを使い、無ければ新規テンプレート
                var cur = dm.MdiActiveDocument;
                if (cur is null)
                {
                    progress?.Invoke("Creating a new drawing...");
                    var doc = dm.Add(""); // 既定テンプレート
                    dm.MdiActiveDocument = doc;
                    targetDoc = doc;
                    return new TargetDocScope(docActivated: true);
                }
                targetDoc = cur;
                return new TargetDocScope(docActivated: false);
            }
        }

        private readonly struct TargetDocScope : IDisposable
        {
            private readonly bool _docActivated;
            public TargetDocScope(bool docActivated) => _docActivated = docActivated;
            public void Dispose()
            {
                // 必要なら元のMDIに戻す等の後片付けをここに
            }
        }

        /// <summary>
        /// ソースDB内のレイヤ名を {old}/{stem} でリネームしてからクローン。
        /// </summary>
        private static void PreRenameLayersInSource(
            Database srcDb,
            string format,
            string stem,
            IReadOnlyList<string> includes,
            HashSet<string> excludes,
            Action<string>? progress)
        {
            using var tr = srcDb.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(srcDb.LayerTableId, OpenMode.ForRead);

            // ワイルドカード → Regex
            var incMatchers = includes?.Select(WildcardToRegex).ToArray() ?? Array.Empty<Regex>();
            var excMatchers = excludes?.Select(WildcardToRegex).ToArray() ?? Array.Empty<Regex>();

            foreach (ObjectId lid in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                var oldName = ltr.Name;

                // 除外
                if (string.Equals(oldName, "0", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(oldName, "DEFPOINTS", StringComparison.OrdinalIgnoreCase)) continue;
                if (MatchesAny(oldName, excMatchers)) continue;

                // include チェック（["*"] なら常時true）
                if (incMatchers.Length > 0 && !MatchesAny(oldName, incMatchers))
                    continue;

                var newName = format
                    .Replace("{old}", oldName, StringComparison.Ordinal)
                    .Replace("{stem}", stem, StringComparison.Ordinal);

                if (string.Equals(newName, oldName, StringComparison.Ordinal))
                    continue; // 変更なし

                // 記号修正＆検証（ValidateSymbolName は void：例外で判定）
                var repaired = SymbolUtilityServices.RepairSymbolName(newName, false);
                try
                {
                    SymbolUtilityServices.ValidateSymbolName(repaired, false); // ← 例外出たら catch
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    throw new Autodesk.AutoCAD.Runtime.Exception(
                        ErrorStatus.InvalidInput,
                        $"Invalid layer name after rename: '{newName}' ({ex.Message})");
                }

                // Rename 実施
                ltr.UpgradeOpen();
                ltr.Name = repaired;
            }

            tr.Commit();
            progress?.Invoke($"Renamed layers in source ({stem}).");
        }

        /// <summary>ソースのModelSpace全エンティティをターゲットへクローン。</summary>
        private static void CloneModelSpaceToTarget(Database srcDb, Database targetDb, Action<string>? progress)
        {
            // src: ModelSpace の全ID収集
            using var trSrc = srcDb.TransactionManager.StartTransaction();
            var btSrc = (BlockTable)trSrc.GetObject(srcDb.BlockTableId, OpenMode.ForRead);
            var msId = btSrc[BlockTableRecord.ModelSpace];
            var ms = (BlockTableRecord)trSrc.GetObject(msId, OpenMode.ForRead);

            var ids = new ObjectIdCollection();
            foreach (ObjectId id in ms) ids.Add(id);
            trSrc.Commit();

            // tgt: ModelSpaceへクローン
            using var trTgt = targetDb.TransactionManager.StartTransaction();
            var btTgt = (BlockTable)trTgt.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
            var msTgt = (BlockTableRecord)trTgt.GetObject(btTgt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var map = new IdMapping();
            srcDb.WblockCloneObjects(
                ids,
                msTgt.ObjectId,
                map,
                DuplicateRecordCloning.Replace, // ← Merge は存在しない
                deferTranslation: false);

            trTgt.Commit();

            progress?.Invoke("Cloned ModelSpace entities.");
        }

        /// <summary>未使用シンボルをパージ。</summary>
        private static void PurgeUnused(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var toPurge = new ObjectIdCollection();

            void Collect<T>(ObjectId tableId) where T : SymbolTable
            {
                var table = (T)tr.GetObject(tableId, OpenMode.ForRead);
                foreach (ObjectId id in table) toPurge.Add(id);
            }

            Collect<LayerTable>(db.LayerTableId);
            Collect<LinetypeTable>(db.LinetypeTableId);
            Collect<TextStyleTable>(db.TextStyleTableId);
            Collect<DimStyleTable>(db.DimStyleTableId);
            Collect<BlockTable>(db.BlockTableId);

            tr.Commit();

            db.Purge(toPurge);
            // 必要ならここで更に個別の不要Blockを削除、等を追加
        }

        // ---------- Helpers ----------

        private static bool MatchesAny(string s, Regex[] matchers)
        {
            for (int i = 0; i < matchers.Length; i++)
                if (matchers[i].IsMatch(s)) return true;
            return false;
        }

        private static Regex WildcardToRegex(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) pattern = "*";
            var esc = Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".");
            return new Regex("^" + esc + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static List<string> GetStringList(JsonObject? obj, string prop, bool defaultIfEmptyStar)
        {
            var list = new List<string>();
            if (obj != null && obj[prop] is JsonArray arr)
            {
                foreach (var n in arr)
                {
                    var s = n?.GetValue<string>() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
            if (defaultIfEmptyStar && list.Count == 0)
                list.Add("*");
            return list;
        }
    }
}
